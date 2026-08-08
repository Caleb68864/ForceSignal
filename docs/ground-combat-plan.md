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
- Quality dice, firepower ratings, impact and armour dice, threat levels, and chit-validity colours
  are all **inputs**, entered on a user's own record card.
- The damage-chit pot composition is exposed as configuration rather than baked in, for the same
  reason — and because the published distribution is not fully verified.
- The vault's `Force Building/{NAC,ESU,FSE,NSL}` notes and `Community/Faction Army Lists` are the
  highest-risk content in the whole vault. Nothing from them is mined into the app.

## Layering

```
ForceSignal.Modules.GroundCombat      shared by both games
    Dice/        quality ladder, closed and open shifts, the three roll shapes  [BUILT]
    Sequence/    alternating activation, initiative, the pass rule, turn end    [TODO]
    Morale/      confidence ladder and the test procedure                       [partly, see below]
    Design/      size class, capacity, armour by facing, points                 [TODO]

ForceSignal.Modules.StarGrunt         figure scale
    Combat/      range bands, the fire sequence vs dispersed targets            [BUILT]
    Morale/      confidence, fatigue, suppression 0-3                           [BUILT]
    Assault/     close assault                                                  [TODO]

ForceSignal.Modules.Dirtside          vehicle scale
    Combat/      two-stage hit and damage resolution                            [Stage 1 BUILT]
    Chits/       the damage pot, composition and validity                       [TODO]
    Support/     the deferred artillery queue                                   [TODO]
```

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
5. **The unit and force model, then contracts, then the API surface** behind the flags.
6. **Icons.** Neither game uses NATO symbology, so the whole visual language is status markers and
   can be original. The vault already carries a clean-room vocabulary in
   `Dirtside II/Reference/dsii_markers.scad` — plain lettered discs reproducing none of GZG's
   colour, artwork or layout — and adopting it directly is IP-safe by construction and matches the
   physical play aids already on hand. StarGrunt additionally wants a single identity badge carrying
   quality and leadership together, with "spent" rendered on the badge rather than as a second
   token, and suppression as a numeric badge rather than three stacked icons.

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
