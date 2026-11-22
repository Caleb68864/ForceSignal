# ForceSignal UX/QA playtest report (2026-11-21)

Tested `origin/master` at 3ae95f4, in an isolated worktree. The API ran on :8817 (Development, SQLite, both ground engines on) and the web app on :6417. Headless Chromium through Playwright, one browser context per device. axe-core 4.10.2 was run on every main screen. Screenshots are in `shots/` and crops in `crops/` next to this file. Logs are `ftA.log`, `ftB.log`, `ftC.log`, `sgA.log`, `sgB.log`, `dsA.log`, `dsB.log`, `kb.log` and `notes.log`.

9 matches were played (4 Full Thrust, 2 StarGrunt, 2 Dirtside, plus a keyboard and help-dialog pass). 42 findings: 0 blockers, 10 major, 24 minor, 8 polish.

**Verdicts:**
- Full Thrust: about 65% of the way to "a dream to use".
- StarGrunt: about 35%.
- Dirtside: about 35%.

**Already good:**
- no horizontal scroll at any of the 5 viewports
- axe found 0 violations
- no crashes and no React warnings
- drafts survive a reload, and an offline guest catches up
- the invite link fills in the room code, and lowercase codes are accepted
- snapshot restore with seat claim works
- Full Thrust's sticky next-step banner is the best UX in the app

**Top 5 changes:**
1. Give every game an end and a "what next" state (M1, M9).
2. Fix the ground-game traps: an enemy as the default target, names instead of GUIDs, refuse an unentered threat level (M2, M3, M8).
3. Fix shared styling: checkboxes, card-grid stretching, map labels that tell ships apart, readable labels instead of code names, sentence-case left-aligned ground text (M4, M5, M6, m6, m19).
4. Lay phones out around the next action: collapse setup, put the activated unit first, 44 px touch targets (M10, m17).
5. Make joining and setup forgiving: spaces in room codes, no duplicate names, warn about all-zero ships and points overruns before Ready, plain-language import errors (M7, m1, m4, m5, m14).

`[RULES]` marks an item that needs the owner's rules knowledge.

## Major

### M1. Full Thrust never ends
- **Steps:** destroy every ship on one side.
- **What happens:**
  - The loser's banner says "Plot your 0 ships (0 locked)…" with LOCK FLEET ORDERS highlighted.
  - The checklist says "Clear to proceed · All your ships have locked orders", which is false.
  - Destroyed ships still show "Awaiting orders", Edit Stats, Duplicate and Repair All.
  - The winner is asked to plot ships for turn 5.
- **Expected:** a "Battle over" screen on both devices with totals, log export and Save Snapshot.
- **Screenshots:** `ftA-13-guest-red-wiped-phone.png`, `crops/gwipe.png`, `ftA-13-host-red-wiped-desk.png`, `ftC-13-*`.
- **Fix:**
  - `lib/nextStep.ts` returns a final step when you have no live ships, or only one side does.
  - `PreTurnChecklist` (`components/ShipCard.tsx`) counts only live ships.
  - Hide order and edit controls on destroyed ships in the `main.tsx` ship grid.
  - Ideally a server "concluded" phase, or a host "End battle" button.
- `[RULES]` early end conditions such as withdrawal or a turn limit.

### M2. StarGrunt Fire and Close Assault default to a friendly unit
- **Steps:** activate Red Bravo and fire at the default target.
- **What happens:** "Red Bravo fired Lasers at Red Alpha… suppressed." In sgB both red squads shot their own "Extra 2". The options don't show sides.
- **Expected:** enemy units first or as the default, the side shown on each option, and a confirm before friendly fire.
- **Fix:** `StarGruntView.tsx` around line 437 (`targets = snapshot.units.filter(u => u.id !== activating?.id && u.figuresAlive > 0)`) and the assault targets around line 632.
- **Screenshot:** `sgA-10-after-fire-phone.png`.
- `[RULES]` whether friendly fire is ever legal.

### M3. Dirtside shows a raw GUID on every platoon row
- **What happens:** every platoon row reads "DE1EF79D-… IS STILL ACTIVATED".
- **Cause:** `GroundCombatSequence.cs:613` uses `CurrentFrame.Unit` (the id) where it needs the unit's name; StarGrunt is fine.
- **Ideally:** show the message once, not on every row.
- **Screenshots:** `dsA-08-fired-0-tabL.png`, `crops/dsB1.png`.

### M4. Checkboxes render as 40–60 px white squares
- **Where:** StarGrunt, Dirtside and Full Thrust's "Animated map" toggle; worst on phone.
- **Cause:** `style.css:102` gives every input `width:100%; min-height:40px; clip-path`.
- **Fix:** `input[type=checkbox], input[type=radio] { width:24px; height:24px; min-height:0; clip-path:none; accent-color:var(--cyan) }`, and make the whole label the tap target.
- **Screenshots:** `crops/sg-a2.png`, `crops/gmap.png`.

### M5. Full Thrust ship cards stretch to the tallest card in their row
- **Where:** desk, tablet landscape and phone landscape.
- **What happens:** next to an expanded card, the other card's chips become empty boxes about 130 px tall and its Focus/Edit buttons about 100 px.
- **Fix:** `align-items: start` on `.ship-grid` (`style.css:607`), and stop `.ship-alerts` and `.card-actions` stretching.
- **Screenshots:** `crops/h-oe.png`, `ftA-T1-09-*`, `ftC-T3-09-*`.

### M6. Map labels are the first 3 letters of the ship name
- **What happens:** all eight "Blue N" ships show as "BLU", and tokens overlap.
- **Cause:** `PlayMap.tsx:906` uses `ship.name.slice(0,3)`; ordnance at line 1287 does the same.
- **Fix:** the shortest abbreviation unique in the fleet (B1…B8), the full name on select, and spread out overlapping tokens.
- **Screenshots:** `ftA-12-host-map-desk.png`, `crops/gmap.png`.

### M7. A room code typed with spaces is refused
- **What happens:** "ZENITH TEMPO LUMEN FALCON" gets "no room", although the code is correct.
- **Fix:** turn spaces and punctuation into dashes on the client and the server, and make the error easier to see.
- **Screenshot:** `ftB-02-guest-join-spaces-tabL.png`.

### M8. StarGrunt confidence test runs with no threat level
- **What happens:** with "Not entered" it rolls as threat 0 and dropped a unit two levels to Shaken. Everywhere else the app refuses numbers nobody entered.
- **Fix:** refuse it in `onTest` (`StarGruntView.tsx`) and add a server check. The Dirtside nerve panel likely needs the same.

### M9. Ground games give no "what now" (StarGrunt and Dirtside)
- **What happens:**
  - After every unit has acted, the status still says "Blue to act".
  - End Turn refuses with "Neither side has run out nor have both passed in succession."
  - Begin Turn stays highlighted mid-turn, and "X goes first" stays enabled.
  - Dirtside's End Turn during choose-first says "No turn is being played" while the header says "Turn 1".
- **Fix:**
  - A ground version of `nextStep.ts`, shown in `NextStepBanner`.
  - Highlight one primary button (`StarGruntView.tsx` around line 455, `DirtsideView.tsx` around line 583).
  - Clear guidance to use the "X passes" buttons.
  - Refusal texts are in `GroundCombatSequence.cs:578-583`.
- `[RULES]` whether a side with no units left to activate passes automatically.
- **Screenshots:** `sgA-11-turn-done-phone.png`, `dsA-09-turn-end-tabL.png`.

### M10. Phone pages bury the button you need
- **Full Thrust:** setup is about 6,300 px tall with Ready at the bottom, and readouts are one per row.
- **StarGrunt:** a turn is about 5,250 px, with Fire below the whole roster and both assault forms.
- **Dirtside:** about 3,500 px.
- **Fix:**
  - Collapse fleet setup to a summary once a fleet exists.
  - 3-column readouts.
  - The activated unit's panel straight under the status bar, and a compact roster.
  - Move "Table, rules and points" to a tab after the start.
- **Screenshots:** `ft1-02-guest-joined-phone.png`, `crops/g-top.png`, `crops/g-bot.png`, `crops/sg-act-half.png`.

## Minor

- **m1. Duplicate display names are allowed** ("Cobalt rolled 6, Cobalt rolled 3"). Refuse on join, or add a suffix.
- **m2. The opponent's damage report uses fixed maximums** (Firecon 0/6, Weapons 0/12). Use the ship's own values (`main.tsx` around lines 2189-2191).
- **m3. Stale order text after reveal.** Opponent cards keep saying "order remains hidden until reveal" next to REVEALED, and ships that held course show "Awaiting orders" during Firing (`main.tsx:2192`).
- **m4. An all-zero ship is accepted silently.** It shows as Operational with a turn cap of 12 at thrust 0. Warn when it's added and in the checklist. `[RULES]` `maxLegalTurn` at thrust 0.
- **m5. A raw JSON parser error on a bad fleet file.** Replace it with "That file isn't a ForceSignal fleet. Nothing was imported." (`main.tsx` import handler, `fleetIo.ts:124`).
- **m6. Raw code names in the ground games:** NotStarted, ChoosingFirstActivator, TurnEnded, GoInPosition, SystemsDownTarget, SoftCover, HullDown, TurretDown. Map them to labels (`StarGruntView.tsx:444`, `DirtsideView.tsx:542`).
- **m7. Scattered, Disorganised and Reorganise** name one StarGrunt state. Pick one term and show the current state on the button.
- **m8. The help dialog doesn't trap focus** (`HelpGuide.tsx`).
- **m9. Enter doesn't submit Create Match or Join Match.** Wrap them in forms (`main.tsx` around line 1530).
- **m10. The legal notice's links look like strike-through text.** Use plain inline links.
- **m11. Dropdowns cut off their text** ("Nothing sav", "Beam b", "Crimsor", and the phone contact list at 130 px).
- **m12. Max Accel silently wipes plotted turns.** Say so. `[RULES]`
- **m13. Host-only controls appear on the guest's screen** (points limit, table size, profile Forget/Start Blank). Disable them for the guest with "The host sets this". The server's response wasn't checked.
- **m14. A points overrun only shows after pressing Ready.** Show it in `readyBlocker` and the checklist.
- **m15. The log is mostly per-ship snapshot rows.** It reaches 300 entries by turn 5, and the Log tab takes about 770 ms to open. Fold each phase's snapshots, add a filter, and render only visible rows.
- **m16. CSV-imported ships get a placeholder weapon** ("Unnamed Mount 1D/1") that later shows as out of range (`fleetIo.ts:79`, `:226`).
- **m17. Touch targets under 44 px:**
  - phone portrait: arc buttons 34×48, the banner × 25×19, the "New here?" link 19 px, the Table/rules header 31 px
  - tablet and phone landscape: most buttons 34–40 px
  - Use `@media (pointer: coarse)` (`notes.log`).
- **m18. The sticky banner covers about 23% of a phone in landscape**, and cards scroll up under it. Add scroll margin and auto-hide the notice.
- **m19. Ground text is all capitals, centred and amber**, and results look like help text. Use sentence case, left-aligned, success/alert styling, and fold long text behind a "?".
- **m20. The StarGrunt roster has no order and no side colours.** The active unit is barely highlighted, "still activated" repeats on every card, and "Confiden/t" breaks mid-word on desk. Group by side and highlight the active unit.
- **m21. StarGrunt allows shots at 30–42″ with "0 potential hits".** `[RULES]`
- **m22. Full Thrust mixes your cards with the opponent's.** Show yours first (`focusedFirstShips`).
- **m23. Reveal is offered during order entry and then refused.** Hide it until the Reveal phase (`main.tsx` around line 2084).
- **m24. A second device can't follow a ground game.** Fixed since by PR #16 (multi-device ground games).

## Polish

- **p1. Grammar:** "Revealed 1 friendly orders" (`main.tsx:892`), "1 hit(s)" (`DirtsideGame.Fire.cs:373`), "Red Lancers's".
- **p2.** Map readouts wrap mid-number.
- **p3.** A meaningless "Ready." notice shows, and a reload loses the scroll position.
- **p4.** Vague activity messages (`format.ts:44`).
- **p5.** The landing page's "NO MATCH" looks like a button, and header buttons wrap at 1440 px.
- **p6.** The help step strip is cut off on phone.
- **p7.** Every refused action logs a red 400 error to the console.
- **p8.** Lock Fleet Orders with most ships unplotted needs a confirm.

## Design suggestions

1. One "what now" banner across all three games, modelled on Full Thrust's.
2. A one-row-per-ship plotting table, "copy to all" and plotting from the map; 8 ships currently take about 30 taps a turn.
3. Skip ships with nothing in arc or range during firing.
4. Let ground games copy the last game's rules profile.
5. A taller phone map (it's about 250 px) and movement trails.
6. On the guest's device, show "Waiting for the host's rules profile".
7. Ground results as a card, like Full Thrust's shot result card.

## Needs the owner's rules knowledge

M1 (end conditions), M2 (friendly fire), M8 (threat 0), M9 (forced pass), m4 (turn cap at thrust 0), m12 (acceleration and turns sharing thrust), m21 (shots beyond effective range).
