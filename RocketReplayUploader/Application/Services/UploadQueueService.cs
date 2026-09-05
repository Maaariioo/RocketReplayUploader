using System.Text.Json;
using RocketReplayUploader.Application.Models;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Http;
using RocketReplayUploader.Infrastructure.IO;
using RocketReplayUploader.Infrastructure.Localization;
using RocketReplayUploader.Infrastructure.Replay;

// Cola de subidas PERSISTENTE: cada replay que hay que subir se guarda en
// %AppData%\RocketReplayUploader\queue.json antes de procesarlo. Si no hay
// conexión o ballchasing falla, se reintenta con backoff exponencial y el
// trabajo nunca se pierde (ni siquiera al cerrar la app).
public class UploadQueueService : IDisposable
{
    private static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 8;

    private readonly IBallchasingService _ballchasing;
    private readonly FileRenamerService _renamer;
    private readonly AppConfig _config;
    private readonly ILogger<UploadQueueService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string _queuePath;

    // Notifica avances para que la interfaz actualice el estado de cada replay.
    public event Action<ReplayProgress>? Progress;

    // Ruta -> visibilidad con la que hay que subirla (la configuración es solo
    // el valor por defecto de la autosubida; la manual puede ser otra).
    private readonly Dictionary<string, string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _retryAfter = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;

    // El "queueFilePath" y el "timeProvider" son inyectables para los tests
    // (evitar tocar %AppData% real y no esperar los backoffs de verdad).
    public UploadQueueService(
        IBallchasingService ballchasing,
        FileRenamerService renamer,
        AppConfig config,
        ILogger<UploadQueueService> logger,
        TimeProvider? timeProvider = null,
        string? queueFilePath = null)
    {
        _ballchasing = ballchasing;
        _renamer = renamer;
        _config = config;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _queuePath = queueFilePath ?? DefaultQueuePath;
        Load();
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public void Enqueue(string path, string? visibility = null)
    {
        var full = Path.GetFullPath(path);
        bool startWorker;
        lock (_gate)
        {
            startWorker = !_pending.ContainsKey(full);
            if (startWorker)
            {
                _pending[full] = visibility ?? _config.Visibility;
                SaveLocked();
            }
        }

        _logger.LogInformation("Añadido a la cola de subida: {Path}", full);
        if (startWorker)
        {
            EnsureWorker();
        }
    }

    public void EnqueueMany(IEnumerable<string> paths, string? visibility = null)
    {
        bool any = false;
        lock (_gate)
        {
            foreach (var path in paths)
            {
                var full = Path.GetFullPath(path);
                if (_pending.TryAdd(full, visibility ?? _config.Visibility))
                {
                    any = true;
                }
            }
            if (any)
            {
                SaveLocked();
            }
        }

        if (any)
        {
            EnsureWorker();
        }
    }

    // Reanuda el procesamiento de lo que quedara pendiente de otras sesiones.
    public void Resume()
    {
        EnsureWorker();
    }

    // La interfaz renombró un archivo a mano mientras estaba pendiente de
    // subida: la cola debe seguir la ruta nueva, o el siguiente pase del bucle
    // descartaría el replay como "ya no existe" sin subirlo nunca.
    public void OnFileRenamed(string oldPath, string newPath)
    {
        var oldFull = Path.GetFullPath(oldPath);
        var newFull = Path.GetFullPath(newPath);
        lock (_gate)
        {
            if (_pending.Remove(oldFull, out var vis))
            {
                _pending[newFull] = vis;
                _retryAfter.Remove(oldFull);
                SaveLocked();
                _logger.LogInformation("Cola actualizada tras renombrar: {Old} -> {New}", oldFull, newFull);
            }
        }
    }

    private void EnsureWorker()
    {
        lock (_gate)
        {
            if (_worker != null && !_worker.IsCompleted)
            {
                return;
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _worker = Task.Run(() => RunLoopAsync(token));
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? next = null;
            string? nextVisibility = null;
            var now = _timeProvider.GetLocalNow().DateTime;

            lock (_gate)
            {
                foreach (var (path, visibility) in _pending.ToList())
                {
                    if (!File.Exists(path))
                    {
                        // el archivo ya no existe (borrado fuera de la app): lo sacamos.
                        // Ojo: si lo que pasó es que se RENOMBRÓ, ProcessOnceAsync ya
                        // actualiza la clave de la cola ANTES de la subida, así que aquí
                        // solo aterrizan los borrados reales.
                        _logger.LogInformation("El replay pendiente ya no existe, se descarta de la cola: {Path}", path);
                        _pending.Remove(path);
                        SaveLocked();
                        continue;
                    }

                    if (_retryAfter.TryGetValue(path, out var until) && now < until)
                    {
                        continue; // está en pausa de reintento
                    }

                    next = path;
                    nextVisibility = visibility;
                    break;
                }
            }

            if (next == null)
            {
                // sin trabajo (o todo en pausa): esperar un poco y volver a mirar
                await Task.Delay(TimeSpan.FromSeconds(15), _timeProvider, ct);
                continue;
            }

            var result = await ProcessWithRetryAsync(next, nextVisibility!, ct);

            lock (_gate)
            {
                if (result.Result != QueueResult.TransientFail)
                {
                    _pending.Remove(result.CurrentPath);
                    _retryAfter.Remove(result.CurrentPath);
                    _retryAfter.Remove(next);
                    SaveLocked();
                }
                else
                {
                    // se queda en la cola; no volver a intentarlo hasta dentro de un rato
                    _retryAfter[result.CurrentPath] = _timeProvider.GetLocalNow().DateTime.AddMinutes(10);
                }
            }
        }
    }

    private async Task<(QueueResult Result, string CurrentPath)> ProcessWithRetryAsync(string path, string visibility, CancellationToken ct)
    {
        // Renombrar UNA sola vez, antes de los reintentos: si el primer intento
        // falla con un error transitorio, los siguientes ya usan la ruta nueva
        // (antes se reintentaba contra la ruta vieja, que ya no existe, y al
        // agotarse los reintentos la pausa se registraba con la clave equivocada,
        // con lo que el bucle re-procesaba el archivo una y otra vez).
        var currentPath = path;
        string? title = null;
        var playerName = _config.GetPlayerNameFor(currentPath);

        var header = ReplayHeaderParser.TryParse(currentPath);
        if (header != null)
        {
            var renamed = _renamer.RenameFromHeader(currentPath, header, playerName);
            if (renamed != null)
            {
                currentPath = renamed.Value.Path;
                title = renamed.Value.Title;
                Notify(path, "renamed", renamed.Value.Path);
                _logger.LogInformation("Renombrado localmente en la cola: {Path}", currentPath);
            }
        }

        // Si renombramos, la clave de la cola se actualiza AHORA: si la subida
        // falla después con un error transitorio, el siguiente pase del bucle
        // vería la ruta vieja como "inexistente" y descartaría el replay sin
        // subirlo nunca (bug de pérdida de trabajo).
        if (!string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            lock (_gate)
            {
                if (_pending.TryGetValue(path, out var vis))
                {
                    _pending.Remove(path);
                    _pending[currentPath] = vis;
                }
                _retryAfter.Remove(path);
                SaveLocked();
            }
        }

        var delay = BackoffBase;
        for (var attempt = 0; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                _logger.LogWarning("Reintento {Attempt}/{Max} para {Path} (espera {Delay})", attempt, MaxAttempts, currentPath, delay);
                await Task.Delay(delay, _timeProvider, ct);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(15).Ticks));
            }

            try
            {
                return await ProcessOnceAsync(currentPath, visibility, title);
            }
            catch (UploadTransientException ex)
            {
                _logger.LogWarning(ex, "Fallo transitorio subiendo {Path}", currentPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado subiendo {Path}", currentPath);
            }
        }

        _logger.LogWarning("Se agotaron los reintentos por ahora para {Path}; se queda en cola", currentPath);
        Notify(currentPath, "error", message: TranslationSource.Instance["Queue.NoConnection"], notify: true);
        return (QueueResult.TransientFail, currentPath);
    }

    private async Task<(QueueResult Result, string CurrentPath)> ProcessOnceAsync(string path, string visibility, string? title)
    {
        var currentPath = path;

        // 1) Subir (lanza UploadTransientException si es reintentable).
        //    (El renombrado local ya se hizo en ProcessWithRetryAsync, una sola
        //    vez antes de los reintentos.)
        string? replayId;
        bool alreadyExisted;
        try
        {
            (replayId, alreadyExisted) = await _ballchasing.UploadReplay(currentPath, visibility);
        }
        catch (UploadTransientException)
        {
            throw;
        }
        catch (UploadPermanentException ex)
        {
            // Rechazo definitivo (key inválida, formato no aceptado...): fuera
            // de la cola, mostrando el motivo real que devolvió ballchasing.
            _logger.LogError(ex, "Ballchasing rechazó el replay {Path}", currentPath);
            Notify(currentPath, "error", message: ex.Message, notify: true);
            return (QueueResult.PermanentFail, currentPath);
        }

        if (replayId == null)
        {
            // Respuesta "rara" de la API (ok sin id): tampoco tiene sentido
            // reintentar con la misma respuesta.
            _logger.LogError("Ballchasing no devolvió un id para {Path}", currentPath);
            Notify(currentPath, "error", message: TranslationSource.Instance["Queue.NoId"], notify: true);
            return (QueueResult.PermanentFail, currentPath);
        }

        // 3) Contabilizar estadísticas solo si la subida fue real (no un 409).
        if (!alreadyExisted)
        {
            try
            {
                _config.TotalUploads++;
                _config.TotalUploadedBytes += new FileInfo(currentPath).Length;
                _config.LastUploadAt = _timeProvider.GetLocalNow().DateTime;
                ConfigStore.Save(_config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudieron actualizar las estadísticas de subidas");
            }
        }

        Notify(currentPath, "uploaded", message: replayId, alreadyExisted: alreadyExisted, notify: true);

        // 4) Si el renombrado local no se pudo, usar los datos de ballchasing.
        if (title == null)
        {
            var metadata = await _ballchasing.GetReplayMetadata(replayId);
            if (metadata?.Blue != null && metadata.Orange != null)
            {
                var renamed = _renamer.Rename(currentPath, metadata, _config.GetPlayerNameFor(currentPath));
                if (renamed != null)
                {
                    Notify(currentPath, "renamed", renamed.Value.Path);
                    currentPath = renamed.Value.Path;
                    title = renamed.Value.Title;
                }
            }
            else
            {
                _logger.LogWarning("No se pudo obtener metadata de {Path} (id {Id}); se omite el renombrado vía API", path, replayId);
            }
        }

        // 5) Forzar el título en ballchasing (no es crítico si falla).
        if (title != null)
        {
            var ok = await _ballchasing.SetTitle(replayId, title);
            if (!ok)
            {
                _logger.LogWarning("No se pudo poner título a {Id} (el replay sí está subido)", replayId);
            }
        }

        // 6) Acción posterior configurable: dejar el archivo, mandarlo a la
        //    Papelera o archivarlo en una subcarpeta "Archivados". (No cambia
        //    "currentPath" a propósito: la clave de la cola sigue siendo la
        //    ruta de la carpeta de replays, que es la que se elimina al acabar.)
        var removedPath = TryRemoveAfterUpload(currentPath);
        if (removedPath != null)
        {
            _logger.LogInformation("Replay retirado de la carpeta tras subir ({Action}): {Path}", _config.AfterUploadAction, currentPath);
            Notify(currentPath, "removed", removedPath);
        }

        _logger.LogInformation("Replay subido y procesado: {Path} (id {Id})", currentPath, replayId);
        return (QueueResult.Success, currentPath);
    }

    // Devuelve la nueva ubicación si el archivo salió de la carpeta de replays
    // (Papelera -> cadena vacía, Archivados -> ruta destino), o null si no hay
    // acción configurada o no se pudo mover.
    private string? TryRemoveAfterUpload(string path)
    {
        var action = _config.AfterUploadAction;
        if (action is not ("recycle" or "archive")) return null;

        try
        {
            if (action == "recycle")
            {
                if (RecycleBin.TryDelete(path, out var error))
                {
                    return "";
                }
                _logger.LogWarning("No se pudo mover {Path} a la Papelera: {Error}", path, error);
                return null;
            }

            var archiveDir = Path.Combine(Path.GetDirectoryName(path) ?? ".", "Archivados");
            Directory.CreateDirectory(archiveDir);
            var target = Path.Combine(archiveDir, Path.GetFileName(path));
            if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase)) return null;

            File.Move(path, target, true);
            return target;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo aplicar la acción '{Action}' a {Path}", action, path);
            return null;
        }
    }

    private void Notify(string path, string status, string? newPath = null, string? message = null, bool alreadyExisted = false, bool notify = false)
    {
        Progress?.Invoke(new ReplayProgress
        {
            Path = path,
            Status = status,
            NewPath = newPath,
            Message = message,
            AlreadyExisted = alreadyExisted,
            Notify = notify
        });
    }

    private static string DefaultQueuePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RocketReplayUploader",
        "queue.json");

    private void Load()
    {
        try
        {
            if (!File.Exists(_queuePath)) return;

            var json = File.ReadAllText(_queuePath);
            lock (_gate)
            {
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string path;
                    string visibility;

                    if (element.ValueKind == JsonValueKind.String)
                    {
                        // Formato antiguo (solo rutas): la visibilidad por defecto.
                        path = element.GetString()!;
                        visibility = _config.Visibility;
                    }
                    else if ((element.TryGetProperty("path", out var pathProp) ||
                              element.TryGetProperty("Path", out pathProp)) &&
                             pathProp.ValueKind == JsonValueKind.String)
                    {
                        path = pathProp.GetString()!;
                        var vis = element.TryGetProperty("visibility", out var visProp) ||
                                  element.TryGetProperty("Visibility", out visProp)
                            ? visProp.GetString()
                            : null;
                        visibility = string.IsNullOrWhiteSpace(vis) ? _config.Visibility : vis;
                    }
                    else
                    {
                        continue;
                    }

                    if (File.Exists(path))
                    {
                        _pending[Path.GetFullPath(path)] = visibility;
                    }
                }
            }

            if (_pending.Count > 0)
            {
                _logger.LogInformation("Cola de subida restaurada con {Count} replays pendientes", _pending.Count);
                SaveLocked(); // re-guarda en el formato nuevo
                EnsureWorker();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo leer la cola de subidas guardada");
        }
    }

    private void SaveLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_queuePath)!;
            Directory.CreateDirectory(dir);
            var entries = _pending
                .Select(kv => new QueueEntryDto { Path = kv.Key, Visibility = kv.Value })
                .ToList();
            File.WriteAllText(_queuePath, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar la cola de subidas");
        }
    }

    private sealed class QueueEntryDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("visibility")]
        public string Visibility { get; set; } = "";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            // esperar a que el worker pare del todo antes de soltar recursos
            // (también evita que dos instancias trabajen sobre la misma cola)
            _worker?.Wait(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // el worker puede estar a mitad de una subida o cancelado
        }
        _cts?.Dispose();
    }

    private enum QueueResult
    {
        Success,
        PermanentFail,
        TransientFail
    }
}
