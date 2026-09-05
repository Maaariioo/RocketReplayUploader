using RocketReplayUploader.Application.Models;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Replay;

public class ReplayProcessor
{
    private readonly FileRenamerService _renamer;
    private readonly UploadQueueService _queue;
    private readonly AppConfig _config;
    private readonly ILogger<ReplayProcessor> _logger;

    // Notifica avances para que la interfaz actualice el estado de cada replay.
    public event Action<ReplayProgress>? Progress;

    public ReplayProcessor(
        FileRenamerService renamer,
        UploadQueueService queue,
        AppConfig config,
        ILogger<ReplayProcessor> logger)
    {
        _renamer = renamer;
        _queue = queue;
        _config = config;
        _logger = logger;
    }

    // Flujo de autosubida: renombrar a partir del .replay local y encolar la
    // subida. La subida real la hace UploadQueueService con reintentos y
    // persistencia (si no hay red, no se pierde el replay).
    public Task ProcessReplay(string path, string? playerName = null)
    {
        try
        {
            // 1) Intento rápido: leer el .replay localmente y renombrar sin
            //    depender de la API ni de comparar el nombre del jugador.
            var header = ReplayHeaderParser.TryParse(path);
            var renamed = header != null ? _renamer.RenameFromHeader(path, header, playerName) : null;

            var currentPath = path;
            if (renamed != null)
            {
                currentPath = renamed.Value.Path;
                Notify(path, "renamed", currentPath);
                _logger.LogInformation("Renombrado localmente a partir del .replay: {Path}", currentPath);
            }
            else
            {
                _logger.LogWarning("No se pudo renombrar localmente (header no legible o incompleto) para {Path}", path);
            }

            // 2) Encolar la subida (con reintentos y persistencia). La autosubida
            //    usa la visibilidad de la configuración; las subidas manuales
            //    pueden elegir otra distinta al encolar.
            _queue.Enqueue(currentPath, _config.Visibility);
            Notify(currentPath, "queued");
            _logger.LogInformation("Replay encolado para subir: {Path}", currentPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando el replay {Path}", path);
            Notify(path, "error", message: ex.Message);
        }
        return Task.CompletedTask;
    }

    private void Notify(string path, string status, string? newPath = null, string? message = null)
    {
        Progress?.Invoke(new ReplayProgress
        {
            Path = path,
            Status = status,
            NewPath = newPath,
            Message = message
        });
    }
}
