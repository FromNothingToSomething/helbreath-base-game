import { useStore } from '@tanstack/react-store';
import type { CSSProperties } from 'react';
import { HeadlessDraggableDialog } from './HeadlessDraggableDialog';
import { RpgButton } from '../components/RpgButton';
import { closeNocPanel, nocStore } from '../store/Noc.store';
import { raceName } from '../../constants/Races';
import {
    branchSkills,
    classById,
    isRecommendedRace,
    skillState,
    sortClassesForRace,
    type CatalogModel,
    type ClassView,
    type SkillView,
} from '../../utils/NocCatalogView';
import type { NocViewModel } from '../../utils/NocView';

interface NocSkillsDialogProps {
    position: { x: number; y: number };
    zIndex?: number;
    onChooseClass: (classId: number) => void;
    onLearnSkill: (skillId: number) => void;
    /** Development builds only (the server also requires Settings.json nocthemar.debug). */
    onDebugRespec?: () => void;
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
    border: '2px solid var(--rpg-leather)',
    maxWidth: 760,
};

const card: CSSProperties = {
    border: '1px solid var(--rpg-leather)',
    background: 'rgba(0, 0, 0, 0.35)',
    padding: '6px 8px',
    display: 'flex',
    flexDirection: 'column',
    gap: 4,
};

const STATUS_STYLE: Record<string, CSSProperties> = {
    learned: { borderColor: 'var(--rpg-gold-dark)', background: 'rgba(120, 90, 20, 0.35)' },
    learnable: { borderColor: '#5fbf5f' },
    locked: { opacity: 0.6 },
    soon: { opacity: 0.4, borderStyle: 'dashed' },
};

const KIND_LABEL: Record<SkillView['kind'], string> = { passive: 'Passive', active: 'Active', ultimate: 'Ultimate' };

function ClassChoice({ catalog, view, onChooseClass }: { catalog: CatalogModel; view: NocViewModel; onChooseClass: (id: number) => void }) {
    const classes = sortClassesForRace(catalog.classes, view.raceId);
    const confirmChoice = (cls: ClassView) => {
        if (window.confirm(`Choose ${cls.name}? The class cannot be changed afterwards.`)) {
            onChooseClass(cls.id);
        }
    };

    return (
        <>
            <div style={{ fontWeight: 'bold' }}>Choose your class</div>
            <div style={{ fontSize: 11, opacity: 0.8 }}>Recommendations depend on your race ({view.raceName}); every race can play every class.</div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(210px, 1fr))', gap: 6 }}>
                {classes.map((cls) => {
                    const recommended = isRecommendedRace(cls, view.raceId);
                    return (
                        <div key={cls.id} style={{ ...card, ...(recommended ? { borderColor: 'var(--rpg-gold-dark)' } : {}) }}>
                            <div style={{ fontWeight: 'bold' }}>
                                {cls.name}
                                {recommended && <span style={{ color: 'var(--rpg-gold-dark)' }}> · recommended for {raceName(view.raceId)}</span>}
                            </div>
                            <div style={{ fontSize: 11, opacity: 0.85 }}>{cls.role}</div>
                            <ul style={{ margin: 0, paddingLeft: 16, fontSize: 11 }}>
                                {cls.branches.map((b) => (
                                    <li key={b.id}>
                                        {b.name} <span style={{ opacity: 0.7 }}>({b.role})</span>
                                    </li>
                                ))}
                            </ul>
                            <RpgButton variant="compact" onClick={() => confirmChoice(cls)}>
                                Choose
                            </RpgButton>
                        </div>
                    );
                })}
            </div>
        </>
    );
}

function SkillTree({
    catalog,
    cls,
    view,
    onLearnSkill,
}: {
    catalog: CatalogModel;
    cls: ClassView;
    view: NocViewModel;
    onLearnSkill: (id: number) => void;
}) {
    return (
        <>
            <div style={{ fontWeight: 'bold' }}>
                {cls.name} · {view.skillPoints} skill point{view.skillPoints === 1 ? '' : 's'}
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: `repeat(${Math.max(1, cls.branches.length)}, minmax(200px, 1fr))`, gap: 6 }}>
                {cls.branches.map((branch) => (
                    <div key={branch.id} style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                        <div style={{ fontWeight: 'bold' }}>
                            {branch.name} <span style={{ opacity: 0.7, fontWeight: 'normal' }}>({branch.role})</span>
                        </div>
                        {branchSkills(catalog, cls.id, branch.id).map((skill) => {
                            const state = skillState(catalog, skill, view);
                            return (
                                <div key={skill.id} style={{ ...card, ...STATUS_STYLE[state.status] }} title={state.reason}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 6 }}>
                                        <span style={{ fontWeight: 'bold' }}>{skill.name}</span>
                                        <span style={{ fontSize: 11, opacity: 0.8 }}>
                                            Lv {skill.level} · {KIND_LABEL[skill.kind]} · {skill.cost} pt
                                        </span>
                                    </div>
                                    <div style={{ fontSize: 11 }}>{skill.desc}</div>
                                    {state.status === 'learned' && <div style={{ fontSize: 11, color: 'var(--rpg-gold-dark)' }}>Learned</div>}
                                    {state.status === 'learnable' && (
                                        <RpgButton variant="compact" onClick={() => onLearnSkill(skill.id)}>
                                            Learn
                                        </RpgButton>
                                    )}
                                    {(state.status === 'locked' || state.status === 'soon') && (
                                        <div style={{ fontSize: 11, opacity: 0.8 }}>{state.status === 'soon' ? 'Coming soon' : state.reason}</div>
                                    )}
                                </div>
                            );
                        })}
                    </div>
                ))}
            </div>
        </>
    );
}

/** Class choice (once, with a race recommendation) and the skill tree of the chosen class. The server is authoritative; this only previews the rules. */
export function NocSkillsDialog({ position, zIndex, onChooseClass, onLearnSkill, onDebugRespec }: NocSkillsDialogProps) {
    const { panelOpen, view, catalog, refusal } = useStore(nocStore, (state) => state);
    if (!panelOpen || !view || !catalog) {
        return null;
    }

    const cls = view.classId === null ? undefined : classById(catalog, view.classId);

    return (
        <HeadlessDraggableDialog position={position} id="noc-skills-dialog" disableDrag zIndex={zIndex} onContextMenu={(e) => e.preventDefault()}>
            <div style={panel}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ fontWeight: 'bold' }}>Class & skills</span>
                    <RpgButton variant="compact" onClick={closeNocPanel}>
                        Close
                    </RpgButton>
                </div>
                {refusal && <div style={{ color: '#e07a5f' }}>{refusal}</div>}
                {cls ? <SkillTree catalog={catalog} cls={cls} view={view} onLearnSkill={onLearnSkill} /> : <ClassChoice catalog={catalog} view={view} onChooseClass={onChooseClass} />}
                {cls && onDebugRespec && (
                    <RpgButton variant="compact" onClick={onDebugRespec}>
                        Respec (debug)
                    </RpgButton>
                )}
            </div>
        </HeadlessDraggableDialog>
    );
}
