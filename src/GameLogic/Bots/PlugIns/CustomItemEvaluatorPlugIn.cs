// <copyright file="CustomItemEvaluatorPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots.PlugIns
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Extensions.Logging;
    using MUnique.OpenMU.DataModel.Configuration.Items;
    using MUnique.OpenMU.GameLogic.Offline;
    using MUnique.OpenMU.GameLogic.PlayerActions.Items;

    /// <summary>
    /// Custom Item Evaluator PlugIn para bots autónomos de Fase 1.
    /// Extiende el comportamiento del BotEquipmentHandler existente con reglas personalizadas:
    /// - Nunca vender items con opciones (excellent, ancient, luck)
    /// - Proteger gems/jewels automáticamente
    /// - Vender solo items con value < threshold y sin options especiales
    /// - Equipar items que mejoren stats actuales del bot
    /// </summary>
    internal sealed class CustomItemEvaluatorPlugIn : BotFeaturePlugIn
    {
        private const int MaxJewelValueToSell = 100; // Jewels > 100 value se protegen
        private const float MinItemValueThreshold = 500.0f; // Items < 500 value pueden venderse si no tienen options
        private static readonly HashSet<ItemGroup> ProtectedGroups = new()
        {
            ItemGroup.Jewel,      // Jewels y gems siempre protegidos
            ItemGroup.ItemBox,    // Cajas de items nunca vender
            ItemOptionItemClass.Gemstone,  // Gemstones no vender
        };

        private static readonly HashSet<ItemGroup> ProtectedOptions = new()
        {
            ItemOptionItemClass.Excellent,   // Excellent options - proteger siempre
            ItemOptionItemClass.Ancient,     // Ancient options - proteger siempre
            ItemOptionItemClass.Luck,        // Luck options - proteger siempre
            ItemOptionItemClass.SmallStone,  // Small stones - proteger siempre
            ItemOptionItemClass.BigStone,    // Big stones - proteger siempre
        };

        private readonly ILogger<CustomItemEvaluatorPlugIn> _logger;

        /// <summary>
        /// Inicializa el plugin con un logger.
        /// </summary>
        /// <param name="logger">Logger para logging del evaluador.</param>
        public CustomItemEvaluatorPlugIn(ILogger<CustomItemEvaluatorPlugIn> logger)
        {
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Evalúa si un item debería ser equipado por el bot.
        /// Reglas personalizadas: equipar solo si mejora stats actuales, nunca items con options especiales.
        /// </summary>
        /// <param name="botPlayer">El bot que podría equipar el item.</param>
        /// <param name="item">El item evaluado para equipar.</param>
        /// <returns>true si el item debería ser equipado; false de lo contrario.</returns>
        public bool ShouldEquipItem(BotPlayer botPlayer, Item item)
        {
            if (botPlayer == null) throw new ArgumentNullException(nameof(botPlayer));
            if (item == null) throw new ArgumentNullException(nameof(item));

            // Regla 1: Nunca equipar items con opciones especiales (excellent, ancient, luck, etc.)
            var hasProtectedOption = item.ItemOptions.Any(opt => ProtectedOptions.Contains(opt.OptionClass));
            if (hasProtectedOption)
            {
                this._logger.LogDebug("Bot {BotName} no equipa item {ItemId} - tiene opción protegida", botPlayer.Name, item.ID);
                return false;
            }

            // Regla 2: Equipar solo si mejora stats actuales del bot
            var currentEquippedItem = botPlayer.GetItemBySlot(item.Slot);
            if (currentEquippedItem != null)
            {
                var newItemValue = CalculateItemValue(item, botPlayer.Class);
                var currentValue = CalculateItemValue(currentEquippedItem, botPlayer.Class);

                if (newItemValue <= currentValue * 0.95f) // Al menos 5% mejor para equipar
                {
                    this._logger.LogDebug("Bot {BotName} no equipa item {ItemId} - no mejora stats actuales", botPlayer.Name, item.ID);
                    return false;
                }

                this._logger.LogInformation("Bot {BotName} equipando item {ItemId} (mejora del {CurrentVal:F0} al {NewVal:F0})", 
                    botPlayer.Name, item.ID, currentValue, newItemValue);
            }

            return true;
        }

        /// <summary>
        /// Evalúa si un item debería venderse.
        /// Reglas personalizadas: nunca vender items con opciones especiales o gems/jewels valiosos.
        /// </summary>
        /// <param name="item">El item evaluado para vender.</param>
        /// <returns>true si el item puede ser vendido; false de lo contrario.</returns>
        public bool ShouldSellItem(Item item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            // Regla 1: Nunca vender items con opciones especiales
            var hasProtectedOption = item.ItemOptions.Any(opt => ProtectedOptions.Contains(opt.OptionClass));
            if (hasProtectedOption)
            {
                this._logger.LogDebug("Bot {BotName} no vende item {ItemId} - tiene opción protegida", "CustomBot", item.ID);
                return false;
            }

            // Regla 2: Nunca vender gems/jewels de alto valor
            if (ProtectedGroups.Contains(item.Group))
            {
                this._logger.LogDebug("Bot {BotName} no vende item {ItemId} - grupo protegido ({Group})", "CustomBot", item.ID, item.Group);
                return false;
            }

            // Regla 3: Vender solo si valor > threshold (evitar vender basura sin valor)
            var itemValue = CalculateItemValue(item, null);
            if (itemValue < MinItemValueThreshold)
            {
                this._logger.LogDebug("Bot {BotName} no vende item {ItemId} - valor muy bajo ({Val:F0})", "CustomBot", item.ID, itemValue);
                return false;
            }

            // Regla 4: Vender si item es de baja calidad y sin opciones especiales
            if (item.ExcellentLevel == 0 && item.LuckOptionLevel == 0)
            {
                this._logger.LogInformation("Bot {BotName} vendiendo item {ItemId} (valor: {Val:F0}, options: {OptionsCount})", 
                    "CustomBot", item.ID, itemValue, item.ItemOptions.Count);
                return true;
            }

            // Si tiene opciones pero no protegidas, vender si valor es bajo
            if (itemValue < MinItemValueThreshold)
            {
                this._logger.LogInformation("Bot {BotName} vendiendo item {ItemId} con opciones secundarias", "CustomBot", item.ID);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Calcula el valor de un item basado en sus stats y opciones.
        /// </summary>
        /// <param name="item">El item a evaluar.</param>
        /// <param name="characterClass">Clase del personaje para calcular stats específicos.</param>
        /// <returns>Valor estimado del item.</returns>
        private float CalculateItemValue(Item item, CharacterClass? characterClass)
        {
            if (item == null) return 0f;

            // Base value desde el precio de venta del item
            var baseValue = item.SellPrice ?? 0;

            // Bonus por excellent level
            var excellentBonus = item.ExcellentLevel * 50.0f;

            // Bonus por ancient set (si aplica)
            var ancientBonus = item.ItemOptions
                .Where(opt => opt.OptionClass == ItemOptionItemClass.Ancient)
                .Sum(opt => CalculateAncientSetBonus(item, opt));

            // Bonus por luck option
            var luckBonus = item.LuckOptionLevel > 0 ? (item.LuckOptionLevel * 100.0f) : 0;

            // Bonus por gemstones/jewels equipados
            var jewelBonus = CalculateJewelBonus(item);

            return baseValue + excellentBonus + ancientBonus + luckBonus + jewelBonus;
        }

        /// <summary>
        /// Calcula el bonus de un ancient set.
        /// </summary>
        private float CalculateAncientSetBonus(Item item, ItemOptionItem option)
        {
            // Simplificado: cada piece de ancient set vale 200 puntos adicionales
            return 200.0f;
        }

        /// <summary>
        /// Calcula el bonus por gemstones/jewels equipados en el item.
        /// </summary>
        private float CalculateJewelBonus(Item item)
        {
            var jewelCount = item.ItemOptions.Count(opt => opt.OptionClass == ItemOptionItemClass.Gemstone);
            return jewelCount * 150.0f; // Cada gem vale 150 puntos adicionales
        }

        /// <summary>
        /// Protege items específicos que nunca deben ser vendidos o equipados.
        /// </summary>
        public void ProtectItem(Item item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            this._logger.LogInformation("Protegiendo item {ItemId} - marcado como protected", item.ID);
            // En implementación real, añadir a lista de items protegidos persistentes
            // Por ahora solo logueamos
        }

        /// <summary>
        /// Registra un evento de venta del bot.
        /// </summary>
        public void LogSale(Item soldItem)
        {
            if (soldItem == null) throw new ArgumentNullException(nameof(soldItem));

            this._logger.LogInformation("Bot vendió item {ItemId} - valor: {Val:F0}, options: {OptionsCount}", 
                soldItem.ID, CalculateItemValue(soldItem, null), soldItem.ItemOptions.Count);
        }

        /// <summary>
        /// Registra un evento de equipamiento del bot.
        /// </summary>
        public void LogEquip(Item equippedItem)
        {
            if (equippedItem == null) throw new ArgumentNullException(nameof(equippedItem));

            this._logger.LogInformation("Bot equipó item {ItemId} - valor: {Val:F0}", 
                equippedItem.ID, CalculateItemValue(equippedItem, null));
        }
    }
}
