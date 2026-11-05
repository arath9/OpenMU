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

        await this._bot.DrainPendingBotActionsAsync().ConfigureAwait(false);
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

    /// <summary>Busy LLMs, recent conversations and cooldowns suppress spontaneous decisions.</summary>
    [TestCase("ready", true)]
    [TestCase("busy", false)]
    [TestCase("conversation", false)]
    [TestCase("cooldown", false)]
    [TestCase("off", false)]
    public async Task AutonomyGateHonorsConversationAndConcurrencyAsync(string state, bool expected)
    {
        await using var human = await this.CreateHumanAsync().ConfigureAwait(false);
        Environment.SetEnvironmentVariable("LLM_AUTONOMY", state == "off" ? "off" : "on");
        var now = DateTimeOffset.UtcNow;
        if (state == "busy")
        {
            this.SetBotField("_llmBusy", 1);
        }
        else if (state == "conversation")
        {
            var talks = (Dictionary<Player, DateTimeOffset>)this.GetBotField("_lastTalk")!;
            talks[human] = now;
        }
        else if (state == "cooldown")
        {
            this.SetBotField("_lastAutonomousDecision", now);
        }

        var gate = typeof(BotProtagonist).GetMethod("ShouldThinkAutonomously", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(gate, Is.Not.Null, "Autonomous gate is missing.");
        var attack = new WorldEvent(WorldEventKind.AttackedBy, human, "Visitor te atacó", now);
        Assert.That(gate!.Invoke(this._bot, new object[] { now, attack, 0.999 }), Is.EqualTo(expected));
    }

    /// <summary>An unprompted attack reaches the HTTP client off-tick and executes only through the queue.</summary>
    [TestCase("ready")]
    [TestCase("left")]
    [TestCase("disabled")]
    [TestCase("conversation")]
    [TestCase("off")]
    [TestCase("unknown")]
    [TestCase("silence")]
    [TestCase("fight")]
    [TestCase("http-error")]
    public async Task AutonomousTickUsesSituationAndQueuesActionAsync(string completionState)
    {
        await using var human = await this.CreateHumanAsync().ConfigureAwait(false);
        Environment.SetEnvironmentVariable("LLM_AUTONOMY", "on");
        using var integration = new BotLLMDialogueIntegration(new MUnique.OpenMU.GameLogic.Bots.LLMIntegration.LocalLLMClient.LLMConfig(), NullLoggerFactory.Instance);
        using var handler = new BrainHandler
        {
            Status = completionState == "http-error" ? System.Net.HttpStatusCode.InternalServerError : System.Net.HttpStatusCode.OK,
            Target = completionState == "unknown" ? "NotNearby" : "Visitor",
            Say = completionState == "silence" ? string.Empty : "Veni aca.",
            Action = completionState == "silence" ? "none" : completionState == "fight" ? "fight" : "follow",
        };
        using var http = new System.Net.Http.HttpClient(handler);
        var client = typeof(BotLLMDialogueIntegration).GetField("_llmClient", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(integration)!;
        client.GetType().GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, http);
        this.SetBotField("_llmIntegration", integration);
        this.SetBotField("_nextThink", DateTimeOffset.MinValue);
        this.Perception.AddEvent(WorldEventKind.AttackedBy, human, "Visitor te atacó");
        await this._bot.TickProtagonistAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var body = await handler.Request.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        Assert.That(body, Does.Contain("QUÉ ACABA DE PASAR").And.Contain("Visitor te atacó").And.Contain("Blade Master veterano"));
        Assert.That(this.GetBotField("_action"), Is.EqualTo(NpcActionKind.None));
        Assert.That(this.GetBotField("_llmBusy"), Is.EqualTo(1));
        Assert.That(this._bot.PendingBotActions.IsEmpty, Is.True, "Only the held HTTP completion may be awaited by this fixture.");
        handler.Release.SetResult();
        for (var i = 0; i < 100 && this._bot.PendingBotActions.IsEmpty; i++)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        if (completionState == "http-error")
        {
            for (var i = 0; i < 100 && (int)this.GetBotField("_llmBusy")! != 0; i++)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.That(this.GetBotField("_llmBusy"), Is.EqualTo(0));
            Assert.That(this._bot.PendingBotActions.IsEmpty, Is.True);
            return;
        }

        Assert.That(this._bot.PendingBotActions.IsEmpty, Is.False);
        Assert.That(this.GetBotField("_action"), Is.EqualTo(NpcActionKind.None));
        if (completionState == "left")
        {
            await human.MoveAsync(new Point(200, 200)).ConfigureAwait(false);
        }
        else if (completionState == "disabled")
        {
            this._feature.Configuration!.Enabled = false;
        }
        else if (completionState == "conversation")
        {
            ((Dictionary<Player, DateTimeOffset>)this.GetBotField("_lastTalk")!)[human] = DateTimeOffset.UtcNow;
        }
        else if (completionState == "off")
        {
            Environment.SetEnvironmentVariable("LLM_AUTONOMY", "off");
        }

        await this._bot.DrainPendingBotActionsAsync().ConfigureAwait(false);
        Assert.That(this.GetBotField("_llmBusy"), Is.EqualTo(0));
        Assert.That(this.GetBotField("_action"), Is.EqualTo(completionState == "ready" ? NpcActionKind.Follow : NpcActionKind.None));
        if (completionState == "ready")
        {
            Assert.That(this.GetBotField("_actionTarget"), Is.SameAs(human));
        }

        var chat = Moq.Mock.Get(human.ViewPlugIns.GetPlugIn<MUnique.OpenMU.GameLogic.Views.IChatViewPlugIn>()!);
        chat.Verify(view => view.ChatMessageAsync("Veni aca.", this._bot.Name, MUnique.OpenMU.GameLogic.Views.ChatMessageType.Normal), completionState is "ready" or "unknown" or "fight" ? Moq.Times.Once() : Moq.Times.Never());
    }

    /// <summary>Each event has its own probability, with no spontaneous reactions to departures or own kills.</summary>
    [TestCase(WorldEventKind.AttackedBy, 0.999, true)]
    [TestCase(WorldEventKind.PlayerDied, 0.599, true)]
    [TestCase(WorldEventKind.PlayerDied, 0.6, false)]
    [TestCase(WorldEventKind.PlayerArrived, 0.299, true)]
    [TestCase(WorldEventKind.PlayerArrived, 0.3, false)]
    [TestCase(WorldEventKind.Idle, 0.099, true)]
    [TestCase(WorldEventKind.Idle, 0.1, false)]
    [TestCase(WorldEventKind.PlayerLeft, 0, false)]
    [TestCase(WorldEventKind.PlayerKilledByProtagonist, 0, false)]
    public async Task EventGateHonorsProbabilityAsync(WorldEventKind kind, double roll, bool expected)
    {
        await using var human = await this.CreateHumanAsync().ConfigureAwait(false);
        Environment.SetEnvironmentVariable("LLM_AUTONOMY", "on");
        var now = DateTimeOffset.UtcNow;
        Assert.That(this._bot.ShouldThinkAutonomously(now, new WorldEvent(kind, human, "event", now), roll), Is.EqualTo(expected));
        ((Dictionary<Player, DateTimeOffset>)this.GetBotField("_lastAutonomousGreeting")!)[human] = now;
        Assert.That(this._bot.ShouldThinkAutonomously(now, new WorldEvent(WorldEventKind.PlayerArrived, human, "arrival", now), 0), Is.False);
        Assert.That(this._bot.ShouldThinkAutonomously(now, new WorldEvent(WorldEventKind.AttackedBy, human, "stale", now.AddMinutes(-1)), 0), Is.False);
    }

    /// <summary>Off keeps the old speech and northbound destination without dispatching autonomous work.</summary>
    [Test]
    public async Task OffPreservesLegacyRoutineAsync()
    {
        await using var human = await this.CreateHumanAsync().ConfigureAwait(false);
        this.SetBotField("_nextThink", DateTimeOffset.MinValue);
        this.Perception.AddEvent(WorldEventKind.AttackedBy, human, "attack");
        await this._bot.TickProtagonistAsync().ConfigureAwait(false);
        Assert.That(this.GetBotField("_llmBusy"), Is.EqualTo(0));
        Assert.That(this._bot.Destination, Is.EqualTo(new ProtagonistWaypoint(0, 130, 178)));
        var chat = Moq.Mock.Get(human.ViewPlugIns.GetPlugIn<MUnique.OpenMU.GameLogic.Views.IChatViewPlugIn>()!);
        chat.Verify(view => view.ChatMessageAsync("bertoSalamin, deja de esconderte detras de los debiles.", this._bot.Name, MUnique.OpenMU.GameLogic.Views.ChatMessageType.Normal), Moq.Times.Once);
    }

    /// <summary>RecentAggressor changes are perceived once, and idle is recorded after two quiet minutes.</summary>
    [Test]
    public async Task TickPerceivesAggressionAndIdleAsync()
    {
        await using var human = await this.CreateHumanAsync().ConfigureAwait(false);
        await this._bot.MoveAsync(new Point(130, 178)).ConfigureAwait(false);
        await human.MoveAsync(this._bot.Position).ConfigureAwait(false);
        Assert.That(human.IsAtSafezone(), Is.False);
        this._bot.RegisterAggressor(human);
        this.SetBotField("_nextThink", DateTimeOffset.MinValue);
        await this._bot.TickProtagonistAsync().ConfigureAwait(false);
        this.SetBotField("_nextThink", DateTimeOffset.MinValue);
        await this._bot.TickProtagonistAsync().ConfigureAwait(false);
        Assert.That(this.Perception.FindEvents(WorldEventKind.AttackedBy).Count(), Is.EqualTo(1));
        var events = (List<WorldEvent>)typeof(NpcPerception).GetField("_events", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(this.Perception)!;
        events.Clear();
        events.Add(new WorldEvent(WorldEventKind.PlayerArrived, human, "old arrival", DateTimeOffset.UtcNow.AddMinutes(-3)));
        this.SetBotField("_nextThink", DateTimeOffset.MinValue);
        await this._bot.TickProtagonistAsync().ConfigureAwait(false);
        Assert.That(this.Perception.FindEvents(WorldEventKind.Idle).Count(), Is.EqualTo(1));
    }

    private object? GetBotField(string name)
        => typeof(BotProtagonist).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(this._bot);

    private void SetBotField(string name, object value)
    {
        var field = typeof(BotProtagonist).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, $"Missing autonomous state: {name}");
        field!.SetValue(this._bot, value);
    }

    private sealed class BrainHandler : System.Net.Http.HttpMessageHandler
    {
        internal System.Net.HttpStatusCode Status { get; init; } = System.Net.HttpStatusCode.OK;

        internal string Say { get; init; } = "Veni aca.";

        internal string Action { get; init; } = "follow";

        internal string Target { get; init; } = "Visitor";

        internal TaskCompletionSource<string> Request { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            this.Request.SetResult(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            await this.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new System.Net.Http.HttpResponseMessage(this.Status)
            {
                Content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = System.Text.Json.JsonSerializer.Serialize(new { say = this.Say, action = this.Action, place = string.Empty, target = this.Target }) }, finish_reason = "stop" } },
                })),
            };
        }
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
