// <copyright file="YarnBotDialogue.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using System.Globalization;
using Yarn;
using Yarn.Compiler;
using Yarn.Markup;

/// <summary>A single player's conversation. Call only from its owning bot's serialized action queue.</summary>
internal sealed class YarnBotDialogue
{
    private readonly Dialogue _dialogue;
    private readonly CompilationResult _compiled;
    private readonly Queue<(bool Command, string Text)> _output = new();
    private (int Id, string Text)[] _options = [];
    private bool _started;

    /// <summary>Initializes a new instance of the <see cref="YarnBotDialogue"/> class with isolated variables.</summary>
    /// <param name="compiled">The validated compiled program and string table.</param>
    /// <param name="playerName">The initiating human character name.</param>
    internal YarnBotDialogue(CompilationResult compiled, string playerName)
    {
        this._compiled = compiled;
        var variables = new MemoryVariableStore();
        variables.SetValue("$playerName", playerName);
        this._dialogue = new Dialogue(variables);
        this._dialogue.SetProgram(compiled.Program ?? throw new ArgumentException("Missing compiled program.", nameof(compiled)));
        this._dialogue.LineHandler = line => this._output.Enqueue((false, this.Resolve(line)));
        this._dialogue.CommandHandler = command => this._output.Enqueue((true, command.Text));
        this._dialogue.OptionsHandler = options =>
        {
            this._options = options.Options.Where(option => option.IsAvailable)
                .Select(option => (option.ID, this.Resolve(option.Line))).ToArray();
            if (this._options.Length == 0)
            {
                this._dialogue.Stop();
            }
        };
        this._dialogue.LogErrorMessage = error => throw new InvalidOperationException(error);
    }

    /// <summary>Gets a value indicating whether the runtime is running or waiting for input.</summary>
    internal bool IsActive => this._dialogue.IsActive;

    /// <summary>Starts the program or resumes the explicitly selected available option.</summary>
    /// <param name="input">The initiating message or explicit option number/text.</param>
    /// <param name="say">The awaited output sink.</param>
    /// <param name="command">The awaited world-command dispatcher.</param>
    internal async ValueTask RespondAsync(string input, Func<string, ValueTask> say, Func<string, ValueTask> command)
    {
        if (!this._started)
        {
            this._started = true;
            this._dialogue.SetNode("START");
        }
        else if (this._options.Length > 0)
        {
            var selection = int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number - 1
                : Array.FindIndex(this._options, option => string.Equals(option.Text, input.Trim(), StringComparison.OrdinalIgnoreCase));
            if (selection < 0 || selection >= this._options.Length)
            {
                await say("Elige un numero de la lista, o escribe cancelar.").ConfigureAwait(false);
                return;
            }

            this._dialogue.SetSelectedOption(this._options[selection].Id);
            this._options = [];
        }

        // Never run unattended choices or async-void callbacks. Await each world command before continuing.
        for (var step = 0; step < 128; step++)
        {
            this._dialogue.Continue();
            while (this._output.TryDequeue(out var output))
            {
                await (output.Command ? command(output.Text) : say(output.Text)).ConfigureAwait(false);
            }

            if (this._options.Length > 0)
            {
                for (var index = 0; index < this._options.Length; index++)
                {
                    await say($"{index + 1}. {this._options[index].Text}").ConfigureAwait(false);
                }

                return;
            }

            if (!this.IsActive)
            {
                return;
            }
        }

        this._dialogue.Stop();
        throw new InvalidOperationException("Dialogue exceeded its per-message output budget.");
    }

    private string Resolve(Line line)
    {
        if (this._compiled.StringTable is not { } table || !table.TryGetValue(line.ID, out var text) || text.text is null)
        {
            throw new InvalidOperationException($"Missing Yarn line {line.ID}.");
        }

        return LineParser.ExpandSubstitutions(text.text, line.Substitutions);
    }
}
