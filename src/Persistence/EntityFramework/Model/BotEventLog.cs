// <copyright file="BotEventLog.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Model;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MUnique.OpenMU.Persistence.Contracts;

/// <summary>
/// Registro de eventos dinámicos entre bots/NPCs (cacerías, duelos, traiciones, rescates).
/// Fase 3: Sistema de eventos para personajes memorables y historias emergentes.
/// </summary>
[Table("BotEventLogs")]
public class BotEventLog
{
    /// <summary>
    /// Identificador único del evento.
    /// </summary>
    [Key]
    public Guid ID { get; set; }

    /// <summary>
    /// Nombre del bot que ejecutó el evento.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string BotName { get; set; } = "";

    /// <summary>
    /// Tipo de evento (huntingParty, duel, betrayal, rescue, giftGiving).
    /// </summary>
    public EventType EventType { get; set; }

    /// <summary>
    /// Contexto adicional del evento (detalles específicos del evento ocurrido).
    /// </summary>
    [MaxLength(500)]
    public string Context { get; set; } = "";

    /// <summary>
    /// Timestamp UTC cuando ocurrió el evento.
    /// </summary>
    [Required]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Enum para tipos de eventos dinámicos.
    /// </summary>
    public enum EventType
    {
        HuntingParty = 1,    // Cacería en grupo programada
        Duel = 2,           // Duelo entre bots/NPCs
        Betrayal = 3,       // Traición (LeilaMasa)
        Rescue = 4,         // Rescate de aliado
        GiftGiving = 5      // Regalo ceremonial
    }
}
