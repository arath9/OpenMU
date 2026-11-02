// <copyright file="ProtagonistGenerator.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.Persistence;

/// <summary>Creates dedicated accounts once; never overwrites an existing player's name or customized equipment.</summary>
internal static class ProtagonistGenerator
{
    internal static string Login(int index) => $"muhero{index + 1}";
    internal static bool IsProtagonistAccount(string login) => Enumerable.Range(0, 4).Any(i => string.Equals(Login(i), login, StringComparison.OrdinalIgnoreCase));

    internal static async ValueTask EnsureAsync(IGameContext game, ProtagonistConfiguration settings)
    {
        settings.Validate();
        var names = new[] { settings.ElmandolionName, settings.BertoName, settings.LeilaName, settings.BichiName };
        var numbers = new byte[] { BotClassNumbers.BladeMasterNumber, BotClassNumbers.BladeMasterNumber, BotClassNumbers.HighElfNumber, BotClassNumbers.DarkLordNumber };
        using var context = game.PersistenceContextProvider.CreateNewPlayerContext(game.Configuration);
        var home = game.Configuration.Maps.Single(m => m.Number == settings.HomeMap);
        for (var i = 0; i < names.Length; i++)
        {
            if (await context.GetAccountByLoginNameAsync(Login(i)).ConfigureAwait(false) is { } existing)
            {
                if (!existing.IsBot || existing.Characters.Count != 1 || existing.Characters.Single().Name != names[i]
                    || existing.Characters.Single().CharacterClass?.Number != numbers[i])
                {
                    throw new InvalidOperationException($"Reserved account {Login(i)} does not match protagonist configuration; not overwriting it.");
                }

                continue;
            }

            if (await context.GetAccountByCharacterNameAsync(names[i]).ConfigureAwait(false) is not null)
            {
                throw new InvalidOperationException($"Character name {names[i]} already exists; choose a different protagonist wire alias.");
            }

            var characterClass = game.Configuration.CharacterClasses.Single(c => c.Number == numbers[i]);
            var account = context.CreateNew<Account>();
            account.LoginName = Login(i);
            account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), 6);
            account.IsBot = true;
            account.Vault = context.CreateNew<ItemStorage>();
            var character = context.CreateNew<Character>();
            character.Name = names[i];
            character.CharacterSlot = 0;
            context.InitializeNewCharacter(character, characterClass);
            character.CurrentMap = home;
            character.PositionX = settings.HomeX;
            character.PositionY = settings.HomeY;
            var level = character.Attributes.Single(a => a.Definition == Stats.Level);
            level.Value = game.Configuration.MaximumLevel;
            character.Experience = game.ExperienceTable[Math.Min((int)level.Value, game.ExperienceTable.Length - 1)];
            character.LevelUpPoints = 0;
            if (i == 0)
            {
                // The hunter is openly hostile; killing him must not penalize the protector.
                character.State = HeroState.PlayerKiller1stStage;
            }
            foreach (var attribute in character.Attributes.Where(a => a.Definition == Stats.BaseStrength || a.Definition == Stats.BaseAgility
                || a.Definition == Stats.BaseVitality || a.Definition == Stats.BaseEnergy || a.Definition == Stats.BaseLeadership))
            {
                attribute.Value = Math.Min(settings.FullStatValue, attribute.Definition!.MaximumValue ?? settings.FullStatValue);
            }

            character.Inventory!.Money = 100000000;
            var equipment = new BotStarterGearEquipper(context, game.Configuration, character, 13);
            equipment.EquipWeapon();
            equipment.EquipArmorSet();
            equipment.AddPotions();
            foreach (var skill in game.Configuration.Skills.Where(s => s.QualifiedCharacters.Contains(characterClass)
                && BotProgression.MayBotOwnSkill(s)
                && BotProgression.MeetsRequirements(s, stat =>
                {
                    var baseStat = BotProgression.TotalToBaseStat(stat);
                    return character.Attributes.FirstOrDefault(a => a.Definition == baseStat)?.Value;
                })))
            {
                var entry = context.CreateNew<SkillEntry>();
                entry.Skill = skill;
                character.LearnedSkills.Add(entry);
            }

            account.Characters.Add(character);
            if (!await context.SaveChangesAsync().ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Could not persist protagonist {names[i]}.");
            }
        }
    }
}
