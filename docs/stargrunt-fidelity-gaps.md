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

## Gap 2 - Confidence never changes - FIXED 2026-08-10

**Severity: high.** `ConfidenceLadder` implements the test, the ladder and the two-level failure,
and the game layer never calls any of it. Every unit is permanently Confident. The unit card shows a
confidence level that is decoration.

**Fixed.** `TakeConfidenceTest` rolls the quality die against leadership plus threat, dropping a
level on a miss and two on a bad one.

Two shape decisions worth recording. It is **not an action and not tied to an activation**: a test
is taken the moment the triggering event happens, to whichever unit it happened to, which is usually
a unit that has not gone yet and may never go this turn. And the **threat level is supplied, not
derived** - it comes from a table rated against the force's mission motivation, and that table is
the user's own, exactly like the firepower die. What the app owns is the procedure.

Because the level is declared, the errata about only *basic* entries triggering a test is the
player's to apply when they read their table. The app never decides that a test is owed.

## Gap 3 - Rally and Reorganise are buttons that do nothing - FIXED 2026-08-10

**Severity: high.** Both are offered by the screen, both spend an action, neither has any effect.
Rally should restore one confidence level on a successful roll against the summed leadership values
of both units, after a successful communication, capped by fatigue and never above Confident.
Reorganise should clear the disorganised state.

**Fixed.** Both are commands rather than steps now, and the generic step route refuses them by name.

Rally puts the action on the **rallying** unit and the roll on the **rallied** one, against both
leadership values added together - unusual enough to be worth saying out loud. One level per
success, never past what fatigue allows, and refused up front when there is nothing to gain so a
commander cannot waste an action finding out.

Writing it turned up a rule I had missed entirely: **only a superior command element may rally**, so
two squads cannot talk each other round. The check is on command level, and the screen only offers
rally against subordinates on the same side.

Reorganise clears the disorganised state. Whether a unit is scattered is measured with a ruler at
the table, so it is declared rather than computed - the same stance taken on cover and range - and
the screen has a button to say so.

Fatigue came with them, which closes most of gap 8: it caps where confidence starts and how far a
rally can bring it back, and rallying is simply wrong without it.

## Gap 4 - Wounds are paired across the squad rather than per figure - FIXED 2026-08-10

**Severity: high - it killed more people than the rules do.**

Wounds and kills are allocated **randomly across the squad's figures**, and a figure taking two
wound results *in one resolution* is dead. The code totals the volley's wounds and converts every
two of them into a death regardless of who they landed on.

With two wounds against an eight-figure squad the rules kill somebody roughly one time in eight;
the code killed somebody every time.

**Fixed.** Hits are allocated to figures at the point of application, through an `IFigureAllocator`
kept separate from the die source - it is a choice among the figures standing, not a die roll, and a
test needs to be able to say exactly where each hit went. A figure taking two wound results in one
resolution dies; wounds from separate volleys never pair, because the rule is scoped to a single
resolution.

The randomness is the rule rather than flavour, which the fix proved immediately: making allocation
real turned a serialization test non-deterministic, because a kill landing on the leader suppresses
the squad a second time. Every scripted volley in the tests now scripts its allocation too.

## Gap 5 - Lingering wounds are recorded and never used - FIXED 2026-08-10

**Severity: low, but actively misleading.** `FiguresWounded` accumulates across resolutions, is
carried in the snapshot and is shown on the unit card. Nothing ever reads it. Since the death rule
is scoped to a single resolution, a wound that does not pair inside that resolution has no further
mechanical life.

**Fixed, and the audit's first reading was wrong.** Checked against the source: a wounded figure is
a **casualty**, not a trooper fighting on hurt. It comes out of the fighting strength and stays with
the unit, which has to carry it - a squad cannot carry more casualties than it has fit troops, and
carrying them makes it encumbered.

So the count means something after all, and something the player needs: **each untreated casualty
raises the threat level** on the confidence table, and abandoning wounded raises it further. It is
now labelled Casualties on the card rather than Wounded, and a wound moves a figure from the
fighting strength into it.

## Gap 6 - A leader casualty does not suppress the squad - FIXED 2026-08-10

**Severity: medium.** Errata: a wounded or killed squad leader automatically hands the squad a
suppression marker. Casualties are applied with no notion of who was hit, so this never fires.
**Fixed**, and it depended on gap 4: the marker cannot be handed over without knowing who was hit.
The squad leader is the first figure while he is standing, so a hit allocated there is a hit on him.
The marker is handed over exactly once - losing a leader suppresses the unit the moment it happens
and does not keep suppressing it every time somebody else is shot.

## Gap 7 - No reaction test, so leaving cover is never gated - FIXED 2026-08-10

**Severity: medium.** The activation policy already reads `NextMoveLeavesCover` and
`ReactionTestCleared` and refuses the move until the caller has passed the test - the seam is built
and documented. The game layer never set either flag and never offered the test, so the gate stood
permanently open.

**Fixed.** Declaring that a move leaves cover is a player call - whether it does is an eyeball
judgement, which is what the flag's own documentation always said - and the test is then offered
against a threat level the player supplies.

The shape follows the one difference from a confidence test: **failing costs the action, never a
level.** So passing spends nothing by itself, because the move that follows spends the action;
failing spends one and bars a second attempt at the same order in the same activation, leaving the
unit to do something else with what it has left.

That last part needed a small addition to the module's vocabulary. The action economy is derived
from the steps in a frame, so a lost action has to *be* a step or it is not lost at all - hence
`StarGruntAction.RefusedOrder`, which reads oddly in a list of things a unit chooses to do because
it is the one thing there a unit does not choose.

## Gap 8 - Fatigue is not modelled - MOSTLY FIXED 2026-08-10

**Severity: medium.** Fatigue sets the starting confidence cap - Fresh, Tired and Exhausted each
cap where a unit begins and how far it can be rallied back.

**Fixed as far as rallying needs**, because rallying is wrong without it. Fatigue is now a scenario
property of the unit, set when it is added and carried in force files, and it both sets opening
confidence and caps recovery. What is still missing is anything that *changes* fatigue during a
game - it is set once and never moves.

## Gap 9 - Close assault is not built at all - FIXED 2026-08-10

**Severity: known and previously recorded.** Item 4 of `ground-combat-plan.md`. The whole
resolution - initiating, terror effects, casualties in close combat, the routed-on-assault rule for
broken units - was absent.

**Fixed.** The engine holds the procedure and the two threat levels a close assault needs, both of
which are countable rather than tabled: the nerve a charge asks follows from the attacker's own
confidence, and the nerve to stand follows from the odds, with power armour worth two men and terror
doubling the result. The melee is the rulebook's, including the parts that catch people out - a tie
settles nothing, weapon shifts are open so an overflow comes off the opponent's die, power armour
doubles the score after the roll rather than the die before it, and cover helps a defender in the
first round only. The rulebook's shotgun-against-power-armour example runs as a fixture.

What became of a downed figure is rolled at the end rather than when he falls, because a stunned man
gets up again on the winning side and is taken on the losing one - which is not known while the
fighting is still going on.

**Deliberately not modelled: who fights whom.** The attacker pairs one figure per defender and the
defender allocates the leftovers, and that rule exists precisely to stop an attacker choosing to gang
up on leaders and specialists. It is a decision between two people over a table, so the pairing is
sent in and the app rolls it.

**Also not modelled: the assault as a tracked state.** The app helps with each step - the charge, the
stand, a round of melee, the fates - rather than holding a multi-round assault and walking the
players through it. Whether that is worth building is a question for after somebody has played one.

## Gap 10 - Support weapons are not distinguished when firing - FIXED 2026-08-10

**Severity: low as written, and the write-up understated it.** `WeaponProfile.IsSupport` was
recorded and never read, and the volley took support dice as a free-form list the player typed per
shot.

Reading the rule turned a tidy-up into a missing rule. Folding a support weapon into squad fire is a
**trade**: it adds its die to the volley, and in exchange it **may not also fire on its own that
activation**. With loose dice the app could not know a weapon had been used, so it could not hold
anyone to the second half.

**Fixed by naming the weapons instead of the dice.** A volley names which of the unit's own support
weapons join it; their firepower dice come off the roster; and every weapon in the volley is spent
in the frame, so the per-activation limit refuses the support weapon a second, separate shot without
any new rule being written - the limit was already read off the steps.

Two smaller things came with it. A support weapon carries a **support firepower die** distinct from
its impact die, because folding one in adds weight and never its own heavier punch. And a weapon can
be marked as **never joining squad fire**, since the rules exclude one man-portable type outright -
a flag on the user's own card rather than a list of weapon names here, which would be shipping part
of their table.

The policy's weapon gate now checks every weapon a step names rather than only the first.

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
2. ~~**Gap 2 and 3** - confidence tests, rally, reorganise.~~ **Done**, and they closed most of gap 8
   on the way, because rallying cannot be right without fatigue.
3. ~~**Gap 4, 5 and 6** - casualty allocation onto figures.~~ **Done.**
4. ~~**Gap 7 and 8** - reaction tests and fatigue.~~ **Done**, bar fatigue that changes mid-game.
5. ~~**Gap 10** - decide whether support weapons are derived or declared.~~ **Done**: named, not
   declared as dice, which is what made the trade enforceable.
6. ~~**Gap 9** - close assault, as its own piece of work.~~ **Done**, bar the assault-as-state
   question above.

The first three items are all the same shape: the engine is built and tested, and the game layer
has to call it. That is a day's work, not a rewrite, and it is the difference between a screen that
tracks a game and one that plays it.
