namespace RocketReplayUploader.Infrastructure.Http;

// Rechazo definitivo de ballchasing (4xx que no es 409: API key inválida,
// formato no aceptado...): la cola debe sacar el replay, no reintentarlo.
public class UploadPermanentException : Exception
{
    public UploadPermanentException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
