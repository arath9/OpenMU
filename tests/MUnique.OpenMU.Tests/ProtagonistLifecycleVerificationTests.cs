// <copyright file="ProtagonistLifecycleVerificationTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Bots;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;

/// <summary>Runs the real generator, plugin and bot lifecycle against Season 6 in-memory persistence.</summary>
[TestFixture]
[NonParallelizable]
public class ProtagonistLifecycleVerificationTests
{
    /// <summary>Plugin registration must produce instances at every gameplay hook.</summary>
    [Test]
    public void ProtagonistIsRegisteredAtAllGameplayHooks()
    {
        var manager = new PlugInManager(null, NullLoggerFactory.Instance, null, null);
        manager.DiscoverAndRegisterPlugIns(typeof(GameContext).Assembly);
        Assert.That(manager.GetActivePlugInsOf<IFeaturePlugIn>(), Has.Some.InstanceOf<ProtagonistFeaturePlugIn>());
        Assert.That(manager.GetActivePlugInsOf<IPeriodicTaskPlugIn>(), Has.Some.InstanceOf<ProtagonistFeaturePlugIn>());
        Assert.That(manager.GetActivePlugInsOf<IChatMessageSentPlugIn>(), Has.Some.InstanceOf<ProtagonistFeaturePlugIn>());
        Assert.That(manager.GetActivePlugInsOf<IAttackableGotKilledPlugIn>(), Has.Some.InstanceOf<ProtagonistFeaturePlugIn>());
    }

    /// <summary>All four must really enter the world, generation must be idempotent and disable must log them out.</summary>
    [Test]
    public async Task SeasonSixProtagonistsSpawnOnceAndStopWhenDisabledAsync()
    {
        var provider = new InMemoryPersistenceContextProvider();
        await new Persistence.Initialization.VersionSeasonSix.DataInitialization(provider, NullLoggerFactory.Instance)
            .CreateInitialDataAsync(1, false).ConfigureAwait(false);
        using var persistence = provider.CreateNewContext();
        var configuration = (await persistence.GetAsync<GameConfiguration>().ConfigureAwait(false)).Single();
        foreach (var map in configuration.Maps)
        {
            map.MonsterSpawns.Clear(); // This lifecycle test must not create unrelated world monsters.
        }

        var manager = new PlugInManager(null, NullLoggerFactory.Instance, null, null);
        var plugin = new ProtagonistFeaturePlugIn();
        manager.RegisterPlugInAtPlugInPoint<IFeaturePlugIn>(plugin);
        var initializer = new MapInitializer(configuration, NullLogger<MapInitializer>.Instance, NullDropGenerator.Instance, null);
        var context = new GameContext(configuration, provider, initializer, NullLoggerFactory.Instance, manager, NullDropGenerator.Instance, new ConfigurationChangeMediator());
        initializer.PlugInManager = manager;
        initializer.PathFinderPool = context.PathFinderPool;
        var settings = new ProtagonistConfiguration { Enabled = true, ServerId = 0 };
        plugin.Configuration = settings;
        try
        {
            await plugin.ExecuteTaskAsync(context).ConfigureAwait(false); // Establishes its delayed startup state.
            plugin.ForceStart();
            await plugin.ExecuteTaskAsync(context).ConfigureAwait(false);
            var players = await context.GetPlayersAsync().ConfigureAwait(false);
            Assert.That(players, Has.Count.EqualTo(4), "The real GameContext must contain four entered protagonists.");
            Assert.That(players.All(player => player is BotProtagonist && player.Account!.IsBot && player.PlayerState.CurrentState == PlayerState.EnteredWorld), Is.True);
            Assert.That(players.Select(player => player.Name), Is.EquivalentTo(new[] { settings.ElmandolionName, settings.BertoName, settings.LeilaName, settings.BichiName }));
            Assert.That(players.Select(player => player.Account!.LoginName), Is.EquivalentTo(new[] { "muhero1", "muhero2", "muhero3", "muhero4" }));
            plugin.ForceStart();
            await plugin.ExecuteTaskAsync(context).ConfigureAwait(false);
            Assert.That(await context.GetPlayersAsync().ConfigureAwait(false), Has.Count.EqualTo(4), "Repeated maintenance must not duplicate bots.");
            await ProtagonistGenerator.EnsureAsync(context, settings).ConfigureAwait(false);
            Assert.That(await context.GetPlayersAsync().ConfigureAwait(false), Has.Count.EqualTo(4), "Repeated generation must preserve the active population.");
        }
        finally
        {
            settings.Enabled = false;
            plugin.ForceStart();
            await plugin.ExecuteTaskAsync(context).ConfigureAwait(false);
            Assert.That(await context.GetPlayersAsync().ConfigureAwait(false), Is.Empty, "Disabled protagonists must leave the world.");
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }
}
