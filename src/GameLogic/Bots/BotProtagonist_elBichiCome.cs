// <copyright file="BotProtagonist_elBichiCome.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using MUnique.OpenMU.GameLogic.PlayerActions.Party;

/// <summary>The wandering former master; power is revealed rarely, not by polling a probability each tick.</summary>
public sealed class BotProtagonist_elBichiCome : BotProtagonist
{
    private readonly ProtagonistChanceGate _interventions = new();
    private DateTimeOffset _rageUntil;
    private DateTimeOffset _helpUntil;
    private Player? _companion;

    public BotProtagonist_elBichiCome(IGameContext context, ProtagonistWorld world)
        : base(context, world, "elBichiCome")
    {
    }

    public override bool CanFarm => DateTimeOffset.UtcNow < this._helpUntil;

    internal async ValueTask ReceiveAleAsync(Player sender)
    {
        if (Rand.NextDouble() < this.Settings.AlePartyProbability)
        {
            this._companion = sender;
            this._helpUntil = DateTimeOffset.UtcNow + this.Settings.AleHelpDuration;
            await this.SayAsync("Un Ale de verdad. Acepta party y vamos a farmear.", true).ConfigureAwait(false);
            await new PartyRequestAction().HandlePartyRequestAsync(this, sender).ConfigureAwait(false);
        }
        else
        {
            await this.SayAsync("Gracias por el Ale. Hoy camino solo.", true).ConfigureAwait(false);
        }
    }

    internal async ValueTask WitnessMurderAsync(BotProtagonist killer)
    {
        var now = DateTimeOffset.UtcNow;
        if (this._interventions.TryTrigger(now, this.Settings.InterventionCooldown, this.Settings.InterventionProbability, Rand.NextDouble()))
        {
            this._rageUntil = now + this.Settings.RageDuration;
            this.Engage(killer, this.Settings.RageDuration);
            if (killer.CurrentMap is { } map)
            {
                this.Destination = new(map.Definition.Number, killer.Position.X, killer.Position.Y);
            }

            await this.SayAsync("Yo les ensene a pelear, no a matar inocentes.", true).ConfigureAwait(false);
        }
    }

    protected override async ValueTask ThinkAsync(DateTimeOffset now)
    {
        if (now < this._rageUntil)
        {
            return;
        }

        this.Engage(null);
        if (now < this._helpUntil && this._companion is { IsDisposed: false, CurrentMap: { } map } companion)
        {
            this.Destination = new(map.Definition.Number, companion.Position.X, companion.Position.Y);
            return;
        }

        this._companion = null;
        this.Patrol(now);
        await this.SayAsync("Una moneda, un Ale... entrenar a esos dos me dejo seco.").ConfigureAwait(false);
    }
}
