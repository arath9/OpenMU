#!/usr/bin/env python3
"""Script to increase chat reaction range from 4 to 12 tiles."""

file_path = r"C:\Users\Andres\Documents\Codex\2026-10-24\bu\mu-online\OpenMu\src\GameLogic\Bots\ProtagonistFeaturePlugIn.cs"

# Leer el archivo
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# Buscar y reemplazar el rango de 4 por 12
old_range = "bot.IsInRange(sender.Position, 4)"
new_range = "bot.IsInRange(sender.Position, 12)"

if old_range in content:
    print("✅ Encontrando rango actual (4 tiles)...")
    content = content.replace(old_range, new_range)
    
    # Escribir el archivo modificado
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(content)
    print("✅ Archivo modificado exitosamente - rango aumentado a 12 tiles")
else:
    print("❌ Rango no encontrado. Buscando variante...")

# Verificar el cambio
with open(file_path, 'r', encoding='utf-8') as f:
    new_content = f.read()

if "bot.IsInRange(sender.Position, 12)" in new_content:
    print("✅ Cambio verificado exitosamente")
else:
    print("❌ Cambio no aplicado correctamente")
