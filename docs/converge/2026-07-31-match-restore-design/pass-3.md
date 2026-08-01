# Pass 3 — standard (client flow, execution modality)

Drove the client flow in the browser instead of reading it.

## Gaps found: 1 (two symptoms)

7. The join screen had **no surface for `message` at all** — it renders in `.section-head p`,
   which only exists inside the match workspace. Every restore failure was therefore silent: the
   app correctly stayed on the join screen and told the user nothing. This also silenced the
   pre-existing "No local snapshot backup found on this device." path behind
   *Export Last Device Backup*, which the design's flow depends on.
   A non-snapshot file also surfaced a raw `JSON.parse` SyntaxError rather than an explanation.

## Fixes

- Status line added to the join screen and the seat picker.
- Unparseable files now report `<name> is not readable as JSON. Pick a snapshot exported by
  ForceSignal.` instead of a raw parser error.

Verified live: not-JSON, wrong-shape, and no-backup-stored all now show a specific message and
leave the user on the join screen.

**Verdict:** 1 gap, fixed. clean_streak = 0.
