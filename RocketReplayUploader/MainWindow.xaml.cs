using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RocketReplayUploader.Application.Models;
using RocketReplayUploader.Infrastructure.Localization;
using RocketReplayUploader.Infrastructure.UI;
using RocketReplayUploader.ViewModels;

namespace RocketReplayUploader;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => EnsureVisible();
    }

    // Si Windows restaura la ventana fuera de pantalla (p. ej. un monitor se
    // desconectó) o la deja minimizada al arrancar, la traemos de vuelta a un
    // monitor visible. Solo se salta esto con --minimized (bandeja).
    private void EnsureVisible()
    {
        if (!Program.StartupArgs.Contains("--minimized") && WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (System.Windows.Forms.Screen.AllScreens.Any(s =>
                s.WorkingArea.IntersectsWith(new System.Drawing.Rectangle(
                    (int)Left, (int)Top, (int)ActualWidth, (int)ActualHeight))))
        {
            return;
        }

        var target = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        Left = target.Left + (target.Width - ActualWidth) / 2;
        Top = target.Top + (target.Height - ActualHeight) / 2;
    }

    public void SetDarkTitleBar(bool dark)
    {
        DarkTitleBar.Apply(this, dark);
    }

    // Doble clic en una fila -> abrir el Explorador con el archivo seleccionado.
    private void ReplaysGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ReplaysGrid.SelectedItem is ReplayItem item)
        {
            (DataContext as MainViewModel)?.OpenInExplorer(item);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        if (!app.IsExiting)
        {
            e.Cancel = true;
            Hide();
            app.ShowTrayBalloon(
                TranslationSource.Instance["Tray.ClosingTitle"],
                TranslationSource.Instance["Tray.ClosingMsg"]);
        }
    }
}
