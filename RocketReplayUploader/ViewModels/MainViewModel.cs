using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using RocketReplayUploader.Application.Models;
using RocketReplayUploader.Application.Services;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Http;
using RocketReplayUploader.Infrastructure.IO;
using RocketReplayUploader.Infrastructure.Localization;
using RocketReplayUploader.Infrastructure.Replay;
using RocketReplayUploader.Infrastructure.UI;
using RocketReplayUploader.Views;

namespace RocketReplayUploader.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AppConfig _config;
    private readonly ReplayWatcher _watcher;
    private readonly ReplayProcessor _processor;
    private readonly FileRenamerService _renamer;
    private readonly BallchasingService _ballchasing;
    private readonly UploadQueueService _queue;
    private readonly ILogger<MainViewModel> _logger;

    private readonly ConcurrentDictionary<string, ReplayItem> _byPath = new();

    // Seguimiento de las operaciones en masa ("Subir todo" / "Renombrar todo").
    private HashSet<string>? _massPaths;
    private bool _massIsRename;
    private int _massTotal;
    private int _massDone;
    private int _massOk;
    private int _massFail;

    public ObservableCollection<ReplayItem> Replays { get; } = new();

    private bool _isAutoUpload;
    public bool IsAutoUpload
    {
        get => _isAutoUpload;
        private set
        {
            if (_isAutoUpload != value)
            {
                _isAutoUpload = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _noReplays = true;
    public bool NoReplays
    {
        get => _noReplays;
        private set
        {
            if (_noReplays != value)
            {
                _noReplays = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasReplays));
            }
        }
    }

    public bool HasReplays => !NoReplays;

    private bool _folderInvalid;
    public bool FolderInvalid
    {
        get => _folderInvalid;
        private set
        {
            if (_folderInvalid != value)
            {
                _folderInvalid = value;
                OnPropertyChanged();
            }
        }
    }

    private string _replayCountText = "";
    public string ReplayCountText
    {
        get => _replayCountText;
        private set
        {
            if (_replayCountText != value)
            {
                _replayCountText = value;
                OnPropertyChanged();
            }
        }
    }

    // Texto extra en el header durante operaciones en masa (vacío = oculto).
    private string _progressText = "";
    public string ProgressText
    {
        get => _progressText;
        private set
        {
            if (_progressText != value)
            {
                _progressText = value;
                OnPropertyChanged();
            }
        }
    }

    // Estadísticas acumuladas (subidos · volumen), vacío hasta la primera subida.
    private string _statsText = "";
    public string StatsText
    {
        get => _statsText;
        private set
        {
            if (_statsText != value)
            {
                _statsText = value;
                OnPropertyChanged();
            }
        }
    }

    // Última subida con éxito: URL para abrirla en ballchasing.com (lo usa la
    // bandeja: "Ver último subido"). Vacío si aún no se subió nada.
    private string? _lastUploadUrl;
    public string? LastUploadUrl
    {
        get => _lastUploadUrl;
        private set
        {
            if (_lastUploadUrl != value)
            {
                _lastUploadUrl = value;
                OnPropertyChanged();
            }
        }
    }

    // Notificaciones de escritorio (subidas y errores definitivos).
    public event Action<string, string>? Notification;

    private bool _isWorking;
    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (_isWorking != value)
            {
                _isWorking = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanMassAction));
                CanCreateGroup = !value && HasSelection;
            }
        }
    }

    public bool CanMassAction => !IsWorking;

    public bool IsDarkMode
    {
        get => _config.Theme != "light";
        set
        {
            var theme = value ? "dark" : "light";
            if (_config.Theme == theme) return;

            _config.Theme = theme;
            ThemeManager.Apply(theme);
            ConfigStore.Save(_config);
            OnPropertyChanged();
        }
    }

    private string _replayFolderDisplay = "";
    public string ReplayFolderDisplay
    {
        get => _replayFolderDisplay;
        private set
        {
            if (_replayFolderDisplay != value)
            {
                _replayFolderDisplay = value;
                OnPropertyChanged();
            }
        }
    }

    // Texto del pie: se guarda como clave + argumentos para re-traducirlo si el
    // usuario cambia de idioma en caliente.
    private string? _statusKey;
    private object?[] _statusArgs = Array.Empty<object?>();
    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    private string? _progressKey;
    private object?[] _progressArgs = Array.Empty<object?>();

    private string _selectedLanguage = "en";
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? "en" : value;
            if (_selectedLanguage == value) return;
            _selectedLanguage = value;
            OnPropertyChanged();

            if (_config.Language != value)
            {
                _config.Language = value;
                ConfigStore.Save(_config);
            }

            // Dispara CultureChanged (los textos pendientes se re-traducen solos).
            TranslationSource.Instance.Language = value;
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand SetupCommand { get; }
    public ICommand ToggleAutoUploadCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand UploadCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand UploadAllCommand { get; }
    public ICommand UploadSelectedCommand { get; }
    public ICommand RenameAllCommand { get; }
    public ICommand CreateGroupCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenBallchasingCommand { get; }

    // Visibilidad para las subidas MANUALES (botón de fila, seleccionados y
    // "Subir todo"). La de la configuración solo aplica a la autosubida.
    private string _selectedVisibility = "unlisted";
    public string SelectedVisibility
    {
        get => _selectedVisibility;
        set
        {
            if (_selectedVisibility != value)
            {
                _selectedVisibility = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _hasSelection;
    public bool HasSelection
    {
        get => _hasSelection;
        private set
        {
            if (_hasSelection != value)
            {
                _hasSelection = value;
                OnPropertyChanged();
            }
        }
    }

    private string _selectedCountText = "";
    public string SelectedCountText
    {
        get => _selectedCountText;
        private set
        {
            if (_selectedCountText != value)
            {
                _selectedCountText = value;
                OnPropertyChanged();
            }
        }
    }

    // Permite crear un grupo con los replays marcados cuando hay selección y no
    // hay una operación en masa en marcha.
    private bool _canCreateGroup;
    public bool CanCreateGroup
    {
        get => _canCreateGroup;
        private set
        {
            if (_canCreateGroup != value)
            {
                _canCreateGroup = value;
                OnPropertyChanged();
            }
        }
    }

    public MainViewModel(
        AppConfig config,
        ReplayWatcher watcher,
        ReplayProcessor processor,
        FileRenamerService renamer,
        BallchasingService ballchasing,
        UploadQueueService queue,
        ILogger<MainViewModel> logger)
    {
        _config = config;
        _watcher = watcher;
        _processor = processor;
        _renamer = renamer;
        _ballchasing = ballchasing;
        _queue = queue;
        _logger = logger;

        RefreshCommand = new RelayCommand(_ => RefreshList());
        SetupCommand = new RelayCommand(_ => OpenSetup());
        ToggleAutoUploadCommand = new RelayCommand(_ => ToggleAutoUpload());
        RenameCommand = new RelayCommand(p => Rename(p as ReplayItem));
        UploadCommand = new RelayCommand(p => Upload(p as ReplayItem));
        DeleteCommand = new RelayCommand(p => Delete(p as ReplayItem));
        UploadAllCommand = new RelayCommand(_ => RunUploadAll());
        UploadSelectedCommand = new RelayCommand(_ => RunUploadSelected());
        RenameAllCommand = new RelayCommand(_ => _ = RunRenameAllAsync());
        CreateGroupCommand = new RelayCommand(_ => _ = RunCreateGroupAsync(), _ => CanCreateGroup);
        OpenLogsCommand = new RelayCommand(_ => OpenLogs());
        OpenBallchasingCommand = new RelayCommand(p => OpenBallchasing(p as ReplayItem));

        _watcher.ReplayDetected += (_, _) => Dispatch(() =>
        {
            RefreshList();
            SetStatus(IsAutoUpload ? "Status.NewReplayAuto" : "Status.NewReplayManual");
        });

        _processor.Progress += p => Dispatch(() => HandleProgress(p));
        _queue.Progress += p => Dispatch(() => HandleProgress(p));

        Replays.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null)
            {
                foreach (ReplayItem item in e.OldItems)
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                    item.Dispose();
                }
            }

            if (e.NewItems != null)
            {
                foreach (ReplayItem item in e.NewItems)
                {
                    item.PropertyChanged += OnItemPropertyChanged;
                }
            }

            UpdateSelectionInfo();
        };

        TranslationSource.Instance.CultureChanged += OnCultureChanged;

        SelectedVisibility = string.IsNullOrWhiteSpace(_config.Visibility) ? "unlisted" : _config.Visibility;
        _selectedLanguage = _config.Language is "es" or "fr" ? _config.Language : "en";
        StatsText = BuildStatsText();
        RefreshList();

        _watcher.Start();
        IsAutoUpload = _watcher.IsRunning;
        _queue.Resume();
    }

    public void ToggleAutoUpload()
    {
        if (IsAutoUpload)
        {
            _watcher.Stop();
            IsAutoUpload = false;
            SetStatus("Status.AutoOff");
        }
        else
        {
            _watcher.Start();
            if (_watcher.IsRunning)
            {
                IsAutoUpload = true;
                SetStatus("Status.AutoOn", FoldersDisplay);
                RefreshList();
            }
            else
            {
                SetStatus("Status.AutoFail");
            }
        }
    }

    private void Rename(ReplayItem? item)
    {
        if (item == null || item.IsBusy) return;

        item.IsBusy = true;
        try
        {
            var header = ReplayHeaderParser.TryParse(item.Path);
            var result = header != null
                ? _renamer.RenameFromHeader(item.Path, header, _config.GetPlayerNameFor(item.Path))
                : null;

            if (result == null)
            {
                item.SetStatus(StatusKeys.RenameFailed);
                _logger.LogWarning("No se pudo renombrar {Path} manualmente", item.Path);
                return;
            }

            _byPath.TryRemove(item.Path, out _);
            item.RenameTo(result.Value.Path);
            _byPath[item.Path] = item;
            // Si el archivo estaba en cola de subida, la cola debe seguir la ruta nueva.
            _queue.OnFileRenamed(item.Path, result.Value.Path);
            item.SetStatus(StatusKeys.Renamed);
            SetStatus("Status.RenamedOf", item.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renombrando {Path}", item.Path);
            item.SetStatus(StatusKeys.RenameError);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    // La subida real la hace la cola (con reintentos y persistencia).
    private void Upload(ReplayItem? item)
    {
        if (item == null || item.IsBusy) return;

        item.IsBusy = true;
        item.SetStatus(StatusKeys.Queued);
        _queue.Enqueue(item.Path, SelectedVisibility);
    }

    // Sube todos los replays marcados con el checkbox, con la visibilidad elegida.
    private void RunUploadSelected()
    {
        var items = Replays.Where(i => i.IsSelected).ToList();
        if (items.Count == 0 || IsWorking) return;

        _massPaths = new HashSet<string>(items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        _massIsRename = false;
        _massTotal = items.Count;
        _massDone = 0;
        _massOk = 0;
        _massFail = 0;

        IsWorking = true;
        SetProgress("Prog.Queued", 0, _massTotal);
        _logger.LogInformation("Subir seleccionados: {Count} replays encolados (visibilidad {Visibility})", items.Count, SelectedVisibility);

        var enqueued = 0;
        foreach (var item in items)
        {
            item.IsSelected = false;
            item.IsBusy = true;
            item.SetStatus(StatusKeys.Queued);
            _queue.Enqueue(item.Path, SelectedVisibility);
            enqueued++;
            SetProgress("Prog.Queued", enqueued, _massTotal);
        }

        UpdateSelectionInfo();
    }

    private void Delete(ReplayItem? item)
    {
        if (item == null || item.IsBusy) return;

        var confirm = MessageBox.Show(
            Lf("Del.Confirm", item.Name),
            L("Del.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes) return;

        if (!RecycleBin.TryDelete(item.Path, out var error))
        {
            _logger.LogError("No se pudo mover a la Papelera {Path}: {Error}", item.Path, error);
            MessageBox.Show(
                Lf("Del.Error", error),
                L("Del.ErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        Replays.Remove(item);
        _byPath.TryRemove(item.Path, out _);
        NoReplays = Replays.Count == 0;
        SetReplayCountText();
        SetStatus("Status.RecycledOf", item.Name);
    }

    // Doble clic en una fila -> abre el Explorador con el archivo seleccionado.
    public void OpenInExplorer(ReplayItem? item)
    {
        if (item == null || !File.Exists(item.Path)) return;

        try
        {
            Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo abrir {Path} en el Explorador", item.Path);
        }
    }

    private void OpenLogs()
    {
        var dir = Infrastructure.Logging.FileLoggerProvider.LogDirectory;
        Directory.CreateDirectory(dir);
        try
        {
            Process.Start("explorer.exe", dir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo abrir la carpeta de logs");
        }
    }

    // Botón "Ver" de la fila: abre el replay subido en ballchasing.com.
    private void OpenBallchasing(ReplayItem? item)
    {
        if (item?.BallchasingUrl == null) return;

        try
        {
            Process.Start(new ProcessStartInfo(item.BallchasingUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo abrir {Url}", item.BallchasingUrl);
        }
    }

    private string BuildStatsText()
    {
        if (_config.TotalUploads <= 0) return "";

        var bytes = _config.TotalUploadedBytes;
        var size = bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB"
            : bytes >= 1024 * 1024
                ? $"{bytes / 1024.0 / 1024.0:F1} MB"
                : $"{bytes / 1024.0:F1} KB";

        return Lf("Stats.Text", _config.TotalUploads, size);
    }

    private void RunUploadAll()
    {
        var items = Replays.ToList();
        if (items.Count == 0 || IsWorking) return;

        _massPaths = new HashSet<string>(items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        _massIsRename = false;
        _massTotal = items.Count;
        _massDone = 0;
        _massOk = 0;
        _massFail = 0;

        IsWorking = true;
        SetProgress("Prog.Queued", 0, _massTotal);
        _logger.LogInformation("Subir todo: {Count} replays encolados", items.Count);

        var enqueued = 0;
        foreach (var item in items)
        {
            item.IsBusy = true;
            item.SetStatus(StatusKeys.Queued);
            _queue.Enqueue(item.Path, SelectedVisibility);
            enqueued++;
            SetProgress("Prog.Queued", enqueued, _massTotal);
        }
    }

    private async Task RunRenameAllAsync()
    {
        var items = Replays.ToList();
        if (items.Count == 0 || IsWorking) return;

        _massPaths = null; // el renombrado lo terminamos aquí mismo, no vía eventos
        _massIsRename = true;
        _massTotal = items.Count;
        _massDone = 0;
        _massOk = 0;
        _massFail = 0;

        IsWorking = true;
        _logger.LogInformation("Renombrar todo: {Count} replays", items.Count);

        foreach (var item in items)
        {
            await Task.Run(() => Rename(item));
            _massDone++;

            var ok = item.StatusKind != ReplayStatusKind.Error;
            if (ok) _massOk++; else _massFail++;

            if (_massIsRename)
            {
                SetProgress(_massFail == 0 ? "Prog.Renamed" : "Prog.RenamedFail", _massOk, _massTotal, _massFail);
            }
        }

        IsWorking = false;
        ClearProgress();
        SetStatus(_massFail == 0 ? "Status.MassRenameOk" : "Status.MassRenameFail", _massOk, _massFail);
    }

    // Crea un grupo en ballchasing.com con los replays marcados. Los que ya
    // estén subidos se añaden directo por su id; los que no, se suben primero.
    private async Task RunCreateGroupAsync()
    {
        var items = Replays.Where(i => i.IsSelected).ToList();
        if (items.Count == 0 || IsWorking) return;

        var dialog = new GroupDialogWindow(items.Count)
        {
            Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        };
        if (dialog.ShowDialog() != true) return;

        // Creamos el grupo ANTES de subir nada, para no perder trabajo a mitad.
        string groupId;
        try
        {
            SetStatus("Group.Creating", dialog.GroupName);
            (groupId, _) = await Task.Run(() =>
                _ballchasing.CreateGroup(
                    dialog.GroupName,
                    dialog.PlayerIdentification,
                    dialog.TeamIdentification));
        }
        catch (UploadPermanentException ex)
        {
            _logger.LogError(ex, "No se pudo crear el grupo '{Name}'", dialog.GroupName);
            MessageBox.Show(
                ex.Message,
                L("Group.FailTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        catch (UploadTransientException ex)
        {
            _logger.LogWarning(ex, "Error transitorio creando el grupo '{Name}'", dialog.GroupName);
            MessageBox.Show(
                L("Group.Transient"),
                L("Group.FailTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado creando el grupo '{Name}'", dialog.GroupName);
            MessageBox.Show(
                L("Group.Unexpected"),
                L("Group.FailTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // Marcar los seleccionados y procesarlos.
        IsWorking = true;
        CanCreateGroup = false;

        var ok = 0;
        var fail = 0;
        var total = items.Count;

        foreach (var item in items)
        {
            item.IsSelected = false;
            item.IsBusy = true;
            SetProgress("Prog.Group", ok + fail + 1, total);

            try
            {
                var replayId = await GetOrUploadReplayIdAsync(item);
                if (string.IsNullOrWhiteSpace(replayId))
                {
                    fail++;
                    item.SetStatus(StatusKeys.GroupAddError);
                    _logger.LogWarning("No se pudo obtener id para añadir {Path} al grupo", item.Path);
                }
                else
                {
                    var assigned = await Task.Run(() => _ballchasing.AssignReplayToGroup(replayId, groupId));
                    if (assigned)
                    {
                        item.BallchasingId = replayId;
                        item.SetStatus(StatusKeys.AddedToGroup);
                        ok++;
                    }
                    else
                    {
                        fail++;
                        item.SetStatus(StatusKeys.GroupAddError);
                        _logger.LogWarning("Ballchasing rechazó añadir {ReplayId} al grupo {GroupId}", replayId, groupId);
                    }
                }
            }
            catch (Exception ex)
            {
                fail++;
                item.SetStatus(StatusKeys.GroupAddError);
                _logger.LogError(ex, "Error añadiendo {Path} al grupo {GroupId}", item.Path, groupId);
            }
            finally
            {
                item.IsBusy = false;
                SetProgress(fail > 0 ? "Prog.GroupFail" : "Prog.Group", ok, total, fail);
            }
        }

        UpdateSelectionInfo();
        IsWorking = false;
        ClearProgress();

        SetStatus(fail == 0 ? "Group.DoneOk" : "Group.DoneFail", dialog.GroupName, ok, fail);

        Notification?.Invoke(
            L(fail == 0 ? "Group.OkTitle" : "Group.ErrTitle"),
            Lf(fail == 0 ? "Group.NotifyOk" : "Group.NotifyFail", dialog.GroupName, ok, fail));
    }

    // Devuelve el id en ballchasing del replay, subiéndolo si hace falta.
    private async Task<string?> GetOrUploadReplayIdAsync(ReplayItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.BallchasingId)) return item.BallchasingId;

        var (id, _) = await Task.Run(() => _ballchasing.UploadReplay(item.Path, SelectedVisibility));
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        item.BallchasingId = id;
        if (item.BallchasingUrl == null)
        {
            item.BallchasingUrl = "https://ballchasing.com/replay/" + id;
        }
        return id;
    }

    private void OpenSetup()
    {
        var window = new SetupWindow(ConfigStore.Load());
        if (window.ShowDialog() != true) return;

        var fresh = ConfigStore.Load()!;
        ApplyConfig(fresh);

        var wasRunning = _watcher.IsRunning;
        _watcher.Stop();
        RefreshList();
        if (wasRunning) _watcher.Start();
        IsAutoUpload = _watcher.IsRunning;
    }

    private void ApplyConfig(AppConfig fresh)
    {
        _config.ReplayFolders = fresh.ReplayFolders;
        _config.BallchasingApiKey = fresh.BallchasingApiKey;
        _config.Visibility = fresh.Visibility;
        _config.AfterUploadAction = fresh.AfterUploadAction;
        _config.TotalUploads = fresh.TotalUploads;
        _config.TotalUploadedBytes = fresh.TotalUploadedBytes;
        _config.LastUploadAt = fresh.LastUploadAt;

        if (_config.Language != fresh.Language)
        {
            _config.Language = fresh.Language;
            TranslationSource.Instance.Language = fresh.Language;
        }

        StatsText = BuildStatsText();
    }

    private string FoldersDisplay =>
        string.Join(" · ", _config.ReplayFolders.Select(f => f.Path).Where(p => !string.IsNullOrWhiteSpace(p)));

    private void RefreshList()
    {
        Replays.Clear();
        _byPath.Clear();

        var folders = _config.ReplayFolders
            .Where(f => !string.IsNullOrWhiteSpace(f.Path) && Directory.Exists(f.Path))
            .ToList();

        if (folders.Count == 0)
        {
            ReplayFolderDisplay = FoldersDisplay;
            NoReplays = true;
            FolderInvalid = true;
            SetReplayCountText();
            SetStatus("Status.NoFolders",
                string.IsNullOrWhiteSpace(FoldersDisplay) ? L("Status.Empty") : FoldersDisplay);
            return;
        }

        ReplayFolderDisplay = FoldersDisplay;
        FolderInvalid = false;

        var files = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(folder.Path, "*.replay"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leyendo la carpeta {Path}", folder.Path);
            }
        }

        foreach (var file in files
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(File.GetLastWriteTime))
        {
            var item = new ReplayItem(file);
            item.SetTooltip(file, ReplayHeaderParser.TryParse(file));
            Replays.Add(item);
            _byPath[file] = item;
        }

        NoReplays = Replays.Count == 0;
        SetReplayCountText();
        SetStatus("Status.InFolder", Replays.Count, ReplayFolderDisplay);
    }

    private void SetReplayCountText()
    {
        ReplayCountText = Replays.Count switch
        {
            0 => L("Cnt.Zero"),
            1 => L("Cnt.One"),
            _ => Lf("Cnt.Many", Replays.Count)
        };
    }

    private void HandleProgress(ReplayProgress p)
    {
        if (!_byPath.TryGetValue(p.Path, out var item)) return;

        switch (p.Status)
        {
            case "renamed" when p.NewPath != null:
                _byPath.TryRemove(p.Path, out _);
                item.RenameTo(p.NewPath);
                _byPath[p.NewPath] = item;
                item.SetStatus(StatusKeys.Renamed);
                break;
            case "queued":
                item.SetStatus(StatusKeys.Queued);
                item.IsBusy = true;
                break;
            case "uploaded":
                item.SetStatus(p.AlreadyExisted ? StatusKeys.AlreadyUploaded : StatusKeys.Uploaded);
                item.IsBusy = false;
                if (p.Message != null)
                {
                    item.BallchasingId = p.Message;
                    var url = "https://ballchasing.com/replay/" + p.Message;
                    item.BallchasingUrl = url;
                    LastUploadUrl = url;
                }
                StatsText = BuildStatsText();
                TrackMassProgress(p.Path, ok: true);
                if (p.Notify)
                {
                    Notification?.Invoke(
                        L(p.AlreadyExisted ? "Notify.AlreadyExisted" : "Notify.Uploaded"),
                        Lf("Notify.UploadedMsg", item.Name));
                }
                break;
            case "removed":
                // El archivo salió de la carpeta (Papelera o Archivados) tras subir.
                Replays.Remove(item);
                _byPath.TryRemove(p.Path, out _);
                NoReplays = Replays.Count == 0;
                SetReplayCountText();
                break;
            case "error":
                item.SetStatus(StatusKeys.ErrorWith, p.Message);
                item.IsBusy = false;
                TrackMassProgress(p.Path, ok: false);
                if (p.Notify)
                {
                    Notification?.Invoke(L("Notify.ErrorTitle"), Lf("Notify.ErrorMsg", item.Name, p.Message));
                }
                break;
        }
    }

    private void TrackMassProgress(string path, bool ok)
    {
        if (_massPaths == null || !_massPaths.Remove(path)) return;

        if (ok) _massOk++; else _massFail++;
        _massDone++;

        SetProgress(_massFail == 0 ? "Prog.Uploaded" : "Prog.UploadedFail", _massOk, _massTotal, _massFail);

        if (_massDone >= _massTotal)
        {
            _massPaths = null;
            IsWorking = false;
            ClearProgress();
            SetStatus(_massFail == 0 ? "Status.MassUploadOk" : "Status.MassUploadFail", _massOk, _massFail);
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReplayItem.IsSelected))
        {
            UpdateSelectionInfo();
        }
    }

    private void UpdateSelectionInfo()
    {
        var count = Replays.Count(i => i.IsSelected);
        HasSelection = count > 0;
        CanCreateGroup = count > 0 && !IsWorking;
        SelectedCountText = count == 0 ? "" : Lf("Sel.UploadSelected", count);
        CommandManager.InvalidateRequerySuggested();
    }

    // Al cambiar de idioma se re-traducen los textos pendientes del pie/header.
    private void OnCultureChanged()
    {
        if (_statusKey != null)
        {
            StatusText = Lf(_statusKey, _statusArgs);
        }
        if (_progressKey != null)
        {
            ProgressText = Lf(_progressKey, _progressArgs);
        }
        SetReplayCountText();
        StatsText = BuildStatsText();
        UpdateSelectionInfo();
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText = Lf(key, args);
    }

    private void SetProgress(string key, params object?[] args)
    {
        _progressKey = key;
        _progressArgs = args;
        ProgressText = Lf(key, args);
    }

    private void ClearProgress()
    {
        _progressKey = null;
        _progressArgs = Array.Empty<object?>();
        ProgressText = "";
    }

    private void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    // Accesos cortos a los textos localizados.
    private static string L(string key) => TranslationSource.Instance[key];
    private static string Lf(string key, params object?[] args) => TranslationSource.Instance.Format(key, args);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}