// <copyright file="NpcMood.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

/// <summary>
/// Humor y energía de un protagonista. Cambian por lo que le pasa (lo atacan, muere, mata, charla)
/// y por el paso del tiempo según su actividad (descansar recupera, cazar cansa).
/// </summary>
public sealed class NpcMoodSystem
{
    private DateTimeOffset _lastTick;
    private double _mood;
    private double _energy = 70;

    /// <summary>Initializes a new instance of the <see cref="NpcMoodSystem"/> class.</summary>
    /// <param name="start">Momento inicial (para tests); por defecto ahora.</param>
    public NpcMoodSystem(DateTimeOffset? start = null)
    {
        this._lastTick = start ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Gets el humor: -100 furioso ... +100 contento.</summary>
    public int MoodScore => (int)Math.Round(this._mood);

    /// <summary>Gets la energía: 0 agotado ... 100 lleno de energía.</summary>
    public int Energy => (int)Math.Round(this._energy);

    /// <summary>Gets el humor en palabras.</summary>
    public string MoodText => this._mood switch
    {
        < -50 => "furioso",
        < -15 => "molesto",
        < 15 => "tranquilo",
        < 50 => "de buen humor",
        _ => "contento",
    };

    /// <summary>Gets la energía en palabras.</summary>
    public string EnergyText => this._energy switch
    {
        < 15 => "agotado",
        < 40 => "cansado",
        < 75 => "con energía",
        _ => "lleno de energía",
    };

    /// <summary>Aplica un cambio de humor y energía.</summary>
    /// <param name="moodDelta">Cambio de humor.</param>
    /// <param name="energyDelta">Cambio de energía.</param>
    /// <returns>True si algún valor cruzó un múltiplo de 10 (para loguear sin spam).</returns>
    public bool Apply(double moodDelta, double energyDelta)
    {
        var before = (this.MoodScore / 10, this.Energy / 10);
        this._mood = Math.Clamp(this._mood + moodDelta, -100, 100);
        this._energy = Math.Clamp(this._energy + energyDelta, 0, 100);
        return before != (this.MoodScore / 10, this.Energy / 10);
    }

    /// <summary>Paso del tiempo según la actividad. Se llama en cada tick.</summary>
    /// <param name="now">Ahora.</param>
    /// <param name="activity">Actividad actual.</param>
    /// <returns>True si algún valor cruzó un múltiplo de 10.</returns>
    public bool Tick(DateTimeOffset now, NpcActivityKind activity)
    {
        var minutes = (now - this._lastTick).TotalMinutes;
        this._lastTick = now;
        if (minutes <= 0 || minutes > 10)
        {
            return false;
        }

        var energyPerMinute = activity switch
        {
            NpcActivityKind.Rest => 3.0,
            NpcActivityKind.Hunt => -1.0,
            _ => -0.2,
        };

        // El humor vuelve hacia neutro de a 1 punto por minuto.
        var moodBack = -Math.Sign(this._mood) * Math.Min(Math.Abs(this._mood), minutes);
        return this.Apply(moodBack, energyPerMinute * minutes);
    }

    /// <summary>Texto para los prompts del LLM.</summary>
    /// <returns>Una frase con humor y energía.</returns>
    public string GetPromptFragment() => $"Estás {this.MoodText} (humor {this.MoodScore}) y {this.EnergyText} (energía {this.Energy}/100).";
}
