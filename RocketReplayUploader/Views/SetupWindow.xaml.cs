using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RocketReplayUploader.Application.Services;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Localization;
using RocketReplayUploader.Infrastructure.Startup;
using RocketReplayUploader.Infrastructure.UI;

namespace RocketReplayUploader.Views;

public partial class SetupWindow : Window
{
    private readonly AppConfig? _existing;
    private bool _keyVisible;
    private bool _loading;
    private string? _statusKey;
    private object?[] _statusArgs = Array.Empty<object?>();

    public SetupWindow(AppConfig? existing)
    {
        _loading = true;
        InitializeComponent();
        _loading = false;

        _existing = existing;

        CboLanguage.SelectedValue = TranslationSource.Instance.Language;

        Closed += (_, _) => TranslationSource.Instance.CultureChanged -= ApplyCulture;
        TranslationSource.Instance.CultureChanged += ApplyCulture;

        SourceInitialized += (_, _) =>
            DarkTitleBar.Apply(this, _existing?.Theme != "light");

        // Cada carpeta configurada va a su fila: DemosEpic es de Epic, el resto
        // (Demos y cualquier otra) va a Steam.
        if (existing?.ReplayFolders is { Count: > 0 })
        {
            foreach (var folder in existing.ReplayFolders)
            {
                if (folder.Path.EndsWith("DemosEpic", StringComparison.OrdinalIgnoreCase))
                {
                    TxtEpicPath.Text = folder.Path;
                    TxtEpicPlayer.Text = folder.PlayerName;
                }
                else
                {
                    TxtSteamPath.Text = folder.Path;
                    TxtSteamPlayer.Text = folder.PlayerName;
                }
            }
        }
        else
        {
            // Sin carpetas configuradas: detectarlas automáticamente.
            FillDetectedFolders();
        }

        TxtKey.Password = existing?.BallchasingApiKey ?? "";

        switch (existing?.Visibility)
        {
            case "public":
                RadPublic.IsChecked = true;
                break;
            case "private":
                RadPrivate.IsChecked = true;
                break;
            default:
                RadUnlisted.IsChecked = true;
                break;
        }

        CboAfterUpload.SelectedIndex = existing?.AfterUploadAction switch
        {
            "recycle" => 1,
            "archive" => 2,
            _ => 0
        };

        ChkAutostart.IsChecked = AutostartInstalled();
    }

    private void ApplyCulture()
    {
        if (_statusKey != null)
        {
            TxtStatus.Text = TranslationSource.Instance.Format(_statusKey, _statusArgs);
        }
    }

    // El selector de idioma: el cambio se aplica al instante (los bindings del
    // XAML se refrescan solos al dispararse PropertyChanged en TranslationSource).
    private void CboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CboLanguage.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            TranslationSource.Instance.Language = lang;
        }
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        TxtStatus.Text = TranslationSource.Instance.Format(key, args);
    }

    // Ojo: alterna entre ocultar (puntos) y mostrar la API key en claro
    // intercambiando el PasswordBox por un TextBox visible.
    private void BtnToggleKey_Click(object sender, RoutedEventArgs e)
    {
        _keyVisible = !_keyVisible;

        if (_keyVisible)
        {
            TxtKeyVisible.Text = TxtKey.Password;
            TxtKey.Visibility = Visibility.Collapsed;
            TxtKeyVisible.Visibility = Visibility.Visible;
            TxtKeyVisible.Focus();
            TxtKeyVisible.CaretIndex = TxtKeyVisible.Text.Length;
        }
        else
        {
            TxtKey.Password = TxtKeyVisible.Text;
            TxtKeyVisible.Visibility = Visibility.Collapsed;
            TxtKey.Visibility = Visibility.Visible;
            TxtKey.Focus();
        }

        TxtToggleGlyph.Text = _keyVisible ? "\uE89F" : "\uE890";
    }

    // Rellena las dos cajas con los directorios estándar: Demos -> Steam,
    // DemosEpic -> Epic (existan o no; la app los vigila cuando aparezcan).
    // No toca los nombres de jugador que ya estén escritos.
    private void FillDetectedFolders()
    {
        foreach (var path in ReplayPathLocator.FindStandardFolders())
        {
            if (path.EndsWith("DemosEpic", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(TxtEpicPath.Text)) TxtEpicPath.Text = path;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(TxtSteamPath.Text)) TxtSteamPath.Text = path;
            }
        }

        var anyExists = Directory.Exists(TxtSteamPath.Text) || Directory.Exists(TxtEpicPath.Text);
        SetStatus(anyExists ? "Setup.StatusDetected" : "Setup.StatusNotYet");
    }

    private void BtnDetect_Click(object sender, RoutedEventArgs e) => FillDetectedFolders();

    private void BtnBrowseSteam_Click(object sender, RoutedEventArgs e) => BrowseInto(TxtSteamPath);

    private void BtnBrowseEpic_Click(object sender, RoutedEventArgs e) => BrowseInto(TxtEpicPath);

    private void BrowseInto(TextBox target)
    {
        var dialog = new OpenFolderDialog
        {
            Title = TranslationSource.Instance["Setup.BrowseTitle"],
            InitialDirectory = Directory.Exists(target.Text) ? target.Text : null
        };

        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FolderName;
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var folders = new List<ReplayFolder>();

        if (!AddRow(TxtSteamPath, TxtSteamPlayer, "Steam", folders)) return;
        if (!AddRow(TxtEpicPath, TxtEpicPlayer, "Epic", folders)) return;

        if (folders.Count == 0)
        {
            ShowError(TranslationSource.Instance["Setup.ErrorNoFolder"]);
            return;
        }

        var key = TxtKey.Password.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowError(TranslationSource.Instance["Setup.ErrorNoKey"]);
            return;
        }

        SetStatus("Setup.CheckingKey");
        SetBusy(true);

        var (ok, steamName) = await ValidateApiKey(key);
        if (!ok)
        {
            SetStatus("Setup.KeyInvalid");
            SetBusy(false);
            return;
        }

        var visibility = RadPrivate.IsChecked == true
            ? "private"
            : RadUnlisted.IsChecked == true ? "unlisted" : "public";

        var afterUpload = CboAfterUpload.SelectedIndex switch
        {
            1 => "recycle",
            2 => "archive",
            _ => "none"
        };

        var language = (CboLanguage.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            ?? TranslationSource.Instance.Language;

        var config = new AppConfig
        {
            ReplayFolders = folders,
            BallchasingApiKey = key,
            Visibility = visibility,
            AfterUploadAction = afterUpload,
            Language = language
        };

        ConfigStore.Save(config);

        if (ChkAutostart.IsChecked == true)
        {
            StartupInstaller.InstallLogonTask(Program.ExecutablePath, showConfirmation: false);
        }
        else
        {
            StartupInstaller.UninstallLogonTask(showConfirmation: false);
        }

        MessageBox.Show(
            this,
            TranslationSource.Instance.Format("Setup.SavedMessage", steamName),
            "RocketReplayUploader",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        DialogResult = true;
    }

    // Añade una fila rellena a la lista de carpetas a guardar. Devuelve false
    // si la fila está a medias (ruta sin nombre de jugador). La carpeta puede
    // no existir todavía: la app la vigilará en cuanto aparezca.
    private bool AddRow(TextBox pathBox, TextBox playerBox, string label, List<ReplayFolder> folders)
    {
        var path = pathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path)) return true; // fila sin usar

        var player = playerBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(player))
        {
            ShowError(TranslationSource.Instance.Format("Setup.ErrorNoPlayer", label));
            return false;
        }

        if (!Directory.Exists(path))
        {
            SetStatus("Setup.WarnFolderMissing", label);
        }

        folders.Add(new ReplayFolder { Path = path, PlayerName = player });
        return true;
    }

    private void SetBusy(bool busy)
    {
        BtnSave.IsEnabled = !busy;
        TxtSteamPath.IsEnabled = !busy;
        TxtSteamPlayer.IsEnabled = !busy;
        TxtEpicPath.IsEnabled = !busy;
        TxtEpicPlayer.IsEnabled = !busy;
        TxtKey.IsEnabled = !busy;
        ChkAutostart.IsEnabled = !busy;
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, TranslationSource.Instance["Setup.ErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static async Task<(bool Ok, string? Name)> ValidateApiKey(string apiKey)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", apiKey);

            var res = await http.GetAsync("https://ballchasing.com/api/");
            if (!res.IsSuccessStatusCode) return (false, null);

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : "?";

            return (true, name);
        }
        catch
        {
            return (false, null);
        }
    }

    private static bool AutostartInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("RocketReplayUploader") is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}