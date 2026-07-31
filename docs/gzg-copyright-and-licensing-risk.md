# ForceSignal GZG Copyright and Licensing Risk Note

_Prepared July 30, 2026. This is a practical engineering/product risk note, not legal advice._

## Short Answer

I cannot truthfully say "ForceSignal breaks X copyright laws." Copyright risk is not counted that way, and a real infringement finding depends on jurisdiction, facts, distribution, copied material, and a lawyer's review.

Based on the current app source, ForceSignal does **not appear to bundle GZG PDFs, copy rulebook prose, include official art, or ship official fleet lists**. That is good.

The app still has **moderate IP risk** because it is explicitly tied to _Full Thrust_, implements GZG game mechanics, uses FT-flavored terminology such as beam classes and arcs, and could be mistaken for an official/endorsed digital implementation if presented carelessly.

## Current App Risk Inventory

| Area | Current Risk | Why It Matters | Mitigation |
| --- | --- | --- | --- |
| Game mechanics implemented in code | Low to moderate | Mechanics are generally less protected than copied expression, but exact presentation can create risk. | Keep implementation as original code and original wording. Avoid copying tables/prose from PDFs. |
| Use of "Full Thrust" / "Ground Zero Games" / "GZG" | Moderate | Names may be trademarks or source identifiers. | Use an explicit unofficial compatibility disclaimer. Ask GZG for permission to use compatibility wording. |
| Rule text, charts, ship cards, SSD layouts | High if copied | Rulebook text, tables, diagrams, and layout are protected expression. | Do not copy them into the UI, docs, seed data, screenshots, or exports. |
| Official PDFs | High if bundled or mirrored | Free download does not automatically grant permission to redistribute through our app. | Link to GZG's official rules page instead of bundling PDFs. |
| Official artwork, logos, ship silhouettes, faction lore | High if copied | Art, logos, setting text, and faction material are protected. | Use original UI art, generic icons, and user-created fleets. |
| Official ship designs or fleet books as built-in data | Moderate to high | Stat blocks and selection/arrangement may be protectable, especially if copied wholesale. | Do not ship official designs. Let users enter/import their own ships. |
| Commercial hosting/SaaS | Higher | Any revenue makes permission expectations stricter. | Keep private/non-commercial until GZG gives written permission. |

## What We Found

GZG currently hosts rule downloads on its own store rules page, including _Full Thrust_, _More Thrust_, Fleet Books, _Dirtside II_, _Stargrunt II_, and _FT Light_:

- <https://shop.groundzerogames.co.uk/rules.html>

GZG's contact page lists Jon at `jon@gzg.com` for product/order enquiries:

- <https://shop.groundzerogames.co.uk/contact-us.html>

The _FT Light_ PDF appears to grant the clearest public reuse permission: free non-commercial duplication/distribution of the complete, unaltered document. That is useful, but it does not automatically authorize a web app that extracts, rewrites, or commercializes the rules:

- <https://downloads.groundzerogames.co.uk/FTLrules.pdf>

The main _Full Thrust_ and Fleet Book PDFs appear to keep normal copyright notices and limited permissions, such as personal-use photocopying for record sheets. Free availability is not the same thing as permission to copy text, tables, art, or layouts into ForceSignal:

- <https://downloads.groundzerogames.co.uk/FullThrust.pdf>
- <https://downloads.groundzerogames.co.uk/FB1Full.pdf>
- <https://downloads.groundzerogames.co.uk/FB2Full.pdf>
- <https://downloads.groundzerogames.co.uk/mt.pdf>

There is precedent for permission-based third-party use. BITS' _Power Projection_ materials and other third-party products reference using Full Thrust material/trademarks with permission, but I did not find a public self-serve licensing program or fan-tool policy:

- <https://www.bitsuk.net/powerprojection/files/0e320184bed534194875c21570af6562-12.html>

## Recommended Mitigation Plan

1. Add a visible disclaimer:
   - "ForceSignal is an unofficial tabletop companion. It is not affiliated with, endorsed by, or sponsored by Ground Zero Games."
   - "Full Thrust and Ground Zero Games are trademarks/property of their respective owners."

2. Change product wording where possible:
   - Safer public wording: "space fleet tabletop companion."
   - Use "Full Thrust-compatible profile" only if GZG is comfortable with compatibility wording.

3. Do not include official content:
   - No PDF uploads bundled into the repo/app.
   - No copied rule text.
   - No official ship SSDs, official fleet lists, faction background, logos, or artwork.
   - No pasted tables from Fleet Books.

4. Link outward:
   - Add a "Rules" link to GZG's official rules page.
   - Let users provide their own legally obtained rules.

5. Keep imports user-owned:
   - JSON/CSV fleet import is fine as a tool, but avoid shipping official GZG fleets as sample data.

6. Ask for written permission before broader release:
   - Permission to say "Full Thrust-compatible."
   - Permission to implement and distribute a digital play aid.
   - Permission, if desired, to include any official rules summaries, ship sheets, or references.
   - Permission for commercial hosting, Patreon, paid SaaS, or app-store distribution.

## Suggested Email To GZG

Subject: Permission request for unofficial Full Thrust digital play aid

Hello Jon,

I am building ForceSignal, a self-hosted multiplayer tabletop companion for hidden movement orders, ship damage tracking, firing logs, and after-action records for space fleet games.

I would like to support Full Thrust players without misusing Ground Zero Games material. The app does not need to bundle your PDFs, official artwork, faction lore, or official ship designs; it can link users to your official rules page instead.

Could you let me know whether you would allow:

- describing the app as "Full Thrust-compatible";
- implementing movement, firing, damage, and ship-record helpers as original code;
- distributing the tool for non-commercial self-hosted use;
- possibly offering hosted/commercial access later, if separately approved;
- including a small attribution and unofficial-use disclaimer.

If you have preferred wording, licensing terms, or restrictions for fan digital tools, I would be happy to follow them.

Thank you,

Caleb

## Practical Bottom Line

The current app is not screaming "obvious copyright disaster," because it is mostly original code and UI. The biggest avoidable risk is accidentally turning it into a digital copy of the rulebooks. Keep it as a play aid, link to official rules, use original wording, avoid official content, and get written permission from GZG before public or commercial distribution.
