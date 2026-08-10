# StarGrunt II Playable Slice — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Two squads, one shooting at the other, activations alternating properly, playable end to end on one screen.

**Architecture:** The game is an immutable `StarGruntGame` record in `Modules.StarGrunt/Game/`, implementing the existing `IStarGruntBoard` and wrapping the existing `GroundCombatSession`. Every command is a pure function returning a new game or a refusal reason. `Application/Ground/StarGruntGameService` adds only a lock, an id lookup and persistence. Everything is behind `Features:StarGrunt`, which when off means the routes are absent.

**Tech Stack:** .NET 10 (no retarget), xUnit, React 19 + Vite + TypeScript, vitest.

**Design:** `docs/plans/2026-08-10-stargrunt-play-design.md`

---

## Ground rules for the whole plan

- **TDD throughout.** Test first, watch it fail, minimal implementation, watch it pass, commit.
- **Nothing in `Modules.*` gains a dependency.** Not on `Application`, not on `Contracts`, not on ASP.NET. That is what lets a Godot client host the game later. If a task seems to need one, stop and ask.
- **No rules numbers in the repo.** Every die, rating and threshold is a user input. If you find yourself typing a table out of a rulebook, stop — see the content policy in `docs/ground-combat-plan.md`.
- **Refusals, not exceptions,** in the game layer. Follow `SequenceCheck` (`Modules.GroundCombat/Sequence/SequenceCheck.cs`): a `Can*` returning a reason, and a verb that applies.
- Run `pwsh scripts/verify.ps1` before each commit if you want the full sweep; the per-task commands below are faster.

---

## Task 1: The unit model

**Files:**
- Create: `src/ForceSignal.Modules.StarGrunt/Game/UnitDefinition.cs`
- Create: `src/ForceSignal.Modules.StarGrunt/Game/UnitStatus.cs`
- Test: `tests/ForceSignal.Modules.StarGrunt.Tests/UnitModelTests.cs`

**Step 1: Write the failing test**

```csharp
using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Morale;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

public sealed class UnitModelTests
{
    [Fact]
    public void Status_ProjectsIntoTheStateTheActivationRulesRead()
    {
        var status = new UnitStatus
        {
            SuppressionMarkers = 2,
            Confidence = ConfidenceLevel.Shaken,
            IsInCover = true,
            IsDisorganised = true,
        };

        var state = status.ToUnitState(CommandLevel.Squad);

        Assert.Equal(2, state.SuppressionMarkers);
        Assert.Equal(ConfidenceLevel.Shaken, state.Confidence);
        Assert.True(state.IsInCover);
        Assert.True(state.IsDisorganised);
        Assert.Equal(CommandLevel.Squad, state.Level);
    }

    [Fact]
    public void Casualties_ComeOffTheFiguresThatWereThere()
    {
        var unit = new UnitDefinition
        {
            Id = new UnitId("alpha"),
            Name = "Alpha Squad",
            Side = new SideId("blue"),
            Figures = [.. Enumerable.Repeat(new FigureProfile(QualityDie.D6), 8)],
        };

        var status = UnitStatus.ForFullStrength(unit);

        Assert.Equal(8, status.FiguresAlive);
        Assert.Equal(0, status.FiguresWounded);
    }
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/ForceSignal.Modules.StarGrunt.Tests/ForceSignal.Modules.StarGrunt.Tests.csproj --filter "FullyQualifiedName~UnitModel"
```
Expected: FAIL, `UnitDefinition` does not exist.

**Step 3: Write the implementation**

`UnitDefinition.cs` — what a unit *is*. All dice are user inputs:

```csharp
public readonly record struct FigureProfile(QualityDie ArmourDie);

public sealed record WeaponProfile
{
    public required string Name { get; init; }
    public required QualityDie ImpactDie { get; init; }
    public bool IsSupport { get; init; }
    public bool IsCloseRange { get; init; }
}

public sealed record UnitDefinition
{
    public required UnitId Id { get; init; }
    public required string Name { get; init; }
    public required SideId Side { get; init; }
    public CommandLevel Level { get; init; } = CommandLevel.Squad;
    public QualityDie QualityDie { get; init; } = QualityDie.D8;
    public QualityDie LeadershipDie { get; init; } = QualityDie.D8;
    public ImmutableArray<FigureProfile> Figures { get; init; } = [];
    public ImmutableArray<WeaponProfile> Weapons { get; init; } = [];
}
```

`UnitStatus.cs` — what has *happened* to it. Note `ToUnitState` projects rather than duplicates; do not restate the fields of `StarGruntUnitState` here beyond what the game itself needs to track.

```csharp
public sealed record UnitStatus
{
    public int FiguresAlive { get; init; }
    public int FiguresWounded { get; init; }
    public int SuppressionMarkers { get; init; }
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Confident;
    public bool IsDisorganised { get; init; }
    public bool IsInCover { get; init; }
    public bool NextMoveLeavesCover { get; init; }
    public bool ReactionTestCleared { get; init; }
    public int TransfersMadeThisTurn { get; init; }

    public static UnitStatus ForFullStrength(UnitDefinition unit) =>
        new() { FiguresAlive = unit.Figures.Length };

    public StarGruntUnitState ToUnitState(CommandLevel level) => new()
    {
        Level = level,
        SuppressionMarkers = SuppressionMarkers,
        Confidence = Confidence,
        IsDisorganised = IsDisorganised,
        IsInCover = IsInCover,
        NextMoveLeavesCover = NextMoveLeavesCover,
        ReactionTestCleared = ReactionTestCleared,
        TransfersMadeThisTurn = TransfersMadeThisTurn,
    };
}
```

**Step 4: Run test to verify it passes**

Same command. Expected: PASS, 2 tests.

**Step 5: Commit**

```bash
git add src/ForceSignal.Modules.StarGrunt/Game tests/ForceSignal.Modules.StarGrunt.Tests/UnitModelTests.cs
git commit -m "feat: what a StarGrunt unit is, and what has happened to it"
```

---

## Task 2: The game as a value, and as a board

**Files:**
- Create: `src/ForceSignal.Modules.StarGrunt/Game/StarGruntGame.cs`
- Test: `tests/ForceSignal.Modules.StarGrunt.Tests/GameBoardTests.cs`

`StarGruntGame` holds the roster, the statuses and the session, and implements `IStarGruntBoard` so the activation policy can read it. This is the piece the sequence layer was built expecting and never had.

**Step 1: Write the failing test**

```csharp
[Fact]
public void Game_AnswersTheBoardQuestionsTheActivationPolicyAsks()
{
    var game = StarGruntGame.Create("Hill 43")
        .WithUnit(Fixtures.Squad("alpha", "blue"))
        .WithUnit(Fixtures.Squad("bravo", "red"));

    IStarGruntBoard board = game;

    Assert.Equal(ConfidenceLevel.Confident, board.State(new UnitId("alpha")).Confidence);
    Assert.Contains(CommandLevel.Squad, board.CommandLevelsOnTable);
    Assert.Null(board.DashOpening(new UnitId("alpha")));
}

[Fact]
public void Game_IsAValue_SoTwoIdenticalGamesAreEqual()
{
    var left = StarGruntGame.Create("Hill 43").WithUnit(Fixtures.Squad("alpha", "blue"));
    var right = StarGruntGame.Create("Hill 43").WithUnit(Fixtures.Squad("alpha", "blue"));

    Assert.Equal(left, right);
}
```

Add a `Fixtures.Squad(id, side)` helper to `tests/ForceSignal.Modules.StarGrunt.Tests/SequenceFixtures.cs` (it already exists — extend it rather than making a second fixtures file).

**Step 2–4:** Run, implement, run.

Implementation notes:
- `Units` is an `ImmutableDictionary<UnitId, UnitDefinition>`, `Statuses` an `ImmutableDictionary<UnitId, UnitStatus>`, `Session` a `GroundCombatSession`.
- **Equality:** immutable dictionaries do *not* compare structurally by default. `Modules.GroundCombat/Sequence/StructuralEquality.cs` already exists for exactly this problem in the session — read it and follow the same approach. The second test above is what catches getting this wrong.
- `DashOpening` returns null for now. Reaction fire on a dash is a later task; returning null means "unobserved", which is a legal answer.

**Step 5: Commit** — `feat: the caller IStarGruntBoard was always waiting for`

---

## Task 3: Starting a turn and alternating activations

**Files:**
- Create: `src/ForceSignal.Modules.StarGrunt/Game/StarGruntGame.Turn.cs` (partial)
- Test: `tests/ForceSignal.Modules.StarGrunt.Tests/GameTurnTests.cs`

Wrap the existing `GroundCombatSequence` transitions. Do **not** reimplement any of them — every one already has a `Can*`/verb pair (`GroundCombatSequence.cs:45-598`).

**Step 1: Write the failing test**

```csharp
[Fact]
public void ATurn_OpensWithTheSmallerSideChoosingWhoGoesFirst()
{
    var game = Fixtures.TwoSquadGame();

    var begun = game.BeginTurn().Value;

    Assert.Equal(TurnPhase.ChoosingFirstActivator, begun.Session.Phase);
    Assert.Equal(1, begun.Session.TurnNumber);
}

[Fact]
public void ActivatingOutOfTurn_IsRefusedWithAReasonRatherThanThrowing()
{
    var game = Fixtures.TwoSquadGame().BeginTurn().Value
        .ChooseFirstActivator(new SideId("blue"), takesIt: true).Value;

    var refused = game.BeginActivation(new SideId("red"), new UnitId("bravo"));

    Assert.False(refused.IsAllowed);
    Assert.NotNull(refused.Reason);
}
```

**Step 3 implementation notes:**

Define the outcome type once, in `Game/GameOutcome.cs`:

```csharp
public readonly record struct GameOutcome<T>(bool IsAllowed, string? Reason, T? Value)
{
    public static GameOutcome<T> Allowed(T value) => new(true, null, value);
    public static GameOutcome<T> Refused(string reason) => new(false, reason, default);
}
```

Each command is: ask the sequence's `Can*`, return `Refused(check.Reason!)` if it says no, otherwise apply the verb and return a new game with the new session. Commands to cover in this task: `BeginTurn`, `ChooseFirstActivator`, `BeginActivation`, `Pass`, `EndFrame`, `EndTurn`.

**Step 5: Commit** — `feat: a StarGrunt turn, alternating as the rules alternate`

---

## Task 4: Firing

**Files:**
- Create: `src/ForceSignal.Modules.StarGrunt/Game/StarGruntGame.Fire.cs` (partial)
- Test: `tests/ForceSignal.Modules.StarGrunt.Tests/GameFireTests.cs`

Wrap `FireCombat.Resolve` (`Combat/FireCombat.cs:105`) and apply what it returns.

**Step 1: Write the failing test** — use a scripted die source so the outcome is fixed. `QualityDiceRoller` takes an injectable source; `tests/ForceSignal.Modules.StarGrunt.Tests` already has fixtures that do this — copy that approach rather than inventing one.

```csharp
[Fact]
public void Fire_TakesCasualtiesOffTheTargetAndSuppressesIt()
{
    var game = Fixtures.FiringGame(scriptedDice: [...]);

    var after = game.Fire(new FireCommand
    {
        Firer = new UnitId("alpha"),
        Target = new UnitId("bravo"),
        FirepowerDie = QualityDie.D8,
        SupportDice = [],
        WeaponName = "Rifles",
        DistanceInches = 10,
        TargetPosture = TargetPosture.InTheOpen,
    }).Value;

    Assert.True(after.Status(new UnitId("bravo")).FiguresAlive < 8);
    Assert.True(after.Status(new UnitId("bravo")).SuppressionMarkers > 0);
}
```

**Implementation notes — the subtle parts:**

- **`FirepowerDie` is an input on the command,** not derived from `FiguresAlive`. Deriving it means shipping the figures-to-firepower table. See the design doc and the content policy.
- The weapon's `ImpactDie` and the target's `ArmourDie` come off the definitions; everything else comes off the command.
- Two `Wound` results on one figure in a single resolution is a death — that is in `HitEffect`'s own docs. Apply kills and wounds accordingly.
- `FireOutcome.Suppresses` is true on at least a minor success; add a suppression marker, capped at 3 (`Morale/Suppression.cs` owns that cap — call it, do not re-derive it).
- Record the whole `FireOutcome` in the game's log. It is kept whole *so a table can audit it*, which is the point.

**Step 5: Commit** — `feat: fire resolved against the roster, casualties and all`

---

## Task 5: The legality projection

**Files:**
- Create: `src/ForceSignal.Modules.StarGrunt/Game/UnitLegality.cs`
- Test: `tests/ForceSignal.Modules.StarGrunt.Tests/GameLegalityTests.cs`

This is the task that stops the client growing its own copy of the rules. Read the design doc's section on it before starting.

For each unit, from the *same* `Can*` calls the commands use: can it activate, can it fire, and the reason when not.

**Step 1: Write the failing test**

```csharp
[Fact]
public void Legality_GivesTheSameAnswerTheCommandWouldGive()
{
    var game = Fixtures.TwoSquadGame().BeginTurn().Value
        .ChooseFirstActivator(new SideId("blue"), takesIt: true).Value;

    var legality = game.LegalityFor(new UnitId("bravo"));
    var refused = game.BeginActivation(new SideId("red"), new UnitId("bravo"));

    Assert.False(legality.CanActivate);
    Assert.Equal(refused.Reason, legality.ActivationBlocker);
}
```

That assertion — the projection and the command returning the *same string* — is the whole point, and is the same shape as `FiringSolution_ReportsTheSameWordsFireWeaponWouldRefuseWith` in `tests/ForceSignal.Application.Tests/InMemoryMatchServiceFiringSolutionTests.cs`. Read that test first.

**Step 5: Commit** — `feat: the game says what each unit may do, and why not`

---

## Task 6: The application shell

**Files:**
- Create: `src/ForceSignal.Application/Ground/IStarGruntGameService.cs`
- Create: `src/ForceSignal.Application/Ground/StarGruntGameService.cs`
- Test: `tests/ForceSignal.Application.Tests/StarGruntGameServiceTests.cs`

Thin. A `Lock`, a `Dictionary<Guid, StarGruntGame>`, and mapping to DTOs. Every method: take the lock, apply a command, store the new game, return a snapshot. A refusal becomes an `InvalidOperationException` here — that is the boundary where refusals turn into errors, and `Program.cs`'s existing handler already maps it to a 400.

**The test that matters:** play a whole turn end to end — create, add two forces, begin turn, choose first activator, activate, fire, end frame, alternate, pass, end turn.

**Step 5: Commit** — `feat: a StarGrunt game the server can hold`

---

## Task 7: Persistence

**Files:**
- Modify: `src/ForceSignal.Application/Ground/StarGruntGameService.cs`
- Modify: `src/ForceSignal.Api/Program.cs` (register a second store)
- Test: `tests/ForceSignal.Application.Tests/StarGruntPersistenceTests.cs`

`IMatchStore` takes this unchanged — its contract is deliberately a key and an opaque string.

**The one trap:** it is keyed by `Guid` and `LoadAll()` returns everything it holds. Give StarGrunt its **own store instance with its own table**, or a restart will hand the Full Thrust service a ground game it cannot parse. `SqliteMatchStore`'s table name must therefore be a constructor parameter — check whether it already is, and make it one if not.

**Test:** save a mid-turn game, rebuild the service from the store, assert the restored game *equals* the original. Record equality is the assertion — that is what the immutable design bought.

**Step 5: Commit** — `feat: a StarGrunt game survives a restart`

---

## Task 8: Contracts and endpoints

**Files:**
- Create: `src/ForceSignal.Contracts/Ground/StarGruntContracts.cs`
- Modify: `src/ForceSignal.Api/Endpoints/GroundCombatEndpoints.cs`
- Test: `tests/ForceSignal.Api.Tests/StarGruntEndpointTests.cs`

Replace the status stub with real routes: create game, add unit, snapshot, begin turn, choose first activator, begin activation, fire, end frame, pass, end turn.

**Write the flag-off test first:**

```csharp
[Fact]
public async Task WithTheFlagOff_TheStarGruntRoutesAreAbsent()
{
    using var factory = CreateFactory(starGrunt: false);
    using var client = factory.CreateClient();

    using var response = await client.PostAsJsonAsync("/api/stargrunt/games", new { name = "Hill 43" });

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

Absent rather than disabled is the promise the flag makes, and it is the one thing here worth a test before anything else exists.

**Step 5: Commit** — `feat: StarGrunt routes, present only when asked for`

---

## Task 9: Web types and API functions

**Files:**
- Modify: `src/ForceSignal.Web/src/types.ts`
- Create: `src/ForceSignal.Web/src/lib/stargruntApi.ts`

Types mirroring the DTOs. **Nothing rules-shaped.** If you are about to write a function that decides whether something is legal, stop — that answer is in the snapshot's legality projection. That mistake cost two rounds of work on the Full Thrust side; see `src/ForceSignal.Web/src/lib/rules.ts` for what is left after removing it.

**Step 5: Commit** — `feat: the wire shapes for a StarGrunt game`

---

## Task 10: The play view

**Files:**
- Create: `src/ForceSignal.Web/src/components/ground/StarGruntView.tsx`
- Modify: `src/ForceSignal.Web/src/main.tsx` (route to it when the feature flag is on)

Roster down one side with each unit's figures, suppression, confidence; the activation order and whose go it is; a fire panel that collects the declared range, posture and dice.

Every button's disabled state and every "why not" comes from `legality`. No local rules.

Hide the whole view unless `/api/features` reports `starGrunt: true` — `main.tsx` already reads that endpoint.

**Step 5: Commit** — `feat: a screen you can play StarGrunt on`

---

## Task 11: Force import and export

**Files:**
- Create: `src/ForceSignal.Web/src/lib/forceIo.ts`
- Test: `src/ForceSignal.Web/src/lib/forceIo.test.ts`

Versioned JSON, following `fleetIo.ts`. **Put a schema version in the file from the first commit** — that is the whole reason this is in the slice rather than a library.

Round-trip test: export a force, import it, assert equality.

**Step 5: Commit** — `feat: a StarGrunt force survives the game it was built for`

---

## Task 12: Play it

Not a code task. Turn the flag on, start the API and the web app, and play a turn of two squads against each other in a browser — the way the firing console was checked in `5dbb75b`. Fix what that finds.

Then update `docs/ground-combat-plan.md`: items 5 and 6 of "what comes next" are partly done, and the layering table needs `Game/` adding.

**Commit** — `docs: what playing StarGrunt for the first time turned up`

---

## What is deliberately not here

Close assault, fog of war and dummy counters, positions and a map, two-device play, a saved force library, `GroundCombat/Design` and points, Dirtside. Each is real work; none is needed to find out whether this loop is any good.
