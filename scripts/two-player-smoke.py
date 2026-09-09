"""Two devices, one match, played through a whole turn.

This is the thing the docs say has never been done since the turn structure changed: two browsers,
two seats, hidden orders locked and revealed independently on separate devices, and the phase
turning over live on both. Everything else is verified in isolation; this is the only check that the
pieces fit together the way a table uses them.

Run it against a dev pair - the API on 8080 and the web dev server on 6297 - with playwright
installed. It exits non-zero on the first thing that did not happen.
"""
import json
import sys
import tempfile
from pathlib import Path

from playwright.sync_api import sync_playwright

WEB = "http://localhost:6297"
problems: list[str] = []

# ForceSignal ships no rules numbers - not in the client, not on the server - so a match arrives
# with an empty profile and readiness refuses to start until somebody fills one in. That is the
# point of the design and it means this script has to bring its own. These numbers are invented for
# the smoke test and correspond to no published game; all the profile needs to be playable is a
# name, a die, one beam result, a range band and a damage track.
SMOKE_PROFILE = {
    "name": "Smoke Test Layer (invented)",
    "dieFaces": 6,
    "beamDamage": [
        {"dieFace": 6, "screenLevel": 0, "damage": 2},
        {"dieFace": 5, "screenLevel": 0, "damage": 1},
    ],
    "beamRangeBandWidth": 12,
    "maxScreenLevel": 0,
    "thresholdRows": "FixedRows",
    "thresholdRowCount": 4,
}


def set_rules_profile(page, path):
    """Loads the profile through the editor's own Import JSON control, then plays against it."""
    page.set_input_files("div[aria-label='Rules profile'] input[type=file]", str(path))
    page.wait_for_timeout(400)
    page.click("div[aria-label='Rules profile'] >> button:has-text('Save & Play Against This')")
    page.wait_for_timeout(700)


def watch(page, who):
    page.on("console", lambda m: problems.append(f"{who}: {m.text}") if m.type == "error" else None)
    page.on("pageerror", lambda e: problems.append(f"{who} uncaught: {e}"))


def add_ship(page, name, x, y, course):
    page.get_by_role("textbox", name="Ship name").fill(name)
    page.get_by_role("spinbutton", name="Position X").fill(str(x))
    page.get_by_role("spinbutton", name="Position Y").fill(str(y))
    page.get_by_role("spinbutton", name="Course", exact=True).fill(str(course))
    page.click("button:has-text('Add Ship')")
    page.wait_for_timeout(700)


profile_file = Path(tempfile.gettempdir()) / "forcesignal-smoke-profile.json"
profile_file.write_text(json.dumps(SMOKE_PROFILE), encoding="utf-8")

with sync_playwright() as pw:
    browser = pw.chromium.launch()
    # Separate contexts: separate localStorage, which is where the order keys live.
    blue_ctx, red_ctx = browser.new_context(), browser.new_context()
    blue, red = blue_ctx.new_page(), red_ctx.new_page()
    watch(blue, "blue")
    watch(red, "red")

    blue.goto(WEB, wait_until="networkidle")
    blue.get_by_role("textbox", name="Display name").fill("Blue")
    blue.click("button:has-text('Create Match')")
    blue.wait_for_selector("button:has-text('Add Ship')", timeout=15000)
    code = blue.inner_text("aside >> css=h2 >> nth=0").strip()
    print(f"room: {code}")

    # Settled by the owner, during fleet setup, before anybody declares ready.
    set_rules_profile(blue, profile_file)
    print("rules profile set")

    red.goto(WEB, wait_until="networkidle")
    red.get_by_role("textbox", name="Display name").fill("Red")
    red.get_by_role("textbox", name="Room code").fill(code)
    red.click("button:has-text('Join Match')")
    red.wait_for_selector("button:has-text('Add Ship')", timeout=15000)
    print("red joined")

    add_ship(blue, "Valiant", 20, 30, 12)
    add_ship(red, "Crimson", 20, 22, 6)

    blue.click("button:has-text('Ready')")
    blue.wait_for_timeout(600)
    red.click("button:has-text('Ready')")
    red.wait_for_timeout(900)
    phase = blue.inner_text("main >> css=strong >> nth=0")
    print(f"both ready: {phase}")
    if "ORDER ENTRY" not in phase.upper():
        problems.append(f"expected order entry, got {phase!r}")

    # An early tap, taken back. Locking a fleet declares the plotting closed in the same breath, so
    # a tap that came before the orders did used to hand the turn over with ships holding course and
    # no way back but to play it out. Blue does exactly that, then reopens - which is only offered
    # while the other admiral is still plotting and the declaration therefore means nothing.
    blue.click("button:has-text('Lock Fleet Orders')")
    blue.wait_for_timeout(800)
    resume = blue.locator("button:has-text('Resume Plotting')")
    if resume.count() == 0:
        problems.append("blue declared plotting done and was offered no way to take it back")
    else:
        resume.click()
        blue.wait_for_timeout(800)
        if blue.locator("button:has-text('Resume Plotting')").count() != 0:
            problems.append("blue took the declaration back and the screen still says it stands")
        print("blue reopened plotting")

    # Each side locks and reveals its own orders, from its own device and its own keys.
    for page, who in ((blue, "blue"), (red, "red")):
        page.click("button:has-text('Lock Fleet Orders')")
        page.wait_for_timeout(800)
    for page, who in ((blue, "blue"), (red, "red")):
        page.click("button:has-text('Reveal Fleet Orders')")
        page.wait_for_timeout(800)

    blue.click("button:has-text('Advance Turn')")
    blue.wait_for_timeout(1200)
    phase = blue.inner_text("main >> css=strong >> nth=0")
    print(f"after advance: {phase}")
    if "FIRING" not in phase.upper():
        problems.append(f"expected firing, got {phase!r}")

    # The opponent's device must have seen the phase change without a reload.
    red_phase = red.inner_text("main >> css=strong >> nth=0")
    print(f"red sees: {red_phase}")
    if "FIRING" not in red_phase.upper():
        problems.append(f"red did not see the firing phase: {red_phase!r}")

    blue.click("button:has-text('Advance Turn')")
    blue.wait_for_timeout(1200)
    print(f"turn closed: {blue.inner_text('main >> css=strong >> nth=0')}")
    red.wait_for_timeout(600)
    print(f"red sees:    {red.inner_text('main >> css=strong >> nth=0')}")

    browser.close()

print(f"\n--- problems: {len(problems)}")
for p in problems:
    print(f"  {p}")
sys.exit(1 if problems else 0)
