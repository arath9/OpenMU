// <copyright file="NpcAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

/// <summary>Lo que un protagonista puede hacer además de hablar.</summary>
public enum NpcActionKind
{
    /// <summary>Solo hablar.</summary>
    None,

    /// <summary>Seguir a un jugador.</summary>
    Follow,

    /// <summary>Llevar a un jugador a un lugar.</summary>
    Guide,

    /// <summary>Pelear con un jugador.</summary>
    Fight,

    /// <summary>Dejar de hacer la acción actual.</summary>
    Stop,
}

/// <summary>Lo que el LLM decidió: qué decir y qué hacer.</summary>
public sealed record NpcIntent(string Say, NpcActionKind Action, string? Place)
{
    /// <summary>Gets the nearby human selected by an autonomous decision; empty means no target.</summary>
    public string Target { get; init; } = string.Empty;
}

/// <summary>Lugares a los que un protagonista puede guiar (solo mapas permitidos, puntos caminables de la patrulla).</summary>
public static class NpcPlaces
{
    /// <summary>Busca un lugar por nombre; null si no existe.</summary>
    public static ProtagonistWaypoint? Find(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "lorencia" => new ProtagonistWaypoint(0, 130, 130),
        "devias" => new ProtagonistWaypoint(2, 211, 40),
        "noria" => new ProtagonistWaypoint(3, 174, 112),
        _ => null,
    };

    /// <summary>Nombre legible de un lugar.</summary>
    public static string NameOf(ProtagonistWaypoint? place) => place?.Map switch
    {
        0 => "Lorencia",
        2 => "Devias",
        3 => "Noria",
        _ => "algún lado",
    };
}
