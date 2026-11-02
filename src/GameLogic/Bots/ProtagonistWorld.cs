// <copyright file="ProtagonistWorld.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

/// <summary>Shared relationships and hunt schedule for one owning game server; no global player state.</summary>
public sealed class ProtagonistWorld
{
    private readonly object _sync = new();
    private readonly ProtagonistSchedule _schedule;
    private readonly BotProtagonist?[] _bots = new BotProtagonist?[4];

    public ProtagonistWorld(ProtagonistConfiguration configuration)
    {
        configuration.Validate();
        this.Configuration = configuration;
        this._schedule = new(configuration, DateTimeOffset.UtcNow);
    }

    public ProtagonistConfiguration Configuration { get; }

    internal YarnBotManager Dialogues { get; } = new();

    public bool IsHunting
    {
        get
        {
            lock (this._sync)
            {
                return this._schedule.IsHunting(DateTimeOffset.UtcNow);
            }
        }
    }

    public BotProtagonist? GetBot(int index)
    {
        lock (this._sync)
        {
            var bot = this._bots[index];
            return bot is { IsDisposed: false, IsDisposing: false } ? bot : null;
        }
    }

    public IEnumerable<BotProtagonist> GetAllBots()
    {
        lock (this._sync)
        {
            return this._bots.Where(b => b is not null && b.IsAlive).Cast<BotProtagonist>();
        }
    }

    public BotProtagonist CreateBot(IGameContext context, int index)
    {
        BotProtagonist bot = index switch
        {
            0 => new BotProtagonist_elmandolion(context, this),
            1 => new BotProtagonist_bertoSalamin(context, this),
            2 => new BotProtagonist_LeilaMasa(context, this),
            3 => new BotProtagonist_elBichiCome(context, this),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
        lock (this._sync)
        {
            this._bots[index] = bot;
        }

        return bot;
    }
}
