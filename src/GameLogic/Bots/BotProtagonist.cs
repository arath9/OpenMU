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

            // Lo nombran en cualquier parte de la frase, o ya está charlando con ese jugador.
            if (!named && !talking)
            {
                return;
            }

            // Anti-spam: 2 segundos por jugador.
            if (talking && nowLlm - last < TimeSpan.FromSeconds(2))
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
