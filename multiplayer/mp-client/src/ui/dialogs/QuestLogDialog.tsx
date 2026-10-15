import { useStore } from '@tanstack/react-store';
import type { CSSProperties } from 'react';
import { DraggableDialog } from './DraggableDialog';
import { RpgButton } from '../components/RpgButton';
import { factionStore, setQuestLogOpen } from '../store/Faction.store';
import { nocStore } from '../store/Noc.store';
import { factionColorLight } from '../factionTheme';
import { objectiveText, questState } from '../../utils/QuestView';
import { tierOf } from '../../utils/FactionView';
import { questTargetName, rewardText } from './FactionDialog';

interface QuestLogDialogProps {
    position: { x: number; y: number };
    zIndex?: number;
    onAbandon: (questId: number) => void;
}

const panel: CSSProperties = {
    color: 'var(--rpg-parchment)',
    fontFamily: 'Georgia, serif',
    fontSize: 13,
    padding: '8px 10px',
    display: 'flex',
    flexDirection: 'column',
    gap: 8,
    background: 'rgba(30, 20, 10, 0.92)',
    width: 380,
    maxHeight: 420,
    overflowY: 'auto',
};

const small: CSSProperties = { fontSize: 11, opacity: 0.85 };

/** Active quests with progress; abandoning works anywhere, turning in needs the faction master (Faction master panel). */
export function QuestLogDialog({ position, zIndex, onAbandon }: QuestLogDialogProps) {
    const isOpen = useStore(factionStore, (state) => state.logOpen);
    const view = useStore(nocStore, (state) => state.view);
    const catalog = useStore(nocStore, (state) => state.catalog);
    const refusal = useStore(nocStore, (state) => state.refusal);
    if (!isOpen || !view || !catalog) {
        return null;
    }
    const rules = catalog.reputationRules;
    return (
        <DraggableDialog title="Quest log" position={position} id="quest-log-dialog" zIndex={zIndex} onContextMenu={(e) => e.preventDefault()}>
            <div style={panel}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={small}>{view.quests.length} / {catalog.questMaxActive} active · {view.questsDone.length} completed</span>
                    <RpgButton variant="compact" onClick={() => setQuestLogOpen(false)}>Close</RpgButton>
                </div>
                {refusal && <div style={{ color: '#e07a5f' }}>{refusal}</div>}
                {view.quests.length === 0 && <div style={small}>No active quest.</div>}
                {view.quests.map((a) => {
                    const q = catalog.questsById.get(a.questId);
                    if (!q) {
                        return null;
                    }
                    const value = view.reputations.find((r) => r.factionId === q.factionId)?.value ?? 0;
                    const st = questState(q, { factionId: view.factionId, tier: rules ? tierOf(rules, value) : 0, active: view.quests, done: view.questsDone, maxActive: catalog.questMaxActive });
                    return (
                        <div key={q.id} style={{ border: '1px solid var(--rpg-leather)', background: 'rgba(0, 0, 0, 0.35)', padding: '6px 8px', display: 'flex', flexDirection: 'column', gap: 4 }}>
                            <div style={{ fontWeight: 'bold', color: factionColorLight(q.factionId) }}>{q.name}{st.status === 'complete' ? ' (ready)' : ''}</div>
                            <div style={small}>{objectiveText(q, st.progress, questTargetName(q))}</div>
                            <div style={small}>Reward: {rewardText(q)}</div>
                            <RpgButton variant="compact" onClick={() => onAbandon(q.id)}>Abandon</RpgButton>
                        </div>
                    );
                })}
            </div>
        </DraggableDialog>
    );
}
