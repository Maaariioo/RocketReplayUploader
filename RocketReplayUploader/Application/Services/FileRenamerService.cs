using RocketReplayUploader.Application.Models;
using RocketReplayUploader.Infrastructure.Replay;
using RocketReplayUploader.Infrastructure.Config;

public class FileRenamerService
{
    private readonly AppConfig _config;
    private readonly ILogger<FileRenamerService> _logger;

    public FileRenamerService(AppConfig config, ILogger<FileRenamerService> logger)
    {
        _config = config;
        _logger = logger;
    }

    // Camino "rápido": usa datos leídos directamente del .replay (sin depender
    // de la API ni de comparar nombres). Devuelve (nueva ruta, nombre sin
    // extensión) o null si el header no traía todo lo necesario.
    public (string Path, string Title)? RenameFromHeader(string path, ReplayHeaderInfo header, string? playerName = null)
    {
        if (BuildTitleFromHeader(header, File.GetLastWriteTime(path), playerName) is not string title)
        {
            return null;
        }

        // Si el archivo ya tiene el prefijo Jugador_Modo_Game_ (lo puso una
        // subida anterior), no lo tocamos: re-subir no debe ir cambiando el
        // nombre cada vez. El título es el nombre actual sin extensión.
        var prefix = title[..(title.Length - 19)];
        var stem = Path.GetFileNameWithoutExtension(path);
        if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return (path, stem);
        }

        return DoRename(path, title);
    }

    // Solo calcula el título (Jugador_Modo_Game_Fecha) a partir del header,
    // sin tocar el archivo. Lo usa la interfaz para poner el título al subir.
    public string? BuildTitleFromHeader(ReplayHeaderInfo header, DateTime? when = null, string? playerName = null)
    {
        if (header.PrimaryPlayerTeam is not int myTeam ||
            header.Team0Score is not int team0 ||
            header.Team1Score is not int team1)
        {
            return null;
        }

        var myGoals = myTeam == 0 ? team0 : team1;
        var enemyGoals = myTeam == 0 ? team1 : team0;

        string mode = header.TeamSize switch
        {
            1 => "1v1",
            2 => "2v2",
            3 => "3v3",
            4 => "4v4",
            _ => "Other"
        };

        // Preferencia: el nombre de jugador de la cuenta dueña de la carpeta.
        // Si no hay ninguno configurado, usamos el que diga el propio .replay
        // (cubre el caso de dos cuentas en la misma máquina sin configurar).
        var effective = playerName;
        if (string.IsNullOrWhiteSpace(effective) && !string.IsNullOrWhiteSpace(header.PlayerName))
        {
            effective = header.PlayerName;
        }
        if (string.IsNullOrWhiteSpace(effective))
        {
            effective = string.IsNullOrWhiteSpace(_config.PlayerName) ? "Player" : _config.PlayerName;
        }

        var timestamp = (when ?? DateTime.Now).ToString("yyyy-MM-dd_HH-mm-ss");
        return $"{effective}_{mode}_Game_{timestamp}";
    }

    // Camino de respaldo: usa los datos ya procesados por ballchasing
    // (se usa solo si RenameFromHeader no pudo leer el .replay localmente).
    public (string Path, string Title)? Rename(string path, ReplayMetadata meta, string? playerName = null)
    {
        var effective = playerName;
        if (string.IsNullOrWhiteSpace(effective))
        {
            effective = _config.PlayerName;
        }

        if (string.IsNullOrWhiteSpace(effective))
        {
            _logger.LogWarning("PlayerName no está configurado, no se puede renombrar {Path}", path);
            return null;
        }

        bool? isBlue = meta.Blue?.Players?.Any(p => p.Name == effective);

        if (isBlue is null)
        {
            _logger.LogWarning(
                "No se encontró a '{PlayerName}' entre los jugadores del replay {Path}. " +
                "Revisa que coincida EXACTO (mayúsculas incluidas) con el nombre que muestra ballchasing.",
                effective, path);
            return null;
        }

        var myTeam = isBlue.Value ? meta.Blue : meta.Orange;
        var enemyTeam = isBlue.Value ? meta.Orange : meta.Blue;

        string mode = meta.Playlist switch
        {
            "ranked-duels" or "unranked-duels" => "1v1",
            "ranked-doubles" or "unranked-doubles" => "2v2",
            "ranked-standard" or "unranked-standard" => "3v3",
            _ => "Other"
        };

        var title = $"{effective}_{mode}_Game_{File.GetLastWriteTime(path):yyyy-MM-dd_HH-mm-ss}";

        // Igual que en RenameFromHeader: si ya tiene el prefijo, no lo tocamos.
        var prefix = title[..(title.Length - 19)];
        var stem = Path.GetFileNameWithoutExtension(path);
        if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return (path, stem);
        }

        return DoRename(path, title);
    }

    private (string Path, string Title)? DoRename(string path, string title)
    {
        var directory = Path.GetDirectoryName(path);

        if (directory == null)
        {
            _logger.LogError("No se pudo determinar el directorio de {Path}", path);
            return null;
        }

        string newName = $"{title}.replay";
        var newPath = Path.Combine(directory, newName);

        // Ya tiene el nombre exacto (p. ej. al re-subir un replay ya renombrado):
        // no lo tocamos para no "rebotar" el nombre con cada subida.
        if (string.Equals(newPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return (path, title);
        }

        // Dos replays guardados en el mismo segundo acabarían con el mismo
        // nombre: no pisar el que ya existe, desambiguar con un sufijo.
        var candidate = newPath;
        for (var i = 2; File.Exists(candidate); i++)
        {
            candidate = Path.Combine(directory, $"{title} ({i}).replay");
        }

        File.Move(path, candidate, false);
        _logger.LogInformation("Renombrado {Old} -> {New}", path, candidate);

        return (candidate, title);
    }
}
