#!/usr/bin/env python3
"""Enhance BotProtagonist.cs with personality-based chat responses."""

file_path = r"C:\Users\Andres\Documents\Codex\2026-10-24\bu\mu-online\OpenMu\src\GameLogic\Bots\BotProtagonist.cs"

# Leer el archivo
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# Reemplazar ReactToChatAsync con versión mejorada por personalidad
old_method = """internal virtual async ValueTask ReactToChatAsync(Player sender, string message)
    {
        // Reacción genérica: los bots responden cuando les hablas
        await this.SayAsync($"Jugador {sender.Name}: ¿qué quieres?").ConfigureAwait(false);
    }"""

new_method = """internal virtual async ValueTask ReactToChatAsync(Player sender, string message)
    {
        var lowerMessage = message.ToLowerInvariant();
        
        // Reacciones basadas en personalidad y palabras clave
        
        if (lowerMessage.Contains("hola") || lowerMessage.Contains("que pasa") || lowerMessage.Contains("buenas"))
        {
            await this.SayAsync($"Jugador {sender.Name}: ¿Qué quieres?").ConfigureAwait(false);
        }
        else if (lowerMessage.Contains("pelear") || lowerMessage.Contains("luchar") || lowerMessage.Contains("batalla") || lowerMessage.Contains("combate"))
        {
            await this.SayAsync("¿Quieres probar tu suerte contra mí?").ConfigureAwait(false);
        }
        else if (lowerMessage.Contains("ayuda") || lowerMessage.Contains("necesito") || lowerMessage.Contains("pedir"))
        {
            await this.SayAsync("Pide ayuda a quien puedas confiar, no a un bot.").ConfigureAwait(false);
        }
        else if (lowerMessage.Contains("adios") || lowerMessage.Contains("chau") || lowerMessage.Contains("hasta luego"))
        {
            await this.SayAsync("Hasta la próxima. No te vuelvas muy confiado.").ConfigureAwait(false);
        }
        else if (lowerMessage.Contains("donde") || lowerMessage.Contains("buscando") || lowerMessage.Contains("buscar"))
        {
            await this.SayAsync("Busco rivales dignos, no turistas como tú.").ConfigureAwait(false);
        }
        else
        {
            // Respuesta genérica pero más natural
            var responses = new[]
            {
                $"Jugador {sender.Name}: ¿Qué tramas?",
                "¿Por qué me hablas? No soy tu amigo.",
                "Habla rápido, no tengo toda la tarde para ti.",
                "¿Qué quieres ahora? No estoy de humor."
            };
            
            // Elegir respuesta aleatoria simple (usando hash del mensaje)
            var hash = message.GetHashCode() % responses.Length;
            await this.SayAsync(responses[hash]).ConfigureAwait(false);
        }
    }"""

if old_method in content:
    print("✅ Encontrando método ReactToChatAsync...")
    content = content.replace(old_method, new_method)
    
    # Escribir el archivo modificado
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(content)
    print("✅ Archivo modificado exitosamente - personalidad implementada")
else:
    print("❌ Método no encontrado. Buscando variante...")

# Verificar el cambio
with open(file_path, 'r', encoding='utf-8') as f:
    new_content = f.read()

if "¿Quieres probar tu suerte" in new_content:
    print("✅ Cambio verificado exitosamente - personalidad implementada")
else:
    print("❌ Cambio no aplicado correctamente")
