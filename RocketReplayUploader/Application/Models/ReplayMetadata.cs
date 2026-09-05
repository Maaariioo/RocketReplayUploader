using System.Text.Json.Serialization;

namespace RocketReplayUploader.Application.Models;

public class ReplayMetadata
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    // "ok" | "pending" | "failed"
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("blue")]
    public Team? Blue { get; set; }

    [JsonPropertyName("orange")]
    public Team? Orange { get; set; }

    [JsonPropertyName("playlist_id")]
    public string? Playlist { get; set; }
}

public class Team
{
    [JsonPropertyName("players")]
    public List<Player>? Players { get; set; }

    [JsonPropertyName("goals")]
    public int Goals { get; set; }
}

public class Player
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
