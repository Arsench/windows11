using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Zenith.App.Services;
using Zenith.App.ViewModels;
using Zenith.App.Views;
using Zenith.Core.Abstractions;
using Zenith.Core.Monitoring;
using Zenith.Core.Safety;
using Zenith.Platform.Windows;

namespace Zenith.App;

public partial class App : Application
{
    private IHost? _host;

    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zenith");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ConfigureLogging();

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddZenithWindowsPlatform();

                    services.AddSingleton<ThemeService>();
                    services.AddSingleton<DialogService>();
                    services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());

                    services.AddSingleton<DashboardViewModel>();
                    services.AddSingleton<SystemViewModel>();
                    services.AddSingleton<StorageViewModel>();
                    services.AddSingleton<DuplicatesViewModel>();
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<ShellViewModel>();

                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync().ConfigureAwait(true);

            HookGlobalExceptionHandlers();

            var settings = _host.Services.GetRequiredService<ISettingsStore>();
            await settings.LoadAsync().ConfigureAwait(true);

            _host.Services.GetRequiredService<PathSafetyGuard>()
                .SetUserExclusions(settings.Current.ExcludedPaths);

            _host.Services.GetRequiredService<ThemeService>().Apply(settings.Current.Theme);

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            // El muestreo arranca después de mostrar la ventana: la app aparece
            // al instante y los datos entran en cuanto están listos.
            await _host.Services.GetRequiredService<MonitoringService>().StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fallo al iniciar Zenith");
            MessageBox.Show(
                "Zenith no ha podido iniciarse. Encontrarás el detalle técnico en el registro de la aplicación.",
                "Zenith", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void ConfigureLogging()
    {
        var logFolder = Path.Combine(DataFolder, "logs");
        Directory.CreateDirectory(logFolder);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(logFolder, "zenith-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .CreateLogger();
    }

    /// <summary>
    /// Nada de pantallas con trazas de excepción: el usuario ve una frase clara
    /// y el detalle queda en el registro para poder depurarlo.
    /// </summary>
    private void HookGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Excepción no controlada en la interfaz");
            args.Handled = true;
            NotifyQuietly("Algo no ha ido bien. La aplicación sigue funcionando; el detalle está en el registro.");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Excepción no controlada en el dominio");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Excepción no observada en una tarea");
            args.SetObserved();
        };
    }

    private void NotifyQuietly(string message)
    {
        try
        {
            _host?.Services.GetRequiredService<DialogService>().Notify(message, ToastKind.Error);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido mostrar el aviso al usuario");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                await _host.Services.GetRequiredService<MonitoringService>().DisposeAsync().ConfigureAwait(false);
                await _host.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error durante el cierre");
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
