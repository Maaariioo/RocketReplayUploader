namespace RocketReplayUploader.Application.Models;

public class ReplayProgress
{
    public string Path { get; init; } = "";
    public string Status { get; init; } = "";
    public string? NewPath { get; init; }
    public string? Message { get; init; }

    // true cuando la API respondió 409 (el replay ya existía en ballchasing).
    public bool AlreadyExisted { get; init; }

    // true cuando este evento debe disparar una notificación de escritorio
    // (subida completada / error definitivo), no solo el estado de la fila.
    public bool Notify { get; init; }
}
