// <copyright file="ProtagonistFeaturePlugIn.cs" company="MUnique">

// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// </copyright>



namespace MUnique.OpenMU.GameLogic.Bots;



using System.Collections.Concurrent;

using System.Runtime.InteropServices;

using System.Threading;

using MUnique.OpenMU.GameLogic.Offline;

using MUnique.OpenMU.GameLogic.PlugIns;

using MUnique.OpenMU.GameLogic.Views;

using MUnique.OpenMU.GameLogic.Views.Inventory;

using MUnique.OpenMU.PlugIns;



/// <summary>Owns the four protagonists without spawning another random bot population or requiring an LLM.</summary>

[PlugIn]

[Guid("af83b745-584a-4a91-a9ad-6e8652aa2869")]

[Display(Name = "MU Legacy protagonists", Description = "Opt-in protagonists: deterministic routines, rivalries, Ale and Zen interactions.")]

public sealed class ProtagonistFeaturePlugIn : IFeaturePlugIn, IPeriodicTaskPlugIn, ISupportCustomConfiguration<ProtagonistConfiguration>, ISupportDefaultCustomConfiguration,

    IAttackableGotKilledPlugIn, IChatMessageSentPlugIn

{

    private readonly ConcurrentDictionary<GameContext, ServerState> _states = new();

    public ProtagonistConfiguration? Configuration { get; set; }



    public object CreateDefaultConfig() => new ProtagonistConfiguration();

    public void ForceStart()

    {

        foreach (var state in this._states.Values)

        {

            state.NextRun = DateTimeOffset.MinValue;

        }

    }



    public async ValueTask ExecuteTaskAsync(GameContext gameContext)

    {

        if (gameContext.IsDisposed || gameContext.IsDisposing || gameContext.ArePeriodicTasksStopped)

        {

            return;

        }



        var settings = this.Configuration ??= new ProtagonistConfiguration();

        if (gameContext is IGameServerContext server && server.Id != settings.ServerId && !this._states.ContainsKey(gameContext))

        {

            return;

        }



        var state = this._states.GetOrAdd(gameContext, _ => new ServerState());

        if (DateTimeOffset.UtcNow < state.NextRun || Interlocked.CompareExchange(ref state.Running, 1, 0) != 0)

        {

            return;

        }



        try

        {

            state.NextRun = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

            if (!settings.Enabled || (gameContext is IGameServerContext owner && owner.Id != settings.ServerId))

            {

                await state.Manager.StopAllAsync().ConfigureAwait(false);

                state.World = null;

                return;

            }



            settings.Validate();

            if (state.World is null)

            {

                await ProtagonistGenerator.EnsureAsync(gameContext, settings).ConfigureAwait(false);

                state.World = new(settings);

                state.Manager = new((game, login) => state.World.CreateBot(game, int.Parse(login.AsSpan(6)) - 1));

            }



            for (var i = 0; i < 1; i++) // Solo spawn Mandolion (índice 0) para trabajar con uno a la vez

            {

                if (!state.Manager.IsActive(ProtagonistGenerator.Login(i), 0))

                {

                    if (gameContext is IGameServerContext capacity && gameContext.PlayerCount >= capacity.ServerConfiguration.MaximumPlayers - 1)

                    {

                        break;

                    }



                    if (await state.Manager.SpawnBotAsync(gameContext, ProtagonistGenerator.Login(i), 0).ConfigureAwait(false))

                    {

                        gameContext.LoggerFactory.CreateLogger<ProtagonistFeaturePlugIn>().LogInformation("Protagonist {Identity} entered the world as {Alias}.", state.World.GetBot(i)?.Identity, state.World.GetBot(i)?.Name);

                    }

                }

            }



            foreach (var bot in state.Manager.Bots.Where(b => b.AwaitsFaultRestart).ToList())

            {

                await state.Manager.RestartBotAsync(gameContext, bot).ConfigureAwait(false);

            }

        }

        catch (Exception exception)

        {

            gameContext.LoggerFactory.CreateLogger<ProtagonistFeaturePlugIn>().LogError(exception, "Protagonist maintenance failed; retrying in 30 seconds.");

        }

        finally

        {

            Interlocked.Exchange(ref state.Running, 0);

        }

    }



    public ValueTask AttackableGotKilledAsync(IAttackable killed, IAttacker? killer)

    {
        if (killed is Player human && human is not OfflinePlayer && human.CurrentMiniGame is null
            && human.CurrentMap is { } deathMap && human.GameContext is GameContext context
            && this._states.TryGetValue(context, out var state) && state.World is { } world && world.Configuration.Enabled)
        {
            // Capture visibility now: the victim can leave the map before the bot drains its queue.
            foreach (var witness in world.GetAllBots().ToArray())
            {
                if (witness.IsDisposed || witness.IsDisposing || witness.CurrentMiniGame is not null
                    || !ReferenceEquals(witness.CurrentMap, deathMap) || !witness.IsInRange(human.Position, 12))
                {
                    continue;
                }

                var kind = ReferenceEquals(witness, killer) ? WorldEventKind.PlayerKilledByProtagonist : WorldEventKind.PlayerDied;
                var text = ReferenceEquals(witness, killer) ? $"Mataste a {human.Name}" : $"Jugador {human.Name} murió cerca";
                witness.PendingBotActions.Enqueue(() =>
                {
                    witness.RecordWorldEvent(kind, human, text);
                    return ValueTask.CompletedTask;
                });
            }
        }

        if (killer is BotProtagonist protagonist && killed is Player victim && victim is not OfflinePlayer

            && protagonist.Settings.Enabled && protagonist.CurrentMiniGame is null)

        {

            if (protagonist is BotProtagonist_elmandolion)

            {

                protagonist.PendingBotActions.Enqueue(() => protagonist.SayAsync(BotProtagonist_elmandolion.KillPhrase, true));

            }



            if (protagonist.World.GetBot(3) is BotProtagonist_elBichiCome master)

            {

                master.PendingBotActions.Enqueue(() => master.WitnessMurderAsync(protagonist));

            }

        }



        return ValueTask.CompletedTask;

    }



    public async ValueTask ChatMessageSentAsync(Player sender, string message, ChatMessageType messageType, Player? receiver)

    {

        if (sender is OfflinePlayer || messageType != ChatMessageType.Normal || !sender.IsAlive

            || sender.PlayerState.CurrentState != PlayerState.EnteredWorld || sender.CurrentMiniGame is not null

            || sender.GameContext is not GameContext context || !this._states.TryGetValue(context, out var state) || state.World is not { } world

            || !world.Configuration.Enabled)

        {

            return;

        }



        foreach (var index in new[] { 0, 1, 2, 3 })

        {

            if (world.GetBot(index) is not { IsAlive: true } bot || !ReferenceEquals(bot.CurrentMap, sender.CurrentMap) || !bot.IsInRange(sender.Position, 12))

            {

                continue;

            }



            // Explicit local commands; never charge Zen or consume Ale just because a player mentions them.

            if (bot is BotProtagonist_LeilaMasa leila && string.Equals(message.Trim(), "LeilaMasa pagar", StringComparison.OrdinalIgnoreCase))

            {

                // The player action lock protects inventory; the bot queue owns demand state. Do not

                // hold both persistence locks at once (it deadlocks with attacks from that bot).

                var paid = await sender.RunPersistenceExclusiveAsync(() => ValueTask.FromResult(leila.TryPayDemand(sender))).ConfigureAwait(false);

                if (paid)

                {

                    leila.PendingBotActions.Enqueue(() => leila.SayAsync("Trato hecho. Hoy estas a salvo.", true));

                    await sender.InvokeViewPlugInAsync<IUpdateMoneyPlugIn>(p => p.UpdateMoneyAsync()).ConfigureAwait(false);

                }

            }

            else if (bot is BotProtagonist_elBichiCome bichi && string.Equals(message.Trim(), "elBichiCome ale", StringComparison.OrdinalIgnoreCase))

            {

                var consumed = await sender.RunPersistenceExclusiveAsync(async () =>

                {

                    var ale = sender.Inventory?.Items.FirstOrDefault(item => item.ItemSlot >= InventoryConstants.EquippableSlotsCount

                        && item.Definition?.Group == world.Configuration.AleGroup && item.Definition.Number == world.Configuration.AleNumber && item.Durability > 0);

                    if (ale is null)

                    {

                        return false;

                    }



                    // Stock Ale has durability 1. Custom stacks lose exactly one unit.

                    if (ale.Durability > 1)

                    {

                        ale.Durability--;

                        await sender.InvokeViewPlugInAsync<IItemDurabilityChangedPlugIn>(p => p.ItemDurabilityChangedAsync(ale, false)).ConfigureAwait(false);

                    }

                    else

                    {

                        await sender.DestroyInventoryItemAsync(ale).ConfigureAwait(false);

                    }



                    return true;

                }).ConfigureAwait(false);

                if (consumed)

                {

                    bichi.PendingBotActions.Enqueue(() => bichi.ReceiveAleAsync(sender));

                }

            }

            else

            {

                bot.PendingBotActions.Enqueue(() => bot.ReactToChatAsync(sender, message));

            }

        }

    }



    private sealed class ServerState

    {

        internal int Running;

        internal DateTimeOffset NextRun = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);

        internal ProtagonistWorld? World;

        internal BotManager Manager = new();

    }

}