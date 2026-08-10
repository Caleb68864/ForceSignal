# StarGrunt II Rules Fidelity Gaps

_First audit 2026-08-10, against the vault digest at `Notes/Ground Zero Games/Stargrunt II` and the
official errata summary. Same treatment `rules-fidelity-gaps.md` gave Full Thrust._

No rule text, tables or numbers from the source are reproduced here. Each gap names what the code
does, what the rules require, and what to change - the decision, not the rule.

## Why this audit exists

Full Thrust got a systematic scan against its rules and it found twenty-three divergences in code
that already looked finished, several of them structural. StarGrunt's engine was built carefully and
with errata already folded in, but had never had the same treatment - and as of today it is
reachable, so a wrong rule is something a real table hits rather than a theoretical concern.

The scan found the engine itself in good shape. Almost every gap below is in the **game layer built
on top of it today**: the rules modules implement mechanics the game never calls.

---

## Gap 1 - Suppression can never be removed, so the game stops after first contact - FIXED 2026-08-10

**Severity: critical. Nothing built on this was playable past a few turns.**

Fire adds a suppression marker through `Suppression.Add`. **Nothing anywhere removes one.**
`Suppression.TryClear` exists, is tested, and is never called by the game layer; the
`RemoveSuppression` action spends an action and has no effect at all.

A suppressed unit may not move or fire. So the first squad shot at is pinned for the rest of the
game, and the game reaches a state where neither side can do anything but pass. A single turn of
browser testing did not reveal this, because it takes a second activation to notice.

**Fixed.** `RemoveSuppression` is a command of its own rather than a step, for the same reason
firing is: it rolls, and the generic step route deliberately carries no die source. One action, one
roll, one marker at best, and the action is spent whether or not it works - which is most of what
suppression costs, since a unit under sustained fire is out of the fight for turns without taking a
casualty. The step route now refuses the action by name rather than silently doing nothing.

Fixing it turned up **gap 11**, below: the roll is against a Leadership *Value*, and the unit model
had a leadership die.

## Gap 2 - Confidence never changes

**Severity: high.** `ConfidenceLadder` implements the test, the ladder and the two-level failure,
and the game layer never calls any of it. Every unit is permanently Confident. The unit card shows a
confidence level that is decoration.

**Required:** a confidence test is triggered by threat events, rolled as quality die against
leadership plus threat, failing by a level and by two on a bad enough roll. Note the errata: tests
are triggered only by *basic* threat entries, and the "+" entries only modify a test already
triggered. Treating every entry as a trigger makes units test far too often.

## Gap 3 - Rally and Reorganise are buttons that do nothing

**Severity: high.** Both are offered by the screen, both spend an action, neither has any effect.
Rally should restore one confidence level on a successful roll against the summed leadership values
of both units, after a successful communication, capped by fatigue and never above Confident.
Reorganise should clear the disorganised state.

These are the same class of bug as gap 1: the action economy is honoured, the consequence is not.

## Gap 4 - Wounds are paired across the squad rather than per figure

**Severity: high - it kills more people than the rules do.**

Wounds and kills are allocated **randomly across the squad's figures**, and a figure taking two
wound results *in one resolution* is dead. The code totals the volley's wounds and converts every
two of them into a death regardless of who they landed on.

With two wounds against an eight-figure squad the rules kill somebody roughly one time in eight;
the code kills somebody every time. The fire engine deliberately does not attribute hits to
individual figures, so fixing this means allocating hits to figures at the point of application -
which is where the rules put it.

## Gap 5 - Lingering wounds are recorded and never used

**Severity: low, but actively misleading.** `FiguresWounded` accumulates across resolutions, is
carried in the snapshot and is shown on the unit card. Nothing ever reads it. Since the death rule
is scoped to a single resolution, a wound that does not pair inside that resolution has no further
mechanical life.

**Required:** either give it a meaning the rules support, or stop displaying a number that means
nothing. The audit's reading is that it has no lasting effect and should go.

## Gap 6 - A leader casualty does not suppress the squad

**Severity: medium.** Errata: a wounded or killed squad leader automatically hands the squad a
suppression marker. Casualties are applied with no notion of who was hit, so this never fires.
Related to gap 4 - both need casualties to land on identified figures.

## Gap 7 - No reaction test, so leaving cover is never gated

**Severity: medium.** The activation policy already reads `NextMoveLeavesCover` and
`ReactionTestCleared` and refuses the move until the caller has passed the test - the seam is built
and documented. The game layer never sets either flag and never offers the test, so the gate is
permanently open.

## Gap 8 - Fatigue is not modelled

**Severity: medium.** Fatigue sets the starting confidence cap - Fresh, Tired and Exhausted each
cap where a unit begins and how far it can be rallied back. `Confidence.cs` supports it. The unit
model has no fatigue field, so every unit is Fresh forever and rallying has no ceiling.

## Gap 9 - Close assault is not built at all

**Severity: known and previously recorded.** Item 4 of `ground-combat-plan.md`. The whole
resolution - initiating, terror effects, casualties in close combat, the routed-on-assault rule for
broken units - is absent. Listed here so the fidelity picture is complete in one place.

## Gap 10 - Support weapons are not distinguished when firing

**Severity: low.** `WeaponProfile.IsSupport` is recorded and never read. The fire command takes
support dice as a free-form list from the player instead of deriving which of the unit's own support
weapons are joining the volley. Defensible while the player is transcribing everything by hand, but
the flag is currently dead weight and should either be used or dropped.

## Gap 11 - Leadership was modelled as a die - FIXED 2026-08-10

**Severity: high, and it blocked gap 1.** `UnitDefinition` carried a `LeadershipDie` on the quality
ladder. The rules use a **Leadership Value** of 1 to 3, where 1 is best, printed on the activation
marker - and every roll against a leader has to beat that number. Nothing in the game ever rolls a
leadership die.

Found while wiring gap 1: `Suppression.TryClear` takes an integer, and there was no honest way to
pass it one. The engines were right and the model built on top of them was wrong.

Now `LeadershipValue`, validated 1 to 3 at the boundary, and the screen offers those three rather
than a die. Rally will want the same value, so this unblocks gap 3 as well.

---

## Not gaps - checked and correct

- **The fire engine itself.** Opposed roll, fail / suppress-only / fully effective, totalling all
  the firer's dice including losers, potential hits by dividing by the range die *type*, the
  remainder roll, impact against armour with no modifiers. Both worked examples run as fixtures.
- **The weapon fire limit is per activation.** The digest's "once per turn" is the uncorrected
  printed wording; the official errata changes it, and the code follows the errata by keeping the
  limit in the activation frame's spent resources. Confirmed against the errata summary.
- **Target rolls exactly one die.** The printed "two" was a misprint and is corrected.
- **Cover shifts both the range die and the armour die**, and impact against armour is an *open*
  shift. Both errata already folded in.
- **Two moves are a dash**, declared as one, so the reaction window can open at the mid-point.
- **Suppression caps at three** and gates exactly the right actions - observe, communicate,
  reorganise only in cover, remove suppression.
- **Alternating activation, the pass rule and the fewer-units-chooses rule.**
- **Two actions per activation**, with two fire actions allowed only using different weapons.

## Suggested order of work

1. ~~**Gap 1** - suppression removal.~~ **Done**, along with gap 11 which it uncovered.
2. **Gap 2 and 3** - confidence tests, rally, reorganise. The morale half of the game.
3. **Gap 4, 5 and 6** - casualty allocation onto figures, which gaps 5 and 6 both hang off.
4. **Gap 7 and 8** - reaction tests and fatigue.
5. **Gap 10** - decide whether support weapons are derived or declared.
6. **Gap 9** - close assault, as its own piece of work.

The first three items are all the same shape: the engine is built and tested, and the game layer
has to call it. That is a day's work, not a rewrite, and it is the difference between a screen that
tracks a game and one that plays it.
