using FileBackup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>();
    })
    .ConfigureAppConfiguration(app =>
    {
        app.AddJsonFile("appsettings.json");
    })
    .Build();

host.Run();