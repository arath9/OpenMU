// <copyright file="BotProtagonist_elmandolion.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

/// <summary>The arrogant dark Blade Master; hunts humans only during the scheduled window.</summary>
public sealed class BotProtagonist_elmandolion : BotProtagonist
{
    public const string KillPhrase = "Comiste pete";

    public BotProtagonist_elmandolion(IGameContext context, ProtagonistWorld world)
        : base(context, world, "elmandolion")
    {
        // Configurar Mandolion para usar LLM Natural por defecto (diálogo en lenguaje natural con personalidad uruguaya)
        _dialogueConfig = new BotDialogueConfig
        {
            PreferredMode = DialogueMode.LLMNatural,
            PersonalityDescription =
                "Blade Master veterano y arrogante. Se cree el dueño de Lorencia y lo dice. " +
                "Mira con desprecio a los novatos pero respeta a quien se le planta. " +
                "Tiene una rivalidad vieja con BertoSala. Cuando empieza la cacería, persigue jugadores. " +
                "Habla seco, sobrador, con humor ácido. Da consejos útiles aunque sea de mala gana.",
            ResponseCooldownSeconds = 30
        };
    }

    protected override async ValueTask ThinkAsync(DateTimeOffset now)
    {
        if (this.World.IsHunting)
                {
                    // Patrullar waypoints definidos en Settings.Patrol
                    if (this.Settings.Patrol is { } patrol && patrol.Length > 0)
                    {
                        var waypointIndex = this._patrolIndex % patrol.Length;
                        var waypoint = patrol[waypointIndex];

                        if (this.CurrentMap is { } map && map.Definition.Number == waypoint.Map)
                        {
                            var distanceToWaypoint = System.Math.Sqrt(System.Math.Pow(this.Position.X - waypoint.X, 2) + System.Math.Pow(this.Position.Y - waypoint.Y, 2));
                            if (distanceToWaypoint < 5)
                            {
                                this._patrolIndex++;
                            }

                            this.Destination = new(waypoint.Map, waypoint.X, waypoint.Y);
                        }
                    }

                    if (this.Settings.AllowPlayerHunting)
                    {
                        this.Engage(this.NearbyHumans().FirstOrDefault(p => !p.IsAtSafezone()));
            }

            if (this.AutonomousThinkingEnabled)
            {
                this.ThinkAsyncLLM(now);
            }
            else
            {
                await this.SayAsync("Lorencia es mia. Salgan si se atreven.").ConfigureAwait(false);
            }

            return;
        }

        this.GoNorth();
        this.Engage(this.World.GetBot(1));
        if (this.AutonomousThinkingEnabled)
        {
            this.ThinkAsyncLLM(now);
        }
        else
        {
            await this.SayAsync("bertoSalamin, deja de esconderte detras de los debiles.").ConfigureAwait(false);
        }
    }
}
