// <copyright file="BotSocialInteraction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Model;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Registro de interacción social entre bots y NPCs/jugadores.
/// Fase 2-3: Sistema de memoria social para conversaciones contextualizadas.
/// </summary>
[Table("BotSocialInteractions")]
public class BotSocialInteraction
{
    /// <summary>
    /// Identificador único de la interacción.
    /// </summary>
    [Key]
    public Guid ID { get; set; }

    /// <summary>
    /// Nombre del bot que interactuó.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string BotName { get; set; } = "";

    /// <summary>
    /// Nombre del objetivo de la interacción (NPC o jugador).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string TargetName { get; set; } = "";

    /// <summary>
    /// Tipo de interacción (chat, trade, quest, PvP, help, friendship).
    /// </summary>
    public Persistence.Contracts.ISocialMemoryRepository.InteractionType InteractionType { get; set; }

    /// <summary>
    /// Timestamp UTC cuando ocurrió la interacción.
    /// </summary>
    [Required]
    public DateTime Timestamp { get; set; }
}
