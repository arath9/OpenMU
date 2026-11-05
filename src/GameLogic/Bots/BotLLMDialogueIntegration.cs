using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Bots.LLMIntegration;
using MUnique.OpenMU.DataModel.Configuration;

namespace MUnique.OpenMU.GameLogic.Bots
{
    /// <summary>
    /// Enum para modos de diálogo disponibles.
    /// </summary>
    public enum DialogueMode
    {
        /// <summary>Opciones numeradas guiadas por YarnSpinner (default)</summary>
        Guided = 0,

        /// <summary>Lenguaje natural con LLM local</summary>
        LLMNatural = 1,

        /// <summary>Híbrido: inicia con opciones, luego permite texto libre</summary>
        Hybrid = 2
    }

    /// <summary>
    /// Configuración de diálogo para un bot protagonista.
    /// </summary>
    public class BotDialogueConfig
    {
        /// <summary>Modo de diálogo preferido (default: Guided)</summary>
        public DialogueMode PreferredMode { get; set; } = DialogueMode.Guided;

        /// <summary>Personalidad del bot para LLM (solo usado en modo LLMNatural/Hybrid)</summary>
        public string PersonalityDescription { get; set; } = "NPC estándar de MU Online";

        /// <summary>Nombre del modelo LLM a usar (null = usa el cargado por defecto en LM Studio)</summary>
        public string? CustomLLMModelName { get; set; } = null;

        /// <summary>Frecuencia mínima entre respuestas del bot (cooldown en segundos, default: 30)</summary>
        public int ResponseCooldownSeconds { get; set; } = 30;

        /// <summary>Si true, el bot solo responde a saludos iniciales específicos</summary>
        public bool GreetingOnlyMode { get; set; } = false;

        /// <summary>Prioridad de bot (más alto = más probable que inicie diálogo primero)</summary>
        public int Priority { get; set; } = 0;
    }

    /// <summary>
    /// Integrador de diálogos LLM para bots protagonistas.
    /// Proporciona capa entre BotProtagonist y el cliente LLM local.
    /// </summary>
    public class BotLLMDialogueIntegration : IDisposable
    {
        private readonly LocalLLMClient _llmClient;
        private readonly ILogger<BotLLMDialogueIntegration>? _logger;
        private readonly Dictionary<string, List<ChatTurn>> _history = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastSeen = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lockObj = new();
        private bool _disposed;

        public BotLLMDialogueIntegration(LocalLLMClient.LLMConfig config, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<BotLLMDialogueIntegration>();
            _llmClient = new LocalLLMClient(config, loggerFactory.CreateLogger<LocalLLMClient>());
            _logger.LogInformation("LLM listo: {Url} modelo {Model}", config.BaseUrl, config.ModelName);
        }

        /// <summary>Responde a un jugador recordando la charla (últimos 8 intercambios, se olvida a los 5 min).</summary>
        public async Task<string?> ReplyAsync(string botName, string playerName, string systemPrompt, string playerMessage)
        {
            List<ChatTurn> turns;
            lock (_lockObj)
            {
                var now = DateTime.UtcNow;
                if (!_history.TryGetValue(playerName, out var h)
                    || now - _lastSeen.GetValueOrDefault(playerName) > TimeSpan.FromMinutes(5))
                {
                    h = new List<ChatTurn>();
                    _history[playerName] = h;
                }

                _lastSeen[playerName] = now;
                turns = new List<ChatTurn>(h) { new("user", playerMessage) };
            }

            var reply = await _llmClient.ChatAsync(systemPrompt, turns, botName).ConfigureAwait(false);
            if (reply is null)
            {
                return null;
            }

            lock (_lockObj)
            {
                var h = _history[playerName];
                h.Add(new ChatTurn("user", playerMessage));
                h.Add(new ChatTurn("assistant", reply));
                if (h.Count > 16)
                {
                    h.RemoveRange(0, h.Count - 16);
                }
            }

            return reply;
        }

        /// <summary>Cómo ReplyAsync, pero devuelve lo que dice Y lo que quiere hacer.</summary>
        public async Task<NpcIntent?> ReplyIntentAsync(string botName, string playerName, string systemPrompt, string playerMessage)
        {
            List<ChatTurn> turns;
            lock (_lockObj)
            {
                var now = DateTime.UtcNow;
                if (!_history.TryGetValue(playerName, out var h)
                    || now - _lastSeen.GetValueOrDefault(playerName) > TimeSpan.FromMinutes(5))
                {
                    h = new List<ChatTurn>();
                    _history[playerName] = h;
                }

                _lastSeen[playerName] = now;
                turns = new List<ChatTurn>(h) { new("user", playerMessage) };
            }

            var intent = await _llmClient.ChatIntentAsync(systemPrompt, turns, botName).ConfigureAwait(false);
            if (intent is null)
            {
                return null;
            }

            lock (_lockObj)
            {
                var h = _history[playerName];
                h.Add(new ChatTurn("user", playerMessage));
                h.Add(new ChatTurn("assistant", intent.Say));
                if (h.Count > 16)
                {
                    h.RemoveRange(0, h.Count - 16);
                }
            }

            return intent;
        }

        /// <summary>Decides from a captured world situation without adding it to a human's chat history.</summary>
        /// <param name="botName">The speaker name.</param>
        /// <param name="systemPrompt">The captured perception and personality.</param>
        /// <param name="targets">The nearby human names accepted by the schema.</param>
        /// <returns>The proposed autonomous intent.</returns>
        public Task<NpcIntent?> ThinkIntentAsync(string botName, string systemPrompt, IReadOnlyList<string> targets)
            => this._llmClient.ChatIntentAsync(systemPrompt, new[] { new ChatTurn("user", "Decidí si reaccionar a lo que acaba de pasar.") }, botName, targets);

        /// <summary>
        /// Procesa un mensaje del jugador en modo híbrido.
        /// Primero intenta YarnSpinner, si hay opciones muestra botones, luego permite texto libre.
        /// </summary>
        public async Task<HybridDialogueResult> ProcessHybridMessageAsync(
            string botName,
            BotDialogueConfig config,
            string playerMessage,
            string? yarnNodeName = null)
        {
            // Esta es una implementación simplificada - en producción integrarías con YarnBotManager
            var reply = await ReplyAsync(botName, "player", config.PersonalityDescription, playerMessage).ConfigureAwait(false);

            return new HybridDialogueResult
            {
                BotName = botName,
                ResponseText = reply,
                ModeUsed = DialogueMode.LLMNatural,
                HasOptions = false // En modo LLM puro no hay opciones predefinidas
            };
        }

        /// <summary>
        /// Verifica si un mensaje parece ser un saludo inicial.
        /// </summary>
        private static bool IsLikelyGreeting(string message)
        {
            var lower = message.ToLowerInvariant();
            
            // Palabras clave de saludo en español
            var greetings = new[] { "hola", "buenas", "buenos días", "buenas tardes", 
                                   "buenas noches", "saludos", "que pasa", "qué tal", 
                                   "como estas", "como estás" };

            return Array.Exists(greetings, g => lower.Contains(g));
        }

        /// <summary>
        /// Resultado de procesamiento de diálogo híbrido.
        /// </summary>
        public class HybridDialogueResult
        {
            public string BotName { get; set; } = "";
            public string? ResponseText { get; set; }
            public DialogueMode ModeUsed { get; set; }
            public bool HasOptions { get; set; }

            /// <summary>Lista de opciones disponibles (solo si HasOptions=true)</summary>
            public List<DialogueOption> AvailableOptions { get; set; } = new();
        }

        /// <summary>
        /// Opción de diálogo disponible.
        /// </summary>
        public class DialogueOption
        {
            public int Index { get; set; }
            public string Text { get; set; } = "";
            public string TargetNodeName { get; set; } = "";

            public override string ToString() => $"[{Index}] {Text}";
        }

        /// <summary>
        /// Verifica si el servidor LLM está disponible.
        /// </summary>
        public async Task<bool> IsLLMAvailableAsync()
        {
            return await _llmClient.IsAvailableAsync();
        }


        public void Dispose()
        {
            if (!_disposed)
            {
                _llmClient.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Logger auxiliar para el integrador de diálogos.
    /// Usa ILogger real en lugar de Debug.WriteLine para que los logs aparezcan en el servidor.
    /// </summary>
    internal static class DialogLogger
    {
        private static Microsoft.Extensions.Logging.ILogger? _logger;

        public static void Initialize(Microsoft.Extensions.Logging.ILogger logger)
        {
            _logger = logger;
        }

        public static void Info(string message) => _logger?.LogInformation("[DIALOG INFO] {Message}", message);
        public static void Warn(string message) => _logger?.LogWarning("[DIALOG WARN] {Message}", message);
        public static void Error(string message) => _logger?.LogError("[DIALOG ERROR] {Message}", message);
    }
}
