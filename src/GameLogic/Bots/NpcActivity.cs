// <copyright file="NpcActivity.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using System.Collections.Generic;

/// <summary>
/// Tipos de actividades autónomas que un protagonista puede realizar por iniciativa propia.
/// Referencia: Plan Mandolion Vida Propia 2026-11-07, Diseño sección "1. Actividades".
/// </summary>
public enum NpcActivityKind
{
    /// <summary>Sin actividad activa (idle/descanso).</summary>
    None = 0,
    
    /// <summary>Vigila la puerta norte de Lorencia (130,178), mira a los que pasan.</summary>
    GuardGate = 1,
    
    /// <summary>Patrulla por el mapa siguiendo waypoints definidos en Settings.Patrol.</summary>
    Patrol = 2,
    
    /// <summary>Caza monstruos en Devias o Noria (habilita farmeo temporal).</summary>
    Hunt = 3,
    
    /// <summary>Vuelve a Lorencia (130,130) y se queda quieto. Elegida si HP bajo o después de morir.</summary>
    Rest = 4,
    
    /// <summary>Busca a un jugador concreto de su memoria (rencor/respeto) en mapa permitido.</summary>
    SeekPlayer = 5,
    
    /// <summary>Va a buscar a BertoSala (cuando esté activo).</summary>
    ChallengeRival = 6,
}

/// <summary>
/// Representa una actividad autónoma completa con parámetros.
/// El LLM elige: activity + parametro (mapa/jugador) + frase opcional para anunciarla.
/// </summary>
public sealed record NpcActivity(
    NpcActivityKind Activity,
    /// <summary>Mapa destino (null = mapa actual). Usado por Hunt/SeekPlayer/ChallengeRival.</summary>
    int? TargetMap,
    /// <summary>Jugador objetivo para SeekPlayer. null si no aplica.</summary>
    string? TargetName,
    /// <summary>Frase opcional para anunciar la actividad. Puede ser vacío.</summary>
    string? AnnounceMessage);

/// <summary>
/// Lugares permitidos para actividades autónomas (puntos caminables de patrulla).
/// Validación: solo estos mapas pueden ser objetivos de Hunt/SeekPlayer/ChallengeRival.
/// </summary>
public static class AllowedActivityMaps
{
    /// <summary>Lorencia - puerta norte (130,178) y centro (130,130).</summary>
    public const int Lorencia = 0;
    
    /// <summary>Devias - zona de caza.</summary>
    public const int Devias = 2;
    
    /// <summary>Noria - zona de caza.</summary>
    public const int Noria = 3;
    
    /// <summary>Lista de todos los mapas permitidos para actividades.</summary>
    public static IReadOnlyList<int> All => new[] { Lorencia, Devias, Noria };
}

/// <summary>
/// Calcula duración random para una actividad dada (minutos).
/// Referencia: Plan Mandolion Vida Propia 2026-11-07 tabla "Actividad | Qué hace | Dura".
/// </summary>
public static class ActivityDurationCalculator
{
    /// <summary>Retorna duración random en minutos para una actividad dada.</summary>
    public static int CalculateMinutes(NpcActivityKind activity) => activity switch
    {
        NpcActivityKind.GuardGate => Random.Shared.Next(5, 16),      // 5-15 min
        NpcActivityKind.Patrol => Random.Shared.Next(10, 21),       // 10-20 min
        NpcActivityKind.Hunt => Random.Shared.Next(10, 21),         // 10-20 min
        NpcActivityKind.Rest => Random.Shared.Next(3, 11),          // 3-10 min
        NpcActivityKind.SeekPlayer => Random.Shared.Next(5, 11),    // hasta 10 min (o encontrar objetivo)
        NpcActivityKind.ChallengeRival => 5,                        // 5 min fijo
        _ => 5,                                                      // default: 5 min
    };
}

/// <summary>Lo que decidió el planificador: actividad, mapa, objetivo, frase opcional y motivo.</summary>
public sealed record NpcPlan(NpcActivityKind Activity, string Map, string Target, string Say, string Reason);
