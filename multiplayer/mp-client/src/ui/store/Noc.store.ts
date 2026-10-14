import { EventBus } from '../../game/EventBus';
import { NOC_STATE_RECEIVED, NOC_STATE_RESET } from '../../constants/EventNames';
import type { NocViewModel } from '../../utils/NocView';
import { createDialogStore } from './utils';

interface NocStoreState {
    /** The HUD is open while a snapshot is known. */
    isOpen: boolean;
    view: NocViewModel | null;
}

const initialState: NocStoreState = {
    isOpen: false,
    view: null,
};

const { store: nocStore } = createDialogStore(initialState);

export { nocStore };

export const setNocView = (view: NocViewModel) => {
    nocStore.setState(() => ({ isOpen: true, view }));
};

export const resetNoc = () => {
    nocStore.setState(() => initialState);
};

// NetworkManager emits the converted snapshot; the HUD only reads this store.
EventBus.on(NOC_STATE_RECEIVED, (view: NocViewModel) => {
    setNocView(view);
});
EventBus.on(NOC_STATE_RESET, () => {
    resetNoc();
});
