import type { APIRequestContext, Page } from '@playwright/test';

/**
 * A Full Thrust match set up through the API - rules profile, two players, a fleet and ships each,
 * both ready - and handed to the browser as the host's stored session, so a spec starts in order
 * entry instead of spending a minute typing a rulebook into the profile editor.
 *
 * The numbers are invented test numbers, as every rules profile in this repository's tests is.
 */
export const apiBase = `http://localhost:${process.env.GROUND_API_PORT ?? '8080'}`;

const beam = (face: number, screens: number, damage: number) => ({ dieFace: face, screenLevel: screens, damage });
export const inventedRules = {
  name: 'Invented', dieFaces: 8,
  beamDamage: [beam(8, 0, 2), beam(7, 0, 2), beam(6, 0, 1), beam(5, 0, 1), beam(8, 1, 1), beam(7, 1, 1), beam(6, 1, 1), beam(8, 2, 1)],
  beamRangeBandWidth: 10, maxScreenLevel: 2, torpedoMaximumRange: 40, torpedoBandWidth: 8, torpedoBestToHit: 3,
  needleBeamRange: 7, needleSystemKillRoll: 8, enhancedNeedleBeams: false, needleHullDamageRoll: 7,
  thresholdRows: 'FixedRows', thresholdRowCount: 3, escortRowCount: 2, cruiserRowCount: 3,
  maxPartiesPerJob: 2, repairRollWithOneParty: 7, repairBestRoll: 6, fighterMoveAllowance: 15,
  carrierRatesFollowBays: false, trueCarrierAllowance: 3, otherShipAllowance: 1, carrierTurnaroundRoll: false,
  turnaround: [{ dieFace: 1, isGroundedForGame: true, turnsBeforeRelaunch: 0 }],
  pointDefenseRange: 5, pointDefenseKills: [{ dieFace: 8, kills: 2 }], pointDefenseChainOnFace: 8,
  missilesPerSalvo: 4, salvoAttackRadius: 5,
};

type Created = { matchId: string; joinCode: string; participantId: string; participantToken: string };

async function json<T>(response: Awaited<ReturnType<APIRequestContext['post']>>): Promise<T> {
  if (!response.ok()) {
    throw new Error(`${response.url()} answered ${response.status()}: ${await response.text()}`);
  }

  return response.json() as Promise<T>;
}

async function fleetWithShips(request: APIRequestContext, matchId: string, token: string, fleetName: string, shipNames: string[], x: number, stacked = false) {
  const snapshot = await json<{ fleets: { id: string; name: string }[] }>(
    await request.post(`${apiBase}/api/matches/${matchId}/fleets`, { data: { participantToken: token, name: fleetName, faction: null } }));
  const fleetId = snapshot.fleets.find((fleet) => fleet.name === fleetName)?.id ?? '';
  for (const [index, name] of shipNames.entries()) {
    await json(await request.post(`${apiBase}/api/fleets/${fleetId}/ships`, {
      data: {
        participantToken: token, name, className: 'Cruiser', thrustRating: 4, initialVelocity: 0, initialCourse: 12,
        hullMax: 8, armorMax: 0, startX: x, startY: stacked ? 20 : 10 + index * 6, fireControlMax: 1,
        weapons: [{ id: '00000000-0000-0000-0000-000000000000', name: 'Beam', attackDice: 2, maxRange: 24, arcs: ['Fore'], kind: 'Beam' }],
      },
    }));
  }
}

/** Seeds a match - in order entry, or still in fleet setup with `ready: false` - and opens it in `page` as the host. */
export async function openSeededMatch(page: Page, request: APIRequestContext, options: { blueShips?: string[]; redShips?: string[]; ready?: boolean; stacked?: boolean } = {}) {
  const host = await json<Created>(await request.post(`${apiBase}/api/matches`, {
    data: { displayName: 'Blue Admiral', matchName: 'Seeded', rules: inventedRules },
  }));
  const guest = await json<Created>(await request.post(`${apiBase}/api/matches/join`, {
    data: { joinCode: host.joinCode, displayName: 'Red Admiral' },
  }));
  await fleetWithShips(request, host.matchId, host.participantToken, 'Blue Squadron', options.blueShips ?? ['Valiant', 'Vigilant'], 20, options.stacked);
  await fleetWithShips(request, host.matchId, guest.participantToken, 'Red Squadron', options.redShips ?? ['Mark'], 50);
  for (const token of options.ready === false ? [] : [host.participantToken, guest.participantToken]) {
    await json(await request.post(`${apiBase}/api/matches/${host.matchId}/participants/me/ready`, {
      data: { isReady: true }, headers: { 'X-Participant-Token': token },
    }));
  }

  const session = { matchId: host.matchId, participantId: host.participantId, participantToken: host.participantToken, joinCode: host.joinCode };
  await page.addInitScript((stored) => {
    if (!sessionStorage.getItem('seeded')) {
      localStorage.setItem('forcesignal.session', stored);
      sessionStorage.setItem('seeded', '1');
    }
  }, JSON.stringify(session));
  await page.goto('/');
  return { host, guest };
}
