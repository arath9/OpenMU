// <copyright file="NpcMood.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using System;

/// <summary>
/// Estados de ánimo que un protagonista puede experimentar (afectan su comportamiento).
/// Referencia: Plan Mandolion Vida Propia 2026-11-07, Diseño sección "3. Estado emocional".
/// </summary>
public enum NpcMoodKind
{
    /// <summary>Sin estado especial (normal/calmo).</summary>
    Normal = 0,
    
    /// <summary>Agresivo - busca pelear o confrontar jugadores.</summary>
    Aggressive = 1,
    
    /// <summary>Miedo - huye de jugadores cercanos.</summary>
    Fearful = 2,
    
    /// <summary>Curioso - se acerca a jugadores y pregunta cosas.</summary>
    Curious = 3,
    
    /// <summary>Triste - busca descansar o recordar algo malo.</summary>
    Sad = 4,
}

/// <summary>
/// Sistema de estado emocional y energético que afecta el comportamiento del NPC.
/// Referencia: Plan Mandolion Vida Propia 2026-11-07, Diseño sección "3. Estado emocional".
/// </summary>
public sealed class NpcMoodSystem
{
    private DateTime _lastUpdate = DateTime.UtcNow;
    
    /// <summary>Nivel de energía (0-100). 0=agotado, 50=normal, 100=plenamente energético.</summary>
    public int Energy { get; private set; } = 70; // Inicialmente 70
    
    /// <summary>Nivel de ánimo (-100 a 100). -100=muy deprimido, 0=neutral, 100=eufórico.</summary>
    public int MoodScore { get; private set; } = 0; // Inicialmente neutral
    
    /// <summary>Estado de ánimo actual (calculado a partir de MoodScore).</summary>
    public NpcMoodKind CurrentMood => MoodScore switch
    {
        >= 50 => NpcMoodKind.Aggressive,
        <= -50 => NpcMoodKind.Sad,
        >= 25 => NpcMoodKind.Curious,
        <= -25 => NpcMoodKind.Fearful,
        _ => NpcMoodKind.Normal
    };

    /// <summary>Registra un evento que afecta el estado emocional/energético.</summary>
    public void RegisterEvent(MUnique.OpenMU.GameLogic.Bots.NpcActionKind action, string? targetName)
    {
        var now = DateTime.UtcNow;
        var deltaSeconds = (now - _lastUpdate).TotalSeconds;
        
        // Recuperación pasiva de energía: +1 cada 30 segundos
        Energy = Math.Min(100, Energy + (int)(deltaSeconds / 30.0));
        
        // Eventos que afectan estado emocional
        switch (action)
        {
            case MUnique.OpenMU.GameLogic.Bots.NpcActionKind.Fight:
                MoodScore = Math.Min(100, MoodScore + 20);
                Energy = Math.Max(0, Energy - 15); // Pelear gasta energía
                break;
                
            case MUnique.OpenMU.GameLogic.Bots.NpcActionKind.Stop:
                MoodScore = Math.Clamp(MoodScore + 5, -100, 100); // Parar mejora ánimo ligeramente
                Energy = Math.Min(100, Energy + 10); // Descansar recupera un poco
                break;
                
            case MUnique.OpenMU.GameLogic.Bots.NpcActionKind.None:
            default:
                // Sin efecto significativo
                break;
        }
        
        _lastUpdate = now;
    }

    /// <summary>
    /// Genera un fragmento de texto para el prompt del LLM que describe el estado actual.
    /// Referencia: Plan Mandolion Vida Propia 2026-11-07, Diseño sección "3. Estado emocional".
    /// </summary>
    public string GetPromptFragment(string botName)
    {
        var energyText = Energy switch
        {
            < 20 => "agotado (muy poca energía)",
            < 50 => "cansado",
            < 80 => "con energía normal",
            _ => "plenamente energético"
        };

        var moodText = CurrentMood switch
        {
            NpcMoodKind.Aggressive => "en un estado de ánimo agresivo y confrontacional",
            NpcMoodKind.Fearful => "miedo e inseguridad",
            NpcMoodKind.Curious => "curioso y abierto a hablar",
            NpcMoodKind.Sad => "triste y melancólico",
            _ => "en un estado de ánimo neutro"
        };

        return $"[{botName}] está {energyText} y siente {moodText}.";
    }
}
