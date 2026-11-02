#!/usr/bin/env python3
"""Script to modify BotProtagonist.cs and add chat reaction logic."""

file_path = r"C:\Users\Andres\Documents\Codex\2026-10-24\bu\mu-online\OpenMu\src\GameLogic\Bots\BotProtagonist.cs"

# Leer el archivo
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# Buscar y reemplazar la línea de ReactToChatAsync
old_line = "internal virtual ValueTask ReactToChatAsync(Player sender, string message) => ValueTask.CompletedTask;"
new_method = """    internal virtual async ValueTask ReactToChatAsync(Player sender, string message)
    {
        // Reacción genérica: los bots responden cuando les hablas
        await this.SayAsync($"Jugador {sender.Name}: ¿qué quieres?").ConfigureAwait(false);
    }"""

if old_line in content:
    print("✅ Encontrando línea a reemplazar...")
    content = content.replace(old_line, new_method)
    
    # Escribir el archivo modificado
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(content)
    print("✅ Archivo modificado exitosamente")
else:
    print("❌ Línea no encontrada. Buscando variante...")

# Verificar el cambio
with open(file_path, 'r', encoding='utf-8') as f:
    new_content = f.read()

if "Jugador {sender.Name}" in new_content:
    print("✅ Cambio verificado exitosamente")
else:
    print("❌ Cambio no aplicado correctamente")
