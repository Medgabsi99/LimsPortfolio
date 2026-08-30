using Lims.Infrastructure;
using Lims.WindowsService;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLimsInfrastructure(builder.Configuration, useInMemoryRevocation: true);
// WindowsService does not use JWT; InMemory revocation avoids DB dependency for that feature.
builder.Services.Configure<InstrumentImportOptions>(builder.Configuration.GetSection("InstrumentImport"));
builder.Services.AddHostedService<InstrumentImportWorker>();

// Runs as a Windows Service when launched by the SCM,
// as a plain console app during development (dotnet run).
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "LimsInstrumentImport";
});

var host = builder.Build();
host.Run();