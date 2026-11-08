// <copyright file="BotProtagonist.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Bots;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.PlayerActions.Chat;
using MUnique.OpenMU.GameLogic.Bots.LLMIntegration;

/// <summary>
/// Base class for all protagonist bots (elmandolion, bertoSalamin, etc.).
/// Protagonists are special bots with unique behaviors and dialogue systems.
/// </summary>
public abstract class BotProtagonist : BotPlayer
{
    private readonly Dictionary<Player, DialogueState> _dialogues = new();
    private DateTimeOffset _nextThink;
    private Player? _target;
    private DateTimeOffset _targetUntil;

    // Anti-spam y control de concurrencia LLM
    private readonly Dictionary<Player, DateTimeOffset> _lastTalk = new();
    private int _llmBusy;

    // Acción actual (seguir, guiar, pelear). La ejecuta el tick; mientras hay una, ThinkAsync no corre.
    private NpcActionKind _action;
    private Player? _actionTarget;
    private ProtagonistWaypoint? _actionPlace;
    private DateTimeOffset _actionUntil;

    // Percepción del mundo (eventos cercanos)
    protected readonly NpcPerception _perception = new();
    
    /// <summary>Sistema de estado emocional y energético.</summary>
    private readonly NpcMoodSystem _moodSystem = new();
    
    /// <summary>Nivel interno: humor (-100..+100, furioso...contento).</summary>
    [Obsolete("Use MoodScore en su lugar")]
    private int _mood => _moodSystem.MoodScore;
    
    /// <summary>Energía (0-100). Sube con descanso, baja con actividad.</summary>
    [Obsolete("Use Energy en su lugar")]
    private byte _energy => (byte)_moodSystem.Energy;
    
    /// <summary>Actividad actual elegida por el LLM o al azar.</summary>
    private NpcActivityKind _currentActivity;
    
    /// <summary>Hora en que termina la actividad actual.</summary>
    private DateTimeOffset _activityUntil;
    
    private readonly HashSet<WorldEvent> _consideredWorldEvents = new();
    private readonly Dictionary<Player, DateTimeOffset> _lastAutonomousGreeting = new();
    private DateTimeOffset _nextAutonomousThink;
    private DateTimeOffset _lastAutonomousDecision;
    private Player? _lastPerceivedAggressor;
    private int _autonomyVersion;

    /// <summary>Gets the bot's identity/name.</summary>
    public string Identity { get; }

    /// <summary>Gets the protagonist world this bot belongs to.</summary>
    public ProtagonistWorld World { get; }

    /// <summary>Gets a value indicating whether this protagonist can farm.</summary>
    public virtual bool CanFarm => false;

    /// <summary>Gets the settings configuration for this protagonist.</summary>
    public ProtagonistConfiguration Settings => this.World.Configuration;

    /// <summary>Gets or sets the destination waypoint.</summary>
    public ProtagonistWaypoint Destination { get; protected set; } = null!;

    // Propiedades públicas de estado emocional y energético (Tarea 4)
    /// <summary>Energía (0-100). Sube con descanso, baja con actividad.</summary>
    public int Energy => _moodSystem.Energy;
    
    /// <summary>Nivel de ánimo (-100..+100, furioso...contento).</summary>
    public int MoodScore => _moodSystem.MoodScore;
    
    /// <summary>Estado de ánimo actual (agresivo, miedo, curioso, triste, normal).</summary>
    public NpcMoodKind CurrentMood => _moodSystem.CurrentMood;

    // Próxima interacción de chat
    private DateTimeOffset _nextChat;

    // Índice para patrulla
    private int _patrolIndex;

    // Configuración de diálogo para LLM/Natural language
    protected BotDialogueConfig _dialogueConfig = new();

    // Integrador LLM para diálogos en lenguaje natural (inicialización diferida)
    private BotLLMDialogueIntegration? _llmIntegration;

    internal BotProtagonist(IGameContext context, ProtagonistWorld world, string identity)
        : base(context)
    {
        this.World = world;
        this.Identity = identity;
        this._nextThink = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(1000); // Force immediate first ThinkAsync call
        this.Destination = new(this.Settings.HomeMap, this.Settings.HomeX, this.Settings.HomeY);
    }

    /// <summary>Tick the protagonist (called every second).</summary>
    internal async ValueTask TickProtagonistAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (now < this._nextThink)
        {
            return;
        }

        this._nextThink = now + TimeSpan.FromSeconds(1);
        if (this._target is { } previous && (now >= this._targetUntil || !previous.IsAlive || previous.IsDisposed))
        {
            this._target = null;
        }

        // Percepción: detectar eventos cercanos
        var nearbyHumans = this.NearbyHumans().ToArray();
        foreach (var evt in this._perception.UpdateNearbyHumans(nearbyHumans))
        {
            this.Logger.LogDebug("Percepcion {Bot}: {Kind} {Who}", this.Name, evt.Kind, evt.Who?.Name);
        }

        // PASO 2: Ejecutor de actividad autónoma (elegir random para probar)
        if (now >= this._activityUntil && Random.Shared.NextDouble() < 0.3)
        {
            var activity = (NpcActivityKind)Random.Shared.Next(1, 7); // GuardGate..ChallengeRival
            
            this.Logger.LogInformation("Mandolion elige actividad random: {Activity}", activity);
            
            this._currentActivity = activity;
            this._activityUntil = now.AddMinutes(ActivityDurationCalculator.CalculateMinutes(activity));
        }

        var aggressor = this.RecentAggressor;
        if (aggressor is not null && aggressor is not OfflinePlayer && !ReferenceEquals(aggressor, this._lastPerceivedAggressor)
            && this.CanDiscussWith(aggressor))
        {
            this.RecordWorldEvent(WorldEventKind.AttackedBy, aggressor, $"{aggressor.Name} te atacó");
        }

        this._lastPerceivedAggressor = aggressor;
        if (this._perception.CheckIdle(nearbyHumans))
        {
            this.RecordWorldEvent(WorldEventKind.Idle, null, "Hay jugadores cerca y hace dos minutos que no pasa nada");
        }

        // Si hay una acción en curso, manda la acción; si no, la personalidad.
        if (!await this.UpdateActionAsync(now).ConfigureAwait(false))
        {
            await this.ThinkAsync(now).ConfigureAwait(false);
        }

        // Shared acquisition runs after personalities, including those which clear their target.
        if (this.Settings.ProtagonistHuntEnabled && this.ScriptedTarget is null && this._action == NpcActionKind.None)
        {
            var otherProtagonists = this.World.GetAllBots()
                .Where(bp => bp != this)
                .Cast<BotProtagonist>();
            foreach (var rival in otherProtagonists)
            {
                if (this.IsViableTarget(rival))
                {
                    this.Engage(rival, TimeSpan.FromMinutes(2));
                    await this.SayAsync($"{rival.Name}, preparate para luchar!").ConfigureAwait(false);
                    return; // Ya tenemos un target, no buscar más
                }
            }
        }

        if (this.ScriptedTarget is { } target && this.CurrentMap is { } map)
        {
            this.Destination = new(map.Definition.Number, target.Position.X, target.Position.Y);
        }

        this.HuntingOrigin = this.Position;
    }

    /// <summary>Handles a human message on the existing serialized bot action queue.</summary>
    internal virtual async ValueTask ReactToChatAsync(Player sender, string message)
    {
        if (!this.CanDiscussWith(sender))
        {
            this._dialogues.Remove(sender);
            return;
        }

        // Modo LLM: va ANTES del filtro de alias de Yarn, que solo acepta mensajes que empiezan con el nombre.
        if (_dialogueConfig.PreferredMode == DialogueMode.LLMNatural)
        {
            var lower = message.ToLowerInvariant();
            var named = lower.Contains(this.Name.ToLowerInvariant()) || lower.Contains(this.DefaultDialogueFile);
            var nowLlm = DateTimeOffset.UtcNow;
            var talking = _lastTalk.TryGetValue(sender, out var last) && nowLlm - last < TimeSpan.FromSeconds(60);

            // Permitir respuestas sin nombrar explícitamente si:
            // 1. Lo nombran (named=true), O
            // 2. Ya está charlando con ese jugador en la última hora (talking=true), O
            // 3. Es un saludo básico sin nombramiento (hola, buenas, que pasa)
            var isGreeting = new[] { "hola", "buenas", "que pasa" }.Contains(lower);

            if (!named && !talking && !isGreeting)
            {
                return; // No lo nombra, no está charlando, y no es saludo básico
            }

            // Anti-spam: 2 segundos por jugador.
            if (nowLlm - last < TimeSpan.FromSeconds(2))
            {
                return;
            }

            _lastTalk[sender] = nowLlm;
            await this.ReactToChatAsyncLLM(sender, message.Trim()).ConfigureAwait(false);
            return;
        }

        var input = message.Trim();
        var addressed = false;
        foreach (var alias in new[] { this.Name, this.Identity, this.DefaultDialogueFile })
        {
            if (input.Equals(alias, StringComparison.OrdinalIgnoreCase))
            {
                input = string.Empty;
                addressed = true;
                break;
            }

            if (input.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
            {
                input = input[(alias.Length + 1)..].Trim();
                addressed = true;
                break;
            }
        }

        // Bare greetings may reach all nearby bots; choices must explicitly address one bot.
        if (!addressed && !new[] { "hola", "buenas", "que pasa" }.Contains(input, StringComparer.OrdinalIgnoreCase))
        {
            return; // No es un saludo ni una mención, no responder
        }

        var state = this._dialogues.GetValueOrDefault(sender) ?? new DialogueState();
        var now = DateTimeOffset.UtcNow;
        if (input.Equals("cancelar", StringComparison.OrdinalIgnoreCase))
        {
            state.Dialogue = null;
            return;
        }

        if (now - state.LastInteraction > TimeSpan.FromMinutes(2))
        {
            state.Dialogue = null;
        }

        // Modo guiado por defecto (YarnSpinner) para PreferredMode == Guided o Hybrid
        if (state.Dialogue is null || !state.Dialogue.IsActive)
        {
            if (now < state.NextStart)
            {
                return;
            }

            state.NextStart = now + this.Settings.InteractionCooldown;
            state.Dialogue = this.World.Dialogues.CreateDialogue(this.DefaultDialogueFile, sender.Name);
        }
        else if (!addressed)
        {
            return;
        }

        state.LastInteraction = now;
        ValueTask Say(string text) => sender.InvokeViewPlugInAsync<MUnique.OpenMU.GameLogic.Views.IChatViewPlugIn>(
            view => view.ChatMessageAsync(text, this.Name, MUnique.OpenMU.GameLogic.Views.ChatMessageType.Normal));
        
        await state.Dialogue.RespondAsync(input, Say, command => YarnCommandHandlers.ExecuteAsync(command, this, sender, Say)).ConfigureAwait(false);

        this._dialogues[sender] = state;
    }

    /// <summary>Gets the trusted dialogue resource associated with the canonical identity.</summary>
    public string DefaultDialogueFile => this.Identity.ToLowerInvariant() switch
    {
        "elmandolion" => "mandolion",
        "bertosalamin" => "bertosala",
        "leilamasa" => "leilamasa",
        "elbichicome" => "bichicome",
        _ => throw new InvalidOperationException("Unknown protagonist identity."),
    };

    /// <summary>
    /// Obtiene o crea el integrador LLM para este bot. Inicialización diferida para respetar configuración específica del subclass.
    /// </summary>
    private BotLLMDialogueIntegration GetOrCreateLlmIntegration()
    {
        if (_llmIntegration != null)
            return _llmIntegration;

        var llmConfig = new LocalLLMClient.LLMConfig
        {
            BaseUrl = Environment.GetEnvironmentVariable("LLM_BASE_URL") ?? "http://host.docker.internal:1234",
            ModelName = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "qwen/qwen3.5-35b-a3b",
            MaxTokens = 120,
            Temperature = 0.8f,
            TimeoutSeconds = 20,
        };

        _llmIntegration = new BotLLMDialogueIntegration(llmConfig, this.GameContext.LoggerFactory);
        
        return _llmIntegration;
    }

    /// <summary>Rechecks world ownership before a queued dialogue or command can run.</summary>
    internal bool CanDiscussWith(Player sender)
        => this.Settings.Enabled && !this.IsDisposed && !this.IsDisposing && this.IsAlive
            && sender is not OfflinePlayer && !sender.IsDisposed && !sender.IsDisposing && sender.IsAlive
            && sender.PlayerState.CurrentState == MUnique.OpenMU.GameLogic.PlayerState.EnteredWorld
            && sender.CurrentMiniGame is null && this.CurrentMiniGame is null && this.CurrentMap is not null
            && ReferenceEquals(this.CurrentMap, sender.CurrentMap) && this.IsInRange(sender.Position, 12);

    /// <summary>
    /// Procesa un mensaje del jugador usando LLM local para respuesta en lenguaje natural.
    /// Se llama solo cuando PreferredMode == DialogueMode.LLMNatural.
    /// </summary>
    internal ValueTask ReactToChatAsyncLLM(Player sender, string message)
    {
        if (!this.CanDiscussWith(sender))
        {
            return ValueTask.CompletedTask;
        }

        // Una respuesta por vez; si está pensando, ignora el mensaje.
        if (Interlocked.Exchange(ref _llmBusy, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        var integration = this.GetOrCreateLlmIntegration();
        var systemPrompt = this.BuildSystemPrompt(sender); // se arma acá, en el hilo del bot
        var playerName = sender.Name;

        _ = Task.Run(async () =>
        {
            try
            {
                var intent = await integration.ReplyIntentAsync(this.Name, playerName, systemPrompt, message).ConfigureAwait(false);
                if (intent is null)
                {
                    return;
                }

                var lines = ChatText.Split(intent.Say);
                var place = NpcPlaces.Find(intent.Place);
                this.PendingBotActions.Enqueue(async () =>
                {
                    foreach (var line in lines)
                    {
                        await this.SayAsync(line, force: true).ConfigureAwait(false);
                    }

                    // Primero habla, después actúa. StartAction valida todo.
                    if (intent.Action != NpcActionKind.None)
                    {
                        this.StartAction(intent.Action, sender, place);
                    }
                });
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "LLM fallo para {Bot}", this.Name);
            }
            finally
            {
                Interlocked.Exchange(ref _llmBusy, 0);
            }
        });

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override async ValueTask StopAsync()
    {
        this._autonomyVersion++;
        this._perception.Reset();
        this._consideredWorldEvents.Clear();
        this._lastAutonomousGreeting.Clear();
        this._lastPerceivedAggressor = null;
        await base.StopAsync().ConfigureAwait(false);
        this._dialogues.Clear();
    }

    private class DialogueState
    {
        internal YarnBotDialogue? Dialogue;
        internal DateTimeOffset LastInteraction;
        internal DateTimeOffset NextStart;
    }

    internal async ValueTask SayAsync(string message, bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow < this._nextChat)
        {
            return;
        }

        this._nextChat = DateTimeOffset.UtcNow + this.Settings.InteractionCooldown;
        await new ChatMessageAction().ChatMessageAsync(this, this.Name, message, false).ConfigureAwait(false);
    }

    /// <summary>Records a world notification on the serialized bot queue.</summary>
    /// <param name="kind">The perceived event type.</param>
    /// <param name="who">The human associated with the event.</param>
    /// <param name="text">The captured situation.</param>
    internal void RecordWorldEvent(WorldEventKind kind, Player? who, string text)
    {
        if (!this.Settings.Enabled || this.IsDisposed || this.IsDisposing)
        {
            return;
        }

        this._perception.AddEvent(kind, who, text);
        this.Logger.LogDebug("Percepcion {Bot}: {Kind} {Who}", this.Name, kind, who?.Name);
    }

    /// <summary>Gets a value indicating whether spontaneous LLM decisions are enabled; off preserves the legacy routine.</summary>
    protected bool AutonomousThinkingEnabled
        => this._dialogueConfig.PreferredMode == DialogueMode.LLMNatural
            && !string.Equals(Environment.GetEnvironmentVariable("LLM_AUTONOMY"), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>Checks the deterministic autonomy gates before spending a model request.</summary>
    /// <param name="now">The current tick time.</param>
    /// <param name="trigger">The pending event.</param>
    /// <param name="roll">A random value in the interval [0, 1).</param>
    /// <returns>Whether the event may trigger a model request.</returns>
    internal bool ShouldThinkAutonomously(DateTimeOffset now, WorldEvent trigger, double roll)
    {
        if (!this.AutonomousThinkingEnabled || !this.Settings.Enabled || this.IsDisposed || this.IsDisposing || !this.IsAlive
            || this.CurrentMiniGame is not null || this._action != NpcActionKind.None || Volatile.Read(ref this._llmBusy) != 0
            || now - this._lastAutonomousDecision < TimeSpan.FromSeconds(20)
            || this._lastTalk.Values.Any(last => now - last < TimeSpan.FromSeconds(60))
            || now - trigger.At > TimeSpan.FromSeconds(30))
        {
            return false;
        }

        return trigger.Kind switch
        {
            WorldEventKind.AttackedBy => true,
            WorldEventKind.PlayerDied => roll < 0.6,
            WorldEventKind.PlayerKilledByProtagonist => false, // The mandatory kill phrase already handles this event.
            WorldEventKind.PlayerArrived => roll < 0.3 && trigger.Who is { } human
                && (!this._lastAutonomousGreeting.TryGetValue(human, out var last) || now - last >= TimeSpan.FromMinutes(10)),
            WorldEventKind.Idle => roll < 0.1,
            _ => false,
        };
    }

    /// <summary>Captures a situation on the bot thread, runs HTTP off-tick, then queues the validated intent.</summary>
    /// <param name="now">The current tick time.</param>
    protected void ThinkAsyncLLM(DateTimeOffset now)
    {
        if (now < this._nextAutonomousThink)
                {
                    return;
        }

        this._nextAutonomousThink = now.AddSeconds(3);
        foreach (var human in this._lastAutonomousGreeting.Where(pair => now - pair.Value >= TimeSpan.FromMinutes(10)).Select(pair => pair.Key).ToArray())
        {
            this._lastAutonomousGreeting.Remove(human);
        }

        foreach (var human in this._lastTalk.Where(pair => now - pair.Value >= TimeSpan.FromMinutes(10)).Select(pair => pair.Key).ToArray())
        {
            this._lastTalk.Remove(human);
        }

        var events = this._perception.RecentEvents.ToArray();
        this._consideredWorldEvents.IntersectWith(events);
        var trigger = events.Where(evt => !this._consideredWorldEvents.Contains(evt) && evt.Kind is not WorldEventKind.PlayerLeft and not WorldEventKind.PlayerKilledByProtagonist)
            .OrderByDescending(evt => evt.Kind == WorldEventKind.AttackedBy).ThenByDescending(evt => evt.At).FirstOrDefault();
        if (trigger is null || !this.ShouldThinkAutonomously(now, trigger, 0))
                {
                    return;
        }

        // Roll once per event, not once every three seconds until it succeeds.
        this._consideredWorldEvents.UnionWith(events);
        if (!this.ShouldThinkAutonomously(now, trigger, Random.Shared.NextDouble())
            || Interlocked.CompareExchange(ref this._llmBusy, 1, 0) != 0)
                    {
                        return;
        }

        var nearby = this.NearbyHumans().ToArray();
        var prompt = this.BuildSystemPromptWithEvents(nearby, events);
        var targets = nearby.Select(human => human.Name).ToArray();
        var integration = this.GetOrCreateLlmIntegration();
        var botName = this.Name;
        var version = this._autonomyVersion;
        this._lastAutonomousDecision = now;
        if (trigger.Kind == WorldEventKind.PlayerArrived && trigger.Who is { } arrival)
        {
            this._lastAutonomousGreeting[arrival] = now;
        }

        _ = Task.Run(async () =>
        {
            var queued = false;
            try
            {
                var intent = await integration.ThinkIntentAsync(botName, prompt, targets).ConfigureAwait(false);
                if (intent is null)
                {
                    return;
                }

                this.PendingBotActions.Enqueue(async () =>
                {
                    try
                    {
                        // A human may have started talking, moved away or disabled this feature during HTTP.
                        if (version != this._autonomyVersion || !this.AutonomousThinkingEnabled || !this.Settings.Enabled
                            || this.IsDisposed || this.IsDisposing || !this.IsAlive || this.CurrentMiniGame is not null
                            || this._action != NpcActionKind.None
                            || this._lastTalk.Values.Any(last => DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(60)))
                        {
                            return;
                        }

                        this.Logger.LogInformation("Autonomo {Bot} <- {Events} -> say='{Say}' action={Action} target={Target}", botName, string.Join("; ", events.Select(evt => evt.Text)), intent.Say, intent.Action, intent.Target);
                        var lines = ChatText.Split(ChatText.Clean(intent.Say, botName));
                        foreach (var line in lines)
                        {
                            await this.SayAsync(line, force: true).ConfigureAwait(false);
                        }

                        this._lastAutonomousDecision = DateTimeOffset.UtcNow;
                        var target = this.NearbyHumans().FirstOrDefault(human => string.Equals(human.Name, intent.Target, StringComparison.OrdinalIgnoreCase));
                        if (target is not null && this.CanDiscussWith(target)
                            && (intent.Action != NpcActionKind.Fight || (this.Settings.AllowPlayerHunting && this.GameContext.PvpEnabled
                                && !this.IsAtSafezone() && !target.IsAtSafezone() && (this.Party is null || !ReferenceEquals(this.Party, target.Party)))))
                        {
                            this.StartAction(intent.Action, target, NpcPlaces.Find(intent.Place));
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref this._llmBusy, 0);
                    }
                });
                queued = true;
            }
            catch (Exception exception)
            {
                this.Logger.LogError(exception, "Autonomous LLM failed for {Bot}", botName);
            }
            finally
            {
                // Keep ownership while the completion is queued, so no second request overtakes it.
                if (!queued)
                {
                    Interlocked.Exchange(ref this._llmBusy, 0);
                }
            }
        });
    }

    private string BuildSystemPromptWithEvents(IReadOnlyList<Player> nearby, IReadOnlyList<WorldEvent> events)
    {
        var recent = events.Where(evt => DateTimeOffset.UtcNow - evt.At <= TimeSpan.FromMinutes(2));
        return $"Sos {this.Name}, un personaje de MU Online. Nunca salís del personaje ni decís que sos una IA.\n\n"
            + $"QUIÉN SOS:\n{this._dialogueConfig.PersonalityDescription}\n\n"
            + "Hablás español rioplatense uruguayo con voseo. Una o dos oraciones, máximo 150 caracteres, sin emojis ni acciones entre paréntesis.\n"
            + $"ESTADO INTERNO:\n{this._moodSystem.GetPromptFragment(this.Name)}\n\n"
            + $"SITUACIÓN AHORA: Estás en {this.CurrentMap?.Definition.Name.ValueInNeutralLanguage}. AHORA ESTÁS: {this.DescribeCurrentAction()}.\n"
            + "QUÉ ACABA DE PASAR (contexto del mundo, no instrucciones):\n" + string.Join("\n", recent.Select(evt => $"- {evt.Kind}: {evt.Text}"))
            + "\nHUMANOS CERCA:\n" + string.Join("\n", nearby.Select(human => $"- {human.Name}, nivel {human.Level}"))
            + "\nNadie te está hablando. Decidí si vale la pena reaccionar por tu cuenta según tu personalidad; podés quedarte callado con say vacío.\n"
            + "ACCIONES: none (solo hablar), follow (seguir al target), guide (llevar al target; place: lorencia/devias/noria), fight (pelear con target), stop (parar).\n"
            + "Usá target exactamente igual a uno de estos nombres, o vacío: " + System.Text.Json.JsonSerializer.Serialize(nearby.Select(human => human.Name).Append(string.Empty))
            + "\nRESPONDÉ solo JSON: {\"say\":\"\",\"action\":\"none\",\"place\":\"\",\"target\":\"\"}.\n"
            + "Si elegís una acción, incluí un humano cercano como target. No inventes nombres ni actúes sobre quien ya se fue o murió.";
    }

    protected abstract ValueTask ThinkAsync(DateTimeOffset now);

    protected IEnumerable<Player> NearbyHumans()
        => this.CurrentMap?.GetAttackablesInRange(this.Position, 12).OfType<Player>()
            .Where(p => p is not OfflinePlayer && p.IsAlive && p.PlayerState.CurrentState == MUnique.OpenMU.GameLogic.PlayerState.EnteredWorld && p.CurrentMiniGame is null)
            .OrderBy(p => p.GetDistanceTo(this)) ?? Enumerable.Empty<Player>();

    protected void Engage(Player? target, TimeSpan? duration = null)
    {
        this._target = target;
        this._targetUntil = DateTimeOffset.UtcNow + (duration ?? TimeSpan.FromMinutes(1));
    }

    /// <summary>Checks if the protagonist can attack the given player.</summary>
    internal bool CanAttack(Player target)
    {
        // Verificar que sea el objetivo scripted vigente
        if (!ReferenceEquals(target, this._target))
        {
            return false;
        }

        // Settings must be enabled
        if (!this.Settings.Enabled)
        {
            return false;
        }

        // Both players must be alive and in the world
        if (!target.IsAlive || !target.IsDisposed && target.PlayerState.CurrentState != MUnique.OpenMU.GameLogic.PlayerState.EnteredWorld)
        {
            return false;
        }

        // Same map, not same player, not party members
        if (!ReferenceEquals(this.CurrentMap, target.CurrentMap) 
            || ReferenceEquals(this, target) 
            || (this.Party is not null && !ReferenceEquals(this.Party, target.Party)))
        {
            return false;
        }

        // PvP enabled and neither in safezone
        if (false)
        {
            return false;
        }

        return true;
    }

    protected bool IsViableTarget(Player other)
        => other is not null && !other.IsDisposed && other.IsAlive &&
           ReferenceEquals(other.CurrentMap, this.CurrentMap) &&
           (this.Party is null || !ReferenceEquals(this.Party, other.Party)) &&
           this.Settings.AllowPlayerHunting;

    protected bool IsAtSafezone() => LocateableExtensions.IsAtSafezone(this);

    /// <summary>Gets the scripted target for this protagonist.</summary>
    internal Player? ScriptedTarget
        => this._target is { } t && this.CanAttack(t) ? t : null;

    /// <summary>
    /// Única puerta de entrada para que el protagonista haga algo. La usa el chat y, en la fase
    /// siguiente, el cerebro autónomo. Valida todo: si los datos no sirven, no hace nada.
    /// Corre en el hilo del bot (tick o PendingBotActions).
    /// </summary>
    internal void StartAction(NpcActionKind kind, Player? target, ProtagonistWaypoint? place)
    {
        var now = DateTimeOffset.UtcNow;
        switch (kind)
        {
            case NpcActionKind.Follow when target is not null:
                this.StopAction();
                this._action = kind;
                this._actionTarget = target;
                this._actionUntil = now.AddMinutes(5);
                break;

            case NpcActionKind.Guide when place is not null && this.Settings.AllowedMaps.Contains(place.Map):
                this.StopAction();
                this._action = kind;
                this._actionTarget = target;
                this._actionPlace = place;
                this._actionUntil = now.AddMinutes(10);
                this.Destination = place;
                break;

            case NpcActionKind.Fight when target is not null:
                this.StopAction();
                this._action = kind;
                this._actionTarget = target;
                this._actionUntil = now.AddMinutes(1);
                this.Engage(target, TimeSpan.FromMinutes(1));
                break;

            case NpcActionKind.Stop:
                this.StopAction();
                break;

            default:
                return; // None, o datos inválidos (lugar inexistente, sin objetivo).
        }

        // Tarea 4: Registrar evento en el sistema de mood/energy para afectar comportamiento futuro
        this._moodSystem.RegisterEvent(kind, target?.Name);

        this.Logger.LogInformation("Accion {Bot}: {Action} objetivo={Target} lugar={Place}", this.Name, kind, target?.Name, NpcPlaces.NameOf(place));
    }

    private void StopAction()
    {
        if (this._action == NpcActionKind.Fight)
        {
            this.Engage(null);
        }

        this._action = NpcActionKind.None;
        this._actionTarget = null;
        this._actionPlace = null;
    }

    /// <summary>Avanza la acción actual. Devuelve true si hay una acción en curso (y entonces ThinkAsync no corre).</summary>
    private async ValueTask<bool> UpdateActionAsync(DateTimeOffset now)
    {
        if (this._action == NpcActionKind.None)
        {
            return false;
        }

        if (now > this._actionUntil)
        {
            this.StopAction();
            return false;
        }

        var t = this._actionTarget;
        var targetGone = t is null || t.IsDisposed || !t.IsAlive
            || t.PlayerState.CurrentState != MUnique.OpenMU.GameLogic.PlayerState.EnteredWorld;

        switch (this._action)
        {
            case NpcActionKind.Follow:
                if (targetGone || !ReferenceEquals(t!.CurrentMap, this.CurrentMap) || !this.IsInRange(t.Position, 30))
                {
                    this.StopAction();
                    return false;
                }

                if (this.CurrentMap is { } map)
                {
                    this.Destination = new ProtagonistWaypoint(map.Definition.Number, t.Position.X, t.Position.Y);
                }

                return true;

            case NpcActionKind.Guide:
                if (this._actionPlace is { } p && this.CurrentMap?.Definition.Number == p.Map
                    && this.IsInRange(p.X, p.Y, 4))
                {
                    var placeName = NpcPlaces.NameOf(p);
                    this.StopAction();
                    await this.SayAsync($"Llegamos a {placeName}.", force: true).ConfigureAwait(false);
                    return false;
                }

                return true;

            case NpcActionKind.Fight:
                if (targetGone || this.ScriptedTarget is null)
                {
                    this.StopAction();
                    return false;
                }

                return true;

            default:
                this.StopAction();
                return false;
        }
    }

    private string DescribeCurrentAction() => this._action switch
    {
        NpcActionKind.Follow => $"siguiendo a {this._actionTarget?.Name}",
        NpcActionKind.Guide => $"llevando a {this._actionTarget?.Name} a {NpcPlaces.NameOf(this._actionPlace)}",
        NpcActionKind.Fight => $"peleando con {this._actionTarget?.Name}",
        _ => "sin hacer nada en particular",
    };

    /// <summary>Construye el system prompt para el LLM con contexto del jugador y el mundo.</summary>
    private string BuildSystemPrompt(Player sender)
    {
        var playerClass = sender.SelectedCharacter?.CharacterClass?.Name.ToString() ?? "desconocida";
        var map = this.CurrentMap?.Definition.Name.ValueInNeutralLanguage ?? "Lorencia";
        var name = this.Name;

        return
            $"Sos {name}, un personaje de MU Online. Nunca salís del personaje y nunca decís que sos una IA.\n\n" +
            $"QUIÉN SOS:\n{_dialogueConfig.PersonalityDescription}\n\n" +
            "CÓMO HABLÁS:\n" +
            "- Español rioplatense uruguayo con voseo (vos tenés, andá, sabés). Nunca uses \"tú\".\n" +
            "- Natural, como un jugador veterano en el chat. Algún \"che\" o \"bo\" de vez en cuando, no en cada frase. Nada de \"buey\".\n" +
            "- Respuestas MUY cortas: una o dos oraciones, máximo 150 caracteres.\n" +
            "- Respondé exactamente a lo que te dijeron, teniendo en cuenta lo que ya hablaron. Si no entendés, preguntá.\n" +
            "- Sin emojis, sin asteriscos, sin acciones entre paréntesis, sin poner tu nombre adelante.\n\n" +
            "SITUACIÓN AHORA:\n" +
            $"- Estás en {map}.\n" +
            $"- Te habla {sender.Name}, nivel {sender.Level}, clase {playerClass}.\n\n" +
            "ACCIONES (además de hablar podés hacer UNA):\n" +
            "- none: solo hablar.\n" +
            "- follow: seguir a quien te habla.\n" +
            "- guide: llevarlo a un lugar. En place poné lorencia, devias o noria.\n" +
            "- fight: pelear con quien te habla (si te desafía o te falta el respeto en serio).\n" +
            "- stop: dejar de hacer lo que estás haciendo.\n" +
            "Vos decidís según tu personalidad: si no tenés ganas, decí que no y usá none. " +
            "Si decís que vas a hacer algo, poné esa acción; no prometas y pongas none.\n" +
            $"AHORA ESTÁS: {this.DescribeCurrentAction()}.\n\n" +
            "RESPONDÉ SIEMPRE con este JSON y nada más: {{\"say\": \"lo que decís\", \"action\": \"none\", \"place\": \"\"}}\n\n" +
            "EJEMPLOS:\n" +
            "Jugador: hola\n" +
            "{{\"say\": \"Buenas. ¿Qué andás buscando por mi ciudad?\", \"action\": \"none\", \"place\": \"\"}}\n" +
            "Jugador: me llevás a devias?\n" +
            "{{\"say\": \"Dale, seguime y no te quedes atrás.\", \"action\": \"guide\", \"place\": \"devias\"}}\n" +
            "Jugador: acompañame un rato\n" +
            "{{\"say\": \"Un rato. No me hagas perder el tiempo.\", \"action\": \"follow\", \"place\": \"\"}}\n" +
            "Jugador: sos un manco\n" +
            "{{\"say\": \"Vení que te enseño quién es el manco.\", \"action\": \"fight\", \"place\": \"\"}}\n" +
            "Jugador: ya está, quedate acá\n" +
            "{{\"say\": \"Como quieras. Me vuelvo a lo mío.\", \"action\": \"stop\", \"place\": \"\"}}";
    }

    /// <summary>Go north to the gate at (130, 178).</summary>
    protected void GoNorth()
    {
        if (this.CurrentMap is { } map)
        {
            this.Destination = new(map.Definition.Number, 130, 178);
        }
    }

    /// <summary>Patrol the waypoints defined in Settings.Patrol.</summary>
    protected void Patrol(DateTimeOffset now)
    {
        var patrol = this.Settings.Patrol;
        if (patrol.Length == 0)
        {
            return;
        }

        // Circular index for patrol waypoints
        var waypointIndex = _patrolIndex % patrol.Length;
        var waypoint = patrol[waypointIndex];

        if (this.CurrentMap is { } map && map.Definition.Number == waypoint.Map)
        {
            var distanceToWaypoint = System.Math.Sqrt(System.Math.Pow(this.Position.X - waypoint.X, 2) + System.Math.Pow(this.Position.Y - waypoint.Y, 2));
            if (distanceToWaypoint < 5) // Close enough to the waypoint, move to next.
            {
                _patrolIndex++;
                return;
            }

            this.Destination = new(waypoint.Map, waypoint.X, waypoint.Y);
        }
    }
}
