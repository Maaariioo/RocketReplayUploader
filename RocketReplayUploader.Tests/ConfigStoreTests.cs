using System.IO;
using RocketReplayUploader.Infrastructure.Config;

namespace RocketReplayUploader.Tests;

// Comparte colección con UploadQueueServiceTests: ambos redirigen el
// ConfigStore a carpetas temporales, así que no deben correr en paralelo.
[Collection("ConfigStore")]
public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rr-config-{Guid.NewGuid():N}");
    private readonly string _originalPath;

    public ConfigStoreTests()
    {
        _originalPath = ConfigStore.ConfigPath;
        ConfigStore.ConfigPath = Path.Combine(_dir, "config.json");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        ConfigStore.ConfigPath = _originalPath;
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    // Bug original: Save cifraba la key MUTANDO la AppConfig en memoria. Como
    // esa misma instancia es la que se usa para las peticiones HTTP, la
    // segunda subida enviaba el valor cifrado "v1:..." como API key y
    // ballchasing la rechazaba con 401 aunque la primera hubiese funcionado.
    [Fact]
    public void Save_CifraLaKeyEnDiscoSinMutarLaInstanciaEnMemoria()
    {
        var config = new AppConfig { BallchasingApiKey = "mi-key-en-claro" };

        ConfigStore.Save(config);

        Assert.Equal("mi-key-en-claro", config.BallchasingApiKey);

        var onDisk = File.ReadAllText(ConfigStore.ConfigPath);
        Assert.Contains("v1:", onDisk);
        Assert.DoesNotContain("mi-key-en-claro", onDisk);
    }

    [Fact]
    public void Load_DescifraLaKeyCifrada()
    {
        ConfigStore.Save(new AppConfig { BallchasingApiKey = "mi-key-en-claro" });

        var loaded = ConfigStore.Load();

        Assert.NotNull(loaded);
        Assert.Equal("mi-key-en-claro", loaded!.BallchasingApiKey);
    }
}
