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
        // Cooldown por jugador-bot, no global por bot
        private readonly Dictionary<(string botName, string playerName), DateTime> _lastResponseTime = new();
        private readonly object _lockObj = new();

        private bool _disposed;

        /// <summary>
        /// Crea una nueva instancia del integrador LLM.
        /// </summary>
        public BotLLMDialogueIntegration(LocalLLMClient.LLMConfig config, ILogger<BotLLMDialogueIntegration>? logger = null)
        {
            _logger = logger;
            
            // Inicializar DialogLogger con el ILogger proporcionado o crear uno temporal
            if (logger != null)
            {
                DialogLogger.Initialize(logger);
            }
            else
            {
                using var factory = new Microsoft.Extensions.Logging.LoggerFactory();
                var tempLogger = factory.CreateLogger<BotLLMDialogueIntegration>();
                DialogLogger.Initialize(tempLogger);
            }
            
            // Crear logger específico para LocalLLMClient usando LoggerFactory temporal si no hay uno disponible
            using var llmFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var llmLogger = llmFactory.CreateLogger<MUnique.OpenMU.GameLogic.Bots.LLMIntegration.LocalLLMClient>();
            _llmClient = new LocalLLMClient(config, llmLogger);
            
            DialogLogger.Info("Integrador LLM inicializado para bot");
        }

        /// <summary>
        /// Procesa un mensaje del jugador y genera respuesta usando LLM.
        /// </summary>
        public async Task<string?> ProcessMessageAsync(
            string botName,
            BotDialogueConfig config,
            string playerMessage,
            string? context = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BotLLMDialogueIntegration));

            // Verificar cooldown POR JUGADOR (no global por bot)
            var now = DateTime.UtcNow;
            lock (_lockObj)
            {
                // Extraer nombre del jugador del contexto o mensaje
                string? playerName = null;
                
                if (!string.IsNullOrEmpty(context))
                {
                    // Contexto formato: "Jugador: Nombre. Nivel: X..."
                    var match = System.Text.RegularExpressions.Regex.Match(context, @"Jugador:\s*(\w+)");
                    if (match.Success)
                        playerName = match.Groups[1].Value;
                }

                // Si no se pudo extraer del contexto, usar el mensaje como fallback
                if (string.IsNullOrEmpty(playerName))
                    playerName = "unknown";

                var key = (botName, playerName);
                
                if (_lastResponseTime.TryGetValue(key, out var lastTime))
                {
                    var elapsed = now - lastTime;
                    if (elapsed.TotalSeconds < config.ResponseCooldownSeconds)
                    {
                        _logger?.LogDebug("Bot '{BotName}' en cooldown para jugador '{PlayerName}' ({Elapsed}s de {Cooldown}s)", botName, playerName, elapsed.TotalSeconds, config.ResponseCooldownSeconds);
                        return null; // En cooldown, no responder
                    }
                }

                _lastResponseTime[key] = now;
            }

            // Verificar si el bot debe responder a este mensaje
            if (config.GreetingOnlyMode && !IsLikelyGreeting(playerMessage))
            {
                return null; // No es saludo, no responder en modo greeting-only
            }

            // Generar respuesta con LLM
            var response = await _llmClient.GenerateResponseAsync(
                botName,
                config.PersonalityDescription,
                playerMessage,
                context);

            if (string.IsNullOrEmpty(response))
            {
                DialogLogger.Warn($"BotLLMDialogueIntegration: Bot '{botName}' no generó respuesta válida");
                return null;
            }

            return response;
        }

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
            var response = await ProcessMessageAsync(botName, config, playerMessage);

            return new HybridDialogueResult
            {
                BotName = botName,
                ResponseText = response,
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
