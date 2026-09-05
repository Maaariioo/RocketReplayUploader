namespace RocketReplayUploader.Infrastructure.Config;

// Una carpeta de replays vigilada, con el nombre de jugador de ESA cuenta.
// Permite jugar en Steam y Epic a la vez con cuentas distintas: cada carpeta
// se vigila y se renombra con el nombre de jugador correspondiente.
public class ReplayFolder
{
    public string Path { get; set; } = "";
    public string PlayerName { get; set; } = "";
}

// Configuración propia de cada usuario. Vive fuera de la carpeta del
// programa (en %AppData%), así que sirve igual para cualquiera que
// descargue el .exe y no se pierde si reemplazas/actualizas el programa.
public class AppConfig
{
    // Carpetas vigiladas (Steam, Epic, las que sea). El viejo ReplayPath/
    // PlayerName se conserva solo para migrar configs antiguas.
    public List<ReplayFolder> ReplayFolders { get; set; } = new();

    // LEGADO: config de la versión antigua, que solo tenía una carpeta.
    public string ReplayPath { get; set; } = "";
    public string PlayerName { get; set; } = "";

    public string BallchasingApiKey { get; set; } = "";

    // "public" | "unlisted" | "private"
    public string Visibility { get; set; } = "unlisted";

    // "dark" | "light"
    public string Theme { get; set; } = "dark";

    // "en" | "es" | "fr"
    public string Language { get; set; } = "en";

    // Qué hacer con el archivo local después de subirlo con éxito:
    // "none" (dejarlo) | "recycle" (Papelera) | "archive" (subcarpeta "Archivados")
    public string AfterUploadAction { get; set; } = "none";

    // Estadísticas acumuladas de subidas (se muestran en la interfaz).
    public int TotalUploads { get; set; }
    public long TotalUploadedBytes { get; set; }
    public DateTime? LastUploadAt { get; set; }

    // Nombre de jugador de la cuenta que toca un archivo, según la carpeta de
    // donde venga (null si no hay nada configurado para esa carpeta).
    public string? GetPlayerNameFor(string path)
    {
        foreach (var folder in ReplayFolders)
        {
            if (string.IsNullOrWhiteSpace(folder.Path)) continue;

            var root = Path.GetFullPath(folder.Path).TrimEnd('\\') + Path.DirectorySeparatorChar;
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(folder.PlayerName) ? null : folder.PlayerName;
            }
        }

        return string.IsNullOrWhiteSpace(PlayerName) ? null : PlayerName;
    }
}
