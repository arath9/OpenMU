// <copyright file="ChatText.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots.LLMIntegration;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>Un turno de conversación para el LLM ("user" o "assistant").</summary>
public sealed record ChatTurn(string Role, string Content);

/// <summary>Limpia y parte texto del LLM para el chat de MU.</summary>
public static class ChatText
{
    /// <summary>Quita razonamiento, saltos de línea, comillas, el nombre adelante y prefijos de comando.</summary>
    public static string Clean(string? text, string botName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Primero el bloque completo, después un </think> suelto.
        var s = Regex.Replace(text, @"<think>[\s\S]*?</think>", string.Empty);
        var end = s.IndexOf("</think>", StringComparison.Ordinal);
        if (end >= 0)
        {
            s = s[(end + "</think>".Length)..];
        }

        s = Regex.Replace(s, @"\s+", " ").Trim();

        if (s.StartsWith(botName + ":", StringComparison.OrdinalIgnoreCase))
        {
            s = s[(botName.Length + 1)..].Trim();
        }

        s = s.Trim('"', '“', '”', ' ');

        // '/', '~', '@', '$', '!' al principio cambian el tipo de mensaje en ChatMessageAction.
        while (s.Length > 0 && "/~@$!".IndexOf(s[0]) >= 0)
        {
            s = s[1..].TrimStart();
        }

        return s;
    }

    /// <summary>Parte el texto por palabras en líneas de hasta maxChars, como máximo maxParts líneas.</summary>
    public static IReadOnlyList<string> Split(string text, int maxChars = 60, int maxParts = 4)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Length > maxChars ? raw[..maxChars] : raw;
            if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
            {
                parts.Add(current.ToString());
                current.Clear();
                if (parts.Count == maxParts)
                {
                    return parts;
                }
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }
}
