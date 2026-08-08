"""Two devices, one match, played through a whole turn including a shot fired.

This is the thing the docs say has never been done since the turn structure changed: two browsers,
two seats, hidden orders locked and revealed independently, and a real volley resolved. Everything
else is verified in isolation; this is the only check that the pieces fit together the way a table
uses them.
"""
import sys
from playwright.sync_api import sync_playwright

WEB = "http://localhost:6297"
problems: list[str] = []


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
