// <copyright file="BotProtagonist_LeilaMasa.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.PlayerActions.Skills;

/// <summary>A helpful elf whose extortion and betrayals have explicit probability gates and cooldowns.</summary>
public sealed class BotProtagonist_LeilaMasa : BotProtagonist
{
    private readonly object _demandSync = new();
    private DateTimeOffset _spotUntil;
    private ProtagonistWaypoint? _stolenSpot;
    private readonly ProtagonistChanceGate _demands = new();
    private readonly ProtagonistChanceGate _betrayals = new();
    private readonly ProtagonistChanceGate _spots = new();
    private readonly ProtagonistChanceGate _buffs = new();
    private Player? _debtor;
    private DateTimeOffset _deadline;

    public BotProtagonist_LeilaMasa(IGameContext context, ProtagonistWorld world)
        : base(context, world, "LeilaMasa")
    {
    }

    public override bool CanFarm => true;
    
    internal void RequestZen(Player player, DateTimeOffset now)
    {
        lock (this._demandSync)
        {
            this._debtor = player;
            this._deadline = now + this.Settings.DemandTimeout;
        }
    }

    internal bool TryPayDemand(Player player)
    {
        lock (this._demandSync)
        {
            if (!ReferenceEquals(this._debtor, player) || DateTimeOffset.UtcNow >= this._deadline || !player.TryRemoveMoney(this.Settings.ZenDemand))
            {
                return false;
            }

            this._debtor = null;
            return true;
        }
    }

    internal override ValueTask ReactToChatAsync(Player sender, string message)
    {
        lock (this._demandSync)
        {
            if (ReferenceEquals(sender, this._debtor) && (message.Contains("no", StringComparison.OrdinalIgnoreCase) || message.Contains("negar", StringComparison.OrdinalIgnoreCase)))
            {
                this._deadline = DateTimeOffset.UtcNow;
            }
        }

        return base.ReactToChatAsync(sender, message);
    }

    protected override async ValueTask ThinkAsync(DateTimeOffset now)
    {
        Player? debtor = null;
        bool hasDebtor;
        lock (this._demandSync)
        {
            if (this._debtor is { } current && now >= this._deadline)
            {
                debtor = current;
                this._debtor = null;
            }

            hasDebtor = this._debtor is not null;
        }

        if (debtor is not null && ReferenceEquals(debtor.CurrentMap, this.CurrentMap))
        {
            if (this._betrayals.TryTrigger(now, this.Settings.BetrayalCooldown, this.Settings.BetrayalProbability, Rand.NextDouble()))
            {
                if (this.Settings.AllowPlayerHunting)
                {
                    this.Engage(debtor, TimeSpan.FromMinutes(1));
                }

                await this.SayAsync("Te avise. Este spot ahora es mio.", true).ConfigureAwait(false);
                this._stolenSpot = new(this.CurrentMap!.Definition.Number, debtor.Position.X, debtor.Position.Y);
                this._spotUntil = now + this.Settings.SpotStealDuration;
            }
        }

        var human = this.NearbyHumans().FirstOrDefault();
        
        // Si ya hay target activo de otra fuente, usarlo (PERO SOLO SI NO HAY OTRA OPCION)
        if (this.ScriptedTarget is not null)
        {
            return;
        }

        // Demand Zen occasionally with probability gate
        if (human is not null && !hasDebtor
            && this._demands.TryTrigger(now, this.Settings.BetrayalCooldown, this.Settings.DemandProbability, Rand.NextDouble()))
        {
            this.RequestZen(human, now);
            hasDebtor = true;
            await this.SayAsync($"{human.Name}, {this.Settings.ZenDemand} Zen. Di: LeilaMasa pagar", true).ConfigureAwait(false);
        }

        // Steal spots occasionally with probability gate  
        if (human is not null && this._spots.TryTrigger(now, this.Settings.BetrayalCooldown, this.Settings.SpotStealProbability, Rand.NextDouble()))
        {
            this._stolenSpot = new(this.CurrentMap!.Definition.Number, human.Position.X, human.Position.Y);
            this._spotUntil = now + this.Settings.SpotStealDuration;
            await this.SayAsync("Lindo spot. Me quedo.").ConfigureAwait(false);
        }

        if (now < this._spotUntil && this._stolenSpot is { } spot)
        {
            this.Destination = spot;
        }
        else if (this.World.GetBot(1) is { CurrentMap: { } map } protector)
        {
            this.Destination = new(map.Definition.Number, protector.Position.X, protector.Position.Y);
        }

        // Buff other players rarely - with cooldown control
        if (human is not null && !hasDebtor && this._buffs.TryTrigger(now, TimeSpan.FromMinutes(3), 0.2, Rand.NextDouble()))
        {
            var buff = this.SkillList?.Skills.FirstOrDefault(s => s.Skill is { SkillType: SkillType.Buff, Target: SkillTarget.Explicit, MagicEffectDef: not null }
                && !human.MagicEffectList.ContainsEffect(s.Skill.MagicEffectDef!.Number));
            if (buff?.Skill is { } skill)
            {
                var strategy = this.GameContext.PlugInManager.GetStrategy<short, ITargetedSkillPlugin>(skill.Number) ?? new TargetedSkillDefaultPlugin();
                await strategy.PerformSkillAsync(this, human, (ushort)skill.Number).ConfigureAwait(false);
            }
        }

        // BUSCAR ACTIVAMENTE otros protagonists como objetivos de combate (ANTES DE CUALQUIER OTRO CHECK - AL FINAL DEL MÉTODO PERO ANTES DE QUE SCRIPTEDTARGET SEA TRUE)
        if (this.Settings.ProtagonistHuntEnabled && this.CurrentMap is not null)
        {
            var otherProtagonists = this.World.GetAllBots().Where(bp => bp != this).Cast<BotProtagonist>();
            foreach (var other in otherProtagonists)
            {
                if (other.IsAlive)
                {
                    // Verificar si están en misma party - si no, atacar
                    bool sameParty = false;
                    if (this.Party is not null && other.Party is not null)
                    {
                        sameParty = ReferenceEquals(this.Party, other.Party);
                    }
                    
                    if (!sameParty)
                    {
                        // ATACAR DIRECTAMENTE - SIN CHECK DE MAPA O SAFEZONE
                        this.Engage(other, TimeSpan.FromMinutes(2));
                        await this.SayAsync($"¡{other.Name}, prepárate para luchar!", true).ConfigureAwait(false);
                        return; // Ya tenemos un target, no buscar más
                    }
                }
            }
        }
    }
}
