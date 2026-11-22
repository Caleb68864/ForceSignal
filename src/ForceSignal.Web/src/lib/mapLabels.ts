/**
 * Short labels for the map that tell every contact apart.
 *
 * The map wrote the first three letters of a name, so all eight of "Blue 1" to "Blue 8" read "BLU"
 * (playtest M6). This gives each the shortest label that is still unique among everything on the
 * map: the initial of each word with any number kept whole ("Blue 3" is B3, "Red Lancer" is RL),
 * then more letters of the first word where initials collide ("Valiant" and "Vigilant" are VAL and
 * VIG), and a running number only when nothing shorter works.
 */
type Named = { id: string; name: string };

const maxLength = 4;

function words(name: string) {
  return name.trim().split(/[\s_-]+/).filter(Boolean);
}

function initials(name: string) {
  return words(name)
    .map((word) => (/^\d+$/.test(word) ? word : word[0]))
    .join('')
    .toUpperCase();
}

function letters(name: string, count: number) {
  const parts = words(name);
  const number = parts.find((word) => /^\d+$/.test(word)) ?? '';
  const stem = (parts.find((word) => !/^\d+$/.test(word)) ?? name).replace(/[^\p{L}\p{N}]/gu, '');
  return (stem.slice(0, Math.max(1, count - number.length)) + number).toUpperCase();
}

export function mapLabels(contacts: readonly Named[]): Map<string, string> {
  const labels = new Map<string, string>();
  const candidates = (name: string) => [initials(name), letters(name, 3), letters(name, maxLength), letters(name, 6)]
    .filter((label) => label.length > 0);

  // A label is used only if no other contact could produce it at the same step, so two ships never
  // share one and the choice does not depend on the order they are listed in.
  const steps = contacts.map((contact) => candidates(contact.name));
  contacts.forEach((contact, index) => {
    const own = steps[index];
    const taken = new Set(labels.values());
    const chosen = own.find((label, step) => !taken.has(label)
      && steps.every((other, otherIndex) => otherIndex === index || other[step] !== label));
    labels.set(contact.id, chosen ?? `${own[0] || '?'}${index + 1}`);
  });

  // Anything still shared - two ships with the very same name - is numbered in list order.
  const seen = new Map<string, number>();
  for (const contact of contacts) {
    const label = labels.get(contact.id) ?? '?';
    const clashes = contacts.filter((other) => labels.get(other.id) === label).length;
    if (clashes > 1) {
      const next = (seen.get(label) ?? 0) + 1;
      seen.set(label, next);
      labels.set(contact.id, `${label}${next}`);
    }
  }

  return labels;
}
