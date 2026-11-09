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
        return {
          heading,
          you: open
            ? `${open.name} is firing. Fire its other weapons, then press Done Firing to hand over.`
            : 'Your turn to fire: on Play Map pick a ship, open Fire, choose a target and fire. Press Done Firing (or Hold Fire) when that ship is finished.',
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
