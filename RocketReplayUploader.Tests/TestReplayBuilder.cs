using System.IO;
using System.Text;

namespace RocketReplayUploader.Tests;

// Crea un .replay sintético mínimo que el ReplayHeaderParser sí sabe leer
// (misma estructura que el builder de ReplayHeaderParserTests).
public static class TestReplayBuilder
{
    public static string BuildReplay(string? playerName = "SmashMaster")
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

            WriteStrProp(writer, "PlayerName", playerName!);
            WriteIntProp(writer, "Team0Score", 3);
            WriteIntProp(writer, "Team1Score", 2);
            WriteIntProp(writer, "PrimaryPlayerTeam", 1);
            WriteIntProp(writer, "TeamSize", 2);
            // BoolProperty real: declara size=0 pero ocupa 1 byte
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
}
