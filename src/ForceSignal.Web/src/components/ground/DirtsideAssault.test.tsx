// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { dirtsideGameKey } from '../../constants.ts';
import type { DirtsideAssaultState, DirtsideElementState, DirtsidePlatoonState, DirtsideSnapshot, GameHandle } from '../../types.ts';
import { DirtsideView } from './DirtsideView.tsx';

const mocks = vi.hoisted(() => ({
  readGame: vi.fn(),
  addPlatoon: vi.fn(),
  recoverSystems: vi.fn(),
  fire: vi.fn(),
  launchAssault: vi.fn(),
  standAgainstAssault: vi.fn(),
  fightAssaultRound: vi.fn(),
  resolveAssaultAftermath: vi.fn(),
  followThrough: vi.fn(),
  endActivation: vi.fn(),
}));
vi.mock('../../lib/dirtsideApi.ts', () => mocks);

const handle: GameHandle = { gameId: 'game-1', token: 'token-1' };

function element(id: string, name: string, extra: Partial<DirtsideElementState> = {}): DirtsideElementState {
  return {
    id,
    name,
    isDestroyed: false,
    isDamaged: false,
    isSystemsDown: false,
    movedOverHalf: false,
    areaDefenceSensorsLive: false,
    movement: 12,
    hasChosen: false,
    hasMoved: false,
    hasTakenCombatAction: false,
    hasStoodDown: false,
    weapons: ['Main Gun'],
    hasBackupSystems: false,
    assaultChits: 3,
    killThreshold: 6,
    canRecoverSystems: false,
    whyItCannotRecoverSystems: 'Nothing is activated.',
    ...extra,
  };
}

function platoon(id: string, name: string, side: string, elements: DirtsideElementState[], extra: Partial<DirtsidePlatoonState> = {}): DirtsidePlatoonState {
  return {
    id,
    name,
    side,
    kind: 'Armour',
    isCybertank: false,
    confidence: 'Confident',
    isUnderFire: false,
    isDisorganised: false,
    hasActivated: false,
    canActivate: true,
    whyItCannotActivate: null,
    qualityDie: 'D8',
    leadershipValue: 2,
    elements,
    ...extra,
  };
}

const alpha = platoon('alpha', 'Alpha Troop', 'blue', [
  element('alpha-1', 'Tank 1'),
  element('alpha-2', 'Tank 2'),
], { hasActivated: true });
const bravo = platoon('bravo', 'Bravo Troop', 'red', [
  element('bravo-1', 'Gun 1'),
  element('bravo-2', 'Gun 2'),
]);

function snapshotWith(overrides: Partial<DirtsideSnapshot> = {}): DirtsideSnapshot {
  return {
    gameId: 'game-1',
    name: 'Ridge Line',
    turnNumber: 1,
    phase: 'Activations',
    sides: ['blue', 'red'],
    activeSide: 'blue',
    activatingUnitId: 'alpha',
    elementsStillToChoose: ['alpha-1', 'alpha-2'],
    canEndActivation: false,
    whyActivationCannotEnd: 'Every element must say what it is doing.',
    assault: null,
    // 2 to 5, invented, and deliberately backwards from the bound the other engine used to hold:
    // this panel took any number from 0 to 9 and the server took whatever arrived, so 99 and -4
    // both went onto a command marker. What a Leadership Value is comes off this profile now.
    profile: {
      fireControl: [],
      posture: [],
      signature: [],
      systemsDownRecoveryRoll: 0,
      systemsDownRecoveryRollWithBackup: 0,
      areaDefenceReach: 0,
      lowestLeadershipValue: 2,
      highestLeadershipValue: 5,
    },
    units: [alpha, bravo],
    log: [],
    version: 3,
    ...overrides,
  };
}

function assaultAt(stage: string, overrides: Partial<DirtsideAssaultState> = {}): DirtsideAssaultState {
  return {
    attackerUnitId: 'alpha',
    defenderUnitId: 'bravo',
    stage,
    round: 1,
    attackerElementIds: ['alpha-1', 'alpha-2'],
    defenderElementIds: [],
    ...overrides,
  };
}

const plainValidity = { colours: 'All', valueScale: 'FaceValue', specialsCount: true, isIneffective: false };

async function open(snapshot: DirtsideSnapshot) {
  localStorage.setItem(dirtsideGameKey, JSON.stringify(handle));
  mocks.readGame.mockResolvedValue(snapshot);
  render(<DirtsideView />);
  await screen.findByText('Reopened Ridge Line.');
}

describe('Dirtside close assault', () => {
  beforeEach(() => {
    localStorage.clear();
    for (const mock of Object.values(mocks)) {
      mock.mockReset();
    }
  });

  afterEach(cleanup);

  it('launches an assault with the elements ticked, the threat entered and the validity chosen', async () => {
    const after = snapshotWith({ assault: assaultAt('AwaitingDefender'), log: ['Alpha Troop went in.'] });
    mocks.launchAssault.mockResolvedValue(after);
    await open(snapshotWith());

    const launch = screen.getByRole('button', { name: 'Launch Assault' }) as HTMLButtonElement;
    expect(launch.disabled).toBe(true);

    fireEvent.click(screen.getByLabelText('Tank 1'));
    fireEvent.click(screen.getByLabelText('Tank 2'));
    fireEvent.change(screen.getByLabelText('Threat level'), { target: { value: '2' } });
    fireEvent.change(screen.getByLabelText('Colours that count'), { target: { value: 'Red' } });
    fireEvent.change(screen.getByLabelText('Value scale'), { target: { value: 'Halved' } });
    fireEvent.click(screen.getByLabelText('Specials count'));
    fireEvent.click(launch);

    await waitFor(() => expect(mocks.launchAssault).toHaveBeenCalledWith(handle, {
      targetUnitId: 'bravo',
      elementIds: ['alpha-1', 'alpha-2'],
      threatLevel: 2,
      validity: { colours: 'Red', valueScale: 'Halved', specialsCount: false, isIneffective: false },
      handToHandValidity: null,
    }));
    expect((await screen.findAllByText('Alpha Troop went in.')).length).toBeGreaterThan(0);
  });

  it('sends a separate hand-to-hand validity only when asked to', async () => {
    mocks.launchAssault.mockResolvedValue(snapshotWith({ assault: assaultAt('AwaitingDefender') }));
    await open(snapshotWith());

    fireEvent.click(screen.getByLabelText('Tank 1'));
    fireEvent.click(screen.getByLabelText('Hand-to-hand reads differently'));
    fireEvent.change(screen.getByLabelText('Hand-to-hand colours'), { target: { value: 'Green' } });
    fireEvent.click(screen.getByRole('button', { name: 'Launch Assault' }));

    await waitFor(() => expect(mocks.launchAssault).toHaveBeenCalledWith(handle, expect.objectContaining({
      elementIds: ['alpha-1'],
      validity: plainValidity,
      handToHandValidity: { ...plainValidity, colours: 'Green' },
    })));
  });

  it('lets the defender stand with its own elements and threat', async () => {
    mocks.standAgainstAssault.mockResolvedValue(snapshotWith({
      assault: assaultAt('AwaitingRound', { defenderElementIds: ['bravo-2'] }),
    }));
    await open(snapshotWith({ assault: assaultAt('AwaitingDefender') }));

    expect(screen.queryByRole('button', { name: 'Launch Assault' })).toBeNull();
    expect(screen.getByText(/Alpha Troop is assaulting Bravo Troop/)).toBeTruthy();

    fireEvent.click(screen.getByLabelText('Gun 2'));
    fireEvent.change(screen.getByLabelText('Threat level'), { target: { value: '3' } });
    fireEvent.click(screen.getByRole('button', { name: 'Stand' }));

    await waitFor(() => expect(mocks.standAgainstAssault).toHaveBeenCalledWith(handle, {
      elementIds: ['bravo-2'],
      threatLevel: 3,
      validity: plainValidity,
      handToHandValidity: null,
    }));
    expect(await screen.findByRole('button', { name: 'Fight Round 1' })).toBeTruthy();
  });

  it('fights the round the assault is waiting on', async () => {
    mocks.fightAssaultRound.mockResolvedValue(snapshotWith({
      assault: assaultAt('AwaitingAftermath', { attackerElementIds: ['alpha-2'], defenderElementIds: ['bravo-1'] }),
    }));
    await open(snapshotWith({ assault: assaultAt('AwaitingRound', { round: 2, defenderElementIds: ['bravo-1'] }) }));

    fireEvent.click(screen.getByRole('button', { name: 'Fight Round 2' }));

    await waitFor(() => expect(mocks.fightAssaultRound).toHaveBeenCalledWith(handle));
    expect(await screen.findByRole('button', { name: 'Resolve Aftermath' })).toBeTruthy();
    expect(screen.getByText(/Still standing: Tank 2/)).toBeTruthy();
    expect(screen.getByText(/Still standing: Gun 1/)).toBeTruthy();
  });

  it('resolves the aftermath with both casualty threats', async () => {
    mocks.resolveAssaultAftermath.mockResolvedValue(snapshotWith({ assault: assaultAt('AwaitingFollowThrough') }));
    await open(snapshotWith({ assault: assaultAt('AwaitingAftermath', { defenderElementIds: ['bravo-1'] }) }));

    fireEvent.change(screen.getByLabelText('Light casualty threat'), { target: { value: '1' } });
    fireEvent.change(screen.getByLabelText('Heavy casualty threat'), { target: { value: '4' } });
    fireEvent.click(screen.getByRole('button', { name: 'Resolve Aftermath' }));

    await waitFor(() => expect(mocks.resolveAssaultAftermath).toHaveBeenCalledWith(handle, {
      lightCasualtyThreat: 1,
      heavyCasualtyThreat: 4,
    }));
    expect(await screen.findByRole('button', { name: 'Follow Through' })).toBeTruthy();
  });

  it('follows through with a threat, and says that ending the activation declines it', async () => {
    mocks.followThrough.mockResolvedValue(snapshotWith({ assault: null, canEndActivation: true, whyActivationCannotEnd: null }));
    await open(snapshotWith({ assault: assaultAt('AwaitingFollowThrough') }));

    expect(screen.getByText(/End Activation declines/)).toBeTruthy();
    fireEvent.change(screen.getByLabelText('Threat level'), { target: { value: '2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Follow Through' }));

    await waitFor(() => expect(mocks.followThrough).toHaveBeenCalledWith(handle, 2));
    expect(await screen.findByRole('button', { name: 'Launch Assault' })).toBeTruthy();
  });
});

describe('Dirtside systems-down recovery', () => {
  beforeEach(() => {
    localStorage.clear();
    for (const mock of Object.values(mocks)) {
      mock.mockReset();
    }
  });

  afterEach(cleanup);

  it('tries to recover an element the game says can try', async () => {
    const down = element('alpha-1', 'Tank 1', { isSystemsDown: true, canRecoverSystems: true, whyItCannotRecoverSystems: null });
    const recovered = snapshotWith({ log: ['Tank 1 got its systems back.'] });
    mocks.recoverSystems.mockResolvedValue(recovered);
    await open(snapshotWith({ units: [{ ...alpha, elements: [down, alpha.elements[1]] }, bravo] }));

    const button = screen.getByRole('button', { name: 'Recover systems on Tank 1' }) as HTMLButtonElement;
    expect(button.disabled).toBe(false);
    fireEvent.click(button);

    await waitFor(() => expect(mocks.recoverSystems).toHaveBeenCalledWith(handle, 'alpha-1'));
    expect((await screen.findAllByText('Tank 1 got its systems back.')).length).toBeGreaterThan(0);
  });

  it('is disabled with the game\'s reason when it cannot', async () => {
    const reason = 'Tank 1 lost its systems this activation; it can try next time it is activated.';
    const down = element('alpha-1', 'Tank 1', { isSystemsDown: true, canRecoverSystems: false, whyItCannotRecoverSystems: reason });
    await open(snapshotWith({ units: [{ ...alpha, elements: [down, alpha.elements[1]] }, bravo] }));

    const button = screen.getByRole('button', { name: 'Recover systems on Tank 1' }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(button.title).toBe(reason);
    // And as text beside it, because a disabled button's tooltip reaches a mouse and nothing else.
    expect(screen.getByText(`Tank 1: ${reason}`)).toBeTruthy();
    expect(mocks.recoverSystems).not.toHaveBeenCalled();
  });
});

describe('Dirtside fire-and-move declaration', () => {
  beforeEach(() => {
    localStorage.clear();
    for (const mock of Object.values(mocks)) {
      mock.mockReset();
    }
  });

  afterEach(cleanup);

  it('sends the over-half declaration with the shot', async () => {
    mocks.fire.mockResolvedValue(snapshotWith());
    await open(snapshotWith());

    fireEvent.click(screen.getByLabelText('Will move over half its movement after firing'));
    fireEvent.click(screen.getByRole('button', { name: 'Fire' }));

    await waitFor(() => expect(mocks.fire).toHaveBeenCalledWith(handle, expect.objectContaining({
      elementId: 'alpha-1',
      targetUnitId: 'bravo',
      willMoveOverHalf: true,
    })));
  });

  it('marks an immobilised element and will not offer it a move', async () => {
    const stuck = element('alpha-1', 'Tank 1', { isImmobilised: true });
    await open(snapshotWith({ units: [{ ...alpha, elements: [stuck, alpha.elements[1]] }, bravo] }));

    // The element has not said what it is doing yet, so it carries that marker too - which is the
    // point of rendering `hasChosen` at all. Matched as a substring rather than as the whole row,
    // so this test stays about immobilisation rather than about the shape of the marker chain.
    expect(screen.getByText(/Tank 1 .*· immobilised/)).toBeTruthy();
    // Found by the element's name, which is what these buttons are called now rather than a bare
    // "Move" repeated per row - and which is also a stronger test than the index order it replaces.
    const stuckMove = screen.getByRole('button', { name: 'Move Tank 1' }) as HTMLButtonElement;
    const otherMove = screen.getByRole('button', { name: `Move ${alpha.elements[1].name}` }) as HTMLButtonElement;
    expect(stuckMove.disabled).toBe(true);
    expect(otherMove.disabled).toBe(false);
  });
});

describe('Dirtside add-platoon card numbers', () => {
  beforeEach(() => {
    localStorage.clear();
    for (const mock of Object.values(mocks)) {
      mock.mockReset();
    }
  });

  afterEach(cleanup);

  it('leaves the assault numbers out when the card does not give them', async () => {
    mocks.addPlatoon.mockResolvedValue(snapshotWith());
    await open(snapshotWith({ activatingUnitId: null, units: [] }));

    fireEvent.click(screen.getByRole('button', { name: 'Add Platoon' }));

    await waitFor(() => expect(mocks.addPlatoon).toHaveBeenCalled());
    const sent = mocks.addPlatoon.mock.calls[0][1];
    expect('qualityDie' in sent).toBe(false);
    expect('leadershipValue' in sent).toBe(false);
    expect('assaultChits' in sent.elements[0]).toBe(false);
    expect('killThreshold' in sent.elements[0]).toBe(false);
    expect(sent.elements[0].hasBackupSystems).toBe(false);
  });

  it('sends the card numbers when they are given', async () => {
    mocks.addPlatoon.mockResolvedValue(snapshotWith());
    await open(snapshotWith({ activatingUnitId: null, units: [] }));

    fireEvent.change(screen.getByLabelText('Quality die'), { target: { value: 'D10' } });
    // 4 is a Leadership Value at this table. The box this replaced would have taken 99 as readily.
    fireEvent.change(screen.getByLabelText('Leadership value'), { target: { value: '4' } });
    fireEvent.click(screen.getByLabelText('Backup systems'));
    fireEvent.change(screen.getByLabelText('Assault chits'), { target: { value: '3' } });
    fireEvent.change(screen.getByLabelText('Kill threshold'), { target: { value: '6' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add Platoon' }));

    await waitFor(() => expect(mocks.addPlatoon).toHaveBeenCalled());
    const sent = mocks.addPlatoon.mock.calls[0][1];
    expect(sent.qualityDie).toBe('D10');
    expect(sent.leadershipValue).toBe(4);
    expect(sent.elements[0]).toEqual(expect.objectContaining({ hasBackupSystems: true, assaultChits: 3, killThreshold: 6 }));
  });

  it('offers the Leadership Values this game entered, and no others', async () => {
    await open(snapshotWith({ activatingUnitId: null, units: [] }));

    const leadership = screen.getByLabelText('Leadership value') as HTMLSelectElement;

    // The entered set plus the not-given option. The old control was a number box clamped 0 to 9,
    // which was a bound this app had put on a card nobody had entered.
    expect([...leadership.options].map((option) => option.value)).toEqual(['', '2', '3', '4', '5']);
  });

  it('offers no Leadership Value at all when the game was never told what they are', async () => {
    await open(snapshotWith({ activatingUnitId: null, units: [], profile: null }));

    const leadership = screen.getByLabelText('Leadership value') as HTMLSelectElement;

    expect([...leadership.options].map((option) => option.value)).toEqual(['']);
    expect(leadership.disabled).toBe(true);
  });
});
