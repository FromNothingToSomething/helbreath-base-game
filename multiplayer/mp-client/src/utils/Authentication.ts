import { ClientMessage } from '../proto/generated/network';
import type { NocCharacterCreation } from '../proto/generated/nocthemar';

/**
 * First message of a connection. `creation` is only used by the server for a character that has no save yet;
 * an existing character ignores it (the race is immutable). Omitted = default race and appearance.
 */
export function encodeAuthenticateRequest(id: string, characterName: string, creation?: NocCharacterCreation): Uint8Array {
    return ClientMessage.encode({
        payload: {
            $case: 'authenticateRequest',
            value: { id, characterName, creation },
        },
    }).finish();
}
