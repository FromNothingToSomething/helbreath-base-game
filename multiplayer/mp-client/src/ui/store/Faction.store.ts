import { NOC_STATE_RESET } from '../../constants/EventNames';
import { EventBus } from '../../game/EventBus';
import { createDialogStore } from './utils';

interface FactionStoreState {
    /** The faction master panel (join, quests of the faction). */
    isOpen: boolean;
    /** The quest log (active quests, abandon). */
    logOpen: boolean;
}

const initialState: FactionStoreState = { isOpen: false, logOpen: false };

const { store: factionStore, toggle: toggleFactionPanel, setOpen: setFactionPanelOpen } = createDialogStore(initialState);

export { factionStore, toggleFactionPanel, setFactionPanelOpen };

export const toggleQuestLog = () => {
    factionStore.setState((state) => ({ ...state, logOpen: !state.logOpen }));
};

export const setQuestLogOpen = (logOpen: boolean) => {
    factionStore.setState((state) => ({ ...state, logOpen }));
};

EventBus.on(NOC_STATE_RESET, () => {
    factionStore.setState(() => initialState);
});
