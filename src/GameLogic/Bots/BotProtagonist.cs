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

        await this.ThinkAsync(now).ConfigureAwait(false);

        // Shared acquisition runs after personalities, including those which clear their target.
        if (this.Settings.ProtagonistHuntEnabled && this.ScriptedTarget is null)
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
                var reply = await integration.ReplyAsync(this.Name, playerName, systemPrompt, message).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(reply))
                {
                    return;
                }

                var lines = ChatText.Split(reply);
                this.PendingBotActions.Enqueue(async () =>
                {
                    foreach (var line in lines)
                    {
                        await this.SayAsync(line, force: true).ConfigureAwait(false);
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
            "EL MUNDO (usá solo esto, no inventes lugares):\n" +
            "Mapas: Lorencia, Noria, Devias, Dungeon, Lost Tower, Atlans, Tarkan, Icarus. " +
            "Cosas: monstruos, Jewel of Bless, Soul y Chaos, alas, reset, party, guild, PK.\n\n" +
            "EJEMPLOS DE TU ESTILO:\n" +
            "Jugador: hola\n" +
            $"{name}: Buenas. ¿Qué andás buscando por mi ciudad?\n" +
            "Jugador: estoy perdido\n" +
            $"{name}: Salí por la puerta norte y seguí el camino. Y no te metas al Dungeon con ese nivel.\n" +
            "Jugador: sos un manco\n" +
            $"{name}: Decímelo en la arena y vemos quién es el manco.";
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
