# 🔧 Correcciones Necesarias - Integración de Plug-ins Fase 1-3

**Fecha:** 29 Octubre 2026  
**Problema detectado:** Los plug-ins personalizados no se integran correctamente con el sistema existente de OpenMU. La compilación falla y las reglas personalizadas no se aplican a los bots existentes.

---

## ❌ Problemas Identificados por GPT

### **1. SocialMemoryRepository - Errores de Compilación**

**Problema:**
- `Microsoft.EntityFrameworkCore` no referenciado en el proyecto base Persistence
- `ISocialMemoryRepository` declarado dentro de su propia implementación (circular dependency)
- `OpenMUContext` y sus `DbSet` sociales no existen
- `EnsureCreated()` en constructor NO es migración incremental

**Solución:**
```bash
# 1. Agregar referencia a EntityFramework en el proyecto Persistence
cd src/Persistence
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Tools

# 2. Mover ISocialMemoryRepository a archivo separado (contract)
src/Persistence/Contracts/ISocialMemoryRepository.cs

# 3. Crear migraciones reales para PostgreSQL (no SQL Server como documentado)
cd src/Persistence/EntityFramework
dotnet ef migrations add AddSocialMemoryTables
dotnet ef database update

# 4. Eliminar EnsureCreated() del constructor - usar migraciones en su lugar
```

### **2. CustomItemEvaluatorPlugIn y CustomNavigationPlugIn - No son Plug-ins Reales**

**Problema:**
- Heredan `BotFeaturePlugIn` pero NO reemplazan los handlers existentes (`BotEquipmentHandler`, `BotNavigator`)
- Sus métodos `ShouldEquipItem()`, `ShouldSellItem()`, `PickHuntingGround()` no tienen consumidores fuera de sus archivos
- No declaran GUID propios exigidos por el registro en puntos de extensión
- Se descubren automáticamente pero se ejecutan como features adicionales, NO como políticas de evaluación

**Solución (Opción A - Mínimo Cambio):**
```csharp
// Convertir evaluador y navegación en clases auxiliares SIN heredar BotFeaturePlugIn

src/GameLogic/Bots/CustomItemEvaluatorPolicy.cs (nueva)
- Eliminar herencia de BotFeaturePlugIn
- Hacer clase estática o con constructor inyectado
- Integrar directamente en BotEquipmentHandler.TryPlanSwap()

src/GameLogic/Bots/CustomNavigationPolicy.cs (nueva)  
- Eliminar herencia de BotFeaturePlugIn
- Integrar directamente en BotNavigator.TryPickHuntingGround()
```

**Solución (Opción B - Plug-ins Reales):**
```csharp
// Crear interfaces específicas con GUID propios y llamadas explícitas

src/GameLogic/Bots/PlugIns/IItemEvaluationPlugin.cs (nueva)
[PlugIn(PLUGIN_GUID = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
public interface IItemEvaluationPlugin {
    bool ShouldEquip(Item item, CharacterClass characterClass);
    bool ShouldSell(Item item);
}

src/GameLogic/Bots/PlugIns/CustomItemEvaluatorPlugin.cs (modificar)
- Implementar IItemEvaluationPlugin en lugar de heredar BotFeaturePlugIn
- Agregar atributo [PlugIn] con GUID único
- Registrar en PlugInManager mediante reflection

// Similar para CustomNavigationPlugIn → INavigationPlugin
```

### **3. Incompatibilidades de Tipos y APIs**

**Problema:** El código personalizado presupone tipos que NO existen:
- `ItemGroup` → Debe ser `Definition.Group`
- `ItemOptionItemClass` → Debe ser `ItemOptionType` o similar  
- `item.Slot` → Debe ser `ItemSlot`
- `item.SellPrice` → Debe ser `PriceCalculator.CalculateSellingPrice()`
- `botPlayer.Class` → Debe ser `SelectedCharacter.CharacterClass`
- `SpawnArea`, `GameObjectProvider` → No existen en este checkout

**Solución:** Adaptar el código al modelo real de OpenMU:
```csharp
// En CustomItemEvaluatorPlugIn.cs

// CORREGIR: ItemGroup → Definition.Group
var itemGroup = item.Definition.Group; // NO item.Group

// CORREGIR: ItemOptionItemClass → ItemOptionType (verificar enum exacto)
if (item.ItemOptions.Any(opt => opt.OptionType == ItemOptionType.Excellent)) { ... }

// CORREGIR: item.Slot → ItemSlot  
var equippedItem = botPlayer.GetItemBySlot(item.Slot); // Verificar ItemSlot vs int

// CORREGIR: item.SellPrice → PriceCalculator
var sellValue = PriceCalculator.CalculateSellingPrice(item, botPlayer.SelectedCharacter.CharacterClass);

// CORREGIR: botPlayer.Class → SelectedCharacter.CharacterClass
var characterClass = botPlayer.SelectedCharacter?.CharacterClass ?? CharacterClass.DarkKnight;

// En CustomNavigationPlugIn.cs

// CORREGIR: SpawnArea/GameObjectProvider → GameMap.Definition.MonsterSpawns
var monsterSpawns = gameMapDefinition.MonsterSpawns; // NO GameObjectProvider.GetSpawnAreas()

// CORREGIR: Verificar estructura de MonsterSpawnArea
foreach (var spawn in monsterSpawns) {
    var zoneName = spawn.Area.Name; // Verificar propiedad exacta
    var maxLevel = spawn.MaxLevel ?? spawn.DefaultMonsterLevel; // Verificar propiedades disponibles
}
```

### **4. Integración Incorrecta con Handlers Existentes**

**Problema:** Las reglas personalizadas no se conectan a los puntos reales de loot/equipamiento:
- `BotEquipmentHandler.TryPlanSwap()` - NO llama a CustomItemEvaluatorPolicy
- `BotShoppingHandler.GetSellableJunk()` - NO aplica protección de jewels
- `Offline/ItemPickupHandler.ShouldPickUp()` - NO usa reglas personalizadas

**Solución:** Integrar explícitamente en los handlers existentes:

```csharp
// En src/GameLogic/Bots/BotEquipmentHandler.cs

// MODIFICAR: TryPlanSwap() para llamar a CustomItemEvaluatorPolicy
public static bool TryPlanSwap(BotPlayer player, Item candidate) {
    // ... validaciones existentes ...
    
    // NUEVO: Aplicar política personalizada de evaluación
    if (CustomItemEvaluatorPolicy.ShouldSkipEvaluation(player, candidate)) {
        return false; // Saltar evaluación estándar si no aplica reglas personalizadas
    }
    
    var isUpgrade = EvaluateForUpgrade(candidate, player);
    return isUpgrade && CustomItemEvaluatorPolicy.AllowsEquipment(candidate, player.Class);
}

// En src/GameLogic/Bots/BotShoppingHandler.cs

// MODIFICAR: GetSellableJunk() para aplicar protección de jewels
public IEnumerable<Item> GetSellableJunk(BotPlayer player) {
    var sellable = new List<Item>();
    
    foreach (var item in player.Backpack.Items) {
        // NUEVO: Proteger gems/jewels valiosos (>100 value)
        if (CustomItemEvaluatorPolicy.IsProtectedJewel(item)) {
            this._logger.LogDebug("Bot {Name} protegiendo jewel valioso", player.Name);
            continue; // NO vender, está protegido
        }
        
        sellable.Add(item);
    }
    
    return sellable;
}

// En src/GameLogic/Offline/ItemPickupHandler.cs

// MODIFICAR: ShouldPickUp() para usar reglas personalizadas de loot
public bool ShouldPickUp(BotPlayer player, Item item) {
    // ... validaciones existentes ...
    
    // NUEVO: Aplicar política personalizada
    if (!CustomItemEvaluatorPolicy.AllowsLooting(item, player)) {
        return false; // NO recoger item según reglas personalizadas
    }
    
    return true;
}
```

### **5. SocialMemoryRepository - Integración Incorrecta**

**Problema:** 
- Implementación EF en proyecto incorrecto (Persistence/EntityFramework no tiene OpenMUContext)
- Contexto compartido singleton entre bots (problemas de concurrencia)
- Migraciones PostgreSQL vs SQL Server como documentado

**Solución:**
```csharp
// src/Persistence/Contracts/ISocialMemoryRepository.cs (nueva - contract)
public interface ISocialMemoryRepository {
    void LogBotInteraction(string botName, string targetName, InteractionType type);
    int GetReputation(string botName, string targetName);
    void UpdateReputation(string botName, string targetName, int delta);
    IEnumerable<BotSocialInteraction> GetRecentInteractions(string botName, int limit = 50);
}

// src/Persistence/EntityFramework/SocialMemoryRepository.cs (modificar)
public class SocialMemoryRepository : ISocialMemoryRepository {
    private readonly OpenMUContext _context; // DEBE existir en este proyecto
    
    public SocialMemoryRepository(OpenMUContext context) {
        this._context = context ?? throw new ArgumentNullException(nameof(context));
        // ELIMINAR: EnsureCreated() - usar migraciones en su lugar
    }
    
    // Implementación de métodos usando DbSet reales (DEBEN existir en OpenMUContext):
    public void LogBotInteraction(...) {
        var interaction = new BotSocialInteraction { ... };
        this._context.BotSocialInteractions.Add(interaction);
        this._context.SaveChanges(); // NO usar EnsureCreated() aquí
    }
}

// src/Persistence/EntityFramework/Migrations/AddSocialMemoryTables.cs (nueva)
[Migration("AddSocialMemoryTables")]
public class AddSocialMemoryTables : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) {
        migrationBuilder.CreateTable(
            name: "BotSocialInteractions",
            columns: table => new {
                ID = table.Column<Guid>(nullable: false),
                BotName = table.Column<string>(maxLength: 100, nullable: false),
                TargetName = table.Column<string>(maxLength: 100, nullable: false),
                InteractionType = table.Column<int>(nullable: false),
                Timestamp = table.Column<DateTime>(nullable: false)
            });
        
        // Agregar índices para performance
        migrationBuilder.CreateIndex(
            name: "IX_BotSocialInteractions_BotName_Timestamp",
            table: "BotSocialInteractions",
            columns: new[] { "BotName", "Timestamp" },
            descending: new[] { false, true });
    }
    
    protected override void Down(MigrationBuilder migrationBuilder) {
        migrationBuilder.DropTable(name: "BotSocialInteractions");
    }
}

// src/Persistence/EntityFramework/OpenMUContext.cs (modificar)
public class OpenMUContext : DbContext {
    // AGREGAR DbSet para tablas sociales (DEBEN existir):
    public DbSet<BotSocialInteraction> BotSocialInteractions { get; set; }
    public DbSet<BotReputationRecord> BotReputationRecords { get; set; }
    public DbSet<BotEventLog> BotEventLogs { get; set; }
    
    // ELIMINAR: EnsureCreated() del constructor - usar migraciones en su lugar
    
    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);
        
        // Configurar entidades sociales con EF Core conventions
        modelBuilder.Entity<BotSocialInteraction>(entity => {
            entity.HasKey(e => e.ID);
            entity.HasIndex(e => new { e.BotName, e.Timestamp }).IsDescending(false, true);
        });
    }
}
```

---

## 📋 Lista de Correcciones Priorizadas

### **CRÍTICO - Compilación Fallida:**

1. ✅ Agregar referencia `Microsoft.EntityFrameworkCore.SqlServer` en `src/Persistence/Persistence.csproj`
2. ✅ Mover `ISocialMemoryRepository` a archivo separado `src/Persistence/Contracts/ISocialMemoryRepository.cs`
3. ✅ Crear migraciones reales para PostgreSQL: `dotnet ef migrations add AddSocialMemoryTables`
4. ✅ Eliminar `EnsureCreated()` del constructor de SocialMemoryRepository
5. ✅ Agregar DbSet sociales a OpenMUContext: `BotSocialInteractions`, `BotReputationRecords`, `BotEventLogs`

### **ALTO - Integración Incorrecta:**

6. ✅ Convertir CustomItemEvaluatorPlugIn en clase auxiliar SIN heredar BotFeaturePlugIn
7. ✅ Convertir CustomNavigationPlugIn en clase auxiliar SIN heredar BotFeaturePlugIn  
8. ✅ Integrear explícitamente en `BotEquipmentHandler.TryPlanSwap()` y `BotNavigator.TryPickHuntingGround()`
9. ✅ Corregir tipos: `ItemGroup` → `Definition.Group`, `item.Slot` → `ItemSlot`, etc.
10. ✅ Agregar GUID propios si se mantienen como plug-ins reales (Opción B)

### **MEDIO - Documentación Engañosa:**

11. ❌ Eliminar referencias a SQL Server en documentación (OpenMU usa PostgreSQL por defecto)
12. ❌ Actualizar `docs/INSTRUCCIONES_PROBAR_BOTS_FASE1.md` con comandos Docker reales del compose distribuido
13. ❌ Corregir TestBotCreator.cs para usar APIs reales, no simuladas

---

## 🚀 Plan de Acción Recomendado

### **Paso 1: Reparar Compilación (CRÍTICO - 2-4 horas)**

```bash
cd C:/Users/Andres/Documents/Codex/2026-10-24/bu/mu-online/OpenMU

# 1. Agregar referencias a EntityFramework
cd src/Persistence
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Tools

# 2. Crear contract file para ISocialMemoryRepository (si no existe)
mkdir src/Persistence/Contracts
# Crear ISocialMemoryRepository.cs con interface limpia

# 3. Corregir SocialMemoryRepository.cs
# - Eliminar EnsureCreated() del constructor
# - Agregar DbSet a OpenMUContext
# - Usar migraciones en lugar de EnsureCreated()

# 4. Crear y aplicar migraciones para tablas sociales
cd src/Persistence/EntityFramework
dotnet ef migrations add AddSocialMemoryTables
dotnet ef database update

# 5. Verificar compilación
cd ../..
dotnet build src/Dapr/GameServer.Host/MUnique.OpenMU.GameServer.Host.csproj -c Release -p:ci=true
```

### **Paso 2: Corregir Integración de Plug-ins (ALTO - 4-6 horas)**

Opción A (recomendada para mínimo cambio):
1. Convertir CustomItemEvaluatorPlugIn en clase auxiliar `CustomItemEvaluatorPolicy` sin heredar BotFeaturePlugIn
2. Convertir CustomNavigationPlugIn en clase auxiliar `CustomNavigationPolicy` sin heredar BotFeaturePlugIn
3. Integrar explícitamente en BotEquipmentHandler y BotNavigator existentes

Opción B (si se prefieren plug-ins reales):
1. Crear interfaces específicas con GUID propios: IItemEvaluationPlugin, INavigationPlugin
2. Agregar atributo [PlugIn] con GUID único a cada plug-in personalizado
3. Registrar explícitamente en PlugInManager mediante reflection

### **Paso 3: Corregir Tipos y APIs (MEDIO - 2-4 horas)**

1. Revisar CustomItemEvaluatorPlugIn.cs para corregir tipos: `Definition.Group`, `ItemSlot`, etc.
2. Revisar CustomNavigationPlugIn.cs para usar APIs reales de GameMap.Definition.MonsterSpawns
3. Actualizar docstrings y comentarios con nombres correctos de propiedades/métodos

### **Paso 4: Actualizar Documentación (BAJO - 1-2 horas)**

1. Actualizar `docs/INSTRUCCIONES_PROBAR_BOTS_FASE1.md` para usar PostgreSQL en lugar de SQL Server
2. Corregir comandos Docker con compose distribuido real (`deploy/distributed/docker-compose.yml`)
3. Eliminar referencias a EnsureCreated() como migración incremental

---

## 🧪 Verificación Post-Corrección

### **Compilación Exitosa:**
```bash
dotnet build src/Dapr/GameServer.Host/MUnique.OpenMU.GameServer.Host.csproj -c Release -p:ci=true --nologo
# Debe mostrar "Build succeeded" sin errores
```

### **Integración Funcional:**
1. Levantar server con Docker Desktop (`docker compose up -d`)
2. Verificar logs de BotGenerator creando bots: `docker logs gameServer0 | Select-String "BotFeature"`
3. Confirmar que CustomItemEvaluatorPolicy protege items valiosos (logs de protección)
4. Verificar SocialMemoryRepository persistiendo relaciones en PostgreSQL

### **Comandos Docker para Logs:**
```bash
# Crear docker-compose.override.yml con logging debug para bots
cat > docker-compose.bots-logs.yml << 'EOF'
services:
  gameServer0:
    environment:
      Logging__LogLevel__BotFeaturePlugIn: "Debug"
      Logging__LogLevel__MUnique.OpenMU.GameLogic.Bots: "Debug"

  gameServer1:
    environment: *bot-logs
EOF

# Levantar con logging debug
docker compose -f deploy/distributed/docker-compose.yml -f docker-compose.bots-logs.yml up -d --build

# Ver logs en tiempo real
docker logs -f gameServer0 gameServer1 | Select-String "Bot|CustomItemEvaluator|SocialMemory"
```

---

## 📝 Resumen de Cambios Mínimos Recomendados

**Cambio 1:** Convertir evaluador y navegación en políticas auxiliares sin heredar BotFeaturePlugIn  
→ Es el camino mínimo. Para hacerlas intercambiables desde el administrador, la alternativa es crear interfaces de plug-in específicas con GUID propios.

**Cambio 2:** Adaptar el código personalizado al modelo real de OpenMU  
→ El evaluador presupone tipos y miembros que no corresponden: ItemGroup, ItemOptionItemClass, item.Slot, item.SellPrice y botPlayer.Class. Usar Definition.Group, ItemSlot, PriceCalculator.CalculateSellingPrice, SelectedCharacter.CharacterClass.

**Cambio 3:** Conectar protección y evaluación con los puntos efectivos de loot/equipamiento  
→ Integrar en BotEquipmentHandler.TryPlanSwap, conservar validaciones existentes. Aplicar protección en BotShoppingHandler.GetSellableJunk antes de clasificar joyas excedentes o equipo. Si se desea recoger tesoros que no sean upgrades, cambiar también Offline/ItemPickupHandler.ShouldPickUp.

**Cambio 4:** Aplicar preferencias de navegación dentro de BotNavigator.TryPickHuntingGround  
→ Conservar comprobaciones de seguridad, selección de posiciones transitables y acciones de viaje existentes. El navegador actual ya pondera densidad/proximidad y seguridad de combate; añadir restricciones propias sin sustituir esas garantías.

**Cambio 5:** Reparar e integrar memoria social respetando las capas de persistencia  
→ Mover la implementación EF al proyecto Persistence/EntityFramework, sacar ISocialMemoryRepository a un contrato accesible y definir entidades/contexto reales con migraciones PostgreSQL. OpenMUContext y sus DbSet sociales no existen en este checkout. Registrar una fábrica o servicio que abra contextos por operación y conectar eventos sociales reales; no compartir un DbContext singleton entre bots.

**Cambio 6:** Reconstruir las imágenes y recrear los servicios afectados después de corregir  
→ Los Dockerfiles compilan y publican el código; reiniciar contenedores no incorpora cambios C#. En el compose distribuido existen gameServer0, gameServer1 y adminPanel. Reconstruir también el administrador si debe mostrar nuevos plug-ins/configuraciones. No hace falta regenerar cuentas ni activar ResetBots/PurgeBots para cambiar las reglas de bots existentes.

---

## 🎯 Próximos Pasos Inmediatos

1. **Reparar compilación** - Agregar referencias EntityFramework, crear migraciones reales
2. **Convertir plug-ins en políticas auxiliares** - Sin heredar BotFeaturePlugIn
3. **Integrar explícitamente en handlers existentes** - BotEquipmentHandler, BotNavigator
4. **Corregir tipos y APIs** - Definition.Group, ItemSlot, PriceCalculator, etc.
5. **Actualizar documentación** - PostgreSQL en lugar de SQL Server, comandos Docker reales

---

*Documento creado: 29 Octubre 2026 - Hermes Agent (Qwen local)*  
*Basado en investigación de subagentes GPT sobre integración de plug-ins OpenMU*
