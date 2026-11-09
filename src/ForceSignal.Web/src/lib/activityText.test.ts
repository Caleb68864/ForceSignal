/**
 * The activity line says what happened in words, for every change the server can announce.
 *
 * The names are read off the API and the match service themselves, so a change announced under a
 * new name fails here until it has a sentence, rather than reaching a player as `NewThing · v12`.
 */

import { describe, expect, it } from 'vitest';
import { describeActivity, formatRulesProfile } from './format.ts';

const serverSources = import.meta.glob([
  '../../../ForceSignal.Api/Endpoints/MatchEndpoints.cs',
  '../../../ForceSignal.Api/Hubs/MatchHub.cs',
  '../../../ForceSignal.Application/Matches/*.cs',
], { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

function announcedReasons(): string[] {
  const text = Object.values(serverSources).join('\n');
  const names = new Set<string>();
  for (const match of text.matchAll(/NotifySnapshotChanged\(hub, \w+, "(\w+)"\)/g)) {
    names.add(match[1]);
  }

  for (const match of text.matchAll(/"MatchSnapshotChanged", [^;]*?"(\w+)"\)/g)) {
    names.add(match[1]);
  }

  for (const match of text.matchAll(/\.Touch\("(\w+)"\)/g)) {
    names.add(match[1]);
  }

  return [...names].sort();
}

describe('the activity line', () => {
  it('has a sentence for every change the server announces', () => {
    // Reached-the-subject: the globs found the files and the readers found names in them.
    expect(Object.keys(serverSources).length).toBeGreaterThan(3);
    const reasons = announcedReasons();
    expect(reasons).toEqual(expect.arrayContaining(['ParticipantConnectionChanged', 'WeaponFired', 'TurnAdvanced']));

    const unexplained = reasons.filter((reason) => describeActivity(reason) === describeActivity('SomethingNobodyHasNamed'));
    expect(unexplained, 'add a sentence to activitySentences in format.ts').toEqual([]);
  });

  it('never shows a raw event name or a version', () => {
    for (const reason of [...announcedReasons(), 'SomethingNobodyHasNamed']) {
      const sentence = describeActivity(reason);
      expect(sentence).not.toMatch(/[a-z][A-Z]\w+/);
      expect(sentence).not.toMatch(/v\d+/);
    }
  });
});

describe('the profile heading', () => {
  it('names the profile the players entered, or says there is none', () => {
    expect(formatRulesProfile({ name: 'Club night set' })).toBe('Club night set');
    expect(formatRulesProfile({ name: '  ' })).toBe('No rules profile yet');
    expect(formatRulesProfile(undefined)).toBe('No rules profile yet');
  });
});
