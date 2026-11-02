// <copyright file="CustomNavigationPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots.PlugIns;

using System;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.PlugIns;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Optional navigation policy over actual map spawn definitions. This is not a population-spawning feature.
/// Movement remains the responsibility of <see cref="BotNavigator"/>.
/// </summary>
/// <summary>
/// Optional navigation policy over actual map spawn definitions. This is not a population-spawning feature.
/// Movement remains the responsibility of <see cref="BotNavigator"/>.
/// </summary>
[PlugIn]
[System.Runtime.InteropServices.Guid("d4e5f6a7-b8c9-0123-defa-234567890123")]
internal sealed class CustomNavigationPlugIn : BotFeaturePlugIn
{
    private const int MaxSafeMonsterLevelDifference = 3;
    private readonly ILogger<CustomNavigationPlugIn> _logger;

    /// <summary>Initializes a new instance of the <see cref="CustomNavigationPlugIn"/> class.</summary>
    public CustomNavigationPlugIn(ILogger<CustomNavigationPlugIn> logger)
    {
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Selects a permanent monster spawn from the bot's current map, or null if none is safe.</summary>
    public MonsterSpawnArea? PickHuntingGround(BotPlayer botPlayer)
    {
        ArgumentNullException.ThrowIfNull(botPlayer);
        var zone = botPlayer.CurrentMap?.Definition.MonsterSpawns
            .Where(area => area is { Quantity: > 0, SpawnTrigger: SpawnTrigger.Automatic, MonsterDefinition.ObjectKind: NpcObjectKind.Monster })
            .Where(area => GetMonsterLevel(area) <= botPlayer.Level + MaxSafeMonsterLevelDifference)
            .OrderByDescending(GetMonsterLevel)
            .ThenByDescending(GetSpawnDensity)
            .FirstOrDefault();
        this._logger.LogDebug("Bot {BotName} selected hunting ground {Zone}", botPlayer.Name, zone);
        return zone;
    }

    /// <summary>Logs a hunting-ground change without issuing unsafe teleports.</summary>
    public void LogHuntingGroundChange(BotPlayer botPlayer, MonsterSpawnArea? fromZone, MonsterSpawnArea toZone)
    {
        ArgumentNullException.ThrowIfNull(botPlayer);
        ArgumentNullException.ThrowIfNull(toZone);
        this._logger.LogInformation("Bot {BotName} changing hunting ground: {FromZone} -> {ToZone}", botPlayer.Name, fromZone, toZone);
    }

    /// <summary>Logs an unsafe spawn area.</summary>
    public void LogDangerousZone(BotPlayer botPlayer, MonsterSpawnArea zone)
    {
        ArgumentNullException.ThrowIfNull(botPlayer);
        ArgumentNullException.ThrowIfNull(zone);
        this._logger.LogWarning("Bot {BotName} detected dangerous hunting ground {Zone}", botPlayer.Name, zone);
    }

    private static int GetMonsterLevel(MonsterSpawnArea area)
        => (int)(area.MonsterDefinition?.Attributes.FirstOrDefault(a => a.AttributeDefinition == Stats.Level)?.Value ?? 0f);

    private static double GetSpawnDensity(MonsterSpawnArea area)
        => (double)area.Quantity / ((Math.Abs(area.X2 - area.X1) + 1) * (Math.Abs(area.Y2 - area.Y1) + 1));
}

