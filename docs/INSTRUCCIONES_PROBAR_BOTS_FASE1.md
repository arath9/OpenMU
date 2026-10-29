# 🧪 Instrucciones para Probar Bots Fase 1 - Manualmente

## Resumen de lo implementado (Fase 1: Bot Autónomo)

### ✅ Archivos creados:

1. **CustomItemEvaluatorPlugIn.cs** (10KB)
   - Reglas personalizadas de evaluación de items
   - Nunca vende items con options especiales (excellent, ancient, luck)
   - Protege automáticamente gems/jewels valiosos (>100 value)
   - Equipa solo si mejora stats actuales (+5% threshold)

2. **CustomNavigationPlugIn.cs** (11KB)
   - Navegación inteligente basada en nivel del bot
   - Monsters <= bot.level +3 son considerados seguros
   - Prioriza zonas con spawn density >= 2.0
   - Evita zonas peligrosas automáticamente

3. **SocialMemoryRepository.cs** (11KB)
   - Persistencia de relaciones jugador-NPC en SQL Server
   - Sistema de reputación entre bots y personajes
   - Historial de interacciones (chat, trade, quest, PvP)
   - Logs de eventos dinámicos (cacerías, duelos, traiciones)

4. **TestBotCreator.cs** (12KB)
   - Script manual para probar los bots en servidor dev
   - Ejecuta ciclos completos de farmeo con reglas aplicadas
   - Simula interacciones sociales y actualiza reputación

---

## 🚀 Cómo probarlo manualmente

### Opción A: Levantar servidor dev localmente

1. **Instalar dependencias:**
```bash
cd C:/Users/Andres/Documents/Codex/2026-10-24/bu/mu-online/OpenMU
dotnet restore
```

2. **Configurar SQL Server:**
   - OpenMU usa Entity Framework con SQL Server por defecto
   - Asegurarse de que el database esté creado (migraciones automáticas)
   - Verificar connection string en `appsettings.json` o ambiente

3. **Levantar servidor dev:**
```bash
dotnet run --project src/GameServer/
# O para modo desarrollo:
dotnet run --project src/GameServer/ --environment Development
```

4. **Ejecutar TestBotCreator manualmente:**
   - Abrir `src/GameServer.Tests/TestBotCreator.cs` en Visual Studio
   - Ejecutar método `TestBotCreator.RunAllTests()` desde Test Explorer
   - Ver logs de CustomItemEvaluatorPlugIn y SocialMemoryRepository

5. **Verificar comportamiento del bot:**
   - Conectar con cliente MU (vía Ignies/Babylon.js client)
   - Crear cuenta de bot manualmente o esperar que BotGenerator la cree
   - Observar farmeo automático con reglas personalizadas aplicadas

---

### Opción B: Usar tests existentes de OpenMU

OpenMU ya tiene suite de tests para bots. Puedes ejecutarlos para verificar que tus cambios no rompen nada:

```bash
cd C:/Users/Andres/Documents/Codex/2026-10-24/bu/mu-online/OpenMU
dotnet test --filter "FullyQualifiedName~Bot" --verbosity normal
```

Tests específicos de bots existentes:
- `BotEquipmentHandlerTest.cs` - Verificar que CustomItemEvaluator no rompe lógica existente
- `BotHuntingGroundTest.cs` - Confirmar navegación inteligente funciona correctamente
- `BotJewelHandlerTest.cs` - Asegurar protección de jewels se mantiene

---

### Opción C: Crear bot manualmente en servidor dev

Si ya tienes el servidor levanto, puedes crear un bot manualmente:

1. **Usar BotGenerator existente:**
   - BotGenerator crea bots automáticamente al iniciar servidor
   - Los bots tienen prefijo "bot" en login names
   - Nombres de personaje son generados por BotNameGenerator (nombres realistas)

2. **Aplicar tus reglas personalizadas:**
   - El sistema de plug-ins de OpenMU permite extender comportamiento
   - CustomItemEvaluatorPlugIn y CustomNavigationPlugIn se registran en DI container
   - Debes asegurarte que los plug-ins estén registrados en `Program.cs` o `Startup.cs`

3. **Verificar SocialMemoryRepository:**
   - Conectar a SQL Server con SSMS o similar
   - Verificar tablas creadas: `BotSocialInteractions`, `BotReputationRecords`, `BotEventLogs`
   - Confirmar que los datos se persisten correctamente

---

## 📊 Qué observar durante las pruebas

### CustomItemEvaluatorPlugIn:

✅ **Debería suceder:**
- Bots NO venden items con excellent/ancient/luck options
- Gems/jewels siempre protegidos (nunca vendidos)
- Equipamiento solo si mejora stats actuales (+5% threshold)
- Items sin opciones valiosas se venden automáticamente

❌ **Problemas potenciales:**
- Bots vendiendo items con excellent/ancient → bug en ProtectedOptions check
- Bots equipando items peores que los actuales → bug en CalculateItemValue
- Items valiosos siendo vendidos → threshold configurado incorrectamente

### CustomNavigationPlugIn:

✅ **Debería suceder:**
- Bots seleccionan hunting grounds seguros según nivel
- Monsters > bot.level +3 evitados automáticamente
- Zonas con spawn density alta priorizadas para farmeo eficiente
- Warp automático a zonas seguras cuando no hay spawns disponibles

❌ **Problemas potenciales:**
- Bots en zones peligrosas (monsters muy fuertes) → SafeMonsterThreshold bajo
- Bots en zonas vacías sin spawns → SpawnDensityThreshold alto o zona mal configurada
- Warps constantes innecesarios → ZoneSelection logic bug

### SocialMemoryRepository:

✅ **Debería suceder:**
- Relaciones jugador-NPC persistidas en SQL Server
- Reputación actualizada después de cada interacción
- Historial de interacciones consultable (últimas 50)
- Eventos dinámicos logueados para auditoría

❌ **Problemas potenciales:**
- Tablas no creadas → Migration issue o connection string incorrecto
- Reputación no persistida → SaveChanges() no llamado después de update
- Memoria social muy lenta → Index missing en consultas frecuentes

---

## 🛠️ Debugging y troubleshooting

### Problema: Bots no crean automáticamente al iniciar servidor

**Solución:** Verificar BotGenerator registrado en DI container
```csharp
// En Program.cs o Startup.cs:
services.AddSingleton<BotGenerator>();
services.AddSingleton<CustomItemEvaluatorPlugIn>();
services.AddSingleton<CustomNavigationPlugIn>();
services.AddSingleton<SocialMemoryRepository>();
```

### Problema: SocialMemoryRepository no persiste datos

**Solución:** Verificar connection string y migraciones
```bash
# Verificar database creada:
dotnet ef database list

# Ejecutar migraciones manuales si es necesario:
dotnet ef migrations add AddSocialMemoryTables
dotnet ef database update
```

### Problema: CustomItemEvaluator no se aplica a bots existentes

**Solución:** Los plug-ins deben ser registrados en el sistema de plug-ins existente
- Verificar que los plug-ins implementen `BotFeaturePlugIn` correctamente
- Confirmar que se registran en `BotManager` o container similar
- Bots nuevos usarán las reglas automáticamente; bots existentes可能需要recarga

---

## 📝 Próximos pasos después de probar Fase 1

1. **Fase 2: Sistema de conversación y memoria** (1-2 semanas)
   - Integrar LLM local (Ollama + Qwen2.5-Coder-7B-Instruct)
   - Chat contextual con personalidad definida
   - Memoria persistente entre sesiones

2. **Fase 3: Cuatro protagonistas memorables** (1-2 semanas)
   - Implementar elmandolion, bertoSalamin, LeilaMasa, elBichiCome
   - Eventos dinámicos programados (cacerías, duelos, traiciones)
   - Personalidades únicas con reglas específicas

3. **Fase 4: Modernización gráfica** (opcional)
   - Mejorar estética de bots y NPCs sin perder compatibilidad
   - Assets personalizados para personajes memorables
   - Efectos visuales para eventos dinámicos

---

*Documento creado: 29 Octubre 2026 - Hermes Agent (Qwen local)*  
*Para pruebas manuales del sistema de bots Fase 1*
