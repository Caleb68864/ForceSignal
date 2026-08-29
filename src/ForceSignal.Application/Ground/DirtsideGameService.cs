using System.Collections.Immutable;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Game;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.Dirtside.Sequence;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Application.Ground;

/// <summary>Application boundary for a Dirtside game.</summary>
public interface IDirtsideGameService : IGroundGameService
{
    /// <summary>Starts a game.</summary>
    DirtsideGameCreatedResponse CreateGame(CreateDirtsideGameRequest request);

    /// <summary>Puts a platoon on the table.</summary>
    DirtsideSnapshotDto AddPlatoon(Guid gameId, AddDirtsidePlatoonRequest request);

    /// <summary>Reads a game without changing it.</summary>
    DirtsideSnapshotDto GetSnapshot(Guid gameId);

    /// <summary>Opens the next turn.</summary>
    DirtsideSnapshotDto BeginTurn(Guid gameId);

    /// <summary>Settles who takes the first activation.</summary>
    DirtsideSnapshotDto ChooseFirstActivator(Guid gameId, ChooseDirtsideFirstActivatorRequest request);

    /// <summary>Opens an activation.</summary>
    DirtsideSnapshotDto BeginActivation(Guid gameId, BeginDirtsideActivationRequest request);

    /// <summary>Moves one element.</summary>
    DirtsideSnapshotDto MoveElement(Guid gameId, MoveDirtsideElementRequest request);

    /// <summary>Declares that an element is sitting the turn out.</summary>
    DirtsideSnapshotDto StandDown(Guid gameId, DirtsideStandDownRequest request);

    /// <summary>Switches an element's area-defence sensors on or off.</summary>
    DirtsideSnapshotDto SetAreaDefenceSensors(Guid gameId, DirtsideSensorsRequest request);

    /// <summary>Fires one element's weapon at one designated element.</summary>
    DirtsideSnapshotDto Fire(Guid gameId, DirtsideFireRequest request);

    /// <summary>Closes the open activation.</summary>
    DirtsideSnapshotDto EndActivation(Guid gameId);

    /// <summary>Declines to activate anything.</summary>
    DirtsideSnapshotDto Pass(Guid gameId, DirtsidePassRequest request);

    /// <summary>Closes the turn.</summary>
    DirtsideSnapshotDto EndTurn(Guid gameId);
}

/// <summary>
/// Holds Dirtside games in memory, writes them to a store, and turns refusals into errors.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the StarGrunt service, deliberately: games behind a lock, a version bumped on
/// every change, the state written to disk inside the lock so a crash cannot leave the game ahead of
/// the file.
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
public sealed class DirtsideGameService : IDirtsideGameService
{
    private readonly IQualityDiceRoller _dice;
    private readonly IChitPot _pot;
    private readonly IMatchStore _store;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Held> _games = [];
    private readonly List<SkippedSave> _skippedSaves = [];

    /// <summary>
    /// Builds the service and brings back whatever the store was holding, so a restart resumes the
    /// games rather than ending them.
    /// </summary>
    /// <param name="rollDie">Die source, injectable so tests can script a firefight.</param>
    /// <param name="store">Where games are written so they survive a restart.</param>
    /// <param name="pot">
    /// The chit pot every hit draws from, injectable so a test can put a known chit on the table.
    /// Defaults to a pot of the standard composition, shuffled properly.
    /// </param>
    public DirtsideGameService(
        IQualityDiceRoller? rollDie = null,
        IMatchStore? store = null,
        IChitPot? pot = null)
    {
        _dice = rollDie ?? new QualityDiceRoller();
        _pot = pot ?? new ChitPot();
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
    /// used to escape here and take every Dirtside route down with it. The table is this engine's
    /// own, so every skip is a real loss, and each is kept with its reason for the host to log.
    /// </remarks>
    private void RestoreAll()
    {
        foreach (var saved in _store.LoadAll())
        {
            try
            {
                var row = GroundGameRecord.Unwrap(saved.State);
                _games[saved.MatchId] = new Held(DirtsideGameSerialization.Restore(row.Game), 1, row.Token, row.LastActivity);
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
    public DirtsideGameCreatedResponse CreateGame(CreateDirtsideGameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = string.IsNullOrWhiteSpace(request.Name) ? "Dirtside" : GroundGameGuards.Truncate(request.Name);

        lock (_gate)
        {
            EvictIdleGames();
            var id = Guid.NewGuid();
            var held = new Held(DirtsideGame.Create(name), 1, GroundGameGuards.NewToken(), DateTimeOffset.UtcNow);
            _games[id] = held;
            Persist(id, held);
            return new DirtsideGameCreatedResponse(id, ToSnapshot(id, held), held.Token);
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
    public DirtsideSnapshotDto AddPlatoon(Guid gameId, AddDirtsidePlatoonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var held = Find(gameId);
            GroundGameGuards.RequireRoom(held.Game.Units.Count, GroundGameGuards.MaxUnitsPerGame, "platoons");
            var platoon = ToDefinition(request);
            if (held.Game.HasUnit(platoon.Id))
            {
                throw new InvalidOperationException($"There is already a platoon called '{platoon.Id}' in this game.");
            }

            return Store(gameId, held.Game.WithUnit(platoon));
        }
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto GetSnapshot(Guid gameId)
    {
        lock (_gate)
        {
            return ToSnapshot(gameId, Find(gameId));
        }
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto BeginTurn(Guid gameId) => Command(gameId, game => game.BeginTurn());

    /// <inheritdoc />
    public DirtsideSnapshotDto ChooseFirstActivator(Guid gameId, ChooseDirtsideFirstActivatorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.ChooseFirstActivator(new SideId(request.Side), request.TakeIt));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto BeginActivation(Guid gameId, BeginDirtsideActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.BeginActivation(new SideId(request.Side), new UnitId(request.UnitId)));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto MoveElement(Guid gameId, MoveDirtsideElementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.MoveElement(
            new ElementId(request.ElementId), request.OverHalfItsMovement));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto StandDown(Guid gameId, DirtsideStandDownRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.StandDown(new ElementId(request.ElementId)));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto SetAreaDefenceSensors(Guid gameId, DirtsideSensorsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.SetAreaDefenceSensors(new ElementId(request.ElementId), request.Live));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto Fire(Guid gameId, DirtsideFireRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var game = Find(gameId).Game;
            var firing = game.Session.CurrentFrame?.Unit
                ?? throw new InvalidOperationException("Nothing is activated, so nothing can fire.");

            var command = new FireCommand(
                firing,
                new ElementId(request.ElementId),
                GroundGameGuards.TruncateOptional(request.Weapon) ?? string.Empty,
                new UnitId(request.TargetUnitId),
                new ElementId(request.TargetElementId),
                Band(request.MeasuredBand));

            var outcome = game.Fire(command, _dice, _pot);
            return outcome.IsAllowed
                ? Store(gameId, outcome.Value!)
                : throw new InvalidOperationException(outcome.Reason);
        }
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto EndActivation(Guid gameId) => Command(gameId, game => game.EndActivation());

    /// <inheritdoc />
    public DirtsideSnapshotDto Pass(Guid gameId, DirtsidePassRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.Pass(new SideId(request.Side)));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto EndTurn(Guid gameId) => Command(gameId, game => game.EndTurn());

    private DirtsideSnapshotDto Command(Guid gameId, Func<DirtsideGame, GameOutcome<DirtsideGame>> command)
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

    private DirtsideSnapshotDto Store(Guid gameId, DirtsideGame game)
    {
        var previous = _games[gameId];
        var held = previous with
        {
            Game = TrimLog(game),
            Version = previous.Version + 1,
            LastActivity = DateTimeOffset.UtcNow,
        };
        _games[gameId] = held;

        // Written inside the lock, for the same reason as everywhere else: the alternative has a
        // window where the game has moved on and the disk has not.
        Persist(gameId, held);
        return ToSnapshot(gameId, held);
    }

    private void Persist(Guid gameId, Held held) =>
        _store.Save(gameId, GroundGameRecord.Wrap(held.Token, held.LastActivity, DirtsideGameSerialization.Save(held.Game)));

    /// <summary>
    /// Drops the oldest log lines once the game passes its ceiling. Done here rather than in the
    /// game because the ceiling is this service's concern - the game is a value and does not know
    /// it is being sent over a wire on every change.
    /// </summary>
    private static DirtsideGame TrimLog(DirtsideGame game) =>
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
                $"This server is already hosting {GroundGameGuards.MaxConcurrentGames} Dirtside games, which is as many as it holds. Try again later.");
        }
    }

    /// <summary>Reads a platoon off the wire and onto the table.</summary>
    private static PlatoonDefinition ToDefinition(AddDirtsidePlatoonRequest request)
    {
        GroundGameGuards.RequireAtMost(request.Elements?.Count ?? 0, GroundGameGuards.MaxMembersPerUnit, "elements");

        return new PlatoonDefinition(
            new UnitId(Required(request.Id, "A platoon needs an id.")),
            Required(request.Name, "A platoon needs a name."),
            new SideId(Required(request.Side, "A platoon needs a side.")),
            Enum.TryParse<DirtsideUnitKind>(request.Kind, ignoreCase: true, out var kind)
                ? kind
                : throw new InvalidOperationException($"'{request.Kind}' is not a unit kind."),
            request.IsCybertank,
            [.. (request.Elements ?? []).Select(ToElement)]);
    }

    private static ElementDefinition ToElement(DirtsideElementDto element)
    {
        ArgumentNullException.ThrowIfNull(element);
        GroundGameGuards.RequireAtMost(element.Weapons?.Count ?? 0, GroundGameGuards.MaxWeaponsPerUnit, "weapons");

        return new ElementDefinition(
            new ElementId(Required(element.Id, "An element needs an id.")),
            Required(element.Name, "An element needs a name."),
            Enum.TryParse<FireControlLevel>(element.FireControl, ignoreCase: true, out var control)
                ? control
                : throw new InvalidOperationException($"'{element.FireControl}' is not a fire control level."),
            element.Signature,
            element.ArmourValue,
            element.Movement,
            [.. (element.Weapons ?? []).Select(ToWeapon)]);
    }

    private static WeaponDefinition ToWeapon(DirtsideWeaponDto weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        return new WeaponDefinition(
            Required(weapon.Name, "A weapon needs a name."),
            weapon.ChitCount,
            Math.Max(1, weapon.Barrels),
            weapon.IsFixedMount,
            new WeaponValidityCard(
                ToValidity(weapon.Close),
                ToValidity(weapon.Medium),
                ToValidity(weapon.Long)),
            weapon.IsInterceptable);
    }

    /// <summary>
    /// Reads one row of a weapon's validity card.
    /// </summary>
    /// <remarks>
    /// A row nobody sent is ineffective rather than permissive. A weapon whose card is half filled in
    /// should do nothing at the bands its owner has not described, not everything.
    /// </remarks>
    private static ChitValidity ToValidity(DirtsideValidityDto? row)
    {
        if (row is null || row.IsIneffective)
        {
            return ChitValidity.Ineffective;
        }

        var colours = string.Equals(row.Colours, "All", StringComparison.OrdinalIgnoreCase)
            ? ChitColours.All
            : Enum.TryParse<ChitColours>(row.Colours, ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"'{row.Colours}' is not a set of chit colours.");

        var scale = Enum.TryParse<ChitValueScale>(row.ValueScale, ignoreCase: true, out var value)
            ? value
            : throw new InvalidOperationException($"'{row.ValueScale}' is not a chit value scale.");

        return new ChitValidity(colours, scale, row.SpecialsCount);
    }

    private static WeaponRangeBand Band(string? band) =>
        Enum.TryParse<WeaponRangeBand>(band, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"'{band}' is not a range band (Close, Medium, Long).");

    /// <summary>A string the request has to carry, trimmed and cut to the display ceiling.</summary>
    private static string Required(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : GroundGameGuards.Truncate(value);

    /// <summary>Turns a game into the shape a table reads.</summary>
    /// <remarks>
    /// Everything a screen needs to disable a button is computed here from the same checks the
    /// commands enforce, so the two cannot drift: the client renders, the game decides. The token
    /// is not here and must never be: a snapshot is what a screen shows, and the token is what lets
    /// it ask.
    /// </remarks>
    private static DirtsideSnapshotDto ToSnapshot(Guid gameId, Held held)
    {
        var game = held.Game;
        var frame = game.Session.CurrentFrame;
        var policy = new DirtsideActivationPolicy(game);
        var stillToChoose = frame is { Kind: FrameKind.Activation }
            ? policy.StillToChoose(frame).Select(element => element.Value).ToArray()
            : [];

        var endOutcome = game.EndActivation();

        return new DirtsideSnapshotDto(
            gameId,
            game.Name,
            game.Session.TurnNumber,
            game.Session.Phase.ToString(),
            [.. game.Sides.Select(side => side.Value)],
            game.Session.ActiveSide is { } active && active != default ? active.Value : null,
            frame?.Unit.Value,
            stillToChoose,
            endOutcome.IsAllowed,
            endOutcome.Reason,
            [.. game.Units.Values.Select(platoon => ToPlatoonState(game, platoon, frame))],
            [.. game.Log],
            held.Version);
    }

    private static DirtsidePlatoonStateDto ToPlatoonState(
        DirtsideGame game,
        PlatoonDefinition platoon,
        ActivationFrame? frame)
    {
        var status = game.Status(platoon.Id);
        var chosen = frame is { Kind: FrameKind.Activation } && frame.Unit == platoon.Id
            ? frame.Steps.IsDefaultOrEmpty
                ? []
                : frame.Steps.Select(step => step.Subject).OfType<ElementId>().ToImmutableHashSet()
            : ImmutableHashSet<ElementId>.Empty;

        var frameForThisUnit = frame is { Kind: FrameKind.Activation } && frame.Unit == platoon.Id ? frame : null;
        var activation = game.BeginActivation(platoon.Side, platoon.Id);
        var hasActivated = game.Session.Sides
            .FirstOrDefault(side => side.Id == platoon.Side)
            ?.Unactivated.Contains(platoon.Id) == false;

        return new DirtsidePlatoonStateDto(
            platoon.Id.Value,
            platoon.Name,
            platoon.Side.Value,
            platoon.Kind.ToString(),
            platoon.IsCybertank,
            status.Confidence.ToString(),
            status.IsUnderFire,
            status.IsDisorganised,
            hasActivated,
            activation.IsAllowed,
            activation.Reason,
            [.. platoon.Elements.Select(element => ToElementState(status, element, chosen, frameForThisUnit))]);
    }

    private static DirtsideElementStateDto ToElementState(
        PlatoonStatus status,
        ElementDefinition element,
        ImmutableHashSet<ElementId> chosen,
        ActivationFrame? frame)
    {
        var state = status.Element(element.Id);

        // Read off the frame's spent resources rather than inferred from the steps, because that is
        // where the activation rules read them too - so a screen cannot come to a different view of
        // what an element has left than the check that will refuse it.
        var hasMoved = frame?.HasSpent(DirtsideSteps.Moved(element.Id)) == true;
        var hasActed = frame?.HasSpent(DirtsideSteps.Acted(element.Id)) == true;
        var hasStoodDown = frame?.HasSpent(DirtsideSteps.StoodDown(element.Id)) == true;

        return new DirtsideElementStateDto(
            element.Id.Value,
            element.Name,
            state.IsDestroyed,
            state.IsDamaged,
            state.IsSystemsDown,
            state.MovedOverHalf,
            state.AreaDefenceSensorsLive,
            chosen.Contains(element.Id),
            hasMoved,
            hasActed,
            hasStoodDown,
            [.. element.Weapons.Select(weapon => weapon.Name)]);
    }

    /// <summary>One game, the version it is on, the token that opens it, and when it was last touched.</summary>
    private sealed record Held(DirtsideGame Game, int Version, string Token, DateTimeOffset LastActivity);
}
