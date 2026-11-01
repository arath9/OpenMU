# Diálogos Yarn de protagonistas

Integración de servidor .NET con YarnSpinner y YarnSpinner.Compiler **3.1.0**. No depende de Unity ni ejecuta el pseudocódigo Unity/Ink del plan de referencia.

## Uso desde el juego

Con los protagonistas habilitados y cerca del jugador (12 casillas, mismo mapa):

1. Escribir `mandolion hola`, `bertosala hola`, `leilamasa hola` o `bichicome hola`. También se acepta el alias de personaje configurado y la identidad canónica.
2. El bot envía al jugador su saludo y opciones numeradas mediante `IChatViewPlugIn` (chat normal, entrega individual).
3. Elegir con `mandolion 1`, por ejemplo, o con `mandolion Dime un secreto`.
4. `mandolion cancelar` cierra la conversación. No elige ninguna recompensa por defecto.

Un `hola` sin destinatario puede saludar a varios bots cercanos, pero las elecciones deben indicar el bot para no seleccionar varias conversaciones a la vez. Cada pareja bot/jugador tiene un runtime y variables independientes. Una sesión inactiva caduca a los dos minutos. El inicio respeta `InteractionCooldown`, separado del cooldown de las frases ambientales. No se modifica la configuración de activación existente ni se habilitan protagonistas automáticamente.

## Recursos y ejecución

- Archivos: `src/GameLogic/Bots/Yarn/{mandolion,bertosala,leilamasa,bichicome}.yarn`.
- Destino de build y publish: `Bots/Yarn/` relativo a `AppContext.BaseDirectory`; no depende del directorio de trabajo.
- `YarnBotManager` permite un directorio explícito para pruebas, resuelve exclusivamente los nombres permitidos y conserva compilaciones por instancia/mundo. Cambiar archivos requiere reiniciar el mundo/servidor para invalidar la caché, incluidos fallos de compilación.
- `YarnBotDialogue` resuelve IDs de líneas mediante StringTable, expande sustituciones y convierte índices visibles a IDs reales de opciones disponibles.
- `$playerName` está declarado en los scripts y se proporciona por sesión.
- La ejecución permanece en `PendingBotActions`, sin `async void`, tareas de elección con delay ni continuación automática de opciones.
- El prototipo bajo `Bots/YarnSpinner/` queda excluido de la compilación GameLogic, incluidos sus `obj` anidados. No se elimina el experimento.

## Comandos permitidos

```yarn
<<GiveItem "Apple" 1>>
<<GiveItem "14:0" 1>>
<<StartQuest "1:0">>
<<ShowLore lorencia>>
```

Los comandos provienen únicamente del programa Yarn del servidor. Escribir comandos Yarn en el chat no los ejecuta. Se validan nombre, argumentos, mundo, distancia y estado del jugador; los comandos desconocidos/malformados abortan la sesión con log.

### GiveItem

Transfiere objetos **existentes del inventario del bot**, sin crear recompensas. Identifica por nombre neutral/invariante o `grupo:número`; acepta 1–32 unidades, excluye objetos equipados, no comerciables y ligados al personaje. Transfiere objetos individuales o pilas completas que sumen la cantidad solicitada; **no divide pilas**. Si no puede reunir esa cantidad exacta, rechaza la entrega. No cambia durabilidad de armas como si fuera cantidad.

Usa `CreateNewTradeContext`, traspaso de ownership de persistencia como el comercio normal y rollback completo del inventario si falta espacio o falla la escritura. Una segunda solicitud sin stock no duplica objetos. La verificación automatizada es con el proveedor InMemory; no constituye validación transaccional contra PostgreSQL ni despliegue del servidor.

El bot ya posee su lock de persistencia en la cola. La adquisición del lock del receptor está acotada a 100 ms para no introducir una espera circular ilimitada; si el jugador está ocupado se rechaza esa ejecución, sin reintentos ocultos.

### StartQuest

Formato `grupo:número`. Resuelve una misión configurada en las definiciones de NPC/monstruos, compatible con la clase, y utiliza un overload interno de `QuestStartAction` que conserva las validaciones originales de nivel, coste, estado activo y repetición. No fabrica una misión arbitraria ni modifica `OpenedNpc` para simular una ventana. Los IDs de ejemplo deben existir en la configuración concreta. Un rechazo se informa al jugador; no se anuncia éxito sin un `ActiveQuest` real.

### ShowLore

Temas locales permitidos: `lorencia`, `mercado`, `leila`. Los temas desconocidos responden que no hay información. No se inventa un método `ISocialMemoryRepository.GetLore` inexistente ni se requiere acceso a la base de datos.

Los textos de batalla son narrativos; no conceden buffs ni saltan validaciones PvP. La interfaz de opciones Babylon.js no forma parte de esta integración: se utiliza el chat existente.

## Verificación reproducible

Desde la raíz de OpenMU:

```bash
dotnet test tests/MUnique.OpenMU.Tests/MUnique.OpenMU.Tests.csproj -p:ci=true -m:1 --filter FullyQualifiedName~YarnBot
dotnet build src/Dapr/GameServer.Host/MUnique.OpenMU.GameServer.Host.csproj -p:ci=true -m:1
```

Las pruebas cubren las cuatro identidades, todas las ramas incluidas, sustituciones, opciones ocultas/IDs, espera de comandos asíncronos, aislamiento por jugador, entrega real de chat por cola/plugin, transferencia sin generación de stock, rollback ante inventario lleno, inicio de misión, argumentos inválidos, rango y errores del compilador/archivos ausentes. Se verifica también la copia de los cuatro recursos en un publish real.
