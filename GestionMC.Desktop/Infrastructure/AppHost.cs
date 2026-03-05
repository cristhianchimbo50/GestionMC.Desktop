using Microsoft.Extensions.DependencyInjection;
using GestionMC.Desktop.Configuration;
using GestionMC.Desktop.Services;
using GestionMC.Desktop.ViewModels;
using System;
using System.IO;

namespace GestionMC.Desktop.Infrastructure;

public static class AppHost
{
    private static ServiceProvider? _provider;

    public static ServiceProvider Services => _provider ?? throw new InvalidOperationException("AppHost not initialized");

    public static void Initialize()
    {
        if (_provider != null) return;

        var services = new ServiceCollection();

        var settings = AppSettingsLoader.Load();
        services.AddSingleton(settings);

        services.AddSingleton<IStoragePathProvider, StoragePathProvider>();

        services.AddTransient<ISriRecibidosService, SriRecibidosService>();
        services.AddTransient<IOracleProveedorService>(sp => new OracleProveedorService(BuildConnectionString(settings)));
        services.AddTransient<IOracleFacturaPagoService>(sp => new OracleFacturaPagoService(BuildConnectionString(settings)));
        services.AddTransient<IOracleColorService>(sp => new OracleColorService(BuildConnectionString(settings)));

        services.AddTransient<Func<ISriSessionService>>(sp => () =>
        {
            var storage = sp.GetRequiredService<IStoragePathProvider>().GetStoragePath();
            return new SriSessionService(storage);
        });

        services.AddSingleton(sp =>
        {
            var file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GestionMC",
                "disabled_providers.xml");
            return new DisabledProvidersStore(file);
        });

        services.AddSingleton<MainViewModel>();

        _provider = services.BuildServiceProvider();
    }

    private static string BuildConnectionString(AppSettings settings)
    {
        var oracle = settings.Oracle;
        return $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={oracle.Host})(PORT={oracle.Port}))(CONNECT_DATA=(SERVICE_NAME={oracle.ServiceName})));User Id={oracle.UserId};Password={oracle.Password};";
    }

    public static void Dispose()
    {
        _provider?.Dispose();
        _provider = null;
    }
}
