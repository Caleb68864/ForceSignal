# Pass 2 — standard (execution modality)

Probed the Errors section against the running API rather than reading it.

| Case | Result |
|---|---|
| Malformed JSON | **400 text/plain** — not problem+json. GAP |
| Wrong schema | 400 problem+json, "no participants to restore" |
| Empty ships | 400 problem+json, "no ships to restore" |
| No participants | 400 problem+json, "no participants to restore" |
| Unknown rules profile | 400 problem+json, names the profile |
| Ship without its fleet | 400 problem+json, names the ship |

## Gaps found: 1

6. Malformed JSON answered with a plain-text 400 because binding `JsonElement` fails in model
   binding, before the handler's `JsonException` catch. The existing "garbage payload" test used
   *valid* JSON, so it never exercised this. The endpoint now reads and parses the body itself,
   and the test became a Theory covering not-JSON, empty body, wrong schema and empty ships.

**Verdict:** 1 gap, fixed. clean_streak = 0. API tests 10 -> 13.
