import { useStore } from '@tanstack/react-store';
import type { CSSProperties } from 'react';
import { DraggableDialog } from './DraggableDialog';
import { RpgButton } from '../components/RpgButton';
import { inventoryDialogStore } from '../store/InventoryDialog.store';
import { clearFuseSelection, mimicStore, setForgeTab, setMimicPanelOpen, toggleFuseSelection, type ForgeTab } from '../store/Mimic.store';
import { nocStore } from '../store/Noc.store';
import { MIMIC_PURPLE, MIMIC_PURPLE_DARK, MIMIC_PURPLE_LIGHT, MIMIC_RARITY_COLOR } from '../mimicTheme';
import type { CatalogModel, MimicView } from '../../utils/NocCatalogView';
import {
    MIMIC_KIND_LABEL,
    MIMIC_RARITY_LABEL,
    classAllows,
    consumePreview,
    equipPreview,
    essenceCounts,
    formatCooldown,
    fusePreview,
    levelProgress,
    ownedAbilities,
    powerMultiplier,
    slotUnlockLevel,
    type Preview,
} from '../../utils/MimicView';
import type { NocViewModel } from '../../utils/NocView';

interface SoulForgeDialogProps {
    position: { x: number; y: number };
    zIndex?: number;
    onConsume: (abilityId: number) => void;
    onFuse: (abilityA: number, abilityB: number) => void;
    onEquip: (abilityId: number, equipped: boolean) => void;
}

const panel: CSSProperties = {
    color: 'var(--rpg-parchment)',
    fontFamily: 'Georgia, serif',
    fontSize: 13,
    padding: '8px 10px',
    display: 'flex',
    flexDirection: 'column',
    gap: 8,
    background: MIMIC_PURPLE_DARK,
    border: `2px solid ${MIMIC_PURPLE}`,
    width: 460,
    maxHeight: 460,
    overflowY: 'auto',
};

const card: CSSProperties = {
    border: `1px solid ${MIMIC_PURPLE}`,
    background: 'rgba(0, 0, 0, 0.35)',
    padding: '6px 8px',
    display: 'flex',
    flexDirection: 'column',
    gap: 4,
};

const small: CSSProperties = { fontSize: 11, opacity: 0.85 };

const TABS: { id: ForgeTab; label: string }[] = [
    { id: 'essences', label: 'Essences' },
    { id: 'abilities', label: 'Abilities' },
    { id: 'rules', label: 'Rules' },
];

function AbilityName({ ability }: { ability: MimicView }) {
    return (
        <span style={{ fontWeight: 'bold', color: MIMIC_RARITY_COLOR[ability.rarity] }}>
            {ability.name}
            <span style={{ fontWeight: 'normal', opacity: 0.8 }}>{` · ${MIMIC_RARITY_LABEL[ability.rarity]} ${MIMIC_KIND_LABEL[ability.kind].toLowerCase()}`}</span>
        </span>
    );
}

function ClassNote({ ability, catalog }: { ability: MimicView; catalog: CatalogModel }) {
    if (ability.classIds.length === 0) {
        return null;
    }
    const names = ability.classIds.map((id) => catalog.classes.find((c) => c.id === id)?.name ?? `Class ${id}`);
    return <div style={small}>{`Reserved to: ${names.join(', ')}`}</div>;
}

function Essences({ catalog, view, onConsume, onFuse }: { catalog: CatalogModel; view: NocViewModel; onConsume: (id: number) => void; onFuse: (a: number, b: number) => void }) {
    const bag = useStore(inventoryDialogStore, (state) => state.baggedItems);
    const selection = useStore(mimicStore, (state) => state.fuseSelection);
    const counts = essenceCounts(catalog, bag);
    const owned = catalog.mimics.filter((m) => counts.has(m.id));
    const picks = selection.filter((id) => counts.has(id));
    const fuse = picks.length === 2 ? fusePreview(catalog, view, picks[0], picks[1]) : undefined;

    return (
        <>
            <div style={small}>Stand within 3 cells of a Soul Forge. Consuming teaches the ability for good and uses the essence up; two essences can be fused into a new ability.</div>
            {owned.length === 0 && <div style={{ opacity: 0.8 }}>No essence in your bag. Monsters leave essence orbs; picking one up needs an Essence Vial or the Assimilation skill.</div>}
            {owned.map((ability) => {
                const preview = consumePreview(catalog, view, ability.id);
                const picked = picks.includes(ability.id);
                return (
                    <div key={ability.id} style={{ ...card, ...(picked ? { borderColor: MIMIC_PURPLE_LIGHT, background: 'rgba(123, 79, 191, 0.3)' } : {}) }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 6 }}>
                            <AbilityName ability={ability} />
                            <span style={small}>{`x${counts.get(ability.id)}`}</span>
                        </div>
                        <div style={small}>{`From ${ability.monsterName ?? 'a fusion'} · mana ${ability.manaCost} · cooldown ${formatCooldown(ability.cooldownMs)}${ability.ready ? '' : ' · not castable yet'}`}</div>
                        <div style={{ fontSize: 11 }}>{ability.desc}</div>
                        <ClassNote ability={ability} catalog={catalog} />
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                            <RpgButton variant="compact" disabled={!preview.ok} title={preview.reason} onClick={() => onConsume(ability.id)}>
                                Consume
                            </RpgButton>
                            <RpgButton variant="compact" onClick={() => toggleFuseSelection(ability.id)}>
                                {picked ? 'Unselect' : 'Select to fuse'}
                            </RpgButton>
                            {!preview.ok && <span style={{ ...small, color: '#e07a5f' }}>{preview.reason}</span>}
                        </div>
                    </div>
                );
            })}
            {picks.length > 0 && (
                <div style={card}>
                    <div style={{ fontWeight: 'bold' }}>Fusion</div>
                    <div style={small}>{picks.map((id) => catalog.mimicsById.get(id)?.name ?? `#${id}`).join(' + ')}</div>
                    {picks.length < 2 && <div style={small}>Select a second essence.</div>}
                    {fuse && fuse.result && <div>{`Gives: ${fuse.result.name}`}</div>}
                    {fuse && !fuse.ok && <div style={{ ...small, color: '#e07a5f' }}>{fuse.reason}</div>}
                    <div style={{ display: 'flex', gap: 6 }}>
                        <RpgButton variant="compact" disabled={!fuse?.ok} onClick={() => { onFuse(picks[0], picks[1]); clearFuseSelection(); }}>
                            Fuse
                        </RpgButton>
                        <RpgButton variant="compact" onClick={clearFuseSelection}>
                            Clear
                        </RpgButton>
                    </div>
                </div>
            )}
        </>
    );
}

function Abilities({ catalog, view, onEquip }: { catalog: CatalogModel; view: NocViewModel; onEquip: (id: number, equipped: boolean) => void }) {
    const rules = catalog.mimicRules;
    const owned = ownedAbilities(catalog, view);
    if (!rules) {
        return null;
    }

    return (
        <>
            <div style={small}>{`Bar: ${view.mimicEquipped.length} / ${view.mimicSlots} slots. The bar can be changed anywhere; an equipped ability is cast from the violet bar.`}</div>
            {owned.length === 0 && <div style={{ opacity: 0.8 }}>No ability learned yet. Consume an essence at a Soul Forge.</div>}
            {owned.map(({ ability, state, equipped }) => {
                const progress = levelProgress(rules, state.xp);
                const preview: Preview = equipped ? { ok: true } : equipPreview(catalog, view, ability.id);
                return (
                    <div key={ability.id} style={card}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 6 }}>
                            <AbilityName ability={ability} />
                            <span style={small}>{`Lv ${progress.level} / ${progress.maxLevel} · power x${powerMultiplier(rules, progress.level).toFixed(2)}`}</span>
                        </div>
                        <div style={{ height: 6, border: `1px solid ${MIMIC_PURPLE}`, background: 'rgba(0, 0, 0, 0.5)' }}>
                            <div style={{ width: `${Math.round(progress.ratio * 100)}%`, height: '100%', background: MIMIC_PURPLE_LIGHT }} />
                        </div>
                        <div style={small}>{progress.atMax ? 'Maximum level' : `${progress.into} / ${progress.needed} XP to level ${progress.level + 1}`}</div>
                        <div style={{ fontSize: 11 }}>{ability.desc}</div>
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                            <RpgButton variant="compact" disabled={!preview.ok} title={preview.reason} onClick={() => onEquip(ability.id, !equipped)}>
                                {equipped ? 'Take off' : 'Equip'}
                            </RpgButton>
                            {!equipped && !preview.ok && <span style={{ ...small, color: '#e07a5f' }}>{preview.reason}</span>}
                            {equipped && <span style={{ ...small, color: MIMIC_PURPLE_LIGHT }}>Equipped</span>}
                        </div>
                    </div>
                );
            })}
        </>
    );
}

function Rules({ catalog, view }: { catalog: CatalogModel; view: NocViewModel }) {
    const rules = catalog.mimicRules;
    if (!rules) {
        return null;
    }
    const monsterAbilities = catalog.mimics.filter((m) => !m.isFusion);
    const mine = monsterAbilities.filter((m) => classAllows(m, view.classId)).length;

    return (
        <>
            <ul style={{ margin: 0, paddingLeft: 18, fontSize: 12, display: 'flex', flexDirection: 'column', gap: 3 }}>
                <li>One ability per monster: an ability you already know cannot be learned again.</li>
                <li>Some abilities are reserved to a class; you can learn {mine} of the {monsterAbilities.length} monster abilities with your class.</li>
                <li>Every cast gives 1 experience to the ability, up to level {rules.maxLevel}; power grows by {Math.round(rules.powerPerLevel * 100)} % per level.</li>
                <li>{`Experience needed in total: ${rules.thresholds.join(', ')}.`}</li>
                <li>
                    {`Bar slots: ${rules.slotsFirst} at level 1, one more every ${rules.levelsPerSlot} levels (levels ${Array.from({ length: rules.slotsMax }, (_, i) => slotUnlockLevel(rules, i)).filter((l): l is number => l !== null).join(', ')}); passives can add more.`}
                </li>
                <li>A fused ability cannot be fused again.</li>
            </ul>
            <div style={{ fontWeight: 'bold' }}>Fusion recipes</div>
            {catalog.fusions.length === 0 && <div style={small}>None.</div>}
            {catalog.fusions.map((f) => (
                <div key={f.result} style={small}>
                    {`${catalog.mimicsById.get(f.a)?.name ?? f.a} + ${catalog.mimicsById.get(f.b)?.name ?? f.b} = ${catalog.mimicsById.get(f.result)?.name ?? f.result}`}
                </div>
            ))}
        </>
    );
}

/** Soul Forge (violet): consume or fuse the essences of the bag, manage the learned abilities and the bar, and read the rules. The server is authoritative; this only previews its rules. */
export function SoulForgeDialog({ position, zIndex, onConsume, onFuse, onEquip }: SoulForgeDialogProps) {
    const isOpen = useStore(mimicStore, (state) => state.isOpen);
    const tab = useStore(mimicStore, (state) => state.tab);
    const view = useStore(nocStore, (state) => state.view);
    const catalog = useStore(nocStore, (state) => state.catalog);
    const refusal = useStore(nocStore, (state) => state.refusal);
    if (!isOpen || !view || !catalog || !catalog.mimicRules) {
        return null;
    }

    return (
        <DraggableDialog title="Soul Forge" position={position} id="soul-forge-dialog" zIndex={zIndex} onContextMenu={(e) => e.preventDefault()}>
            <div style={panel}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 6 }}>
                    <div style={{ display: 'flex', gap: 4 }}>
                        {TABS.map((t) => (
                            <RpgButton key={t.id} variant="compact" disabled={tab === t.id} onClick={() => setForgeTab(t.id)}>
                                {t.label}
                            </RpgButton>
                        ))}
                    </div>
                    <RpgButton variant="compact" onClick={() => setMimicPanelOpen(false)}>
                        Close
                    </RpgButton>
                </div>
                {refusal && <div style={{ color: '#e07a5f' }}>{refusal}</div>}
                {tab === 'essences' && <Essences catalog={catalog} view={view} onConsume={onConsume} onFuse={onFuse} />}
                {tab === 'abilities' && <Abilities catalog={catalog} view={view} onEquip={onEquip} />}
                {tab === 'rules' && <Rules catalog={catalog} view={view} />}
            </div>
        </DraggableDialog>
    );
}
