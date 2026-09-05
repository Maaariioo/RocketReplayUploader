using System.Text;

namespace RocketReplayUploader.Infrastructure.Replay;

public class ReplayHeaderInfo
{
    public string? PlayerName { get; set; }
    public int? Team0Score { get; set; }
    public int? Team1Score { get; set; }
    public int? PrimaryPlayerTeam { get; set; }
    public int? TeamSize { get; set; }
    public string? MapName { get; set; }
    public string? MatchType { get; set; }
    public string? ReplayName { get; set; }
}

// Parser "best effort" del header (no comprimido) de un .replay de Rocket League.
// Formato basado en documentación de proyectos open-source de la comunidad
// (rattletrap / boxcars), no en una spec oficial de Epic/Psyonix.
// No está validado contra un archivo real en este entorno: si algo no encaja,
// devuelve null y el llamador debe usar el método anterior (vía API) como respaldo.
public static class ReplayHeaderParser
{
    public static ReplayHeaderInfo? TryParse(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            int headerSize = reader.ReadInt32();
            uint headerCrc = reader.ReadUInt32();

            int engineVersion = reader.ReadInt32();
            int licenseeVersion = reader.ReadInt32();

            // Versiones recientes del motor incluyen un net-version extra
            if (engineVersion >= 868 && licenseeVersion >= 18)
            {
                reader.ReadInt32();
            }

            ReadString(reader); // nombre de clase, p.ej. "TAGame.Replay_Soccar_TA"

            var props = ReadPropertyDict(reader);

            var info = new ReplayHeaderInfo
            {
                PlayerName = props.GetValueOrDefault("PlayerName") as string,
                Team0Score = AsInt(props.GetValueOrDefault("Team0Score")),
                Team1Score = AsInt(props.GetValueOrDefault("Team1Score")),
                PrimaryPlayerTeam = AsInt(props.GetValueOrDefault("PrimaryPlayerTeam")),
                TeamSize = AsInt(props.GetValueOrDefault("TeamSize")),
                MapName = props.GetValueOrDefault("MapName") as string,
                MatchType = props.GetValueOrDefault("MatchType") as string,
                ReplayName = props.GetValueOrDefault("ReplayName") as string
            };

            return info;
        }
        catch
        {
            // Cualquier desalineación de bytes (versión distinta del formato, etc.)
            // -> lo tratamos como "no se pudo parsear" en vez de reventar la app.
            return null;
        }
    }

    private static int? AsInt(object? value) => value == null ? null : Convert.ToInt32(value);

    private static Dictionary<string, object?> ReadPropertyDict(BinaryReader reader)
    {
        var dict = new Dictionary<string, object?>();

        while (true)
        {
            var name = ReadString(reader);
            if (name == "None" || string.IsNullOrEmpty(name)) break;

            var type = ReadString(reader);
            long size = reader.ReadInt64();
            long valueStart = reader.BaseStream.Position;

            object? value = type switch
            {
                "IntProperty" => reader.ReadInt32(),
                "FloatProperty" => reader.ReadSingle(),
                "QWordProperty" => reader.ReadInt64(),
                "BoolProperty" => reader.ReadByte() != 0,
                "StrProperty" => ReadString(reader),
                "NameProperty" => ReadString(reader),
                _ => null // ByteProperty/ArrayProperty/etc: no nos interesan aquí
            };

            // Nos reposicionamos exactamente al final del valor usando el tamaño
            // declarado, así no nos desalineamos con tipos que no parseamos arriba.
            // Ojo con BoolProperty: declara size=0 pero el valor ocupa 1 byte real;
            // reposicionar con size=0 volvería atrás y desalinearía todo el dict.
            reader.BaseStream.Position = valueStart + Math.Max(size, 1);

            if (value != null && !dict.ContainsKey(name))
            {
                dict[name] = value;
            }
        }

        return dict;
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length == 0) return string.Empty;

        if (length < 0)
        {
            // UTF-16LE: el tamaño negativo indica número de "chars" (incluye el \0 final).
            // -length puede desbordar para Int32.MinValue, así que lo hacemos en long.
            long byteCount = -(long)length * 2;
            if (byteCount <= 0 || byteCount > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new EndOfStreamException();
            }
            var bytes = reader.ReadBytes((int)byteCount);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }

        if (length > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new EndOfStreamException();
        }

        var asciiBytes = reader.ReadBytes(length);
        return Encoding.ASCII.GetString(asciiBytes).TrimEnd('\0');
    }
}
