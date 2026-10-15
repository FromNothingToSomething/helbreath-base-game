// Shared test helpers (not a *.test.ts file: the runner glob skips it).
import { NocSnapshot } from '../../multiplayer/mp-client/src/proto/generated/nocthemar';

/**
 * A complete NocSnapshot: every field the generated type has is filled (numbers 0, repeated fields [], optional fields absent),
 * then `over` is applied. The generated encoder iterates repeated fields directly, so a literal that omits one (a field added
 * to the proto later) cannot be encoded; build snapshots with this helper instead of an object literal.
 */
export const makeNocSnapshot = (over: Partial<NocSnapshot> = {}): NocSnapshot => NocSnapshot.fromPartial(over);
