using Lims.Core.Interfaces;
using Lims.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lims.Infrastructure;

/// <summary>DI registration for the infrastructure layer.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all LIMS infrastructure services.
    /// Uses <see cref="SqlTokenRevocationStore"/> by default so that JWT
    /// revocations (logouts) survive service restarts and multi-instance
    /// deployments. Pass <paramref name="useInMemoryRevocation"/> = <c>true</c>
    /// in tests to avoid a DB dependency.
    /// </summary>
    public static IServiceCollection AddLimsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryRevocation = false)
    {
        var connectionString = configuration.GetConnectionString("LimsDb")
            ?? throw new InvalidOperationException("Connection string 'LimsDb' is missing (appsettings.json).");

        services.AddSingleton<ISqlConnectionFactory>(_ => new SqlConnectionFactory(connectionString));
        services.AddScoped<ISampleRepository, SampleRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<ISqlAuditReader, SqlAuditReader>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        // SQL-backed revocation survives restarts / multi-instance deployments.
        // Tests pass useInMemoryRevocation=true to avoid needing a live DB.
        if (useInMemoryRevocation)
            services.AddSingleton<ITokenRevocationStore, InMemoryTokenRevocationStore>();
        else
            services.AddSingleton<ITokenRevocationStore, SqlTokenRevocationStore>();

        // JWT options are read lazily by the token service: the SOAP service and
        // the Windows Service reuse this layer without needing a Jwt section.
        services.AddSingleton<ITokenService>(new JwtTokenService(configuration.GetSection("Jwt")));
        return services;
    }
}