"""Drive a real browser through one whole turn and fail on any console error.

The unit tests cover the server's rules; the type checker covers the client's shapes. Neither
notices a client that compiles and then throws on load - which is exactly what happened when a
module-scope call to `crypto.randomUUID` blanked the page on every device that was not the host's.
This is the cheapest thing that would have caught it.

It is also the check a refactor of the client is verified against: the split of `main.tsx` into
modules is invisible to the compiler if an import cycle leaves something undefined at module-init
time, and visible immediately here.

Run the API and the web dev server first, then:

    python scripts/web-smoke.py

Exits non-zero if the browser logged an error or a step could not be reached.
"""

import sys

from playwright.sync_api import sync_playwright

WEB = "http://localhost:6297"

# One full turn: set a fleet up, ready, lock, reveal, and advance through movement into firing.
TURN = ["Ready", "Lock Fleet Orders", "Reveal Fleet Orders", "Advance Turn"]

# The three top-level views. The map is the largest single piece of the client and the log is the
# one that renders proportionally to how long the game has run, so both are worth touching.
VIEWS = ["Play Map", "Log", "Ships"]


def main() -> int:
    errors: list[str] = []

    with sync_playwright() as playwright:
        browser = playwright.chromium.launch()
        page = browser.new_page()
        page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
        page.on("pageerror", lambda e: errors.append(f"uncaught: {e}"))

        page.goto(WEB, wait_until="networkidle")
        page.wait_for_selector("text=Create Match", timeout=15_000)
        page.click("button:has-text('Create Match')")

        page.wait_for_selector("button:has-text('Add Ship')", timeout=15_000)
        page.click("button:has-text('Add Ship')")
        page.wait_for_timeout(700)

        for label in TURN:
            page.click(f"button:has-text('{label}')")
            page.wait_for_timeout(700)

        phase = page.inner_text("main >> css=strong >> nth=0")
        print(f"after one turn: {phase}")
        if "FIRING" not in phase.upper():
            errors.append(f"expected the firing phase, got {phase!r}")

        for view in VIEWS:
            page.click(f"button:has-text('{view}')")
            page.wait_for_timeout(700)

        page.click("button:has-text('Advance Turn')")
        page.wait_for_timeout(900)
        phase = page.inner_text("main >> css=strong >> nth=0")
        print(f"after the turn closes: {phase}")
        if "TURN 2" not in phase.upper():
            errors.append(f"expected turn 2, got {phase!r}")

        browser.close()

    if errors:
        print(f"\n{len(errors)} problem(s):")
        for error in errors:
            print(f"  {error}")
        return 1

    print("\nclean: one turn played, every view rendered, nothing logged an error")
    return 0


if __name__ == "__main__":
    sys.exit(main())
