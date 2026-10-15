import { EventBus } from '../../game/EventBus';
import { IN_UI_CAST_SPELL, NOC_STATE_RESET } from '../../constants/EventNames';
import type { CastSpellEvent } from '../../Types';
import { createDialogStore } from './utils';

export type ForgeTab = 'essences' | 'abilities' | 'rules';

interface MimicStoreState {
    /** The Soul Forge panel (essences, learned abilities, rules). */
    isOpen: boolean;
    tab: ForgeTab;
    /** Ability ids picked for a fusion, at most two, in the order picked. */
    fuseSelection: number[];
}

const initialState: MimicStoreState = {
    isOpen: false,
    tab: 'essences',
    fuseSelection: [],
};

const { store: mimicStore, toggle: toggleMimicPanel, setOpen: setMimicPanelOpen } = createDialogStore(initialState);

export { mimicStore, toggleMimicPanel, setMimicPanelOpen };

export const setForgeTab = (tab: ForgeTab) => {
    mimicStore.setState((state) => ({ ...state, tab }));
};

/** Adds the essence to the fusion picks, or removes it when already picked; a third pick replaces the oldest. */
export const toggleFuseSelection = (abilityId: number) => {
    mimicStore.setState((state) => {
        if (state.fuseSelection.includes(abilityId)) {
            return { ...state, fuseSelection: state.fuseSelection.filter((id) => id !== abilityId) };
        }
        return { ...state, fuseSelection: [...state.fuseSelection, abilityId].slice(-2) };
    });
};

export const clearFuseSelection = () => {
    mimicStore.setState((state) => ({ ...state, fuseSelection: [] }));
};

/** Starts a cast of a mimicry ability: the same flow as a spell (cast animation, then the target click); the server routes ids from 2000 up to mimicry. */
export const castMimicAbility = (abilityId: number) => {
    EventBus.emit(IN_UI_CAST_SPELL, { spellId: abilityId } satisfies CastSpellEvent);
};

EventBus.on(NOC_STATE_RESET, () => {
    mimicStore.setState(() => initialState);
});
