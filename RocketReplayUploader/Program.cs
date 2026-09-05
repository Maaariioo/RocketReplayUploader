using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RocketReplayUploader.Application.Services;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Localization;
using RocketReplayUploader.Infrastructure.Startup;
using RocketReplayUploader.Infrastructure.UI;

namespace RocketReplayUploader;

public static class Program
{
    public static string ExecutablePath { get; private set; } = "";
    public static string[] StartupArgs { get; private set; } = Array.Empty<string>();

    [STAThread]
    public static int Main(string[] args)
    {
        StartupArgs = args;
        ExecutablePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];

        // --- Comandos de instalación (no arrancan la app) ---
        if (args.Contains("--install"))
        {
            StartupInstaller.InstallLogonTask(ExecutablePath);
            return 0;
        }
        if (args.Contains("--uninstall"))
        {
            StartupInstaller.UninstallLogonTask();
            return 0;
        }
        if (args.Contains("--install-service"))
        {
            StartupInstaller.InstallWindowsService(ExecutablePath);
            return 0;
        }
        if (args.Contains("--uninstall-service"))
        {
            StartupInstaller.UninstallWindowsService();
            return 0;
        }

        // --- Modo servicio: sin interfaz, solo el watcher (Windows Service o --service) ---
        if (!Environment.UserInteractive || args.Contains("--service"))
        {
            var config = ConfigStore.Load();
            TranslationSource.Instance.Language =
                string.IsNullOrWhiteSpace(config?.Language) ? "en" : config!.Language;
            if (config == null)
            {
                Console.WriteLine(TranslationSource.Instance["Prog.MissingConfig"]);
                return 1;
            }

            var host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options => options.ServiceName = "RocketReplayUploader")
                .ConfigureServices(services =>
                {
                    services.AddReplayServices(config);
                    services.AddHostedService<HostedReplayWatcher>();
                })
                .Build();

            host.Run();
            return 0;
        }

        // --- Modo interfaz: ventana de gestión + bandeja del sistema ---
        // Una sola instancia por usuario: si ya hay otra corriendo, se le pide
        // que enseñe su ventana y esta segunda instancia sale.
        var user = Environment.UserName;
        if (!SingleInstance.TryAcquire(user))
        {
            SingleInstance.NotifyExisting(user);
            return 0;
        }

        var showSignal = SingleInstance.CreateShowSignal(user);
        try
        {
            var app = new App(showSignal);
            app.InitializeComponent();
            return app.Run();
        }
        finally
        {
            showSignal?.Dispose();
            SingleInstance.Release();
        }
    }
}
