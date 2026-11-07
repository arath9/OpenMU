// <copyright file="ProtagonistTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.Offline;

using MUnique.OpenMU.GameLogic.Bots;

[TestFixture]
public class ProtagonistTests
{
    [Test]
    public void OrdinaryBotManagerRetainsReflectionCompatibleDefaultConstructor()
    {
        Assert.That(Activator.CreateInstance<BotManager>(), Is.Not.Null);
    }

    [Test]
    public async Task LeilaOnlyChargesEachDemandOnceAsync()
    {
        var game = GameContextTestHelper.CreateGameContext();
        var human = await PlayerTestHelper.CreatePlayerAsync(game).ConfigureAwait(false);
        human.Money = 50000;
        var world = new ProtagonistWorld(new ProtagonistConfiguration());
        await using var leila = (BotProtagonist_LeilaMasa)world.CreateBot(game, 2);
        leila.RequestZen(human, DateTimeOffset.UtcNow);
        Assert.That(leila.TryPayDemand(human), Is.True);
        Assert.That(leila.TryPayDemand(human), Is.False);
        Assert.That(human.Money, Is.EqualTo(40000));
        await human.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public void SpotStealDurationIsValidated()
    {
        var configuration = new ProtagonistConfiguration { SpotStealDuration = TimeSpan.Zero };
        Assert.Throws<ArgumentException>(configuration.Validate);
    }

    [Test]
    public async Task GameServerSpawnsFourPersistentProtagonistsAndStopsThemAsync()
    {
        var provider = new MUnique.OpenMU.Persistence.InMemory.InMemoryPersistenceContextProvider();
        using var persistence = provider.CreateNewContext();
        var configuration = persistence.CreateNew<MUnique.OpenMU.Persistence.BasicModel.GameConfiguration>();
        new MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix.GameConfigurationInitializer(persistence, configuration).Initialize();
        var defaults = new ProtagonistConfiguration();
        Assert.That(defaults.Patrol.Select(point => point.Map).Distinct().Count(), Is.GreaterThan(1), "The wanderer must visit multiple allowed maps by default.");
        foreach (var point in defaults.Patrol)
        {
            var terrain = new MUnique.OpenMU.GameLogic.GameMapTerrain(configuration.Maps.Single(map => map.Number == point.Map));
            Assert.That(terrain.WalkMap[point.X, point.Y], Is.True, $"Default patrol waypoint {point} must be walkable on stock Season 6 terrain.");
        }

        var lorencia = configuration.Maps.Single(m => m.Number == 0);
        lorencia.MonsterSpawns.Clear();
        var serverConfiguration = persistence.CreateNew<MUnique.OpenMU.Persistence.BasicModel.GameServerConfiguration>();
        serverConfiguration.MaximumPlayers = 100;
        serverConfiguration.Maps.Add(lorencia);
        var definition = new MUnique.OpenMU.Persistence.BasicModel.GameServerDefinition
        {
            ServerID = 1,
            GameConfiguration = configuration,
            ServerConfiguration = serverConfiguration,
            PvpEnabled = true,
        };
        var logger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger>();
        logger.Setup(log => log.IsEnabled(Moq.It.IsAny<Microsoft.Extensions.Logging.LogLevel>())).Returns(true);
        var loggerFactory = new Moq.Mock<Microsoft.Extensions.Logging.ILoggerFactory>();
        loggerFactory.Setup(factory => factory.CreateLogger(Moq.It.IsAny<string>())).Returns(logger.Object);
        var plugins = new MUnique.OpenMU.PlugIns.PlugInManager([], loggerFactory.Object, null, null);
        await using var server = new MUnique.OpenMU.GameServer.GameServer(definition,
            new Moq.Mock<MUnique.OpenMU.Interfaces.IGuildServer>().Object,
            new Moq.Mock<MUnique.OpenMU.Interfaces.IEventPublisher>().Object,
            new Moq.Mock<MUnique.OpenMU.Interfaces.ILoginServer>().Object, provider,
            new Moq.Mock<MUnique.OpenMU.Interfaces.IFriendServer>().Object,
            loggerFactory.Object, plugins,
            new MUnique.OpenMU.GameLogic.ConfigurationChangeMediator());
        await server.StartAsync().ConfigureAwait(false);
        var feature = server.Context.FeaturePlugIns.GetPlugIn<ProtagonistFeaturePlugIn>();
        Assert.That(feature, Is.Not.Null, "The real GameServer must discover the feature.");
        feature!.Configuration = new ProtagonistConfiguration { Enabled = true };
        var game = (MUnique.OpenMU.GameLogic.GameContext)server.Context;
        // First heartbeat creates the state; ForceStart skips only its documented 15-second startup delay.
        await feature.ExecuteTaskAsync(game).ConfigureAwait(false);
        feature.ForceStart();
        await feature.ExecuteTaskAsync(game).ConfigureAwait(false);
        Assert.That(game.PlayerCount, Is.EqualTo(4), "All protagonists must enter the real GameServer world.");
        feature.ForceStart();
        await feature.ExecuteTaskAsync(game).ConfigureAwait(false);
        Assert.That(game.PlayerCount, Is.EqualTo(4), "Maintenance must be idempotent.");
        using var playerContext = provider.CreateNewPlayerContext(configuration);
        for (var i = 0; i < 4; i++)
        {
            var account = await playerContext.GetAccountByLoginNameAsync($"muhero{i + 1}").ConfigureAwait(false);
            Assert.That(account, Is.Not.Null);
            Assert.That(account!.IsBot, Is.True);
            Assert.That(account.Characters.Single().Attributes.Single(a => a.Definition == MUnique.OpenMU.GameLogic.Attributes.Stats.BaseStrength).Value, Is.GreaterThan(30000));
        }

        // Verify the real death-hook-to-chat route and a real Ale inventory debit in this world.
        var protagonists = new List<MUnique.OpenMU.GameLogic.Player>();
        await game.ForEachPlayerAsync(player =>
        {
            protagonists.Add(player);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        var hunter = protagonists.OfType<BotProtagonist_elmandolion>().Single();
        var bichi = protagonists.OfType<BotProtagonist_elBichiCome>().Single();
        foreach (var protagonist in protagonists.OfType<BotProtagonist>())
        {
            await protagonist.RunPersistenceExclusiveAsync(() => protagonist.TickProtagonistAsync()).ConfigureAwait(false);
        }

        // The existing navigator jitters its first evaluation by 2-3.5 seconds. Poll beyond
        // that delay rather than mistaking a not-yet-started timer for failed navigation.
        for (var attempt = 0; attempt < 12 && protagonists.All(bot => bot.Position == new MUnique.OpenMU.Pathfinding.Point(130, 130)); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }
        Assert.That(protagonists.OfType<BotProtagonist>().Any(bot => bot.Position != new MUnique.OpenMU.Pathfinding.Point(130, 130)), Is.True,
            "Live BotNavigator/helper timers must move a protagonist. " + string.Join(" | ", protagonists.OfType<BotProtagonist>().Select(bot => $"{bot.Name}: pos={bot.Position} alive={bot.IsAlive} state={bot.PlayerState.CurrentState} walking={bot.IsWalking} pending={bot.PendingBotActions.Count} dest={bot.Destination} gridStart={bot.CurrentMap!.Terrain.AIgrid[130, 130]} gridGoal={bot.CurrentMap.Terrain.AIgrid[130, 178]}")) + string.Join(" || ", logger.Invocations.Where(call => call.Method.Name == "Log" && (Microsoft.Extensions.Logging.LogLevel)call.Arguments[0] >= Microsoft.Extensions.Logging.LogLevel.Error).Select(call => $"{call.Arguments[2]}: {call.Arguments[3]}").TakeLast(8)));
        feature.Configuration.InterventionProbability = 0;
        feature.Configuration.AlePartyProbability = 0;
        await using (var visitor = await PlayerTestHelper.CreatePlayerAsync(game).ConfigureAwait(false))
        {
            visitor.SelectedCharacter!.Name = "Visitor";
            visitor.SelectedCharacter.PositionX = hunter.Position.X;
            visitor.SelectedCharacter.PositionY = hunter.Position.Y;
            await visitor.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);
            await visitor.MoveAsync(hunter.Position).ConfigureAwait(false);
            var chat = Moq.Mock.Get(visitor.ViewPlugIns.GetPlugIn<MUnique.OpenMU.GameLogic.Views.IChatViewPlugIn>()!);
            await feature.AttackableGotKilledAsync(visitor, hunter).ConfigureAwait(false);
            await hunter.RunPersistenceExclusiveAsync(() => hunter.DrainPendingBotActionsAsync()).ConfigureAwait(false);
            chat.Verify(view => view.ChatMessageAsync("Comiste pete", hunter.Name, MUnique.OpenMU.GameLogic.Views.ChatMessageType.Normal), Moq.Times.Once);

            await visitor.MoveAsync(bichi.Position).ConfigureAwait(false);
            var ale = visitor.PersistenceContext.CreateNew<MUnique.OpenMU.DataModel.Entities.Item>();
            ale.Definition = configuration.Items.Single(item => item.Group == 14 && item.Number == 9);
            ale.Durability = 2;
            Assert.That(await visitor.Inventory!.AddItemAsync(ale).ConfigureAwait(false), Is.True);
            await feature.ChatMessageSentAsync(visitor, "elBichiCome ale", MUnique.OpenMU.GameLogic.Views.ChatMessageType.Normal, null).ConfigureAwait(false);
            Assert.That(ale.Durability, Is.EqualTo(1), "A gift consumes one unit, not a whole stack.");
            await feature.ChatMessageSentAsync(visitor, "elBichiCome ale", MUnique.OpenMU.GameLogic.Views.ChatMessageType.Normal, null).ConfigureAwait(false);
            Assert.That(visitor.Inventory.Items.Contains(ale), Is.False, "The last unit must really leave the inventory.");
            await feature.ChatMessageSentAsync(visitor, "elBichiCome ale", MUnique.OpenMU.GameLogic.Views.ChatMessageType.Normal, null).ConfigureAwait(false);
            Assert.That(visitor.Inventory.Items.Any(), Is.False, "A chat mention cannot fabricate Ale.");
            await bichi.RunPersistenceExclusiveAsync(() => bichi.DrainPendingBotActionsAsync()).ConfigureAwait(false);
        }

        feature.Configuration.Enabled = false;
        feature.ForceStart();
        await feature.ExecuteTaskAsync(game).ConfigureAwait(false);
        Assert.That(game.PlayerCount, Is.Zero, "Disable must disconnect all four bots without deleting their accounts.");
    }

    [Test]
    public void ProbabilityGateHonorsBoundariesAndConsumesFailedRollCooldown()
    {
        var now = DateTimeOffset.UnixEpoch;
        var gate = new ProtagonistChanceGate();
        Assert.That(gate.TryTrigger(now, TimeSpan.FromMinutes(1), 0, 0), Is.False);
        Assert.That(gate.TryTrigger(now, TimeSpan.FromMinutes(1), 1, 0), Is.False);
        Assert.That(gate.TryTrigger(now.AddMinutes(1), TimeSpan.FromMinutes(1), 1, 0.999), Is.True);
    }

    [Test]
    public async Task AllFourCharactersUseBotManagerAndKeepCanonicalIdentityAsync()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var configuration = new ProtagonistConfiguration();
        var world = new ProtagonistWorld(configuration);
        var bots = Enumerable.Range(0, 4).Select(i => world.CreateBot(context, i)).ToArray();
        Assert.That(bots.Select(b => b.Identity), Is.EqualTo(new[] { "elmandolion", "bertoSalamin", "LeilaMasa", "elBichiCome" }));
        Assert.That(bots.Select(b => b.GetType().Name), Is.EqualTo(new[] { "BotProtagonist_elmandolion", "BotProtagonist_bertoSalamin", "BotProtagonist_LeilaMasa", "BotProtagonist_elBichiCome" }));
        Assert.That(bots.All(b => b is BotPlayer), Is.True);
        foreach (var bot in bots)
        {
            await bot.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public void DefaultsDoNotEnablePlayerHunting()
    {
        var configuration = new ProtagonistConfiguration();
        Assert.That(configuration.Enabled, Is.False);
        Assert.That(configuration.AllowPlayerHunting, Is.False);
        Assert.DoesNotThrow(configuration.Validate);
        configuration.BetrayalProbability = double.NaN;
        Assert.Throws<ArgumentException>(configuration.Validate);
    }

    [Test]
    public void HuntStartsAfterTwoHoursAndExpiresWithoutCatchUpBurst()
    {
        var now = DateTimeOffset.UnixEpoch;
        var schedule = new ProtagonistSchedule(new ProtagonistConfiguration(), now);
        Assert.That(schedule.IsHunting(now.AddMinutes(119)), Is.False);
        Assert.That(schedule.IsHunting(now.AddHours(2)), Is.True);
        Assert.That(schedule.IsHunting(now.AddHours(2).AddMinutes(11)), Is.False);
        Assert.That(schedule.IsHunting(now.AddHours(4)), Is.True);
        Assert.That(schedule.IsHunting(now.AddDays(20)), Is.True);
        Assert.That(schedule.IsHunting(now.AddDays(20).AddMinutes(11)), Is.False);
    }
}
