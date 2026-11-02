// <copyright file="YarnCommandHandlers.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using System.Globalization;
using System.Text;
using System.Threading;
using MUnique.OpenMU.DataModel;
using MUnique.OpenMU.GameLogic.PlayerActions.Quests;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.Views.Inventory;

/// <summary>Allowlisted, awaited world commands emitted by trusted Yarn programs, never by player chat.</summary>
internal static class YarnCommandHandlers
{
    /// <summary>Validates and executes an allowlisted command against the initiating human.</summary>
    /// <param name="command">The server-emitted command text.</param>
    /// <param name="bot">The bot whose serialized queue owns this execution.</param>
    /// <param name="player">The initiating human.</param>
    /// <param name="say">The awaited output sink.</param>
    internal static async ValueTask ExecuteAsync(string command, BotProtagonist bot, Player player, Func<string, ValueTask> say)
    {
        if (!bot.CanDiscussWith(player))
        {
            return;
        }

        var arguments = Split(command);
        switch (arguments)
        {
            case ["ShowLore", var topic]:
                await say(topic.ToLowerInvariant() switch
                {
                    "lorencia" => "Lorencia es un refugio para viajeros. Fuera de sus muros, prepara tu equipo y busca aliados.",
                    "mercado" => "En el mercado de Lorencia, revisa cada objeto y confirma el trato antes de entregar tu Zen.",
                    "leila" => "Leila protege su spot y nunca olvida una deuda. Una sonrisa no siempre es una promesa.",
                    _ => "No conozco nada sobre eso.",
                }).ConfigureAwait(false);
                return;
            case ["GiveItem", var itemName, var amount] when int.TryParse(amount, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) && quantity is > 0 and <= 32:
                await WithPlayerLockAsync(player, say, async () =>
                {
                    if (bot.CanDiscussWith(player))
                    {
                        await GiveItemAsync(bot, player, itemName, quantity, say).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
                return;
            case ["StartQuest", var questId]:
                var parts = questId.Split(':');
                if (parts.Length != 2 || !short.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var group)
                    || !short.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    throw new FormatException("StartQuest requires a nonnegative group:number identifier.");
                }

                await WithPlayerLockAsync(player, say, async () =>
                {
                    if (!bot.CanDiscussWith(player))
                    {
                        return;
                    }

                    var quest = player.GameContext.Configuration.Monsters.SelectMany(monster => monster.Quests)
                        .FirstOrDefault(candidate => candidate.Group == group && candidate.Number == number
                            && (candidate.QualifiedCharacter is null || Equals(candidate.QualifiedCharacter, player.SelectedCharacter!.CharacterClass)));
                    if (quest is null)
                    {
                        await say("Esa mision no esta disponible para tu personaje.").ConfigureAwait(false);
                        return;
                    }

                    await new QuestStartAction().StartQuestAsync(player, quest).ConfigureAwait(false);
                    await say(player.GetQuestState(group)?.ActiveQuest == quest ? "Mision activa." : "No cumples los requisitos de esa mision.").ConfigureAwait(false);
                }).ConfigureAwait(false);
                return;
            default:
                throw new FormatException($"Unsupported Yarn command or invalid arguments: {command}");
        }
    }

    /// <summary>Tokenizes Yarn commands, preserving quoted names and rejecting malformed strings.</summary>
    /// <param name="command">The server-emitted command text.</param>
    internal static string[] Split(string command)
    {
        if (command.Length > 512)
        {
            throw new FormatException("Yarn command is too long.");
        }

        var result = new List<string>();
        var token = new StringBuilder();
        var quoted = false;
        var escaped = false;
        var started = false;
        foreach (var character in command)
        {
            if (escaped)
            {
                token.Append(character);
                escaped = false;
            }
            else if (character == '\\' && quoted)
            {
                escaped = true;
            }
            else if (character == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (started)
                {
                    result.Add(token.ToString());
                    token.Clear();
                    started = false;
                }
            }
            else
            {
                token.Append(character);
                started = true;
            }
        }

        if (quoted || escaped)
        {
            throw new FormatException("Unterminated quoted Yarn argument.");
        }

        if (started)
        {
            result.Add(token.ToString());
        }

        return result.ToArray();
    }

    // The bot queue already holds its persistence lock. A bounded acquisition of the human lock
    // breaks a potential inverse-order wait (combat/trade/save); never wait indefinitely here.
    private static async ValueTask WithPlayerLockAsync(Player player, Func<string, ValueTask> say, Func<ValueTask> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await player.RunPersistenceExclusiveAsync(action, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            await say("Estas ocupado. Intenta de nuevo mas tarde.").ConfigureAwait(false);
        }
    }

    private static async ValueTask GiveItemAsync(BotProtagonist bot, Player player, string itemName, int quantity, Func<string, ValueTask> say)
    {
        if (bot.Inventory is not { } source || player.Inventory is not { } destination
            || bot.PlayerState.CurrentState != PlayerState.EnteredWorld
            || bot.ShopStorage?.StoreOpen == true || player.ShopStorage?.StoreOpen == true)
        {
            await say("No puedo entregar objetos ahora.").ConfigureAwait(false);
            return;
        }

        // Transfer existing objects only. Quantity means units, not whole potion stacks.
        // Partial-stack splitting is deliberately rejected rather than minting a replacement object.
        var remaining = quantity;
        var selected = new List<(Item Item, byte Slot)>();
        foreach (var item in source.Items.Where(item => item.ItemSlot >= InventoryConstants.EquippableSlotsCount
            && item.Definition is { IsTradable: true, IsBoundToCharacter: false } definition
            && (string.Equals(definition.Name.GetTranslation(CultureInfo.InvariantCulture), itemName, StringComparison.OrdinalIgnoreCase)
                || $"{definition.Group}:{definition.Number}" == itemName)))
        {
            var units = item.IsStackable() ? (int)item.Durability : 1;
            if (units <= 0 || units > remaining || (item.IsStackable() && item.Durability != units))
            {
                continue;
            }

            selected.Add((item, item.ItemSlot));
            remaining -= units;
            if (remaining == 0)
            {
                break;
            }
        }

        if (remaining != 0)
        {
            await say("No tengo esa cantidad disponible en objetos o pilas completas.").ConfigureAwait(false);
            return;
        }

        // Match the normal trade persistence ownership handoff, with all-or-nothing inventory rollback.
        using var trade = bot.GameContext.PersistenceContextProvider.CreateNewTradeContext();
        var detached = new HashSet<Item>();
        var committed = false;
        try
        {
            foreach (var entry in selected)
            {
                trade.Attach(entry.Item);
                await source.RemoveItemAsync(entry.Item).ConfigureAwait(false);
                if (!await destination.AddItemAsync(entry.Item).ConfigureAwait(false))
                {
                    await say("No tienes espacio en el inventario.").ConfigureAwait(false);
                    return;
                }
            }

            foreach (var entry in selected)
            {
                bot.PersistenceContext.Detach(entry.Item);
                detached.Add(entry.Item);
            }

            if (!await trade.SaveChangesAsync().ConfigureAwait(false))
            {
                throw new InvalidOperationException("Item ownership could not be persisted.");
            }

            committed = true;
            foreach (var entry in selected)
            {
                player.PersistenceContext.Attach(entry.Item);
            }
        }
        finally
        {
            if (!committed)
            {
                foreach (var entry in selected)
                {
                    if (destination.Items.Contains(entry.Item))
                    {
                        await destination.RemoveItemAsync(entry.Item).ConfigureAwait(false);
                    }

                    if (!source.Items.Contains(entry.Item) && !await source.AddItemAsync(entry.Slot, entry.Item).ConfigureAwait(false))
                    {
                        bot.Logger.LogCritical("Failed to restore Yarn gift {Item} to its original inventory.", entry.Item);
                    }

                    if (detached.Contains(entry.Item))
                    {
                        bot.PersistenceContext.Attach(entry.Item);
                    }
                }
            }
        }

        await player.InvokeViewPlugInAsync<IUpdateInventoryListPlugIn>(view => view.UpdateInventoryListAsync()).ConfigureAwait(false);
        await say($"Recibiste {itemName} x{quantity}.").ConfigureAwait(false);
        foreach (var entry in selected)
        {
            bot.GameContext.PlugInManager.GetPlugInPoint<IItemTradedToOtherPlayerPlugIn>()?.ItemTraded(bot, player, entry.Item);
        }
    }
}
