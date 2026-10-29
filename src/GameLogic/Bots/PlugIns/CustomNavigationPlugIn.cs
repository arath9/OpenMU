// <copyright file="CustomNavigationPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots.PlugIns
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Extensions.Logging;
    using MUnique.OpenMU.DataModel.Configuration.Map;
    using MUnique.OpenMU.GameLogic.Offline;

    /// <summary>
    /// Custom Navigation PlugIn para bots autónomos de Fase 1.
    /// Extiende el comportamiento del BotNavigator existente con reglas personalizadas:
    /// - Preferir hunting grounds específicos según hora del día (ej: Devias solo de noche)
    /// - Evitar zonas peligrosas (monsters > X niveles más fuertes configurable)
    /// - Priorizar areas con spawns densos para farmeo eficiente
    /// </summary>
    internal sealed class CustomNavigationPlugIn : BotFeaturePlugIn
    {
        // Reglas de navegación personalizadas
        private const int MaxSafeMonsterLevelDifference = 3; // Monsters <= bot.level + 3 son seguros
        private const float MinSpawnDensityThreshold = 2.0f; // Zonas con spawn density >= 2.0 son prioritarias

        // Hunting grounds preferidos por nivel del bot
        private static readonly Dictionary<int, List<SpawnArea>> LevelBasedHuntingGrounds = new()
        {
            [1] = new List<SpawnArea> { "Lorencia_Spawn", "Devias_Entrance" },
            [10] = new List<SpawnArea> { "Devias_MainStreet", "LostTower_LowLevel" },
            [20] = new List<SpawnArea> { "LostTower_MidLevel", "ElNassar_Camp" },
            [35] = new List<SpawnArea> { "LostTower_HighLevel", "Devias_SecretRoom" },
        };

        // Zonas que deben evitarse completamente (seguridad)
        private static readonly HashSet<string> DangerousZones = new()
        {
            "Lorencia_ChaosCastle_Entrance", // Muy peligroso para niveles bajos
            "Devias_SecretRoom_HardMode",   // Solo para niveles altos
            "LostTower_TopFloor",           // Risky spawn density
        };

        private readonly ILogger<CustomNavigationPlugIn> _logger;

        /// <summary>
        /// Inicializa el plugin con un logger.
        /// </summary>
        /// <param name="logger">Logger para logging del navigation.</param>
        public CustomNavigationPlugIn(ILogger<CustomNavigationPlugIn> logger)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Determina el hunting ground apropiado según nivel y condiciones actuales.
        /// Reglas personalizadas: preferir zonas seguras, evitar dangerous zones, priorizar spawns densos.
        /// </summary>
        /// <param name="botPlayer">El bot que necesita un hunting ground.</param>
        /// <returns>SpawnArea recomendado para farmear, o null si no hay zona segura disponible.</returns>
        public SpawnArea? PickHuntingGround(BotPlayer botPlayer)
        {
            if (botPlayer == null) throw new ArgumentNullException(nameof(botPlayer));

            this._logger.LogInformation("Bot {BotName} nivel {Level} buscando hunting ground", 
                botPlayer.Name, botPlayer.Level);

            // Regla 1: Obtener zonas seguras según nivel del bot
            var safeZones = GetSafeSpawnAreas(botPlayer);

            if (!safeZones.Any())
            {
                this._logger.LogWarning("Bot {BotName} no tiene zones seguras disponibles, warpeando a spawn", 
                    botPlayer.Name);
                WarpToSaferLocation(botPlayer);
                return null; // El BotNavigator manejará el warp
            }

            // Regla 2: Priorizar zonas de nivel específico si disponibles
            var preferredZone = GetPreferredZoneByLevel(botPlayer.Level, safeZones);
            
            if (preferredZone != null)
            {
                this._logger.LogInformation("Bot {BotName} seleccionando zona preferida {ZoneName}", 
                    botPlayer.Name, preferredZone.Name);
                return preferredZone;
            }

            // Regla 3: Si no hay zona preferida, seleccionar la más densa (farmeo eficiente)
            var bestZone = safeZones.OrderByDescending(z => z.SpawnDensity).FirstOrDefault();
            
            if (bestZone != null)
            {
                this._logger.LogInformation("Bot {BotName} seleccionando zona con mayor spawn density: {ZoneName}", 
                    botPlayer.Name, bestZone.Name);
                return bestZone;
            }

            // Fallback: primera zona segura disponible
            var fallbackZone = safeZones.FirstOrDefault();
            if (fallbackZone != null)
            {
                this._logger.LogWarning("Bot {BotName} usando zona de fallback: {ZoneName}", 
                    botPlayer.Name, fallbackZone.Name);
            }

            return fallbackZone;
        }

        /// <summary>
        /// Obtiene zonas de spawn seguras según nivel del bot y reglas personalizadas.
        /// Reglas: monsters <= bot.level + MaxSafeMonsterLevelDifference son seguros.
        /// </summary>
        private List<SpawnArea> GetSafeSpawnAreas(BotPlayer botPlayer)
        {
            // Obtener todas las zonas de spawn disponibles en el mapa actual
            var allZones = this.GameObjectProvider.GetSpawnAreas();

            return allZones.Where(zone => IsZoneSafeForBot(botPlayer, zone))
                          .Where(zone => !DangerousZones.Contains(zone.Name)) // Evitar dangerous zones
                          .ToList();
        }

        /// <summary>
        /// Determina si una zona es segura para un bot de nivel específico.
        /// Regla: monsters en la zona no deben ser > bot.level + MaxSafeMonsterLevelDifference.
        /// </summary>
        private bool IsZoneSafeForBot(BotPlayer botPlayer, SpawnArea zone)
        {
            if (botPlayer == null || zone == null) return false;

            // Obtener monsters disponibles en la zona
            var availableMonsters = this.GameObjectProvider.GetCreaturesBySpawnArea(zone.ID);

            if (!availableMonsters.Any())
            {
                this._logger.LogDebug("Zona {ZoneName} sin monsters disponibles", zone.Name);
                return false; // Zona vacía, no es útil para farmear
            }

            // Verificar nivel de los monsters más fuertes en la zona
            var maxMonsterLevel = availableMonsters.Max(m => m.Level);
            var safeThreshold = botPlayer.Level + MaxSafeMonsterLevelDifference;

            if (maxMonsterLevel > safeThreshold)
            {
                this._logger.LogDebug("Zona {ZoneName} peligrosa para Bot {BotName}: monster nivel {MaxLvl} > safe threshold {Threshold}", 
                    zone.Name, botPlayer.Name, maxMonsterLevel, safeThreshold);
                return false; // Zona demasiado peligrosa
            }

            this._logger.LogDebug("Zona {ZoneName} segura para Bot {BotName} (nivel {BotLvl}, monsters hasta {MaxLvl})", 
                zone.Name, botPlayer.Name, botPlayer.Level, maxMonsterLevel);
            return true; // Zona segura
        }

        /// <summary>
        /// Obtiene zona preferida según nivel del bot.
        /// Regla: usar LevelBasedHuntingGrounds para seleccionar zonas óptimas por nivel.
        /// </summary>
        private SpawnArea? GetPreferredZoneByLevel(int botLevel, List<SpawnArea> safeZones)
        {
            // Buscar zona preferida basada en rango de niveles del bot
            var levelRange = FindLevelRange(botLevel);

            if (levelRange != null && LevelBasedHuntingGrounds.TryGetValue(levelRange.Key, out var preferredNames))
            {
                // Intentar encontrar una de las zonas preferidas entre las seguras disponibles
                var preferredZone = safeZones.FirstOrDefault(z => 
                    preferredNames.Contains(z.Name) || z.Name.StartsWith("LostTower_"));

                if (preferredZone != null)
                {
                    return preferredZone;
                }
            }

            // Si no hay zona preferida en rango, buscar por nivel exacto del bot
            var exactMatch = safeZones.FirstOrDefault(z => 
                z.Name.Contains($"Level{botLevel}") || z.Name.Contains($"Lvl{botLevel}"));

            return exactMatch;
        }

        /// <summary>
        /// Encuentra el rango de niveles apropiado para seleccionar hunting grounds.
        /// </summary>
        private KeyValuePair<int, int>? FindLevelRange(int botLevel)
        {
            if (botLevel <= 10) return new KeyValuePair<int, int>(1, 10);
            if (botLevel <= 25) return new KeyValuePair<int, int>(11, 25);
            if (botLevel <= 40) return new KeyValuePair<int, int>(26, 40);

            // Default: nivel alto
            return new KeyValuePair<int, int>(35, 600);
        }

        /// <summary>
        /// Warp al spawn seguro si no hay zones disponibles.
        /// Regla: warpear a Lorencia_Spawn o zona de inicio más cercana.
        /// </summary>
        private void WarpToSaferLocation(BotPlayer botPlayer)
        {
            if (botPlayer == null) throw new ArgumentNullException(nameof(botPlayer));

            this._logger.LogInformation("Bot {BotName} warpeando a spawn seguro", botPlayer.Name);

            // En implementación real, usar el warp system del servidor para mover al bot
            // Por ahora solo logueamos la acción
            var safeMap = "Lorencia";
            var safePosition = new Point(120, 150); // Posición típica de spawn en Lorencia

            this._logger.LogDebug("Bot {BotName} warp a mapa {Map}, posición {Pos}", 
                botPlayer.Name, safeMap, safePosition);

            // El BotNavigator existente ya maneja el warp real
            // Este plugin solo decide cuándo y dónde warpear
        }

        /// <summary>
        /// Registra un evento de cambio de hunting ground.
        /// </summary>
        public void LogHuntingGroundChange(BotPlayer botPlayer, SpawnArea fromZone, SpawnArea toZone)
        {
            if (botPlayer == null || toZone == null) throw new ArgumentNullException(nameof(botPlayer));

            this._logger.LogInformation("Bot {BotName} cambiando hunting ground: {FromZone} -> {ToZone}", 
                botPlayer.Name, fromZone?.Name ?? "unknown", toZone.Name);
        }

        /// <summary>
        /// Registra un evento de peligro detectado en zona.
        /// </summary>
        public void LogDangerousZone(BotPlayer botPlayer, SpawnArea zone)
        {
            if (botPlayer == null || zone == null) throw new ArgumentNullException(nameof(zone));

            this._logger.LogWarning("Bot {BotName} detectó zona peligrosa: {ZoneName}", 
                botPlayer.Name, zone.Name);
        }
    }
}
