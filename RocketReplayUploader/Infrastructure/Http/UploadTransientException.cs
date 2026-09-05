namespace RocketReplayUploader.Infrastructure.Http;

// Error de red/HTTP transitorio (sin conexión, 429 rate-limit, 5xx...):
// la cola de subida debe reintentarlo más tarde, no darlo por perdido.
public class UploadTransientException : Exception
{
    public UploadTransientException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
