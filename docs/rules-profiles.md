# Rules profiles: where the numbers live

ForceSignal ships no rules numbers. A match is played against a **rules profile** its players fill
in from their own rulebook and record cards, and the app refuses to play against an incomplete one.

## The line

**The engine owns procedures. The player owns every number those procedures read.**

Procedures are code: what is rolled against what, what a screen does to a die, what order things
happen in, that a beam loses dice with range at all, that completing the last hull row is death
rather than another check, that point defence chains.

Numbers are input: die faces, the beam damage table, range band widths, to-hit ladders, threshold
row counts, repair rolls, needle kill rolls, reach, salvo sizes, what each point-defence face
shoots down, what each turnaround face means.

## Why there is no default

There was, twice, and both were the same mistake.

The first was two ready-made profiles named after published rules layers, with those layers'
numbers written into them. The second was smaller and easier to miss: the numbers that had never
made it into a profile at all - the beam damage table, the torpedo ladder, damage-control rolls,
carrier allowances - sitting as constants in the rule classes, each one justified at the time as
too small to matter.

A default that happens to be somebody's published numbers is still those numbers, shipped. So
`RulesProfile.Empty` is blank, `docs/rules-profile-template.json` is blank, and the client's form
starts blank. The app says what is missing rather than filling it in.

## What this buys, besides the obvious

Every table's house rules work. A group that plays with a different die, kinder repair numbers, or
a shorter beam band enters those and the app follows. Nothing about the engine assumed six faces.

The tests prove it. `TestRules.Invented` - an eight-sided die, ten-mu beam bands, three hull rows,
a needle that kills on an 8 - is deliberately nobody's published game. A test that passes against
it proves the code read the profile it was handed rather than remembering something. A fixture
copied out of a rulebook would prove neither, and would put the rulebook back in the repository by
the back door.

## Filling one in

In the app, under **Rules profile** during fleet setup: enter the numbers once and the browser
keeps them. Export to JSON to share one file across a table, or import
`docs/rules-profile-template.json` and fill it in by hand.

A section your table does not use can be left at zero. Only a half-entered section is refused: a
salvo size with no attack radius is somebody stopping half way, while an empty ordnance section is
a choice.
