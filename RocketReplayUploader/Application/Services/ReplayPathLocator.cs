namespace RocketReplayUploader.Application.Services;

// Localiza la carpeta donde Rocket League guarda los replays. La versión de
// Steam usa Documentos\My Games\Rocket League\TAGame\Demos; la de Epic (la
// gratuita, que es la que casi todo el mundo usa hoy) guarda en
// ...\TAGame\DemosEpic. Hay que probar varias ubicaciones porque Windows
// puede redirigir Documentos a OneDrive (o no).
public static class ReplayPathLocator
{
    // Candidatas en orden de probabilidad. "candidates" es inyectable para tests.
    public static string? FindDefault(IEnumerable<string>? candidates = null)
    {
        var list = (candidates ?? DefaultCandidates()).ToList();
        if (list.Count == 0) return null;

        // 1) Preferir la carpeta con el replay MÁS RECIENTE: si el juego guarda
        //    en varias (Demos y DemosEpic conviven en máquinas que pasaron de
        //    Steam a Epic), la que está en uso de verdad es la del replay nuevo.
        var withReplays = list
            .Where(Directory.Exists)
            .Select(c => (Path: c, Newest: NewestReplay(c)))
            .Where(x => x.Newest.HasValue)
            .OrderByDescending(x => x.Newest)
            .Select(x => x.Path)
            .ToList();
        if (withReplays.Count > 0) return withReplays[0];

        // 2) Una carpeta que exista (aunque esté vacía, es donde el juego escribe).
        foreach (var candidate in list)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        // 3) Sin pistas: devolver la primera candidata para que el usuario la
        //    confirme (mejor que dejar el campo vacío).
        return list[0];
    }

    // Carpetas hermanas estándar (Demos y/o DemosEpic) que existan y tengan
    // replays, excluyendo las ya configuradas. Sirve para que la app vigile
    // Steam y Epic a la vez sin que el usuario tenga que configurar nada.
    public static List<string> FindSiblingReplayFolders(IEnumerable<string> configured)
    {
        var result = new List<string>();
        var configuredSet = new HashSet<string>(
            configured.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in configured)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;

            // ...\Rocket League\TAGame (la carpeta que contiene Demos y DemosEpic).
            var tagame = Path.GetDirectoryName(Path.GetFullPath(folder));
            if (tagame == null || !visited.Add(tagame)) continue;

            foreach (var candidate in new[] { Path.Combine(tagame, "Demos"), Path.Combine(tagame, "DemosEpic") })
            {
                var full = Path.GetFullPath(candidate);
                if (configuredSet.Contains(full) || result.Contains(full, StringComparer.OrdinalIgnoreCase)) continue;

                if (Directory.Exists(full) && HasReplays(full))
                {
                    result.Add(full);
                }
            }
        }

        return result;
    }

    // Los dos directorios estándar de replays (Demos de Steam y DemosEpic de
    // Epic), existan o no: así la app queda cubierta aunque el usuario todavía
    // no haya jugado en una de las cuentas (o tenga el juego en otro sitio).
    // Si Documentos está en OneDrive, esa variante ya coincide con "docs"; en
    // caso contrario se añade también por si el juego escribió ahí.
    public static List<string> FindStandardFolders()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFolder(string path)
        {
            var full = Path.GetFullPath(path);
            if (seen.Add(full))
            {
                result.Add(full);
            }
        }

        AddFolder(Path.Combine(docs, "My Games", "Rocket League", "TAGame", "Demos"));
        AddFolder(Path.Combine(docs, "My Games", "Rocket League", "TAGame", "DemosEpic"));

        var oneDrive = Path.Combine(userProfile, "OneDrive");
        if (Directory.Exists(oneDrive))
        {
            AddFolder(Path.Combine(oneDrive, "Documents", "My Games", "Rocket League", "TAGame", "Demos"));
            AddFolder(Path.Combine(oneDrive, "Documents", "My Games", "Rocket League", "TAGame", "DemosEpic"));
        }

        return result;
    }

    private static bool HasReplays(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.replay").Any();
        }
        catch
        {
            return false;
        }
    }

    // Fecha del replay más reciente de la carpeta (o null si no hay ninguno).
    private static DateTime? NewestReplay(string folder)
    {
        try
        {
            var newest = Directory.EnumerateFiles(folder, "*.replay")
                .Select(File.GetLastWriteTime)
                .OrderByDescending(t => t)
                .FirstOrDefault();
            return newest == default ? null : newest;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> DefaultCandidates()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        yield return Path.Combine(docs, "My Games", "Rocket League", "TAGame", "Demos");
        yield return Path.Combine(docs, "My Games", "Rocket League", "TAGame", "DemosEpic");
        yield return Path.Combine(userProfile, "OneDrive", "Documents", "My Games", "Rocket League", "TAGame", "Demos");
        yield return Path.Combine(userProfile, "OneDrive", "Documents", "My Games", "Rocket League", "TAGame", "DemosEpic");
        yield return Path.Combine(userProfile, "Documents", "My Games", "Rocket League", "TAGame", "Demos");
        yield return Path.Combine(userProfile, "Documents", "My Games", "Rocket League", "TAGame", "DemosEpic");
    }
}
