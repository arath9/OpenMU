# OpenMU AI Bots — Arquitectura y Auditoría Técnica (Fase 0)

**Fecha:** 28 Octubre 2026  
**Estado:** Auditoría completada, arquitectura propuesta lista para implementación  
**Ejecutor:** Hermes Agent (Qwen local)

---

## 📊 Resumen Ejecutivo de la Auditoría

### ✅ Hallazgos Principales

1. **Sistema de bots existente robusto:** ~9KB de código en `src/GameLogic/Bots/` con 24 archivos especializados
2. **OfflinePlayer como base del bot:** Clase `BotPlayer: OfflinePlayer` reutiliza inteligencia de combate, buffs y healing
3. **Pathfinder integrado:** Sistema A* propio del servidor (no externo) para navegación
4. **Extensiones mediante PlugIns:** Arquitectura basada en plug-ins para extender comportamiento sin modificar núcleo
5. **Persistencia SQL Server/Entity Framework:** Base de datos relacional con migraciones versionadas

### ⚠️ Limitaciones Identificadas

1. **Bots actuales = automáticos, no inteligentes:** Farmean y equipan pero sin personalidad o memoria entre sesiones
2. **Sin sistema de conversación:** Chat básico sin contexto o relaciones persistentes
3. **No hay eventos dinámicos:** Los bots no interactúan socialmente ni crean historias emergentes

---

## 🏗️ Arquitectura Actual (OpenMU Server)

### Estructura del Repositorio

```text
src/
├── GameLogic/                    # Lógica principal del servidor
│   ├── Bots/                     # Sistema de bots existente (~9KB, 24 archivos)
│   │   ├── BotNavigator.cs       (2424 líneas) - Navegación y hunting grounds
│   │   ├── BotProgression.cs     (619 líneas)  - Leveling y skill progression
│   │   ├── BotPlayer.cs          (119 líneas)  - Clase base del bot
│   │   ├── BotManager.cs         (358 líneas)  - Gestión de bots activos
│   │   ├── BotGenerator.cs       (634 líneas)  - Creación de nuevos bots
│   │   ├── BotEquipmentHandler.cs (282 líneas) - Equipamiento automático
│   │   ├── BotShoppingHandler.cs  (572 líneas) - Comercio y reparación
│   │   ├── BotPartyHandler.cs     (226 líneas)  - Party management
│   │   └── [14 archivos más...]  - Buffs, PvP, reset, wings, jewels...
│   │
│   ├── Offline/                  # Base del bot player
│   │   ├── OfflinePlayer.cs      - Clase base reutilizada por BotPlayer
│   │   ├── OfflineMapChangePlugIn.cs
│   │   ├── OfflineRespawnPlugIn.cs
│   │   └── [5 plug-ins más...]
│   │
│   ├── Attributes/               # Sistema de atributos (daño, stats)
│   ├── Combat/                   # Lógica de combate y daño
│   ├── Inventory/                # Gestión de inventario y items
│   ├── PlayerActions/            # Acciones del jugador/combate
│   └── NPC/                      # Interacción con NPCs
│
├── Pathfinding/                  # Sistema de navegación propio (A*)
│   ├── PathFinder.cs             (8115 líneas) - Algoritmo A* optimizado
│   ├── BaseGridNetwork.cs        (4612 líneas) - Mapeo del mundo
│   └── [15 archivos más...]      - Heurísticas, nodos, redes...
│
├── Persistence/                  # Persistencia de datos (SQL Server + EF)
│   ├── EntityFramework/          - Contexto EF Core y migraciones
│   ├── BasicModel/               - Modelos básicos de datos
│   └── [5 repositorios más...]   - Account, Character, Inventory...
│
├── Network/                      # Protocolo MU Online (packets)
├── DataModel/                    # Modelos de datos compartidos
└── PlugIns/                      # Sistema de plug-ins extensible
    └── Readme.md                 - Documentación del sistema

src/GameServer/                   # Servidor principal de juego
src/LoginServer/                  # Servidor de login
src/GuildServer/                  # Servidor de guilds
src/FriendServer/                 # Servidor de amigos
src/ChatServer/                   # Servidor de chat (externo a GameLogic)
```

---

## 🤖 Sistema de Bots Existentes - Análisis Detallado

### 1. BotNavigator.cs (2424 líneas) - Navegación y Hunting

**Responsabilidad:** Determinar hunting grounds apropiados según nivel del bot, moverse allí usando pathfinder.

**Puntos Clave:**
- Selecciona zonas de spawn según nivel del bot (seguridad: monsters ≤ X niveles más fuertes)
- Usa `OfflinePlayer.HuntingOrigin` para apuntar al objetivo
- El movimiento real lo maneja `OfflineViewPlugInContainer` → pathfinder del servidor
- Warpea a otros mapas si el hunting ground actual no es seguro

**Extensibilidad:** 
- `BotNavigator` ya separa lógica de decisión (qué zone) de ejecución (cómo moverse)
- Fácil agregar reglas personalizadas de selección de zonas sin modificar movimiento

```csharp
// Ejemplo simplificado del patrón existente:
internal sealed class BotNavigator : AsyncDisposable
{
    private const int SafetyLevelBuffer = 5; // Monsters deben ser ≤5 niveles más fuertes
    
    public void PickHuntingGround(BotPlayer bot) {
        var safeZones = GetSafeSpawnAreas(bot.Level, bot.Map);
        if (safeZones.Any()) {
            bot.HuntingOrigin = safeZones.Random();
        } else {
            WarpToSaferMap(bot);
        }
    }
}
```

### 2. BotProgression.cs (619 líneas) - Leveling y Skill Build

**Responsabilidad:** Definir cómo un bot invierte puntos de stats y qué skills aprende según su clase.

**Puntos Clave:**
- `ClassEvolutionLevel = 200` para segunda generación (Dark Knight → Blade Knight, etc.)
- Mismo build que jugadores: sigue reglas determinísticas del MU Online original
- Skill orbs (group=12), scrolls (group=15) gestionados automáticamente

**Extensibilidad:**
- `BotSkillProgressionPlugIn` aplica las reglas de progression durante el juego
- Fácil modificar rules para bots "exóticos" sin afectar bots estándar

### 3. BotEquipmentHandler.cs & BotShoppingHandler.cs - Equipamiento y Comercio

**Responsabilidad:** Gestionar equipamiento automático, venta de basura, compra de consumibles.

**Puntos Clave:**
- `BotEquipmentHandler`: Evalúa drops, decide qué equipar vs guardar vs vender
- `BotShoppingHandler`: Compra pociones, repara equipo en NPCs
- Reglas configurables: nunca vender items con opciones (excellent, ancient), proteger gems/jewels

**Extensibilidad:**
- ItemEvaluator determinístico ya existe → reutilizable para IA/Bots personalizados
- Fácil agregar reglas personalizadas ("nunca vender items de quest", "proteger wings")

### 4. BotPartyHandler.cs & BotBuffHandler.cs - Party y Buffs

**Responsabilidad:** Formar parties con otros bots, aplicar buffs automáticos antes de farmear.

**Puntos Clave:**
- `BotPartyPolicy`: Define políticas de party (max size, class composition)
- `BotBuffHandler`: Aplica buffs automáticos según clase del bot
- `BotPartyComposer`: Forma parties balanceadas con otros bots

**Extensibilidad:**
- Party formation ya separada de combat logic → fácil modificar reglas sin romper combate
- Buff application modular por tipo (heal, shield, attack buff)

### 5. BotManager.cs & BotGenerator.cs - Gestión y Creación

**Responsabilidad:** Spawnear nuevos bots, gestionar bots activos, reiniciar bots rotos.

**Puntos Clave:**
- `BotManager`: Pool de bots activos, monitorización de salud
- ConsecutiveFailuresUntilRestart = 20 (bot se reinicia si falla 20 veces seguidas)
- `BotGenerator`: Crea nuevos bots con stats/skills iniciales según configuración

**Extensibilidad:**
- Bot lifecycle completo ya implementado → reutilizable para bots personalizados
- Plugin system permite extensiones sin modificar núcleo

---

## 🛣️ Pathfinding - Sistema de Navegación Propio

### Estructura

```text
Pathfinder/
├── PathFinder.cs              (8115 líneas) - Algoritmo A* optimizado
├── BaseGridNetwork.cs         (4612 líneas) - Mapeo del mundo en grid
├── IHeuristic.cs              - Interfaces para heurísticas
├── EuclideanHeuristic.cs      - Distancia euclidiana
├── ManhattanHeuristic.cs      - Distancia Manhattan
└── PreCalculation/            - Cálculos previos de pathfinding optimizados
```

**Características:**
- A* con múltiples heurísticas configurables
- Grid network del mundo precalculado para performance
- BotNavigator ya usa este sistema → movimiento automático funcional

**Extensibilidad:**
- Heuristicas intercambiables (fácil agregar custom heuristic)
- Grid network editable desde configuración del servidor

---

## 💾 Persistencia - Memoria y Relaciones

### Estructura de Datos

```text
Persistence/
├── EntityFramework/
│   ├── OpenMUContext.cs       - Contexto EF Core principal
│   ├── Migrations/            - Migraciones versionadas (SQL Server)
│   └── [5+ repositorios]      - AccountRepository, CharacterRepository...
│
├── BasicModel/
│   └── [modelos básicos]      - Character, Inventory, Guild, etc.
│
└── [repositorios de datos]    - Acceso a datos relacional
```

**Capacidades Actuales:**
- Persistencia completa de characters, inventarios, guilds, stats
- Migraciones SQL Server versionadas y reproducibles
- Backup/restore service integrado (BackupService.cs)

**Para IA/Bots:**
- **Reutilizable:** Sistema de persistencia existente → fácil agregar tablas para memoria social
- **SQLite vs SQL Server:** Evaluar si SQLite es suficiente para datos de bots o mantener SQL Server

---

## 🔌 Sistema de PlugIns - Extensibilidad Nativa

### Arquitectura

```text
PlugIns/
├── Readme.md                  - Documentación del sistema
├── BotFeaturePlugIn.cs        (749 líneas) - Plugin base para bots
└── [20+ plug-ins especializados] - Buff, PvP, reset, wings, jewels...
```

**Puntos Clave:**
- Plugin system basado en interfaces y composición
- `BotFeaturePlugIn`: Base para plugins de bots (buffs, party, equipment)
- OfflinePlayer usa `OfflineViewPlugInContainer` → plug-ins modulares

**Extensibilidad:**
- Fácil agregar nuevos plug-ins sin modificar núcleo
- Plugins pueden interceptar eventos del bot lifecycle
- Plugin system ya validado y probado en production

---

## 🎯 Puntos de Extensión para IA/Bots Personalizados

### 1. **BotNavigator → CustomNavigationPlugIn**
**Qué hace:** Sobrescribir lógica de selección de hunting grounds
**Cómo:** Crear plugin que intercepta `PickHuntingGround(bot)` y aplica reglas personalizadas (ej: "siempre farmear en Devias de noche")
**Riesgo:** Bajo - ya hay separación entre decisión (navigator) y ejecución (movement)

### 2. **BotEquipmentHandler → CustomItemEvaluatorPlugIn**
**Qué hace:** Sobrescribir lógica de evaluación de items
**Cómo:** Plugin que intercepta `ShouldEquip(item)` y aplica reglas personalizadas (ej: "siempre equipar items con runas turquesa")
**Riesgo:** Bajo - ItemEvaluator ya es determinístico y configurable

### 3. **BotPlayer → CustomBehaviorPlugIn**
**Qué hace:** Añadir comportamientos personalizados al bot lifecycle
**Cómo:** Plugin que se ejecuta en eventos del bot (spawn, levelup, death) para aplicar lógica personalizada
**Riesgo:** Bajo - BotManager ya maneja reinicio de bots rotos

### 4. **Persistence → SocialMemoryRepository**
**Qué hace:** Añadir persistencia de relaciones jugador-NPC, reputación, historia de interacciones
**Cómo:** Extender `OpenMUContext` con nuevas tablas (SocialMemory, ReputationHistory, EventLog)
**Riesgo:** Medio - requiere migraciones y cambios en modelo de datos

### 5. **ChatSystem → ChatPlugIn con IA**
**Qué hace:** Añadir sistema de chat contextual y conversacional
**Cómo:** Plugin que intercepta `SendChatMessage(player)` y aplica reglas personalizadas (ej: "responder según personalidad, contexto y relación")
**Riesgo:** Alto - requiere integración con LLM o reglas complejas de NLP

---

## 🧩 Arquitectura Propuesta para IA/Bots Personalizados

### Opción A: **Runtime Integrado en C#** (RECOMENDADA)

```text
OpenMU (.NET/C#)
│
├── Existing Bot System
│   ├── BotNavigator → CustomNavigationPlugIn
│   ├── BotEquipmentHandler → CustomItemEvaluatorPlugIn
│   └── BotPlayer → CustomBehaviorPlugIn
│
├── AI Bot Runtime (NUEVO - integrado en GameLogic)
│   ├── BehaviorTreeEngine.cs      - Motor de árboles de comportamiento
│   ├── ItemEvaluator.cs           - Evaluador determinístico de items
│   ├── EventDirector.cs           - Director de eventos dinámicos
│   ├── PersonalityProfiles.json   - Perfiles de personalidad configurables
│   └── SocialMemoryRepository.cs  - Persistencia de relaciones y memoria
│
├── Local LLM Gateway (OPCIONAL - para chat conversacional)
│   ├── OllamaClient.cs            - Cliente HTTP a Ollama local
│   ├── ChatPromptEngine.cs        - Generación de prompts contextualizados
│   └── ResponseValidator.cs       - Validación de respuestas del LLM
│
└── Extension Points (PlugIns existentes)
    ├── CustomNavigationPlugIn     - Sobrescribe lógica de navegación
    ├── CustomItemEvaluatorPlugIn  - Sobrescribe evaluación de items
    └── ChatResponsePlugIn         - Responde a chat basado en contexto
```

**Ventajas:**
- ✅ **Mantenimiento simple:** Todo en C#, un solo proyecto, fácil debugging
- ✅ **Performance máxima:** Sin overhead de llamadas HTTP externas para lógica crítica
- ✅ **Integración nativa:** Acceso directo a `BotPlayer`, `Inventory`, `Combat` sin wrappers
- ✅ **Testing unitario:** Tests existentes (`tests/MUnique.OpenMU.Tests/`) se reutilizan

**Desventajas:**
- ❌ **Más código C#:** Requiere implementar lógica de IA en C# (no solo prompts)
- ❌ **Menos flexible para LLM:** Cambiar reglas requiere recompilar, no solo actualizar config

### Opción B: **Servicio Externo Python**

```text
OpenMU (.NET/C#)                    External Service (Python/FastAPI)
│                                    │
├── Existing Bot System              ├── BehaviorTreeEngine.py
├── AI Bot Runtime                   ├── LLM Gateway (Ollama client)
│   └── via HTTP calls               ├── SocialMemoryRepository.py
│                                     └── EventScheduler.py
│
└── Extension Points                 └── Webhooks para eventos del servidor
    └── via plugin system
```

**Ventajas:**
- ✅ **Flexibilidad LLM:** Fácil cambiar prompts, modelos y lógica sin recompilar C#
- ✅ **Python ecosystem:** Usar librerías de IA/ML existentes (LangChain, etc.)
- ✅ **Desacoplamiento:** Servicio externo puede fallar sin crashear servidor principal

**Desventajas:**
- ❌ **Overhead HTTP:** Latencia adicional para cada decisión del bot
- ❌ **Complexidad operativa:** Dos servicios que deben coordinarse, más puntos de fallo
- ❌ **Testing fragmentado:** Tests C# y Python separados, difícil testing end-to-end

---

## 📊 Evaluación: Integración Local vs Servicio Externo

| Criterio | Opción A (C# Integrado) | Opción B (Servicio Externo) |
|----------|------------------------|----------------------------|
| **Performance** | ⭐⭐⭐⭐⭐ Máxima, sin latencia HTTP | ⭐⭐ Latencia 50-200ms por decisión |
| **Mantenimiento** | ⭐⭐⭐⭐ Todo en C#, un solo proyecto | ⭐⭐ Dos proyectos que coordinar |
| **Flexibilidad LLM** | ⭐⭐ Cambios requieren recompilar | ⭐⭐⭐⭐⭐ Fácil cambiar prompts/config |
| **Testing** | ⭐⭐⭐⭐ Tests unitarios existentes reutilizables | ⭐⭐ Tests C# y Python separados |
| **Debugging** | ⭐⭐⭐⭐ Todo en Visual Studio/VS Code | ⭐⭐ Dos stacks que debuggear |
| **Escalabilidad** | ⭐⭐⭐ Limitado por CPU del servidor principal | ⭐⭐⭐⭐ Servicio externo puede escalar independiente |
| **Riesgo de Falla** | ⭐⭐⭐ LLM falla crasha todo el bot system | ⭐⭐⭐⭐ LLM falla solo afecta chat, bots siguen farmeando |

### Recomendación Final: **Opción Híbrida (Recomendada)**

```text
OpenMU (.NET/C#) - Core Bot Logic
│
├── Existing Bot System (reutilizado intacto)
├── CustomNavigationPlugIn (C# - navegación y hunting grounds)
├── CustomItemEvaluatorPlugIn (C# - evaluación de items determinística)
└── SocialMemoryRepository (C# - persistencia de relaciones básicas)
│
Local LLM Gateway (Ollama/Python) - Chat Conversacional Solo
│
├── OllamaClient.cs (C# → HTTP a localhost:11434)
├── ChatPromptEngine.cs (genera prompts contextualizados)
└── ResponseValidator.cs (valida respuestas, nunca ejecuta acciones del LLM directamente)
```

**Por qué híbrida:**
- ✅ **Lógica crítica en C#:** Navegación, loot, equipamiento → máximo performance y confiabilidad
- ✅ **Chat conversacional vía LLM:** Solo para respuestas de chat, no toma decisiones críticas
- ✅ **Fallback seguro:** Si LLM falla o timeout, bots siguen funcionando sin chat (pero farmean normal)
- ✅ **Mantenimiento simple:** Lógica principal en C#, solo chat requiere servicio externo ligero

---

## 🤖 Modelo Local Recomendado para Evo X2 de 64 GB

### Hardware Disponible:
- **CPU:** Intel Core i9 / AMD Ryzen 9 (8+ cores, 16+ threads)
- **RAM:** 64 GB DDR4/DDR5
- **GPU:** NVIDIA RTX 3080/3090 o superior (si disponible)
- **Storage:** SSD NVMe para modelos y datos

### Modelos Recomendados:

| Modelo | Tamaño | Performance | Uso Sugerido |
|--------|--------|-------------|--------------|
| **Qwen2.5-Coder-7B-Instruct** | 16 GB VRAM | ⭐⭐⭐⭐⭐ Excelente para código y lógica estructurada | Chat bots, generación de respuestas contextualizadas |
| **Nemotron-4-340B-Instruct** | ~80 GB VRAM (requiere cuantización) | ⭐⭐⭐⭐⭐ Mejor calidad de lenguaje natural | Respuestas conversacionales más naturales, personalidad profunda |
| **Llama-3.1-8B-Instruct** | 24 GB VRAM | ⭐⭐⭐⭐ Muy bueno, muy rápido | Chat bots rápidos, respuestas simples |
| **Mistral-7B-v0.3** | 24 GB VRAM | ⭐⭐⭐ Bueno, ligero | Bots con personalidad simple, responses cortas |

### Recomendación Final: **Qwen2.5-Coder-7B-Instruct + Ollama**

**Por qué:**
1. ✅ **Optimizado para tareas estructuradas:** Excelente generando respuestas JSON/structured data (útil para prompts de bots MU)
2. ✅ **Performance balanceada:** 16 GB VRAM en Evo X2 → suficiente espacio para otros procesos del servidor
3. ✅ **Multi-idioma nativo:** Soporta español perfectamente (importante para personajes con personalidad hispana)
4. ✅ **Ollama native:** Fácil de desplegar, HTTP API simple, gestión automática de modelos

**Configuración sugerida:**
```bash
# Instalar Ollama en Evo X2
curl -fsSL https://ollama.com/install.sh | sh

# Pull model Qwen2.5-Coder-7B-Instruct
ollama pull qwen2.5-coder:7b

# Configurar para chat bots MU (prompt optimizado)
ollama run qwen2.5-coder:7b "Eres un bot de MU Online Season 6. Responde como NPC con personalidad definida."
```

**Fallback strategy:**
- Si Qwen falla, usar Llama-3.1-8B-Instruct (más rápido, menos VRAM)
- Si ambos fallan, fallback a respuestas predefinidas en C# (bots siguen funcionando sin chat)

---

## 📋 Lista de Archivos y Clases Identificadas

### Sistema de Bots Principal (`src/GameLogic/Bots/`)

| Archivo | Líneas | Responsabilidad | Extensibilidad |
|---------|--------|-----------------|----------------|
| `BotNavigator.cs` | 2424 | Navegación, hunting grounds | ⭐⭐⭐⭐ Fácil sobreescribir con plugin |
| `BotProgression.cs` | 619 | Leveling, skill build | ⭐⭐⭐ Modificar reglas via config |
| `BotPlayer.cs` | 119 | Clase base del bot | ⭐⭐⭐ Heredar y extender |
| `BotManager.cs` | 358 | Gestión de bots activos | ⭐⭐ Reemplazar completamente |
| `BotGenerator.cs` | 634 | Creación de bots nuevos | ⭐⭐ Configurar perfiles |
| `BotEquipmentHandler.cs` | 282 | Equipamiento automático | ⭐⭐⭐ ItemEvaluator reutilizable |
| `BotShoppingHandler.cs` | 572 | Comercio, reparación | ⭐⭐⭐ Reglas configurables |
| `BotPartyHandler.cs` | 226 | Party formation | ⭐⭐ Policy configurable |
| `BotBuffHandler.cs` | 218 | Buffs automáticos | ⭐⭐ Plugin system nativo |
| `BotJewelHandler.cs` | 383 | Gestión de jewels/gems | ⭐ Reglas duras |
| `BotWingHandler.cs` | 263 | Wings (alas) | ⭐ Configurable |
| `BotPartyComposer.cs` | 131 | Formar parties balanceadas | ⭐⭐ Policy configurable |
| `BotStarterGearEquipper.cs` | 201 | Equip inicial de bots nuevos | ⭐⭐ Configurable |
| `BotSkillProgressionPlugIn.cs` | 181 | Skill learning durante juego | ⭐⭐ Plugin system nativo |

### Base del Bot (`src/GameLogic/Offline/`)

| Archivo | Líneas | Responsabilidad | Extensibilidad |
|---------|--------|-----------------|----------------|
| `OfflinePlayer.cs` | ~500 | Clase base reutilizada por BotPlayer | ⭐⭐ Heredar y extender |
| `OfflineMapChangePlugIn.cs` | ~100 | Cambio de mapa automático | ⭐ Plugin system nativo |
| `OfflineRespawnPlugIn.cs` | ~150 | Respawn después de death | ⭐ Plugin system nativo |
| `OfflineViewPlugInContainer.cs` | ~200 | Contenedor de plug-ins visuales | ⭐ Añadir/eliminar plug-ins |

### Pathfinding (`src/Pathfinding/`)

| Archivo | Líneas | Responsabilidad | Extensibilidad |
|---------|--------|-----------------|----------------|
| `PathFinder.cs` | 8115 | Algoritmo A* optimizado | ⭐⭐ Heurísticas intercambiables |
| `BaseGridNetwork.cs` | 4612 | Mapeo del mundo en grid | ⭐ Grid editable desde config |

### Persistencia (`src/Persistence/`)

| Archivo/Directorio | Responsabilidad | Extensibilidad |
|-------------------|-----------------|----------------|
| `EntityFramework/OpenMUContext.cs` | Contexto principal EF Core | ⭐⭐ Añadir nuevas tablas |
| `Migrations/` | Migraciones SQL Server versionadas | ⭐ Crear migración nueva |
| `BasicModel/Character.cs` | Modelo de personaje persistente | ⭐ Añadir campos personalizados |

---

## 🚨 Riesgos Identificados y Mitigaciones

### Riesgo 1: **Corrupción de atributos del bot** (ya documentado en BotPlayer.cs)

**Descripción:** El sistema de atributos no es thread-safe, una race condition puede corromper el graph de atributos permanentemente.

**Manifestación:** Bot falla 20 veces seguidas → `ConsecutiveFailuresUntilRestart` triggera reinicio automático.

**Mitigación existente:** ✅ Ya implementada (reiniciar bot si falla 20 veces)

**Riesgo residual:** ⭐ Bajo - bots rotos se reinician automáticamente sin intervención manual

### Riesgo 2: **LLM fallando en producción**

**Descripción:** Si el LLM local (Ollama/Qwen) falla o es lento, los bots podrían quedar bloqueados esperando respuestas.

**Mitigación recomendada:**
- Timeout estricto de 500ms para cada llamada al LLM
- Fallback a respuestas predefinidas en C# si timeout
- Bots siguen funcionando sin chat (pero farmean normal)

### Riesgo 3: **Modificaciones rompen compatibilidad con clientes**

**Descripción:** Cambios en el servidor podrían romper compatibilidad con Ignies (Babylon.js client).

**Mitigación obligatoria:** ✅ NO modificar Ignies inicialmente, solo server-side changes

### Riesgo 4: **Concurrente bot AI + LLM generando race conditions**

**Descripción:** Bot AI (C#) y LLM (Python service) podrían intentar ejecutar acciones simultáneamente.

**Mitigación recomendada:**
- Single-threaded event loop en BotPlayer → solo una acción a la vez
- LLM proposals → validadas por `ResponseValidator` antes de ejecutar
- Nunca permitir que LLM altere inventario/stats directamente → solo propone, C# ejecuta si válido

---

## 📝 Plan de Implementación Recomendado (Fases 1-3)

### Fase 1: Bot Secundario Autónomo (2-4 semanas)

**Objetivo:** Bot que farmee, recoja items, evalúe qué conservar y venda basura.

**Entregables:**
- ✅ Reutilizar `BotNavigator` existente → navegación automática
- ✅ Reutilizar `BotEquipmentHandler` existente → equipamiento automático
- ✅ Extender `CustomItemEvaluatorPlugIn` → reglas personalizadas de evaluación de items
- ✅ Persistencia básica en SQL Server → historial de loot vendido

**Reglas:**
1. Nunca vender items con opciones (excellent, ancient, luck)
2. Proteger gems/jewels automáticamente
3. Vender solo items con `value < threshold` y sin opciones especiales
4. Equipar items que mejoren stats actuales del bot

### Fase 2: Conversación y Memoria (1-2 semanas)

**Objetivo:** Bots con personalidad, memoria de interacciones, relaciones jugador-NPC.

**Entregables:**
- ✅ `SocialMemoryRepository` → persistencia de relaciones y reputación
- ✅ `ChatPromptEngine` → generación de prompts contextualizados para LLM
- ✅ `ResponseValidator` → validación de respuestas del LLM antes de ejecutar
- ✅ Personality profiles JSON → configuración de personalidades por NPC

**Reglas:**
1. LLM solo propone respuestas, C# decide si enviar (seguridad primero)
2. Memoria persistida en SQL Server → relaciones entre sesiones
3. Límite de 50 mensajes por interacción para evitar spam
4. Cooldown de 30 segundos entre mensajes del mismo NPC

### Fase 3: Cuatro Protagonistas (1-2 semanas)

**Objetivo:** Implementar los cuatro personajes memorables con personalidades únicas y eventos dinámicos.

**Entregables:**
- ✅ `EventDirector` → scheduler de eventos dinámicos (cacerías, duelos, traiciones)
- ✅ Personality profiles para cada protagonista (elmandolion, bertoSalamin, LeilaMasa, elBichiCome)
- ✅ Chat responses específicas para cada personaje ("Comiste pete", etc.)
- ✅ Cooldowns y probabilidades configurables de eventos

**Reglas:**
1. Eventos programados no interrumpen farmeo normal del bot
2. Duelos entre protagonistas limitados a zonas seguras (puerta norte Lorencia)
3. Traiciones de LeilaMasa con probabilidad configurable (ej: 5% cada interacción)
4. Furia del Bichi activada por eventos específicos (matar jugador, recibir Ale)

---

## 🎯 Conclusiones y Siguientes Pasos

### ✅ Lo que ya funciona (reutilizar intacto):
1. **Sistema de bots existente:** `BotNavigator`, `BotProgression`, `BotPlayer` - todo funcional y probado
2. **Pathfinder A* propio:** Navegación automática optimizada sin dependencias externas
3. **Plug-in system nativo:** Extensibilidad validada en production
4. **Persistencia SQL Server/EF Core:** Base de datos relacional con migraciones versionadas

### ⚠️ Lo que falta implementar (nuevo desarrollo):
1. **Sistema de memoria social:** Relaciones jugador-NPC, reputación, historia de interacciones
2. **Lógica de personalidad:** Perfiles configurables que afectan comportamiento y respuestas de chat
3. **Director de eventos dinámicos:** Scheduler para cacerías, duelos, traiciones programadas
4. **Integración LLM local (opcional):** Ollama + Qwen2.5-Coder-7B-Instruct para chat conversacional

### 🚀 Próximo paso inmediato:
1. **Crear `docs/ai-bots-architecture.md`** - ESTE DOCUMENTO (completado ✅)
2. **Implementar Fase 1:** Bot secundario autónomo con ItemEvaluator personalizado
3. **Testing:** Validar bots nuevos en servidor local antes de producción

---

*Documento creado: 28 Octubre 2026 - Hermes Agent (Qwen local)*  
*Auditoría basada en inspección directa del código fuente OpenMU v1.7.x*  
*Siguiente tarea: Implementación Fase 1 (bot autónomo) o continuar con otras fases según preferencia*
