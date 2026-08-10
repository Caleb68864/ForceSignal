# Godot Phase 0: Does ForceSignal Compile and Run on .NET 8?

_Spike run 2026-08-10 on branch `spike/net8-godot-retarget`. The answer is yes, for the cost of
changing three lines. `master` stays on .NET 10._

## Why this was worth an afternoon

Godot 4's .NET build targets **net8.0**, while this repo targets **net10.0**. Every architectural
decision taken while building the StarGrunt slice leaned on the idea that the rules could travel to
a Godot client one day: the game is an immutable value rather than a mutable service, the modules
were kept free of dependencies, and the client renders rather than decides.

Those choices are defensible on their own merits. But "so it can go to Godot" was being cited as a
reason without anyone having checked that the code compiles against net8 at all. This spike checks
it, so the premise is either proved or dropped rather than assumed.

## Result

**Every project except the web host builds and its tests pass on net8.0.** 989 tests, run on the
net8 runtime rather than merely compiled against net8 reference assemblies.

| Project | net8.0 | Tests on the net8 runtime |
| --- | --- | --- |
| `Modules.GroundCombat` | clean, no change | 112 |
| `Modules.StarGrunt` | clean, no change | 174 |
| `Modules.Dirtside` | clean, no change | 219 |
| `Modules.FullThrust` | clean, no change | 159 |
| `Domain` | clean, no change | 83 |
| `Contracts` | clean, no change | - |
| `Application` | one line | 231 |
| `Infrastructure` | one line | 11 |
| `Api` | not attempted - a Godot client would not take the web host | - |

## The only thing that broke

`System.Threading.Lock`, which is .NET 9 and later, in three places:

- `InMemoryMatchService.cs`
- `StarGruntGameService.cs`
- `SqliteMatchStore.cs`

Each is `private readonly Lock _gate = new();` and each becomes
`private readonly object _gate = new();`. Every `lock (_gate)` statement around them is unchanged,
because the syntax is identical. That is the whole of the port.

Nothing else needed touching. No API was missing, no language feature was unavailable, no package
refused to resolve - including `Microsoft.Data.Sqlite` 10.0.10, which restores and runs against
net8 without complaint. Collection expressions, `ArgumentException.ThrowIfNullOrWhiteSpace`,
`char.IsAsciiLetterOrDigit` and the rest are all net8-era or older.

## What this means for a Godot client

**StarGrunt is the cleanest case and needs two projects.** `Modules.GroundCombat` has no project
references at all, and `Modules.StarGrunt` references only it. Neither needed a single change. A
Godot project can reference those two assemblies and host `StarGruntGame` in process - roster,
statuses, session, rules, serialization and the legality projection - with no ASP.NET, no
`Application`, and no HTTP.

**Full Thrust needs more but still works.** `Domain`, `Modules.FullThrust`, `Contracts` and
`Application` - because the match state lives in `InMemoryMatchService` rather than in a value the
way StarGrunt's does. One of the three `Lock` swaps is in that file.

**Persistence travels too.** `Infrastructure` and its SQLite store work on net8, so a Godot build
could keep the same save format rather than inventing one.

## What this spike did not check

- **The Api project.** Not attempted, and not interesting: a Godot client replaces the web host
  rather than hosting it.
- **Godot itself.** Nothing here was loaded into Godot. What is proved is that the libraries build
  and behave on the framework Godot targets, which is the part that could have been a wall.
- **Whether net8 is still the right target.** Godot's .NET version moves. Check what the Godot
  release in hand actually targets before relying on this.

## Recommendation

Do not retarget `master`. There is no benefit today, and .NET 10 is where the rest of the work is.

What matters is that the port is a three-line change plus a `TargetFramework` edit, which is small
enough that it does not need planning for in advance. Keep doing what the StarGrunt slice already
does - rules in dependency-free modules, game state as a value, the client rendering rather than
deciding - and the retarget stays this cheap whenever it is wanted.

The one thing worth carrying forward: **`System.Threading.Lock` is the only .NET 9+ API in the
codebase.** If more creep in, this stops being a three-line change. Worth a glance whenever
something new reaches for a framework type.
