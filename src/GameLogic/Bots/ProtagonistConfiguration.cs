// <copyright file="ProtagonistConfiguration.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

/// <summary>Opt-in deterministic protagonist settings, independent of the local LLM.</summary>
public sealed class ProtagonistConfiguration
{
    /// <summary>Gets or sets whether the four protagonists are spawned.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the unique owning server id (prevents cross-server duplication).</summary>
    public int ServerId { get; set; } = 1;

    /// <summary>Gets or sets whether scripted aggression may incur normal PK penalties.</summary>
    public bool AllowPlayerHunting { get; set; }

    /// <summary>Gets or sets whether protagonists can fight each other for fun and drama.</summary>
    public bool ProtagonistHuntEnabled { get; set; }

    public TimeSpan HuntInterval { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan HuntDuration { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan BetrayalCooldown { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan InterventionCooldown { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RageDuration { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan InteractionCooldown { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan SpotStealDuration { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan DemandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan AleHelpDuration { get; set; } = TimeSpan.FromMinutes(10);
    public double BetrayalProbability { get; set; } = 0.1;
    public double SpotStealProbability { get; set; } = 0.15;
    public double InterventionProbability { get; set; } = 0.05;
    public double DemandProbability { get; set; } = 0.15;
    public double AlePartyProbability { get; set; } = 0.7;
    public int ZenDemand { get; set; } = 10000;
    public int FullStatValue { get; set; } = 32767;
    public short HomeMap { get; set; }
    public byte HomeX { get; set; } = 130;
    public byte HomeY { get; set; } = 130;
    public byte NorthGateX { get; set; } = 130;
    public byte NorthGateY { get; set; } = 178;
    public short[] AllowedMaps { get; set; } = [0, 2, 3];
    public ProtagonistWaypoint[] Patrol { get; set; } = [new(0, 130, 178), new(0, 110, 185), new(0, 145, 185), new(2, 211, 40), new(3, 174, 112)];
    public byte AleGroup { get; set; } = 14;
    public short AleNumber { get; set; } = 9;
    public string ElmandolionName { get; set; } = "Mandolion";
    public string BertoName { get; set; } = "BertoSala";
    public string LeilaName { get; set; } = "LeilaMasa";
    public string BichiName { get; set; } = "BichiCome";

    /// <summary>Rejects unsafe configuration before creating accounts or starting timers.</summary>
    public void Validate()
    {
        if (this.HuntInterval <= TimeSpan.Zero || this.HuntDuration <= TimeSpan.Zero || this.HuntDuration >= this.HuntInterval
            || this.BetrayalCooldown < TimeSpan.Zero || this.InterventionCooldown < TimeSpan.Zero
            || this.InteractionCooldown <= TimeSpan.Zero || this.RageDuration <= TimeSpan.Zero
            || this.SpotStealDuration <= TimeSpan.Zero || this.DemandTimeout <= TimeSpan.Zero || this.AleHelpDuration <= TimeSpan.Zero
            || this.ZenDemand <= 0 || this.FullStatValue is < 1 or > 32767)
        {
            throw new ArgumentException("Invalid protagonist durations, Zen or stats.");
        }

        foreach (var probability in new[] { this.BetrayalProbability, this.SpotStealProbability, this.InterventionProbability, this.DemandProbability, this.AlePartyProbability })
        {
            if (!double.IsFinite(probability) || probability is < 0 or > 1)
            {
                throw new ArgumentException("Protagonist probabilities must be finite and between 0 and 1.");
            }
        }

        var names = new[] { this.ElmandolionName, this.BertoName, this.LeilaName, this.BichiName };
        if (names.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > 10 || n.Any(c => !char.IsAsciiLetterOrDigit(c)))
            || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4
            || this.AllowedMaps is null || !this.AllowedMaps.Contains(this.HomeMap)
            || this.Patrol is not { Length: > 0 } || this.Patrol.Any(p => !this.AllowedMaps.Contains(p.Map)))
        {
            throw new ArgumentException("Use four distinct ASCII wire names (1-10 characters) and allowed patrol maps including home.");
        }
    }
}

/// <summary>A permitted patrol destination; walking and warps still use normal server validation.</summary>
public sealed record ProtagonistWaypoint(short Map, byte X, byte Y);
