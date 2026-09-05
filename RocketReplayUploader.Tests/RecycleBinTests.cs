using System.IO;
using RocketReplayUploader.Infrastructure.IO;

namespace RocketReplayUploader.Tests;

public class RecycleBinTests
{
    [Fact]
    public void TryDelete_MueveElArchivoALaPapelera()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rr-papelera-{Guid.NewGuid():N}.replay");
        File.WriteAllText(path, "contenido");

        var ok = RecycleBin.TryDelete(path, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TryDelete_ArchivoInexistenteFalla()
    {
        var ok = RecycleBin.TryDelete(@"Z:\no\existe\fake.replay", out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
