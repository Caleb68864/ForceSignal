/**
 * Small screen offsets that fan out contacts sitting on (nearly) the same spot of the table, so each
 * can be seen and tapped (playtest M6: eight sister ships stacked into one token).
 *
 * Only the drawing moves: positions, ranges and arcs are still read from where each ship really is,
 * and the marker's title still gives its coordinates. Contacts are grouped when they are within
 * `within` table units of each other, chained, so a line of close ships fans out as one group.
 */
export type Spread = { dx: number; dy: number };

export function spreadOffsets(points: readonly { id: string; x: number; y: number }[], within: number): Map<string, Spread> {
  const parent = points.map((_, index) => index);
  const root = (index: number): number => (parent[index] === index ? index : (parent[index] = root(parent[index])));
  for (let a = 0; a < points.length; a += 1) {
    for (let b = a + 1; b < points.length; b += 1) {
      if (Math.hypot(points[a].x - points[b].x, points[a].y - points[b].y) <= within) {
        parent[root(a)] = root(b);
      }
    }
  }

  const groups = new Map<number, number[]>();
  points.forEach((_, index) => groups.set(root(index), [...(groups.get(root(index)) ?? []), index]));

  const offsets = new Map<string, Spread>();
  for (const members of groups.values()) {
    if (members.length < 2) {
      offsets.set(points[members[0]].id, { dx: 0, dy: 0 });
      continue;
    }

    // A ring big enough that neighbours on it do not overlap a 54px marker, and no bigger.
    const radius = Math.min(64, Math.max(30, (members.length * 58) / (2 * Math.PI)));
    members.forEach((member, slot) => {
      const angle = (2 * Math.PI * slot) / members.length - Math.PI / 2;
      offsets.set(points[member].id, { dx: Math.round(Math.cos(angle) * radius), dy: Math.round(Math.sin(angle) * radius) });
    });
  }

  return offsets;
}
