using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Replay;

namespace RocketReplayUploader.Tests;

public class FileRenamerServiceTests
{
    private static FileRenamerService Create(string playerName = "Mario") =>
        new(new AppConfig { PlayerName = playerName }, NullLogger<FileRenamerService>.Instance);

    private static ReplayHeaderInfo Header(
        int team0 = 3, int team1 = 2, int myTeam = 0, int teamSize = 2) => new()
    {
        Team0Score = team0,
        Team1Score = team1,
        PrimaryPlayerTeam = myTeam,
        TeamSize = teamSize
    };

    [Fact]
    public void BuildTitleFromHeader_ComponeElTituloConLaFecha()
    {
        var title = Create().BuildTitleFromHeader(Header(teamSize: 2));

        Assert.NotNull(title);
        Assert.Matches(
            new Regex(@"^Mario_2v2_Game_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$"),
            title);
    }

    [Fact]
    public void BuildTitleFromHeader_ModoSegunTeamSize()
    {
        Assert.StartsWith("Mario_1v1_Game_", Create().BuildTitleFromHeader(Header(teamSize: 1)));
        Assert.StartsWith("Mario_3v3_Game_", Create().BuildTitleFromHeader(Header(teamSize: 3)));
        Assert.StartsWith("Mario_4v4_Game_", Create().BuildTitleFromHeader(Header(teamSize: 4)));
        Assert.StartsWith("Mario_Other_Game_", Create().BuildTitleFromHeader(Header(teamSize: 6)));
    }

    [Fact]
    public void BuildTitleFromHeader_GolesPropiosSegunMiEquipo()
    {
        // Mi equipo = 1 -> mis goles son los del equipo naranja (Team1Score = 2).
        // El título no incluye el marcador, pero sí el modo: nada que asertar más
        // allá de que no falle; el caso real de validación es el nombre de archivo.
        var title = Create().BuildTitleFromHeader(Header(team0: 3, team1: 2, myTeam: 1));
        Assert.NotNull(title);
    }

    [Fact]
    public void BuildTitleFromHeader_FaltanDatosDevuelveNull()
    {
        Assert.Null(Create().BuildTitleFromHeader(new ReplayHeaderInfo()));
        Assert.Null(Create().BuildTitleFromHeader(new ReplayHeaderInfo { Team0Score = 1 }));
    }

    [Fact]
    public void BuildTitleFromHeader_SinPlayerNameUsaPlayer()
    {
        var title = Create("").BuildTitleFromHeader(Header(teamSize: 2));
        Assert.StartsWith("Player_2v2_Game_", title);
    }

    [Fact]
    public void RenameFromHeader_RenombraElArchivoEnDisco()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rr-renamer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var original = Path.Combine(dir, "original.replay");
        File.WriteAllText(original, "contenido-de-prueba");

        try
        {
            var result = Create().RenameFromHeader(original, Header(teamSize: 2));

            Assert.NotNull(result);
            Assert.True(File.Exists(result!.Value.Path));
            Assert.Equal("contenido-de-prueba", File.ReadAllText(result.Value.Path));
            Assert.False(File.Exists(original));
            Assert.Matches(@"^Mario_2v2_Game_", Path.GetFileName(result.Value.Path));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void RenameFromHeader_HeaderIncompletoNoTocaElArchivo()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rr-renamer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var original = Path.Combine(dir, "original.replay");
        File.WriteAllText(original, "contenido-de-prueba");

        try
        {
            Assert.Null(Create().RenameFromHeader(original, new ReplayHeaderInfo()));
            Assert.True(File.Exists(original));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void RenameFromHeader_ReplayYaRenombradoNoSeToca()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rr-renamer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var already = Path.Combine(dir, "Mario_2v2_Game_2026-01-15_12-30-00.replay");
        File.WriteAllText(already, "contenido-de-prueba");

        try
        {
            var result = Create().RenameFromHeader(already, Header(teamSize: 2));

            // Ya tiene el patrón correcto: no se debe renombrar otra vez (evita
            // que cada re-subida cambie el nombre del archivo).
            Assert.NotNull(result);
            Assert.Equal(already, result!.Value.Path);
            Assert.Equal("Mario_2v2_Game_2026-01-15_12-30-00", result.Value.Title);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void RenameFromHeader_ArchivoYaExistenteConElMismoTituloSeDesambiguado()
    {
        // Dos replays guardados en el mismo segundo calcularían el mismo nombre:
        // el segundo no debe pisar al primero.
        var dir = Path.Combine(Path.GetTempPath(), $"rr-renamer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var original = Path.Combine(dir, "original.replay");
        File.WriteAllText(original, "contenido-de-prueba");
        var when = new DateTime(2026, 3, 4, 5, 6, 7);
        File.SetLastWriteTime(original, when); // el título sale de esta fecha

        try
        {
            var title = "Mario_2v2_Game_2026-03-04_05-06-07";
            var first = Create().RenameFromHeader(original, Header(teamSize: 2));

            Assert.NotNull(first);
            Assert.Equal(Path.Combine(dir, title + ".replay"), first!.Value.Path);

            var secondSrc = Path.Combine(dir, "otro.replay");
            File.WriteAllText(secondSrc, "otro-contenido");
            File.SetLastWriteTime(secondSrc, when); // mismo segundo -> mismo título
            var second = Create().RenameFromHeader(secondSrc, Header(teamSize: 2));

            Assert.NotNull(second);
            Assert.Equal(Path.Combine(dir, title + " (2).replay"), second!.Value.Path);
            Assert.Equal("otro-contenido", File.ReadAllText(second!.Value.Path));
            Assert.Equal("contenido-de-prueba", File.ReadAllText(first.Value.Path)); // no se pisó
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void BuildTitleFromHeader_UsaLaFechaIndicada()
    {
        var when = new DateTime(2026, 3, 4, 5, 6, 7);
        var title = Create().BuildTitleFromHeader(Header(teamSize: 2), when);

        Assert.Equal("Mario_2v2_Game_2026-03-04_05-06-07", title);
    }
}
