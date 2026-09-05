using System.IO;
using RocketReplayUploader.Application.Services;

namespace RocketReplayUploader.Tests;

public class ReplayPathLocatorTests : IDisposable
{
    private readonly string _dir;

    public ReplayPathLocatorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"rr-locator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    private string NewDir(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void SinCarpetasExistentes_DevuelveLaPrimeraCandidata()
    {
        var a = Path.Combine(_dir, "no-existe-a");
        var b = Path.Combine(_dir, "no-existe-b");

        var result = ReplayPathLocator.FindDefault(new[] { a, b });

        Assert.Equal(a, result);
    }

    [Fact]
    public void SinCandidatas_DevuelveNull()
    {
        Assert.Null(ReplayPathLocator.FindDefault(Array.Empty<string>()));
    }

    [Fact]
    public void CarpetasVacias_PrefiereLaPrimeraQueExiste()
    {
        var first = NewDir("primera");
        var second = NewDir("segunda");

        var result = ReplayPathLocator.FindDefault(new[] { first, second, Path.Combine(_dir, "no") });

        Assert.Equal(first, result);
    }

    [Fact]
    public void CarpetaConReplays_PrefiereSobreUnaVacia()
    {
        var empty = NewDir("vacia");
        var withReplays = NewDir("con-replays");
        File.WriteAllText(Path.Combine(withReplays, "partida.replay"), "contenido");

        var result = ReplayPathLocator.FindDefault(new[] { empty, withReplays });

        Assert.Equal(withReplays, result);
    }

    [Fact]
    public void CarpetaConReplays_PrefiereAunqueNoSeaLaPrimeraCandidata()
    {
        var withReplays = NewDir("con-replays");
        File.WriteAllText(Path.Combine(withReplays, "otra.replay"), "contenido");
        var empty = NewDir("vacia");

        // La primera candidata existe pero está vacía; la segunda tiene replays.
        var result = ReplayPathLocator.FindDefault(new[] { empty, withReplays });

        Assert.Equal(withReplays, result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }
}
