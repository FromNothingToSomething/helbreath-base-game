import { useStore } from '@tanstack/react-store';
import type { CSSProperties } from 'react';
import { HeadlessDraggableDialog } from './HeadlessDraggableDialog';
import { RpgButton } from '../components/RpgButton';
import { nocStore, toggleNocPanel } from '../store/Noc.store';

interface NocHudDialogProps {
    position: { x: number; y: number };
    zIndex?: number;
    onBringToFront?: () => void;
    /** Development builds only: asks the server for XP (honoured when Settings.json nocthemar.debug is true). */
    onDebugGrantXp?: () => void;
}

const BAR_WIDTH = 220;

const barTrack: CSSProperties = {
    position: 'relative',
    width: BAR_WIDTH,
    height: 14,
    border: '1px solid var(--rpg-leather)',
    background: 'rgba(0, 0, 0, 0.55)',
};

function Bar({ ratio, color, label }: { ratio: number; color: string; label: string }) {
    return (
        <div style={barTrack}>
            <div style={{ width: `${Math.round(ratio * 100)}%`, height: '100%', background: color }} />
            <span
                style={{
                    position: 'absolute',
                    inset: 0,
                    textAlign: 'center',
                    fontSize: 11,
                    lineHeight: '14px',
                    color: 'var(--rpg-parchment)',
                    textShadow: '1px 1px 2px var(--rpg-shadow)',
                }}
            >
                {label}
            </span>
        </div>
    );
}

/** Minimal level / XP / mana HUD fed by Noc.store (server-authoritative). Fixed position, not draggable. */
export function NocHudDialog({ position, zIndex, onBringToFront, onDebugGrantXp }: NocHudDialogProps) {
    const view = useStore(nocStore, (state) => state.view);
    const hasCatalog = useStore(nocStore, (state) => state.catalog !== null);
    if (!view) {
        return null;
    }

    const xpLabel = view.atMaxLevel ? 'MAX' : `${view.xp} / ${view.xpToNext} XP`;

    return (
        <HeadlessDraggableDialog
            position={position}
            id="noc-hud-dialog"
            disableDrag
            zIndex={zIndex}
            onBringToFront={onBringToFront}
            onContextMenu={(e) => e.preventDefault()}
        >
            <div
                style={{
                    color: 'var(--rpg-parchment)',
                    fontFamily: 'Georgia, serif',
                    fontSize: 13,
                    padding: '6px 8px',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 4,
                    background: 'rgba(30, 20, 10, 0.75)',
                    border: '2px solid var(--rpg-leather)',
                }}
            >
                <div style={{ fontWeight: 'bold' }}>Lv {view.level} · {view.raceName}</div>
                <Bar ratio={view.xpRatio} color="var(--rpg-gold-dark)" label={xpLabel} />
                <Bar ratio={view.manaRatio} color="#2f5fbf" label={`${view.mana} / ${view.maxMana} MP`} />
                {hasCatalog && (
                    <RpgButton variant="compact" onClick={toggleNocPanel}>
                        {view.classId === null ? 'Choose a class' : view.skillPoints > 0 ? `Skills (${view.skillPoints})` : 'Skills'}
                    </RpgButton>
                )}
                {onDebugGrantXp && <RpgButton onClick={onDebugGrantXp}>+100 XP (debug)</RpgButton>}
            </div>
        </HeadlessDraggableDialog>
    );
}
