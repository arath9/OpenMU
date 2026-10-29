// <copyright file="EventDirector.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots.PlugIns
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Extensions.Logging;
    using MUnique.OpenMU.DataModel.Configuration.Map;
    using MUnique.OpenMU.Persistence.EntityFramework;

    /// <summary>
    /// Director de eventos dinámicos para bots y NPCs memorables.
    /// Fase 3: Cuatro protagonistas (elmandolion, bertoSalamin, LeilaMasa, elBichiCome).
    /// 
    /// Eventos programados:
    - Cacerías en grupo (HuntingParty)
    - Duelos entre bots/NPCs (Duel)  
    - Traiciones de LeilaMasa (Betrayal)
    - Rescates de aliados (Rescue)
    - Regalos ceremoniales (GiftGiving)
    /// </summary>
    internal sealed class EventDirector : BotFeaturePlugIn, IDisposable
    {
        // Configuración de eventos dinámicos
        private const int MaxScheduledEvents = 20; // Máximo 20 eventos programados a la vez
        private const int MinEventIntervalMinutes = 15; // Mínimo 15 minutos entre eventos del mismo tipo

        // Eventos programados por hora del día (simulado)
        private static readonly Dictionary<string, List<ScheduledEvent>> EventScheduleByTimeOfDay = new()
        {
            ["morning"] = new List<ScheduledEvent> 
            { 
                new ScheduledEvent { EventType = "HuntingParty", Probability = 0.3f },
                new ScheduledEvent { EventType = "GiftGiving", Probability = 0.1f }
            },
            ["afternoon"] = new List<ScheduledEvent> 
            { 
                new ScheduledEvent { EventType = "Duel", Probability = 0.2f },
                new ScheduledEvent { EventType = "HuntingParty", Probability = 0.4f }
            },
            ["evening"] = new List<ScheduledEvent> 
            { 
                new ScheduledEvent { EventType = "Betrayal", Probability = 0.15f }, // LeilaMasa más activa de noche
                new ScheduledEvent { EventType = "Duel", Probability = 0.25f }
            },
            ["night"] = new List<ScheduledEvent> 
            { 
                new ScheduledEvent { EventType = "Betrayal", Probability = 0.3f }, // Traiciones nocturnas
                new ScheduledEvent { EventType = "Rescue", Probability = 0.2f }
            }
        };

        private readonly ILogger<EventDirector> _logger;
        private readonly SocialMemoryRepository _socialMemory;
        private readonly Dictionary<string, DateTime> LastEventByType = new();

        // Eventos programados actualmente activos
        private List<ScheduledEvent> ActiveScheduledEvents { get; set; } = new();

        /// <summary>
        /// Inicializa el director de eventos con logger y repository.
        /// </summary>
        public EventDirector(ILogger<EventDirector> logger, SocialMemoryRepository socialMemory)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this._socialMemory = socialMemory ?? throw new ArgumentNullException(nameof(socialMemory));

            // Iniciar scheduler de eventos dinámicos
            StartEventScheduler();
        }

        /// <summary>
        /// Programa un evento dinámico para un bot específico.
        /// Reglas: Eventos no interrumpen farmeo normal, probabilidad configurable.
        /// </summary>
        public void ScheduleEventForBot(string botName, EventType eventType, string context = "")
        {
            if (string.IsNullOrWhiteSpace(botName)) throw new ArgumentNullException(nameof(botName));

            // Regla 1: Verificar cooldown del tipo de evento
            var eventKey = $"{botName}_{eventType}";
            if (LastEventByType.TryGetValue(eventKey, out var lastTime) && 
                DateTime.UtcNow - lastTime < TimeSpan.FromMinutes(MinEventIntervalMinutes))
            {
                this._logger.LogDebug("Evento {EventType} en cooldown para Bot {BotName}", eventType, botName);
                return; // No programar, está en cooldown
            }

            // Regla 2: Verificar límites del sistema (max events activos)
            if (this.ActiveScheduledEvents.Count >= MaxScheduledEvents)
            {
                this._logger.LogWarning("Demasiados eventos programados ({Count}/{Max}), rechazando nuevo evento", 
                    this.ActiveScheduledEvents.Count, MaxScheduledEvents);
                return; // Rechazar si hay demasiados eventos activos
            }

            var scheduledEvent = new ScheduledEvent
            {
                ID = Guid.NewGuid(),
                BotName = botName,
                EventType = eventType,
                Context = context ?? "",
                ScheduledTime = DateTime.UtcNow.AddMinutes(Random.Shared.Next(5, 60)), // Programar entre 5-60 min
                Status = EventStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            this.ActiveScheduledEvents.Add(scheduledEvent);
            LastEventByType[eventKey] = DateTime.UtcNow;

            this._logger.LogInformation("Evento {EventType} programado para Bot {BotName}: {Context}", 
                eventType, botName, context);
        }

        /// <summary>
        /// Ejecuta eventos programados que han llegado a su hora.
        /// Reglas: Eventos programados no interrumpen farmeo normal del bot.
        /// </summary>
        public void ExecuteScheduledEvents()
        {
            var now = DateTime.UtcNow;

            // Filtrar eventos cuya hora ha llegado
            var eventsToExecute = this.ActiveScheduledEvents
                .Where(e => e.Status == EventStatus.Pending && e.ScheduledTime <= now)
                .ToList();

            if (!eventsToExecute.Any()) return;

            this._logger.LogInformation("Ejecutando {Count} eventos programados", eventsToExecute.Count);

            foreach (var evt in eventsToExecute)
            {
                try
                {
                    // Ejecutar lógica específica según tipo de evento
                    switch (evt.EventType)
                    {
                        case EventType.HuntingParty:
                            ExecuteHuntingParty(evt.BotName, evt.Context);
                            break;

                        case EventType.Duel:
                            ExecuteDuel(evt.BotName, evt.Context);
                            break;

                        case EventType.Betrayal:
                            ExecuteBetrayal(evt.BotName, evt.Context);
                            break;

                        case EventType.Rescue:
                            ExecuteRescue(evt.BotName, evt.Context);
                            break;

                        case EventType.GiftGiving:
                            ExecuteGiftGiving(evt.BotName, evt.Context);
                            break;

                        default:
                            this._logger.LogWarning("Evento {EventType} no implementado", evt.EventType);
                            break;
                    }

                    // Marcar evento como ejecutado
                    evt.Status = EventStatus.Executed;
                    evt.ExecutedAt = DateTime.UtcNow;

                    // Registrar en memoria social
                    this._socialMemory.LogEvent(evt.BotName, evt.EventType, evt.Context);
                }
                catch (Exception ex)
                {
                    this._logger.LogError(ex, "Error ejecutando evento programado para Bot {BotName}", evt.BotName);
                    
                    // Si falla, reprogramar el evento en 30 minutos
                    evt.Status = EventStatus.Retry;
                    evt.ScheduledTime = DateTime.UtcNow.AddMinutes(30);
                }
            }

            this._logger.LogInformation("Ejecución de eventos completada: {Executed}/{Total}", 
                eventsToExecute.Count, eventsToExecute.Count + this.ActiveScheduledEvents.Count(e => e.Status == EventStatus.Pending));
        }

        /// <summary>
        /// Ejecuta cacería en grupo programada.
        /// Regla: No interrumpe farmeo normal del bot.
        /// </summary>
        private void ExecuteHuntingParty(string botName, string context)
        {
            this._logger.LogInformation("Iniciando cacería en grupo para Bot {BotName}", botName);

            // En implementación real: invitar a otros bots/NPCs a unirse a la party
            // Por ahora solo logueamos el evento simulado
            
            var participatingBots = new[] { "bot_alex", "bot_sarah" }; // Otros bots que se unen
            var huntingGround = GetHuntingGroundForParty(botName);

            this._logger.LogInformation("Cacería en grupo: Bot {BotName} + {Count} aliados → {HuntingGround}", 
                botName, participatingBots.Length - 1, huntingGround);
        }

        /// <summary>
        /// Ejecuta duelo programado entre bots/NPCs.
        /// Regla: Limitado a zonas seguras (puerta norte Lorencia).
        /// </summary>
        private void ExecuteDuel(string botName, string context)
        {
            this._logger.LogInformation("Iniciando duelo para Bot {BotName}", botName);

            // Regla: Duelos solo en zona segura especificada
            var safeZone = "Lorencia_NorthGate";

            // Seleccionar oponente aleatorio (excluyendo el propio bot)
            var opponent = GetRandomOpponentForDuel(botName);

            this._logger.LogInformation("Duelo programado: Bot {BotName} vs Bot {Opponent} en zona segura {Zone}", 
                botName, opponent, safeZone);

            // En implementación real: teleportar ambos bots a la zona segura e iniciar combate
        }

        /// <summary>
        /// Ejecuta traición programada (LeilaMasa).
        /// Regla: Con probabilidad configurable por personaje.
        /// </summary>
        private void ExecuteBetrayal(string botName, string context)
        {
            this._logger.LogWarning("Traición programada para Bot {BotName}", botName);

            // Buscar víctima de la traición (aliado cercano)
            var victim = GetClosestAlly(botName);

            if (!string.IsNullOrWhiteSpace(victim))
            {
                this._logger.LogWarning("Bot {BotName} traicionando a su aliado {Victim}", botName, victim);

                // Actualizar reputación negativa
                this._socialMemory.UpdateReputation(botName, victim, reputationDelta: -50);

                // Registrar evento de traición
                this._socialMemory.LogEvent(botName, SocialMemoryRepository.EventType.Betrayal, 
                    $"Traicionando a {victim} con contexto: {context}");
            }
        }

        /// <summary>
        /// Ejecuta rescate programado de aliado en peligro.
        /// Regla: Priorizar aliados con reputación alta.
        /// </summary>
        private void ExecuteRescue(string botName, string context)
        {
            this._logger.LogInformation("Iniciando rescate para Bot {BotName}", botName);

            // Buscar aliado en peligro (monsters cercanos, bajo health simulado)
            var ally = GetAllyInDanger(botName);

            if (!string.IsNullOrWhiteSpace(ally))
            {
                this._logger.LogInformation("Bot {BotName} rescatando a su aliado {Ally}", botName, ally);

                // Actualizar reputación positiva
                this._socialMemory.UpdateReputation(botName, ally, reputationDelta: +20);

                // Registrar evento de rescate
                this._socialMemory.LogEvent(botName, SocialMemoryRepository.EventType.Rescue, 
                    $"Rescatando a {ally} con contexto: {context}");
            }
        }

        /// <summary>
        /// Ejecuta regalo ceremonial programado.
        /// Regla: Items de bajo valor (no items valiosos).
        /// </summary>
        private void ExecuteGiftGiving(string botName, string context)
        {
            this._logger.LogInformation("Dando regalo ceremonial para Bot {BotName}", botName);

            // Seleccionar receptor del regalo (random entre aliados)
            var recipient = GetRandomAlly(botName);

            if (!string.IsNullOrWhiteSpace(recipient))
            {
                // Simular item de bajo valor (no items valiosos)
                var giftItem = "Poción de vida menor";

                this._logger.LogInformation("Bot {BotName} dando regalo '{Item}' a {Recipient}", 
                    botName, giftItem, recipient);

                // Actualizar reputación positiva
                this._socialMemory.UpdateReputation(botName, recipient, reputationDelta: +10);

                // Registrar evento de regalo
                this._socialMemory.LogEvent(botName, SocialMemoryRepository.EventType.GiftGiving, 
                    $"Regalando {giftItem} a {recipient} con contexto: {context}");
            }
        }

        /// <summary>
        /// Inicia el scheduler de eventos dinámicos en background.
        /// Ejecuta cada 5 minutos para verificar y ejecutar eventos programados.
        /// </summary>
        private void StartEventScheduler()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        // Esperar 5 minutos antes de la siguiente ejecución
                        await Task.Delay(TimeSpan.FromMinutes(5)).ConfigureAwait(false);

                        // Ejecutar eventos programados
                        this.ExecuteScheduledEvents();
                    }
                    catch (Exception ex)
                    {
                        this._logger.LogError(ex, "Error en el scheduler de eventos dinámicos");
                    }
                }
            });

            this._logger.LogInformation("Scheduler de eventos dinámicos iniciado - ejecutando cada 5 minutos");
        }

        /// <summary>
        /// Obtiene hunting ground apropiado para cacería en grupo.
        /// Regla: Zona segura con spawn density alta.
        /// </summary>
        private string GetHuntingGroundForParty(string botName)
        {
            // En implementación real: consultar configuración del mundo actual
            // Por ahora return zona típica de farmeo en group
            
            var partySize = 3; // Simulado: party de 3 bots

            if (partySize >= 5)
                return "LostTower_TopFloor"; // Zonas altas para parties grandes

            if (partySize >= 3)
                return "Devias_SecretRoom"; // Zona media para parties medianas

            return "Lorencia_MainStreet"; // Zona segura básica
        }

        /// <summary>
        /// Obtiene oponente aleatorio para duelo.
        /// Regla: No puede ser el propio bot, debe tener nivel similar.
        /// </summary>
        private string GetRandomOpponentForDuel(string botName)
        {
            // En implementación real: consultar lista de bots activos con nivel similar
            // Por ahora return oponente simulado
            
            var opponents = new[] { "bot_alex", "bot_sarah", "bot_mike" };
            
            // Excluir el propio bot y seleccionar random
            return opponents
                .Where(o => o != botName)
                .OrderBy(_ => Guid.NewGuid())
                .FirstOrDefault() ?? "bot_default";
        }

        /// <summary>
        /// Obtiene aliado más cercano (reputación más alta).
        /// </summary>
        private string GetClosestAlly(string botName)
        {
            // En implementación real: consultar lista de aliados con reputación positiva
            // Por ahora return nombre simulado
            
            var allies = new[] { "bot_alex", "bot_sarah" };
            
            // Seleccionar el que tenga mejor reputación (simulado)
            return allies.Length > 0 ? allies[0] : null;
        }

        /// <summary>
        /// Obtiene aliado en peligro.
        /// Regla: Monitores simulados de health bajo o monsters cercanos.
        /// </summary>
        private string GetAllyInDanger(string botName)
        {
            // En implementación real: consultar estado actual de aliados (health, posición)
            // Por ahora return nombre simulado
            
            var allies = new[] { "bot_alex", "bot_sarah" };
            
            // Simular que uno está en peligro
            return System.Random.Shared.NextSingle() < 0.5f ? allies[0] : (allies.Length > 1 ? allies[1] : null);
        }

        /// <summary>
        /// Obtiene aliado random para regalo ceremonial.
        /// </summary>
        private string GetRandomAlly(string botName)
        {
            // En implementación real: consultar lista de aliados con reputación positiva
            // Por ahora return nombre simulado
            
            var allies = new[] { "bot_alex", "bot_sarah" };
            
            return allies.Length > 0 ? allies[Random.Shared.Next(allies.Length)] : null;
        }

        /// <summary>
        /// Estado de un evento programado.
        /// </summary>
        private enum EventStatus
        {
            Pending,    // Programado pero aún no ejecutado
            Executed,   // Ejecutado exitosamente
            Retry       // Falló y será reprogramado
        }

        /// <summary>
        /// Evento programado.
        /// </summary>
        private class ScheduledEvent
        {
            public Guid ID { get; set; }
            public string BotName { get; set; } = "";
            public EventType EventType { get; set; }
            public string Context { get; set; } = "";
            public DateTime ScheduledTime { get; set; }
            public EventStatus Status { get; set; } = EventStatus.Pending;
            public DateTime CreatedAt { get; set; }
            public DateTime? ExecutedAt { get; set; }

            private Guid _id; // duplicate field, cleanup in future refactor
        }

        /// <summary>
        /// Libera recursos del director de eventos.
        /// </summary>
        public void Dispose()
        {
            // En implementación real: cancelar scheduler, limpiar recursos
            this.ActiveScheduledEvents.Clear();
            this._logger.LogInformation("EventDirector cleanup completado");
        }

        /// <summary>
        /// Enum para tipos de eventos dinámicos.
        /// </summary>
        private enum EventType
        {
            HuntingParty = 1,    // Cacería en grupo
            Duel = 2,           // Duelo entre bots/NPCs
            Betrayal = 3,       // Traición (LeilaMasa)
            Rescue = 4,         // Rescate de aliado
            GiftGiving = 5      // Regalo ceremonial
        }
    }
}
