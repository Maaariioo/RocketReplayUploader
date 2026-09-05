using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using RocketReplayUploader.Application.Models;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Infrastructure.Http;
using RocketReplayUploader.Infrastructure.Localization;

public class BallchasingClient
{
    private readonly HttpClient _http;
    private readonly AppConfig _config;
    private readonly ILogger<BallchasingClient> _logger;

    public BallchasingClient(HttpClient http, AppConfig config, ILogger<BallchasingClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(config.BallchasingApiKey))
        {
            throw new InvalidOperationException(TranslationSource.Instance["Exc.MissingKey"]);
        }
    }

    // La key se aplica por petición (no en el constructor): si el usuario la
    // cambia en Configuración, el cambio vale sin reiniciar la app.
    private void ApplyAuth(HttpRequestMessage request)
    {
        var key = _config.BallchasingApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(TranslationSource.Instance["Exc.MissingKey"]);
        }

        _logger.LogDebug("Petición con API key len={Len} {Fingerprint}", key.Length, KeyFingerprint(key));
        request.Headers.TryAddWithoutValidation("Authorization", key);
    }
    private class UploadResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private class GroupResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }
    }

    // Crea un grupo de replays en ballchasing.com y devuelve su id y enlace.
    // Lanza UploadPermanentException si ballchasing lo rechaza (p. ej. key no
    // válida o sin permisos para crear grupos).
    public async Task<(string Id, string Link)> CreateGroup(string name, string playerIdentification, string teamIdentification)
    {
        var payload = JsonSerializer.Serialize(new
        {
            name,
            player_identification = playerIdentification,
            team_identification = teamIdentification
        });

        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://ballchasing.com/api/groups")
        {
            Content = content
        };
        ApplyAuth(request);

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Error de red creando el grupo '{Name}'", name);
            throw new UploadTransientException(TranslationSource.Instance["Exc.Network"], ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning("Timeout creando el grupo '{Name}'", name);
            throw new UploadTransientException(TranslationSource.Instance["Exc.Timeout"], ex);
        }

        if (res.StatusCode == HttpStatusCode.TooManyRequests || (int)res.StatusCode >= 500)
        {
            throw new UploadTransientException(TranslationSource.Instance.Format("Exc.TransientGroup", (int)res.StatusCode));
        }

        if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new UploadPermanentException(TranslationSource.Instance["Exc.ApiKeyRejected"]);
        }

        if (!res.IsSuccessStatusCode)
        {
            var errorBody = await res.Content.ReadAsStringAsync();
            var reason = ExtractError(errorBody);
            _logger.LogError("Fallo al crear el grupo '{Name}': {Status} - {Body}", name, res.StatusCode, errorBody);
            throw new UploadPermanentException(
                TranslationSource.Instance.Format("Exc.GroupRejected", (int)res.StatusCode) +
                (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}"));
        }

        var json = await res.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<GroupResponse>(json);

        if (string.IsNullOrWhiteSpace(data?.Id))
        {
            _logger.LogError("Respuesta de creación de grupo sin id: {Body}", json);
            throw new UploadPermanentException(TranslationSource.Instance["Exc.GroupNoId"]);
        }

        return (data.Id, data.Link ?? $"https://ballchasing.com/groups/{data.Id}");
    }

    // Asigna un replay (ya subido) a un grupo existente (PATCH /replays/{id}
    // con el campo "group"). Devuelve false si ballchasing lo rechaza.
    public async Task<bool> AssignReplayToGroup(string replayId, string groupId)
    {
        var payload = JsonSerializer.Serialize(new { group = groupId });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"https://ballchasing.com/api/replays/{replayId}")
        {
            Content = content
        };
        ApplyAuth(request);

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Error de red asignando {ReplayId} al grupo {GroupId}", replayId, groupId);
            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout asignando {ReplayId} al grupo {GroupId}", replayId, groupId);
            return false;
        }

        if (res.StatusCode == HttpStatusCode.TooManyRequests || (int)res.StatusCode >= 500)
        {
            _logger.LogWarning("Respuesta transitoria asignando {ReplayId} al grupo {GroupId}: {Status}", replayId, groupId, res.StatusCode);
            return false;
        }

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            _logger.LogError("Fallo al asignar {ReplayId} al grupo {GroupId}: {Status} - {Body}", replayId, groupId, res.StatusCode, body);
            return false;
        }

        return true;
    }

    public async Task<(string? Id, bool AlreadyExisted)> Upload(string path, string? visibility = null)
    {
        var effective = string.IsNullOrWhiteSpace(visibility)
            ? string.IsNullOrWhiteSpace(_config.Visibility) ? "public" : _config.Visibility
            : visibility;

        using var form = new MultipartFormDataContent();
        using var file = File.OpenRead(path);
        form.Add(new StreamContent(file), "file", Path.GetFileName(path));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://ballchasing.com/api/v2/upload?visibility={effective}")
        {
            Content = form
        };
        ApplyAuth(request);

        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Error de red subiendo {Path}", path);
            throw new UploadTransientException(TranslationSource.Instance["Exc.Network"], ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning("Timeout subiendo {Path}", path);
            throw new UploadTransientException(TranslationSource.Instance["Exc.Timeout"], ex);
        }

        // Transitorios: rate-limit (429) y errores de servidor (5xx) -> se reintenta.
        if (res.StatusCode == HttpStatusCode.TooManyRequests || (int)res.StatusCode >= 500)
        {
            var errorBody = await res.Content.ReadAsStringAsync();
            _logger.LogWarning("Respuesta transitoria subiendo {Path}: {Status}", path, res.StatusCode);
            throw new UploadTransientException($"HTTP {(int)res.StatusCode}: {errorBody[..Math.Min(errorBody.Length, 200)]}");
        }

        // 201 = subida creada. 409 = replay duplicado, pero igual trae el id existente.
        if (res.StatusCode != HttpStatusCode.Created && res.StatusCode != HttpStatusCode.Conflict)
        {
            var errorBody = await res.Content.ReadAsStringAsync();
            _logger.LogError(
                "Fallo al subir {Path}: {Status} - {Body} (key: len={KeyLen} {KeyFingerprint})",
                path, res.StatusCode, errorBody, _config.BallchasingApiKey.Length, KeyFingerprint(_config.BallchasingApiKey));

            // Rechazo definitivo: sacar el motivo real del cuerpo para que la
            // interfaz pueda mostrarlo (p. ej. key inválida/caducada).
            var reason = ExtractError(errorBody);
            if (res.StatusCode == HttpStatusCode.Unauthorized ||
                res.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new UploadPermanentException(TranslationSource.Instance["Exc.ApiKeyRejected"]);
            }

            throw new UploadPermanentException(
                TranslationSource.Instance.Format("Exc.FileRejected", (int)res.StatusCode) +
                (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}"));
        }

        var json = await res.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<UploadResponse>(json);

        if (data?.Id == null)
        {
            _logger.LogError("Respuesta de subida sin id para {Path}: {Body}", path, json);
        }

        return (data?.Id, res.StatusCode == HttpStatusCode.Conflict);
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
        }
        catch (JsonException)
        {
            // el cuerpo no es JSON: no hay motivo que mostrar
        }

        return null;
    }

    // Huella de la key para diagnósticos (primeros/últimos 4 caracteres): nunca
    // registra la key completa.
    private static string KeyFingerprint(string key)
    {
        if (string.IsNullOrEmpty(key)) return "(vacía)";
        return key.Length <= 8 ? "(corta)" : $"{key[..4]}...{key[^4..]}";
    }

    public async Task<ReplayMetadata?> GetReplay(string id)
    {
        // Ballchasing procesa el replay de forma asíncrona: justo después de subirlo
        // puede devolver status "pending", así que reintentamos unos segundos.
        const int maxAttempts = 10;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://ballchasing.com/api/replays/{id}");
            ApplyAuth(request);

            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(request);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Error de red obteniendo el replay {Id}", id);
                return null;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Timeout obteniendo el replay {Id}", id);
                return null;
            }

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                _logger.LogError("Fallo al obtener replay {Id}: {Status} - {Body}", id, res.StatusCode, body);
                return null;
            }

            var json = await res.Content.ReadAsStringAsync();
            var meta = JsonSerializer.Deserialize<ReplayMetadata>(json);

            if (meta?.Status is null or "ok")
            {
                return meta;
            }

            if (meta.Status == "failed")
            {
                _logger.LogError("Ballchasing no pudo procesar el replay {Id}", id);
                return null;
            }

            // status == "pending" -> esperar y reintentar
            await Task.Delay(2000);
        }

        _logger.LogWarning("El replay {Id} sigue en estado pendiente tras {Attempts} intentos", id, maxAttempts);
        return null;
    }

    // Ballchasing no usa el nombre del archivo subido como título: usa el campo
    // interno "ReplayName" del .replay (normalmente vacío) o el id de la partida.
    // Por eso forzamos el título explícitamente después de subir.
    public async Task<bool> SetTitle(string id, string title)
    {
        var payload = JsonSerializer.Serialize(new { title });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Patch, $"https://ballchasing.com/api/replays/{id}")
        {
            Content = content
        };
        ApplyAuth(request);

        var res = await _http.SendAsync(request);

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            _logger.LogError("Fallo al poner título '{Title}' al replay {Id}: {Status} - {Body}", title, id, res.StatusCode, body);
            return false;
        }

        return true;
    }
}
