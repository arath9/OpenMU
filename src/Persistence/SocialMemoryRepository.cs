// <copyright file="SocialMemoryRepository.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Persistence.Contracts;

    /// <summary>
    /// Repository para persistencia de memoria social entre bots y jugadores.
    /// Fase 1: relaciones básicas, reputación, historial de interacciones.
    /// Fase 2+: conversaciones completas, memorias persistentes, personalidad contextual.
    /// </summary>
    public class SocialMemoryRepository : ISocialMemoryRepository
    {
        private readonly EntityDataContext _context;
        private readonly ILogger<SocialMemoryRepository> _logger;

        // Tablas de memoria social (nuevas para Fase 1)
        private DbSet<BotSocialInteraction> Interactions => this._context.BotSocialInteractions;
        private DbSet<BotReputationRecord> ReputationRecords => this._context.BotReputationRecords;
        private DbSet<BotEventLog> EventLogs => this._context.BotEventLogs;

        /// <summary>
        /// Inicializa el repository con contexto EF Core y logger.
        /// </summary>
        /// <param name="context">Contexto EF Core principal.</param>
        /// <param name="logger">Logger para logging del repository.</param>
        public SocialMemoryRepository(EntityDataContext context, ILogger<SocialMemoryRepository>? logger = null)
        {
            this._context = context ?? throw new ArgumentNullException(nameof(context));
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Crear tablas si no existen (migración automática para desarrollo)

            this._logger.LogInformation("SocialMemoryRepository inicializado");
        }

        /// <summary>
        /// Registra una interacción entre un bot y un jugador/NPC.
        /// </summary>
        public void LogBotInteraction(string botName, string targetName, InteractionType interactionType)
        {
            if (string.IsNullOrWhiteSpace(botName)) throw new ArgumentNullException(nameof(botName));
            if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentNullException(nameof(targetName));

            var interaction = new BotSocialInteraction
            {
                ID = Guid.NewGuid(),
                BotName = botName,
                TargetName = targetName,
                InteractionType = interactionType,
                Timestamp = DateTime.UtcNow,
                Context = GetContextForInteraction(interactionType) // Simplificado por ahora
            };

            this.Interactions.Add(interaction);
            this._context.SaveChanges();

            this._logger.LogInformation("Bot {BotName} interactuó con {TargetName}: {Type}", 
                botName, targetName, interactionType);
        }

        /// <summary>
        /// Actualiza la reputación de un bot hacia otro personaje.
        /// </summary>
        public void UpdateReputation(string botName, string targetName, int reputationDelta)
        {
            if (string.IsNullOrWhiteSpace(botName)) throw new ArgumentNullException(nameof(botName));
            if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentNullException(nameof(targetName));

            // Buscar o crear registro de reputación existente
            var record = this.ReputationRecords.FirstOrDefault(r => 
                r.BotName == botName && r.TargetName == targetName);

            if (record == null)
            {
                record = new BotReputationRecord
                {
                    ID = Guid.NewGuid(),
                    BotName = botName,
                    TargetName = targetName,
                    CurrentReputation = reputationDelta, // Inicializar con delta
                    TotalInteractions = 1,
                    LastUpdated = DateTime.UtcNow
                };

                this.ReputationRecords.Add(record);
            }
            else
            {
                record.CurrentReputation += reputationDelta;
                record.TotalInteractions++;
                record.LastUpdated = DateTime.UtcNow;
            }

            this._context.SaveChanges();

            this._logger.LogInformation("Bot {BotName} reputación hacia {TargetName}: +{Delta}, total: {Current}", 
                botName, targetName, reputationDelta, record.CurrentReputation);
        }

        /// <summary>
        /// Obtiene la reputación actual de un bot hacia otro personaje.
        /// </summary>
        public int GetReputation(string botName, string targetName)
        {
            if (string.IsNullOrWhiteSpace(botName)) throw new ArgumentNullException(nameof(botName));
            if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentNullException(nameof(targetName));

            var record = this.ReputationRecords.FirstOrDefault(r => 
                r.BotName == botName && r.TargetName == targetName);

            return record?.CurrentReputation ?? 0; // Default: neutral si no hay historial
        }

        /// <summary>
        /// Registra un evento dinámico (cacería, duelo, traición).
        /// </summary>
        public void LogEvent(string botName, EventType eventType, string context)
        {
            if (string.IsNullOrWhiteSpace(botName)) throw new ArgumentNullException(nameof(botName));

            var logEntry = new BotEventLog
            {
                ID = Guid.NewGuid(),
                BotName = botName,
                EventType = eventType,
                Context = context ?? "",
                Timestamp = DateTime.UtcNow
            };

            this.EventLogs.Add(logEntry);
            this._context.SaveChanges();

            this._logger.LogInformation("Bot {BotName} evento: {EventType} - {Context}", 
                botName, eventType, context);
        }

        /// <summary>
        /// Obtiene historial de interacciones recientes de un bot.
        /// </summary>
        public IEnumerable<BotSocialInteraction> GetRecentInteractions(string botName, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(botName)) throw new ArgumentNullException(nameof(botName));

            return this.Interactions
                .Where(i => i.BotName == botName)
                .OrderByDescending(i => i.Timestamp)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// Obtiene estadísticas de reputación de un bot.
        /// </summary>
        public Dictionary<string, int> GetReputationStats(string botName)
        {
            if (string.IsNullOrWhiteSpace(botName)) throw new ArgumentNullException(nameof(botName));

            return this.ReputationRecords
                .Where(r => r.BotName == botName)
                .ToDictionary(r => r.TargetName, r => r.CurrentReputation);
        }

        /// <summary>
        /// Limpia interacciones antiguas (más de 7 días) para mantener performance.
        /// </summary>
        public void CleanupOldInteractions(int daysThreshold = 7)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysThreshold);

            var deletedCount = this.Interactions
                .Where(i => i.Timestamp < cutoffDate)
                .Count();

            if (deletedCount > 0)
            {
                this.Interactions.RemoveRange(this.Interactions.Where(i => i.Timestamp < cutoffDate));
                this._context.SaveChanges();

                this._logger.LogInformation("Limpiadas {Count} interacciones antiguas (> {Days} días)", 
                    deletedCount, daysThreshold);
            }
        }

        /// <summary>
        /// Obtiene contexto simplificado para tipo de interacción.
        /// </summary>
        private string GetContextForInteraction(InteractionType interactionType)
        {
            return interactionType switch
            {
                InteractionType.Chat => "conversacion",
                InteractionType.Trade => "comercio",
                InteractionType.Quest => "mision",
                InteractionType.PvP => "combate",
                InteractionType.Help => "ayuda",
                _ => "desconocido"
            };
        }

        /// <summary>
        /// Enum para tipos de interacción social.
        /// </summary>
        public enum InteractionType
        {
            Chat = 1,           // Conversación básica
            Trade = 2,          // Comercio/intercambio
            Quest = 3,          // Misión compartida
            PvP = 4,            // Combate/PvP
            Help = 5,           // Ayuda/servicio
            Friendship = 6,     // Amistad/formación de party
        }

        /// <summary>
        /// Enum para tipos de eventos dinámicos.
        /// </summary>
        public enum EventType
        {
            HuntingParty = 1,    // Cacería en grupo
            Duel = 2,           // Duelo entre bots/NPCs
            Betrayal = 3,       // Traición (LeilaMasa)
            Rescue = 4,         // Rescate de aliado
            GiftGiving = 5,     // Regalo ceremonial
        }

        /// <summary>
        /// Modelo para interacciones sociales.
        /// </summary>
        public class BotSocialInteraction
        {
            public Guid ID { get; set; }
            public string BotName { get; set; } = "";
            public string TargetName { get; set; } = "";
            public InteractionType InteractionType { get; set; }
            public string Context { get; set; } = "";
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// Modelo para registros de reputación.
        /// </summary>
        public class BotReputationRecord
        {
            public Guid ID { get; set; }
            public string BotName { get; set; } = "";
            public string TargetName { get; set; } = "";
            public int CurrentReputation { get; set; }
            public int TotalInteractions { get; set; }
            public DateTime LastUpdated { get; set; }
        }

        /// <summary>
        /// Modelo para logs de eventos dinámicos.
        /// </summary>
        public class BotEventLog
        {
            public Guid ID { get; set; }
            public string BotName { get; set; } = "";
            public EventType EventType { get; set; }
            public string Context { get; set; } = "";
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// Interface para el repository.
        /// </summary>
        public interface ISocialMemoryRepository
        {
            void LogBotInteraction(string botName, string targetName, InteractionType interactionType);
            void UpdateReputation(string botName, string targetName, int reputationDelta);
            int GetReputation(string botName, string targetName);
            void LogEvent(string botName, EventType eventType, string context);
            IEnumerable<BotSocialInteraction> GetRecentInteractions(string botName, int limit = 50);
            Dictionary<string, int> GetReputationStats(string botName);
            void CleanupOldInteractions(int daysThreshold = 7);
        }
    }
}
