// <copyright file="BotProtagonist_bertoSalamin.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

/// <summary>Protector and permanent rival of elmandolion; intercepts the hunter instead of attacking his victims.</summary>
public sealed class BotProtagonist_bertoSalamin : BotProtagonist
{
    public BotProtagonist_bertoSalamin(IGameContext context, ProtagonistWorld world)
        : base(context, world, "bertoSalamin")
    {
    }

    protected override async ValueTask ThinkAsync(DateTimeOffset now)
    {
        var rival = this.World.GetBot(0);
        if (this.World.IsHunting && rival is { CurrentMap: { } map })
        {
            this.Destination = new(map.Definition.Number, rival.Position.X, rival.Position.Y);
        }
        else
        {
            this.GoNorth();
        }

        this.Engage(rival);
        await this.SayAsync("elmandolion, conmigo. A los demas dejalos en paz.").ConfigureAwait(false);
    }
}
