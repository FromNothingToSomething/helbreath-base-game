import type { NocCharacterCreation } from '../../proto/generated/nocthemar';
import { defaultDraft, toCreationMessage, withAxis, withRace, type AppearanceAxis, type CreationDraft } from '../../utils/CharacterCreation';
import { createDialogStore } from './utils';

export interface CharacterCreationState {
    /** The creation panel is shown (inside the connect dialog). */
    isOpen: boolean;
    /** A choice was confirmed and goes with the next connection; otherwise the character gets the default race. */
    enabled: boolean;
    draft: CreationDraft;
}

const initialState: CharacterCreationState = {
    isOpen: false,
    enabled: false,
    draft: defaultDraft(),
};

const { store: characterCreationStore, setOpen: setCharacterCreationOpen } = createDialogStore(initialState);

export { characterCreationStore, setCharacterCreationOpen };

export const selectRace = (raceId: number) => {
    characterCreationStore.setState((state) => ({ ...state, draft: withRace(state.draft, raceId) }));
};

export const selectAppearance = (axis: AppearanceAxis, value: number) => {
    characterCreationStore.setState((state) => ({ ...state, draft: withAxis(state.draft, axis, value) }));
};

/** Keeps the draft for the next connection and closes the panel. */
export const confirmCharacterCreation = () => {
    characterCreationStore.setState((state) => ({ ...state, enabled: true, isOpen: false }));
};

/** Back to the default race and appearance, nothing sent. */
export const clearCharacterCreation = () => {
    characterCreationStore.setState(() => initialState);
};

/** Creation block for AuthenticateRequest, or undefined when no choice was confirmed. */
export const getCreationForConnect = (): NocCharacterCreation | undefined => {
    const { enabled, draft } = characterCreationStore.state;
    return enabled ? toCreationMessage(draft) : undefined;
};
