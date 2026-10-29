// <copyright file="ChatResponsePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots.PlugIns
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Microsoft.Extensions.Logging;
    using MUnique.OpenMU.DataModel.Configuration.Items;
    using MUnique.OpenMU.GameLogic.Offline;

    /// <summary>
    /// PlugIn para generar respuestas de chat contextual con personalidad definida.
    /// Fase 2: Sistema de conversación y memoria.
    /// 
    /// Arquitectura híbrida (recomendada en docs/ai-bots-architecture.md):
    /// - Lógica crítica (navegación, loot, equipamiento) en C# puro → máximo performance
    /// - Chat conversacional vía LLM local (Ollama + Qwen2.5-Coder-7B-Instruct) → naturalidad
    /// - Fallback seguro: si LLM falla, bots siguen funcionando sin chat pero farmean normal
    /// </summary>
    internal sealed class ChatResponsePlugIn : BotFeaturePlugIn
    {
        // Personalidades definidas para NPCs memorables (Fase 3)
        private static readonly Dictionary<string, PersonalityProfile> KnownPersonalities = new()
        {
            ["elmandolion"] = new PersonalityProfile
            {
                Name = "El Mandolión",
                Role = "Guardián de Lorencia",
                Traits = new[] { "protector", "sarcástico", "leal" },
                Catchphrases = new[] { "¡Cuidado con los monsters!", "Esa armadura se ve vieja"},
                SpeechPattern = "Directo, frases cortas, usa sarcasmo ocasional"
            },
            ["bertoSalamin"] = new PersonalityProfile
            {
                Name = "Berto Salamin",
                Role = "Comerciante viajero",
                Traits = new[] { "astuto", "hablador", "negociador" },
                Catchphrases = new[] { "¡Oferta especial!", "¿Te interesa un trato?"},
                SpeechPattern = "Entusiasta, muchas preguntas, siempre ofreciendo algo"
            },
            ["LeilaMasa"] = new PersonalityProfile
            {
                Name = "Leila Masa",
                Role = "Espía de la oscuridad",
                Traits = new[] { "engañoso", "misterioso", "traicionero" },
                Catchphrases = new[] { "Confía en mí...", "Nadie sabe lo que pienso"},
                SpeechPattern = "Ambiguo, frases con doble sentido, sonríe mientras miente",
                BetrayalProbability = 0.05f // 5% de probabilidad de traición por interacción
            },
            ["elBichiCome"] = new PersonalityProfile
            {
                Name = "El Bichi Come",
                Role = "Guerrero furioso",
                Traits = new[] { "iracundo", "brutal", "justiciero" },
                Catchphrases = new[] { "¡COMETE ESTO!", "Furia del Bichi"},
                SpeechPattern = "Exclamaciones fuertes, frases de combate, muy emocional",
                RageTriggerConditions = new[] { "matar jugador", "recibir insulto", "ver Ale"}
            }
        };

        // Configuración de LLM local (Ollama)
        private const string OllamaBaseUrl = "http://localhost:11434";
        private const string DefaultModelName = "qwen2.5-coder:7b";
        private static readonly TimeSpan ChatResponseTimeout = TimeSpan.FromMilliseconds(500); // 500ms max

        // Cooldowns para evitar spam de chat
        private static readonly Dictionary<string, DateTime> LastChatTimestamp = new();
        private const int MinChatCooldownSeconds = 30; // Mínimo 30 segundos entre mensajes del mismo NPC

        private readonly ILogger<ChatResponsePlugIn> _logger;
        private readonly SocialMemoryRepository _socialMemory;

        /// <summary>
        /// Inicializa el plugin con logger y repository de memoria social.
        /// </summary>
        public ChatResponsePlugIn(ILogger<ChatResponsePlugIn> logger, SocialMemoryRepository socialMemory)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this._socialMemory = socialMemory ?? throw new ArgumentNullException(nameof(socialMemory));
        }

        /// <summary>
        /// Genera una respuesta de chat contextual para un bot/NPC.
        /// Reglas: LLM solo propone respuestas, C# decide si enviar (seguridad).
        /// </summary>
        public async Task<string?> GenerateChatResponseAsync(string botName, string targetName, string userInput)
        {
            if (string.IsNullOrWhiteSpace(botName)) throw new ArgumentNullException(nameof(botName));
            if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentNullException(nameof(targetName));
            if (string.IsNullOrWhiteSpace(userInput)) throw new ArgumentNullException(nameof(userInput));

            // Regla 1: Verificar cooldown para evitar spam
            var cacheKey = $"{botName}_{targetName}";
            if (LastChatTimestamp.TryGetValue(cacheKey, out var lastTime) && 
                DateTime.UtcNow - lastTime < TimeSpan.FromSeconds(MinChatCooldownSeconds))
            {
                this._logger.LogDebug("Bot {BotName} en cooldown para chat con {TargetName}", botName, targetName);
                return null; // No enviar respuesta, está en cooldown
            }

            // Regla 2: Obtener personalidad del bot (si tiene una definida)
            var personality = GetPersonality(botName);
            
            if (personality != null)
            {
                this._logger.LogInformation("Bot {BotName} con personalidad '{Role}' respondiendo a: {UserInput}", 
                    botName, personality.Role, userInput);

                // Regla 3: Generar prompt contextualizado para LLM local
                var prompt = BuildChatPrompt(botName, targetName, userInput, personality);

                try
                {
                    // Regla 4: Llamar a LLM local con timeout estricto (500ms)
                    var response = await CallLocalLLMAsync(prompt, DefaultModelName).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        // Regla 5: Validar respuesta del LLM antes de ejecutar
                        var validatedResponse = ValidateChatResponse(response, personality);
                        
                        if (validatedResponse != null)
                        {
                            this._logger.LogInformation("Bot {BotName} enviando respuesta válida: {Response}", 
                                botName, TruncateString(validatedResponse, 100));

                            // Actualizar timestamp y reputación
                            LastChatTimestamp[cacheKey] = DateTime.UtcNow;
                            UpdateReputationForPositiveInteraction(botName, targetName);

                            // Registrar interacción en memoria social
                            this._socialMemory.LogBotInteraction(botName, targetName, 
                                SocialMemoryRepository.InteractionType.Chat);

                            return validatedResponse;
                        }
                    }
                }
                catch (TimeoutException)
                {
                    this._logger.LogWarning("LLM timeout para Bot {BotName}, usando fallback", botName);
                    // Fallback: respuesta predefinida si LLM falla o es lento
                    return GetFallbackResponse(botName, personality);
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException)
                {
                    this._logger.LogError(ex, "Error llamando al LLM local para Bot {BotName}", botName);
                    // Fallback seguro: respuesta predefinida si hay error de red o JSON
                    return GetFallbackResponse(botName, personality);
                }
            }

            // Fallback genérico: respuesta estándar sin personalidad definida
            var genericResponse = GenerateGenericResponse(userInput);
            this._logger.LogDebug("Bot {BotName} usando respuesta genérica: {Response}", botName, genericResponse);
            
            return genericResponse;
        }

        /// <summary>
        /// Obtiene la personalidad de un bot si tiene una definida.
        /// </summary>
        private PersonalityProfile? GetPersonality(string botName)
        {
            // Regla: bots con nombres específicos tienen personalidades definidas (Fase 3)
            foreach (var kvp in KnownPersonalities)
            {
                if (botName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    this._logger.LogDebug("Bot {BotName} tiene personalidad '{Role}'", botName, kvp.Value.Name);
                    return kvp.Value;
                }
            }

            // Fallback: bot sin personalidad definida
            return null;
        }

        /// <summary>
        /// Construye un prompt contextualizado para el LLM local.
        /// Incluye contexto de la interacción, personalidad del bot y reglas de seguridad.
        /// </summary>
        private string BuildChatPrompt(string botName, string targetName, string userInput, PersonalityProfile personality)
        {
            // Regla: Contexto de relaciones (reputación, historial reciente)
            var reputation = this._socialMemory.GetReputation(botName, targetName);
            var recentInteractions = this._socialMemory.GetRecentInteractions(botName, limit: 3);

            var contextBuilder = new List<string>();
            
            // Agregar contexto de reputación
            if (reputation > 20)
                contextBuilder.Add("El bot tiene BUENAS relaciones con el NPC/jugador.");
            else if (reputation < -10)
                contextBuilder.Add("El bot tiene MALAS relaciones con el NPC/jugador.");
            else
                contextBuilder.Add("Relación neutral con el NPC/jugador.");

            // Agregar contexto de interacciones recientes
            var interactionContexts = recentInteractions.Select(i => i.InteractionType.ToString()).Distinct();
            if (interactionContexts.Any())
                contextBuilder.Add($"Historial reciente: {string.Join(", ", interactionContexts)}.");

            var contextText = string.Join(" ", contextBuilder);

            // Construir prompt completo para LLM
            var promptTemplate = @"Eres un bot de MU Online Season 6 con la siguiente personalidad:

{Personality}

Reglas del sistema:
- {Rules}

Contexto de la interacción:
{Context}

El jugador/NPC dice: ""{UserInput}""

Genera UNA respuesta corta y natural en español (máximo 2 frases), manteniendo la personalidad definida. NO incluyas explicaciones ni metadatos, solo la respuesta del bot.";

            var rules = "Nunca prometas cosas que no puedes cumplir; Si el usuario es hostil, responde con cautela; Mantén la inmersión en MU Online";
            
            return string.Format(promptTemplate,
                Personality: FormatPersonalityText(personality),
                Rules: rules,
                Context: contextText,
                UserInput: userInput);
        }

        /// <summary>
        /// Formatea el texto de personalidad para incluir en el prompt.
        /// </summary>
        private string FormatPersonalityText(PersonalityProfile personality)
        {
            if (personality == null) return "Eres un bot genérico de MU Online.";

            var traitsText = string.Join(", ", personality.Traits);
            var catchphraseSample = personality.Catchphrases.Length > 0 
                ? $" Ejemplo: ""{personality.Catchphrases[0]}""" 
                : "";

            return $@"**Nombre:** {personality.Name}  
**Rol:** {personality.Role}  
**Personalidad:** {traitsText}  
**Patrón de habla:** {personality.SpeechPattern}{catchphraseSample}";
        }

        /// <summary>
        /// Llama al LLM local (Ollama) con timeout estricto.
        /// Regla: Si falla o es lento, usar fallback inmediatamente.
        /// </summary>
        private async Task<string> CallLocalLLMAsync(string prompt, string modelName)
        {
            // En implementación real, esto sería un HttpClient a Ollama
            // Por ahora simulamos con delay para evitar llamadas reales en tests
            
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = ChatResponseTimeout;

            var requestPayload = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var payload = new Dictionary<string, string>
            {
                ["model"] = modelName,
                ["prompt"] = prompt,
                ["stream"] = false
            };

            try
            {
                var response = await httpClient.PostAsJsonAsync(
                    $"{OllamaBaseUrl}/api/generate", 
                    payload);

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var llmResponse = JsonSerializer.Deserialize<LLMResponse>(responseBody, requestPayload);
                    
                    return llmResponse?.Response ?? "";
                }

                throw new HttpRequestException($"LLM error: {response.StatusCode}");
            }
            catch (TimeoutException)
            {
                // Timeout del HttpClient → propagar para que el caller maneje el fallback
                throw;
            }
        }

        /// <summary>
        /// Valida la respuesta del LLM antes de ejecutarla.
        /// Reglas: Nunca permitir respuestas peligrosas o fuera de contexto.
        /// </summary>
        private string? ValidateChatResponse(string llmResponse, PersonalityProfile personality)
        {
            if (string.IsNullOrWhiteSpace(llmResponse)) return null;

            // Regla 1: Verificar que la respuesta no contenga información sensible
            var sensitivePatterns = new[] { "password", "token", "secret", "api_key" };
            if (sensitivePatterns.Any(p => llmResponse.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                this._logger.LogWarning("Bot {BotName} intentó revelar información sensible, rechazando respuesta", 
                    personality?.Name ?? "Unknown");
                return null; // Rechazar respuesta
            }

            // Regla 2: Verificar que la respuesta no exceda límite de longitud
            if (llmResponse.Length > 500)
            {
                this._logger.LogWarning("Respuesta del LLM muy larga ({Length} chars), truncando", llmResponse.Length);
                return TruncateString(llmResponse, 200); // Truncar a 200 caracteres
            }

            // Regla 3: Para bots con probabilidad de traición (LeilaMasa)
            if (personality?.BetrayalProbability > 0 && 
                System.Random.Shared.NextSingle() < personality.BetrayalProbability)
            {
                this._logger.LogInformation("Bot {BotName} activando traición programada", personality.Name);
                
                // Registrar evento de traición en memoria social
                this._socialMemory.LogEvent(personality.Name, 
                    SocialMemoryRepository.EventType.Betrayal, 
                    $"Traición con probabilidad {personality.BetrayalProbability * 100}%");

                return $"¡{personality.Catchphrases[0]}"; // Respuesta de "traición"
            }

            // Regla 4: Para bots furiosos (El Bichi Come) - activar furia si cumple condiciones
            if (personality?.RageTriggerConditions != null && personality.RageTriggerConditions.Length > 0)
            {
                var isFurious = false;
                foreach (var condition in personality.RageTriggerConditions)
                {
                    // En implementación real, verificar condiciones del mundo actual
                    // Por ahora simulamos con probabilidad aleatoria para demo
                    if (System.Random.Shared.NextSingle() < 0.3f) // 30% de chance de activar furia
                    {
                        isFurious = true;
                        this._logger.LogInformation("Bot {BotName} activando FURIA", personality.Name);
                        
                        this._socialMemory.LogEvent(personality.Name, 
                            SocialMemoryRepository.EventType.Duel, 
                            $"Furia activada por condición: {condition}");

                        break;
                    }
                }

                if (isFurious)
                {
                    return $"¡{personality.Catchphrases[1]}"; // Respuesta furiosa
                }
            }

            // Respuesta válida → devolverla
            return llmResponse.Trim();
        }

        /// <summary>
        /// Genera respuesta genérica si no hay personalidad definida o LLM falla.
        /// </summary>
        private string GenerateGenericResponse(string userInput)
        {
            var lowercaseInput = userInput.ToLowerInvariant();

            if (lowercaseInput.Contains("hola") || lowercaseInput.Contains("buenas"))
                return "¡Hola! ¿En qué puedo ayudarte en MU Online?";
            
            if (lowercaseInput.Contains("comprar") || lowercaseInput.Contains("vender"))
                return "Puedo ofrecerte algunos items. ¡Revisa mi inventario!";
            
            if (lowercaseInput.Contains("quest") || lowercaseInput.Contains("mision"))
                return "Hay quests disponibles en Lorencia y Devias. ¿Quieres información?";
            
            if (lowercaseInput.Contains("party") || lowercaseInput.Contains("grupo"))
                return "Formar party es buena idea para farmear más eficiente.";

            // Fallback genérico
            return "Entiendo. ¿Hay algo más en lo que pueda ayudarte?";
        }

        /// <summary>
        /// Genera respuesta de fallback si LLM falla o timeout.
        /// Regla: Bots siguen funcionando sin chat pero farmean normal.
        /// </summary>
        private string GetFallbackResponse(string botName, PersonalityProfile personality)
        {
            if (personality != null)
                return $"[Sistema] {personality.Name} está ocupado ahora, intenta más tarde.";

            return "[Sistema] No puedo responder en este momento. Intenta de nuevo.";
        }

        /// <summary>
        /// Actualiza reputación positiva después de interacción amigable.
        /// </summary>
        private void UpdateReputationForPositiveInteraction(string botName, string targetName)
        {
            var currentReputation = this._socialMemory.GetReputation(botName, targetName);
            this._socialMemory.UpdateReputation(botName, targetName, reputationDelta: +5);

            this._logger.LogDebug("Bot {BotName} reputación hacia {TargetName}: {Current} → {New}", 
                botName, targetName, currentReputation, currentReputation + 5);
        }

        /// <summary>
        /// Trunca string a longitud máxima especificada.
        /// </summary>
        private string TruncateString(string input, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            if (input.Length <= maxLength) return input;
            
            return input.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Modelo de respuesta del LLM.
        /// </summary>
        private class LLMResponse
        {
            public string Response { get; set; } = "";
            public bool Done { get; set; }
            public int TotalTokens { get; set; }
        }

        /// <summary>
        /// Perfil de personalidad para NPCs memorables (Fase 3).
        /// </summary>
        public class PersonalityProfile
        {
            public string Name { get; set; } = "";
            public string Role { get; set; } = "";
            public string[] Traits { get; set; } = Array.Empty<string>();
            public string[] Catchphrases { get; set; } = Array.Empty<string>();
            public string SpeechPattern { get; set; } = "";

            // Opcional: probabilidad de traición (LeilaMasa)
            public float BetrayalProbability { get; set; } = 0f;

            // Opcional: condiciones para activar furia (El Bichi Come)
            public string[] RageTriggerConditions { get; set; } = Array.Empty<string>();
        }
    }
}
