import { useStore } from '@tanstack/react-store';
import type { CSSProperties } from 'react';
import { HeadlessDraggableDialog } from './HeadlessDraggableDialog';
import { nocStore } from '../store/Noc.store';
import { MIMIC_PURPLE, MIMIC_PURPLE_DARK, MIMIC_PURPLE_LIGHT, MIMIC_RARITY_COLOR } from '../mimicTheme';
import type { CatalogModel, MimicView } from '../../utils/NocCatalogView';
import { MIMIC_KIND_LABEL, MIMIC_RARITY_LABEL, buildSlots, formatCooldown, type SlotView } from '../../utils/MimicView';
import type { NocViewModel } from '../../utils/NocView';

interface MimicBarDialogProps {
    position: { x: number; y: number };
    zIndex?: number;
    /** Starts the cast of an equipped, castable ability. */
    onCast: (abilityId: number) => void;
    /** Takes an ability off the bar (right click on its slot). */
    onUnequip: (abilityId: number) => void;
}

const SLOT_SIZE = 44;

const barStyle: CSSProperties = {
    display: 'flex',
    gap: 4,
    padding: 5,
    background: MIMIC_PURPLE_DARK,
    border: `2px solid ${MIMIC_PURPLE}`,
    boxShadow: '0 0 8px rgba(123, 79, 191, 0.55)',
};

const slotBase: CSSProperties = {
    width: SLOT_SIZE,
    height: SLOT_SIZE,
    boxSizing: 'border-box',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    fontFamily: 'Georgia, serif',
    fontSize: 11,
    lineHeight: 1.1,
    padding: 0,
    color: 'var(--rpg-parchment)',
};

function abilityTitle(ability: MimicView, level: number): string {
    const lines = [
        `${ability.name} (level ${level})`,
        `${MIMIC_KIND_LABEL[ability.kind]} · ${MIMIC_RARITY_LABEL[ability.rarity]}`,
        `Mana ${ability.manaCost} · cooldown ${formatCooldown(ability.cooldownMs)}`,
        ability.desc,
    ];
    if (!ability.ready) {
        lines.push('Not castable yet.');
    }
    lines.push('Right click: take off the bar.');
    return lines.join('\n');
}

function Slot({ slot, catalog, view, onCast, onUnequip }: { slot: SlotView; catalog: CatalogModel; view: NocViewModel; onCast: (id: number) => void; onUnequip: (id: number) => void }) {
    if (slot.state === 'locked') {
        return (
            <div style={{ ...slotBase, border: `1px solid ${MIMIC_PURPLE}`, opacity: 0.4 }} title={slot.unlockLevel === null ? 'Locked' : `Opens at level ${slot.unlockLevel}`}>
                {slot.unlockLevel === null ? '-' : `Lv ${slot.unlockLevel}`}
            </div>
        );
    }
    if (slot.state === 'free' || slot.abilityId === null) {
        return (
            <div style={{ ...slotBase, border: `1px dashed ${MIMIC_PURPLE_LIGHT}`, opacity: 0.75 }} title="Free slot: equip an ability from the Soul Forge panel.">
                -
            </div>
        );
    }

    const ability = catalog.mimicsById.get(slot.abilityId);
    const level = view.mimics.find((m) => m.id === slot.abilityId)?.level ?? 1;
    if (!ability) {
        return <div style={{ ...slotBase, border: `1px solid ${MIMIC_PURPLE}` }}>{`#${slot.abilityId}`}</div>;
    }

    return (
        <button
            type="button"
            style={{
                ...slotBase,
                cursor: ability.ready ? 'pointer' : 'not-allowed',
                background: 'rgba(123, 79, 191, 0.3)',
                border: `2px solid ${MIMIC_RARITY_COLOR[ability.rarity]}`,
                opacity: ability.ready ? 1 : 0.55,
            }}
            title={abilityTitle(ability, level)}
            onPointerDown={(e) => e.stopPropagation()}
            onClick={() => {
                if (ability.ready) {
                    onCast(ability.id);
                }
            }}
            onContextMenu={(e) => {
                e.preventDefault();
                e.stopPropagation();
                onUnequip(ability.id);
            }}
        >
            <span style={{ fontWeight: 'bold' }}>{ability.name.slice(0, 7)}</span>
            <span style={{ opacity: 0.8 }}>{`Lv ${level}`}</span>
        </button>
    );
}

/** Mimicry bar (violet): one slot per bar place. A filled slot casts its ability (the same cast flow as a spell), right click takes it off; locked slots show the character level that opens them. */
export function MimicBarDialog({ position, zIndex, onCast, onUnequip }: MimicBarDialogProps) {
    const view = useStore(nocStore, (state) => state.view);
    const catalog = useStore(nocStore, (state) => state.catalog);
    const refusal = useStore(nocStore, (state) => state.refusal);
    if (!view || !catalog || !catalog.mimicRules) {
        return null;
    }

    const slots = buildSlots(catalog.mimicRules, view);
    return (
        <HeadlessDraggableDialog position={position} id="mimic-bar-dialog" disableDrag zIndex={zIndex} onContextMenu={(e) => e.preventDefault()}>
            {refusal && <div style={{ color: '#e07a5f', fontFamily: 'Georgia, serif', fontSize: 12, textShadow: '1px 1px 2px #000', marginBottom: 2 }}>{refusal}</div>}
            <div style={barStyle}>
                {slots.map((slot) => (
                    <Slot key={slot.index} slot={slot} catalog={catalog} view={view} onCast={onCast} onUnequip={onUnequip} />
                ))}
            </div>
        </HeadlessDraggableDialog>
    );
}
