import { useStore } from '@tanstack/react-store';
import type { CSSProperties } from 'react';
import { DraggableDialog } from './DraggableDialog';
import { RpgButton } from '../components/RpgButton';
import { factionStore, setFactionPanelOpen } from '../store/Faction.store';
import { nocStore } from '../store/Noc.store';
import { factionColor, factionColorLight, tierColor } from '../factionTheme';
import { getItemById } from '../../constants/Items';
import { reputationBar, reputationOf, tierOf, type FactionView, type QuestView, type ReputationRules } from '../../utils/FactionView';
import { objectiveText, questState } from '../../utils/QuestView';
import type { CatalogModel } from '../../utils/NocCatalogView';
import type { NocViewModel } from '../../utils/NocView';

interface FactionDialogProps {
    position: { x: number; y: number };
    zIndex?: number;
    onJoin: (factionId: number) => void;
    onAccept: (questId: number) => void;
    onTurnIn: (questId: number) => void;
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
    width: 440,
    maxHeight: 480,
    overflowY: 'auto',
};

const small: CSSProperties = { fontSize: 11, opacity: 0.85 };

/** Names for a quest objective: items come from the client item catalog, monsters are shown by catalog id. */
export function questTargetName(quest: QuestView): string {
    return quest.kind === 'deliver' ? (getItemById(quest.targetId)?.name ?? `item ${quest.targetId}`) : `rival monsters (#${quest.targetId})`;
}

export function rewardText(quest: QuestView): string {
    const parts = [`${quest.rewardXp} XP`, `${quest.rewardRep >= 0 ? '+' : ''}${quest.rewardRep} reputation`];
    for (const item of quest.rewardItems) {
        parts.push(`${item.quantity} x ${getItemById(item.itemId)?.name ?? `item ${item.itemId}`}`);
    }
    return parts.join(', ');
}

export function ReputationBarView({ rules, value, factionId }: { rules: ReputationRules; value: number; factionId: number }) {
    const bar = reputationBar(rules, value);
    return (
        <div>
            <div style={{ position: 'relative', height: 14, border: '1px solid var(--rpg-leather)', background: 'rgba(0, 0, 0, 0.55)' }}>
                <div style={{ width: `${Math.round(bar.ratio * 100)}%`, height: '100%', background: factionColor(factionId) }} />
                <span style={{ position: 'absolute', inset: 0, textAlign: 'center', fontSize: 11, lineHeight: '14px', textShadow: '1px 1px 2px var(--rpg-shadow)' }}>
                    {bar.tierName} · {value}{bar.to === null ? '' : ` / ${bar.to}`}
                </span>
            </div>
        </div>
    );
}

function Join({ catalog, view, onJoin }: { catalog: CatalogModel; view: NocViewModel; onJoin: (id: number) => void }) {
    const rules = catalog.reputationRules!;
    return (
        <>
            <div style={small}>Join a faction (level {rules.joinLevel} required, stand next to its master). The choice is final.</div>
            {catalog.factions.map((f) => (
                <FactionCard key={f.id} faction={f} catalog={catalog}>
                    <div style={small}>Rivals: {f.rivals.map((r) => catalog.factionsById.get(r)?.name ?? r).join(', ') || 'none'} · Reputation {reputationOf(view.reputations, f.id)}</div>
                    <RpgButton variant="compact" disabled={view.level < rules.joinLevel} onClick={() => onJoin(f.id)}>
                        {view.level < rules.joinLevel ? `Level ${rules.joinLevel} required` : 'Join'}
                    </RpgButton>
                </FactionCard>
            ))}
        </>
    );
}

function FactionCard({ faction, children }: { faction: FactionView; catalog: CatalogModel; children: React.ReactNode }) {
    return (
        <div style={{ border: `1px solid ${factionColor(faction.id)}`, background: 'rgba(0, 0, 0, 0.35)', padding: '6px 8px', display: 'flex', flexDirection: 'column', gap: 4 }}>
            <div style={{ fontWeight: 'bold', color: factionColorLight(faction.id) }}>{faction.name} <span style={small}>({faction.hq})</span></div>
            <div style={small}>{faction.desc}</div>
            {children}
        </div>
    );
}

function Member({ catalog, view, onAccept, onTurnIn }: { catalog: CatalogModel; view: NocViewModel; onAccept: (id: number) => void; onTurnIn: (id: number) => void }) {
    const rules = catalog.reputationRules!;
    const factionId = view.factionId!;
    const faction = catalog.factionsById.get(factionId);
    const value = reputationOf(view.reputations, factionId);
    const tier = tierOf(rules, value);
    const ctx = { factionId, tier, active: view.quests, done: view.questsDone, maxActive: catalog.questMaxActive };
    const quests = catalog.quests.filter((q) => q.factionId === factionId);
    return (
        <>
            <div style={{ fontWeight: 'bold', color: factionColorLight(factionId) }}>{faction?.name ?? `Faction ${factionId}`}</div>
            <ReputationBarView rules={rules} value={value} factionId={factionId} />
            <div style={{ ...small, color: tierColor(tier) }}>Active quests: {view.quests.length} / {catalog.questMaxActive}</div>
            {quests.map((q) => {
                const st = questState(q, ctx);
                return (
                    <div key={q.id} style={{ border: '1px solid var(--rpg-leather)', background: 'rgba(0, 0, 0, 0.35)', padding: '6px 8px', display: 'flex', flexDirection: 'column', gap: 4, opacity: st.status === 'done' ? 0.6 : 1 }}>
                        <div style={{ fontWeight: 'bold' }}>{q.name}</div>
                        <div style={small}>{q.desc}</div>
                        <div style={small}>{objectiveText(q, st.progress, questTargetName(q))}</div>
                        <div style={small}>Reward: {rewardText(q)}</div>
                        {st.status === 'available' && <RpgButton variant="compact" onClick={() => onAccept(q.id)}>Accept</RpgButton>}
                        {st.status === 'complete' && <RpgButton variant="compact" onClick={() => onTurnIn(q.id)}>Turn in</RpgButton>}
                        {st.status === 'active' && <div style={small}>In progress</div>}
                        {st.status === 'done' && <div style={small}>Completed</div>}
                        {st.status === 'locked' && <div style={{ ...small, color: '#e07a5f' }}>{st.reason}</div>}
                    </div>
                );
            })}
        </>
    );
}

/** Faction master panel: join (no faction yet) or the quests of the faction. The server checks the master is within range and answers with NocActionRefused otherwise. */
export function FactionDialog({ position, zIndex, onJoin, onAccept, onTurnIn }: FactionDialogProps) {
    const isOpen = useStore(factionStore, (state) => state.isOpen);
    const view = useStore(nocStore, (state) => state.view);
    const catalog = useStore(nocStore, (state) => state.catalog);
    const refusal = useStore(nocStore, (state) => state.refusal);
    if (!isOpen || !view || !catalog || !catalog.reputationRules) {
        return null;
    }
    return (
        <DraggableDialog title="Faction master" position={position} id="faction-dialog" zIndex={zIndex} onContextMenu={(e) => e.preventDefault()}>
            <div style={panel}>
                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                    <RpgButton variant="compact" onClick={() => setFactionPanelOpen(false)}>Close</RpgButton>
                </div>
                {refusal && <div style={{ color: '#e07a5f' }}>{refusal}</div>}
                {view.factionId === null ? <Join catalog={catalog} view={view} onJoin={onJoin} /> : <Member catalog={catalog} view={view} onAccept={onAccept} onTurnIn={onTurnIn} />}
            </div>
        </DraggableDialog>
    );
}
