// <copyright file="YarnBotIntegrationTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Configuration.Quests;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Bots;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;

/// <summary>Verifies real protagonists, queues, chat views, inventory and quest states without a database.</summary>
[TestFixture]
[NonParallelizable]
public class YarnBotIntegrationTests
{
    /// <summary>Verifies four bots deliver yarn and keep visitor sessions separate.</summary>
    [Test]
    public async Task FourBotsDeliverYarnAndKeepVisitorSessionsSeparate()
    {
        await WithWorldAsync(async (context, bots, visitor, plugin) =>
        {
            var chat = Mock.Get(visitor.ViewPlugIns.GetPlugIn<IChatViewPlugIn>()!);
            foreach (var bot in bots)
            {
                await plugin.ChatMessageSentAsync(visitor, bot.Name + " hola", ChatMessageType.Normal, null).ConfigureAwait(false);
                await bot.RunPersistenceExclusiveAsync(() => bot.DrainPendingBotActionsAsync()).ConfigureAwait(false);
                chat.Verify(view => view.ChatMessageAsync(It.Is<string>(text => text.Contains("Visitor")), bot.Name, ChatMessageType.Normal), Times.Once);
            }

            var mandolion = bots[0];
            await mandolion.RunPersistenceExclusiveAsync(() => mandolion.ReactToChatAsync(visitor, mandolion.Name + " 1")).ConfigureAwait(false);
            chat.Verify(view => view.ChatMessageAsync(It.Is<string>(text => text.Contains("Lorencia") && text.Contains("refugio")), mandolion.Name, ChatMessageType.Normal), Times.Once);
            var before = chat.Invocations.Count;
            await mandolion.RunPersistenceExclusiveAsync(() => mandolion.ReactToChatAsync(visitor, mandolion.Name + " hola")).ConfigureAwait(false);
            Assert.That(chat.Invocations.Count, Is.EqualTo(before), "Restart cooldown must be independent of ambient speech.");

            await using var other = await PlayerTestHelper.CreatePlayerAsync(context).ConfigureAwait(false);
            other.SelectedCharacter!.Name = "Other";
            other.SelectedCharacter.PositionX = visitor.Position.X;
            other.SelectedCharacter.PositionY = visitor.Position.Y;
            await other.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);
            await mandolion.RunPersistenceExclusiveAsync(() => mandolion.ReactToChatAsync(other, mandolion.Name + " hola")).ConfigureAwait(false);
            Mock.Get(other.ViewPlugIns.GetPlugIn<IChatViewPlugIn>()!).Verify(view => view.ChatMessageAsync(It.Is<string>(text => text.Contains("Other")), mandolion.Name, ChatMessageType.Normal), Times.Once);
        }).ConfigureAwait(false);
    }

    /// <summary>Verifies commands transfer owned item and start real quest.</summary>
    [Test]
    public async Task CommandsTransferOwnedItemAndStartRealQuest()
    {
        await WithWorldAsync(async (context, bots, visitor, plugin) =>
        {
            var bot = bots[1];
            var definition = context.Configuration.Items.Single(item => item.Group == 14 && item.Number == 0);
            var item = bot.PersistenceContext.CreateNew<Item>();
            item.Definition = definition;
            item.Durability = 1;
            Assert.That(await bot.Inventory!.AddItemAsync(item).ConfigureAwait(false), Is.True);
            var lines = new List<string>();
            ValueTask Say(string text) { lines.Add(text); return ValueTask.CompletedTask; }
            await bot.RunPersistenceExclusiveAsync(() => YarnCommandHandlers.ExecuteAsync("GiveItem \"Apple\" 1", bot, visitor, Say)).ConfigureAwait(false);
            Assert.That(bot.Inventory.Items.Contains(item), Is.False, $"{definition.Name} tradable={definition.IsTradable} bound={definition.IsBoundToCharacter}, bot={bot.PlayerState.CurrentState}, eligible={bot.CanDiscussWith(visitor)}, slot={item.ItemSlot} minimum={MUnique.OpenMU.DataModel.InventoryConstants.EquippableSlotsCount} dura={item.Durability} stock={string.Join(";", bot.Inventory.Items.Select(stock => $"[{stock.Definition?.Name}]:{stock.ItemSlot}/{stock.Durability}"))}, output={string.Join(" | ", lines)}");
            Assert.That(visitor.Inventory!.Items.Contains(item), Is.True);
            var count = visitor.Inventory.Items.Count();
            await bot.RunPersistenceExclusiveAsync(() => YarnCommandHandlers.ExecuteAsync("GiveItem \"Apple\" 2", bot, visitor, Say)).ConfigureAwait(false);
            Assert.That(visitor.Inventory.Items.Count(), Is.EqualTo(count), "Missing stock must never mint rewards.");

            var quest = visitor.PersistenceContext.CreateNew<QuestDefinition>();
            quest.Group = 100;
            quest.Number = 0;
            quest.MinimumCharacterLevel = 0;
            context.Configuration.Monsters.First().Quests.Add(quest);
            await bot.RunPersistenceExclusiveAsync(() => YarnCommandHandlers.ExecuteAsync("StartQuest \"100:0\"", bot, visitor, Say)).ConfigureAwait(false);
            Assert.That(visitor.SelectedCharacter!.QuestStates.Single(state => state.Group == 100).ActiveQuest, Is.SameAs(quest));
            await bot.RunPersistenceExclusiveAsync(() => YarnCommandHandlers.ExecuteAsync("StartQuest \"100:0\"", bot, visitor, Say)).ConfigureAwait(false);
            Assert.That(visitor.SelectedCharacter.QuestStates.Count(state => state.Group == 100), Is.EqualTo(1));
        }).ConfigureAwait(false);
    }

    /// <summary>Verifies full inventory rolls back the entire gift.</summary>
    [Test]
    public async Task FullInventoryRollsBackTheEntireGift()
    {
        await WithWorldAsync(async (context, bots, visitor, plugin) =>
        {
            var bot = bots[1];
            var definition = context.Configuration.Items.Single(item => item.Group == 14 && item.Number == 0);
            var stock = new List<Item>();
            for (var index = 0; index < 2; index++)
            {
                var item = bot.PersistenceContext.CreateNew<Item>();
                item.Definition = definition;
                item.Durability = 1;
                Assert.That(await bot.Inventory!.AddItemAsync(item).ConfigureAwait(false), Is.True);
                stock.Add(item);
            }

            var inventory = visitor.Inventory!;
            Item? last = null;
            while (true)
            {
                var filler = visitor.PersistenceContext.CreateNew<Item>();
                filler.Definition = definition;
                filler.Durability = 1;
                if (!await inventory.AddItemAsync(filler).ConfigureAwait(false))
                {
                    visitor.PersistenceContext.Detach(filler);
                    break;
                }

                last = filler;
            }

            Assert.That(last, Is.Not.Null);
            await inventory.RemoveItemAsync(last!).ConfigureAwait(false); // Exactly one slot for a two-object gift.
            var previousItems = inventory.Items.ToArray();
            var previousSlots = stock.Select(item => item.ItemSlot).ToArray();
            await bot.RunPersistenceExclusiveAsync(() => YarnCommandHandlers.ExecuteAsync("GiveItem 14:0 2", bot, visitor, _ => ValueTask.CompletedTask)).ConfigureAwait(false);
            Assert.That(inventory.Items, Is.EquivalentTo(previousItems));
            Assert.That(stock.All(item => bot.Inventory!.Items.Contains(item)), Is.True);
            Assert.That(stock.Select(item => item.ItemSlot), Is.EqualTo(previousSlots));
        }).ConfigureAwait(false);
    }

    /// <summary>Verifies commands reject invalid arguments and out of range visitors.</summary>
    [Test]
    public async Task CommandsRejectInvalidArgumentsAndOutOfRangeVisitors()
    {
        await WithWorldAsync(async (context, bots, visitor, plugin) =>
        {
            var bot = bots[0];
            foreach (var command in new[] { "GiveItem Apple 0", "GiveItem Apple -1", "GiveItem Apple 9999999999999", "StartQuest ../x", "RunScript whatever" })
            {
                Assert.ThrowsAsync<FormatException>(async () => await bot.RunPersistenceExclusiveAsync(() => YarnCommandHandlers.ExecuteAsync(command, bot, visitor, _ => ValueTask.CompletedTask)).ConfigureAwait(false));
            }

            var lines = new List<string>();
            ValueTask Say(string line) { lines.Add(line); return ValueTask.CompletedTask; }
            await bot.RunPersistenceExclusiveAsync(() => YarnCommandHandlers.ExecuteAsync("ShowLore nonexistent", bot, visitor, Say)).ConfigureAwait(false);
            Assert.That(lines, Is.EqualTo(new[] { "No conozco nada sobre eso." }));
            lines.Clear();
            await visitor.MoveAsync(new MUnique.OpenMU.Pathfinding.Point(1, 1)).ConfigureAwait(false);
            await bot.RunPersistenceExclusiveAsync(() => YarnCommandHandlers.ExecuteAsync("ShowLore lorencia", bot, visitor, Say)).ConfigureAwait(false);
            Assert.That(lines, Is.Empty, "Queued commands must recheck range, not trust the original chat event.");
        }).ConfigureAwait(false);
    }

    private static async Task WithWorldAsync(Func<GameContext, BotProtagonist[], Player, ProtagonistFeaturePlugIn, Task> assertion)
    {
        var provider = new InMemoryPersistenceContextProvider();
        await new Persistence.Initialization.VersionSeasonSix.DataInitialization(provider, NullLoggerFactory.Instance)
            .CreateInitialDataAsync(1, false).ConfigureAwait(false);
        using var persistence = provider.CreateNewContext();
        var configuration = (await persistence.GetAsync<GameConfiguration>().ConfigureAwait(false)).Single();
        foreach (var map in configuration.Maps)
        {
            map.MonsterSpawns.Clear();
        }

        var manager = new PlugInManager(null, NullLoggerFactory.Instance, null, null);
        var plugin = new ProtagonistFeaturePlugIn();
        manager.RegisterPlugInAtPlugInPoint<IFeaturePlugIn>(plugin);
        var initializer = new MapInitializer(configuration, NullLogger<MapInitializer>.Instance, NullDropGenerator.Instance, null);
        await using var context = new GameContext(configuration, provider, initializer, NullLoggerFactory.Instance, manager, NullDropGenerator.Instance, new ConfigurationChangeMediator());
        initializer.PlugInManager = manager;
        initializer.PathFinderPool = context.PathFinderPool;
        var settings = new ProtagonistConfiguration { Enabled = true, ServerId = 0 };
        plugin.Configuration = settings;
        try
        {
            await plugin.ExecuteTaskAsync(context).ConfigureAwait(false);
            plugin.ForceStart();
            await plugin.ExecuteTaskAsync(context).ConfigureAwait(false);
            var bots = (await context.GetPlayersAsync().ConfigureAwait(false)).OfType<BotProtagonist>().OrderBy(bot => bot.Account!.LoginName).ToArray();
            Assert.That(bots, Has.Length.EqualTo(4));
            foreach (var bot in bots)
            {
                // Stop only private AI components, not StopAsync (which logs the character out).
                foreach (var component in new[]
                {
                    typeof(BotPlayer).GetField("_navigator", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!,
                    typeof(MUnique.OpenMU.GameLogic.Offline.OfflinePlayer).GetField("_intelligence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!,
                })
                {
                    if (component.GetValue(bot) is IAsyncDisposable disposable)
                    {
                        await disposable.DisposeAsync().ConfigureAwait(false);
                    }

                    component.SetValue(bot, null);
                }
            }

            await using var visitor = await PlayerTestHelper.CreatePlayerAsync(context).ConfigureAwait(false);
            visitor.SelectedCharacter!.Name = "Visitor";
            Mock.Get(visitor.SelectedCharacter).Setup(character => character.QuestStates).Returns(new List<CharacterQuestState>());
            visitor.SelectedCharacter.PositionX = bots[0].Position.X;
            visitor.SelectedCharacter.PositionY = bots[0].Position.Y;
            await visitor.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);
            await assertion(context, bots, visitor, plugin).ConfigureAwait(false);
        }
        finally
        {
            settings.Enabled = false;
            plugin.ForceStart();
            await plugin.ExecuteTaskAsync(context).ConfigureAwait(false);
        }
    }
}
