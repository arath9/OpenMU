// <copyright file="ProtagonistChanceGate.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

/// <summary>One probability roll per cooldown, including unsuccessful rolls (no per-tick amplification).</summary>
public sealed class ProtagonistChanceGate
{
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;

    public bool TryTrigger(DateTimeOffset now, TimeSpan cooldown, double probability, double roll)
    {
        if (!double.IsFinite(probability) || probability is < 0 or > 1 || !double.IsFinite(roll) || roll is < 0 or >= 1 || cooldown < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        if (now < this._nextAttempt)
        {
            return false;
        }

        this._nextAttempt = now + cooldown;
        return roll < probability;
    }
}
