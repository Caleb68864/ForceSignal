# Match Restore From Snapshot Backup — Design

**Date:** 2026-07-31
**Status:** approved, ready for implementation planning

## Problem

Match state is in-memory. An API restart mid-battle destroys the match: the client detects
the dead session, clears it, and drops you at the join screen. The app can already export a
full snapshot (`Save Snapshot`, and the autosaved `Export Last Device Backup` on the join
screen), but there is no way to put one back. The export is a dead end exactly when it
matters.

This was demonstrated during the converge run: killing the API mid-session produced a
session-expired state with the whole table gone.

## Scope

Restore returns **full state** — phase, turn number, damage, positions, velocity and course,
ordnance markers, and the battle log — from a snapshot file. It does not add durable
server-side persistence; that remains deferred in the roadmap pending table testing.

## Out of Scope

- Server-side durable autosave and self-recovery on boot. Better UX, but it is the deferred
  persistence decision and a much larger change. The import path designed here is what such
  an autosave would eventually call, so this is a step toward it, not away from it.
- Reconstructing hidden orders. Commitment hashes and salts are deliberately never exported.
- Fleet transfer between participants.

## Approach

A dedicated server-side import endpoint, chosen over two alternatives:

- **Client-side replay through existing endpoints** cannot satisfy the scope at all — phase,
  turn number, firing results and the log have no write path. It also costs one request per
  ship and leaves a half-built table on failure.
- **Server-side durable autosave** is out of scope above.

## API Surface

`POST /api/matches/restore`

- Body: the exported backup, accepting either the `{ savedAt, snapshot }` wrapper or a bare
  snapshot, because users will supply whichever file they kept.
- No participant token required. Possession of the file is the authority, as with fleet import.
- Returns the restored snapshot, the room code, and the claimable seats.
- Rejected when there are no ships, no participants, or `rulesProfileKey` names a profile this
  build does not have.

`POST /api/matches/{matchId}/seats/{participantId}/claim`

- Body: `{ displayName }` as confirmation.
- Returns a fresh session (participant id + token).
- An already-claimed seat is refused, so two devices cannot take the same admiral.

`GET /api/matches/{matchId}/seats` and join-by-code both surface unclaimed seats. Join keeps
its current behaviour of minting a new participant for matches that have none.

## Server-Side Rebuild

`RestoreMatch` constructs a `MatchState` directly rather than replaying public operations,
inside the existing `_gate` lock, so the operation is atomic: a validation failure leaves the
store untouched. It reuses the existing normalizers — `NormalizeIconKey`, `NormalizeWeapons`,
`ClampPosition`, `ClampDamage`, `NormalizeText`, and the fighter and ordnance normalizers — so
a hand-edited file cannot inject illegal state.

Identity:

- New match id and new participant tokens.
- Preserved fleet, ship and participant ids, keeping `homeCarrierShipId`, order statuses and
  firing-result references valid, and letting a device recognise the seat it held.
- The exported room code is reused when free, otherwise a new one is minted and reported.
- Participants return with `IsConnected = false` and no token until claimed; `IsReady` is preserved.

Phase mapping:

| Exported phase | Restored as |
|---|---|
| `Movement`, `Firing` | same phase, firing results preserved so spent weapons stay spent |
| `OrderEntry`, `OrdersLocked`, `Reveal` | `OrderEntry`, commitments dropped |
| `FleetSetup` | `FleetSetup` |

The log carries over, plus one appended entry recording the restore, the source `savedAt`, and
— when the phase fell back — that locked orders could not be recovered.

## Client Flow

The join screen gains **Restore Match** beside *Export Last Device Backup*, reusing the hidden
file-input pattern from fleet import.

1. Pick a snapshot file.
2. Seat list appears: each admiral with role and fleet/ship counts.
3. Tap your seat to receive a live session.
4. A second device enters the room code, sees the same list minus taken seats, and claims its own.

Fleets follow the seat, so ownership and hidden-order privacy survive a restore.

## Errors

Malformed JSON, wrong schema, empty ships, unknown rules profile, and claiming a taken or
nonexistent seat each return problem+json with a specific message. No partial restores.

## Testing

- Round trip: build a match, damage it, advance to Firing, export via `GetSnapshot`, restore,
  then assert phase, turn, damage, ordnance, firing results and log all match, and that a
  weapon already fired stays spent.
- Phase fallback from `OrdersLocked` lands in `OrderEntry` with commitments cleared and the
  explanatory log entry present.
- Seat claim issues a working token; a second claim on the same seat is refused.
- A claimed seat commands exactly its own fleets and no others.
- A hand-mangled payload is clamped, not accepted.
