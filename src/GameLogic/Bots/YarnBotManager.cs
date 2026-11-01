// <copyright file="YarnBotManager.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using System.Collections.Concurrent;
using System.IO;
using Yarn.Compiler;

/// <summary>Locates trusted server-owned protagonist dialogue resources.</summary>
public sealed class YarnBotManager
{
    private readonly ConcurrentDictionary<string, Lazy<CompilationResult>> _programs = new();

    /// <summary>Initializes a new instance of the <see cref="YarnBotManager"/> class relative to the executable, not the working directory.</summary>
    /// <param name="basePath">An optional explicit resource directory.</param>
    public YarnBotManager(string? basePath = null)
    {
        this.BasePath = Path.GetFullPath(basePath ?? Path.Combine(AppContext.BaseDirectory, "Bots", "Yarn"));
    }

    /// <summary>Gets the absolute resource directory.</summary>
    public string BasePath { get; }

    /// <summary>Resolves a canonical identity or dialogue resource name.</summary>
    /// <param name="dialogueName">The identity or resource name, never a player-supplied path.</param>
    /// <returns>The absolute path.</returns>
    public string GetDialoguePath(string dialogueName)
    {
        var name = dialogueName.ToLowerInvariant() switch
        {
            "elmandolion" or "mandolion" => "mandolion",
            "bertosalamin" or "bertosala" => "bertosala",
            "leilamasa" => "leilamasa",
            "elbichicome" or "bichicome" => "bichicome",
            _ => throw new ArgumentException("Unknown protagonist dialogue.", nameof(dialogueName)),
        };
        return Path.Combine(this.BasePath, name + ".yarn");
    }

    /// <summary>Checks whether a protagonist resource is available.</summary>
    /// <param name="dialogueName">The canonical identity or resource name.</param>
    /// <returns>Whether the file exists.</returns>
    public bool DialogueExists(string dialogueName) => File.Exists(this.GetDialoguePath(dialogueName));

    /// <summary>Creates an isolated runtime backed by a cached, validated program.</summary>
    /// <param name="identity">The canonical protagonist or resource name.</param>
    /// <param name="playerName">The initiating human character name.</param>
    internal YarnBotDialogue CreateDialogue(string identity, string playerName)
    {
        var path = this.GetDialoguePath(identity);
        var compiled = this._programs.GetOrAdd(path, file => new Lazy<CompilationResult>(() => Compile(file))).Value;
        return new YarnBotDialogue(compiled, playerName);
    }

    private static CompilationResult Compile(string path)
    {
        var library = new global::Yarn.Dialogue(new global::Yarn.MemoryVariableStore()).Library;
        var result = Compiler.Compile(CompilationJob.CreateFromString(path, File.ReadAllText(path), library));
        if (result.Program is null || result.Diagnostics.Any(diagnostic => diagnostic.Severity == Diagnostic.DiagnosticSeverity.Error))
        {
            throw new InvalidDataException($"Yarn compilation failed for {path}: {string.Join(Environment.NewLine, result.Diagnostics)}");
        }

        return result;
    }
}
