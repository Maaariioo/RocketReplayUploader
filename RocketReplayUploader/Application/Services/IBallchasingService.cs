using RocketReplayUploader.Application.Models;

// Abstracción de ballchasing.com para poder probar la cola de subidas con un
// doble (fake) sin tocar la red real.
public interface IBallchasingService
{
    Task<(string? Id, bool AlreadyExisted)> UploadReplay(string path, string visibility);
    Task<ReplayMetadata?> GetReplayMetadata(string id);
    Task<bool> SetTitle(string id, string title);
    Task<(string Id, string Link)> CreateGroup(string name, string playerIdentification, string teamIdentification);
    Task<bool> AssignReplayToGroup(string replayId, string groupId);
}
