using System.Text.Json;
using RocketReplayUploader.Application.Services;

namespace RocketReplayUploader.Infrastructure.Config;

public static class ConfigStore
{
    private static string? _configPath;

    // %AppData%\RocketReplayUploader\config.json (por usuario de Windows).
    // Configurable para los tests (evitar tocar el %AppData% real).
    public static string ConfigPath
    {
        get => _configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RocketReplayUploader",
            "config.json");
        set => _configPath = value;
    }

    public static AppConfig? Load()
    {
        if (!File.Exists(ConfigPath)) return null;

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json);
            if (config == null) return null;

            // La API key se guarda cifrada (DPAPI): al cargar la desciframos
            // para usarla en memoria; la siguiente Save() la vuelve a cifrar.
            if (SecretProtector.IsProtected(config.BallchasingApiKey))
            {
                config.BallchasingApiKey = SecretProtector.Unprotect(config.BallchasingApiKey) ?? "";
            }
            else if (!string.IsNullOrEmpty(config.BallchasingApiKey))
            {
                // Migración automática: la config antigua guardaba la key en
                // claro; la volvemos a guardar cifrada de una vez.
                Save(config);
            }

            MigrateFolders(config);

            return config;
        }
        catch
        {
            return null; // config corrupta -> tratamos como "no configurado"
        }
    }

    // Migración de carpetas: las configs viejas tenían una sola carpeta
    // (ReplayPath/PlayerName); ahora hay una lista para poder vigilar Steam y
    // Epic a la vez. Además, si la cuenta de la otra plataforma deja replays
    // en su carpeta (Demos vs DemosEpic), se añade sola.
    private static void MigrateFolders(AppConfig config)
    {
        var changed = false;

        if (config.ReplayFolders.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(config.ReplayPath))
            {
                config.ReplayFolders.Add(new ReplayFolder
                {
                    Path = config.ReplayPath,
                    PlayerName = config.PlayerName
                });
                changed = true;
            }
            else
            {
                return; // sin ninguna carpeta configurada: nada que migrar
            }
        }

        var configured = config.ReplayFolders.Select(f => f.Path).Where(p => !string.IsNullOrWhiteSpace(p));
        var siblings = ReplayPathLocator.FindSiblingReplayFolders(configured);
        foreach (var sibling in siblings)
        {
            if (config.ReplayFolders.Any(f =>
                    string.Equals(f.Path, sibling, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Nombre de jugador por defecto: el de la config antigua (el
            // usuario puede ajustarlo en Configuración).
            config.ReplayFolders.Add(new ReplayFolder { Path = sibling, PlayerName = config.PlayerName });
            changed = true;
        }

        if (changed)
        {
            Save(config);
        }
    }

    public static void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);

        // IMPORTANTE: cifrar la key en una COPIA, no en la instancia que nos
        // pasan. La AppConfig vive en memoria con la key en claro y es la misma
        // que se usa para las peticiones HTTP: si la mutásemos aquí (bug
        // original), la siguiente subida enviaría el valor ya cifrado "v1:..."
        // como API key y ballchasing la rechazaría con 401.
        var toSave = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config)) ?? config;
        if (!string.IsNullOrEmpty(toSave.BallchasingApiKey) &&
            !SecretProtector.IsProtected(toSave.BallchasingApiKey))
        {
            toSave.BallchasingApiKey = SecretProtector.Protect(toSave.BallchasingApiKey);
        }

        var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
