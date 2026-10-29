// <copyright file="SocialMemoryRepository.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework;

using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Persistence.Contracts;

/// <summary>
/// Repository para persistencia de memoria social entre bots y NPCs/players.
/// Fase 2-3: Sistema de conversación contextualizada y eventos dinámicos.
/// </summary>
public class SocialMemoryRepository : ISocialMemoryRepository, IDisposable
{
    private readonly EntityDataContext _context;
    private readonly ILogger<SocialMemoryRepository>? _logger;

    /// <summary>
    /// Inicializa el repository con contexto EF Core y logger opcional.
    /// </summary>
    public SocialMemoryRepository(EntityDataContext context, ILogger<SocialMemoryRepository>? logger = null)
    {
        this._context = context ?? throw new System.ArgumentNullException(nameof(context));
        this._logger = logger;
    }

    /// <inheritdoc/>
    public void LogBotInteraction(string botName, string targetName, ISocialMemoryRepository.InteractionType interactionType)
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));
        if (string.IsNullOrWhiteSpace(targetName)) throw new System.ArgumentNullException(nameof(targetName));

        var interaction = new Model.BotSocialInteraction
        {
            ID = System.Guid.NewGuid(),
            BotName = botName,
            TargetName = targetName,
            InteractionType = interactionType,
            Timestamp = System.DateTime.UtcNow
        };

        this._context.BotSocialInteractions.Add(interaction);
        this._context.SaveChanges();

        this._logger?.LogInformation("Bot {BotName} interactuó con {TargetName}: {InteractionType}", 
            botName, targetName, interactionType);
    }

    /// <inheritdoc/>
    public int GetReputation(string botName, string targetName)
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));
        if (string.IsNullOrWhiteSpace(targetName)) throw new System.ArgumentNullException(nameof(targetName));

        var record = this._context.BotReputationRecords
            .FirstOrDefault(r => r.BotName == botName && r.TargetName == targetName);

        return record?.CurrentReputation ?? 0; // Default: neutral si no hay historial
    }

    /// <inheritdoc/>
    public void UpdateReputation(string botName, string targetName, int reputationDelta)
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));
        if (string.IsNullOrWhiteSpace(targetName)) throw new System.ArgumentNullException(nameof(targetName));

        // Buscar o crear registro de reputación existente
        var record = this._context.BotReputationRecords
            .FirstOrDefault(r => r.BotName == botName && r.TargetName == targetName);

        if (record == null)
        {
            record = new Model.BotReputationRecord
            {
                ID = System.Guid.NewGuid(),
                BotName = botName,
                TargetName = targetName,
                CurrentReputation = reputationDelta, // Inicializar con delta
                TotalInteractions = 1,
                LastUpdated = System.DateTime.UtcNow
            };

            this._context.BotReputationRecords.Add(record);
        }
        else
        {
            record.CurrentReputation += reputationDelta;
            record.TotalInteractions++;
            record.LastUpdated = System.DateTime.UtcNow;
        }

        this._context.SaveChanges();

        this._logger?.LogInformation("Bot {BotName} reputación hacia {TargetName}: +{Delta}, total: {Current}", 
            botName, targetName, reputationDelta, record.CurrentReputation);
    }

    /// <inheritdoc/>
    public IEnumerable<Model.BotSocialInteraction> GetRecentInteractions(string botName, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));

        return this._context.BotSocialInteractions
            .Where(i => i.BotName == botName)
            .OrderByDescending(i => i.Timestamp)
            .Take(limit)
            .ToList();
    }

    /// <inheritdoc/>
    public void LogEvent(string botName, ISocialMemoryRepository.EventType eventType, string context = "")
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));

        var logEntry = new Model.BotEventLog
        {
            ID = System.Guid.NewGuid(),
            BotName = botName,
            EventType = eventType,
            Context = context ?? "",
            Timestamp = System.DateTime.UtcNow
        };

        this._context.BotEventLogs.Add(logEntry);
        this._context.SaveChanges();

        this._logger?.LogInformation("Bot {BotName} evento: {EventType} - {Context}", 
            botName, eventType, context);
    }

    /// <inheritdoc/>
    public void CleanupOldInteractions(int daysThreshold = 7)
    {
        var cutoffDate = System.DateTime.UtcNow.AddDays(-daysThreshold);

        var interactionsToDelete = this._context.BotSocialInteractions
            .Where(i => i.Timestamp < cutoffDate)
            .ToList();

        if (interactionsToDelete.Any())
        {
            this._context.BotSocialInteractions.RemoveRange(interactionsToDelete);
            this._context.SaveChanges();

            this._logger?.LogInformation("Limpiadas {Count} interacciones antiguas (> {Days} días)", 
                interactionsToDelete.Count, daysThreshold);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // En implementación real: cerrar contextos, liberar recursos
        this._logger?.LogInformation("SocialMemoryRepository cleanup completado");
    }
}
