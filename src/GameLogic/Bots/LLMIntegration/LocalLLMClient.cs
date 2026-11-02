using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Offline;

namespace MUnique.OpenMU.GameLogic.Bots.LLMIntegration
{
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

                // Serializar correctamente con JsonSerializer - ESCAPA AUTOMÁTICAMENTE saltos de línea y comillas
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
                        ["enable_thinking"] = false // Qwen3.5: deshabilitar thinking para obtener content directo
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(request, JsonOptions);
                
                _logger.LogInformation("Generando respuesta LLM para '{BotName}' con mensaje: '{PlayerMessage}'", botName, playerMessage);
                _logger.LogDebug("Payload JSON enviado: {Payload}", jsonPayload.Substring(0, Math.Min(200, jsonPayload.Length)));

                using var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content);
                
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("LM Studio error HTTP ({Status}): {Body}", response.StatusCode, responseBody);
                    return "Che, mis pensamientos estan desordenados. Intenta de nuevo.";
                }

                // Deserializar respuesta correctamente
                var result = JsonSerializer.Deserialize<LLMResponseDto>(responseBody, JsonOptions);

                if (result?.Choices == null || result.Choices.Length == 0)
                {
                    _logger.LogWarning("LM Studio respondió sin choices: {Body}", responseBody);
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
                    return "Che, mis pensamientos estan desordenados. Intenta de nuevo.";
                }

                // Validar longitud mínima
                if (botResponse.Length < 5)
                {
                    _logger.LogWarning("Bot '{BotName}' respuesta muy corta: '{Content}'", botName, botResponse);
                    return "Che, mis pensamientos estan desordenados. Intenta de nuevo.";
                }

                _logger.LogInformation("Respuesta LLM generada exitosamente para '{BotName}': {Response}", botName, botResponse.Substring(0, Math.Min(100, botResponse.Length)));
                
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
            // Usar comillas dobles normales y evitar caracteres especiales en strings C#
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
            [System.Text.Json.Serialization.JsonPropertyName("role")]
            public string Role { get; set; } = "";
            
            [System.Text.Json.Serialization.JsonPropertyName("content")]
            public string Content { get; set; } = "";
        }
        private class LLMResponse
        {
            [System.Text.Json.Serialization.JsonPropertyName("choices")]
            public List<Choice> Choices { get; set; } = new();
        }

        private class Choice
        {
            [System.Text.Json.Serialization.JsonPropertyName("message")]
            public MessageData Message { get; set; } = new();
        }

        private class MessageData
        {
            [System.Text.Json.Serialization.JsonPropertyName("content")]
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

            // CRÍTICO: reasoning_effort="none" deshabilita el razonamiento en Qwen3.5 para LM Studio
            // enable_thinking=false NO funciona según pruebas verificadas con qwen/qwen3.5-35b-a3b
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

        // JsonSerializerOptions compartidos para serialización y deserialización
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private string SanitizeAndStripReasoning(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // Qwen3.5 incluye reasoning_content en <think> tags - extraer solo el contenido real
            var stripped = System.Text.RegularExpressions.Regex.Replace(text, @"</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"<think>.*?</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            
            // Sanizar: quitar prefijos no deseados y limitar longitud para paquete de chat C1 (max ~240 bytes)
            var sanitized = stripped.Trim();
            
            // Quitar prefijos comunes que rompen el chat
            var prefixesToRemove = new[] { "/", "~", "@", "$", "!" };
            foreach (var prefix in prefixesToRemove)
            {
                if (sanitized.StartsWith(prefix))
                {
                    sanitized = sanitized.Substring(1).Trim();
                }
            }

            // Partir en trozos de ≤60 chars si es muy largo (paquete C1 limita a ~240 bytes)
            const int MAX_CHUNK_SIZE = 60;
            if (sanitized.Length > MAX_CHUNK_SIZE)
            {
                sanitized = sanitized.Substring(0, MAX_CHUNK_SIZE);
                // Intentar cortar en palabra completa
                var lastSpace = sanitized.LastIndexOf(' ');
                if (lastSpace > MAX_CHUNK_SIZE * 0.7)
                {
                    sanitized = sanitized.Substring(0, lastSpace);
                }
            }

            return sanitized;
        }
    }

    internal static class DialogLogger
    {
        public static void Info(string m) => System.Console.WriteLine($"[DIALOG INFO] {m}");
        public static void Warn(string m) => System.Console.WriteLine($"[DIALOG WARN] {m}");
        public static void Error(string m) => System.Console.Error.WriteLine($"[DIALOG ERROR] {m}");
    }
}
