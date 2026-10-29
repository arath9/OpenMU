// <copyright file="BotReputationRecord.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Model;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Registro de reputación entre bots y otros personajes.
/// Fase 2-3: Seguimiento de relaciones sociales para personalización contextual.
/// </summary>
[Table("BotReputationRecords")]
public class BotReputationRecord
{
    /// <summary>
    /// Identificador único del registro.
    /// </summary>
    [Key]
    public Guid ID { get; set; }

    /// <summary>
    /// Nombre del bot que tiene la reputación.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string BotName { get; set; } = "";

    /// <summary>
    /// Nombre del personaje hacia el cual se tiene la reputación.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string TargetName { get; set; } = "";

    /// <summary>
    /// Nivel actual de reputación (positivo = buena relación, negativo = mala relación).
    /// </summary>
    public int CurrentReputation { get; set; }

    /// <summary>
    /// Total de interacciones registradas con este personaje.
    /// </summary>
    public int TotalInteractions { get; set; }

    /// <summary>
    /// Último timestamp de actualización de reputación.
    /// </summary>
    [Required]
    public DateTime LastUpdated { get; set; }
}
