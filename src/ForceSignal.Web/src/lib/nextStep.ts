/**
 * What this player should do now, and who the table is waiting on.
 *
 * A new player's first question at every point of a match is "is it me?", and the answer was
 * spread over a phase chip, a checklist that counted the other side's ships, and the battle log.
 * This reads it all off the snapshot - phases, who is ready, whose orders are in, whose turn it is
 * to fire - so the banner that shows it cannot disagree with the server. It decides nothing: every
 * fact here is a field the server sent.
 */

import { gapsIn } from './rulesProfile.ts';
import { blankRulesProfile, type MatchSnapshot, type Participant } from '../types.ts';

export type NextStep = {
  /** The phase in words, with the turn. */
  heading: string;
  /** What this player should do now. */
  you: string;
  /** Who else the table is waiting on, if anyone. */
  waiting: string | null;
  /** True when the next move is this player's to make. */
  yourMove: boolean;
};

export function nextStep(snapshot: MatchSnapshot, meId: string): NextStep {
  const me = snapshot.participants.find((participant) => participant.id === meId);
  const host = snapshot.participants.find((participant) => participant.role === 'Owner');
  const isHost = Boolean(me && host && me.id === host.id);
  const hostName = host?.displayName ?? 'the host';
  const others = snapshot.participants.filter((participant) => participant.id !== meId);
  const ownerOf = new Map(snapshot.fleets.map((fleet) => [fleet.id, fleet.ownerParticipantId]));
  const myShips = snapshot.ships.filter((ship) => ownerOf.get(ship.fleetId) === meId);
  const myLiveShips = myShips.filter((ship) => !ship.isDestroyed);
  const turn = `Turn ${snapshot.turnNumber}`;

  if (snapshot.battleOver) {
    return {
      heading: 'Battle over',
      you: `${snapshot.battleOver.message.replace(/ The battle is over\.$/, '')} Export the log or save a snapshot to keep it.`,
      waiting: null,
      yourMove: false,
    };
  }

  switch (snapshot.phase) {
    case 'FleetSetup': {
      const heading = 'Fleet setup';
      const rulesMissing = gapsIn(snapshot.rules ?? blankRulesProfile).length > 0;
      if (rulesMissing && isHost) {
        return { heading, you: 'Set the rules profile first: open Rules profile → Edit numbers and enter your rulebook\'s numbers, then Save & Play Against This.', waiting: null, yourMove: true };
      }

      const hostNote = rulesMissing ? `Waiting for ${hostName} to set the rules profile.` : null;
      if (myShips.length === 0) {
        return { heading, you: 'Add your ships under Fleet setup → Add ship, then press Ready.', waiting: hostNote, yourMove: true };
      }

      if (!me?.isReady) {
        return { heading, you: `You have ${count(myShips.length, 'ship')}. When your fleet is complete, press Ready.`, waiting: hostNote, yourMove: true };
      }

      return { heading, you: 'You are ready.', waiting: hostNote ?? waitingOn(others.filter((participant) => !participant.isReady), 'to press Ready'), yourMove: false };
    }

    case 'OrderEntry': {
      const heading = `Order entry · ${turn}`;
      if (!me?.ordersComplete) {
        const locked = myLiveShips.filter((ship) => snapshot.orderStatuses.some((status) => status.shipId === ship.id && status.isCommitted)).length;
        return {
          heading,
          you: `Plot your ${count(myLiveShips.length, 'ship')} (${locked} locked), then press Lock Fleet Orders. A ship with no order keeps its speed and heading.`,
          waiting: null,
          yourMove: true,
        };
      }

      return { heading, you: 'Your orders are in.', waiting: waitingOn(others.filter((participant) => !participant.ordersComplete), 'to finish plotting'), yourMove: false };
    }

    case 'OrdersLocked':
    case 'Reveal': {
      const heading = `Reveal · ${turn}`;
      const unrevealed = (participantId: string) => snapshot.orderStatuses.some((status) => status.ownerParticipantId === participantId && status.isCommitted && !status.isRevealed);
      if (unrevealed(meId)) {
        return { heading, you: 'Everyone has locked. Press Reveal Fleet Orders.', waiting: null, yourMove: true };
      }

      return { heading, you: 'Your orders are revealed.', waiting: waitingOn(others.filter((participant) => unrevealed(participant.id)), 'to reveal'), yourMove: false };
    }

    case 'Movement': {
      const heading = `Movement · ${turn}`;
      return isHost
        ? { heading, you: 'Every order is revealed. Press Advance Turn to move the ships.', waiting: null, yourMove: true }
        : { heading, you: 'Every order is revealed.', waiting: `Waiting for ${hostName} to press Advance Turn and move the ships.`, yourMove: false };
    }

    case 'Firing': {
      const heading = `Firing · ${turn}`;
      const firing = snapshot.firingParticipantId;
      if (!firing) {
        return isHost
          ? { heading, you: `Every ship has fired. Press Advance Turn to start turn ${snapshot.turnNumber + 1}.`, waiting: null, yourMove: true }
          : { heading, you: 'Every ship has fired.', waiting: `Waiting for ${hostName} to press Advance Turn.`, yourMove: false };
      }

      if (firing === meId) {
        const open = myShips.find((ship) => ship.id === snapshot.firingShipId);
        if (open) {
          return { heading, you: `${open.name} is firing. Fire its other weapons, then press Done Firing to hand over.`, waiting: null, yourMove: true };
        }

        // Design suggestion 3: say which ships have nothing to shoot at on the map, so they can be
        // passed over with Hold Fire instead of opened one by one. Advice: the table measures.
        const activated = new Set(snapshot.activatedShipIds ?? []);
        const left = myLiveShips.filter((ship) => !activated.has(ship.id));
        const idle = left.filter((ship) => ship.hasTargetOnMap === false);
        const armed = left.filter((ship) => ship.hasTargetOnMap !== false);
        if (left.length > 0 && armed.length === 0) {
          return {
            heading,
            you: `None of your ships still to fire has a target in arc and range on the map (${idle.map((ship) => ship.name).join(', ')}). Hold Fire for each - or measure on the table if the map is out.`,
            waiting: null,
            yourMove: true,
          };
        }

        return {
          heading,
          you: `Your turn to fire: pick ${armed.length > 0 ? armed.map((ship) => ship.name).join(' or ') : 'a ship'}, open Fire, choose a target and fire. Press Done Firing (or Hold Fire) when that ship is finished.${idle.length > 0 ? ` ${idle.map((ship) => ship.name).join(', ')} ${idle.length === 1 ? 'has' : 'have'} nothing in arc and range on the map.` : ''}`,
          waiting: null,
          yourMove: true,
        };
      }

      const shooter = snapshot.participants.find((participant) => participant.id === firing);
      return { heading, you: 'Not your turn to fire yet.', waiting: `Waiting for ${shooter?.displayName ?? 'the other side'} to fire a ship.`, yourMove: false };
    }

    default:
      return { heading: snapshot.phase, you: '', waiting: null, yourMove: false };
  }
}

function waitingOn(players: Participant[], what: string): string | null {
  if (players.length === 0) {
    return null;
  }

  return `Waiting for ${players.map((participant) => participant.displayName).join(', ')} ${what}.`;
}

function count(value: number, noun: string): string {
  return `${value} ${noun}${value === 1 ? '' : 's'}`;
}

/** How a command shows: the one thing to press now, an available extra, done, or not at all. */
export type CommandState = 'primary' | 'secondary' | 'done' | null;

export type MatchCommands = {
  ready: CommandState;
  lock: CommandState;
  reveal: CommandState;
  advance: CommandState;
  /**
   * Set when Advance Turn would end firing with ships still waiting to shoot: the confirmation to
   * show, naming them. Null when advancing loses nothing.
   */
  advanceWarning: string | null;
  /** Why Ready cannot be pressed yet, said beside the disabled button. */
  readyBlocker: string | null;
  /** True for the host once play has begun and while the battle is still being fought. */
  endBattle: boolean;
};

/**
 * Which match commands this player is offered, and which one is the thing to press now.
 *
 * Every command used to show in every phase, for every seat: Lock and Reveal during fleet setup,
 * Advance Turn on the guest's screen where the server refuses it. This offers only what the phase
 * and the seat allow, with at most one of them primary.
 */
export function matchCommands(snapshot: MatchSnapshot, meId: string): MatchCommands {
  const step = nextStep(snapshot, meId);
  const me = snapshot.participants.find((participant) => participant.id === meId);
  const isHost = me?.role === 'Owner';
  const none: MatchCommands = {
    ready: null, lock: null, reveal: null, advance: null, advanceWarning: null, readyBlocker: null,
    endBattle: false,
  };
  if (snapshot.battleOver) {
    return none;
  }

  const commands = commandsForPhase(snapshot, meId, step, isHost, none);
  return { ...commands, endBattle: isHost && snapshot.phase !== 'FleetSetup' };
}

function commandsForPhase(
  snapshot: MatchSnapshot,
  meId: string,
  step: NextStep,
  isHost: boolean,
  none: MatchCommands,
): MatchCommands {
  const me = snapshot.participants.find((participant) => participant.id === meId);
  switch (snapshot.phase) {
    case 'FleetSetup': {
      if (me?.isReady) {
        return { ...none, ready: 'done' };
      }

      const ownerOf = new Map(snapshot.fleets.map((fleet) => [fleet.id, fleet.ownerParticipantId]));
      const mine = snapshot.ships.filter((ship) => ownerOf.get(ship.fleetId) === meId);
      // Said before Ready is pressed rather than as its refusal (playtest m14).
      const points = mine.reduce((total, ship) => total + (ship.pointsValue ?? 0), 0);
      const overLimit = snapshot.pointsLimit > 0 && points > snapshot.pointsLimit;
      const blocker = gapsIn(snapshot.rules ?? blankRulesProfile).length > 0
        ? 'Needs a rules profile first.'
        : mine.length === 0 ? 'Add at least one ship first.'
          : overLimit ? `Your fleet is ${points} points, over the ${snapshot.pointsLimit}-point limit.` : null;
      return { ...none, ready: blocker ? 'secondary' : 'primary', readyBlocker: blocker };
    }
    case 'OrderEntry':
      return { ...none, lock: me?.ordersComplete ? 'done' : 'primary' };
    case 'OrdersLocked':
    case 'Reveal':
      return { ...none, reveal: step.yourMove ? 'primary' : 'done' };
    case 'Movement':
      return { ...none, advance: isHost ? 'primary' : null };
    case 'Firing': {
      if (!isHost) {
        return none;
      }

      if (!snapshot.firingParticipantId) {
        return { ...none, advance: 'primary' };
      }

      const ownerOf = new Map(snapshot.fleets.map((fleet) => [fleet.id, fleet.ownerParticipantId]));
      const activated = new Set(snapshot.activatedShipIds ?? []);
      // A ship with nothing in arc and range on the map loses nothing by being skipped (design 3).
      const waiting = snapshot.ships.filter((ship) => !ship.isDestroyed && !activated.has(ship.id)
        && ship.hasTargetOnMap !== false && ship.weapons?.some((weapon) => !weapon.isDestroyed));
      const names = waiting.map((ship) => {
        const owner = snapshot.participants.find((participant) => participant.id === ownerOf.get(ship.fleetId));
        return owner ? `${ship.name} (${owner.displayName})` : ship.name;
      });
      return {
        ...none,
        advance: 'secondary',
        advanceWarning: `Firing is not finished.${names.length > 0 ? ` Not yet fired: ${names.join(', ')}.` : ''} End the firing phase anyway? Ships that have not fired lose their fire this turn.`,
      };
    }

    default:
      return none;
  }
}
