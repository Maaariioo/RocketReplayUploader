using System.Windows;
using RocketReplayUploader.Infrastructure.Localization;
using RocketReplayUploader.Infrastructure.UI;

namespace RocketReplayUploader.Views;

public partial class GroupDialogWindow : Window
{
    public string GroupName { get; private set; } = "";
    public string PlayerIdentification { get; private set; } = "by-id";
    public string TeamIdentification { get; private set; } = "by-distinct-players";

    private readonly int _replayCount;
    private string? _statusKey;
    private object?[] _statusArgs = Array.Empty<object?>();

    public GroupDialogWindow(int replayCount)
    {
        _replayCount = replayCount;
        InitializeComponent();
        UpdateIntro();

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this, _configThemeIsLight());
        Loaded += (_, _) => TxtName.Focus();
        Closed += (_, _) => TranslationSource.Instance.CultureChanged -= ApplyCulture;
        TranslationSource.Instance.CultureChanged += ApplyCulture;
    }

    private void ApplyCulture()
    {
        UpdateIntro();
        if (_statusKey != null)
        {
            TxtStatus.Text = TranslationSource.Instance.Format(_statusKey, _statusArgs);
        }
    }

    private void UpdateIntro()
    {
        TxtIntro.Text = TranslationSource.Instance.Format("Group.DialogIntro", _replayCount);
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        TxtStatus.Text = TranslationSource.Instance.Format(key, args);
    }

    private static bool _configThemeIsLight()
    {
        try
        {
            return Infrastructure.Config.ConfigStore.Load()?.Theme == "light";
        }
        catch
        {
            return false;
        }
    }

    private void BtnCreate_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetStatus("Group.NameRequired");
            TxtName.Focus();
            return;
        }

        GroupName = name;
        PlayerIdentification = CboPlayerId.SelectedIndex == 1 ? "by-name" : "by-id";
        TeamIdentification = CboTeamId.SelectedIndex == 1 ? "by-player-clusters" : "by-distinct-players";

        DialogResult = true;
    }
}