import { EventBus } from '../../game/EventBus';
import { NOC_ACTION_REFUSED_RECEIVED, NOC_CATALOG_RECEIVED, NOC_STATE_RECEIVED, NOC_STATE_RESET } from '../../constants/EventNames';
import type { CatalogModel } from '../../utils/NocCatalogView';
import type { NocViewModel } from '../../utils/NocView';
import { createDialogStore } from './utils';

interface NocStoreState {
    /** The HUD is open while a snapshot is known. */
    isOpen: boolean;
    view: NocViewModel | null;
    /** Classes and skills; null on a server without class content. */
    catalog: CatalogModel | null;
    /** Latest refusal of a class/skill request, cleared by the next accepted state. */
    refusal: string | null;
    /** The class / skill-tree panel. */
    panelOpen: boolean;
}

const initialState: NocStoreState = {
    isOpen: false,
    view: null,
    catalog: null,
    refusal: null,
    panelOpen: false,
};

const { store: nocStore } = createDialogStore(initialState);

export { nocStore };

export const setNocView = (view: NocViewModel) => {
    nocStore.setState((state) => ({ ...state, isOpen: true, view, refusal: null }));
};

export const setNocCatalog = (catalog: CatalogModel) => {
    nocStore.setState((state) => ({ ...state, catalog }));
};

export const setNocRefusal = (refusal: string | null) => {
    nocStore.setState((state) => ({ ...state, refusal }));
};

export const toggleNocPanel = () => {
    nocStore.setState((state) => ({ ...state, panelOpen: !state.panelOpen, refusal: null }));
};

export const closeNocPanel = () => {
    nocStore.setState((state) => ({ ...state, panelOpen: false }));
};

export const resetNoc = () => {
    nocStore.setState(() => initialState);
};

// NetworkManager emits the converted snapshot; the HUD only reads this store.
EventBus.on(NOC_STATE_RECEIVED, (view: NocViewModel) => {
    setNocView(view);
});
EventBus.on(NOC_CATALOG_RECEIVED, (catalog: CatalogModel) => {
    setNocCatalog(catalog);
});
EventBus.on(NOC_ACTION_REFUSED_RECEIVED, (data: { reason: string }) => {
    setNocRefusal(data.reason);
});
EventBus.on(NOC_STATE_RESET, () => {
    resetNoc();
});
