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
    private readonly IDbContextFactory<EntityDataContext> _contextFactory;
    private readonly ILogger<SocialMemoryRepository>? _logger;

    /// <summary>
    /// Inicializa el repository con una fábrica de contextos EF Core y logger opcional.
    /// </summary>
    public SocialMemoryRepository(IDbContextFactory<EntityDataContext> contextFactory, ILogger<SocialMemoryRepository>? logger = null)
    {
        this._contextFactory = contextFactory ?? throw new System.ArgumentNullException(nameof(contextFactory));
        this._logger = logger;
    }

    /// <inheritdoc/>
    public void LogBotInteraction(string botName, string targetName, ISocialMemoryRepository.InteractionType interactionType)
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));
        if (string.IsNullOrWhiteSpace(targetName)) throw new System.ArgumentNullException(nameof(targetName));

        using var dataContext = this._contextFactory.CreateDbContext();
        var interaction = new Model.BotSocialInteraction
        {
            ID = System.Guid.NewGuid(),
            BotName = botName,
            TargetName = targetName,
            InteractionType = interactionType,
            Timestamp = System.DateTime.UtcNow
        };

        dataContext.BotSocialInteractions.Add(interaction);
        dataContext.SaveChanges();

        this._logger?.LogInformation("Bot {BotName} interactuó con {TargetName}: {InteractionType}", 
            botName, targetName, interactionType);
    }

    /// <inheritdoc/>
    public int GetReputation(string botName, string targetName)
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));
        if (string.IsNullOrWhiteSpace(targetName)) throw new System.ArgumentNullException(nameof(targetName));

        using var dataContext = this._contextFactory.CreateDbContext();
        var record = dataContext.BotReputationRecords
            .FirstOrDefault(r => r.BotName == botName && r.TargetName == targetName);

        return record?.CurrentReputation ?? 0; // Default: neutral si no hay historial
    }

    /// <inheritdoc/>
    public void UpdateReputation(string botName, string targetName, int reputationDelta)
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));
        if (string.IsNullOrWhiteSpace(targetName)) throw new System.ArgumentNullException(nameof(targetName));

        // Buscar o crear registro de reputación existente
        using var dataContext = this._contextFactory.CreateDbContext();
        var record = dataContext.BotReputationRecords
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

            dataContext.BotReputationRecords.Add(record);
        }
        else
        {
            record.CurrentReputation += reputationDelta;
            record.TotalInteractions++;
            record.LastUpdated = System.DateTime.UtcNow;
        }

        dataContext.SaveChanges();

        this._logger?.LogInformation("Bot {BotName} reputación hacia {TargetName}: +{Delta}, total: {Current}", 
            botName, targetName, reputationDelta, record.CurrentReputation);
    }

    /// <inheritdoc/>
    public IEnumerable<ISocialMemoryRepository.BotSocialInteraction> GetRecentInteractions(string botName, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));

        using var dataContext = this._contextFactory.CreateDbContext();
        return dataContext.BotSocialInteractions
            .Where(i => i.BotName == botName)
            .OrderByDescending(i => i.Timestamp)
            .Take(limit)
            .Select(i => new ISocialMemoryRepository.BotSocialInteraction
            {
                ID = i.ID,
                BotName = i.BotName,
                TargetName = i.TargetName,
                InteractionType = i.InteractionType,
                Timestamp = i.Timestamp,
            })
            .ToList();
    }

    /// <inheritdoc/>
    public void LogEvent(string botName, ISocialMemoryRepository.EventType eventType, string context = "")
    {
        if (string.IsNullOrWhiteSpace(botName)) throw new System.ArgumentNullException(nameof(botName));

        using var dataContext = this._contextFactory.CreateDbContext();
        var logEntry = new Model.BotEventLog
        {
            ID = System.Guid.NewGuid(),
            BotName = botName,
            EventType = eventType,
            Context = context ?? "",
            Timestamp = System.DateTime.UtcNow
        };

        dataContext.BotEventLogs.Add(logEntry);
        dataContext.SaveChanges();

        this._logger?.LogInformation("Bot {BotName} evento: {EventType} - {Context}", 
            botName, eventType, context);
    }

    /// <inheritdoc/>
    public void CleanupOldInteractions(int daysThreshold = 7)
    {
        using var dataContext = this._contextFactory.CreateDbContext();
        var cutoffDate = System.DateTime.UtcNow.AddDays(-daysThreshold);

        var interactionsToDelete = dataContext.BotSocialInteractions
            .Where(i => i.Timestamp < cutoffDate)
            .ToList();

        if (interactionsToDelete.Any())
        {
            dataContext.BotSocialInteractions.RemoveRange(interactionsToDelete);
            dataContext.SaveChanges();

            this._logger?.LogInformation("Limpiadas {Count} interacciones antiguas (> {Days} días)", 
                interactionsToDelete.Count, daysThreshold);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Each operation owns and disposes its context; the factory is owned by DI.
        this._logger?.LogInformation("SocialMemoryRepository cleanup completado");
    }
}
