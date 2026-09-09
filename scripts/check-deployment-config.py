"""Holds the deployment files to each other.

Nothing here runs the stack. These are the disagreements that cost nothing to make and are close to
free to miss, because every one of them still starts, still serves, and still passes every test:

  - a variable documented in .env.example that docker-compose.yml never reads, so setting it does
    exactly nothing while the file says it is how you configure the thing;
  - a port that moved in one file and not the other, so the dev server, the compose mapping and the
    origin the API is told to allow stop describing the same address;
  - an nginx location that adds a header of its own, which silently drops every header the server
    block set, because that is how add_header inheritance works;
  - a Development settings overlay that overlays nothing.

Run it directly, or let scripts/verify.ps1 run it. It prints every problem it finds rather than the
first, and exits 1 if there were any.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
problems: list[str] = []


def fail(message: str) -> None:
    problems.append(message)


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


def env_example_names(text: str) -> set[str]:
    """Every variable .env.example names, commented-out examples included.

    A commented example is documentation as much as an uncommented one: it tells a reader that
    setting that name will do something, so it has to be true of it too.
    """
    return set(re.findall(r"^\s*#?\s*([A-Z][A-Z0-9_]*)=", text, re.MULTILINE))


def compose_variable_names(text: str) -> set[str]:
    return set(re.findall(r"\$\{([A-Za-z_][A-Za-z0-9_]*)", text))


def port_of(url: str) -> str | None:
    match = re.search(r":(\d+)", url)
    return match.group(1) if match else None


def check_env_reaches_the_container() -> None:
    env_text = read(".env.example")
    compose_text = read("docker-compose.yml")
    documented = env_example_names(env_text)
    interpolated = compose_variable_names(compose_text)

    for name in sorted(documented - interpolated):
        fail(
            f".env.example documents {name}, but docker-compose.yml never interpolates it. "
            "Compose passes .env to a container only through ${...}, so setting it does nothing."
        )

    for name in sorted(interpolated - documented):
        fail(
            f"docker-compose.yml reads ${{{name}}}, but .env.example does not document it. "
            "An operator has no way to find out the knob exists."
        )


def check_ports_agree() -> None:
    env_text = read(".env.example")
    compose_text = read("docker-compose.yml")
    vite_text = read("src/ForceSignal.Web/vite.config.ts")

    web_origin = re.search(r"^FORCESIGNAL_WEB_ORIGIN=(\S+)", env_text, re.MULTILINE)
    api_base = re.search(r"^VITE_API_BASE_URL=(\S+)", env_text, re.MULTILINE)
    vite_port = re.search(r"port:\s*(\d+)", vite_text)
    published = re.findall(r'^\s*-\s*"(\d+):(\d+)"', compose_text, re.MULTILINE)

    if not (web_origin and api_base and vite_port and len(published) == 2):
        fail("Could not read the ports out of .env.example, docker-compose.yml and vite.config.ts.")
        return

    api_published, web_published = published[0][0], published[1][0]

    if vite_port.group(1) != web_published:
        fail(
            f"vite.config.ts serves the web app on {vite_port.group(1)} but docker-compose.yml "
            f"publishes it on {web_published}. One address, two answers."
        )
    if port_of(web_origin.group(1)) != web_published:
        fail(
            f"FORCESIGNAL_WEB_ORIGIN names port {port_of(web_origin.group(1))}, but the web "
            f"container is published on {web_published}, so CORS would refuse the browser it is for."
        )
    if port_of(api_base.group(1)) != api_published:
        fail(
            f"VITE_API_BASE_URL names port {port_of(api_base.group(1))}, but the API container is "
            f"published on {api_published}, so the built web app would call nothing."
        )


def check_dev_api_url_matches_the_dev_server() -> None:
    """The web app's built-in fallback has to be the address `dotnet run` actually listens on."""
    api_text = read("src/ForceSignal.Web/src/lib/api.ts")
    launch = json.loads(read("src/ForceSignal.Api/Properties/launchSettings.json"))

    fallback = re.search(r"VITE_API_BASE_URL\s*\?\?\s*'([^']+)'", api_text)
    urls = [profile.get("applicationUrl", "") for profile in launch["profiles"].values()]
    if not fallback:
        fail("api.ts no longer has a readable default API base URL.")
        return

    if not any(url.startswith(fallback.group(1)) for url in urls):
        fail(
            f"api.ts falls back to {fallback.group(1)}, which no launchSettings.json profile "
            f"listens on ({', '.join(urls)}). `npm run dev` would reach nothing."
        )


def check_every_nginx_location_carries_the_headers() -> None:
    """nginx drops the parent's add_header lines the moment a location declares one of its own."""
    nginx_text = read("src/ForceSignal.Web/nginx.conf")
    include = "include /etc/nginx/security-headers.inc;"

    for match in re.finditer(r"location\s+([^{]+?)\s*\{", nginx_text):
        start = match.end()
        depth, index = 1, start
        while index < len(nginx_text) and depth:
            depth += (nginx_text[index] == "{") - (nginx_text[index] == "}")
            index += 1
        body = nginx_text[start:index]
        if include not in body:
            fail(
                f"nginx location '{match.group(1).strip()}' does not include security-headers.inc. "
                "A location that adds any header of its own inherits none of the server block's, so "
                "this one answers without the security headers every other location sends."
            )


def check_no_short_flags_through_the_powershell_wrapper() -> None:
    """A single-dash flag handed to Invoke-Native is bound by PowerShell, not passed to the tool.

    Invoke-Native declares [Parameter()] attributes, which makes it an advanced function, which
    gives it PowerShell's common parameters. "-d" is an unambiguous prefix of "-Debug", so
    `Invoke-Native docker compose up -d` bound the -d to the wrapper and started the stack
    attached: compose sat streaming container logs and the step hung until it was killed. Nothing
    caught it for as long as that branch never ran, and nothing about the line looks wrong.

    Double-dash flags are safe - PowerShell does not treat them as parameter names - so this looks
    only for the single-dash short form, which has no business going through the wrapper at all.
    """
    for script in sorted((ROOT / "scripts").glob("*.ps1")):
        for number, line in enumerate(read(f"scripts/{script.name}").splitlines(), start=1):
            if "Invoke-Native" not in line or line.lstrip().startswith("#"):
                continue
            for flag in re.findall(r"(?<![\w-])-([A-Za-z])(?![\w-])", line):
                fail(
                    f"{script.name}:{number} passes -{flag} through Invoke-Native. PowerShell binds "
                    f"a single-dash flag to the wrapper's own parameters - -{flag} may well match a "
                    "common parameter - so the tool never receives it. Call the tool with & instead."
                )


def check_development_overlay_overlays_something() -> None:
    overlay = ROOT / "src/ForceSignal.Api/appsettings.Development.json"
    if not overlay.exists():
        return

    base = json.loads(read("src/ForceSignal.Api/appsettings.json"))
    development = json.loads(read("src/ForceSignal.Api/appsettings.Development.json"))

    def differs(left, right) -> bool:
        if isinstance(right, dict) and isinstance(left, dict):
            return any(key not in left or differs(left[key], value) for key, value in right.items())
        return left != right

    if not differs(base, development):
        fail(
            "appsettings.Development.json sets nothing appsettings.json does not already set. "
            "An overlay that overlays nothing is a file a reader has to check to learn it says "
            "nothing; delete it, or put the Development difference in it."
        )


for check in (
    check_env_reaches_the_container,
    check_ports_agree,
    check_dev_api_url_matches_the_dev_server,
    check_every_nginx_location_carries_the_headers,
    check_no_short_flags_through_the_powershell_wrapper,
    check_development_overlay_overlays_something,
):
    check()

if problems:
    print("Deployment configuration disagrees with itself:\n", file=sys.stderr)
    for problem in problems:
        print(f"  - {problem}", file=sys.stderr)
    sys.exit(1)

print("Deployment configuration checks passed.")
