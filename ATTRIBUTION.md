# Attribution

## Ground unit icons — game-icons.net

The ground unit icons in `src/ForceSignal.Web/src/assets/unit-icons.svg` come from
[game-icons.net](https://game-icons.net) and are included under
[Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/).

**No screen renders one yet.** `UnitIcon.tsx` is imported by nothing but its own test, so the set is
tree-shaken out and a built bundle contains none of it. That does not make the credit optional: the
licence asks for it wherever the work is *distributed*, and this repository distributes the sheet
whether or not the app draws from it. Crediting artwork that ships and is not shown is generous;
deleting the credit while the file is still in the tree would be a licence breach, and removing the
file is the only thing that would make the credit unnecessary.

So the wording in each of the three places says *shipped*, not *used* — a smaller claim, and the
true one. If the icons are ever wired into a screen, the stronger wording is correct again;
`unitIconCreditIsTrue` in `UnitIcon.test.ts` fails on the day that happens, so the sentence cannot
quietly stay understated either.

The three copies are this file, the header of the icon sheet itself, and the app's on-screen notice
panel, so removing one by accident does not silently put the project out of licence.

Icons made by the following artists, available on <https://game-icons.net>:

| Artist | Icons in the sheet |
|---|---|
| **Lorc** | `battle-tank`, `cannon`, `flying-flag`, `missile-pod`, `radar-dish` |
| **Delapouite** | `battle-mech`, `crosshair`, `delivery-drone`, `helicopter`, `jet-fighter` |
| **Skoll** | `apc`, `jeep`, `machine-gun`, `minefield`, `trench-body-armor` |
| **sbed** | `medical-pack`, `rifle`, `turret` |

### What was changed

Creative Commons Attribution allows modification provided the credit stands. Two changes were made,
and no path data was altered:

- The full-bleed black square each icon ships behind its glyph was removed, so an icon is a shape
  rather than a black tile on a dark map.
- The explicit white fill was dropped, so a glyph inherits `currentColor` and can be tinted per
  side like everything else on the map.

### If you add or remove an icon

Update this table, the header comment in `unit-icons.svg`, and `unitIconOptions` in
`src/components/UnitIcon.tsx`. The tests in `UnitIcon.test.ts` check that the sheet and the app
agree on which icons exist and that the credits are still present, so they will fail if one is
forgotten.

## Ship icons — original

The Full Thrust ship class silhouettes are **not** from any icon pack. They are original inline SVG
drawn for ForceSignal and live in `shipIconPath` in
`src/ForceSignal.Web/src/components/ShipCard.tsx`. They carry no third-party licence obligation.

## Rules content

ForceSignal is an unofficial companion and is not affiliated with, endorsed by, or sponsored by
Ground Zero Games. Full Thrust, Stargrunt and Dirtside are theirs.

No rules text, tables, ship record sheets, fleet lists, faction background, artwork or points values
are bundled. The app implements mechanics — which are procedures rather than expression — and every
number a game needs is entered by the player from rules they own. See the content policy in
`README.md` and in `docs/ground-combat-plan.md`.
