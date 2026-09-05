using System.Diagnostics;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RocketReplayUploader.Application.Services;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Localization;
using RocketReplayUploader.Infrastructure.Logging;
using RocketReplayUploader.Infrastructure.Startup;
using RocketReplayUploader.Infrastructure.UI;
using RocketReplayUploader.ViewModels;
using RocketReplayUploader.Views;
using WpfApplication = System.Windows.Application;

namespace RocketReplayUploader;

public partial class App : WpfApplication
{
    private ServiceProvider? _services;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private ReplayWatcher? _watcher;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _trayOpenItem;
    private Forms.ToolStripMenuItem? _trayViewLastItem;
    private Forms.ToolStripMenuItem? _trayAutoUploadItem;
    private Forms.ToolStripMenuItem? _trayExitItem;
    private Icon? _icon;
    private bool _isExiting;
    private DateTime _lastBalloonAt = DateTime.MinValue;

    // Señal de "muestra tu ventana" que llega cuando el usuario lanza el .exe
    // otra vez mientras esta instancia ya está corriendo.
    private readonly EventWaitHandle? _showSignal;

    public bool IsExiting => _isExiting;

    public App(EventWaitHandle? showSignal = null)
    {
        _showSignal = showSignal;
        if (showSignal != null)
        {
            WatchShowSignal();
        }
    }

    private void WatchShowSignal()
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    _showSignal!.WaitOne();
                    _showSignal.Reset();
                    Dispatcher.BeginInvoke(ShowMainWindow);
                }
            }
            catch
            {
                // la app se está cerrando
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var config = ConfigStore.Load();

        // El idioma y el tema se aplican ANTES de abrir cualquier ventana
        // (incluido el setup), para que ya salgan en el idioma guardado.
        if (!string.IsNullOrWhiteSpace(config?.Language))
        {
            TranslationSource.Instance.Language = config!.Language;
        }
        ThemeManager.Apply(config?.Theme ?? "dark");

        if (config == null || Program.StartupArgs.Contains("--setup"))
        {
            var setup = new SetupWindow(config);
            if (setup.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
            config = ConfigStore.Load()!;
        }

        _services = new ServiceCollection()
            .AddLogging(builder => builder.AddDebug().AddFile())
            .AddReplayServices(config)
            .AddSingleton<MainViewModel>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();

        _watcher = _services.GetRequiredService<ReplayWatcher>();
        _viewModel = _services.GetRequiredService<MainViewModel>();
        _mainWindow = _services.GetRequiredService<MainWindow>();

        _mainWindow.Show();
        _mainWindow.SetDarkTitleBar(_viewModel.IsDarkMode);
        CreateTrayIcon();

        if (Program.StartupArgs.Contains("--minimized"))
        {
            // Arranque con Windows: quedarse en la bandeja sin molestar.
            _mainWindow.Hide();
            ShowTrayBalloon(
                TranslationSource.Instance["Tray.MinimizedTitle"],
                TranslationSource.Instance["Tray.MinimizedMsg"]);
        }
    }

    private void CreateTrayIcon()
    {
        _icon = LoadAppIcon();

        _tray = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Rocket Replay Uploader",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        _trayOpenItem = new Forms.ToolStripMenuItem(
            TranslationSource.Instance["Tray.Open"], null, (_, _) => ShowMainWindow());
        menu.Items.Add(_trayOpenItem);
        _trayViewLastItem = new Forms.ToolStripMenuItem(
            TranslationSource.Instance["Tray.ViewLast"], null, (_, _) => OpenBallchasing(_viewModel!.LastUploadUrl))
        {
            Enabled = false
        };
        menu.Items.Add(_trayViewLastItem);
        _trayAutoUploadItem = new Forms.ToolStripMenuItem(
            TranslationSource.Instance["Tray.AutoOn"], null, (_, _) => _viewModel!.ToggleAutoUpload());
        menu.Items.Add(_trayAutoUploadItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _trayExitItem = new Forms.ToolStripMenuItem(
            TranslationSource.Instance["Tray.Exit"], null, (_, _) => ExitApp());
        menu.Items.Add(_trayExitItem);

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMainWindow();

        // Notificaciones de subida/error y actualización del menú de la bandeja.
        _viewModel!.Notification += (title, message) =>
            ShowTrayBalloon(title, message, Forms.ToolTipIcon.Info);

        _viewModel!.PropertyChanged += (_, e2) =>
        {
            if (e2.PropertyName == nameof(MainViewModel.IsAutoUpload) && _trayAutoUploadItem != null)
            {
                _trayAutoUploadItem.Text = _viewModel.IsAutoUpload
                    ? TranslationSource.Instance["Tray.AutoOn"]
                    : TranslationSource.Instance["Tray.AutoOff"];
            }
            else if (e2.PropertyName == nameof(MainViewModel.LastUploadUrl) && _trayViewLastItem != null)
            {
                _trayViewLastItem.Enabled = !string.IsNullOrEmpty(_viewModel.LastUploadUrl);
            }
            else if (e2.PropertyName == nameof(MainViewModel.IsDarkMode))
            {
                _mainWindow?.SetDarkTitleBar(_viewModel.IsDarkMode);
            }
        };

        // Al cambiar de idioma se re-traducen las entradas del menú de la bandeja.
        TranslationSource.Instance.CultureChanged += UpdateTrayItems;
    }

    private void UpdateTrayItems()
    {
        var t = TranslationSource.Instance;
        if (_trayOpenItem != null) _trayOpenItem.Text = t["Tray.Open"];
        if (_trayViewLastItem != null) _trayViewLastItem.Text = t["Tray.ViewLast"];
        if (_trayAutoUploadItem != null)
        {
            _trayAutoUploadItem.Text = (_viewModel?.IsAutoUpload ?? false)
                ? t["Tray.AutoOn"]
                : t["Tray.AutoOff"];
        }
        if (_trayExitItem != null) _trayExitItem.Text = t["Tray.Exit"];
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    public void ShowTrayBalloon(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        if (_tray == null) return;

        // Windows encola los globos del tray: si acaban de entrar muchos a la
        // vez (subidas masivas), no abrumar con uno por replay.
        if (DateTime.Now - _lastBalloonAt < TimeSpan.FromSeconds(6)) return;
        _lastBalloonAt = DateTime.Now;

        _tray.ShowBalloonTip(5000, title, message, icon);
    }

    private static void OpenBallchasing(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // sin logger aquí; el fallo no es grave
        }
    }

    private void ExitApp()
    {
        _isExiting = true;
        _mainWindow?.Close();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TranslationSource.Instance.CultureChanged -= UpdateTrayItems;
        _watcher?.Stop();
        _tray?.Dispose();
        _icon?.Dispose();
        _services?.Dispose();
        _showSignal?.Dispose();
        base.OnExit(e);
    }

    private static Icon LoadAppIcon()
    {
        using var stream = WpfApplication.GetResourceStream(
            new Uri("pack://application:,,,/Assets/app.ico"))?.Stream
            ?? throw new InvalidOperationException("Falta el recurso Assets/app.ico");
        return new Icon(stream);
    }
}