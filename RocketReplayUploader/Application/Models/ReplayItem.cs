using System.ComponentModel;
using System.Runtime.CompilerServices;
using RocketReplayUploader.Infrastructure.Localization;
using RocketReplayUploader.Infrastructure.Replay;

namespace RocketReplayUploader.Application.Models;

public enum ReplayStatusKind
{
    Pending,
    Busy,
    Ok,
    Error
}

// Claves de los estados traducibles de un replay (ver Resources/Strings.*.resx).
public static class StatusKeys
{
    public const string Pending = "St.Pending";
    public const string Queued = "St.Queued";
    public const string Uploading = "St.Uploading";
    public const string Renamed = "St.Renamed";
    public const string Uploaded = "St.Uploaded";
    public const string AlreadyUploaded = "St.AlreadyUploaded";
    public const string AddedToGroup = "St.AddedToGroup";
    public const string GroupAddError = "St.GroupAddError";
    public const string RenameFailed = "St.RenameFailed";
    public const string RenameError = "St.RenameError";
    public const string ErrorWith = "St.ErrorWith";
}

public class ReplayItem : INotifyPropertyChanged, IDisposable
{
    public string Path { get; private set; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Size { get; }
    public string Modified { get; }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    // Marca del checkbox para subir varios replays de una vez.
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    // El estado se guarda como clave traducible + argumentos: así el texto (y
    // el estado derivado StatusKind) se refresca solo al cambiar de idioma.
    private string _statusKey = StatusKeys.Pending;
    private object?[] _statusArgs = Array.Empty<object?>();
    public string Status => TranslationSource.Instance.Format(_statusKey, _statusArgs);

    private string _tooltipText = "";
    public string TooltipText
    {
        get => _tooltipText;
        private set
        {
            if (_tooltipText != value)
            {
                _tooltipText = value;
                OnPropertyChanged();
            }
        }
    }

    // URL de ballchasing.com del replay subido (null hasta que se sube).
    // Habilita el botón "Ver" de la fila.
    private string? _ballchasingUrl;
    public string? BallchasingUrl
    {
        get => _ballchasingUrl;
        set
        {
            if (_ballchasingUrl != value)
            {
                _ballchasingUrl = value;
                OnPropertyChanged();
            }
        }
    }

    // Id del replay en ballchasing.com (null hasta que se sube). Se usa para
    // asignar el replay a un grupo sin tener que subirlo otra vez.
    private string? _ballchasingId;
    public string? BallchasingId
    {
        get => _ballchasingId;
        set
        {
            if (_ballchasingId != value)
            {
                _ballchasingId = value;
                OnPropertyChanged();
            }
        }
    }

    private ReplayStatusKind _statusKind = ReplayStatusKind.Pending;
    public ReplayStatusKind StatusKind
    {
        get => _statusKind;
        private set
        {
            if (_statusKind != value)
            {
                _statusKind = value;
                OnPropertyChanged();
            }
        }
    }

    private ReplayHeaderInfo? _tooltipHeader;

    public ReplayItem(string path)
    {
        Path = path;
        var info = new FileInfo(path);
        Size = FormatSize(info.Length);
        Modified = info.LastWriteTime.ToString("dd/MM/yyyy HH:mm");

        // Al cambiar de idioma se re-traduce el estado y el tooltip al vuelo.
        TranslationSource.Instance.CultureChanged += OnCultureChanged;
    }

    // Cambia el estado mostrado a partir de una clave traducible (ver StatusKeys).
    public void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        OnPropertyChanged(nameof(Status));
        UpdateStatusKind();
    }

    public void RenameTo(string newPath)
    {
        Path = newPath;
        OnPropertyChanged(nameof(Name));
    }

    // Tooltip con la ruta + lo que se puede sacar del header del .replay.
    public void SetTooltip(string fullPath, ReplayHeaderInfo? header)
    {
        _tooltipHeader = header;
        var t = TranslationSource.Instance;

        if (header == null)
        {
            TooltipText = fullPath;
            return;
        }

        var parts = new List<string> { fullPath };
        if (header.MapName != null)
        {
            parts.Add(t.Format("Tip.Map", header.MapName));
        }
        if (header.TeamSize is int size)
        {
            parts.Add(t.Format("Tip.Mode", size, size));
        }
        if (header.Team0Score is int s0 && header.Team1Score is int s1)
        {
            parts.Add(t.Format("Tip.Score", s0, s1));
        }
        if (header.MatchType != null)
        {
            parts.Add(t.Format("Tip.MatchType", header.MatchType));
        }

        TooltipText = string.Join("\n", parts);
    }

    private void OnCultureChanged()
    {
        OnPropertyChanged(nameof(Status));
        SetTooltip(Path, _tooltipHeader);
    }

    private void UpdateStatusKind()
    {
        StatusKind = _statusKey switch
        {
            StatusKeys.Uploaded or StatusKeys.AlreadyUploaded or StatusKeys.Renamed or StatusKeys.AddedToGroup => ReplayStatusKind.Ok,
            StatusKeys.Queued or StatusKeys.Uploading => ReplayStatusKind.Busy,
            StatusKeys.ErrorWith or StatusKeys.RenameFailed or StatusKeys.RenameError or StatusKeys.GroupAddError => ReplayStatusKind.Error,
            _ => ReplayStatusKind.Pending
        };
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / 1024.0 / 1024.0:F1} MB"
    };

    public void Dispose()
    {
        TranslationSource.Instance.CultureChanged -= OnCultureChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}