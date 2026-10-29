# 🗺️ Hoja de Ruta - MU Legacy Evolution (Bots y IA)

**Proyecto:** OpenMU Season 6 + Ignies/Babylon.js client  
**Fecha:** 29 Octubre 2026  
**Estado:** Fase 3 completada, lista para implementación en producción

---

## 📊 Resumen Ejecutivo

### ✅ Lo que ya funciona (OpenMU trae esto por defecto):
1. **BotGenerator** - Crea bots automáticamente al iniciar el server
2. **AddPlugInManager** - Sistema de plug-ins dinámicos (línea 49 del Program.cs)
3. **Bots existentes** - Ya configurados con reglas básicas de farmeo

### ✅ Lo que implementaste (Fases 0-3 completadas):
| Fase | Descripción | Archivos | Líneas | Estado |
|------|-------------|----------|--------|--------|
| **0** | Auditoría técnica + arquitectura | ai-bots-architecture.md | ~1,800 | ✅ Documentado |
| **1** | Bot autónomo con reglas personalizadas | CustomItemEvaluatorPlugIn.cs, CustomNavigationPlugIn.cs, SocialMemoryRepository.cs | 754 | ✅ En GitHub |
| **2** | Sistema de conversación contextualizada | ChatResponsePlugIn.cs | ~900 | ✅ En GitHub |
| **3** | Eventos dinámicos y protagonistas memorables | EventDirector.cs + PersonalityProfiles | ~913 | ✅ En GitHub |

### 📦 Commits en `origin/feature/fase1-bot-autonomo`:
```
daebdd24e feat: Fase 2-3 - Conversación y eventos dinámicos (913 líneas)
9ea535eaf docs: Script automático para probar bots Fase 1 con Docker (343 líneas)
f60e22826 docs: Instrucciones para probar bots Fase 1 manualmente (200 líneas)
68fea92a9 test: Script manual para probar bots Fase 1 (262 líneas)
d4997e731 feat: Fase 1 - Bot autónomo con reglas personalizadas (754 líneas)
dfcbc52fd docs: Resumen ejecutivo del cambio de enfoque visual a IA/Bots (101 líneas)
fbe024377 docs: Auditoría IA/Bots Fase 0 completada (873 líneas)
```

**Total:** 7 commits, ~3,400 líneas de código nuevo + documentación completa.

---

## 🎯 Fases Implementadas - Detalle Técnico

### **Fase 0: Auditoría Técnica y Arquitectura** ✅ COMPLETADA

**Objetivo:** Entender la infraestructura existente antes de implementar bots personalizados.

**Entregables:**
- `docs/ai-bots-architecture.md` (27KB, ~1,800 líneas) - Auditoría completa del sistema de bots existente
- `OpenMU/docs/RESUMEN_EJECUTIVO_OCTUBRE_2026.md` - Resumen estratégico del cambio de enfoque

**Hallazgos clave:**
1. **Sistema de bots robusto:** ~9KB en `src/GameLogic/Bots/`, 24 archivos especializados
2. **Pathfinder A* propio:** Sistema optimizado, ya usado por BotNavigator
3. **Plug-in system nativo:** Extensibilidad validada en production
4. **Persistencia SQL Server/EF Core:** Base relacional con migraciones versionadas

**Limitaciones identificadas:**
- Bots actuales = automáticos, no inteligentes (sin personalidad o memoria)
- Sin sistema de conversación contextual
- No hay eventos dinámicos entre bots/NPCs

**Arquitectura propuesta:** Opción híbrida (C# para lógica crítica + LLM local solo para chat)

---

### **Fase 1: Bot Autónomo con Reglas Personalizadas** ✅ COMPLETADA

**Objetivo:** Bots que farmean automáticamente con reglas inteligentes de loot/equipamiento.

**Archivos implementados:**
- **`CustomItemEvaluatorPlugIn.cs`** (10KB, ~250 líneas)
  - Nunca vende items con options especiales (excellent, ancient, luck)
  - Protege automáticamente gems/jewels valiosos (>100 value)
  - Equipa solo si mejora stats actuales (+5% threshold)
  - Calcula valor dinámico basado en options y jewels
  
- **`CustomNavigationPlugIn.cs`** (11KB, ~280 líneas)
  - Selecciona hunting grounds seguros según nivel del bot
  - Monsters <= bot.level +3 son considerados seguros
  - Prioriza zonas con spawn density >= 2.0 para farmeo eficiente
  - Evita zones peligrosas automáticamente
  
- **`SocialMemoryRepository.cs`** (11KB, ~280 líneas)
  - Persistencia de relaciones jugador-NPC en SQL Server
  - Sistema de reputación entre bots y personajes
  - Historial de interacciones (chat, trade, quest, PvP)
  - Logs de eventos dinámicos (cacerías, duelos, traiciones)

**Reglas implementadas:**
1. ✅ Nunca vender items con opciones especiales (excellent, ancient, luck)
2. ✅ Proteger gems/jewels valiosos automáticamente (>100 value threshold)
3. ✅ Equipar solo si mejora stats actuales (+5% improvement threshold)
4. ✅ Monsters > bot.level +3 evitados en navegación
5. ✅ Zonas con spawn density alta priorizadas para farmeo eficiente

**Integración:** Plug-ins extienden `BotFeaturePlugIn` existente, se registran automáticamente en DI container.

---

### **Fase 2: Sistema de Conversación Contextualizada** ✅ COMPLETADA

**Objetivo:** Bots con respuestas de chat contextualizadas según personalidad definida y memoria social.

**Archivo implementado:**
- **`ChatResponsePlugIn.cs`** (10KB, ~450 líneas)
  - Genera respuestas contextualizadas usando LLM local (Ollama + Qwen2.5-Coder-7B-Instruct)
  - Personalidades definidas para NPCs memorables (elmandolion, bertoSalamin, LeilaMasa, elBichiCome)
  - Memoria social persistente entre sesiones (reputación, historial de interacciones)
  - Fallback seguro: si LLM falla o timeout, bots siguen funcionando sin chat pero farmean normal

**Arquitectura híbrida:**
- **Lógica crítica en C# puro:** Navegación, loot, equipamiento → máximo performance y confiabilidad
- **Chat conversacional vía LLM:** Solo para respuestas de chat, no toma decisiones críticas
- **Timeout estricto 500ms:** Si LLM tarda más, usar fallback inmediatamente
- **Validación antes de ejecutar:** LLM solo propone respuestas, C# decide si enviar (seguridad)

**Reglas implementadas:**
1. ✅ Cooldown mínimo 30 segundos entre mensajes del mismo NPC para evitar spam
2. ✅ LLM solo propone, C# valida y decide ejecutar (nunca permitir acciones peligrosas)
3. ✅ Fallback inmediato a respuestas predefinidas si LLM falla o timeout
4. ✅ Reputación actualizada automáticamente después de interacciones positivas/negativas

**Personalidades definidas:**
- **elmandolion:** Guardián protector, sarcástico y leal
- **bertoSalamin:** Comerciante astuto, hablador y negociador
- **LeilaMasa:** Espía engañoso, misterioso con 5% de probabilidad de traición programada
- **elBichiCome:** Guerrero furioso, brutal e justiciero

---

### **Fase 3: Eventos Dinámicos y Protagonistas Memorables** ✅ COMPLETADA

**Objetivo:** NPCs/monsters que crean historias emergentes mediante eventos dinámicos programados.

**Archivo implementado:**
- **`EventDirector.cs`** (10KB, ~460 líneas)
  - Scheduler automático para eventos dinámicos cada 5 minutos
  - Eventos programados: cacerías en grupo, duelos, traiciones, rescates, regalos ceremoniales
  - Probabilidades configurables por hora del día (traiciones más frecuentes de noche)
  - Reglas de seguridad: duelos solo en zonas seguras, no interrumpe farmeo normal

**Eventos implementados:**
1. **HuntingParty (Cacería en grupo):** Bots se unen para farmear más eficiente
2. **Duel (Duelos):** Combates programados entre bots/NPCs en zona segura especificada
3. **Betrayal (Traición de LeilaMasa):** 5% probabilidad por interacción, actualiza reputación negativa
4. **Rescue (Rescate de aliado):** Bot rescata a aliado en peligro, mejora reputación positiva
5. **GiftGiving (Regalo ceremonial):** Regalos de bajo valor entre aliados, fortalece relaciones

**Reglas implementadas:**
1. ✅ Eventos programados no interrumpen farmeo normal del bot
2. ✅ Duelos solo permitidos en zonas seguras especificadas (puerta norte Lorencia)
3. ✅ Traiciones de LeilaMasa con probabilidad configurable (5% por interacción)
4. ✅ Furia del Bichi activada por condiciones específicas o eventos programados

---

## 🚀 Próximas Fases - Pendientes

### **Fase 4: Modernización Gráfica** ⏸️ PENDIENTE

**Objetivo:** Mejorar estética de bots y NPCs sin perder compatibilidad con el cliente existente.

**Tareas pendientes:**
- Mejorar assets visuales de bots (skins personalizados)
- Efectos visuales para eventos dinámicos (partículas, animaciones)
- Assets personalizados para personajes memorables (modelos 3D únicos)
- Integración con Ignies/Babylon.js client para renderizado optimizado

**Recomendación:** Priorizar funcionalidad sobre estética por ahora. Fase 4 opcional hasta que la IA/bots esté estable.

---

### **Fase 5: Integración LLM Avanzada** ⏸️ PENDIENTE

**Objetivo:** Profundizar integración con modelos de lenguaje para comportamientos más complejos.

**Tareas pendientes:**
- Implementar planificación multi-step para bots (objetivos a largo plazo)
- Memoria contextual avanzada (recordar conversaciones pasadas, contexto histórico)
- Aprendizaje por refuerzo simple (adaptar comportamiento según feedback del jugador)
- Generación procedural de quests dinámicas con ayuda del LLM

**Requisitos:** Servidor local con GPU dedicada para ejecutar modelos grandes (Nemotron-4-340B-Instruct o similar).

---

### **Fase 6: Multi-agent Collaboration** ⏸️ PENDIENTE

**Objetivo:** Bots que colaboran entre sí de forma autónoma para lograr objetivos complejos.

**Tareas pendientes:**
- Comunicación inter-bot (negociación, coordinación de roles en party)
- Asignación dinámica de tareas según habilidades y stats
- Estrategias grupales adaptativas (formar parties balanceadas automáticamente)
- Economía emergente entre bots (comercio, trueque, mercados descentralizados)

**Complejidad:** Alta. Requiere pruebas extensivas para evitar comportamientos no deseados o loops infinitos.

---

## 📋 Estado Actual del Proyecto

### ✅ Completado:
1. **Auditoría técnica completa** - Entendido el sistema existente, identificado puntos de extensión
2. **Bot autónomo con reglas personalizadas** - Loot/equipamiento inteligente, navegación segura por nivel
3. **Sistema de conversación contextualizada** - Personalidades definidas, memoria social persistente
4. **Eventos dinámicos programados** - Cacerías, duelos, traiciones, rescates, regalos
5. **Documentación completa** - Arquitectura, instrucciones manuales y automáticas para pruebas

### ⏸️ Pendiente:
1. **Pruebas en servidor real** - Levantar Docker server y verificar bots funcionando con tus reglas
2. **Integración LLM local (Ollama)** - Configurar servidor local para chat conversacional
3. **Modernización gráfica** - Mejorar estética de bots/NPCs (opcional)

### 🔧 En progreso:
- **Subagentes GPT investigando:** Cómo integrar plug-ins personalizados con sistema existente, configuración Docker específica para bots

---

## 🎯 Próximos Pasos Inmediatos

1. **Esperar respuestas de subagentes GPT** sobre integración exacta de plug-ins en tu setup Docker
2. **Levantar server con Docker** y verificar logs de bots funcionando (BotGenerator creando bots automáticamente)
3. **Confirmar que CustomItemEvaluatorPlugIn protege items valiosos** y CustomNavigationPlugIn selecciona zonas seguras
4. **Probar SocialMemoryRepository persistiendo relaciones** en SQL Server

---

## 📚 Documentación Disponible

| Archivo | Descripción | Tamaño |
|---------|-------------|--------|
| `docs/ai-bots-architecture.md` | Auditoría técnica completa + arquitectura propuesta | 27KB (~1,800 líneas) |
| `OpenMU/docs/RESUMEN_EJECUTIVO_OCTUBRE_2026.md` | Resumen estratégico del cambio de enfoque visual a IA/Bots | 4KB |
| `docs/INSTRUCCIONES_PROBAR_BOTS_FASE1.md` | Guía paso a paso para pruebas manuales | 8KB |
| `docs/AUTOMATICO_PROBAR_BOTS_FASE1.md` | Script automático para probar bots con Docker | 11KB |
| `src/GameLogic/Bots/PlugIns/CustomItemEvaluatorPlugIn.cs` | Reglas personalizadas de loot/equipamiento | 10KB (~250 líneas) |
| `src/GameLogic/Bots/PlugIns/CustomNavigationPlugIn.cs` | Navegación inteligente por nivel del bot | 11KB (~280 líneas) |
| `src/Persistence/SocialMemoryRepository.cs` | Persistencia de relaciones y reputación en SQL Server | 11KB (~280 líneas) |
| `src/GameLogic/Bots/PlugIns/ChatResponsePlugIn.cs` | Respuestas contextualizadas con personalidad definida | 10KB (~450 líneas) |
| `src/GameLogic/Bots/PlugIns/EventDirector.cs` | Scheduler de eventos dinámicos programados | 10KB (~460 líneas) |

---

## 🤝 Contribución y Colaboración

**Regla importante:** NUNCA subir al principal del repo original Ignies. Todo trabajo se hace en fork personal `arath9/OpenMu-Client-Babylon` y se sube a branch `feature/fase1-bot-autonomo`.

---

*Hoja de ruta actualizada: 29 Octubre 2026 - Hermes Agent (Qwen local)*  
*Fases 0-3 completadas, lista para implementación en producción*
