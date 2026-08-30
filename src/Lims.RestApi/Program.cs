using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Lims.Core.Interfaces;
using Lims.Infrastructure;
using Lims.RestApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Services & DI ---------------------------------------------------------
// Configure model validation failure response to match the domain validator
// shape: { "errors": ["field: message", ...] } — consistent across all 400s.
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(o =>
    {
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var errors = ctx.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(x =>
                    string.IsNullOrEmpty(e.Key) ? x.ErrorMessage : $"{e.Key}: {x.ErrorMessage}"))
                .ToList();
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { errors });
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();   // liveness probe for load balancers / monitoring

// ---- HTTPS / HSTS -----------------------------------------------------------
// HSTS header (Strict-Transport-Security) is only sent in Production to avoid
// breaking local HTTP development. Max-age and preload are configured via
// Hsts:* keys in appsettings.Production.json.
builder.Services.AddHsts(options =>
{
    var cfg = builder.Configuration.GetSection("Hsts");
    options.MaxAge           = cfg.GetValue("MaxAge", TimeSpan.FromDays(365));
    options.IncludeSubDomains = cfg.GetValue("IncludeSubDomains", true);
    options.Preload           = cfg.GetValue("Preload", true);
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LIMS API",
        Version = "v1",
        Description = "Laboratory Information Management System - sample lifecycle, " +
                      "test results and instrument management. Consumed by the lab " +
                      "front-end, the SSIS middleware and third-party ERP systems."
    });

    // JWT bearer support: click "Authorize" in Swagger UI and paste the token
    // returned by POST /api/auth/login.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT from POST /api/auth/login. Paste the token only (no 'Bearer ' prefix)."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddLimsInfrastructure(builder.Configuration, useInMemoryRevocation: false);

// ---- Authentication : JWT bearer (see POST /api/auth/login) ----------------
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"] ?? "Lims.RestApi",
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"] ?? "Lims.Clients",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                jwtSection["SigningKey"]
                    ?? throw new InvalidOperationException(
                        "Jwt:SigningKey is missing. Set it via user-secrets (dev) " +
                        "or the Jwt__SigningKey environment variable (prod)."))),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // Server-side revocation on every request:
        //   1. jti present in the revocation store (logout)
        //   2. account still active
        //   3. account TokenVersion matches the token's "ver" claim
        //      (password change/reset/deactivation revokes all prior tokens)
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (principal is null) { context.Fail("No principal."); return; }

                var revocation = context.HttpContext.RequestServices
                    .GetRequiredService<ITokenRevocationStore>();
                var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
                if (jti is not null && revocation.IsRevoked(jti))
                {
                    context.Fail("Token has been revoked (logout).");
                    return;
                }

                var users = context.HttpContext.RequestServices
                    .GetRequiredService<IUserRepository>();
                var userIdClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                                  ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdClaim, out var userId))
                {
                    context.Fail("Token has no user id.");
                    return;
                }

                var account = await users.GetByIdAsync(userId, context.HttpContext.RequestAborted);
                if (account is null || !account.IsActive)
                {
                    context.Fail("Account is disabled or no longer exists.");
                    return;
                }

                var ver = principal.FindFirstValue("ver");
                if (!int.TryParse(ver, out var tokenVersion) || tokenVersion != account.TokenVersion)
                {
                    context.Fail("Token was issued before a password change or account update.");
                }
            }
        };
    });
builder.Services.AddAuthorization();

// ---- Rate limiting : brute-force protection on the auth endpoints ----------
// Fixed window per client IP; the auth endpoints opt in via [EnableRateLimiting].
// Permit limit is configurable (integration tests raise it; default 10/min).
var authPermitLimit = builder.Configuration.GetValue("RateLimit:AuthPermitLimit", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.OnRejected = static (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many attempts. Try again in a minute." }, context.HttpContext.RequestAborted));
    };
});

// Production hardening: consistent 500 responses without stack traces
builder.Services.AddProblemDetails();

var app = builder.Build();

// ---- Middleware pipeline ---------------------------------------------------
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Web dashboard (wwwroot/index.html) - the lab front-end
app.UseDefaultFiles();
app.UseStaticFiles();

// Swagger is enabled in all environments: this LIMS API is its own
// documentation for lab IT teams and integrating third parties.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "LIMS API";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LIMS API v1");
});

// HSTS: only meaningful over HTTPS and outside of development
if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();

// Security headers on every response — defence in depth
app.Use(static async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"]  = "nosniff";
    ctx.Response.Headers["X-Frame-Options"]          = "DENY";
    ctx.Response.Headers["Referrer-Policy"]          = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    await next();
});

app.UseRateLimiter();      // ← must run before authentication so brute-force
                            //   attempts are rejected before any DB I/O
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposes the implicit Program class to WebApplicationFactory (integration tests).
public partial class Program { }