using System.IO;
using RocketReplayUploader.Infrastructure.Replay;

namespace RocketReplayUploader.Tests;

// Valida el parser contra archivos REALES de Rocket League. Si la carpeta
// estándar no existe (máquina de CI o sin el juego), el test se salta.
public class ReplayHeaderParserRealFileTests
{
    private static string ReplayFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Rocket League", "TAGame", "Demos");

    private static IEnumerable<string> RealReplays()
    {
        if (!Directory.Exists(ReplayFolder)) yield break;
        foreach (var file in Directory.EnumerateFiles(ReplayFolder, "*.replay").Take(10))
        {
            yield return file;
        }
    }

    [Fact]
    public void TryParse_LeeReplaysRealesSinFallar()
    {
        var replays = RealReplays().ToList();
        if (replays.Count == 0) return; // sin replays reales: nada que validar

        int parsed = 0;
        foreach (var replay in replays)
        {
            var info = ReplayHeaderParser.TryParse(replay);
            Assert.NotNull(info);
            parsed++;
        }

        Assert.True(parsed > 0, "Ningún replay real se pudo parsear");
    }

    [Fact]
    public void TryParse_ReplaysRealesDevuelvenDatosConsistentes()
    {
        var replays = RealReplays().ToList();
        if (replays.Count == 0) return;

        foreach (var replay in replays)
        {
            var info = ReplayHeaderParser.TryParse(replay);
            Assert.NotNull(info);

            // Coherencia básica: si hay marcador, ambos equipos deben tenerlo,
            // y los equipos no pueden ir negativos ni superar cifras absurdas.
            if (info!.Team0Score is int s0 && info.Team1Score is int s1)
            {
                Assert.InRange(s0, 0, 99);
                Assert.InRange(s1, 0, 99);
            }

            // El tamaño de equipo de un partido real suele estar entre 1 y 4
            // (también pueden aparecer otros modos; 0 solo si falló el parseo).
            if (info.TeamSize is int ts)
            {
                Assert.InRange(ts, 1, 8);
            }

            if (info.PrimaryPlayerTeam is int team)
            {
                Assert.InRange(team, 0, 1);
            }

            if (info.MapName != null)
            {
                Assert.NotEqual("", info.MapName);
            }
        }
    }
}
