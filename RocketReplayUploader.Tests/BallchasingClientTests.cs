using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Http;

namespace RocketReplayUploader.Tests;

// HttpMessageHandler de prueba: responde lo que le pida cada test.
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(_responder(request));
}

public class BallchasingClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rr-http-{Guid.NewGuid():N}");
    private readonly List<HttpClient> _clients = new();
    private string? _lastAuthHeader;

    public BallchasingClientTests()
    {
        Directory.CreateDirectory(_dir);
    }

    private BallchasingClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            _lastAuthHeader = request.Headers.TryGetValues("Authorization", out var values)
                ? string.Join(",", values)
                : null;
            return responder(request);
        });
        var http = new HttpClient(handler);
        _clients.Add(http);
        return new BallchasingClient(http, Config(), NullLogger<BallchasingClient>.Instance);
    }

    private static AppConfig Config() => new()
    {
        ReplayPath = "C:\\fake\\replays",
        PlayerName = "Mario",
        BallchasingApiKey = "test-key",
        Visibility = "unlisted"
    };

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private string CreateUploadFile()
    {
        var path = Path.Combine(_dir, $"upload-{Guid.NewGuid():N}.replay");
        File.WriteAllBytes(path, new byte[64]);
        return path;
    }

    [Fact]
    public async Task Upload_201DevuelveIdYNoEsDuplicado()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.Created, """{"id":"abc123"}"""));
        var path = CreateUploadFile();

        var (id, alreadyExisted) = await client.Upload(path, "public");

        Assert.Equal("abc123", id);
        Assert.False(alreadyExisted);
        Assert.Equal("test-key", _lastAuthHeader);
    }

    [Fact]
    public async Task Upload_409DevuelveIdDelExistente()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.Conflict, """{"id":"dup456"}"""));
        var path = CreateUploadFile();

        var (id, alreadyExisted) = await client.Upload(path, "unlisted");

        Assert.Equal("dup456", id);
        Assert.True(alreadyExisted);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Upload_ErroresTransitoriosLanzanExcepcion(int statusCode)
    {
        var client = CreateClient(_ => Json((HttpStatusCode)statusCode, """{"error":"retry later"}"""));
        var path = CreateUploadFile();

        await Assert.ThrowsAsync<UploadTransientException>(() => client.Upload(path, "public"));
    }

    [Fact]
    public async Task Upload_KeyInvalidaLanzaErrorPermanenteConMotivo()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.Unauthorized, """{"error":"invalid API key"}"""));
        var path = CreateUploadFile();

        var ex = await Assert.ThrowsAsync<UploadPermanentException>(() => client.Upload(path, "public"));

        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task Upload_KeyCambiadaEnConfigSeAplicaSinReiniciar()
    {
        var config = Config();
        var handler = new StubHttpMessageHandler(request =>
        {
            _lastAuthHeader = request.Headers.TryGetValues("Authorization", out var values)
                ? string.Join(",", values)
                : null;
            return Json(HttpStatusCode.Created, """{"id":"abc123"}""");
        });
        var http = new HttpClient(handler);
        _clients.Add(http);
        var client = new BallchasingClient(http, config, NullLogger<BallchasingClient>.Instance);
        var path = CreateUploadFile();

        await client.Upload(path, "public");
        Assert.Equal("test-key", _lastAuthHeader);

        config.BallchasingApiKey = "new-key"; // lo que hace Configuración al guardar
        await client.Upload(path, "public");

        Assert.Equal("new-key", _lastAuthHeader);
    }

    [Fact]
    public async Task Upload_RechazoGenerico4xxIncluyeElMotivoDelServidor()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.BadRequest, """{"error":"unsupported replay version"}"""));
        var path = CreateUploadFile();

        var ex = await Assert.ThrowsAsync<UploadPermanentException>(() => client.Upload(path, "public"));

        Assert.Contains("unsupported replay version", ex.Message);
    }

    [Fact]
    public async Task Upload_ErrorDeRedLanzaExcepcionTransitoria()
    {
        var client = CreateClient(_ => throw new HttpRequestException("connection refused"));
        var path = CreateUploadFile();

        await Assert.ThrowsAsync<UploadTransientException>(() => client.Upload(path, "public"));
    }

    [Fact]
    public async Task GetReplay_EnviaLaApiKeyEnElHeader()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """{"id":"abc","status":"ok"}"""));

        await client.GetReplay("abc");

        Assert.Equal("test-key", _lastAuthHeader);
    }

    [Fact]
    public async Task GetReplay_DevuelveMetadataCuandoEstaOk()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """{"id":"abc","status":"ok","playlist_id":"ranked-doubles"}"""));
        var path = CreateUploadFile();

        var meta = await client.GetReplay("abc");

        Assert.NotNull(meta);
        Assert.Equal("ranked-doubles", meta!.Playlist);
    }

    [Fact]
    public async Task GetReplay_EstadoFailedDevuelveNull()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """{"id":"abc","status":"failed"}"""));

        Assert.Null(await client.GetReplay("abc"));
    }

    [Fact]
    public async Task GetReplay_ReintentaMientrasEstePendiente()
    {
        var calls = 0;
        var client = CreateClient(_ =>
        {
            calls++;
            return calls < 2
                ? Json(HttpStatusCode.OK, """{"id":"abc","status":"pending"}""")
                : Json(HttpStatusCode.OK, """{"id":"abc","status":"ok"}""");
        });

        var meta = await client.GetReplay("abc");

        Assert.NotNull(meta);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SetTitle_DevuelveTrueConExito()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.True(await client.SetTitle("abc", "Mario_2v2_Game_2026-01-01_00-00-00"));
    }

    [Fact]
    public async Task SetTitle_DevuelveFalseSiLaApiRechaza()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.BadRequest, """{"error":"bad title"}"""));

        Assert.False(await client.SetTitle("abc", "Mario_2v2_Game_2026-01-01_00-00-00"));
    }

    [Fact]
    public async Task CreateGroup_EnviaLosCamposYDevuelveIdYEnlace()
    {
        string? sentBody = null;
        var client = CreateClient(request =>
        {
            sentBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.Created, """{"id":"mi-grupo-abc","link":"https://ballchasing.com/api/groups/mi-grupo-abc"}""");
        });

        var (id, link) = await client.CreateGroup("Sesión del viernes", "by-id", "by-distinct-players");

        Assert.Equal("mi-grupo-abc", id);
        Assert.Equal("https://ballchasing.com/api/groups/mi-grupo-abc", link);
        Assert.Contains("\"name\":\"Sesi\\u00F3n del viernes\"", sentBody);
        Assert.Contains("\"player_identification\":\"by-id\"", sentBody);
        Assert.Contains("\"team_identification\":\"by-distinct-players\"", sentBody);
        Assert.Equal("test-key", _lastAuthHeader);
    }

    [Fact]
    public async Task CreateGroup_KeyInvalidaLanzaErrorPermanente()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.Unauthorized, """{"error":"invalid API key"}"""));

        var ex = await Assert.ThrowsAsync<UploadPermanentException>(() => client.CreateGroup("g", "by-id", "by-distinct-players"));

        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task CreateGroup_ErroresTransitoriosSeReintentaran()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.TooManyRequests, """{"error":"slow down"}"""));

        await Assert.ThrowsAsync<UploadTransientException>(() => client.CreateGroup("g", "by-id", "by-distinct-players"));
    }

    [Fact]
    public async Task CreateGroup_RespuestaSinIdLanzaErrorPermanente()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.Created, """{"link":"https://ballchasing.com/groups/x"}"""));

        var ex = await Assert.ThrowsAsync<UploadPermanentException>(() => client.CreateGroup("g", "by-id", "by-distinct-players"));

        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public async Task AssignReplayToGroup_PatchConElIdDelGrupoYDevuelveTrue()
    {
        string? sentBody = null;
        var client = CreateClient(request =>
        {
            sentBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Equal(HttpMethod.Patch, request.Method);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var ok = await client.AssignReplayToGroup("replay-1", "mi-grupo-abc");

        Assert.True(ok);
        Assert.Contains("\"group\":\"mi-grupo-abc\"", sentBody);
        Assert.Equal("test-key", _lastAuthHeader);
    }

    [Fact]
    public async Task AssignReplayToGroup_DevuelveFalseSiLaApiRechaza()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.BadRequest, """{"error":"nope"}"""));

        Assert.False(await client.AssignReplayToGroup("replay-1", "mi-grupo-abc"));
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }
}
