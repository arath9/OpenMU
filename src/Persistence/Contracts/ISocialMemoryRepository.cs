// <copyright file="ISocialMemoryRepository.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Contracts;

using System.Collections.Generic;
using System;

/// <summary>
/// Contract para repository de memoria social entre bots y NPCs.
/// Fase 2-3: Sistema de conversación contextualizada y eventos dinámicos.
/// </summary>
public interface ISocialMemoryRepository : IDisposable
{
    /// <summary>
    /// Registra una interacción entre un bot y otro personaje/NPC.
    /// </summary>
    void LogBotInteraction(string botName, string targetName, InteractionType interactionType);

    /// <summary>
    /// Obtiene la reputación actual de un bot hacia otro personaje.
    /// </summary>
    int GetReputation(string botName, string targetName);

    /// <summary>
    /// Actualiza la reputación de un bot hacia otro personaje.
    /// </summary>
    void UpdateReputation(string botName, string targetName, int reputationDelta);

    /// <summary>
    /// Obtiene historial de interacciones recientes de un bot.
    /// </summary>
    IEnumerable<BotSocialInteraction> GetRecentInteractions(string botName, int limit = 50);

    /// <summary>
    /// Registra un evento dinámico (cacería, duelo, traición).
    /// </summary>
    void LogEvent(string botName, EventType eventType, string context);

    /// <summary>
    /// Limpia interacciones antiguas para mantener performance.
    /// </summary>
    void CleanupOldInteractions(int daysThreshold = 7);

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
        Friendship = 6      // Amistad/formación de party
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
        GiftGiving = 5      // Regalo ceremonial
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
}

