using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Net;
using Mmorpg.Network;

namespace Server.World.Game;

/// <summary>Base type for messages delivered to a single <see cref="GameWorld"/> mailbox.</summary>
public abstract record GameWorldMessage;

/// <summary>Persisted character settings and location captured from a <see cref="GameWorldPlayer"/> for disk saves, login restore, and world transfer handoff.</summary>
public sealed record PersistedInventoryItem(
    int ItemId,
    long ItemUid,
    int? BagX,
    int? BagY,
    int Quantity,
    int BagZIndex,
    ItemEffectConfig[]? EffectOverrides);

/// <summary>Persisted equipped item row keyed by server slot name (for example <c>weapon</c> or <c>ring-left</c>).</summary>
public sealed record PersistedEquippedItem(
    int ItemId,
    long ItemUid,
    int? BagX,
    int? BagY,
    ItemEffectConfig[]? EffectOverrides);

/// <summary>Persisted equipped item row keyed by server slot name (for example <c>weapon</c> or <c>ring-left</c>); omits bag-only runtime fields like quantity and z-order.</summary>
public sealed record PersistedEquippedInventoryItem(
    string Slot,
    PersistedEquippedItem Item);

/// <summary>Persisted character settings and location captured from a <see cref="GameWorldPlayer"/> for disk saves, login restore, and world transfer handoff. Optional <c>FacingDirection</c> is grid facing 0–7; absent in older JSON files.</summary>
public sealed record PlayerPersistenceState(
    string GameWorldId,
    int X,
    int Y,
    int MovementSpeedMs,
    int CastSpeedMs,
    int AttackSpeedMs,
    int AttackRange,
    int Damage,
    int StunDuration,
    int AttackType,
    bool AttackMode,
    bool RunMode,
    bool AllowDashAttack,
    /// <summary>0 = male, 1 = female; matches <see cref="Mmorpg.Network.PlayerGender"/>.</summary>
    int GenderValue = 0,
    /// <summary>0 = light, 1 = tanned, 2 = dark; matches <see cref="Mmorpg.Network.PlayerSkinColor"/>.</summary>
    int SkinColorValue = 0,
    /// <summary>Hair style index 0–7 (client Style 1–8).</summary>
    int HairStyleIndex = 0,
    /// <summary>Underwear palette index 0–7.</summary>
    int UnderwearColorIndex = 0,
    int? FacingDirection = null,
    PersistedInventoryItem[]? BagItems = null,
    PersistedEquippedInventoryItem[]? EquippedItems = null,
    string CharacterName = "",
    /// <summary>Save format version; absent in pre-versioning files, which deserialize to <see cref="CharacterSchema.Legacy"/>. Stamped by <see cref="ICharacterStore"/> on save.</summary>
    int SchemaVersion = CharacterSchema.Legacy,
    /// <summary>NOCTHEMAR block; null for legacy saves and means "create defaults at login".</summary>
    NocState? Noc = null);

/// <summary>State carried across worlds during a transfer: session identity plus the player settings snapshot to reapply in the target world.</summary>
public sealed record TransferredPlayerState(Guid SessionId, PlayerPersistenceState State);

/// <summary>Authoritative destination chosen by the source world; spawn coordinates are optional for non-teleport transfers.</summary>
public sealed record WorldTransferDestination(string WorldId, int? SpawnX, int? SpawnY);

/// <summary>First-time join: session id plus outbound hooks installed on the WebSocket connection.</summary>
public sealed record PlayerConnectedMessage(
    Guid SessionId,
    Action<ServerMessage> SendMessage,
    Action<string?> RequestDisconnect,
    Action<WorldTransferDestination> RequestWorldChange,
    PlayerPersistenceState? PersistedState,
    string CharacterName,
    /// <summary>Clears pending logout and notifies client when combat damage cancels a timed logout.</summary>
    Action InterruptLogoutDueToCombat,
    /// <summary>T30: validated creation choice of a character that has no save; null otherwise.</summary>
    NewCharacterChoice? NewCharacter = null) : GameWorldMessage;

/// <summary>Existing in-world player attached a new socket after disconnect grace.</summary>
public sealed record PlayerReconnectedMessage(
    Guid SessionId,
    Action<ServerMessage> SendMessage,
    Action<string?> RequestDisconnect,
    Action<WorldTransferDestination> RequestWorldChange,
    string CharacterName) : GameWorldMessage;

/// <summary>Socket closed; <paramref name="SessionRemainsActive"/> controls whether others still see a disconnected ghost in range.</summary>
public sealed record PlayerDisconnectedMessage(Guid SessionId, bool SessionRemainsActive) : GameWorldMessage;

/// <summary>Emitted by cleanup when the reconnect window expired—world should remove the player entity.</summary>
public sealed record RemoveDisconnectedPlayerMessage(Guid SessionId) : GameWorldMessage;

/// <summary>Gameplay packet from an authenticated client already bound to this world.</summary>
public sealed record ClientPacketMessage(Guid SessionId, ClientMessage Message) : GameWorldMessage;

/// <summary>Ask a world to snapshot the current player settings and location for immediate persistence in <c>Server.cs</c>.</summary>
public sealed record SavePlayerStateRequestMessage(
    Guid SessionId,
    TaskCompletionSource<PlayerPersistenceState?> Completion) : GameWorldMessage;

/// <summary>Ask source world to remove the player and signal <see cref="Completion"/> with transfer payload.</summary>
public sealed record TransferPlayerOutMessage(
    Guid SessionId,
    string TargetWorldId,
    TaskCompletionSource<TransferredPlayerState?> Completion) : GameWorldMessage;

/// <summary>Ask target world to spawn the player using preserved state and the same send/disconnect hooks.</summary>
public sealed record TransferPlayerInMessage(
    TransferredPlayerState Player,
    int? SpawnX,
    int? SpawnY,
    Action<ServerMessage> SendMessage,
    Action<string?> RequestDisconnect,
    Action<WorldTransferDestination> RequestWorldChange,
    Action InterruptLogoutDueToCombat) : GameWorldMessage;
