// <copyright file="CustomItemEvaluatorPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots.PlugIns;

using System;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.PlugIns;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.GameLogic.PlayerActions.Items;
using MUnique.OpenMU.Persistence.Contracts;

/// <summary>Optional item policy using the server's real equipment and pricing APIs, not a spawning feature.</summary>
/// <summary>
/// Optional item policy using the server's real equipment and pricing APIs, not a spawning feature.
/// </summary>
[PlugIn]
[System.Runtime.InteropServices.Guid("d6eb3056-1a27-5760-8fc9-5e7bd40cdecb")]
internal sealed class CustomItemEvaluatorPlugIn : BotFeaturePlugIn
{
    private const float MinItemValueThreshold = 500f;
    private static readonly ItemPriceCalculator PriceCalculator = new();
    private readonly ILogger<CustomItemEvaluatorPlugIn> _logger;

    /// <summary>Initializes a new instance of the <see cref="CustomItemEvaluatorPlugIn"/> class.</summary>
    public CustomItemEvaluatorPlugIn(ILogger<CustomItemEvaluatorPlugIn> logger)
    {
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Delegates class, slot and stat validation to the existing equipment handler.</summary>
    public bool ShouldEquipItem(BotPlayer botPlayer, Item item)
    {
        ArgumentNullException.ThrowIfNull(botPlayer);
        ArgumentNullException.ThrowIfNull(item);
        return !HasProtectedOptions(item) && BotEquipmentHandler.IsUpgradeFor(botPlayer, item);
    }

    /// <summary>Protects special options and consumable/box/jewel groups from NPC sales.</summary>
    public bool ShouldSellItem(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Definition is { IsSellableToNpc: true, Group: < 12 }
            && !HasProtectedOptions(item)
            && CalculateItemValue(item) >= MinItemValueThreshold;
    }

    /// <summary>Logs an explicit protection request; this helper does not mutate persisted items.</summary>
    public void ProtectItem(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        this._logger.LogInformation("Protection requested for item {Item}", item);
    }

    /// <summary>Logs a completed sale.</summary>
    public void LogSale(Item soldItem)
    {
        ArgumentNullException.ThrowIfNull(soldItem);
        this._logger.LogInformation("Bot sold item {Item}, value {Value}", soldItem, CalculateItemValue(soldItem));
    }

    /// <summary>Logs completed equipment changes.</summary>
    public void LogEquip(Item equippedItem)
    {
        ArgumentNullException.ThrowIfNull(equippedItem);
        this._logger.LogInformation("Bot equipped item {Item}, value {Value}", equippedItem, CalculateItemValue(equippedItem));
    }

    private static bool HasProtectedOptions(Item item)
        => item.ItemSetGroups.Any(s => s.AncientSetDiscriminator != 0)
            || item.SocketCount > 0
            || item.ItemOptions.Any(o => o.ItemOption?.OptionType == ItemOptionTypes.Excellent
                || o.ItemOption?.OptionType == ItemOptionTypes.Luck);

    private static float CalculateItemValue(Item item)
        => PriceCalculator.CalculateSellingPrice(item, item.Durability());
}

