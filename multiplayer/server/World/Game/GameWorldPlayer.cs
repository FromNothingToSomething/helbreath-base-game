using System.Collections.Generic;
using System.Threading.RateLimiting;
using Mmorpg.Network;
using Server.Helpers;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Utils;
using Server.World;

namespace Server.World.Game;

/// <summary>
/// Per-session avatar on a map: grid position, visibility set, ping samples, movement anti-cheat, optional spawn protection,
/// combat interrupt stunlock timing for movement validation, and one authoritative per-player inventory.
/// Connection callbacks may be cleared while the entity remains for reconnect grace.
/// </summary>
public class GameWorldPlayer : GameWorldActionableEntity {
    private Action<ServerMessage>? sendMessage;
    private Action<string?>? requestDisconnect;
    private readonly Action<WorldTransferDestination> requestWorldChange;
    /// <summary>Invoked when combat damage cancels a pending timed logout (clears session logout deadline and notifies the client).</summary>
    private readonly Action interruptLogoutDueToCombat;
    /// <summary>Other player ids currently considered within this client's view radius (server authority).</summary>
    private readonly HashSet<long> playersInRange = new();
    /// <summary>Monster ids currently within this client's view radius (server authority).</summary>
    private readonly HashSet<long> monstersInRange = new();
    /// <summary>NPC instance ids currently within this client's view radius (server authority).</summary>
    private readonly HashSet<long> npcsInRange = new();
    /// <summary>Ground effect ids currently within this client's view radius (server authority).</summary>
    private readonly HashSet<long> groundEffectsInRange = new();
    /// <summary>Top-most ground item ids currently within this client's view radius (server authority).</summary>
    private readonly HashSet<long> groundItemsInRange = new();
    private readonly MovementSpeedViolationCheckConfig movementSpeedViolationCheckConfig;
    /// <summary>Fraction of each base timing (ms) withheld in addition to <see cref="GetCappedPingVariance"/> for anti-cheat slack; from <c>Settings.json</c> <c>timings.antiHackTimingLagFactor</c>.</summary>
    private readonly double antiHackTimingLagFactor;
    private readonly PlayerPingTracker playerPingTracker;
    /// <summary>Sliding-window limiter for rapid successive moves; recreated when paralysis resets the window.</summary>
    private SlidingWindowRateLimiter movementSpeedViolations;

    /// <summary>Authoritative current HP; reduced by combat hits. Reset to <see cref="maxHp"/> on <see cref="SetInitialState"/>.</summary>
    private int hp;
    /// <summary>Authoritative max HP for this session; sent in <see cref="Mmorpg.Network.InitialState"/> and <see cref="Mmorpg.Network.HpUpdated"/>.</summary>
    private int maxHp;
    /// <summary>Level/XP block (mana is tracked live in <see cref="mana"/> and merged back by <see cref="CreateNocState"/>).</summary>
    private NocState noc;
    private readonly StatPipeline stats;
    private readonly ProgressionConfig progression;
    private readonly RaceConfig races;
    private readonly ProgressionService progressionService;
    /// <summary>Race whose modifiers are currently in <see cref="stats"/> (source <c>ModifierSources.Race</c>); never null after construction.</summary>
    private RaceDefinition race;
    /// <summary>Current mana; clamped to [0, <see cref="MaxMana"/>].</summary>
    private int mana;
    private int movementSpeedMs = 220;
    /// <summary>Unix ms of the last accepted movement request for delta-based speed checks.</summary>
    private long lastMovementRequestMs;
    /// <summary>Ms since epoch of the last successful player attack damage delivery (scheduler callback); null until the first delivery this connection or after dash resets cadence.</summary>
    private long? lastPlayerAttackDamageDeliveredMs;
    private bool runningMode = true;
    private bool attackMode = true;
    /// <summary>While active, movement requests snap the player back without consuming violations.</summary>
    private DateTimeOffset? serverForcedParalysisUntil;
    /// <summary>When set and <c>now</c> is before this instant, the combat interrupt stunlock window is still active for movement checks.</summary>
    private DateTimeOffset? combatInterruptStunlockUntil;
    /// <summary>Until this instant, server rejects non-pickup player actions after an accepted <see cref="Mmorpg.Network.PlayerPickupRequested"/>; lockout duration uses animation ms minus lag factor and <see cref="GetCappedPingVariance"/>.</summary>
    private DateTimeOffset? pickupDurationUntil;
    /// <summary>Until this instant, server rejects other player actions after an accepted <see cref="Mmorpg.Network.PlayerBowStanceRequested"/>; lockout duration uses animation ms minus lag factor and <see cref="GetCappedPingVariance"/>.</summary>
    private DateTimeOffset? bowStanceDurationUntil;
    /// <summary>When true, movement is rejected until protection is cleared (timer or first move).</summary>
    private bool spawnProtection;
    /// <summary>Interrupt stunlock duration (ms) from the last combat hit; cleared when <see cref="combatInterruptStunlockUntil"/> elapses.</summary>
    private int stunlockDurationMs;
    /// <summary>Increments on each combat hit that is not <see cref="Server.AttackType.NoInterrupt"/>; pending player-attack callbacks compare against a captured snapshot.</summary>
    private int interruptedCount;
    /// <summary>Ms of monster stunlock when this player lands <see cref="Server.AttackType.Stun"/> and <see cref="GameWorldMonster.TryApplyStunlock"/> succeeds; clamped 100–2000.</summary>
    private int attackStunDurationMs = 500;
    /// <summary>Chebyshev cells: authoritative melee reach vs monsters (sent in <see cref="Mmorpg.Network.InitialState"/>).</summary>
    private int attackRangeCells = 3;
    /// <summary>Authoritative melee damage vs monsters.</summary>
    private int damage = 100;
    /// <summary>Base time between melee attempts in ms; lag-compensation delay uses half this value.</summary>
    private int attackSpeedMs = 600;
    /// <summary>Full spell cast bar duration in ms; sent in <see cref="Mmorpg.Network.InitialState"/>; server clamps 200–2000.</summary>
    private int castSpeedMs = 1200;
    /// <summary>Persisted local-player hit mode selection mirrored back to the client UI; current combat validation still uses packet-provided attack type.</summary>
    private int attackType = (int)Server.AttackType.Stun;
    /// <summary>Persisted local-player preference for dash-attacks; currently mirrored to the client UI only.</summary>
    private bool allowDashAttack = true;
    /// <summary>Last spell selected by the player for the current cast flow; cleared on cancel or after the cast resolves.</summary>
    private int? requestedSpellId;
    /// <summary>Unix ms when the last accepted <see cref="Mmorpg.Network.SpellCastStartRequest"/> was processed; used for cast-request interval checks.</summary>
    private long? lastSpellCastStartMs;
    /// <summary>Per-player authoritative bag and equipment state; persisted across logout and world transfer.</summary>
    private readonly InventoryManager inventoryManager;
    /// <summary>0 = male, 1 = female; matches <see cref="Mmorpg.Network.PlayerGender"/>.</summary>
    private int genderValue;
    /// <summary>0 = light, 1 = tanned, 2 = dark; matches <see cref="Mmorpg.Network.PlayerSkinColor"/>.</summary>
    private int skinColorValue;
    /// <summary>Hair style index 0–7 (client Style 1–8).</summary>
    private int hairStyleIndex;
    /// <summary>Underwear palette index 0–7.</summary>
    private int underwearColorIndex;
    /// <summary>Client-supplied character display name from authenticate; persisted in <see cref="PlayerPersistenceState"/>.</summary>
    private string characterName = "";

    public Guid SessionId { get; }
    public long PlayerId { get; }

    protected override TemporaryEffectEntityKind EntityKind => TemporaryEffectEntityKind.Player;
    protected override long EntityId => PlayerId;
    public IReadOnlyCollection<long> PlayersInRange => playersInRange;
    public IReadOnlyCollection<long> MonstersInRange => monstersInRange;
    public IReadOnlyCollection<long> NpcsInRange => npcsInRange;
    public IReadOnlyCollection<long> GroundEffectsInRange => groundEffectsInRange;
    public IReadOnlyCollection<long> GroundItemsInRange => groundItemsInRange;

    public int Hp => hp;
    public int MaxHp => maxHp;
    public int Level => noc.Level;
    /// <summary>XP accumulated toward the next level.</summary>
    public long Xp => noc.Xp;
    public int Mana => mana;
    public int MaxMana => stats.MaxMana;
    /// <summary>Stat resolution for this player (level base plus modifiers); world thread only.</summary>
    public StatPipeline Stats => stats;
    /// <summary>True when <see cref="hp"/> is below 1 (lethal threshold).</summary>
    public bool IsDead => hp < 1;
    public bool Disconnected { get; private set; }
    public bool RunningMode => runningMode;
    public bool AttackMode => attackMode;
    public int BaseMovementSpeedMs => movementSpeedMs;

    /// <summary>Final ms per tile (run/walk base with temporary-effect modifiers); matches client tile duration.</summary>
    public int MovementSpeedMs =>
        Math.Clamp(
            TemporaryEffectSpeedModifierMath.ApplyModifierSumToDurationMs(
                runningMode ? movementSpeedMs : movementSpeedMs * 2,
                temporaryEffectMovementSpeedModifierSum),
            100,
            1000);

    /// <summary>Base melee cadence ms from UI/persistence before temporary-effect modifiers.</summary>
    public int BaseAttackSpeedMs => attackSpeedMs;

    /// <summary>Base spell cast bar ms from UI/persistence before temporary-effect modifiers.</summary>
    public int BaseCastSpeedMs => castSpeedMs;
    public double PingVariance => playerPingTracker.PingVariance;
    public long LastPingTimeMs => playerPingTracker.LastPingTimeMs;
    public bool SpawnProtection => spawnProtection;
    /// <summary>Combat interrupt stunlock duration (ms) for the active window; 0 when not stunlocked.</summary>
    public int StunlockDurationMs => stunlockDurationMs;

    /// <summary>Generation counter for interrupting hits; <see cref="RegisterNonNoInterruptDamage"/> increments. Starts at 0; reset with connection state.</summary>
    public int InterruptedCount => interruptedCount;

    /// <summary>Chebyshev cells: authoritative melee reach vs monsters (sent in <see cref="Mmorpg.Network.InitialState"/>).</summary>
    public int AttackRange => attackRangeCells;

    /// <summary>Authoritative melee damage vs monsters until per-player stats exist.</summary>
    public int Damage => damage;

    /// <summary>Effective time between melee attempts in ms (base + temporary-effect modifiers).</summary>
    public int AttackSpeedMs =>
        Math.Clamp(
            TemporaryEffectSpeedModifierMath.ApplyModifierSumToDurationMs(attackSpeedMs, temporaryEffectAttackSpeedModifierSum),
            200,
            2000);

    /// <summary>Monster stunlock duration (ms) for this player’s Stun hits; sent in <see cref="Mmorpg.Network.InitialState"/>.</summary>
    public int AttackStunDurationMs => attackStunDurationMs;

    /// <summary>Effective spell cast bar duration in ms (base + temporary-effect modifiers).</summary>
    public int CastSpeedMs =>
        Math.Clamp(
            TemporaryEffectSpeedModifierMath.ApplyModifierSumToDurationMs(castSpeedMs, temporaryEffectCastSpeedModifierSum),
            200,
            2000);
    /// <summary>Persisted local-player hit mode selection mirrored to the client UI.</summary>
    public int AttackType => attackType;
    /// <summary>Persisted local-player dash-attack toggle mirrored to the client UI.</summary>
    public bool AllowDashAttack => allowDashAttack;
    public int? RequestedSpellId => requestedSpellId;
    public InventoryManager InventoryManager => inventoryManager;

    /// <summary>0 = male, 1 = female; matches <see cref="Mmorpg.Network.PlayerGender"/>.</summary>
    public int GenderValue => genderValue;

    /// <summary>0 = light, 1 = tanned, 2 = dark; matches <see cref="Mmorpg.Network.PlayerSkinColor"/>.</summary>
    public int SkinColorValue => skinColorValue;

    /// <summary>Hair style index 0–7.</summary>
    public int HairStyleIndex => hairStyleIndex;

    /// <summary>Underwear palette index 0–7.</summary>
    public int UnderwearColorIndex => underwearColorIndex;

    /// <summary>Client-supplied character display name from authenticate; persisted with player saves.</summary>
    public string CharacterName => characterName;

    public GameWorldPlayer(
        Guid sessionId,
        Action<ServerMessage> sendMessage,
        Action<string?> requestDisconnect,
        Action<WorldTransferDestination> requestWorldChange,
        Action interruptLogoutDueToCombat,
        IReadOnlyDictionary<int, ItemConfig> itemsById,
        Server.MovementSpeedViolationCheckConfig violationCheckConfig,
        int pingVarianceSampleSize,
        double antiHackTimingLagFactor,
        ProgressionConfig progression,
        RaceConfig races) {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(races);
        ArgumentNullException.ThrowIfNull(sendMessage);
        ArgumentNullException.ThrowIfNull(requestDisconnect);
        ArgumentNullException.ThrowIfNull(requestWorldChange);
        ArgumentNullException.ThrowIfNull(interruptLogoutDueToCombat);
        ArgumentNullException.ThrowIfNull(itemsById);
        ArgumentNullException.ThrowIfNull(violationCheckConfig);
        SessionId = sessionId;
        PlayerId = BitConverter.ToInt64(sessionId.ToByteArray(), 0);
        this.interruptLogoutDueToCombat = interruptLogoutDueToCombat;
        this.requestWorldChange = requestWorldChange;
        this.movementSpeedViolationCheckConfig = violationCheckConfig;
        this.antiHackTimingLagFactor = antiHackTimingLagFactor;
        inventoryManager = new InventoryManager(itemsById);
        this.progression = progression;
        progressionService = new ProgressionService(progression);
        noc = progressionService.Normalize(null);
        stats = new StatPipeline(progression, noc.Level);
        this.races = races;
        race = races.Default;
        AddRaceModifiers(race);
        noc = noc with { RaceId = race.Id };
        playerPingTracker = new PlayerPingTracker(pingVarianceSampleSize);
        movementSpeedViolations = CreateMovementSpeedViolationsLimiter(violationCheckConfig);
        SetFacingDirection(1);
        AttachConnection(sendMessage, requestDisconnect);
    }

    /// <summary>Builds a rate limiter from config segments-per-window settings.</summary>
    private static SlidingWindowRateLimiter CreateMovementSpeedViolationsLimiter(MovementSpeedViolationCheckConfig config) {
        return new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions {
            PermitLimit = config.Limit,
            Window = TimeSpan.FromSeconds(config.Window),
            SegmentsPerWindow = config.SegmentsPerWindow,
        });
    }

    public void SetSpawnProtection(bool value) {
        spawnProtection = value;
    }

    public void AttachConnection(Action<ServerMessage> sendMessage, Action<string?> requestDisconnect) {
        ArgumentNullException.ThrowIfNull(sendMessage);
        ArgumentNullException.ThrowIfNull(requestDisconnect);

        this.sendMessage = sendMessage;
        this.requestDisconnect = requestDisconnect;
        Disconnected = false;
        ResetConnectionState();
    }

    public void DetachConnection() {
        sendMessage = null;
        requestDisconnect = null;
        Disconnected = true;
        ClearPlayersInRange();
        ResetConnectionState();
    }

    public void SetMovementSpeedMs(int ms) {
        var clamped = Math.Clamp(ms, 100, 500);
        if (movementSpeedMs == clamped) {
            return;
        }

        movementSpeedMs = clamped;
        InvalidateMovementCadenceBaselineAfterEffectiveSpeedChange();
    }

    public void SetRunningMode(bool value) {
        if (runningMode == value) {
            return;
        }

        runningMode = value;
        InvalidateMovementCadenceBaselineAfterEffectiveSpeedChange();
    }

    public void SetAttackMode(bool value) {
        attackMode = value;
    }

    /// <summary>Updates stunlock duration from the client UI when sync is enabled; clamps to 100–2000 ms.</summary>
    public void SetAttackStunDurationMs(int ms) {
        attackStunDurationMs = Math.Clamp(ms, 100, 2000);
    }

    /// <summary>Updates melee cadence from the client UI when sync is enabled; clamps to 200–2000 ms (matches client attack speed slider).</summary>
    public void SetAttackSpeedMs(int ms) {
        attackSpeedMs = Math.Clamp(ms, 200, 2000);
    }

    /// <summary>Updates melee reach from the client UI when sync is enabled; clamps to 1–20 cells.</summary>
    public void SetAttackRangeCells(int cells) {
        attackRangeCells = Math.Clamp(cells, 1, 20);
    }

    /// <summary>Updates melee damage vs monsters from the client UI when sync is enabled; clamps to 1–1000.</summary>
    public void SetAttackDamage(int value) {
        damage = Math.Clamp(value, 1, 1000);
    }

    /// <summary>Updates spell cast duration from the client UI when sync is enabled; clamps to 200–2000 ms.</summary>
    public void SetCastSpeedMs(int ms) {
        castSpeedMs = Math.Clamp(ms, 200, 2000);
    }

    /// <summary>Updates the persisted local-player hit mode selection; invalid values fall back to <see cref="Server.AttackType.Stun"/>.</summary>
    public void SetAttackType(int value) {
        attackType = Enum.IsDefined(typeof(Server.AttackType), value)
            ? value
            : (int)Server.AttackType.Stun;
    }

    /// <summary>Updates the persisted local-player dash-attack preference mirrored to the client UI.</summary>
    public void SetAllowDashAttack(bool value) {
        allowDashAttack = value;
    }

    /// <summary>Sets gender, skin, hair, and underwear indices from client or persistence; clamps to wire ranges.</summary>
    public void SetAppearance(int gender, int skinColor, int hairIdx, int underwearIdx) {
        genderValue = gender is 0 or 1 ? gender : 0;
        skinColorValue = skinColor is >= 0 and <= 2 ? skinColor : 0;
        hairStyleIndex = Math.Clamp(hairIdx, 0, 7);
        underwearColorIndex = Math.Clamp(underwearIdx, 0, 7);
    }

    public void SetRequestedSpellId(int spellId) {
        requestedSpellId = spellId;
    }

    /// <summary>Records server time of the last accepted spell cast start (for <see cref="IsSpellCastTimingViolation"/>).</summary>
    public void RecordSpellCastStartTimeMs(long unixMs) {
        lastSpellCastStartMs = unixMs;
    }

    /// <summary>
    /// True when <paramref name="nowMs"/> is before <see cref="lastSpellCastStartMs"/> plus cast duration minus lag factor and capped ping variance, or when no start was recorded.
    /// </summary>
    /// <param name="actualElapsedSinceStartMs"><see cref="nowMs"/> minus <see cref="lastSpellCastStartMs"/> when a start was recorded; otherwise null.</param>
    public bool IsSpellCastTimingViolation(long nowMs, out double minIntervalMs, out double? actualElapsedSinceStartMs) {
        minIntervalMs = ComputeMinRequiredTimeMs(CastSpeedMs);
        if (!lastSpellCastStartMs.HasValue) {
            actualElapsedSinceStartMs = null;
            return true;
        }

        actualElapsedSinceStartMs = nowMs - lastSpellCastStartMs.Value;
        return nowMs < lastSpellCastStartMs.Value + minIntervalMs;
    }

    public void ClearRequestedSpell() {
        requestedSpellId = null;
    }

    /// <summary>Clears pending spell selection and cast-start timing when the player takes combat damage that is not <see cref="Server.AttackType.NoInterrupt"/>.</summary>
    public void ClearSpellCastStateOnInterruptingDamage() {
        requestedSpellId = null;
        lastSpellCastStartMs = null;
    }

    public void SetInitialState(int x, int y) {
        SetGridPosition(x, y);
        ApplyNocState(progressionService.Normalize(null));
        hp = maxHp;
        mana = stats.MaxMana; // a new character starts with full mana, whatever its race does to the maximum
    }

    /// <summary>Id of the player's race (<c>Races.json</c>); always a listed race.</summary>
    public int RaceId => race.Id;

    /// <summary>
    /// Switches the race: the previous race's modifiers are removed and the new ones added in one step (calling it again with the same race changes nothing),
    /// then max HP and mana are recomputed and HP and mana are clamped to the new maxima (they are never raised here).
    /// Marks the progression and HP state dirty when something observable changed, so the next tick tells the client.
    /// </summary>
    public void ApplyRace(RaceDefinition newRace) {
        ArgumentNullException.ThrowIfNull(newRace);
        var oldMaxHp = maxHp;
        var oldHp = hp;
        if (!ApplyRaceCore(newRace)) {
            return;
        }

        nocDirty = true;
        hpDirty |= maxHp != oldMaxHp || hp != oldHp;
    }

    /// <summary>Returns true when the race, max HP, HP or mana changed.</summary>
    private bool ApplyRaceCore(RaceDefinition newRace) {
        var oldRaceId = race.Id;
        var oldMaxHp = maxHp;
        var oldHp = hp;
        var oldMana = mana;
        stats.RemoveSource(race.SourceId);
        race = newRace;
        AddRaceModifiers(newRace);
        noc = noc with { RaceId = newRace.Id };
        maxHp = stats.MaxHp;
        hp = Math.Min(hp, maxHp);
        mana = Math.Clamp(mana, 0, stats.MaxMana);
        return oldRaceId != newRace.Id || oldMaxHp != maxHp || oldHp != hp || oldMana != mana;
    }

    private void AddRaceModifiers(RaceDefinition definition) {
        foreach (var modifier in definition.Modifiers) {
            stats.Add(modifier);
        }
    }

    /// <summary>Adopts a (normalized) level/XP/mana block: refreshes the stat base, <see cref="maxHp"/> and clamps HP and mana to the new maxima. The race id always follows <see cref="race"/>.</summary>
    private void ApplyNocState(NocState state) {
        noc = state with { RaceId = race.Id };
        stats.SetLevel(noc.Level);
        maxHp = stats.MaxHp;
        hp = Math.Min(hp, maxHp);
        mana = Math.Clamp(noc.Mana, 0, stats.MaxMana);
    }

    /// <summary>XP credited for a kill worth <paramref name="baseXp"/>: scaled by <see cref="StatId.XpGainMult"/> and rounded half away from zero; 0 for a non-positive base.</summary>
    public long ScaleKillXp(int baseXp) {
        if (baseXp <= 0) {
            return 0;
        }

        var scaled = Math.Round(baseXp * stats.XpGainMult, MidpointRounding.AwayFromZero);
        return scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
    }

    /// <summary>Grants the XP of a kill, with the player's XP gain modifiers. Debug and admin grants use <see cref="GrantXp"/>, which stays raw.</summary>
    public int GrantKillXp(int baseXp) => GrantXp(ScaleKillXp(baseXp));

    /// <summary>
    /// Grants XP with chained level-ups. On level-up the stat base is refreshed, HP is refilled (unless dead) and mana is refilled to <see cref="MaxMana"/>.
    /// Returns the number of levels gained; the caller is responsible for notifying the client.
    /// </summary>
    public int GrantXp(long amount) {
        var result = progressionService.GrantXp(CreateNocState(), amount);
        if (result.LevelsGained == 0) {
            nocDirty |= result.State.Xp != noc.Xp;
            noc = result.State;
            return 0;
        }

        ApplyNocState(result.State);
        mana = stats.MaxMana;
        if (!IsDead) {
            hp = maxHp;
        }

        nocDirty = true;
        hpDirty = true;
        return result.LevelsGained;
    }

    /// <summary>Upper bound on the time credited to one <see cref="TickMana"/> call, so a stalled world or a long gap cannot grant a burst of mana.</summary>
    private const long MaxManaTickDeltaMs = 5000;
    /// <summary>Unix ms of the previous <see cref="TickMana"/>; 0 until the first call.</summary>
    private long lastManaTickMs;
    /// <summary>Regen not yet worth a whole point, carried to the next tick so regen is independent of the tick rate.</summary>
    private double manaFraction;

    /// <summary>
    /// Regenerates mana by elapsed time (<see cref="StatPipeline.ManaRegen"/> per second), not per tick, with sub-point remainders carried over.
    /// The first call only sets the time baseline. Dead players do not regenerate; mana above the maximum is clamped down.
    /// Allocation-free; world thread only. Returns true when <see cref="Mana"/> changed.
    /// </summary>
    public bool TickMana(long nowMs) {
        var last = lastManaTickMs;
        lastManaTickMs = nowMs;
        if (last == 0 || nowMs <= last) {
            return false;
        }

        var max = stats.MaxMana;
        if (mana >= max) {
            var clamped = mana > max;
            mana = max;
            manaFraction = 0;
            nocDirty |= clamped;
            return clamped;
        }

        if (IsDead) {
            return false;
        }

        manaFraction += stats.ManaRegen * Math.Min(nowMs - last, MaxManaTickDeltaMs) / 1000.0;
        var gained = (int)Math.Min(manaFraction, max);
        if (gained <= 0) {
            return false;
        }

        manaFraction -= gained;
        mana = Math.Min(max, mana + gained);
        nocDirty = true;
        return true;
    }

    /// <summary>Weapon damage after the player's <see cref="StatId.DamageMult"/> modifiers; equals <see cref="Damage"/> when there are none.</summary>
    public int ComputeAttackDamage() => stats.ComputeDamage(damage);

    /// <summary>Set by progression/mana changes since the last <see cref="TryFlushNocUpdates"/>.</summary>
    private bool nocDirty;
    /// <summary>Set when a level-up changed HP or max HP since the last <see cref="TryFlushNocUpdates"/>.</summary>
    private bool hpDirty;

    /// <summary>Test hook: how many snapshots were built. The XP threshold is looked up only there, i.e. at flush and join, never per tick.</summary>
    internal int NocSnapshotsBuilt { get; private set; }

    /// <summary>Wire snapshot of level, XP (with the threshold for the next level; 0 at max level) and mana.</summary>
    public NocSnapshot CreateNocSnapshot() {
        NocSnapshotsBuilt++;
        progression.TryGetXpToNext(noc.Level, out var xpToNext);
        return NetworkManager.CreateNocSnapshot(noc.Level, noc.Xp, xpToNext, mana, stats.MaxMana);
    }

    /// <summary>
    /// Sends the pending progression update. Called once per world tick: at most one <see cref="NocStateUpdated"/> per tick and player,
    /// built from the state at that moment, so any number of XP grants or mana changes inside the tick collapse into the last state (last state wins).
    /// The existing state messages are sent on top when needed: a level-up adds one <see cref="HpUpdated"/> because max HP changed.
    /// The limit applies to <see cref="NocStateUpdated"/> only, not to the messages a player receives overall. Nothing pending: no allocation, no message.
    /// While disconnected the flags are dropped: the reconnect <see cref="InitialState"/> carries the current values.
    /// Returns true when at least one message was sent.
    /// </summary>
    public bool TryFlushNocUpdates() {
        if (!nocDirty && !hpDirty) {
            return false;
        }

        var sendNoc = nocDirty;
        var sendHp = hpDirty;
        nocDirty = false;
        hpDirty = false;
        if (Disconnected || sendMessage is null) {
            return false;
        }

        if (sendHp) {
            Send(NetworkManager.CreateHpUpdated(hp, maxHp));
        }

        if (sendNoc) {
            Send(NetworkManager.CreateNocStateUpdated(CreateNocSnapshot()));
        }

        return true;
    }

    /// <summary>Snapshot of the persisted NOCTHEMAR block with the live mana.</summary>
    public NocState CreateNocState() => noc with { Mana = mana };

    /// <summary>Applies persisted player-configurable settings after spawn creation while keeping clamp logic centralized in the existing setters.</summary>
    public void ApplyPersistedState(PlayerPersistenceState state) {
        ArgumentNullException.ThrowIfNull(state);
        SetMovementSpeedMs(state.MovementSpeedMs);
        SetCastSpeedMs(state.CastSpeedMs);
        SetAttackSpeedMs(state.AttackSpeedMs);
        SetAttackRangeCells(state.AttackRange);
        SetAttackDamage(state.Damage);
        SetAttackStunDurationMs(state.StunDuration);
        SetAttackMode(state.AttackMode);
        SetRunningMode(state.RunMode);
        SetAttackType(state.AttackType);
        SetAllowDashAttack(state.AllowDashAttack);
        SetAppearance(state.GenderValue, state.SkinColorValue, state.HairStyleIndex, state.UnderwearColorIndex);
        if (state.FacingDirection.HasValue) {
            SetFacingDirection(state.FacingDirection.Value);
        }
        if (state.BagItems is not null || state.EquippedItems is not null) {
            inventoryManager.LoadFromPersistence(state.BagItems, state.EquippedItems);
            inventoryManager.TryUnequipAllGenderMismatchedEquipment(genderValue, out _);
        }
        if (!string.IsNullOrWhiteSpace(state.CharacterName)) {
            SetCharacterName(state.CharacterName);
        }
        // The save layer already replaced an unknown race id (CharacterAccess.TryLoadAsync, with a warning naming the character); this is only the last line of defence.
        var restoredRace = state.Noc?.RaceId is int savedRaceId && races.TryGet(savedRaceId, out var found) ? found : races.Default;
        ApplyRaceCore(restoredRace);
        if (state.Noc is not null) {
            // Absent block (legacy save already migrated, or a state built without one) keeps the level-1 defaults from SetInitialState.
            ApplyNocState(progressionService.Normalize(state.Noc));
            hp = maxHp;
        }
    }

    /// <summary>Captures the current world-backed player settings and location for persistence.</summary>
    public PlayerPersistenceState CreatePersistenceState(string gameWorldId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameWorldId);
        return new PlayerPersistenceState(
            gameWorldId,
            posX,
            posY,
            movementSpeedMs,
            castSpeedMs,
            attackSpeedMs,
            attackRangeCells,
            damage,
            attackStunDurationMs,
            attackType,
            attackMode,
            runningMode,
            allowDashAttack,
            genderValue,
            skinColorValue,
            hairStyleIndex,
            underwearColorIndex,
            FacingDirection,
            inventoryManager.CreatePersistedBagItems(),
            inventoryManager.CreatePersistedEquippedItems(),
            characterName,
            SchemaVersion: CharacterSchema.Current,
            Noc: CreateNocState());
    }

    /// <summary>Sets the display name from authenticate or loaded persistence.</summary>
    public void SetCharacterName(string name) {
        characterName = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
    }

    /// <summary>Subtracts damage from <see cref="hp"/> (floors at 0).</summary>
    public void ApplyDamage(int damage) {
        if (damage <= 0 || IsDead) {
            return;
        }

        hp = Math.Max(0, hp - damage);
    }

    /// <summary>Invoked from combat fan-out after HP loss; clears pending timed logout server-side when applicable.</summary>
    public void NotifyCombatDamageMayCancelLogout() {
        interruptLogoutDueToCombat();
    }

    /// <summary>Restores HP to max after resurrection; clears dead state.</summary>
    public void ApplyResurrection() {
        hp = maxHp;
    }

    public void SetPosition(int x, int y) {
        SetGridPosition(x, y);
    }

    /// <summary>Applies server-side standstill: resets the violation limiter so the next window starts clean after <paramref name="until"/>.</summary>
    public void SetServerForcedParalysisUntil(DateTimeOffset until) {
        serverForcedParalysisUntil = until;
        movementSpeedViolations.Dispose();
        movementSpeedViolations = CreateMovementSpeedViolationsLimiter(movementSpeedViolationCheckConfig);
    }

    public bool IsServerForcedParalysisActive() {
        return serverForcedParalysisUntil.HasValue && DateTimeOffset.UtcNow < serverForcedParalysisUntil.Value;
    }

    /// <summary>Non-negative ping variance capped by <see cref="MovementSpeedViolationCheckConfig.MaxPingVariance"/>; used for movement speed and stunlock checks.</summary>
    public double GetCappedPingVariance() {
        return Math.Min(Math.Max(0, PingVariance), movementSpeedViolationCheckConfig.MaxPingVariance);
    }

    /// <summary>Minimum required time (ms) for <paramref name="baseMs"/> after subtracting <see cref="antiHackTimingLagFactor"/> and <see cref="GetCappedPingVariance"/>.</summary>
    private double ComputeMinRequiredTimeMs(int baseMs) {
        return Math.Max(0, baseMs - baseMs * antiHackTimingLagFactor - GetCappedPingVariance());
    }

    /// <summary>
    /// Expected minimum wall-clock gap (ms) between accepted movement steps for the player's current effective speed and lag slack.
    /// Exposed for diagnostics (<see cref="movementSpeedViolationCheckConfig.Verbose"/>).
    /// </summary>
    public double GetMovementCadenceMinRequiredMs() => ComputeMinRequiredTimeMs(MovementSpeedMs);

    /// <summary>
    /// Clears the last-movement timestamp so the next accepted step gets <c>deltaMs == 0</c> and skips cadence comparison.
    /// Required when <see cref="MovementSpeedMs"/> semantics change without a matching client gap (run/walk toggle, base speed change, or temporary movement-speed modifiers).
    /// </summary>
    private void InvalidateMovementCadenceBaselineAfterEffectiveSpeedChange() {
        lastMovementRequestMs = 0;
    }

    protected override void OnTemporaryEffectMovementSpeedModifierSumChanged() {
        InvalidateMovementCadenceBaselineAfterEffectiveSpeedChange();
    }

    /// <summary>True while <paramref name="nowUtc"/> is before pickup lockout ends; clears expired state.</summary>
    public bool IsPickupActionBlocking(DateTimeOffset nowUtc) {
        if (!pickupDurationUntil.HasValue) {
            return false;
        }

        if (nowUtc >= pickupDurationUntil.Value) {
            pickupDurationUntil = null;
            return false;
        }

        return true;
    }

    /// <summary>Starts pickup lockout: <paramref name="animationTimeMs"/> minus lag factor and <see cref="GetCappedPingVariance"/>, floored at zero.</summary>
    public void BeginPickupActionLockout(int animationTimeMs) {
        var durationMs = ComputeMinRequiredTimeMs(animationTimeMs);
        pickupDurationUntil = DateTimeOffset.UtcNow.AddMilliseconds(durationMs);
    }

    /// <summary>Clears pickup lockout when the player takes an interrupting combat hit (<see cref="AttackType"/> other than <see cref="AttackType.NoInterrupt"/>).</summary>
    public void ClearPickupActionLockout() {
        pickupDurationUntil = null;
    }

    /// <summary>True while bow stance lockout is active; clears expired state.</summary>
    public bool IsBowStanceActionBlocking(DateTimeOffset nowUtc) {
        if (!bowStanceDurationUntil.HasValue) {
            return false;
        }

        if (nowUtc >= bowStanceDurationUntil.Value) {
            bowStanceDurationUntil = null;
            return false;
        }

        return true;
    }

    /// <summary>Starts bow stance lockout: <paramref name="animationTimeMs"/> minus lag factor and <see cref="GetCappedPingVariance"/>, floored at zero.</summary>
    public void BeginBowStanceActionLockout(int animationTimeMs) {
        var durationMs = ComputeMinRequiredTimeMs(animationTimeMs);
        bowStanceDurationUntil = DateTimeOffset.UtcNow.AddMilliseconds(durationMs);
    }

    /// <summary>Clears bow stance lockout when the player takes an interrupting combat hit (<see cref="AttackType"/> other than <see cref="AttackType.NoInterrupt"/>).</summary>
    public void ClearBowStanceActionLockout() {
        bowStanceDurationUntil = null;
    }

    /// <summary>True while pickup or bow stance lockout blocks other player actions.</summary>
    public bool IsPickupOrBowStanceLockoutActive(DateTimeOffset nowUtc) {
        return IsPickupActionBlocking(nowUtc) || IsBowStanceActionBlocking(nowUtc);
    }

    /// <summary>Call when this player receives combat damage with an attack mode other than <see cref="Server.AttackType.NoInterrupt"/>.</summary>
    public void RegisterNonNoInterruptDamage() {
        interruptedCount++;
    }

    /// <summary>Starts or refreshes the interrupt stunlock window from a combat hit.</summary>
    public void RegisterCombatInterruptStunlock(int stunDurationMs) {
        if (stunDurationMs <= 0) {
            return;
        }

        stunlockDurationMs = stunDurationMs;
        combatInterruptStunlockUntil = DateTimeOffset.UtcNow.AddMilliseconds(stunDurationMs);
    }

    /// <summary>
    /// True when a movement request arrives before <c>stunlockDurationMs - stunlockDurationMs * antiHackTimingLagFactor - GetCappedPingVariance()</c> has elapsed since stunlock started.
    /// Clears stunlock state once <see cref="combatInterruptStunlockUntil"/> has passed.
    /// When returning <see langword="true"/>, <paramref name="cappedPingVariance"/> and <paramref name="requiredWaitMs"/> are set for logging.
    /// </summary>
    public bool IsMovementStunlockViolation(long nowUnixMs, out double cappedPingVariance, out double requiredWaitMs) {
        cappedPingVariance = 0;
        requiredWaitMs = 0;
        if (!combatInterruptStunlockUntil.HasValue) {
            return false;
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs);
        if (now >= combatInterruptStunlockUntil.Value) {
            combatInterruptStunlockUntil = null;
            stunlockDurationMs = 0;
            return false;
        }

        cappedPingVariance = GetCappedPingVariance();
        requiredWaitMs = ComputeMinRequiredTimeMs(stunlockDurationMs);
        if (requiredWaitMs <= 0) {
            return false;
        }

        var firstAllowedAt = combatInterruptStunlockUntil.Value.AddMilliseconds(-(cappedPingVariance + stunlockDurationMs * antiHackTimingLagFactor));
        return now < firstAllowedAt;
    }

    /// <summary>Milliseconds left until <see cref="combatInterruptStunlockUntil"/>; 0 when none or already elapsed.</summary>
    public int GetRemainingCombatStunlockMs(DateTimeOffset nowUtc) {
        if (!combatInterruptStunlockUntil.HasValue || nowUtc >= combatInterruptStunlockUntil.Value) {
            return 0;
        }

        var ms = (combatInterruptStunlockUntil.Value - nowUtc).TotalMilliseconds;
        return (int)Math.Max(0, Math.Min(int.MaxValue, ms));
    }

    /// <summary>
    /// Returns the ping delta (|delta - pingIntervalMs|) and updates last ping time.
    /// Returns null on the first ping when no previous time exists.
    /// </summary>
    public long? GetPingDeltaAndUpdateLastPingMs(int pingIntervalMs) {
        return playerPingTracker.RecordPingAndGetDelta(pingIntervalMs);
    }

    public long GetAndUpdateLastMovementRequestMs() {
        var currentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var delta = lastMovementRequestMs == 0 ? 0 : currentMs - lastMovementRequestMs;
        lastMovementRequestMs = currentMs;
        return delta;
    }

    /// <summary>
    /// Returns false when <paramref name="deltaMs"/> is below the allowed minimum cadence too often (sliding window); caller applies paralysis.
    /// Otherwise returns true (accepted cadence, or forgiven within window).
    /// </summary>
    public bool CheckMovementSpeedViolation(long deltaMs) {
        var minRequiredMs = GetMovementCadenceMinRequiredMs();
        if (deltaMs >= minRequiredMs) {
            return true;
        }

        using var lease = movementSpeedViolations.AttemptAcquire(1);
        if (lease.IsAcquired) {
            if (movementSpeedViolationCheckConfig.Verbose) {
                Console.WriteLine(
                    $"[GameWorldPlayer:{PlayerId}] Movement speed violation forgiven (sliding window). " +
                    $"deltaMs={deltaMs} minRequiredMs={minRequiredMs:F1} effectiveMovementMs={MovementSpeedMs} runningMode={runningMode} baseMovementMs={movementSpeedMs}");
            }
            return true;
        }

        if (movementSpeedViolationCheckConfig.Verbose) {
            Console.WriteLine(
                $"[GameWorldPlayer:{PlayerId}] Movement speed violation limit exhausted → paralysis. " +
                $"deltaMs={deltaMs} minRequiredMs={minRequiredMs:F1} effectiveMovementMs={MovementSpeedMs} runningMode={runningMode} baseMovementMs={movementSpeedMs}");
        }

        return false;
    }

    /// <summary>
    /// If enough wall-clock time has passed since the last successful melee damage delivery (vs <see cref="AttackSpeedMs"/> minus lag factor and capped ping variance),
    /// records <paramref name="nowMs"/> as the new delivery time and returns <see langword="true"/>. Otherwise returns <see langword="false"/> without updating state.
    /// </summary>
    public bool TryRecordPlayerAttackDamageDelivery(long nowMs, out double minIntervalMs, out double elapsedSinceLastDeliveryMs) {
        minIntervalMs = ComputeMinRequiredTimeMs(AttackSpeedMs);
        elapsedSinceLastDeliveryMs = 0;
        if (lastPlayerAttackDamageDeliveredMs.HasValue) {
            elapsedSinceLastDeliveryMs = nowMs - lastPlayerAttackDamageDeliveredMs.Value;
            if (elapsedSinceLastDeliveryMs < minIntervalMs) {
                return false;
            }
        }

        lastPlayerAttackDamageDeliveredMs = nowMs;
        return true;
    }

    /// <summary>Clears the last recorded player attack damage delivery so the next regular attack does not inherit cadence timing from a dash hit.</summary>
    public void ClearLastPlayerAttackDamageDeliveryTime() {
        lastPlayerAttackDamageDeliveredMs = null;
    }

    public void Send(ServerMessage message) {
        ArgumentNullException.ThrowIfNull(message);
        if (Disconnected || sendMessage is null) {
            return;
        }

        sendMessage(message);
    }

    public bool IsPlayerInRange(long playerId) {
        return playersInRange.Contains(playerId);
    }

    public bool AddPlayerInRange(long playerId) {
        return playersInRange.Add(playerId);
    }

    public bool RemovePlayerInRange(long playerId) {
        return playersInRange.Remove(playerId);
    }

    public void ReplacePlayersInRange(IEnumerable<long> playerIds) {
        ArgumentNullException.ThrowIfNull(playerIds);

        playersInRange.Clear();
        foreach (var playerId in playerIds) {
            playersInRange.Add(playerId);
        }
    }

    public void ClearPlayersInRange() {
        playersInRange.Clear();
    }

    public bool IsMonsterInRange(long monsterId) {
        return monstersInRange.Contains(monsterId);
    }

    public bool AddMonsterInRange(long monsterId) {
        return monstersInRange.Add(monsterId);
    }

    public bool RemoveMonsterInRange(long monsterId) {
        return monstersInRange.Remove(monsterId);
    }

    public void ReplaceMonstersInRange(IEnumerable<long> monsterIds) {
        ArgumentNullException.ThrowIfNull(monsterIds);

        monstersInRange.Clear();
        foreach (var monsterId in monsterIds) {
            monstersInRange.Add(monsterId);
        }
    }

    public void ClearMonstersInRange() {
        monstersInRange.Clear();
    }

    public bool IsNpcInRange(long npcId) {
        return npcsInRange.Contains(npcId);
    }

    public bool AddNpcInRange(long npcId) {
        return npcsInRange.Add(npcId);
    }

    public bool RemoveNpcInRange(long npcId) {
        return npcsInRange.Remove(npcId);
    }

    public void ReplaceNpcsInRange(IEnumerable<long> npcIds) {
        ArgumentNullException.ThrowIfNull(npcIds);

        npcsInRange.Clear();
        foreach (var npcId in npcIds) {
            npcsInRange.Add(npcId);
        }
    }

    public void ClearNpcsInRange() {
        npcsInRange.Clear();
    }

    public bool IsGroundEffectInRange(long groundEffectId) {
        return groundEffectsInRange.Contains(groundEffectId);
    }

    public bool AddGroundEffectInRange(long groundEffectId) {
        return groundEffectsInRange.Add(groundEffectId);
    }

    public bool RemoveGroundEffectInRange(long groundEffectId) {
        return groundEffectsInRange.Remove(groundEffectId);
    }

    public void ReplaceGroundEffectsInRange(IEnumerable<long> groundEffectIds) {
        ArgumentNullException.ThrowIfNull(groundEffectIds);

        groundEffectsInRange.Clear();
        foreach (var groundEffectId in groundEffectIds) {
            groundEffectsInRange.Add(groundEffectId);
        }
    }

    public void ClearGroundEffectsInRange() {
        groundEffectsInRange.Clear();
    }

    public bool IsGroundItemInRange(long groundItemUid) {
        return groundItemsInRange.Contains(groundItemUid);
    }

    public bool AddGroundItemInRange(long groundItemUid) {
        return groundItemsInRange.Add(groundItemUid);
    }

    public bool RemoveGroundItemInRange(long groundItemUid) {
        return groundItemsInRange.Remove(groundItemUid);
    }

    public void ReplaceGroundItemsInRange(IEnumerable<long> groundItemUids) {
        ArgumentNullException.ThrowIfNull(groundItemUids);

        groundItemsInRange.Clear();
        foreach (var groundItemUid in groundItemUids) {
            groundItemsInRange.Add(groundItemUid);
        }
    }

    public void ClearGroundItemsInRange() {
        groundItemsInRange.Clear();
    }

    protected override int GetEffectiveMovementSpeedMsForBroadcast() => MovementSpeedMs;

    protected override int GetEffectiveAttackSpeedMsForBroadcast() => AttackSpeedMs;

    protected override int? GetEffectiveCastSpeedMsForBroadcast() => CastSpeedMs;

    /// <summary>Fills <see cref="PlayerEnteredRange.ActiveTemporaryEffects"/> for visibility snapshots.</summary>
    public void FillActiveTemporaryEffects(PlayerEnteredRange snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        CopyActiveTemporaryEffectTypesTo(snapshot.ActiveTemporaryEffects);
    }

    public void RequestDisconnect(string? message = null) {
        if (Disconnected || requestDisconnect is null) {
            return;
        }

        requestDisconnect(message);
    }

    public void RequestWorldChange(WorldTransferDestination destination) {
        if (Disconnected) {
            return;
        }

        requestWorldChange(destination);
    }

    /// <summary>Clears per-connection timing/ping state after reconnect or detach.</summary>
    private void ResetConnectionState() {
        lastMovementRequestMs = 0;
        lastPlayerAttackDamageDeliveredMs = null;
        playerPingTracker.Reset();
        combatInterruptStunlockUntil = null;
        stunlockDurationMs = 0;
        interruptedCount = 0;
        pickupDurationUntil = null;
        bowStanceDurationUntil = null;
        lastSpellCastStartMs = null;
    }
}
