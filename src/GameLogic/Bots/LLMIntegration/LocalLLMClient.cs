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
        public int TimeoutSeconds { get; set; } = 30;
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
            
            _logger.LogInformation("Generando respuesta LLM para '{BotName}' con mensaje: '{PlayerMessage}'", botName, playerMessage);
            
            using var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content);
            
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
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
        catch (Exception ex)
        {
            DialogLogger.Error($"Error generando respuesta: {ex.Message}");
            return null;
        }
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
                _logger.LogWarning("LM Studio HTTP {Status}: {Body}", response.StatusCode, body);
                return null;
            }

            var result = JsonSerializer.Deserialize<LLMResponseDto>(body, JsonOptions);
            var raw = result?.Choices.FirstOrDefault()?.Message.Content;
            var clean = ChatText.Clean(raw, botName);
            _logger.LogInformation("LLM {Bot} <- '{Last}' -> '{Reply}'", botName, turns.LastOrDefault()?.Content, clean);
            return string.IsNullOrWhiteSpace(clean) ? null : clean;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM fallo para {Bot}", botName);
            DialogLogger.Error($"Error en ChatAsync: {ex.Message}");
            return null;
        }
    }

    internal static class DialogLogger
    {
        public static void Info(string message) => System.Console.WriteLine($"[DIALOG INFO] {message}");
        public static void Warn(string message) => System.Console.WriteLine($"[DIALOG WARN] {message}");
        public static void Error(string message) => System.Console.Error.WriteLine($"[DIALOG ERROR] {message}");
    }
}
