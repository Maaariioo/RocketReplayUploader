using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RocketReplayUploader.Application.Models;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Http;

namespace RocketReplayUploader.Tests;

// TimeProvider "inmediato": el tiempo no avanza solo (los tests lo empujan) y
// los timers disparan el callback al momento, así que los backoffs de la cola
// no ralentizan los tests (Task.Delay con TimeProvider usa CreateTimer).
public class ImmediateTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => UtcNow;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => new ImmediateTimer(callback, state, dueTime);
}

public class ImmediateTimer : ITimer
{
    private readonly TimerCallback _callback;
    private readonly object? _state;

    public ImmediateTimer(TimerCallback callback, object? state, TimeSpan dueTime)
    {
        _callback = callback;
        _state = state;
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            _callback(_state);
        }
    }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            _callback(_state);
        }
        return true;
    }

    public void Dispose() { }
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public class CapturingLogger : ILogger<UploadQueueService>
{
    private readonly List<string> _logs;
    public CapturingLogger(List<string> logs) => _logs = logs;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _logs.Add($"{logLevel}: {formatter(state, exception)}");
}

public class FakeBallchasing : IBallchasingService
{
    public int UploadAttempts { get; private set; }

    // null = siempre con éxito (id "fake-id"). Lanza UploadTransientException
    // o devuelve (null, false) según lo que pida el test.
    public Func<string, string, Task<(string? Id, bool AlreadyExisted)>>? UploadHandler { get; set; }
    public Func<string, Task<ReplayMetadata?>>? MetadataHandler { get; set; }
    public Func<string, string, Task<bool>>? SetTitleHandler { get; set; }

    public Task<(string? Id, bool AlreadyExisted)> UploadReplay(string path, string visibility)
    {
        UploadAttempts++;
        if (UploadHandler != null)
        {
            return UploadHandler(path, visibility);
        }
        return Task.FromResult<(string?, bool)>(("fake-id", false));
    }

    public Task<ReplayMetadata?> GetReplayMetadata(string id)
        => MetadataHandler?.Invoke(id) ?? Task.FromResult<ReplayMetadata?>(null);

    public Task<bool> SetTitle(string id, string title)
        => SetTitleHandler?.Invoke(id, title) ?? Task.FromResult(true);

    public Task<(string Id, string Link)> CreateGroup(string name, string playerIdentification, string teamIdentification)
        => Task.FromResult(("fake-group-id", $"https://ballchasing.com/groups/fake-group-id"));

    public Task<bool> AssignReplayToGroup(string replayId, string groupId)
        => Task.FromResult(true);
}

// Comparte colección con ConfigStoreTests: ambos redirigen el ConfigStore a
// carpetas temporales, así que no deben correr en paralelo.
[Collection("ConfigStore")]
public class UploadQueueServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rr-queue-{Guid.NewGuid():N}");
    private readonly string _originalConfigPath;
    private readonly ImmediateTimeProvider _time = new();
    private readonly List<string> _logs = new();

    public UploadQueueServiceTests()
    {
        Directory.CreateDirectory(_dir);
        // La cola guarda las estadísticas con ConfigStore.Save: redirigirlo a
        // la carpeta temporal para no tocar el %AppData% real.
        _originalConfigPath = ConfigStore.ConfigPath;
        ConfigStore.ConfigPath = Path.Combine(_dir, "config.json");
    }

    private UploadQueueService Create(FakeBallchasing fake, AppConfig? config = null)
        => new(
            fake,
            new FileRenamerService(config ?? Config(), NullLogger<FileRenamerService>.Instance),
            config ?? Config(),
            new CapturingLogger(_logs),
            _time,
            Path.Combine(_dir, "queue.json"));

    private static AppConfig Config(string afterUpload = "none") => new()
    {
        ReplayPath = "C:\\fake\\replays",
        PlayerName = "Mario",
        BallchasingApiKey = "test-key",
        Visibility = "unlisted",
        AfterUploadAction = afterUpload
    };

    private string CreateReplayFile()
    {
        var src = TestReplayBuilder.BuildReplay();
        var dest = Path.Combine(_dir, $"replay-{Guid.NewGuid():N}.replay");
        File.Move(src, dest);
        return dest;
    }

    // Espera (sin dormir más de lo necesario) a que la cola quede como se pide.
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task SubidaConExito_VaciaLaCola()
    {
        var fake = new FakeBallchasing();
        using var queue = Create(fake);
        var file = CreateReplayFile();

        queue.Enqueue(file);
        await WaitUntilAsync(() => queue.PendingCount == 0);

        Assert.Equal(1, fake.UploadAttempts);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task FalloTransitorio_ElReplaySeQuedaEnColaYSeReintenta()
    {
        var fake = new FakeBallchasing
        {
            UploadHandler = (_, _) => Task.FromException<(string?, bool)>(new UploadTransientException("sin red"))
        };
        using var queue = Create(fake);
        var file = CreateReplayFile();

        queue.Enqueue(file);
        await WaitUntilAsync(() => fake.UploadAttempts >= 8); // agota los reintentos del pase

        Assert.Equal(1, queue.PendingCount); // NO se descarta

        // Pasan los 10 min de pausa: el siguiente pase vuelve a intentarlo.
        _time.UtcNow = _time.UtcNow.AddMinutes(11);
        fake.UploadHandler = (_, _) => Task.FromResult<(string?, bool)>(("fake-id", false));
        await WaitUntilAsync(() => queue.PendingCount == 0);

        Assert.True(fake.UploadAttempts >= 9);
    }

    [Fact]
    public async Task RenombradoDuranteLaSubida_LaColaSigueLaRutaNueva()
    {
        // Bug original: si la subida se renombraba y luego fallaba (transitorio),
        // el siguiente pase veía la ruta vieja como inexistente y DESCARTABA el
        // replay sin subirlo nunca.
        var fake = new FakeBallchasing
        {
            UploadHandler = (_, _) => Task.FromException<(string?, bool)>(new UploadTransientException("sin red"))
        };
        using var queue = Create(fake);
        var file = CreateReplayFile(); // el header se puede leer -> la cola lo renombra

        queue.Enqueue(file);
        await WaitUntilAsync(() => fake.UploadAttempts >= 8);

        Assert.Equal(1, queue.PendingCount); // clave crítica: sigue en cola

        // La cola debe haber guardado la ruta NUEVA (renombrada), no la vieja.
        var saved = File.ReadAllText(Path.Combine(_dir, "queue.json"));
        Assert.DoesNotContain(Path.GetFileName(file), saved);
        Assert.Contains("Mario_2v2_Game_", saved);

        // Y con red, al final se sube.
        _time.UtcNow = _time.UtcNow.AddMinutes(11);
        fake.UploadHandler = (_, _) => Task.FromResult<(string?, bool)>(("fake-id", false));
        await WaitUntilAsync(() => queue.PendingCount == 0);

        Assert.True(fake.UploadAttempts >= 9);
    }

    [Fact]
    public async Task FalloPermanente_SaleDeLaCola()
    {
        var fake = new FakeBallchasing
        {
            UploadHandler = (_, _) => Task.FromResult<(string?, bool)>((null, false))
        };
        using var queue = Create(fake);
        var file = CreateReplayFile();

        queue.Enqueue(file);
        await WaitUntilAsync(() => queue.PendingCount == 0);

        Assert.Equal(1, fake.UploadAttempts);
    }

    [Fact]
    public async Task FalloPermanenteConMotivo_SacaDeLaColaYNotificaElMotivo()
    {
        var messages = new List<string>();
        var fake = new FakeBallchasing
        {
            UploadHandler = (_, _) =>
                throw new UploadPermanentException("Ballchasing rechazó la API key (¿caducada o revocada?). Genera una nueva en ballchasing.com")
        };
        using var queue = Create(fake);
        queue.Progress += p =>
        {
            if (p.Status == "error" && p.Message != null) messages.Add(p.Message);
        };
        var file = CreateReplayFile();

        queue.Enqueue(file);
        await WaitUntilAsync(() => queue.PendingCount == 0);

        Assert.Equal(1, fake.UploadAttempts); // un solo intento: no se reintenta
        var msg = Assert.Single(messages);
        Assert.Contains("API key", msg);
    }

    [Fact]
    public async Task Estadisticas_SeAcumulanTrasSubir()
    {
        var config = Config();
        var fake = new FakeBallchasing();
        using var queue = Create(fake, config);
        var file = CreateReplayFile();
        var size = new FileInfo(file).Length;

        queue.Enqueue(file);
        await WaitUntilAsync(() => queue.PendingCount == 0);

        Assert.Equal(1, config.TotalUploads);
        Assert.Equal(size, config.TotalUploadedBytes);
        Assert.NotNull(config.LastUploadAt);

        // Bug original: tras la subida, ConfigStore.Save cifraba la key EN LA
        // MISMA instancia en memoria, y la siguiente subida la enviaba cifrada
        // (401 de ballchasing). La instancia debe seguir con la key en claro.
        Assert.Equal("test-key", config.BallchasingApiKey);
    }

    [Fact]
    public async Task Estadisticas_LosDuplicados409NoCuentan()
    {
        var config = Config();
        var fake = new FakeBallchasing
        {
            UploadHandler = (_, _) => Task.FromResult<(string?, bool)>(("existing-id", true)) // 409
        };
        using var queue = Create(fake, config);
        var file = CreateReplayFile();

        queue.Enqueue(file);
        await WaitUntilAsync(() => queue.PendingCount == 0);

        Assert.Equal(0, config.TotalUploads);
    }

    [Fact]
    public async Task AccionPosterior_ArchivarMueveElArchivo()
    {
        var config = Config(afterUpload: "archive");
        using var queue = Create(new FakeBallchasing(), config);
        var file = CreateReplayFile();

        queue.Enqueue(file);
        await WaitUntilAsync(() => queue.PendingCount == 0);

        // La cola renombra el archivo antes de subirlo, así que en "Archivados"
        // estará con el nombre nuevo (Mario_2v2_Game_...).
        var archived = Directory.GetFiles(Path.Combine(_dir, "Archivados"));
        Assert.Single(archived);
        Assert.StartsWith("Mario_2v2_Game_", Path.GetFileName(archived[0]));
        Assert.Empty(Directory.GetFiles(_dir, "*.replay"));
    }

    [Fact]
    public async Task AccionPosterior_PapeleraEliminaElArchivo()
    {
        var config = Config(afterUpload: "recycle");
        using var queue = Create(new FakeBallchasing(), config);
        var file = CreateReplayFile();

        queue.Enqueue(file);
        await WaitUntilAsync(() => queue.PendingCount == 0);

        Assert.Empty(Directory.GetFiles(_dir, "*.replay"));
    }

    [Fact]
    public async Task Persistencia_LaColaSeRestauraEntreInstancias()
    {
        Func<string, string, Task<(string?, bool)>> fail =
            (_, _) => Task.FromException<(string?, bool)>(new UploadTransientException("sin red"));
        var file = CreateReplayFile();

        using (var queue = Create(new FakeBallchasing { UploadHandler = fail }))
        {
            queue.Enqueue(file);
            // Esperar a que la cola haya procesado el archivo (renombrado +
            // agotado los reintentos) antes de "cerrar la app".
            await WaitUntilAsync(() => queue.PendingCount == 1);
            await Task.Delay(300); // margen para que termine el pase actual
            var savedAfterProcessing = File.ReadAllText(Path.Combine(_dir, "queue.json"));
            Assert.Equal(1, queue.PendingCount);
            Assert.Contains("Mario_2v2_Game_", savedAfterProcessing); // clave actualizada
        }

        // Nueva instancia (como un reinicio de la app): recupera lo pendiente.
        // Con el fake aún fallando, la entrada debe seguir ahí, no perderse.
        var fake2 = new FakeBallchasing { UploadHandler = fail };
        using (var queue2 = Create(fake2))
        {
            Assert.Equal(1, queue2.PendingCount);
            await WaitUntilAsync(() => fake2.UploadAttempts >= 8);
            Assert.Equal(1, queue2.PendingCount);
        }
    }

    [Fact]
    public async Task Persistencia_FormatoLegadoDeRutasSimples()
    {
        var a = CreateReplayFile();
        var b = CreateReplayFile();
        // Formato antiguo: solo rutas (escapadas como JSON válido).
        File.WriteAllText(Path.Combine(_dir, "queue.json"),
            System.Text.Json.JsonSerializer.Serialize(new[] { a, b }));

        var fake = new FakeBallchasing
        {
            UploadHandler = (_, _) => Task.FromException<(string?, bool)>(new UploadTransientException("sin red"))
        };
        using var queue = Create(fake);

        await WaitUntilAsync(() => fake.UploadAttempts >= 8);
        Assert.Equal(2, queue.PendingCount);
    }

    public void Dispose()
    {
        ConfigStore.ConfigPath = _originalConfigPath;
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }
}
