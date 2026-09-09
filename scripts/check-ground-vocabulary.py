"""Holds the web client's copy of the ground-combat vocabularies to the server's.

The engine enums are the authority. `ForceSignal.Contracts` spells them out as `DirtsideWire` and
`StarGruntWire` because that assembly deliberately depends on nothing but the domain, and
`GroundWireVocabularyTests` holds those arrays against the enums so the spelling-out cannot drift.

This is the last link in that chain: the browser cannot import a C# array, so the client keeps its
own copy in `src/lib/groundVocabulary.ts`, and this compares the two. Without it the client's list is
an independent guess - which is what it was, typed out beside the dropdowns that render it, in a
third spelling that agreed with the other two only by luck. A band the API accepts and no screen
offers is invisible until a player goes looking for it.

Run it directly, or let scripts/verify.ps1 run it.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONTRACTS = ROOT / "src/ForceSignal.Contracts/Ground"
CLIENT = ROOT / "src/ForceSignal.Web/src/lib/groundVocabulary.ts"

problems: list[str] = []


def csharp_strings(source: str, field: str) -> list[str] | None:
    """Reads `public static readonly string[] <field> = [...]` out of a contracts file."""
    match = re.search(rf"string\[\]\s+{field}\s*=\s*\[(.*?)\];", source, re.DOTALL)
    return re.findall(r'"([^"]*)"', match.group(1)) if match else None


def csharp_numbers(source: str, field: str) -> list[str] | None:
    match = re.search(rf"int\[\]\s+{field}\s*=\s*\[(.*?)\];", source, re.DOTALL)
    return re.findall(r"-?\d+", match.group(1)) if match else None


def typescript_values(source: str, name: str) -> list[str] | None:
    """Reads `export const <name> = [...] as const;` out of the client module."""
    match = re.search(rf"export const {name}\s*=\s*\[(.*?)\]\s*as const;", source, re.DOTALL)
    if not match:
        return None
    body = match.group(1)
    quoted = re.findall(r"'([^']*)'", body)
    return quoted if quoted else re.findall(r"-?\d+", body)


def compare(label: str, server: list[str] | None, client: list[str] | None) -> None:
    if server is None:
        problems.append(f"{label}: could not find the vocabulary in the contracts assembly.")
        return
    if client is None:
        problems.append(f"{label}: could not find the vocabulary in groundVocabulary.ts.")
        return
    if server != client:
        problems.append(
            f"{label}: the server says {server} and the client says {client}. "
            "One of them is offering or accepting something the other does not."
        )


dirtside = (CONTRACTS / "DirtsideContracts.cs").read_text(encoding="utf-8-sig")
stargrunt = (CONTRACTS / "StarGruntContracts.cs").read_text(encoding="utf-8-sig")
client = CLIENT.read_text(encoding="utf-8-sig")

compare("Bands", csharp_strings(dirtside, "Bands"), typescript_values(client, "bands"))
compare("FireControls", csharp_strings(dirtside, "FireControls"), typescript_values(client, "fireControls"))
compare("QualityDice", csharp_strings(dirtside, "QualityDice"), typescript_values(client, "qualityDice"))
compare("AssaultStages", csharp_strings(dirtside, "AssaultStages"), typescript_values(client, "assaultStages"))
compare("Ladder", csharp_numbers(stargrunt, "Ladder"), typescript_values(client, "qualityLadder"))

# A copy nothing reads is the state this was written to end, so check the client actually uses it.
for module in (ROOT / "src/ForceSignal.Web/src/components/ground").glob("*.tsx"):
    text = module.read_text(encoding="utf-8-sig")
    for name in ("bands", "fireControls", "qualityDice", "qualityLadder"):
        if re.search(rf"^const {name}\s*=\s*\[", text, re.MULTILINE):
            problems.append(
                f"{module.name} declares its own '{name}' instead of importing it from "
                "groundVocabulary.ts, which puts the vocabulary back into two places."
            )

if problems:
    print("The ground-combat vocabularies disagree:\n", file=sys.stderr)
    for problem in problems:
        print(f"  - {problem}", file=sys.stderr)
    sys.exit(1)

print("Ground-combat vocabulary checks passed.")
