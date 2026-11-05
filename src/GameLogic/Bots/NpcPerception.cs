// <copyright file="NpcPerception.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using System.Collections.Generic;

/// <summary>Eventos que un protagonista percibe del mundo.</summary>
public enum WorldEventKind
{
    /// <summary>Nadie, evento genérico.</summary>
    None,

    /// <summary>Un humano entró al rango de 12 casillas.</summary>
    PlayerArrived,

    /// <summary>Un humano salió del rango de 12 casillas.</summary>
    PlayerLeft,

    /// <summary>Un humano murió cerca.</summary>
    PlayerDied,

    /// <summary>Un humano fue asesinado por este protagonista.</summary>
    PlayerKilledByProtagonist,

    /// <summary>Este protagonista fue atacado por un humano (agresor reciente).</summary>
    AttackedBy,

    /// <summary>Hay humanos cerca y no pasó nada en 2 min (idle).</summary>
    Idle,
}

/// <summary>Evento percibido por un protagonista.</summary>
public sealed record WorldEvent(WorldEventKind Kind, Player? Who, string Text, DateTimeOffset At);

/// <summary>Buffer de eventos percibidos por un bot protagonista. Implementa detección básica sin LLM.</summary>
public class NpcPerception
{
    private readonly List<WorldEvent> _events = new();
    private HashSet<Player>? _lastNearbyHumans;

    /// <summary>Últimos ~10 eventos (FIFO).</summary>
    public IReadOnlyList<WorldEvent> RecentEvents => _events.AsReadOnly();

    /// <summary>Limpia buffer de eventos viejos, mantiene solo los últimos 10.</summary>
    private void Prune()
    {
        while (_events.Count > 10)
        {
            _events.RemoveAt(0);
        }
    }

    /// <summary>Encuadra evento al buffer (si no es None).</summary>
    public void AddEvent(WorldEventKind kind, Player? who, string text)
    {
        if (kind == WorldEventKind.None || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _events.Add(new WorldEvent(kind, who, text, DateTimeOffset.UtcNow));
        Prune();
    }

    /// <summary>Actualiza el estado de humanos cercanos. Devuelve eventos de llegada/salida si hubo cambios.</summary>
    public IEnumerable<WorldEvent> UpdateNearbyHumans(IEnumerable<Player> currentNearbyHumans)
    {
        var result = new List<WorldEvent>();
        var currentSet = new HashSet<Player>(currentNearbyHumans);

        if (_lastNearbyHumans != null)
        {
            // Llegaron nuevos jugadores al rango
            foreach (var arrived in currentSet.Except(_lastNearbyHumans))
            {
                result.Add(new WorldEvent(WorldEventKind.PlayerArrived, arrived, $"Jugador {arrived.Name} llegó cerca", DateTimeOffset.UtcNow));
            }

            // Salieron jugadores del rango
            foreach (var left in _lastNearbyHumans.Except(currentSet))
            {
                result.Add(new WorldEvent(WorldEventKind.PlayerLeft, left, $"Jugador {left.Name} salió de cerca", DateTimeOffset.UtcNow));
            }
        }

        _lastNearbyHumans = currentSet;
        return result;
    }

    /// <summary>Verifica si hay humanos cerca y no pasó nada en 2 min (idle).</summary>
    public bool CheckIdle(IEnumerable<Player> nearbyHumans)
    {
        if (!nearbyHumans.Any())
        {
            return false;
        }

        // Revisar eventos recientes: si no hay ninguno en los últimos 2 min, es idle
        var now = DateTimeOffset.UtcNow;
        var hasRecentEvent = _events.Any(e => e.At > now - TimeSpan.FromMinutes(2) && e.Kind != WorldEventKind.None);
        return !hasRecentEvent;
    }

    /// <summary>Busca eventos de tipo específico en el buffer reciente.</summary>
    public IEnumerable<WorldEvent> FindEvents(WorldEventKind kind)
    {
        return _events.Where(e => e.Kind == kind);
    }

    /// <summary>Verifica si hay algún evento reciente (no None) en los últimos 30 segundos.</summary>
    public bool HasRecentActivity()
    {
        var now = DateTimeOffset.UtcNow;
        return _events.Any(e => e.At > now - TimeSpan.FromSeconds(30) && e.Kind != WorldEventKind.None);
    }

    /// <summary>Limpia completamente el buffer y estado de humanos cercanos.</summary>
    public void Reset()
    {
        _events.Clear();
        _lastNearbyHumans = null;
    }
}
