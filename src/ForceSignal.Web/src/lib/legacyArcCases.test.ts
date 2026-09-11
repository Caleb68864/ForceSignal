/**
 * The client half of the legacy-arc parity fixture.
 *
 * `expandLegacyArc` and the server's `ExpandLegacyArc` read the same text off the same fleet file
 * and disagreed on the spelling neither suite exercised: the server stripped spaces and hyphens
 * before comparing, this side stripped only spaces and only in its fallback branch, so `Fore-Port`
 * resolved to ForePort on the server and to Fore on screen. See `legacyArcCases.json`.
 *
 * Compared as sets, deliberately. The order these come back in is not the subject and the two
 * implementations build their lists differently; `normalizeArcs` reorders against `firableArcs`
 * afterwards anyway.
 */

import { describe, expect, it } from 'vitest';
import cases from './legacyArcCases.json' with { type: 'json' };
import { expandLegacyArc, normalizeArcs } from './normalize.ts';

const sorted = (arcs: readonly string[]) => [...arcs].sort();

describe('a mount name written before the six arcs existed', () => {
  it('expands to exactly what the shared case list says, on this side too', () => {
    // Reached-the-subject: the fixture parsed and still carries the hyphenated case this file was
    // written for. A fixture reduced to an empty list would leave the walk reporting success.
    expect(cases.cases.length).toBeGreaterThanOrEqual(13);
    expect(cases.cases.some((entry) => entry.written === 'Fore-Port')).toBe(true);

    for (const entry of cases.cases) {
      expect(sorted(expandLegacyArc(entry.written)), `${entry.written || '(empty)'}`)
        .toEqual(sorted(entry.arcs));
    }
  });

  it('never hands back the blind spot', () => {
    // Rules-fidelity gap 3: no weapon fires through the aft arc. An expansion that produced it
    // would put a mount there through the import door, past every editor guard.
    expect(expandLegacyArc(cases.blindSpot.written)).not.toContain(cases.blindSpot.mustNotContain);
  });

  it('reaches the same answer through normalizeArcs, which is what callers use', () => {
    // The front door. `expandLegacyArc` is only reached when no explicit arc list arrived, and a
    // guard that tested it alone could pass while the path a fleet import actually takes did not.
    expect(normalizeArcs(null, 'Fore-Port')).toEqual(['ForePort']);
    expect(normalizeArcs(null, 'Fore Port')).toEqual(['ForePort']);

    // The control that must be accepted: an explicit list still wins over the legacy name.
    expect(normalizeArcs(['AftPort'], 'Fore-Port')).toEqual(['AftPort']);
  });

  it('is equally strict about a name neither side knows', () => {
    // The dullest violation available: a normalizer that stripped every non-letter would happily
    // turn nonsense into an arc. Falling back is the answer, not inventing a bearing.
    expect(expandLegacyArc('Fore/Port')).toEqual(['Fore']);
  });
});
