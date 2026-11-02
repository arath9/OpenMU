// <copyright file="ProtagonistSchedule.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

/// <summary>Server-local event clock; access is serialized through the protagonist world.</summary>
public sealed class ProtagonistSchedule
{
    private readonly ProtagonistConfiguration _configuration;
    private DateTimeOffset _nextHunt;
    private DateTimeOffset _huntUntil;

    public ProtagonistSchedule(ProtagonistConfiguration configuration, DateTimeOffset started)
    {
        configuration.Validate();
        this._configuration = configuration;
        this._nextHunt = started + configuration.HuntInterval;
    }

    public bool IsHunting(DateTimeOffset now)
    {
        if (now >= this._nextHunt)
        {
            this._huntUntil = now + this._configuration.HuntDuration;
            this._nextHunt = now + this._configuration.HuntInterval;
        }

        return now < this._huntUntil;
    }
}
