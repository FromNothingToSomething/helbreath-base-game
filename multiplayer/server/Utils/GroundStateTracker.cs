using System.Collections.Generic;
using Server;

namespace Server.Utils;

/// <summary>
/// One active immobile ground effect on the map: fixed cell, stable id for client visibility updates,
/// and either periodic damage ticks or a step-on-only lifetime.
/// </summary>
public sealed class GroundEffectState {
    private readonly int durationMs;
    private int ticksLeft;

    /// <summary>Coarse scheduler id for the next pending callback (periodic tick or expiry).</summary>
    public int ScheduledTickId { get; set; }

    public Guid GroundEffectGuid { get; }
    public long GroundEffectId { get; }
    /// <summary>Spell catalog id; used server-side for on-damage <see cref="SpellConfig.TemporaryEffects"/> when a tick or step delivers damage.</summary>
    public int SpellId { get; }
    public GroundEffectType EffectType { get; }
    public long CasterPlayerId { get; }
    public int PosX { get; }
    public int PosY { get; }
    public int Group { get; }
    public int? TickRateMs { get; }
    public int DurationMs => durationMs;
    public int DamagePerTick { get; }
    /// <summary>Resolved spell hit mode for each tick (ground effects never use <see cref="AttackType.Knockback"/>; that value is mapped to <see cref="AttackType.Stun"/> at creation).</summary>
    public AttackType SpellAttackType { get; }
    public bool HasPeriodicDamage => TickRateMs.HasValue;
    public int TicksLeft => ticksLeft;
    public int RemainingDurationMs => HasPeriodicDamage ? ticksLeft * TickRateMs!.Value : durationMs;

    public GroundEffectState(
        Guid groundEffectGuid,
        int spellId,
        GroundEffectType effectType,
        long casterPlayerId,
        int posX,
        int posY,
        int group,
        int? tickRateMs,
        int durationMs,
        int damagePerTick,
        AttackType spellAttackType) {
        if (damagePerTick <= 0) {
            throw new ArgumentOutOfRangeException(nameof(damagePerTick), "Damage per tick must be positive.");
        }
        if (durationMs <= 0) {
            throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be positive.");
        }
        if (group < 0) {
            throw new ArgumentOutOfRangeException(nameof(group), "Group must be non-negative.");
        }
        if (tickRateMs.HasValue) {
            if (tickRateMs.Value <= 0) {
                throw new ArgumentOutOfRangeException(nameof(tickRateMs), "Tick rate must be positive.");
            }
            if (durationMs % tickRateMs.Value != 0) {
                throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be divisible by tick rate.");
            }
            ticksLeft = durationMs / tickRateMs.Value;
        }

        GroundEffectGuid = groundEffectGuid;
        GroundEffectId = BitConverter.ToInt64(groundEffectGuid.ToByteArray(), 0);
        SpellId = spellId;
        EffectType = effectType;
        CasterPlayerId = casterPlayerId;
        PosX = posX;
        PosY = posY;
        Group = group;
        TickRateMs = tickRateMs;
        this.durationMs = durationMs;
        DamagePerTick = damagePerTick;
        SpellAttackType = spellAttackType;
    }

    public bool TryConsumeTick() {
        if (!HasPeriodicDamage) {
            return false;
        }
        if (ticksLeft <= 0) {
            return false;
        }

        ticksLeft--;
        return ticksLeft > 0;
    }
}

/// <summary>One dropped inventory item stack entry on the map; only the newest entry on a cell is visible to clients.</summary>
public sealed class GroundItemState {
    public int ItemId { get; }
    public long ItemUid { get; }
    public int Quantity { get; }
    public ItemEffectConfig[]? EffectOverrides { get; }
    public int PosX { get; }
    public int PosY { get; }

    public GroundItemState(int itemId, long itemUid, int quantity, ItemEffectConfig[]? effectOverrides, int posX, int posY) {
        if (quantity <= 0) {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Ground item quantity must be positive.");
        }

        ItemId = itemId;
        ItemUid = itemUid;
        Quantity = quantity;
        EffectOverrides = CloneEffectOverrides(effectOverrides);
        PosX = posX;
        PosY = posY;
    }

    public static GroundItemState FromInventoryItem(InventoryItemState item, int posX, int posY) {
        ArgumentNullException.ThrowIfNull(item);
        return new GroundItemState(item.ItemId, item.ItemUid, item.Quantity, item.EffectOverrides, posX, posY);
    }

    private static ItemEffectConfig[]? CloneEffectOverrides(ItemEffectConfig[]? effectOverrides) {
        if (effectOverrides is null || effectOverrides.Length == 0) {
            return null;
        }

        var copy = new ItemEffectConfig[effectOverrides.Length];
        Array.Copy(effectOverrides, copy, effectOverrides.Length);
        return copy;
    }
}

/// <summary>Static cell with one or more active ground effects or dropped items, indexed through <see cref="GroundStatesSpatialGrid"/> for view-range queries.</summary>
public sealed class GroundStateCell {
    public int X { get; }
    public int Y { get; }

    public GroundStateCell(int x, int y) {
        X = x;
        Y = y;
    }
}

/// <summary>Spatial index of non-empty ground-state cells sized to the player view radius.</summary>
public sealed class GroundStatesSpatialGrid {
    private readonly SpatialGrid<GroundStateCell> grid;

    public GroundStatesSpatialGrid(int viewRadiusX, int viewRadiusY) {
        if (viewRadiusX < 0) {
            throw new ArgumentOutOfRangeException(nameof(viewRadiusX), "View radius X must be non-negative.");
        }
        if (viewRadiusY < 0) {
            throw new ArgumentOutOfRangeException(nameof(viewRadiusY), "View radius Y must be non-negative.");
        }

        grid = new SpatialGrid<GroundStateCell>(
            Math.Max(1, viewRadiusX + 1),
            Math.Max(1, viewRadiusY + 1));
    }

    public void Add(GroundStateCell cell) {
        ArgumentNullException.ThrowIfNull(cell);
        grid.Add(cell, cell.X, cell.Y);
    }

    public bool Remove(GroundStateCell cell) {
        ArgumentNullException.ThrowIfNull(cell);
        return grid.Remove(cell);
    }

    /// <summary>Iterates non-empty ground-state cells in the inclusive world-coordinate rectangle.</summary>
    /// <remarks>Returns a struct enumerable so <c>foreach</c> does not allocate an iterator object on the heap.</remarks>
    public CellsInRectangleEnumerable GetCellsInRectangle(int minX, int minY, int maxX, int maxY) {
        return new CellsInRectangleEnumerable(grid.QueryRectangle(minX, minY, maxX, maxY), minX, minY, maxX, maxY);
    }

    public readonly struct CellsInRectangleEnumerable {
        private readonly SpatialGrid<GroundStateCell>.RectangleEnumerable inner;
        private readonly int minX;
        private readonly int minY;
        private readonly int maxX;
        private readonly int maxY;

        internal CellsInRectangleEnumerable(
            SpatialGrid<GroundStateCell>.RectangleEnumerable inner,
            int minX,
            int minY,
            int maxX,
            int maxY) {
            this.inner = inner;
            this.minX = minX;
            this.minY = minY;
            this.maxX = maxX;
            this.maxY = maxY;
        }

        public CellsInRectangleEnumerator GetEnumerator() {
            return new CellsInRectangleEnumerator(inner.GetEnumerator(), minX, minY, maxX, maxY);
        }
    }

    public struct CellsInRectangleEnumerator {
        private SpatialGrid<GroundStateCell>.RectangleEnumerator inner;
        private readonly int minX;
        private readonly int minY;
        private readonly int maxX;
        private readonly int maxY;
        private GroundStateCell? current;

        internal CellsInRectangleEnumerator(
            SpatialGrid<GroundStateCell>.RectangleEnumerator inner,
            int minX,
            int minY,
            int maxX,
            int maxY) {
            this.inner = inner;
            this.minX = minX;
            this.minY = minY;
            this.maxX = maxX;
            this.maxY = maxY;
            current = null;
        }

        public GroundStateCell Current => current!;

        public bool MoveNext() {
            while (inner.MoveNext()) {
                var cell = inner.Current;
                if (cell.X < minX || cell.X > maxX || cell.Y < minY || cell.Y > maxY) {
                    continue;
                }
                current = cell;
                return true;
            }
            current = null;
            return false;
        }
    }
}

/// <summary>
/// Map-sized owner of long-lived ground effects plus dropped-item stacks. Effects keep their existing scheduler-driven
/// lifecycle, while each cell may also host a bounded LIFO item stack whose newest item is the visible top.
/// </summary>
public sealed class GroundStateTracker {
    private readonly int sizeX;
    private readonly int sizeY;
    private readonly int maxDroppedItemsInStack;
    private readonly Scheduler scheduler;
    private readonly Action<GroundEffectState> onTickDue;
    private readonly Action<GroundEffectState> onExpired;
    private readonly List<GroundEffectState>?[] effectsByCell;
    private readonly RingBuffer<GroundItemState>?[] droppedItemsByCell;
    private readonly GroundStateCell?[] activeCellsByIndex;
    private readonly Dictionary<long, GroundEffectState> activeEffectsById = new();
    private readonly Dictionary<long, GroundItemState> activeTopGroundItemsById = new();
    private readonly GroundStatesSpatialGrid spatialGrid;

    public GroundStateTracker(
        int sizeX,
        int sizeY,
        int viewRadiusX,
        int viewRadiusY,
        int maxDroppedItemsInStack,
        Scheduler scheduler,
        Action<GroundEffectState> onTickDue,
        Action<GroundEffectState> onExpired) {
        if (sizeX <= 0) {
            throw new ArgumentOutOfRangeException(nameof(sizeX), "Map width must be positive.");
        }
        if (sizeY <= 0) {
            throw new ArgumentOutOfRangeException(nameof(sizeY), "Map height must be positive.");
        }
        if (maxDroppedItemsInStack <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxDroppedItemsInStack), "Max dropped items in stack must be positive.");
        }

        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(onTickDue);
        ArgumentNullException.ThrowIfNull(onExpired);

        this.sizeX = sizeX;
        this.sizeY = sizeY;
        this.maxDroppedItemsInStack = maxDroppedItemsInStack;
        this.scheduler = scheduler;
        this.onTickDue = onTickDue;
        this.onExpired = onExpired;
        effectsByCell = new List<GroundEffectState>?[sizeX * sizeY];
        droppedItemsByCell = new RingBuffer<GroundItemState>?[sizeX * sizeY];
        activeCellsByIndex = new GroundStateCell?[sizeX * sizeY];
        spatialGrid = new GroundStatesSpatialGrid(viewRadiusX, viewRadiusY);
    }

    public bool TryAddEffect(
        int spellId,
        GroundEffectType effectType,
        long casterPlayerId,
        int posX,
        int posY,
        int group,
        int? tickRateMs,
        int durationMs,
        int damagePerTick,
        AttackType spellAttackType,
        out GroundEffectState? effect) {
        effect = null;
        if (!IsWithinBounds(posX, posY)) {
            return false;
        }

        var index = GetIndex(posX, posY);
        var cellEffects = effectsByCell[index];
        if (cellEffects is not null) {
            foreach (var existingEffect in cellEffects) {
                if (existingEffect.Group == group) {
                    return false;
                }
            }
        } else {
            cellEffects = new List<GroundEffectState>(1);
            effectsByCell[index] = cellEffects;
        }

        effect = new GroundEffectState(
            Guid.NewGuid(),
            spellId,
            effectType,
            casterPlayerId,
            posX,
            posY,
            group,
            tickRateMs,
            durationMs,
            damagePerTick,
            spellAttackType);
        cellEffects.Add(effect);
        activeEffectsById[effect.GroundEffectId] = effect;
        EnsureCellTracked(index, posX, posY);
        if (effect.HasPeriodicDamage) {
            ScheduleNextTick(effect);
        } else {
            ScheduleExpiration(effect);
        }
        return true;
    }

    public bool TryAddDroppedItem(InventoryItemState item, int posX, int posY, out GroundItemState? previousTopItem, out GroundItemState? addedItem) {
        ArgumentNullException.ThrowIfNull(item);
        previousTopItem = null;
        addedItem = null;
        if (!IsWithinBounds(posX, posY)) {
            return false;
        }

        var index = GetIndex(posX, posY);
        var stack = droppedItemsByCell[index];
        if (stack is null) {
            stack = new RingBuffer<GroundItemState>(maxDroppedItemsInStack);
            droppedItemsByCell[index] = stack;
        }

        if (stack.TryPeekNewest(out var currentTopItem)) {
            previousTopItem = currentTopItem;
            activeTopGroundItemsById.Remove(currentTopItem.ItemUid);
        }

        addedItem = GroundItemState.FromInventoryItem(item, posX, posY);
        stack.Add(addedItem, out _);
        activeTopGroundItemsById[addedItem.ItemUid] = addedItem;
        EnsureCellTracked(index, posX, posY);
        return true;
    }

    /// <summary>The top-most item of a cell without removing it.</summary>
    public bool TryPeekTopDroppedItem(int posX, int posY, out GroundItemState? topItem) {
        topItem = null;
        if (!IsWithinBounds(posX, posY)) {
            return false;
        }

        var stack = droppedItemsByCell[GetIndex(posX, posY)];
        return stack is not null && stack.TryPeekNewest(out topItem);
    }

    public bool TryRemoveTopDroppedItem(int posX, int posY, out GroundItemState? removedItem, out GroundItemState? revealedTopItem) {
        removedItem = null;
        revealedTopItem = null;
        if (!IsWithinBounds(posX, posY)) {
            return false;
        }

        var index = GetIndex(posX, posY);
        var stack = droppedItemsByCell[index];
        if (stack is null || !stack.TryRemoveNewest(out var removed)) {
            return false;
        }

        removedItem = removed;
        activeTopGroundItemsById.Remove(removed.ItemUid);
        if (stack.TryPeekNewest(out var nextTop)) {
            revealedTopItem = nextTop;
            activeTopGroundItemsById[nextTop.ItemUid] = nextTop;
        } else {
            droppedItemsByCell[index] = null;
        }

        CleanupCellIfEmpty(index);
        return true;
    }

    public void FillEffectsInRectangle(int minX, int minY, int maxX, int maxY, Dictionary<long, GroundEffectState> destination) {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        if (minX > maxX || minY > maxY) {
            return;
        }

        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(sizeX - 1, maxX);
        maxY = Math.Min(sizeY - 1, maxY);
        foreach (var cell in spatialGrid.GetCellsInRectangle(minX, minY, maxX, maxY)) {
            var cellEffects = effectsByCell[GetIndex(cell.X, cell.Y)];
            if (cellEffects is null) {
                continue;
            }

            foreach (var effect in cellEffects) {
                destination[effect.GroundEffectId] = effect;
            }
        }
    }

    public void FillTopItemsInRectangle(int minX, int minY, int maxX, int maxY, Dictionary<long, GroundItemState> destination) {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        if (minX > maxX || minY > maxY) {
            return;
        }

        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(sizeX - 1, maxX);
        maxY = Math.Min(sizeY - 1, maxY);
        foreach (var cell in spatialGrid.GetCellsInRectangle(minX, minY, maxX, maxY)) {
            var items = droppedItemsByCell[GetIndex(cell.X, cell.Y)];
            if (items is null || !items.TryPeekNewest(out var topItem)) {
                continue;
            }

            destination[topItem.ItemUid] = topItem;
        }
    }

    public bool TryGetEffect(long groundEffectId, out GroundEffectState effect) {
        return activeEffectsById.TryGetValue(groundEffectId, out effect!);
    }

    public bool TryGetTopGroundItem(long itemUid, out GroundItemState item) {
        return activeTopGroundItemsById.TryGetValue(itemUid, out item!);
    }

    public bool TryGetEffectsAtCell(int posX, int posY, out IReadOnlyList<GroundEffectState>? effects) {
        if (!IsWithinBounds(posX, posY)) {
            effects = null;
            return false;
        }

        var cellEffects = effectsByCell[GetIndex(posX, posY)];
        if (cellEffects is null || cellEffects.Count == 0) {
            effects = null;
            return false;
        }

        effects = cellEffects;
        return true;
    }

    public bool TryGetTopGroundItemAtCell(int posX, int posY, out GroundItemState item) {
        if (!IsWithinBounds(posX, posY)) {
            item = default!;
            return false;
        }

        var stack = droppedItemsByCell[GetIndex(posX, posY)];
        if (stack is null) {
            item = default!;
            return false;
        }

        return stack.TryPeekNewest(out item);
    }

    private void ScheduleNextTick(GroundEffectState effect) {
        effect.ScheduledTickId = scheduler.SetTimeout(effect.TickRateMs!.Value, () => HandleTick(effect.GroundEffectId));
    }

    private void ScheduleExpiration(GroundEffectState effect) {
        effect.ScheduledTickId = scheduler.SetTimeout(effect.DurationMs, () => HandleExpire(effect.GroundEffectId));
    }

    private void HandleTick(long groundEffectId) {
        if (!activeEffectsById.TryGetValue(groundEffectId, out var effect)) {
            return;
        }

        onTickDue(effect);
        if (effect.TryConsumeTick()) {
            ScheduleNextTick(effect);
            return;
        }

        ExpireEffect(effect);
    }

    private void HandleExpire(long groundEffectId) {
        if (!activeEffectsById.TryGetValue(groundEffectId, out var effect)) {
            return;
        }

        ExpireEffect(effect);
    }

    private void ExpireEffect(GroundEffectState effect) {
        var index = GetIndex(effect.PosX, effect.PosY);
        var cellEffects = effectsByCell[index];
        if (cellEffects is not null) {
            cellEffects.Remove(effect);
            if (cellEffects.Count == 0) {
                effectsByCell[index] = null;
            }
        }

        activeEffectsById.Remove(effect.GroundEffectId);
        CleanupCellIfEmpty(index);
        onExpired(effect);
    }

    private void EnsureCellTracked(int index, int posX, int posY) {
        if (activeCellsByIndex[index] is not null) {
            return;
        }

        var cell = new GroundStateCell(posX, posY);
        activeCellsByIndex[index] = cell;
        spatialGrid.Add(cell);
    }

    private void CleanupCellIfEmpty(int index) {
        if (effectsByCell[index] is not null || droppedItemsByCell[index] is not null) {
            return;
        }

        var activeCell = activeCellsByIndex[index];
        if (activeCell is null) {
            return;
        }

        spatialGrid.Remove(activeCell);
        activeCellsByIndex[index] = null;
    }

    private bool IsWithinBounds(int x, int y) {
        return x >= 0 && x < sizeX && y >= 0 && y < sizeY;
    }

    private int GetIndex(int x, int y) {
        return (y * sizeX) + x;
    }
}
