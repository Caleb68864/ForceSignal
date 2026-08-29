# Ground Combat: StarGrunt II and Dirtside II in ForceSignal

ForceSignal's working game is Full Thrust, in space. This is the plan for the two ground-combat
games alongside it: **StarGrunt II** at squad-and-figure scale, and **Dirtside II** at
platoon-and-vehicle scale. Both are Ground Zero Games rulesets, and both are being built while Full
Thrust remains the game anyone actually turns up to play.

The research behind this is in the local vault at `Notes/Ground Zero Games`, which carries a
paraphrased digest of both rulebooks. This document records the decisions, not the rules.

## The constraint that shapes everything

At any moment one of these engines may be half-finished, and neither may be able to reach a table
that came to play Full Thrust. So:

- Both default to **off**, and off means **absent** rather than disabled. A switched-off engine
  contributes no routes at all; its paths 404 exactly as if the code had never been written. There
  is no partially-wired state left behind to interfere.
- Only an explicit `true` turns one on. `yes`, `1`, `on`, and a blank all leave it off, because an
  unclear setting must never be the reason an unfinished engine reaches a game.
- Readiness names any in-progress engine that is switched on, since the reason to check readiness
  before a game is to find out what is about to be in the way.

See `ForceSignal.Application/Features/FeatureFlags.cs`. Configuration is the `Features` section, or
`FORCESIGNAL_FEATURES_STARGRUNT` / `FORCESIGNAL_FEATURES_DIRTSIDE` for a container.

## Content policy

The same rule as the rest of the repo, and it is not a formality here: these engines could easily
become a place where a rulebook ends up checked in.

**Ship the engine, never the rulebook.** Mechanics and procedures are implemented; the numbers are
the user's to enter from their own legally obtained rules. Concretely:

- No weapon tables, armour tables, quality-to-stat tables, army lists, faction background, points
  values, artwork, or rule text.
- Quality dice, firepower ratings, impact and armour dice, threat levels, close-combat weapon
  shifts, and chit-validity colours are all **inputs**, entered on a user's own record card.
- The line to hold: the engine owns **procedures** - what is rolled against what, what shifts what,
  what order things happen in - and the player owns every **number** those procedures read. Close
  assault was written the other way round first, with the charge threats, the weapon shifts and the
  bands for a downed figure hard-coded, on the reasoning that they were small enough to work out
  rather than look up. That reasoning is how the line gets crossed, so it was undone before this
  repo went anywhere public.
- The damage-chit pot composition is exposed as configuration rather than baked in, for the same
  reason — and because the published distribution is not fully verified.
- The vault's `Force Building/{NAC,ESU,FSE,NSL}` notes and `Community/Faction Army Lists` are the
  highest-risk content in the whole vault. Nothing from them is mined into the app.

## Layering

```
ForceSignal.Modules.GroundCombat      shared by both games
    Dice/        quality ladder, closed and open shifts, the three roll shapes  [BUILT]
    Sequence/    alternating activation, initiative, the pass rule, turn end    [BUILT]
    Morale/      confidence ladder and the test procedure                       [BUILT]
    Design/      size class, capacity, armour by facing, points                 [TODO]

ForceSignal.Modules.StarGrunt         figure scale
    Combat/      range bands, the fire sequence vs dispersed targets            [BUILT]
    Morale/      confidence, fatigue, suppression 0-3                           [BUILT]
    Sequence/    activation policy, steps, command levels                       [BUILT]
    Game/        the game as a value: roster, statuses, session, legality       [BUILT]
    Assault/     close assault                                                  [BUILT]

ForceSignal.Modules.Dirtside          vehicle scale
    Combat/      two-stage hit and damage resolution, infantry, close assault,
                 systems-down recovery                                          [BUILT]
    Chits/       the damage pot, composition and validity                       [BUILT]
    Sequence/    the activation policy                                          [BUILT]
    Game/        the game as a value; close assault and systems recovery
                 reaching the table 2026-08-29                                  [BUILT]
    Support/     the deferred artillery queue                                   [TODO]
```

What each Dirtside resolver does and what the game layer has not yet called is audited in
`dirtside-fidelity-gaps.md`, in the same numbered form as the other two engines.

### What is genuinely shared

Both games are the same designer's, and the overlap is real rather than convenient:

- **The quality die ladder** `d4 < d6 < d8 < d10 < d12`, and the closed/open shift semantics.
  Identical in both.
- **The three roll shapes**, the exceed-don't-match rule, and the multiple-opposed roll. Dirtside's
  multiple-mount rule (one extra die per barrel, each checked independently against the target's
  score) is exactly the shape StarGrunt's fire engine already needed.
- **The five confidence levels** and the test procedure: quality die against leadership plus threat,
  pass / minus one / minus two on half or less, threat levels never cumulative.
- **Alternating single-unit activation**, marker inversion, and the turn-end reset. Including the
  initiative rule — *the side with fewer units on the table chooses whether to take or give the
  first activation* — and the pass rule, *legal only when you have fewer unactivated units than the
  opponent*. Both games have this word for word.
- **Basic / Enhanced / Superior → D6 / D8 / D10** for system quality, since StarGrunt's vehicles use
  Dirtside's design system.
- **The vehicle design system itself.** StarGrunt explicitly borrows Dirtside's. Build `Design/`
  once, in the shared layer.

One extension needed: Dirtside has only three quality grades where StarGrunt has five. The
grade-to-die mapping must be a lookup each game supplies, not a fixed enum.

### What must fork, and must not be forced together

| | StarGrunt | Dirtside |
|---|---|---|
| Damage | impact die vs armour die | draw chits from a bag, sum, compare to flat armour |
| Casualties | individual figures, per-figure armour, wounds allocated | element whole or gone |
| Suppression | 0-3 stacking, central to the game | one binary flag, infantry only, cleared at end of own activation |
| Command | climbing ladder, a die type worse per level bypassed | flat — losing the command unit is a force-wide permanent penalty instead |
| Actions | two per activation | one combat action per element |
| Firepower | computed at roll time from figures actually firing | fixed per weapon class |
| Ground scale | 1" = 10 m | 1" = 100 m |

**Do not build a shared damage abstraction.** Chit-draw and impact-vs-armour have nothing in common
beyond producing an outcome; one interface over both buys nothing and obscures each.

**Unit stats do not convert between the games.** Neither rulebook offers a conversion. Anything
ForceSignal provides there is an original feature and must be labelled as one.

## Built so far

All of it pure, injectable-dice, and tested; none of it reachable while the flags are off.

- **`GroundCombat/Dice`** — the ladder, `Shift`/`ShiftClosed`/`ShiftOpposed`, the three roll shapes,
  and an injectable die source that corrects a source returning a result off the face. The worked
  example from the rules notes is a fixture.
- **`StarGrunt/Combat`** — range bands (band = the firer's own quality die in inches), the range die
  by band with cover and in-position shifts, and effective reach falling out of the shift running
  off the ladder rather than being a rule of its own. The full fire sequence against dispersed
  targets: opposed roll, potential hits by dividing the firer total by the range die *type*, the
  remainder roll, impact against armour. Both worked examples run as fixtures.
- **`StarGrunt/Morale`** — the confidence ladder, tests, fatigue as both a starting rung and a
  ceiling, suppression stacking to three and what a pinned unit may still do.
- **`Dirtside/Combat`** — stage one: the firer's die from fire control and range band, the movement
  penalty, the target's signature die and posture die with the higher kept rather than summed.

## Errata already folded in

The published rulebooks were never reprinted with GZG's corrections, so working from the PDF alone
gets these wrong. Two changed code that was already written:

- **Impact against armour is an *open* shift**, not a closed one. It was implemented closed, which
  silently threw away the protection of troops whose armour was already at the top of the ladder —
  they should degrade the incoming weapon instead. Fixed, with a test at the boundary.
- **A weapon's fire limit is per *activation*, not per game turn.** This matters once the activation
  layer exists: a unit granted a second activation by a transferred action may fire the same weapon
  again. The "has fired" flag must be scoped to the activation session.

Two more to honour when the surrounding systems are built:

- Confidence tests are triggered only by *basic* threat entries; the `+` rows only modify a test
  already triggered. Treating all rows alike makes units test far too often.
- IAVR firepower is D10, and the untreated-casualty confidence modifier is +1/+0/+0.

## What comes next, in order

1. **`GroundCombat/Sequence`** — alternating activation as a *suspendable, serializable session*,
   not a pure function. This is the load-bearing piece for both games and the hardest control flow.
   Dirtside has five interrupt windows (opportunity fire, area-defence interception, air defence,
   close assault, confidence tests) plus a deferred artillery queue; StarGrunt has reaction fire and
   nested transferred activations. Build it against **Dirtside first**: its activation and morale
   rules are the simpler of the two while being structurally identical at the level the core cares
   about, so anything Dirtside cannot express is a genuine over-fit to StarGrunt.
2. **`Dirtside/Chits`** — the damage pot. Highest-risk item in the whole plan: get the
   without-replacement semantics or the distribution wrong and *every* damage probability in the
   game is subtly off. Composition is configuration. Validity is a per-weapon, per-range,
   per-armour-type lookup the user fills in on their own card, and it wants a full test matrix
   because that is where subtle rules bugs will concentrate.
3. **`GroundCombat/Design`** — size class, capacity, armour by facing (store the front and derive
   the rest), the cross-limits, and the staged points calculation. The published worked examples
   make good regression fixtures. The rounding rule is genuinely unspecified in the source, so it
   is a named constant.
4. **StarGrunt close assault** — heavily psychological, leaning on reaction and confidence at both
   ends, and resolving entirely within one game turn.
5. ~~**The unit and force model, then contracts, then the API surface** behind the flags.~~
   **Done for StarGrunt**, as a playable slice: see
   `plans/2026-08-10-stargrunt-play-design.md`. `StarGruntGame` is the caller `IStarGruntBoard` was
   always written for - an immutable value holding the roster, the per-unit statuses and the
   session, with every command a pure function returning a new game or a refusal. `Application`
   adds a lock, an id, a version and the wire mapping; nothing else. The whole game therefore still
   lives in a module with no dependencies, which is what a client that is not a browser would need.

   The snapshot carries a legality projection - what each unit may do and why not - computed from
   the same checks the commands enforce, with tests asserting the two produce the same sentence.
   That is deliberately built before any client existed: the Full Thrust console grew its own copy
   of the firing rules, that copy was incomplete, and removing it took two rounds of work.

   Still out: fog of war, positions, two-device play, and a saved force library. StarGrunt close
   assault landed 2026-08-10. **Dirtside** got the same slice - a `DirtsideGame` value, a service,
   routes and a hot-seat screen - with direct fire and the activation sequence playable; close
   assault and systems-down recovery reach the table 2026-08-29, and what is still unreachable
   (opportunity fire, interception, indirect fire, morale, infantry as stands) is numbered in
   `dirtside-fidelity-gaps.md`.
6. **Icons.** Neither game uses NATO symbology, so the whole visual language is status markers and
   can be original. The vault already carries a clean-room vocabulary in
   `Dirtside II/Reference/dsii_markers.scad` — plain lettered discs reproducing none of GZG's
   colour, artwork or layout — and adopting it directly is IP-safe by construction and matches the
   physical play aids already on hand. StarGrunt additionally wants a single identity badge carrying
   quality and leadership together, with "spent" rendered on the badge rather than as a second
   token, and suppression as a numeric badge rather than three stacked icons.

## Decisions already taken for the next two pieces

### The activation layer is a value, not a mutable session

A suspended activation has to survive being serialized and restored, and a mutable class has no
natural serialization boundary - you end up hand-writing a DTO that drifts from the object. So the
session is an immutable record of immutable collections, and every transition is a pure function
paired with a check that returns a reason instead of throwing. Restore fidelity then becomes a
record equality assertion rather than a bespoke comparer, and undo, replay and the after-action log
all come free, because the history *is* the log.

Two things this makes structural rather than remembered:

- **Resources spent are derived from the steps taken, never stored.** That is what makes StarGrunt's
  per-activation weapon limit impossible to get wrong, and it removes a field that could go stale
  across a restore.
- **Counts are derived from the sets they count.** Both the pass rule and the first-activator rule
  read how many units are unactivated; a stored count that disagrees with the set is a bug factory.

### The frame stack is justified by Dirtside, not StarGrunt

It looked like nesting was a StarGrunt-only need, since only StarGrunt lets a commander hand a
subordinate a whole extra activation. It is not. Dirtside reaches an interrupt inside an interrupt
on its own: a mover is interrupted by opportunity fire, and that fire is itself interrupted by
area-defence interception of the missile it just launched. So the stack is derivable from the
simpler game, which is the test of whether it belongs in the shared layer at all. StarGrunt's
granted activation then drops in as a frame kind and costs the shared layer nothing.

The same test caught a real over-fit: an activation is **not** a budget of two actions. That is
StarGrunt's shape. Dirtside activates a platoon and then lets each element inside it choose
independently. The shared layer therefore accumulates an ordered list of steps that may name a
subject, and asks the game layer whether the frame is complete rather than counting anything.

Nesting is bounded by three rules rather than by a limit: a commander has only two actions to give
away, transfers only go down a chain of command that has finitely many levels, and no reaction can
retrigger its own kind. A depth ceiling exists on top of those purely as a tripwire - if it ever
fires, one of the three has a hole.

### The chit engine's hard parts

Draws are **without replacement within one shot, and the pot is restored between shots.** One draw
is one damage resolution against one element - a twin mount scoring two hits is two draws with a
restore between, not one draw of twice the size, and that genuinely changes the distribution.

The mechanism that differentiates weapons is **invalid chits consuming their draw slot**. You never
discard and redraw. The colours do not differ in severity at all - every colour averages the same
value - they differ only in availability, so the whole spread between weapons comes from throwing
draws away. Filtering before counting would quietly make every weapon equal.

**"Ineffective" is not the same as "no valid colours"**, and collapsing them produces a live bug:
special chits are not colour-gated, so a weapon the rules say cannot hurt the target at all would
still immobilise or destroy it. Ineffective has to short-circuit before any chit is drawn.

Chit count is an **input to the engine**, not something it derives from the weapon. The base rule -
count equals weapon size class - holds only for vehicle guns; missiles are launcher-set, several
weapons are flat regardless of class, and artillery multiplies by tube.

### Two things the build turned up that the design did not say

**The pass rule deadlocks a finished turn if taken alone.** "Fewer unactivated units than the
opponent" is exactly right while a turn is being played, and wrong at the end of one: two sides that
have both finished hold zero and zero, neither is fewer than the other, so neither may pass and the
turn cannot end. The shared guard stays worded as the rules word it - it is the rule - and the
degenerate case is handled beside it, because a side with nothing left to activate is not choosing
to pass.

**The interrupt-window count is probably one too many.** A confidence test triggered by opportunity
fire reads as a consequence resolved *inside* the interrupt rather than a window of its own
competing for the stack. Worth settling when Dirtside's policy is written.

Also worth recording, since the two are easy to conflate: StarGrunt's weapon limit is per
activation, while Dirtside's one-weapon-per-combat-action is per element per action. Both express
against the same derived set of resources spent in a frame; the shared layer holds no opinion about
the scope, and should not.

### A rules reading that is counterintuitive and correct

Dirtside's confidence effects are given in two columns, dismounted infantry and armour, and they
**cross over rather than run parallel**. At Shaken it is the *armour* that balks - it needs a
reaction test to leave cover or advance, withdraws to cover if caught in the open, may not
close-assault, and is routed outright if close-assaulted - while Shaken infantry acts normally, its
restrictions biting through the reaction-test system instead. At Broken it reverses: the foot
soldiers stop advancing but keep fighting from where they are, while the armour turns for home and
only returns fire if attacked.

This reads backwards - one expects a tank to be steadier than a man - and it has now been misread
twice during this work, once in a research pass and once in a brief written from it. It is correct:
verified against the vault note and against the raw rulebook text, which agree. The sense of it is
that a rattled crew buttons up and pulls back while infantry are already on the ground and carry on.
Anyone tempted to "fix" it should check the source first.

### Confidence tests are not an interrupt window

Settled during the build, and worth recording because the plan previously implied five windows. A
window exists so the *other* player may choose to spend something: it has a responding side, an
eligibility list, a cap and a cost. A confidence test has none of those - nobody chooses it, nobody
may decline it, and it costs only morale. Modelling it as a window would also mean the
no-self-retrigger guard silently stopped a unit fired on twice from testing twice, which inverts the
rule. So it is a consequence resolved inline between steps, with the interrupted frame resuming
underneath. Four windows, not five.

## Two things worth deciding early

**Fog of war.** StarGrunt ships thirty dummy counters — the largest quantity in the box. Hidden
units, dummy markers, a sniper's alternate positions, face-down artillery impact markers. Bluffing
is a first-class feature, not a nicety, and an app without it loses a large part of the game.
ForceSignal already has the hidden-order commitment machinery, which is the right shape for this.

**How much to adjudicate.** Both games assume a physical table and an umpire's eye: line of sight is
resolved by tape and eyeball, cover is assigned per shot, and target priority is deliberately not
algorithmic. Prompting a player to confirm rather than computing occlusion matches how the games are
actually played, and needs no geometry engine. Target priority in particular should ship as an
advisory hint and never as an enforced rule.
