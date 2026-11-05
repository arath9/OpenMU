// <copyright file="MandolionAutonomyTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Bots;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;

/// <summary>Exercises the real protagonist queue and world with no external server or database.</summary>
[TestFixture]
[NonParallelizable]
public class MandolionAutonomyTests
{
    private GameContext _context = null!;
    private ProtagonistFeaturePlugIn _feature = null!;
    private BotProtagonist_elmandolion _bot = null!;
    private string? _previousAutonomy;

    /// <summary>Creates the currently configured single-protagonist world and freezes only its timers.</summary>
    [SetUp]
    public async Task SetUpAsync()
    {
        this._previousAutonomy = Environment.GetEnvironmentVariable("LLM_AUTONOMY");
        Environment.SetEnvironmentVariable("LLM_AUTONOMY", "off");
        var provider = new InMemoryPersistenceContextProvider();
        using var persistence = provider.CreateNewContext();
        var configuration = persistence.CreateNew<Persistence.BasicModel.GameConfiguration>();
        new Persistence.Initialization.VersionSeasonSix.GameConfigurationInitializer(persistence, configuration).Initialize();
        foreach (var map in configuration.Maps)
        {
            map.MonsterSpawns.Clear();
        }

        var manager = new PlugInManager(null, NullLoggerFactory.Instance, null, null);
        this._feature = new ProtagonistFeaturePlugIn { Configuration = new ProtagonistConfiguration { Enabled = true, ServerId = 0 } };
        manager.RegisterPlugInAtPlugInPoint<IFeaturePlugIn>(this._feature);
        var initializer = new MapInitializer(configuration, NullLogger<MapInitializer>.Instance, NullDropGenerator.Instance, null);
        this._context = new GameContext(configuration, provider, initializer, NullLoggerFactory.Instance, manager, NullDropGenerator.Instance, new ConfigurationChangeMediator());
        initializer.PlugInManager = manager;
        initializer.PathFinderPool = this._context.PathFinderPool;
        await this._feature.ExecuteTaskAsync(this._context).ConfigureAwait(false);
        this._feature.ForceStart();
        await this._feature.ExecuteTaskAsync(this._context).ConfigureAwait(false);
        this._bot = (await this._context.GetPlayersAsync().ConfigureAwait(false)).OfType<BotProtagonist_elmandolion>().Single();
        foreach (var (type, name) in new[] { (typeof(BotPlayer), "_navigator"), (typeof(OfflinePlayer), "_intelligence") })
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (field.GetValue(this._bot) is IAsyncDisposable timer)
            {
                await timer.DisposeAsync().ConfigureAwait(false);
                field.SetValue(this._bot, null);
            }
        }

        this.Perception.Reset();
    }

    /// <summary>Stops owned players and restores the process setting.</summary>
    [TearDown]
    public async Task TearDownAsync()
    {
        Environment.SetEnvironmentVariable("LLM_AUTONOMY", this._previousAutonomy);
        this._feature.Configuration!.Enabled = false;
        this._feature.ForceStart();
        await this._feature.ExecuteTaskAsync(this._context).ConfigureAwait(false);
        await this._context.DisposeAsync().ConfigureAwait(false);
    }

    private NpcPerception Perception => (NpcPerception)typeof(BotProtagonist).GetField("_perception", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this._bot)!;

    /// <summary>The kill hook must not mutate another bot's perception until its queue runs.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task NearbyHumanDeathIsQueuedAsync(bool killedByBot)
    {
        await using var human = await this.CreateHumanAsync().ConfigureAwait(false);
        await this._feature.AttackableGotKilledAsync(human, killedByBot ? this._bot : null).ConfigureAwait(false);
        Assert.That(this.Perception.RecentEvents, Is.Empty);
        await this._bot.DrainPendingBotActionsAsync().ConfigureAwait(false);
        Assert.That(this.Perception.RecentEvents.Select(e => e.Kind), Is.EqualTo(new[] { killedByBot ? WorldEventKind.PlayerKilledByProtagonist : WorldEventKind.PlayerDied }));
        Assert.That(this.Perception.RecentEvents.Single().Who, Is.SameAs(human));
    }

    /// <summary>Death notifications are local, opt-in and human-only.</summary>
    [TestCase("far")]
    [TestCase("map")]
    [TestCase("disabled")]
    [TestCase("offline")]
    public async Task UnobservableDeathIsIgnoredAsync(string reason)
    {
        await using var human = await this.CreateHumanAsync().ConfigureAwait(false);
        if (reason == "far")
        {
            await human.MoveAsync(new Point(200, 200)).ConfigureAwait(false);
        }
        else if (reason == "map")
        {
            await human.WarpToAsync(new MUnique.OpenMU.DataModel.Configuration.ExitGate
            {
                Map = this._context.Configuration.Maps.Single(map => map.Number == 2),
                X1 = 211,
                X2 = 211,
                Y1 = 40,
                Y2 = 40,
            }).ConfigureAwait(false);
            await human.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);
        }
        else if (reason == "disabled")
        {
            this._feature.Configuration!.Enabled = false;
        }

        await this._feature.AttackableGotKilledAsync(reason == "offline" ? this._bot : human, null).ConfigureAwait(false);
        await this._bot.DrainPendingBotActionsAsync().ConfigureAwait(false);
        Assert.That(this.Perception.RecentEvents, Is.Empty);
    }

    private async ValueTask<Player> CreateHumanAsync()
    {
        var human = await PlayerTestHelper.CreatePlayerAsync(this._context).ConfigureAwait(false);
        human.SelectedCharacter!.Name = "Visitor";
        human.SelectedCharacter.PositionX = this._bot.Position.X;
        human.SelectedCharacter.PositionY = this._bot.Position.Y;
        await human.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);
        return human;
    }
}
