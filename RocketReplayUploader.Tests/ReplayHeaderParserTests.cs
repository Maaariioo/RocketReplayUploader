using System.IO;
using System.Text;
using RocketReplayUploader.Infrastructure.Replay;

namespace RocketReplayUploader.Tests;

public class ReplayHeaderParserTests
{
    private static string BuildReplay()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rr-test-{Guid.NewGuid():N}.replay");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0);          // headerSize (sin uso por el parser)
            writer.Write(0u);         // headerCrc
            writer.Write(872);        // engineVersion
            writer.Write(18);         // licenseeVersion
            writer.Write(2);          // netVersion extra (872 >= 868)
            WriteString(writer, "TAGame.Replay_Soccar_TA");

            WriteStrProp(writer, "PlayerName", "SmashMaster");
            WriteIntProp(writer, "Team0Score", 3);
            WriteIntProp(writer, "Team1Score", 2);
            WriteIntProp(writer, "PrimaryPlayerTeam", 1);
            WriteIntProp(writer, "TeamSize", 2);
            // BoolProperty real: declara size=0 pero ocupa 1 byte (ver fix del parser)
            WriteString(writer, "bForfeit");
            WriteString(writer, "BoolProperty");
            writer.Write(0L);
            writer.Write((byte)1);
            WriteStrProp(writer, "MapName", "UtopiaColiseum_P");
            WriteNameProp(writer, "MatchType", "Online");
            WriteStrProp(writer, "ReplayName", "Super match");

            WriteString(writer, "None");
        }
        return path;
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        w.Write(bytes.Length);
        w.Write(bytes);
    }

    private static void WriteStrProp(BinaryWriter w, string name, string value)
    {
        WriteString(w, name);
        WriteString(w, "StrProperty");
        w.Write((long)(4 + Encoding.ASCII.GetByteCount(value))); // size del valor
        WriteString(w, value);
    }

    private static void WriteNameProp(BinaryWriter w, string name, string value)
    {
        WriteString(w, name);
        WriteString(w, "NameProperty");
        w.Write((long)(4 + Encoding.ASCII.GetByteCount(value)));
        WriteString(w, value);
    }

    private static void WriteIntProp(BinaryWriter w, string name, int value)
    {
        WriteString(w, name);
        WriteString(w, "IntProperty");
        w.Write(4L); // size del valor
        w.Write(value);
    }

    [Fact]
    public void TryParse_DevuelveDatosDelHeader()
    {
        var path = BuildReplay();
        try
        {
            var info = ReplayHeaderParser.TryParse(path);

            Assert.NotNull(info);
            Assert.Equal("SmashMaster", info!.PlayerName);
            Assert.Equal(3, info.Team0Score);
            Assert.Equal(2, info.Team1Score);
            Assert.Equal(1, info.PrimaryPlayerTeam);
            Assert.Equal(2, info.TeamSize);
            Assert.Equal("UtopiaColiseum_P", info.MapName);
            Assert.Equal("Online", info.MatchType);
            Assert.Equal("Super match", info.ReplayName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryParse_ArchivoInexistenteDevuelveNull()
    {
        Assert.Null(ReplayHeaderParser.TryParse(@"Z:\no\existe\fake.replay"));
    }

    [Fact]
    public void TryParse_ArchivoBasuraDevuelveNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rr-basura-{Guid.NewGuid():N}.replay");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            Assert.Null(ReplayHeaderParser.TryParse(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryParse_TerminadorNoneCierraElDiccionario()
    {
        // Un archivo que acaba con "None" justo después de las props: el parser
        // no debe leer más allá de los bytes disponibles.
        var path = BuildReplay();
        try
        {
            var info = ReplayHeaderParser.TryParse(path);
            Assert.NotNull(info);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
