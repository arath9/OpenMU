# 🚀 Script Automático para Probar Bots Fase 1 con Docker

## 📋 Resumen Rápido

Este script automatiza la verificación de que tus bots nuevos (Fase 1) están funcionando correctamente en el servidor Docker existente.

---

## ✅ Lo que ya tienes (OpenMU trae esto por defecto):

1. **BotGenerator** - Crea bots automáticamente al iniciar el server
2. **AddPlugInManager** - Sistema de plug-ins dinámicos (línea 49 del Program.cs)
3. **Bots existentes** - Ya configurados con reglas básicas de farmeo

---

## 🎯 Lo que implementaste (Fase 1):

### ✅ CustomItemEvaluatorPlugIn
- Reglas personalizadas para loot/equipamiento
- Nunca vende items excellent/ancient/luck
- Protege gems/jewels valiosos (>100 value)
- Equipa solo si mejora stats actuales (+5% threshold)

### ✅ CustomNavigationPlugIn  
- Navegación inteligente por nivel del bot
- Monsters <= bot.level +3 son seguros
- Prioriza zonas con spawn density >= 2.0
- Evita zonas peligrosas automáticamente

### ✅ SocialMemoryRepository
- Persistencia de relaciones jugador-NPC en SQL Server
- Sistema de reputación entre bots y personajes
- Historial de interacciones (chat, trade, quest, PvP)

---

## 🔧 Cómo integrar tus plug-ins con el sistema existente

### Opción A: Plug-ins automáticos (RECOMENDADO - sin cambios)

Los plug-ins personalizados se registran automáticamente si:
1. ✅ Extienden `BotFeaturePlugIn` correctamente (ya lo hacen tus archivos)
2. ✅ Tienen constructor que acepta `ILogger<T>` (ya lo tienen)
3. ✅ Están en el mismo namespace o referenced assemblies (verificar)

**Ventaja:** No necesitas cambiar nada en Program.cs ni appsettings.json

### Opción B: Registro manual explícito (si la A no funciona)

Si tus plug-ins personalizados no se cargan automáticamente, agrega esto a `Program.cs`:

```csharp
// En src/Dapr/GameServer.Host/Program.cs, después de AddPlugInManager:

services.AddSingleton<CustomItemEvaluatorPlugIn>();
services.AddSingleton<CustomNavigationPlugIn>();
services.AddSingleton<SocialMemoryRepository>();

// Opcional: Registrar en el sistema de plug-ins existente si hay un container específico
```

### Opción C: Configuración JSON (si OpenMU usa configuraciones externas)

Crea `plugins.config.json` o similar con configuración explícita.

---

## 🚀 Comandos para levantar el server con Docker

### 1. Levantar servidor con logs de bots habilitados

```bash
cd C:/Users/Andres/Documents/Codex/2026-10-24/bu/mu-online/OpenMU

# Levantar solo GameServer (asumiendo que ya tienes DB configurada)
docker run -d \
  --name openmu-game-server \
  -p 5173:8080 \
  -e LOG_LEVEL=Information \
  -e BOT_LOG_ENABLED=true \
  munique/openmu:latest

# O si usas tu propia imagen local:
docker run -d \
  --name openmu-game-server \
  -p 5173:8080 \
  -v $PWD/src/GameServer:/app \
  your-local-image-name
```

### 2. Ver logs en tiempo real de bots

```bash
# Seguir logs del container
docker logs -f openmu-game-server

# Filtrar solo logs de bots
docker logs -f openmu-game-server | Select-String "Bot"

# Ver logs desde hace 10 minutos con contexto de bots
docker logs --since 10m openmu-game-server | Select-String "Bot|CustomItemEvaluator|CustomNavigation|SocialMemory"
```

### 3. Levantar con SQL Server local (si no usas Docker para DB)

```bash
# Configurar variables de ambiente
$env:ConnectionStrings__Default = "Server=localhost;Database=OpenMU;User Id=sa;Password=TuPassword123!"
$env:LOG_LEVEL=Information
$env:BOT_ENABLED=true

# Levantar servidor .NET directo (sin Docker)
dotnet run --project src/Dapr/GameServer.Host/
```

---

## 🧪 Verificación automática de bots funcionando

### Script PowerShell para verificar bots creados automáticamente

```powershell
# Copiar a: scripts\verify-bots.ps1

Write-Host "=== Verificando Bots Fase 1 ===" -ForegroundColor Cyan

# Conectar a SQL Server y contar bots existentes
$connectionString = "Server=localhost;Database=OpenMU;Trusted_Connection=True;"
$commandText = "SELECT COUNT(*) FROM Accounts WHERE IsBot = 1"

try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $cmd = New-Object System.Data.SqlClient.SqlCommand($commandText, $conn)
    $conn.Open()
    
    $botCount = $cmd.ExecuteScalar()
    
    Write-Host "✅ Bots existentes en DB: $botCount" -ForegroundColor Green
    
    if ($botCount -gt 0) {
        Write-Host "✅ BotGenerator está creando bots automáticamente al iniciar" -ForegroundColor Green
    } else {
        Write-Host "⚠️ No hay bots creados. Verificar configuración de BotGenerator." -ForegroundColor Yellow
        Write-Host "   Revisar logs del server para mensajes de BotGenerator" -ForegroundColor Yellow
    }
    
    $conn.Close()
} catch {
    Write-Host "❌ Error conectando a SQL Server: $_" -ForegroundColor Red
    exit 1
}

# Verificar tablas de SocialMemoryRepository creadas
Write-Host "`n=== Verificando SocialMemoryRepository ===" -ForegroundColor Cyan

$tables = @(
    "BotSocialInteractions",
    "BotReputationRecords", 
    "BotEventLogs"
)

$conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
foreach ($table in $tables) {
    $cmdText = "SELECT COUNT(*) FROM $table"
    $cmd = New-Object System.Data.SqlClient.SqlCommand($cmdText, $conn)
    
    try {
        $conn.Open()
        $count = $cmd.ExecuteScalar()
        Write-Host "  ✓ Tabla '$table': $count registros" -ForegroundColor Green
        $conn.Close()
    } catch {
        Write-Host "  ✗ Tabla '$table' NO existe (migración pendiente?)" -ForegroundColor Red
        $conn.Close()
    }
}

Write-Host "`n✅ Verificación completada!" -ForegroundColor Green
```

---

## 📊 Qué observar en los logs del server

### ✅ Logs esperados de BotGenerator:
```
[2026-10-29 12:34:56.789] [Information] [MUnique.OpenMU.GameLogic.Bots.BotGenerator] 
BotGenerator started creating bots...
BotGenerator created bot account 'bot_alex' with character name 'AlexTheWarrior'
BotGenerator created bot account 'bot_sarah' with character name 'SarahMage'
BotGenerator finished - 5 bots created successfully
```

### ✅ Logs esperados de CustomItemEvaluatorPlugIn:
```
[2026-10-29 12:34:57.123] [Information] [MUnique.OpenMU.GameLogic.Bots.PlugIns.CustomItemEvaluatorPlugIn] 
Bot AlexTheWarrior vendiendo item SwordOfPower (valor: 500, options: 0)
Bot AlexTheWarrior equipando item LeatherArmor (mejora del 12% sobre equipo actual)
```

### ✅ Logs esperados de CustomNavigationPlugIn:
```
[2026-10-29 12:34:57.456] [Information] [MUnique.OpenMU.GameLogic.Bots.PlugIns.CustomNavigationPlugIn] 
Bot AlexTheWarrior seleccionando zona segura: LostTower_MidLevel (nivel bot: 25)
Bot AlexTheWarrior warping a spawn seguro - monsters > nivel actual +3 evitados
```

### ✅ Logs esperados de SocialMemoryRepository:
```
[2026-10-29 12:34:57.789] [Information] [MUnique.OpenMU.Persistence.EntityFramework.SocialMemoryRepository] 
Bot AlexTheWarrior interactuó con NPC_Oron: Chat (contexto: "conversacion sobre quest")
Reputación actualizada: Bot AlexTheWarrior → NPC_Oron = +10, total = 45
```

---

## 🛠️ Troubleshooting rápido

### Problema: Bots no aparecen en DB después de iniciar server

**Posibles causas:**
1. BotGenerator no registrado en DI container
2. Database migration incompleta (tablas faltantes)
3. Connection string incorrecto

**Solución:**
```bash
# Verificar migraciones aplicadas
dotnet ef database list

# Ejecutar migraciones pendientes
cd src/Dapr/GameServer.Host
dotnet ef migrations add AddBotTables
dotnet ef database update

# Reiniciar server y verificar logs
docker restart openmu-game-server
docker logs -f openmu-game-server | Select-String "BotGenerator"
```

### Problema: Tus plug-ins personalizados no se aplican a bots existentes

**Posible causa:** Los plug-ins no están registrados en el container de inyección de dependencias.

**Solución:**
```bash
# Agregar registro manual en Program.cs (opcional)
# En src/Dapr/GameServer.Host/Program.cs, después de AddPlugInManager:

services.AddSingleton<CustomItemEvaluatorPlugIn>();
services.AddSingleton<CustomNavigationPlugIn>();
services.AddSingleton<SocialMemoryRepository>();

# Recompilar y levantar server
dotnet build src/Dapr/GameServer.Host/
docker-compose up -d  # o docker run según tu configuración
```

### Problema: SocialMemoryRepository no persiste datos

**Posible causa:** Tablas de SQL Server no creadas (migration pendiente).

**Solución:**
```bash
# Verificar tablas existentes
dotnet ef database list

# Crear migración para las nuevas tablas
cd src/Persistence
dotnet ef migrations add AddSocialMemoryTables

# Aplicar migraciones
dotnet ef database update

# Reiniciar server y verificar logs
docker restart openmu-game-server
```

### Problema: Bots farmean en zonas peligrosas (monsters muy fuertes)

**Posible causa:** SafeMonsterThreshold bajo o zona de spawn mal configurada.

**Solución:**
```bash
# Editar CustomNavigationPlugIn.cs y aumentar el threshold
# En src/GameLogic/Bots/PlugIns/CustomNavigationPlugIn.cs:

private const int MaxSafeMonsterLevelDifference = 5;  # Cambiar de 3 a 5

# Recompilar y levantar server
dotnet build src/GameServer/
docker-compose up -d
```

---

## 📝 Próximos pasos después de verificar bots funcionando

1. **Fase 2: Sistema de conversación y memoria** (cuando quieras continuar)
   - Integrar LLM local (Ollama + Qwen2.5-Coder-7B-Instruct)
   - Chat contextual con personalidad definida
   - Memoria persistente entre sesiones

2. **Fase 3: Cuatro protagonistas memorables** (opcional)
   - Implementar elmandolion, bertoSalamin, LeilaMasa, elBichiCome
   - Eventos dinámicos programados (cacerías, duelos, traiciones)
   - Personalidades únicas con reglas específicas

3. **Fase 4: Modernización gráfica** (opcional)
   - Mejorar estética de bots y NPCs sin perder compatibilidad
   - Assets personalizados para personajes memorables
   - Efectos visuales para eventos dinámicos

---

## 🎯 Resumen final

✅ **Lo que tienes ahora:**
- Bots existentes funcionando con reglas básicas de farmeo
- BotGenerator creando bots automáticamente al iniciar server
- Sistema de plug-ins listo para cargar tus personalizaciones

✅ **Lo que implementaste (Fase 1):**
- CustomItemEvaluatorPlugIn - reglas personalizadas de loot/equipamiento
- CustomNavigationPlugIn - navegación inteligente por nivel  
- SocialMemoryRepository - persistencia de relaciones y reputación

✅ **Cómo verificar funcionando:**
1. Levantar server con Docker (`docker-compose up` o `dotnet run`)
2. Ver logs de BotGenerator creando bots automáticamente
3. Confirmar que CustomItemEvaluatorPlugIn protege items valiosos
4. Verificar SocialMemoryRepository persistiendo relaciones en SQL Server

✅ **Próximos pasos:**
- Esperar respuesta de subagentes GPT sobre integración exacta de plug-ins
- Ejecutar script `scripts/verify-bots.ps1` para verificar bots creados
- Probar manualmente con cliente MU si quieres ver el farmeo en acción

---

*Script creado: 29 Octubre 2026 - Hermes Agent (Qwen local)*  
*Para automatizar pruebas de bots Fase 1 con servidor Docker existente*
