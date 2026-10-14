import type { CSSProperties } from 'react';
import { useStore } from '@tanstack/react-store';
import { RpgButton } from '../components/RpgButton';
import { RACES, getRace, getDefaultRace } from '../../constants/Races';
import { allowedValues, type AppearanceAxis } from '../../utils/CharacterCreation';
import {
    characterCreationStore,
    clearCharacterCreation,
    confirmCharacterCreation,
    selectAppearance,
    selectRace,
} from '../store/CharacterCreation.store';

const selectStyle: CSSProperties = {
    width: '100%',
    boxSizing: 'border-box',
    padding: '4px 6px',
    marginTop: '2px',
    fontFamily: 'Georgia, serif',
    fontSize: '13px',
    color: 'var(--rpg-parchment)',
    backgroundColor: 'rgba(0,0,0,0.5)',
    border: '1px solid #704214',
    borderRadius: '2px',
};

const GENDER_LABELS = ['Male', 'Female'];
const SKIN_LABELS = ['Light', 'Tanned', 'Dark'];

function axisLabel(axis: AppearanceAxis, value: number): string {
    switch (axis) {
        case 'gender':
            return GENDER_LABELS[value] ?? String(value);
        case 'skinColor':
            return SKIN_LABELS[value] ?? String(value);
        default:
            return String(value + 1); // hair styles and underwear palettes are shown 1-8
    }
}

const AXIS_ROWS: { axis: AppearanceAxis; title: string }[] = [
    { axis: 'gender', title: 'Gender' },
    { axis: 'skinColor', title: 'Skin' },
    { axis: 'hairStyleIndex', title: 'Hair style' },
    { axis: 'underwearColorIndex', title: 'Underwear color' },
];

/**
 * Race and appearance choice for a NEW character, shown inside the connect dialog.
 * Only the values the selected race allows are offered, so the server never has to refuse a choice made here.
 * The server ignores the choice when the character already exists.
 */
export function CharacterCreationDialog() {
    const { isOpen, draft } = useStore(characterCreationStore, (s) => s);
    if (!isOpen) {
        return null;
    }

    const race = getRace(draft.raceId) ?? getDefaultRace();

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', borderTop: '1px solid #704214', paddingTop: '8px' }}>
            <label>
                Race
                <select
                    style={selectStyle}
                    value={draft.raceId}
                    onChange={(ev) => selectRace(Number.parseInt(ev.target.value, 10))}
                >
                    {RACES.map((r) => (
                        <option key={r.id} value={r.id}>
                            {r.name}
                        </option>
                    ))}
                </select>
            </label>
            {AXIS_ROWS.map(({ axis, title }) => (
                <label key={axis}>
                    {title}
                    <select
                        style={selectStyle}
                        value={draft[axis]}
                        onChange={(ev) => selectAppearance(axis, Number.parseInt(ev.target.value, 10))}
                    >
                        {allowedValues(race, axis).map((value) => (
                            <option key={value} value={value}>
                                {axisLabel(axis, value)}
                            </option>
                        ))}
                    </select>
                </label>
            ))}
            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '8px', paddingTop: '4px' }}>
                <RpgButton onClick={confirmCharacterCreation}>Use</RpgButton>
                <RpgButton onClick={clearCharacterCreation}>Default</RpgButton>
            </div>
        </div>
    );
}
