# How to play with ForceSignal (draft help, 2026-11-09)

Plain-language help for a brand-new player. Each section is one step of a match, with the image to show beside it. Images are in `src/ForceSignal.Web/public/help/`, served at `/help/<file>`.

Notes for whoever builds the Help component:
- Steps 3 and 7 describe how the app behaves today, including some rough edges from the UX review (for example, the owner has to press **Advance Turn** after reveal). Update those sentences when the fixes land.
- ForceSignal ships no rules numbers. The help text must never quote dice values, ranges or tables. It says where to enter them, not what they are.

---

## 1. Start or join a match

One player taps **Create Match** and becomes the host. The host's screen then shows a four-word **room code**. Read it out or tap **Copy Code** to share it. Everyone else types their name and the code, then taps **Join Match**. A phone works fine for this.

Images: `help/01-create-or-join.png` (desktop), `help/01-create-or-join-phone.png` (phone), `help/02-room-code.png` (room code with Copy Code)

## 2. Set the rules profile (host)

ForceSignal has no built-in rules numbers, so the host enters the ones from your own rulebook once, under **Rules profile → Edit Numbers**. Alternatively, load a saved file with **Import JSON**. The list in amber shows exactly what is still missing. When it is empty, tap **Save & Play Against This**. Your browser remembers the profile for next time. No one can start until a profile is set.

Image: `help/03-rules-profile.png`

## 3. Build your fleet

Each player adds their own ships under **Fleet setup → Add ship**. A preset (Cruiser, Carrier, Fighters...) only sets the ship's shape and icon. Take the stats (thrust, hull boxes, screens, fire controls) from your own record card. Add each weapon with **Add** under Weapons: pick its type, its dice (or tubes), its range, and the arcs it can fire through. Then tap **Add Ship**. Course is a clock face: 12 points up the table, 3 points right.

Image: `help/04-add-ship.png`

## 4. Ready up

When your fleet is complete, tap **Ready** under **Match commands**. Once every player is ready, the match moves to **Order entry** and the top bar shows the phase and turn number.

Image: `help/05-ready.png`

## 5. Plot your orders

Open **Play Map** and tap one of your ships, then the **Helm** tab. Right-click, tap, or long-press empty space to plot where it should go. The arrow shows the path it will really fly, and the panel says whether that is within its thrust. You can also plot from the ship card: use the compass, **Max Accel** / **Max Decel**, or **Port +1** / **Starboard +1**. A ship you leave alone keeps its speed and heading.

Images: `help/06-plot-on-map.png` (desktop map), `help/06-plot-on-card-phone.png` (ship card on a phone)

## 6. Lock your orders

When you are happy with every ship, tap **Lock Fleet Orders**. This seals your orders so the other player cannot see them, and tells the table you have finished plotting. Changed your mind before everyone has locked? Tap **Resume Plotting**. The **Pre-turn checklist** shows what is still missing.

Image: `help/07-checklist-locked.png`

## 7. Reveal and move

Once every player has locked, each player taps **Reveal Fleet Orders**. When all orders are revealed, the host taps **Advance Turn**, and every ship moves at once. Check the map to see where everyone ended up.

Image: `help/08-after-movement-map.png`

## 8. Fire

The app rolls to decide who fires first, and the two sides then take turns, one ship at a time. On **Play Map**, tap your ship, open **Fire**, pick a **Target** and a **Weapon**, and tap **Use Map Solution** to measure the range. If the line above the target says **Ready**, tap **Fire**. When that ship has finished, tap **Done Firing** (or **Hold Fire** if it won't shoot) to hand the turn over. If Fire is greyed out, the line at the top of the panel says why: not your turn, out of arc, out of range, or the weapon has already fired.

Images: `help/09-fire-console.png` (desktop), `help/09-fire-console-phone.png` (phone), `help/09-fire-refused.png` (a refused shot and its reason)

## 9. Read the results

Every roll the app makes goes into the **Log** tab, newest first: what each ship fired at, the dice rolled, and how much armour and hull damage it did. Damage to your ships also shows on their cards under **Damage**. Export the log as **MD**, **CSV** or **JSON** for an after-action report.

Image: `help/10-battle-log.png`

## 10. Next turn

When everyone has finished firing, the host taps **Advance Turn**. The top bar changes to **Order entry · Turn 2**, and you plot again from step 5. Damage, ammunition and fighter states carry over from turn to turn.

Image: `help/11-next-turn.png`
