# StarGrunt II: Making It Playable

_Design settled 2026-08-10. Records decisions, not rules._

StarGrunt has a substantial rules engine and no way to reach it. The quality ladder, range bands,
the full fire sequence, confidence, fatigue, suppression and the whole activation layer are built
and tested - roughly 900 lines under `Modules.StarGrunt` plus the shared `GroundCombat` core, with
130 tests behind them. None of it is addressable: the feature flag is off, and the only route the
flag would expose is a status stub that reports the engine exists.

This is the design for the layer that closes that gap, and for the first slice of play it should
deliver.

## What the slice is

**Two squads, one shooting at the other, activations alternating properly, end to end on a screen.**

Not a demo of the fire engine - a game you can sit down and play, with the app holding the state a
person otherwise tracks on paper.

Deliberately outside it: close assault, fog of war and dummy counters, positions and a map,
two-device play, a saved force library, `GroundCombat/Design` and points, and everything Dirtside.
Each of those is a real feature; none of them is needed to find out whether the loop is any good.

## The four decisions that shape it

### The app is a companion, not a table

Minis stay on the physical table. The app holds the roster, the activation order, suppression,
confidence and fatigue, and it adjudicates rolls. It does not hold positions, and it computes no
line of sight.

This is not a shortcut, it is what the game is. `ground-combat-plan.md` settled it already - line of
sight is resolved by tape and eyeball, cover is assigned per shot, and target priority is
deliberately not algorithmic. The code has been written that way throughout:
`StarGruntUnitState.NextMoveLeavesCover` carries the comment *"a declaration, not a calculation"*.
Adding a map would mean deciding things the rulebook leaves to the players.

Range comes in as a declared distance, as `FireAttempt.DistanceInches` already expects. Positions
can be added later without disturbing any of this, exactly as Full Thrust cross-checks a declared
range against a map without ever refusing a shot over it.

### One device, passed around the table

No room codes, no seats, no participant tokens, no realtime sync. Both players use one screen, the
way they would share an umpire's clipboard.

There is nothing yet to keep private. StarGrunt's hidden information is fog of war - dummy counters,
concealed units, a sniper's alternate positions - and that is out of this slice. Building seats and
tokens now would be building privacy machinery around a game with no secrets in it. When fog of war
arrives it will want the hidden-order commitment machinery Full Thrust already has, which is a
better fit than participant tokens anyway.

### Forces are entered per game, and exported to a file

Build a force in the app, export it as versioned JSON, import it next time. No device library yet.

The library is the nicer thing to live with and it is not much code, but it is UI and storage work
in a slice whose job is to prove the play loop. The file gets the important half of the benefit -
a force survives, and it survives in a format something else could read one day.

### The game is a value, and the shell around it is thin

`StarGruntGame` is an immutable record holding the roster, the per-unit state, and the existing
`GroundCombatSession`. Every command is a pure function returning either a new game or a reason it
was refused. `Application` adds a lock, an id lookup, and persistence, and nothing else.

Three reasons, in order of weight.

**It is the style this layer already chose.** The sequence design settled on *"an immutable record
of immutable collections, and every transition is a pure function paired with a check that returns a
reason instead of throwing"*, and gave its reasons: a suspended activation has to survive being
serialized, restore fidelity becomes record equality rather than a bespoke comparer, and undo,
replay and the after-action log all come free because the history is the log. Those reasons apply
unchanged to the game around the session.

**It fills the hole the sequence layer was built with.** `IStarGruntBoard` exists so the activation
rules can read the world without owning it - *"the caller keeps it because the caller is the one
rolling dice and moving models"*. There has never been a caller. `StarGruntGame` is it, and it
implements that interface directly.

**It is the best answer available to the Godot question.** `Modules.StarGrunt` and
`Modules.GroundCombat` have no dependencies. Keeping the whole game in them means a Godot client
hosts StarGrunt in-process with no ASP.NET and no `Application` layer at all - where a Full Thrust
port has to bring `InMemoryMatchService` with it. The rules and the game state travel together as a
class library, and the transport is the only thing that changes.

The cost is real and small: applying three wounds rebuilds a record rather than mutating a field.
At platoon scale that is nothing, and it buys the undo and replay above.

Staying on .NET 10. The net8.0 retarget a Godot build needs is a separate, later, half-day question,
and nothing here should be built around it.

## Layering

```
Modules.StarGrunt/Game/          NEW - the game as a value
    StarGruntGame                immutable record; implements IStarGruntBoard
    UnitDefinition               what a unit is: quality, leadership, figures, weapons
    UnitStatus                   what has happened to it: casualties, suppression, confidence
    Commands / GameOutcome       pure transitions, refusal reasons rather than exceptions

Application/Ground/              NEW - the shell
    StarGruntGameService         lock, id lookup, IMatchStore persistence, DTO mapping

Contracts/Ground/                NEW - the wire shapes
Api/Endpoints/GroundCombat       real routes in place of the status stub, behind the flag
Web/                             one new view
```

## The unit model

```
UnitDefinition   Id, Name, Side, CommandLevel, QualityGrade, LeadershipDie,
                 Figures[{ ArmourDie }],
                 Weapons[{ Name, ImpactDie, IsSupport, IsCloseRange }]

UnitStatus       FiguresAlive, FiguresWounded, SuppressionMarkers, Confidence, Fatigue,
                 IsDisorganised, IsInCover, NextMoveLeavesCover,
                 ReactionTestCleared, TransfersMadeThisTurn
```

`UnitStatus` projects into `StarGruntUnitState` rather than restating it. The activation rules
already define what they need to read; a second copy of that shape would be a second place for it to
drift.

Every die on this model is a number the user types in from their own record card. That is the
content policy and it has a sharp edge here: **the firepower die stays an input per shot rather than
being derived from the figures firing.** Deriving it would mean shipping the figures-to-firepower
table, which is exactly the kind of published table that must not end up in this repo.
`FireAttempt.FirepowerDie` was already written to take it as an input, and that stays.

## The play loop

Create a game, add forces and units or import them, start the turn. The side with fewer units on the
table chooses whether to take or give the first activation. Players then alternate a unit at a time,
each activation spending actions on moving, firing or reorganising, until both sides are done and
the turn ends.

Fire resolves through `FireCombat` from a declared range, a declared posture, and dice the player
enters: the opposed roll, the potential hits, the remainder roll, each impact against armour.
Casualties and suppression come back and are applied. Confidence tests resolve inline between steps,
which the sequence work already settled - a test is a consequence, not an interrupt window, because
nobody chooses it, nobody may decline it, and it costs only morale.

Every command returns a fresh snapshot. That is the same command-to-snapshot shape `IMatchService`
has, and it is what makes the transport swappable: the same commands can arrive over HTTP today and
over a Steam socket later without the game noticing.

## The legality projection, built before the duplicate exists

The snapshot carries what each unit may legally do and why not, computed from the same checks the
commands enforce.

This is the lesson from the Full Thrust side, applied early. The web client there grew its own copy
of the firing rules, that copy was incomplete, and the console spent months offering shots the
server refused. Two rounds of work went into removing it. StarGrunt has no client yet, so there is
no duplicate to remove - and the way to keep it that way is to answer the question the client would
otherwise be tempted to answer for itself, from the beginning.

The rule this project has now paid for twice: **the client renders, the game decides.**

## Errors

Pure transitions never throw. They return a refusal reason, following `SequenceCheck`, which the
sequence layer already established for exactly this.

The shell turns a refusal into a 400. The distinction that matters is the one the order preview and
the firing solution both landed on: a command holds the line and refuses, while a question describes
instead. The legality projection is the question; the commands are the line.

## Persistence

Serialized through `IMatchStore`. Its contract is a key and an opaque string, deliberately - *"the
shape of a match is the application's business and changes with the rules; a store's business is
only that what went in comes back out"* - so it takes a StarGrunt game without modification.

One thing it does need: a **separate store instance with its own table**. The store is keyed by
`Guid` and `LoadAll()` hands back everything it holds, so sharing one table would eventually hand
the Full Thrust service a ground game it cannot parse.

## Testing

- **Module tests** on the pure transitions, with the rulebook's worked examples as fixtures. This is
  the pattern the fire engine and the dice ladder already follow.
- **A service test that plays a whole turn** - two forces, alternating activations, a firefight, a
  confidence test, turn end.
- **Endpoint tests including the flag off**, asserting the routes are absent rather than merely
  disabled. That is the promise the flag makes and it is worth a test.
- **A browser pass** over the loop.

## The feature flag does not change

Off by default, and off means absent: no routes at all, 404 as if the code had never been written.
Only an explicit `true` turns it on. The web view hides itself off `/api/features`.

The reason stands unchanged - the failure that matters is StarGrunt getting in the way of a Full
Thrust game someone actually turned up to play.

## A correction to the plan

`ground-combat-plan.md` lists StarGrunt `Assault/ close assault [BUILT]` in its layering table.
There is no `Assault/` folder; close assault is unbuilt, which item 4 of "what comes next" says
correctly. The table is wrong and is fixed alongside this document.
