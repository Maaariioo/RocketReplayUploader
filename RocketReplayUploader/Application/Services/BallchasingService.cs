using RocketReplayUploader.Application.Models;
using RocketReplayUploader.Infrastructure.Http;

namespace RocketReplayUploader.Application.Services;

public class BallchasingService : IBallchasingService
{
    private readonly BallchasingClient _client;

    public BallchasingService(BallchasingClient client)
    {
        _client = client;
    }

    // Devuelve (id, yaExistía): "yaExistía" es true cuando ballchasing respondió
    // 409 porque ese replay ya estaba subido antes (mismo id de partida).
    public async Task<(string? Id, bool AlreadyExisted)> UploadReplay(string path, string visibility)
    {
        return await _client.Upload(path, visibility);
    }

    public async Task<ReplayMetadata?> GetReplayMetadata(string id)
    {
        return await _client.GetReplay(id);
    }

    public async Task<bool> SetTitle(string id, string title)
    {
        return await _client.SetTitle(id, title);
    }

    public async Task<(string Id, string Link)> CreateGroup(string name, string playerIdentification, string teamIdentification)
    {
        return await _client.CreateGroup(name, playerIdentification, teamIdentification);
    }

    public async Task<bool> AssignReplayToGroup(string replayId, string groupId)
    {
        return await _client.AssignReplayToGroup(replayId, groupId);
    }
}
