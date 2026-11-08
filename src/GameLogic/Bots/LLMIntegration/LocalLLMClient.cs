// <copyright file="LocalLLMClient.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots.LLMIntegration;

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Offline;
using System.Net.Http;

/// <summary>Cliente para interactuar con un LLM local (LM Studio).</summary>
public class LocalLLMClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _modelName;
    private readonly int _maxTokens;
    private readonly ILogger<LocalLLMClient> _logger;

    public class LLMConfig
    {
        public string BaseUrl { get; set; } = "http://localhost:1234";
        public string ModelName { get; set; } = "qwen/qwen3.5-35b-a3b";
        public int MaxTokens { get; set; } = 500;
        public float Temperature { get; set; } = 0.7f;
        /// <summary>Timeout inicial en segundos (PASO 1: 45s).</summary>
        public int TimeoutSeconds { get; set; } = 45;
        /// <summary>Retraso entre reintento (5 s).</summary>
        public int RetryDelaySeconds { get; set; } = 5;
    }

    private readonly LLMConfig _config;

    public LocalLLMClient(LLMConfig config, ILogger<LocalLLMClient> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _baseUrl = config.BaseUrl.TrimEnd('/');
        _modelName = config.ModelName;
        _maxTokens = config.MaxTokens;
        _logger = logger;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
        };
    }

    public async Task<string?> GenerateResponseAsync(
        string botName, 
        string personalityDescription, 
        string playerMessage,
        string? context = null)
    {
        var lastException = default(Exception);
        
        // PASO 1: Reintento automático si LM Studio está "unloaded" o timeout inicial
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var systemPrompt = BuildSystemPrompt(botName, personalityDescription);
                var userMessage = $"Jugador: {playerMessage}";

                if (!string.IsNullOrEmpty(context))
                {
                    context = "Contexto del juego. " + context;
                }

                var messagesArray = new[]
                {
                    new ChatMessageDto { Role = "system", Content = systemPrompt },
                    new ChatMessageDto { Role = "user", Content = (context ?? "") + userMessage }
                };

                var request = new LLMRequest
                {
                    Model = _modelName,
                    Messages = messagesArray,
                    MaxTokens = _maxTokens,
                    Temperature = _config.Temperature,
                    Stream = false,
                    ChatTemplateKwargs = new Dictionary<string, object> 
                    { 
                        ["enable_thinking"] = false
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(request, JsonOptions);
                
                if (attempt > 0)
                {
                    _logger.LogWarning("Reintento LLM para '{BotName}' después de 5s", botName);
                }
                
                using var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content);
                
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Verificar si es error de modelo "unloaded/loading" → reintento
                    var bodyLower = responseBody.ToLowerInvariant();
                    if (bodyLower.Contains("unloaded") || bodyLower.Contains("loading"))
                    {
                        _logger.LogWarning("LM Studio está descargando en intento {Attempt}", attempt + 1);
                        if (attempt < 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
                            continue; // Reintentar
                        }
                    }
                    
                    _logger.LogWarning("LM Studio error HTTP ({Status}): {Body}", response.StatusCode, responseBody);
                    DialogLogger.Error($"Error generando respuesta: LM Studio no respondió correctamente");
                    return "Che, mis pensamientos estan desordenados. Intenta de nuevo.";
                }

                var result = JsonSerializer.Deserialize<LLMResponseDto>(responseBody, JsonOptions);

                if (result?.Choices == null || result.Choices.Length == 0)
                {
                    _logger.LogWarning("LM Studio respondió sin choices: {Body}", responseBody);
                    DialogLogger.Error($"Error generando respuesta: Sin respuestas de LM Studio");
                    return "Che, mis pensamientos estan desordenados. Intenta de nuevo.";
                }

                var choice = result.Choices[0];
                
                if (choice.FinishReason == "length")
                {
                    _logger.LogWarning("LM Studio cortó respuesta por length - aumentar max_tokens");
                }

                var botResponse = SanitizeAndStripReasoning(choice.Message.Content);

                if (string.IsNullOrEmpty(botResponse))
                {
                    _logger.LogWarning("Bot '{BotName}' respuesta vacia después de sanitizar", botName);
                    DialogLogger.Error($"Error generando respuesta: Respuesta vacía");
                    return "Che, mis pensamientos estan desordenados. Intenta de nuevo.";
                }

                if (botResponse.Length < 5)
                {
                    _logger.LogWarning("Bot '{BotName}' respuesta muy corta", botName);
                    DialogLogger.Error($"Error generando respuesta: Respuesta muy corta");
                    return "Che, mis pensamientos estan desordenados. Intenta de nuevo.";
                }

                _logger.LogInformation("Respuesta LLM generada exitosamente para '{BotName}'", botName);
                
                return botResponse;
            }
            catch (TaskCanceledException) when (attempt < 1)
            {
                // Timeout inicial, reintentar
                lastException = new Exception($"Timeout en intento {attempt + 1}");
                _logger.LogWarning("LM Studio timeout en intento {Attempt}, reintentando...", attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(ex, "Error LLM para '{BotName}' en intento {Attempt}", botName, attempt + 1);
                
                // Solo reintentar si es error de conexión/red
                if (ex is HttpRequestException && attempt < 1)
                {
                    _logger.LogWarning("Reintentando después de error HTTP...");
                    await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
                    continue;
                }
                
                // Si no es reintentable, salir
                break;
            }
        }

        // Ningún intento funcionó
        DialogLogger.Error($"Error generando respuesta: {lastException?.Message}");
        return null;
    }

    private string BuildSystemPrompt(string botName, string personalityDescription)
    {
        var prompt = "Eres un NPC de MU Online con personalidad: " + personalityDescription + ".\n\n" +
                     "REGLAS IMPORTANTES DE LENGUAJE URUGUAYO:\n" +
                     "1. Responde SIEMPRE en espanol con modismos uruguayos autenticos (voseo, lunfardo rioplatense)\n" +
                     "2. Usar vos en lugar de tu (ej: vos tenes, no tu tienes)\n" +
                     "3. Incluir expresiones tipicas: che, boludo, buey, laburo, bondi, etc.\n" +
                     "4. Sere consistente con tu personalidad descrita arriba\n" +
                     "5. No menciones que eres una IA o AI\n" +
                     "6. Mantene respuestas concisas (2-3 oraciones)\n\n" +
                     "CONTEXTO DEL JUEGO:\n" +
                     "- Mundo: MU Online (fantasia oscura medieval estilo uruguayo)\n" +
                     "- Razas: Elfos, Humanos, Dark Elves, Mages, Knights, Wizards\n" +
                     "- Items: Anillos de diamante, Joyas, Wings\n" +
                     "- Zonas: Devias, Valhalla, Netherworld\n\n" +
                     "EJEMPLOS DE LENGUAJE URUGUAYO APROPIADO:\n" +
                     "- Che que pasa! Que queres ahora?\n" +
                     "- Boludo no me vengas con esas historias.\n" +
                     "- Buey esto es serio, no es para jugar.\n" +
                     "- No seas boludo y anda al punto.\n\n" +
                     "Ahora responde como " + botName + " manteniendo tu rol inmersivo con acento uruguayo autentico.";
        
        return prompt;
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/v1/models");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private class LLMResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; set; } = new();
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public MessageData Message { get; set; } = new();
    }

    private class MessageData
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    // DTOs para serialización correcta del payload LLM
    private class ChatMessageDto
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private class LLMRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";
        
        [JsonPropertyName("messages")]
        public ChatMessageDto[] Messages { get; set; } = Array.Empty<ChatMessageDto>();
        
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 150;
        
        [JsonPropertyName("temperature")]
        public float Temperature { get; set; } = 0.7f;
        
        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;

        [JsonPropertyName("chat_template_kwargs")]
        public Dictionary<string, object>? ChatTemplateKwargs { get; set; }

        [JsonPropertyName("reasoning_effort")]
        public string ReasoningEffort { get; set; } = "none";

        [JsonPropertyName("response_format")]
        public object? ResponseFormat { get; set; }
    }

    private class LLMResponseDto
    {
        [JsonPropertyName("choices")]
        public ChoiceDto[] Choices { get; set; } = Array.Empty<ChoiceDto>();
    }

    private class ChoiceDto
    {
        [JsonPropertyName("message")]
        public MessageDto Message { get; set; } = new();
        
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class MessageDto
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static object CreateIntentSchema(IReadOnlyList<string>? targets)
    {
        var properties = new Dictionary<string, object>
        {
            ["say"] = new { type = "string" },
            ["action"] = new { type = "string", @enum = new[] { "none", "follow", "guide", "fight", "stop" } },
            ["place"] = new { type = "string", @enum = new[] { "", "lorencia", "devias", "noria" } },
        };
        if (targets is not null)
        {
            properties["target"] = new { type = "string", @enum = targets.Append(string.Empty).Distinct().ToArray() };
        }

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "npc_intent",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties,
                    required = properties.Keys.ToArray(),
                    additionalProperties = false,
                },
            },
        };
    }

    private string SanitizeAndStripReasoning(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        var stripped = System.Text.RegularExpressions.Regex.Replace(text, @"</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"<think>.*?</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        
        var sanitized = stripped.Trim();
        
        var prefixesToRemove = new[] { "/", "~", "@", "$", "!" };
        foreach (var prefix in prefixesToRemove)
        {
            if (sanitized.StartsWith(prefix))
            {
                sanitized = sanitized.Substring(1).Trim();
            }
        }

        const int MAX_CHUNK_SIZE = 60;
        if (sanitized.Length > MAX_CHUNK_SIZE)
        {
            sanitized = sanitized.Substring(0, MAX_CHUNK_SIZE);
            var lastSpace = sanitized.LastIndexOf(' ');
            if (lastSpace > MAX_CHUNK_SIZE * 0.7)
            {
                sanitized = sanitized.Substring(0, lastSpace);
            }
        }

        return sanitized;
    }

    /// <summary>Manda system prompt + historial y devuelve la respuesta limpia, o null si falla.</summary>
    public async Task<string?> ChatAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns, string botName, CancellationToken cancellationToken = default)
    {
        var lastException = default(Exception);
        
        // PASO 1 (corregido): Reintento automático si LM Studio está "unloaded"/"loading" o timeout
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var messages = new List<ChatMessageDto> { new() { Role = "system", Content = systemPrompt } };
                messages.AddRange(turns.Select(t => new ChatMessageDto { Role = t.Role, Content = t.Content }));

                var request = new LLMRequest
                {
                    Model = _modelName,
                    Messages = messages.ToArray(),
                    MaxTokens = _maxTokens,
                    Temperature = _config.Temperature,
                    Stream = false,
                    ChatTemplateKwargs = new Dictionary<string, object> { ["enable_thinking"] = false },
                };

                var json = JsonSerializer.Serialize(request, JsonOptions);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // Verificar si es error de modelo "unloaded/loading" → reintento
                    var bodyLower = body.ToLowerInvariant();
                    if (bodyLower.Contains("unloaded") || bodyLower.Contains("loading"))
                    {
                        _logger.LogWarning("LM Studio está descargando en intento {Attempt}", attempt + 1);
                        if (attempt < 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
                            continue; // Reintentar
                        }
                    }

                    _logger.LogWarning("LM Studio HTTP {Status}: {Body}", response.StatusCode, body);
                    return null;
                }

                var result = JsonSerializer.Deserialize<LLMResponseDto>(body, JsonOptions);
                var raw = result?.Choices.FirstOrDefault()?.Message.Content;
                var clean = ChatText.Clean(raw, botName);
                _logger.LogInformation("LLM {Bot} <- '{Last}' -> '{Reply}'", botName, turns.LastOrDefault()?.Content, clean);
                return string.IsNullOrWhiteSpace(clean) ? null : clean;
            }
            catch (TaskCanceledException) when (attempt < 1)
            {
                // Timeout inicial, reintentar
                lastException = new Exception($"Timeout en intento {attempt + 1}");
                _logger.LogWarning("LM Studio timeout en intento {Attempt}, reintentando...", attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(ex, "LLM fallo para '{BotName}' en intento {Attempt}", botName, attempt + 1);

                // Solo reintentar si es error de conexión/red
                if (ex is HttpRequestException && attempt < 1)
                {
                    _logger.LogWarning("Reintentando después de error HTTP...");
                    await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
                    continue;
                }

                // Si no es reintentable, salir
                break;
            }
        }

        // Ningún intento funcionó
        _logger.LogError(lastException, "LLM fallo para {BotName} después de reintento", botName);
        return null;
    }

    /// <summary>Cómo ChatAsync, pero el modelo responde JSON {say, action, place}. Null si falla.</summary>
    public Task<NpcIntent?> ChatIntentAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns, string botName, CancellationToken cancellationToken = default)
        => this.ChatIntentAsync(systemPrompt, turns, botName, null, cancellationToken);

    /// <summary>Le pregunta al modelo qué actividad hacer ahora (JSON: activity, map, target, say, reason). Null si falla.</summary>
    public async Task<NpcPlan?> PlanActivityAsync(string systemPrompt, string botName, IReadOnlyList<string> targets, CancellationToken cancellationToken = default)
    {
        var schema = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "npc_plan",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["activity"] = new { type = "string", @enum = new[] { "GuardGate", "Patrol", "Hunt", "Rest", "SeekPlayer", "ChallengeRival" } },
                        ["map"] = new { type = "string", @enum = new[] { "", "lorencia", "devias", "noria" } },
                        ["target"] = new { type = "string", @enum = targets.Append(string.Empty).Distinct().ToArray() },
                        ["say"] = new { type = "string" },
                        ["reason"] = new { type = "string" },
                    },
                    required = new[] { "activity", "map", "target", "say", "reason" },
                    additionalProperties = false,
                },
            },
        };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var request = new LLMRequest
                {
                    Model = _modelName,
                    Messages = new[]
                    {
                        new ChatMessageDto { Role = "system", Content = systemPrompt },
                        new ChatMessageDto { Role = "user", Content = "¿Qué hacés ahora?" },
                    },
                    MaxTokens = 200,
                    Temperature = _config.Temperature,
                    Stream = false,
                    ChatTemplateKwargs = new Dictionary<string, object> { ["enable_thinking"] = false },
                    ResponseFormat = schema,
                };

                var json = JsonSerializer.Serialize(request, JsonOptions);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var lower = body.ToLowerInvariant();
                    if (attempt == 0 && (lower.Contains("unloaded") || lower.Contains("loading")))
                    {
                        _logger.LogWarning("Reintento LLM (plan) para {Bot}: el modelo no está cargado", botName);
                        await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    _logger.LogWarning("LM Studio HTTP {Status} (plan): {Body}", response.StatusCode, body);
                    return null;
                }

                var raw = JsonSerializer.Deserialize<LLMResponseDto>(body, JsonOptions)?.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"<think>[\s\S]*?</think>", string.Empty).Trim();
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                string Read(string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
                if (!Enum.TryParse<NpcActivityKind>(Read("activity"), true, out var activity))
                {
                    return null;
                }

                return new NpcPlan(activity, Read("map"), Read("target"), ChatText.Clean(Read("say"), botName), Read("reason"));
            }
            catch (TaskCanceledException) when (attempt == 0 && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Reintento LLM (plan) para {Bot}: timeout", botName);
                await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plan LLM fallo para {Bot}", botName);
                return null;
            }
        }

        return null;
    }

    /// <summary>Requests an autonomous intent with a target restricted to the supplied nearby humans.</summary>
    /// <param name="systemPrompt">The captured world situation.</param>
    /// <param name="turns">The request turns.</param>
    /// <param name="botName">The speaker name.</param>
    /// <param name="targets">Allowed human names; null preserves the chat schema.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The proposed intent, or null on failure.</returns>
    public async Task<NpcIntent?> ChatIntentAsync(string systemPrompt, IReadOnlyList<ChatTurn> turns, string botName, IReadOnlyList<string>? targets, CancellationToken cancellationToken = default)
    {
        var lastException = default(Exception);
        
        // PASO 1 (corregido): Reintento automático si LM Studio está "unloaded"/"loading" o timeout
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var messages = new List<ChatMessageDto> { new() { Role = "system", Content = systemPrompt } };
                messages.AddRange(turns.Select(t => new ChatMessageDto { Role = t.Role, Content = t.Content }));

                var request = new LLMRequest
                {
                    Model = _modelName,
                    Messages = messages.ToArray(),
                    MaxTokens = _maxTokens,
                    Temperature = _config.Temperature,
                    Stream = false,
                    ChatTemplateKwargs = new Dictionary<string, object> { ["enable_thinking"] = false },
                    ResponseFormat = CreateIntentSchema(targets),
                };

                var json = JsonSerializer.Serialize(request, JsonOptions);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // Verificar si es error de modelo "unloaded/loading" → reintento
                    var bodyLower = body.ToLowerInvariant();
                    if (bodyLower.Contains("unloaded") || bodyLower.Contains("loading"))
                    {
                        _logger.LogWarning("LM Studio está descargando en intento {Attempt}", attempt + 1);
                        if (attempt < 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
                            continue; // Reintentar
                        }
                    }
                    
                    _logger.LogWarning("LM Studio HTTP {Status}: {Body}", response.StatusCode, body);
                    return null;
                }

                var result = JsonSerializer.Deserialize<LLMResponseDto>(body, JsonOptions);
                var raw = result?.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline).Trim();

                NpcIntent intent;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    var say = root.TryGetProperty("say", out var s) ? s.GetString() : null;
                    var actionText = root.TryGetProperty("action", out var a) ? a.GetString() : null;
                    var place = root.TryGetProperty("place", out var p) ? p.GetString() : null;
                    var action = Enum.TryParse<NpcActionKind>(actionText, true, out var parsed) ? parsed : NpcActionKind.None;
                    var target = root.TryGetProperty("target", out var t) ? t.GetString() : null;
                    intent = new NpcIntent(ChatText.Clean(say, botName), action, place) { Target = target ?? string.Empty };
                }
                catch (JsonException)
                {
                    // El modelo no respetó el JSON: usar todo como texto y no hacer nada.
                    intent = new NpcIntent(ChatText.Clean(raw, botName), NpcActionKind.None, null);
                }

                _logger.LogInformation("LLM {Bot} <- '{Last}' -> say='{Say}' action={Action} place={Place}", botName, turns.LastOrDefault()?.Content, intent.Say, intent.Action, intent.Place);
                return intent;
            }
            catch (TaskCanceledException) when (attempt < 1)
            {
                // Timeout inicial, reintentar
                lastException = new Exception($"Timeout en intento {attempt + 1}");
                _logger.LogWarning("LM Studio timeout en intento {Attempt}, reintentando...", attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(ex, "LLM fallo para '{BotName}' en intento {Attempt}", botName, attempt + 1);
                
                // Solo reintentar si es error de conexión/red
                if (ex is HttpRequestException && attempt < 1)
                {
                    _logger.LogWarning("Reintentando después de error HTTP...");
                    await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));
                    continue;
                }
                
                // Si no es reintentable, salir
                break;
            }
        }

        // Ningún intento funcionó
        _logger.LogError(lastException, "LLM fallo para {BotName} después de reintento", botName);
        return null;
    }

    internal static class DialogLogger
    {
        public static void Info(string message) => System.Console.WriteLine($"[DIALOG INFO] {message}");
        public static void Warn(string message) => System.Console.WriteLine($"[DIALOG WARN] {message}");
        public static void Error(string message) => System.Console.Error.WriteLine($"[DIALOG ERROR] {message}");
    }
}
