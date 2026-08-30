using System.Net.Http.Json;
using Lims.Core.Interfaces;
using Lims.Core.Services;
using Lims.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lims.Tests.Integration;

/// <summary>
/// Boots the real REST API pipeline (routing, JWT bearer, role authorization,
/// rate limiter, controllers) with the SQL repositories replaced by in-memory
/// fakes - no LocalDB required, CI friendly.
/// </summary>
public sealed class LimsApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-test-signing-key-0123456789abcdef0123456789abcdef";

    public FakeUserRepository Users { get; } = null!;
    public FakeSampleRepository Samples { get; } = new();

    public LimsApiFactory()
    {
        // fake user store needs the real hasher to produce compatible hashes
        Users = new FakeUserRepository(new PasswordHasher());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("ConnectionStrings:LimsDb", "Server=unused;Database=unused;");
        builder.UseSetting("RateLimit:AuthPermitLimit", "1000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISampleRepository>();
            services.AddSingleton<ISampleRepository>(Samples);
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(Users);

            // AuthController now depends on IAuditRepository, not ISampleRepository.
            services.RemoveAll<IAuditRepository>();
            services.AddSingleton<IAuditRepository>(new FakeAuditRepository());

            // Use in-memory revocation so tests never touch the DB.
            services.RemoveAll<ITokenRevocationStore>();
            services.AddSingleton<ITokenRevocationStore, InMemoryTokenRevocationStore>();
        });
    }

    /// <summary>Signs in via the real POST /api/auth/login endpoint and returns the JWT.</summary>
    public async Task<string> LoginAsync(string username, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        return payload!["token"].GetString()!;
    }

    /// <summary>HttpClient with the given JWT pre-attached.</summary>
    public HttpClient CreateClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}