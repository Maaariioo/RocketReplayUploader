using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using RocketReplayUploader.Infrastructure.Config;

namespace RocketReplayUploader.Application.Services;

// Vigilante de las carpetas de replays (una por cuenta: Steam, Epic...).
// En el modo interfaz se enciende/apaga con el toggle de autosubida; en el
// modo servicio lo arranca HostedReplayWatcher. Hay un FileSystemWatcher por
// carpeta existente + un escaneo periódico: cubre los casos en los que el
// watcher se pierde eventos (carpetas en la nube, red, desbordes) y arranca
// la vigilancia de una carpeta que no existía cuando la app se inició.
public class ReplayWatcher : IDisposable
{
    private readonly AppConfig _config;
    private readonly ReplayProcessor _processor;
    private readonly ILogger<ReplayWatcher> _logger;

    // Carpetas configuradas (existan o no en este momento).
    private readonly List<ReplayFolder> _allFolders = new();

    // Un FileSystemWatcher por carpeta existente.
    private readonly List<(ReplayFolder Folder, FileSystemWatcher Watcher)> _watchers = new();
    private Timer? _scanTimer;

    // Evita procesar el mismo archivo dos veces si FileSystemWatcher
    // dispara varios eventos "Created" seguidos (algo habitual en Windows).
    private readonly ConcurrentDictionary<string, byte> _processing = new(StringComparer.OrdinalIgnoreCase);

    // Archivos ya conocidos (para el escaneo periódico: solo se tratan los nuevos).
    private readonly ConcurrentDictionary<string, byte> _seen = new(StringComparer.OrdinalIgnoreCase);

    // Nombre ya puesto por esta app (Jugador_Modo_Game_Fecha): si aparece un
    // archivo así es el resultado de nuestro propio renombrado, no un replay
    // nuevo que haya que subir otra vez (ballchasing ya lo tiene).
    private static readonly Regex RenamedPattern = new(
        @"^.+_\d+v\d+_Game_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.replay$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(30);

    // path del replay nuevo + nombre de jugador de la cuenta dueña de esa carpeta.
    public event Action<string, string?>? ReplayDetected;

    public bool IsRunning { get; private set; }

    public ReplayWatcher(AppConfig config, ReplayProcessor processor, ILogger<ReplayWatcher> logger)
    {
        _config = config;
        _processor = processor;
        _logger = logger;
    }

    public void Start()
    {
        if (IsRunning) return;

        var folders = _config.ReplayFolders
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .ToList();

        if (folders.Count == 0)
        {
            _logger.LogError("No hay carpetas de replays configuradas");
            return;
        }

        _allFolders.AddRange(folders);

        foreach (var folder in folders)
        {
            if (Directory.Exists(folder.Path))
            {
                StartWatching(folder);
            }
            else
            {
                _logger.LogInformation(
                    "La carpeta {Path} no existe todavía; se empezará a vigilar en cuanto aparezca", folder.Path);
            }
        }

        _scanTimer = new Timer(_ => ScanAll(), null, RescanInterval, RescanInterval);

        IsRunning = true;
        ReplayDetected?.Invoke(folders[0].Path, folders[0].PlayerName);
    }

    public void Stop()
    {
        _scanTimer?.Dispose();
        _scanTimer = null;

        foreach (var (_, watcher) in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        _allFolders.Clear();

        IsRunning = false;
        _logger.LogInformation("Vigilancia detenida.");
    }

    private void StartWatching(ReplayFolder folder)
    {
        var watcher = new FileSystemWatcher(folder.Path, "*.replay")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
        };

        watcher.Created += (s, e) => OnCreated(e.FullPath, folder.PlayerName);
        watcher.Error += (s, e) => _logger.LogError(e.GetException(), "Error en FileSystemWatcher");
        watcher.EnableRaisingEvents = true;
        _watchers.Add((folder, watcher));

        // Cebamos el escaneo con lo que ya hay en la carpeta: los replays
        // anteriores a este arranque NO se suben solos (decisión conservadora).
        SeedSeen(folder.Path);

        _logger.LogInformation("Vigilando {Path} en busca de nuevos replays...", folder.Path);
    }

    private bool IsWatched(string path) =>
        _watchers.Any(w => string.Equals(w.Folder.Path, path, StringComparison.OrdinalIgnoreCase));

    // Pase periódico: arranca la vigilancia de carpetas que acaban de aparecer
    // y busca replays nuevos en las que ya están vigiladas.
    private void ScanAll()
    {
        foreach (var folder in _allFolders)
        {
            if (!IsWatched(folder.Path) && Directory.Exists(folder.Path))
            {
                StartWatching(folder);
                _logger.LogInformation("La carpeta {Path} ya existe; empezando a vigilarla", folder.Path);
            }
        }

        foreach (var (folder, _) in _watchers.ToList())
        {
            ScanForNewReplays(folder.Path, folder.PlayerName);
        }
    }

    private async void OnCreated(string fullPath, string? playerName)
    {
        if (!_processing.TryAdd(fullPath, 0))
        {
            return; // ya se está procesando este archivo
        }

        try
        {
            _seen.TryAdd(fullPath, 0);
            if (!ShouldAutoProcess(fullPath))
            {
                return;
            }

            await Task.Delay(3000); // esperar a que termine de guardarse
            ReplayDetected?.Invoke(fullPath, playerName);
            await _processor.ProcessReplay(fullPath, playerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error manejando el evento Created para {Path}", fullPath);
        }
        finally
        {
            _processing.TryRemove(fullPath, out _);
        }
    }

    // Red de seguridad: cada 30 s mira si hay archivos que el watcher no llegó
    // a ver (OneDrive/red, watcher saturado, etc.) y los trata como nuevos.
    private void ScanForNewReplays(string path, string? playerName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

            foreach (var file in Directory.EnumerateFiles(path, "*.replay"))
            {
                if (!_seen.TryAdd(file, 0)) continue;

                // Si el archivo no existe al procesarlo (p. ej. se borró justo
                // ahora), ProcessReplay lo descarta solo.
                if (!ShouldAutoProcess(file)) continue;

                ReplayDetected?.Invoke(file, playerName);
                _logger.LogInformation("Escaneo periódico: replay nuevo {Path}", file);
                _ = _processor.ProcessReplay(file, playerName);
            }

            // Mantener _seen acotado: olvidar rutas que ya no existen.
            if (_seen.Count > 2000)
            {
                foreach (var seen in _seen.Keys)
                {
                    if (!File.Exists(seen))
                    {
                        _seen.TryRemove(seen, out _);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en el escaneo periódico de replays");
        }
    }

    // Un archivo con nuestro patrón de renombrado no puede ser un replay nuevo:
    // se sube por duplicado (409) y llena la cola de ruido.
    private bool ShouldAutoProcess(string path)
    {
        if (RenamedPattern.IsMatch(Path.GetFileName(path)))
        {
            _logger.LogDebug("Replay ya renombrado por la app, se omite la autosubida: {Path}", path);
            return false;
        }

        return true;
    }

    private void SeedSeen(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*.replay"))
            {
                _seen[file] = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo la carpeta para el escaneo inicial");
        }
    }

    public void Dispose() => Stop();
}
