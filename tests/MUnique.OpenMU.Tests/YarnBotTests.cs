// <copyright file="YarnBotTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.Bots;

/// <summary>Exercises the real Yarn runtime and server dialogue integration.</summary>
[TestFixture]
public class YarnBotTests
{
    /// <summary>Verifies resources are copied and canonical names resolve.</summary>
    [TestCase("elmandolion", "mandolion.yarn")]
    [TestCase("bertoSalamin", "bertosala.yarn")]
    [TestCase("LeilaMasa", "leilamasa.yarn")]
    [TestCase("elBichiCome", "bichicome.yarn")]
    public void ResourcesAreCopiedAndCanonicalNamesResolve(string identity, string file)
    {
        var manager = new YarnBotManager();
        Assert.That(System.IO.Path.GetFileName(manager.GetDialoguePath(identity)), Is.EqualTo(file));
        Assert.That(manager.DialogueExists(identity), Is.True);
    }

    /// <summary>Verifies resource paths cannot escape trusted directory.</summary>
    [TestCase("../mandolion")]
    [TestCase("C:/mandolion")]
    [TestCase("mandolion.yarn")]
    public void ResourcePathsCannotEscapeTrustedDirectory(string path)
        => Assert.Throws<ArgumentException>(() => new YarnBotManager().GetDialoguePath(path));

    /// <summary>Verifies dialogue runtime is part of game logic.</summary>
    [Test]
    public void DialogueRuntimeIsPartOfGameLogic()
        => Assert.That(typeof(BotProtagonist).Assembly.GetType("MUnique.OpenMU.GameLogic.Bots.YarnBotDialogue"), Is.Not.Null);

    /// <summary>Verifies runtime waits for choice and resolves player substitution.</summary>
    [Test]
    public async Task RuntimeWaitsForChoiceAndResolvesPlayerSubstitution()
    {
        var session = new YarnBotManager().CreateDialogue("mandolion", "Visitor");
        var lines = new List<string>();
        var commands = new List<string>();
        ValueTask Say(string text) { lines.Add(text); return ValueTask.CompletedTask; }
        ValueTask Command(string text) { commands.Add(text); return ValueTask.CompletedTask; }
        await session.RespondAsync("hola", Say, Command).ConfigureAwait(false);
        Assert.That(lines[0], Does.Contain("Visitor"));
        Assert.That(session.IsActive, Is.True);
        Assert.That(commands, Is.Empty);
        await session.RespondAsync("99", Say, Command).ConfigureAwait(false);
        Assert.That(commands, Is.Empty, "Invalid choices must not silently select a reward.");
        await session.RespondAsync("1", Say, Command).ConfigureAwait(false);
        Assert.That(commands, Is.EqualTo(new[] { "ShowLore lorencia" }));
        Assert.That(session.IsActive, Is.False);
    }

    /// <summary>Verifies every shipped branch compiles and completes.</summary>
    [TestCase("mandolion", "1", "ShowLore lorencia")]
    [TestCase("mandolion", "2", null)]
    [TestCase("mandolion", "3", null)]
    [TestCase("bertosala", "1", "GiveItem \"Apple\" 1")]
    [TestCase("bertosala", "2", "StartQuest \"1:0\"")]
    [TestCase("bertosala", "3", "ShowLore mercado")]
    [TestCase("bertosala", "4", null)]
    [TestCase("leilamasa", "1", "ShowLore leila")]
    [TestCase("leilamasa", "2", null)]
    [TestCase("leilamasa", "3", null)]
    [TestCase("bichicome", "1", "ShowLore lorencia")]
    [TestCase("bichicome", "2", null)]
    [TestCase("bichicome", "3", null)]
    public async Task EveryShippedBranchCompilesAndCompletes(string identity, string choice, string? expectedCommand)
    {
        var session = new YarnBotManager().CreateDialogue(identity, "Tester");
        var commands = new List<string>();
        ValueTask Say(string text) => ValueTask.CompletedTask;
        ValueTask Command(string text) { commands.Add(text); return ValueTask.CompletedTask; }
        await session.RespondAsync("hola", Say, Command).ConfigureAwait(false);
        await session.RespondAsync(choice, Say, Command).ConfigureAwait(false);
        Assert.That(session.IsActive, Is.False);
        Assert.That(commands, Is.EqualTo(expectedCommand is null ? Array.Empty<string>() : new[] { expectedCommand }));
    }

    /// <summary>Verifies command tokenizer preserves quoted arguments.</summary>
    [TestCase("GiveItem \"Large Healing Potion\" 2", new[] { "GiveItem", "Large Healing Potion", "2" })]
    [TestCase(" ShowLore  lorencia ", new[] { "ShowLore", "lorencia" })]
    public void CommandTokenizerPreservesQuotedArguments(string command, string[] expected)
        => Assert.That(YarnCommandHandlers.Split(command), Is.EqualTo(expected));

    /// <summary>Verifies command tokenizer rejects malformed quotes.</summary>
    [TestCase("GiveItem \"unclosed")]
    [TestCase("GiveItem \"escaped\\")]
    public void CommandTokenizerRejectsMalformedQuotes(string command)
        => Assert.Throws<FormatException>(() => YarnCommandHandlers.Split(command));

    /// <summary>Verifies invalid yarn is rejected with diagnostics.</summary>
    [Test]
    public void InvalidYarnIsRejectedWithDiagnostics()
    {
        var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "YarnProbe", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "mandolion.yarn"), "not a Yarn node");
            var error = Assert.Throws<System.IO.InvalidDataException>(() => new YarnBotManager(directory).CreateDialogue("mandolion", "Tester"));
            Assert.That(error!.Message, Does.Contain("compilation failed"));
            Assert.Throws<System.IO.FileNotFoundException>(() => new YarnBotManager(directory).CreateDialogue("bertosala", "Tester"));
        }
        finally
        {
            System.IO.Directory.Delete(directory, true);
        }
    }

    /// <summary>Verifies hidden options use actual yarn ids and commands are awaited.</summary>
    [Test]
    public async Task HiddenOptionsUseActualYarnIdsAndCommandsAreAwaited()
    {
        var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "YarnProbe", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "mandolion.yarn"), "title: START\n---\n-> Hidden <<if false>>\n    Wrong.\n-> Visible\n    <<ShowLore lorencia>>\n    Finished.\n===\n");
            var session = new YarnBotManager(directory).CreateDialogue("mandolion", "Tester");
            var lines = new List<string>();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ValueTask Say(string text) { lines.Add(text); return ValueTask.CompletedTask; }
            await session.RespondAsync("hola", Say, _ => ValueTask.CompletedTask).ConfigureAwait(false);
            Assert.That(lines, Is.EqualTo(new[] { "1. Visible" }));
            var execution = session.RespondAsync("1", Say, _ => new ValueTask(release.Task)).AsTask();
            Assert.That(execution.IsCompleted, Is.False);
            Assert.That(lines, Does.Not.Contain("Finished."));
            release.SetResult();
            await execution.ConfigureAwait(false);
            Assert.That(lines, Does.Contain("Finished."));
            Assert.That(lines, Does.Not.Contain("Wrong."));
        }
        finally
        {
            System.IO.Directory.Delete(directory, true);
        }
    }

    /// <summary>Verifies dialogue manager is part of game logic.</summary>
    [Test]
    public void DialogueManagerIsPartOfGameLogic()
    {
        Assert.That(typeof(BotProtagonist).Assembly.GetType("MUnique.OpenMU.GameLogic.Bots.YarnBotManager"), Is.Not.Null,
            "The server needs a resource manager, not the standalone Unity prototype.");
    }
}
