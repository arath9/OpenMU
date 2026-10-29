# 🎮 MU Online AI Bots — Roadmap para Hermes

## Estado Actual (Octubre 2026)

### ✅ Completado - Infraestructura Técnica (Fases 1-4 del proyecto anterior)
| Fase | Logro Principal | Estado | Utilidad para IA/Bots |
|------|----------------|--------|----------------------|
| **1: Auditoría** | Rig MU documentado (61 slots), sockets mapeados | ✅ | Animaciones compatibles, puntos de extensión conocidos |
| **2: Pipeline Assets** | Visual overrides funcionando con fallback automático | ✅ | Reemplazar skins/armaduras dinámicamente |
| **3: Personaje Veyra** | 5 partes modulares, rig compatible, tests passing | ✅ | Modelo base para NPCs personalizados |
| **4: Equipamiento** | Sistema de mejoras +0/+10/+15, efectos Babylon PBR | ⚠️ | Técnicamente correcto pero sin personalidad artística |

### 📊 Aprendizajes Críticos
- **Rig MU canónico:** 61 slots (skin_player + bone_0..59), no "62 huesos" como especulábamos inicialmente
- **Sockets correctos:** Weapon1→34, Weapon2→43, Wings→48 (Babylon)
- **Assets visuales actuales:** Técnicamente válidos pero sin alma artística - procedurales sin variation
- **61+ tests aprobados** en múltiples archivos validando infraestructura técnica

### ⚠️ Limitación Reconocida
Los assets generados son **funcionales pero no memorables**. No transforman la experiencia visual como se esperaba. Priorizamos ahora **IA/bots con personalidad** sobre calidad artística visual.

---

## Objetivo
Mantener nuestro servidor privado de **MU Online Season 6** para jugar localmente con amigos. No se monetizará.

**Estado actual:** OpenMU + Ignies (Babylon.js) ya funcionan, con personajes, combate y bots existentes.

**Cambio de prioridades:** pausamos la modernización gráfica del cliente. El nuevo objetivo es crear bots autónomos con IA, personalidades, memoria, economía y eventos dinámicos.

## 🛠️ Stack y aplicaciones

- **OpenMU (.NET/C#):** servidor, combate, inventarios, economía y lógica del mundo.
- **Ignies (Babylon.js/TypeScript):** cliente existente. Evitar modificaciones salvo necesidad.
- **Hermes Agent:** agente coordinador del desarrollo, auditorías, código y pruebas.
- **Ollama o llama.cpp:** inferencia local de LLMs en la Evo X2 de 64 GB.
- **Modelo local:** evaluar Qwen y Nemotron según calidad, latencia y compatibilidad.
- **Python o C#:** servicio de orquestación de bots. Elegir según la arquitectura existente.
- **SQLite:** memoria persistente, relaciones, eventos y reputación. Evaluar reutilizar la persistencia actual.
- **Behavior Trees / FSM:** decisiones de navegación, farming, combate y comercio.
- **Blender (opcional):** armaduras y apariencias especiales para protagonistas.
- **Discord:** seguimiento del proyecto, logs y notificaciones opcionales.
- **Docker Compose (opcional):** levantar servicios adicionales reproduciblemente.

**Principio:** IA híbrida. Lógica determinística para combate, movimiento, loot y economía; LLM para conversaciones, personalidad y decisiones sociales.

No ejecutar un LLM por cada acción ni cargar un modelo diferente para cada bot.

## 📍 Fase 0 — Auditoría de OpenMU
**Estimación: 2–4 días**

Inspeccionar el código real y documentar:

- Implementación actual de bots.
- Navegación, combate, farming y experiencia.
- Inventario, equipamiento, loot y tiendas.
- Chat, party y PvP.
- Extensiones disponibles y persistencia.
- Cómo ejecutar acciones sin romper las reglas del servidor.

**Entregable:** `docs/ai-bots-architecture.md` con clases, archivos, puntos de extensión y propuesta de arquitectura.

No reescribir los bots existentes.

## 🤖 Fase 1 — Bots secundarios autónomos
**Estimación: 1–3 semanas**

Implementar un bot completo que pueda:

1. Buscar monstruos y farmear.
2. Recoger objetos.
3. Evaluar si un ítem sirve para su clase.
4. Conservar ítems valiosos, excellent, raros o útiles.
5. Equipar mejoras.
6. Vender basura a NPCs.
7. Comprar consumibles y reparar.
8. Subir de nivel y regresar a farmear.

Crear un `ItemEvaluator` determinístico y configurable. Nunca vender objetos protegidos.

Luego reutilizar el sistema para múltiples bots con diferentes clases, niveles y comportamientos.

**Entregable:** bots secundarios que progresan y participan en la economía sin intervención manual.

## 🧠 Fase 2 — Conversación y memoria
**Estimación: 1–2 semanas**

Integrar un único LLM local compartido.

Cada bot tendrá:
- Personalidad y estilo de conversación.
- Memoria de interacciones.
- Relaciones con jugadores y otros bots.
- Objetivos y estado emocional.
- Contexto del mundo y eventos recientes.

El LLM propondrá acciones estructuradas como `request_zen`, `offer_party`, `challenge_player` o `defend_player`.

El runtime validará y ejecutará las acciones permitidas.

Agregar límites de frecuencia, longitud de respuestas, contexto y timeout. Si el LLM falla, los bots siguen funcionando.

**Entregable:** conversación natural dentro del chat de MU y memoria persistente entre sesiones.

## 👑 Fase 3 — Cuatro protagonistas
**Estimación: 1–2 semanas**

### ⚔️ elmandolion — Blade Master oscuro
- Full stats, extremadamente poderoso.
- Reside en Lorencia.
- Personalidad malvada, arrogante y provocadora.
- Cada 2 horas inicia una cacería de jugadores.
- Puede recorrer distintas zonas permitidas del mapa.
- Cuando mata a un jugador dice exactamente: **"Comiste pete"**.
- Puede tener una armadura final personalizada.
- Rival permanente de bertoSalamin.

### 🛡️ bertoSalamin — Blade Master protector
- Full stats y muy poderoso.
- Protege jugadores durante las cacerías.
- Rival de elmandolion.
- Fuera de eventos se enfrenta con él en la puerta norte de Lorencia.
- Ambos discuten y se provocan por chat.
- Puede tener armadura personalizada.

### 🏹 LeilaMasa — Elf impredecible
- Aliada habitual de bertoSalamin.
- Da buffs y ayuda a jugadores.
- Ocasionalmente exige Zen.
- Puede robar spots de farming o atacar a jugadores que se nieguen.
- Sus traiciones deben tener probabilidades y cooldowns configurables.

### 🍺 elBichiCome — Dark Lord errante
- Vagabundo que recorre mapas pidiendo Zen y Ale.
- Fue maestro de elmandolion y bertoSalamin.
- Normalmente parece inofensivo.
- Si recibe Ale, puede ofrecer party y ayudar a farmear.
- Es extremadamente poderoso.
- Puede revelar su verdadero poder cuando un protagonista mata a un jugador o un evento lo enfurece.
- Sus intervenciones deben ser raras e impredecibles.

**Entregable:** cuatro personajes persistentes con rutinas, personalidades, relaciones y comportamientos diferenciados.

## 🔥 Fase 4 — Director de eventos
**Estimación: 2–4 semanas**

Implementar un `EventDirector` independiente del LLM.

Eventos:

1. **Cacería:** elmandolion persigue jugadores cada 2 horas; bertoSalamin puede intervenir.
2. **Duelo de Lorencia:** enfrentamientos y discusiones entre los dos BM en la puerta norte.
3. **Chantaje de LeilaMasa:** exige Zen, roba spots o traiciona.
4. **Furia del Bichi:** interviene inesperadamente contra otro protagonista.
5. **Eventos emergentes:** alianzas, venganzas, rescates y encuentros aleatorios.

Agregar:
- Scheduler.
- Probabilidades configurables.
- Cooldowns.
- Estados de evento.
- Anuncios por chat.
- Protección de zonas seguras.
- Recuperación después de reinicios.
- Límites para evitar acoso continuo a jugadores.

**Entregable:** eventos dinámicos que se desarrollan incluso sin intervención administrativa.

## 🌍 Fase 5 — Mundo vivo
**Estimación: continua**

- Memoria social persistente.
- Reputación individual por jugador.
- Bots que recuerdan favores, ataques y regalos.
- Rivalidades que evolucionan.
- Bots secundarios que comercian y progresan.
- Conversaciones espontáneas entre NPCs.
- Estadísticas de eventos y actividad.
- Apariencias especiales opcionales.

Ejemplo: LeilaMasa recuerda que un jugador no le pagó Zen; elmandolion recuerda una derrota; elBichiCome reconoce a quien le regaló Ale.

## 🧩 Arquitectura objetivo

```text
OpenMU (.NET)
    |
    +-- Existing Bot System
    |      |
    |      +-- Farming / Combat / Loot
    |      +-- Inventory / Trading
    |      +-- Movement / Party
    |
    +-- AI Bot Runtime
           |
           +-- FSM / Behavior Trees
           +-- ItemEvaluator
           +-- EventDirector
           +-- Personality Profiles
           +-- Memory / Reputation
           |
           +-- Local LLM Gateway
                  |
                  +-- Ollama / llama.cpp
                  +-- Qwen / Nemotron
```

La arquitectura es orientativa. Ajustarla según la auditoría real del código.

## ⚙️ Reglas técnicas

- Reutilizar OpenMU y sus bots existentes.
- Mantener el servidor funcional durante todo el desarrollo.
- No modificar Ignies inicialmente.
- No permitir que el LLM altere inventarios, stats o economía directamente.
- Validar todas las acciones mediante reglas del servidor.
- Limitar consumo de CPU, GPU y memoria.
- Mantener logs y métricas.
- Permitir desactivar la IA sin detener el servidor.
- Separar configuración, personalidad y lógica.
- Priorizar comportamiento divertido sobre simulación compleja.
- Mantener el proyecto privado y respetar licencias de código y assets.

## 🚀 Primera tarea para Hermes

**Ejecutá únicamente la Fase 0.**

1. Localizá el código de OpenMU y sus bots actuales.
2. Identificá las clases responsables de navegación, combate, loot, inventario, chat, party y PvP.
3. Verificá qué funcionalidades ya existen y cuáles faltan.
4. Proponé la arquitectura mínima para implementar bots económicos y los cuatro protagonistas.
5. Evaluá si conviene integrar el runtime directamente en C# o mediante un servicio externo.
6. Evaluá el modelo local y runtime más adecuados para la Evo X2 de 64 GB.
7. Documentá hallazgos, riesgos, dependencias y archivos a modificar.
8. Presentá el plan de implementación antes de hacer cambios importantes.

**Prioridad inmediata:** un bot que farmee, recoja ítems, identifique cuáles conservar y venda el resto.

**Objetivo final:** que nuestro servidor de MU se sienta como un mundo vivo, con personajes memorables, historias emergentes y bots autónomos, sin perder la jugabilidad clásica.

---

## 🚀 Próximos Pasos Inmediatos (Cambio de Enfoque)

### ¿Por qué cambiar ahora?
1. **Infraestructura técnica validada:** Rig MU, sockets, tests funcionando perfectamente
2. **Más valor jugable inmediato:** NPCs/monsters inteligentes mejoran gameplay AHORA
3. **Menos dependencia de arte externo:** No necesita artista 3D freelance
4. **Personalidad > Estética:** Experiencia del juego mejora con comportamientos únicos

### Primera Tarea para Hermes (Fase 0 - IA/Bots)
**Ejecutar ÚNICAMENTE la Fase 0:**

1. ✅ Localizar el código de OpenMU y sus bots actuales
2. ✅ Identificar las clases responsables de navegación, combate, loot, inventario, chat, party y PvP
3. ✅ Verificar qué funcionalidades ya existen y cuáles faltan
4. ✅ Proponer la arquitectura mínima para implementar bots económicos y los cuatro protagonistas
5. ✅ Evaluar si conviene integrar el runtime directamente en C# o mediante un servicio externo
6. ✅ Evaluar el modelo local y runtime más adecuados para la Evo X2 de 64 GB
7. ✅ Documentar hallazgos, riesgos, dependencias y archivos a modificar
8. ✅ Presentar el plan de implementación antes de hacer cambios importantes

### Entregable Esperado
**`docs/ai-bots-architecture.md`** con:
- Clases y archivos identificados
- Puntos de extensión para IA/Bots
- Propuesta de arquitectura mínima viable
- Evaluación de integración C# vs servicio externo
- Recomendación del modelo LLM local (Qwen, Nemotron, etc.)

### Reglas durante la Fase 0
- **NO reescribir** los bots existentes
- **NO modificar Ignies** inicialmente (cliente Babylon.js)
- **Priorizar** comportamiento divertido sobre simulación compleja
- **Mantener** el servidor funcional durante todo el desarrollo

---

*Hoja de ruta actualizada: Octubre 2026 - Cambio de enfoque hacia IA/Bots y personalidades*
*Documento base original: OpenMU AI Bots Roadmap v1.0*