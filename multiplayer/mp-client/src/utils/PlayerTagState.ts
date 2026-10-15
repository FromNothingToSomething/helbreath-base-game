import { EventBus } from '../game/EventBus';
import { NOC_STATE_RESET, PLAYER_FACTION_CHANGED, PLAYER_TAG_OPTIONS_CHANGED } from '../constants/EventNames';

/** Display options of the tag above players (name, faction icon + color). Both on by default. */
export interface PlayerTagOptions {
    showNames: boolean;
    showFaction: boolean;
}

const STORAGE_KEY = 'nocthemar.playerTagOptions';
const DEFAULTS: PlayerTagOptions = { showNames: true, showFaction: true };

/** Parses a stored JSON string; anything unusable falls back to the defaults, field by field. */
export function parseTagOptions(raw: string | null): PlayerTagOptions {
    if (raw === null) {
        return { ...DEFAULTS };
    }
    try {
        const parsed: unknown = JSON.parse(raw);
        if (typeof parsed !== 'object' || parsed === null) {
            return { ...DEFAULTS };
        }
        const o = parsed as Record<string, unknown>;
        return {
            showNames: typeof o.showNames === 'boolean' ? o.showNames : DEFAULTS.showNames,
            showFaction: typeof o.showFaction === 'boolean' ? o.showFaction : DEFAULTS.showFaction,
        };
    } catch {
        return { ...DEFAULTS };
    }
}

function load(): PlayerTagOptions {
    try {
        return parseTagOptions(typeof localStorage === 'undefined' ? null : localStorage.getItem(STORAGE_KEY));
    } catch {
        return { ...DEFAULTS };
    }
}

let options: PlayerTagOptions = load();
let selfFactionId: number | null = null;

export function getTagOptions(): PlayerTagOptions {
    return options;
}

export function setTagOptions(next: Partial<PlayerTagOptions>): void {
    options = { ...options, ...next };
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(options));
    } catch {
        // storage unavailable: the option only lasts for this session
    }
    EventBus.emit(PLAYER_TAG_OPTIONS_CHANGED, options);
}

/** Faction of the local player, kept here because the self Player may be created after the first snapshot. */
export function getSelfFactionId(): number | null {
    return selfFactionId;
}

EventBus.on(PLAYER_FACTION_CHANGED, (data: { factionId: number | null; self: boolean }) => {
    if (data.self) {
        selfFactionId = data.factionId;
    }
});
EventBus.on(NOC_STATE_RESET, () => {
    selfFactionId = null;
});
