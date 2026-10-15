import { useStore } from '@tanstack/react-store';
import type { CSSProperties } from 'react';
import { HeadlessDraggableDialog } from './HeadlessDraggableDialog';
import { RpgButton } from '../components/RpgButton';
import { nocStore, toggleNocPanel } from '../store/Noc.store';
import { toggleMimicPanel } from '../store/Mimic.store';
import { MIMIC_PURPLE_LIGHT } from '../mimicTheme';
import { toggleFactionPanel, toggleQuestLog } from '../store/Faction.store';
import { factionColor, factionColorLight } from '../factionTheme';
import { reputationOf } from '../../utils/FactionView';
import { ReputationBarView } from './FactionDialog';

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
    const factionCatalog = useStore(nocStore, (state) => (state.catalog?.reputationRules ? state.catalog : null));
    const hasMimicry = useStore(nocStore, (state) => (state.catalog?.mimics.length ?? 0) > 0);
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
                {factionCatalog && view.factionId !== null && factionCatalog.reputationRules && (
                    <div style={{ color: factionColorLight(view.factionId), fontSize: 11 }}>
                        {factionCatalog.factionsById.get(view.factionId)?.name ?? 'Faction'}
                        <ReputationBarView
                            rules={factionCatalog.reputationRules}
                            value={reputationOf(view.reputations, view.factionId)}
                            factionId={view.factionId}
                        />
                    </div>
                )}
                {factionCatalog && (
                    <RpgButton variant="compact" onClick={toggleFactionPanel} style={{ borderColor: factionColor(view.factionId) }}>
                        {view.factionId === null ? 'Faction' : 'Faction master'}
                    </RpgButton>
                )}
                {factionCatalog && view.factionId !== null && (
                    <RpgButton variant="compact" onClick={toggleQuestLog}>
                        {view.quests.length > 0 ? `Quests (${view.quests.length})` : 'Quests'}
                    </RpgButton>
                )}
                {hasMimicry && (
                    <RpgButton variant="compact" onClick={toggleMimicPanel} style={{ borderColor: MIMIC_PURPLE_LIGHT }}>
                        Mimicry
                    </RpgButton>
                )}
                {onDebugGrantXp && <RpgButton onClick={onDebugGrantXp}>+100 XP (debug)</RpgButton>}
            </div>
        </HeadlessDraggableDialog>
    );
}
