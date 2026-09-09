using System.Collections.Immutable;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Assault;
using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Morale;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Application.Ground;

/// <summary>Application boundary for a StarGrunt game.</summary>
public interface IStarGruntGameService : IGroundGameService
{
    /// <summary>Starts a game.</summary>
    StarGruntGameCreatedResponse CreateGame(CreateStarGruntGameRequest request);

    /// <summary>Puts a unit on the table.</summary>
    StarGruntSnapshotDto AddUnit(Guid gameId, AddStarGruntUnitRequest request);

    /// <summary>Reads a game without changing it.</summary>
    StarGruntSnapshotDto GetSnapshot(Guid gameId);

    /// <summary>Opens the next turn.</summary>
    StarGruntSnapshotDto BeginTurn(Guid gameId);

    /// <summary>Settles who takes the first activation.</summary>
    StarGruntSnapshotDto ChooseFirstActivator(Guid gameId, ChooseFirstActivatorRequest request);

    /// <summary>Opens an activation.</summary>
    StarGruntSnapshotDto BeginActivation(Guid gameId, BeginStarGruntActivationRequest request);

    /// <summary>Spends an action on something other than shooting.</summary>
    StarGruntSnapshotDto TakeStep(Guid gameId, StarGruntStepRequest request);

    /// <summary>Fires one weapon at another unit.</summary>
    StarGruntSnapshotDto Fire(Guid gameId, StarGruntFireRequest request);

    /// <summary>Spends an action trying to shake off one suppression marker.</summary>
    StarGruntSnapshotDto RemoveSuppression(Guid gameId, StarGruntUnitActionRequest request);

    /// <summary>Puts a unit's nerve to the test.</summary>
    StarGruntSnapshotDto TakeConfidenceTest(Guid gameId, StarGruntConfidenceTestRequest request);

    /// <summary>Spends a command element's action steadying a subordinate.</summary>
    StarGruntSnapshotDto Rally(Guid gameId, StarGruntRallyRequest request);

    /// <summary>Spends an action putting a scattered unit back in order.</summary>
    StarGruntSnapshotDto Reorganise(Guid gameId, StarGruntUnitActionRequest request);

    /// <summary>Declares whether a unit has scattered out of integrity.</summary>
    StarGruntSnapshotDto SetDisorganised(Guid gameId, StarGruntDisorganisedRequest request);

    /// <summary>Rolls to see whether troops have the nerve for a risky order.</summary>
    StarGruntSnapshotDto TakeReactionTest(Guid gameId, StarGruntReactionTestRequest request);

    /// <summary>Declares that a unit's next move leaves cover.</summary>
    StarGruntSnapshotDto SetLeavesCover(Guid gameId, StarGruntLeavesCoverRequest request);

    /// <summary>Declares a close assault and rolls the attacker's nerve to make it.</summary>
    StarGruntSnapshotDto DeclareCharge(Guid gameId, StarGruntChargeRequest request);

    /// <summary>Rolls the defender's nerve to stand and receive a charge.</summary>
    StarGruntSnapshotDto DefenderStands(Guid gameId, StarGruntStandRequest request);

    /// <summary>Fights one round of melee.</summary>
    StarGruntSnapshotDto FightMelee(Guid gameId, StarGruntMeleeRequest request);

    /// <summary>Rolls what became of the figures a unit had downed.</summary>
    StarGruntSnapshotDto SettleTheDowned(Guid gameId, StarGruntSettleDownedRequest request);

    /// <summary>Closes the open activation.</summary>
    StarGruntSnapshotDto EndActivation(Guid gameId);

    /// <summary>Declines to activate anything.</summary>
    StarGruntSnapshotDto Pass(Guid gameId, StarGruntPassRequest request);

    /// <summary>Ends the turn.</summary>
    StarGruntSnapshotDto EndTurn(Guid gameId);
}

/// <summary>
/// Holds StarGrunt games and takes commands against them.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately thin. Every rule lives in <see cref="StarGruntGame"/>, which is a value in a module
/// with no dependencies; this adds a lock, an id, a version counter and the mapping to the wire.
/// That split is what would let a client that is not a browser host the game directly, without any
/// of this.
/// </para>
/// <para>
/// This is also the boundary where a refusal becomes an error. Inside the game a refusal is an
/// answer, because a player asking and a player doing are different acts. By the time a command has
/// been sent, the asking is over.
/// </para>
/// <para>
/// And it is the boundary that knows who may touch a game. The game is a hot-seat screen - one
/// device, both sides - so there is one token per game rather than one per side, minted when the
/// game is created and required on everything after. The rules never see it.
/// </para>
/// </remarks>
public sealed class StarGruntGameService : IStarGruntGameService
{
    private readonly IQualityDiceRoller _dice;
    private readonly IFigureAllocator _allocator;
    private readonly IMatchStore _store;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Held> _games = [];
    private readonly List<SkippedSave> _skippedSaves = [];

    /// <summary>
    /// Builds the service and brings back whatever the store was holding, so a restart resumes the
    /// games rather than ending them.
    /// </summary>
    /// <param name="rollDie">Die source, injectable so tests can script a firefight.</param>
    /// <param name="store">
    /// Where games are written so they survive a restart. Defaults to keeping nothing, which is what
    /// a test and a throwaway session want.
    /// </param>
    /// <param name="allocator">
    /// Who catches the hits, injectable so a test can script allocation as well as the dice. Defaults
    /// to spreading them evenly across the figures still standing.
    /// </param>
    public StarGruntGameService(
        IQualityDiceRoller? rollDie = null,
        IMatchStore? store = null,
        IFigureAllocator? allocator = null)
    {
        _dice = rollDie ?? new QualityDiceRoller();
        _allocator = allocator ?? new FigureAllocator();
        _store = store ?? NoMatchStore.Instance;
        RestoreAll();
    }

    /// <inheritdoc />
    public IReadOnlyList<SkippedSave> SkippedSaves => _skippedSaves;

    /// <summary>
    /// Brings back whatever the store was holding.
    /// </summary>
    /// <remarks>
    /// A save that cannot be read is skipped rather than allowed to stop the others loading, and
    /// the catch is as wide as that promise: a row that parses and then falls over being rebuilt
    /// used to escape here and take every StarGrunt route down with it. The table is this engine's
    /// own, so every skip is a real loss, and each is kept with its reason for the host to log.
    /// </remarks>
    private void RestoreAll()
    {
        foreach (var saved in _store.LoadAll())
        {
            try
            {
                var row = GroundGameRecord.Unwrap(saved.State);
                _games[saved.MatchId] = new Held(StarGruntGameSerialization.Restore(row.Game), 1, row.Token, row.LastActivity);
            }
#pragma warning disable CA1031 // Every failure is the same failure here: this row does not load.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _skippedSaves.Add(new SkippedSave(saved.MatchId, ex.Message));
            }
        }
    }

    /// <inheritdoc />
    public StarGruntGameCreatedResponse CreateGame(CreateStarGruntGameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = string.IsNullOrWhiteSpace(request.Name) ? "StarGrunt" : GroundGameGuards.Truncate(request.Name);

        lock (_gate)
        {
            EvictIdleGames();
            var id = Guid.NewGuid();
            var held = new Held(StarGruntGame.Create(name), 1, GroundGameGuards.NewToken(), DateTimeOffset.UtcNow);
            _games[id] = held;
            Persist(id, held);
            return new StarGruntGameCreatedResponse(id, ToSnapshot(id, held), held.Token);
        }
    }

    /// <inheritdoc />
    public void RequireToken(Guid gameId, string token)
    {
        lock (_gate)
        {
            // Looked up without touching the game's activity: a stranger guessing at tokens must
            // not be what keeps a game from being retired.
            if (!_games.TryGetValue(gameId, out var held))
            {
                throw new NotFoundException("Game was not found.");
            }

            if (string.IsNullOrWhiteSpace(token) || !GroundGameGuards.TokensMatch(held.Token, token))
            {
                throw new UnauthorizedAccessException("The game token does not match this game.");
            }
        }
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto AddUnit(Guid gameId, AddStarGruntUnitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var held = Find(gameId);
            GroundGameGuards.RequireRoom(held.Game.Units.Count, GroundGameGuards.MaxUnitsPerGame, "units");
            var unit = ToDefinition(request);
            if (held.Game.HasUnit(unit.Id))
            {
                throw new InvalidOperationException($"There is already a unit called '{unit.Id}' in this game.");
            }

            return Store(gameId, held.Game.WithUnit(unit));
        }
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto GetSnapshot(Guid gameId)
    {
        lock (_gate)
        {
            return ToSnapshot(gameId, Find(gameId));
        }
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto BeginTurn(Guid gameId) => Command(gameId, game => game.BeginTurn());

    /// <inheritdoc />
    public StarGruntSnapshotDto ChooseFirstActivator(Guid gameId, ChooseFirstActivatorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.ChooseFirstActivator(new SideId(request.Side), request.TakeIt));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto BeginActivation(Guid gameId, BeginStarGruntActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.BeginActivation(new SideId(request.Side), new UnitId(request.UnitId)));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto TakeStep(Guid gameId, StarGruntStepRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var step = StepFor(request.Action);
        return Command(gameId, game => game.TakeStep(step));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto Fire(Guid gameId, StarGruntFireRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        GroundGameGuards.RequireAtMost(request.SupportWeapons?.Count ?? 0, GroundGameGuards.MaxWeaponsPerUnit, "support weapons");
        var command = new FireCommand
        {
            Firer = new UnitId(request.FirerId),
            Target = new UnitId(request.TargetId),
            WeaponName = GroundGameGuards.TruncateOptional(request.WeaponName)!,
            FirepowerDie = Die(request.FirepowerDie, nameof(request.FirepowerDie)),
            SupportWeapons = [.. (request.SupportWeapons ?? []).Select(GroundGameGuards.Truncate)],
            DistanceInches = request.DistanceInches,
            TargetPosture = new TargetPosture(Cover(request.Cover), request.InPosition),
        };

        return Command(gameId, game => game.Fire(command, _dice, _allocator));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto RemoveSuppression(Guid gameId, StarGruntUnitActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.RemoveSuppression(new UnitId(request.UnitId), _dice));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto TakeConfidenceTest(Guid gameId, StarGruntConfidenceTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.TakeConfidenceTest(new UnitId(request.UnitId), request.ThreatLevel, _dice));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto Rally(Guid gameId, StarGruntRallyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.Rally(new UnitId(request.RallyingUnitId), new UnitId(request.RalliedUnitId), _dice));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto Reorganise(Guid gameId, StarGruntUnitActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.Reorganise(new UnitId(request.UnitId)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unit integrity is measured with a ruler at the table, so this is a declaration rather than a
    /// calculation - the same stance taken on cover and range.
    /// </remarks>
    public StarGruntSnapshotDto SetDisorganised(Guid gameId, StarGruntDisorganisedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var held = Find(gameId);
            var unit = new UnitId(request.UnitId);
            if (!held.Game.HasUnit(unit))
            {
                throw new InvalidOperationException($"There is no unit called '{request.UnitId}' on the table.");
            }

            return Store(gameId, held.Game.WithStatus(unit, status => status with { IsDisorganised = request.IsDisorganised }));
        }
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto TakeReactionTest(Guid gameId, StarGruntReactionTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.TakeReactionTest(new UnitId(request.UnitId), request.ThreatLevel, _dice));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto SetLeavesCover(Guid gameId, StarGruntLeavesCoverRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.SetNextMoveLeavesCover(new UnitId(request.UnitId), request.LeavesCover));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto DeclareCharge(Guid gameId, StarGruntChargeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.DeclareCloseAssault(
            new UnitId(request.AttackerId), new UnitId(request.DefenderId), request.ThreatLevel, _dice));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto DefenderStands(Guid gameId, StarGruntStandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.DefenderStands(
            new UnitId(request.AttackerId), new UnitId(request.DefenderId), request.Terror, _dice));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto FightMelee(Guid gameId, StarGruntMeleeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        GroundGameGuards.RequireAtMost(request.Pairings?.Count ?? 0, GroundGameGuards.MaxMeleePairings, "melee pairings");
        var pairings = (request.Pairings ?? []).Select(pairing => new MeleePairing(
            pairing.AttackerShift,
            pairing.DefenderShift,
            pairing.AttackerPowerArmour,
            pairing.DefenderPowerArmour)).ToArray();

        return Command(gameId, game => game.FightMeleeRound(
            new UnitId(request.AttackerId),
            new UnitId(request.DefenderId),
            pairings,
            request.DefendersInCover,
            _dice));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto SettleTheDowned(Guid gameId, StarGruntSettleDownedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.SettleTheDowned(
            new UnitId(request.UnitId),
            request.Downed,
            request.WonTheAssault,
            request.DeadUpTo,
            request.WoundedUpTo,
            _dice));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto EndActivation(Guid gameId) => Command(gameId, game => game.EndActivation());

    /// <inheritdoc />
    public StarGruntSnapshotDto Pass(Guid gameId, StarGruntPassRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.Pass(new SideId(request.Side)));
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto EndTurn(Guid gameId) => Command(gameId, game => game.EndTurn());

    /// <summary>Runs a command, turning its refusal into the error the API reports as a 400.</summary>
    private StarGruntSnapshotDto Command(Guid gameId, Func<StarGruntGame, GameOutcome<StarGruntGame>> command)
    {
        lock (_gate)
        {
            var outcome = command(Find(gameId).Game);
            return outcome.IsAllowed
                ? Store(gameId, outcome.Value!)
                : throw new InvalidOperationException(outcome.Reason);
        }
    }

    /// <summary>
    /// The game, and a note that it is still in use. Every read and every write comes through
    /// here, so this is the one place that knows a game is not abandoned - a screen watching the
    /// table without touching anything counts.
    /// </summary>
    private Held Find(Guid gameId)
    {
        if (!_games.TryGetValue(gameId, out var held))
        {
            throw new NotFoundException("Game was not found.");
        }

        held = held with { LastActivity = DateTimeOffset.UtcNow };
        _games[gameId] = held;
        return held;
    }

    private StarGruntSnapshotDto Store(Guid gameId, StarGruntGame game)
    {
        var previous = _games[gameId];
        var held = previous with
        {
            Game = TrimLog(game),
            Version = previous.Version + 1,
            LastActivity = DateTimeOffset.UtcNow,
        };
        _games[gameId] = held;

        // Written inside the lock, like the Full Thrust store: the alternative has a window where
        // the game has moved on and the disk has not, and a crash in that window loses the turn
        // nobody wants to replay.
        Persist(gameId, held);
        return ToSnapshot(gameId, held);
    }

    private void Persist(Guid gameId, Held held) =>
        _store.Save(gameId, GroundGameRecord.Wrap(held.Token, held.LastActivity, StarGruntGameSerialization.Save(held.Game)));

    /// <summary>
    /// Drops the oldest log lines once the game passes its ceiling. Done here rather than in the
    /// game because the ceiling is this service's concern - the game is a value and does not know
    /// it is being sent over a wire on every change.
    /// </summary>
    private static StarGruntGame TrimLog(StarGruntGame game) =>
        game.Log.Length <= GroundGameGuards.MaxLogEntries
            ? game
            : game with { Log = game.Log.RemoveRange(0, game.Log.Length - GroundGameGuards.MaxLogEntries) };

    /// <summary>
    /// Drops games nobody has touched inside the retention window, and refuses a new one if the
    /// server is still full. Called when a game is opened rather than on a timer, so a process
    /// that is doing nothing stays doing nothing. A live game is never retired to make room.
    /// </summary>
    private void EvictIdleGames()
    {
        var cutoff = DateTimeOffset.UtcNow - GroundGameGuards.IdleGameRetention;
        var stale = _games.Where(entry => entry.Value.LastActivity < cutoff).Select(entry => entry.Key).ToArray();
        foreach (var gameId in stale)
        {
            // Gone on purpose, so the stored copy goes too; otherwise the next restart undoes it.
            _games.Remove(gameId);
            _store.Remove(gameId);
        }

        if (_games.Count >= GroundGameGuards.MaxConcurrentGames)
        {
            throw new InvalidOperationException(
                $"This server is already hosting {GroundGameGuards.MaxConcurrentGames} StarGrunt games, which is as many as it holds. Try again later.");
        }
    }

    private static UnitDefinition ToDefinition(AddStarGruntUnitRequest request)
    {
        GroundGameGuards.RequireAtMost(request.Figures?.Count ?? 0, GroundGameGuards.MaxMembersPerUnit, "figures");
        GroundGameGuards.RequireAtMost(request.Weapons?.Count ?? 0, GroundGameGuards.MaxWeaponsPerUnit, "weapons");

        return new UnitDefinition
        {
            Id = new UnitId(Required(request.Id, "A unit needs an id.")),
            Name = Required(request.Name, "A unit needs a name."),
            Side = new SideId(Required(request.Side, "A unit needs a side.")),
            Level = Enum.TryParse<CommandLevel>(request.Level, ignoreCase: true, out var level)
                ? level
                : throw new InvalidOperationException($"'{request.Level}' is not a command level."),
            QualityDie = Die(request.QualityDie, nameof(request.QualityDie)),
            LeadershipValue = Leadership(request.LeadershipValue),
            Fatigue = Enum.TryParse<FatigueLevel>(request.Fatigue, ignoreCase: true, out var fatigue)
                ? fatigue
                : throw new InvalidOperationException($"'{request.Fatigue}' is not a fatigue level (Fresh, Tired, Exhausted)."),
            Figures = [.. (request.Figures ?? []).Select(figure => new FigureProfile(Die(figure.ArmourDie, "armour")))],
            Weapons = [.. (request.Weapons ?? []).Select(weapon => new WeaponProfile
            {
                Name = Required(weapon.Name, "A weapon needs a name."),
                ImpactDie = Die(weapon.ImpactDie, "impact"),
                IsSupport = weapon.IsSupport,
                IsCloseRange = weapon.IsCloseRange,
                SupportFirepowerDie = Die(weapon.SupportFirepowerDie, "support firepower"),
                NeverJoinsSquadFire = weapon.NeverJoinsSquadFire,
            })],
        };
    }

    /// <summary>A string the request has to carry, trimmed and cut to the display ceiling.</summary>
    private static string Required(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : GroundGameGuards.Truncate(value);

    /// <summary>
    /// Checks a Leadership Value is one the rules recognise.
    /// </summary>
    /// <remarks>
    /// One to three, one being the best. Not a die: nothing rolls a leadership die, and every roll
    /// against a leader has to beat this number.
    /// </remarks>
    private static int Leadership(int value) =>
        value is >= 1 and <= 3
            ? value
            : throw new InvalidOperationException($"A Leadership Value of {value} is not one the rules use (1 to 3, 1 best).");

    /// <summary>Turns a face count into a rung of the ladder, refusing anything that is not one.</summary>
    private static QualityDie Die(int faces, string what) =>
        Enum.IsDefined(typeof(QualityDie), faces)
            ? (QualityDie)faces
            : throw new InvalidOperationException($"A {what} die of {faces} is not on the quality ladder (4, 6, 8, 10, 12).");

    private static CoverLevel Cover(string? cover) =>
        Enum.TryParse<CoverLevel>(cover, ignoreCase: true, out var level)
            ? level
            : throw new InvalidOperationException($"'{cover}' is not a cover level (None, Soft, Hard).");

    /// <summary>
    /// Turns an action's name into a step.
    /// </summary>
    /// <remarks>
    /// Fire, TransferAction and RemoveSuppression are not reachable here. The first two have to name
    /// what they spend, and a step built with an empty resource set is how a per-activation limit
    /// stops working; the third rolls a die, and this route deliberately has no die source. Each has
    /// a command of its own.
    /// </remarks>
    private static ActivationStep StepFor(string? action)
    {
        if (!Enum.TryParse<StarGruntAction>(action, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException($"'{action}' is not an action a unit can take.");
        }

        return parsed is StarGruntAction.Fire or StarGruntAction.TransferAction or StarGruntAction.RemoveSuppression
                or StarGruntAction.Rally or StarGruntAction.Reorganise
            ? throw new InvalidOperationException($"{parsed} has a command of its own, because it names what it spends or rolls for it.")
            : StarGruntSteps.Simple(parsed);
    }

    /// <remarks>
    /// The token is not here and must never be: a snapshot is what a screen shows, and the token
    /// is what lets it ask.
    /// </remarks>
    private static StarGruntSnapshotDto ToSnapshot(Guid gameId, Held held)
    {
        var game = held.Game;
        var legality = game.Legality();
        var activated = game.Session.Sides.SelectMany(side => side.Activated).ToHashSet();

        return new StarGruntSnapshotDto(
            gameId,
            game.Name,
            game.Session.TurnNumber,
            game.Session.Phase.ToString(),
            [.. game.Sides.Select(side => side.Value)],
            game.Session.ActiveSide?.Value,
            game.Session.CurrentFrame?.Unit.Value,
            // A game still being built has an empty session and so nobody entitled to the choice.
            // That is now the guard's own answer rather than a second copy of it here, so the read
            // and the write paths cannot drift apart on what an unbuilt game means.
            SequenceGuards.FirstActivationChooser(game.Session)?.Value,
            [.. game.Units.Values.Select(unit => ToUnitDto(game, unit, legality[unit.Id], activated.Contains(unit.Id)))],
            [.. game.Log],
            held.Version);
    }

    private static StarGruntUnitDto ToUnitDto(
        StarGruntGame game,
        UnitDefinition unit,
        UnitLegality legality,
        bool hasActivated)
    {
        var status = game.Status(unit.Id);
        return new StarGruntUnitDto(
            unit.Id.Value,
            unit.Name,
            unit.Side.Value,
            unit.Level.ToString(),
            (int)unit.QualityDie,
            unit.LeadershipValue,
            unit.Fatigue.ToString(),
            status.FiguresAlive,
            unit.FullStrength,
            status.FiguresWounded,
            status.IsLeaderDown,
            status.SuppressionMarkers,
            status.Confidence.ToString(),
            status.IsDisorganised,
            status.IsInCover,
            status.NextMoveLeavesCover,
            status.ReactionTestCleared,
            hasActivated,
            [.. unit.Weapons.Select(weapon => new StarGruntWeaponDto(
                weapon.Name,
                (int)weapon.ImpactDie,
                weapon.IsSupport,
                weapon.IsCloseRange,
                (int)weapon.SupportFirepowerDie,
                weapon.NeverJoinsSquadFire))],
            legality.CanActivate,
            legality.ActivationBlocker,
            [.. legality.Weapons.Select(weapon => new StarGruntWeaponLegalityDto(weapon.Name, weapon.CanFire, weapon.Blocker))]);
    }

    /// <summary>One game, how many times it has changed, the token that opens it, and when it was last touched.</summary>
    private sealed record Held(StarGruntGame Game, long Version, string Token, DateTimeOffset LastActivity);
}
