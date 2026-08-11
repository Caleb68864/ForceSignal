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
public interface IDirtsideGameService
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
/// </remarks>
/// <param name="rollDie">Die source, injectable so tests can script a firefight.</param>
/// <param name="store">Where games are written so they survive a restart.</param>
/// <param name="pot">
/// The chit pot every hit draws from, injectable so a test can put a known chit on the table.
/// Defaults to a pot of the standard composition, shuffled properly.
/// </param>
public sealed class DirtsideGameService(
    IQualityDiceRoller? rollDie = null,
    IMatchStore? store = null,
    IChitPot? pot = null)
    : IDirtsideGameService
{
    private readonly IQualityDiceRoller _dice = rollDie ?? new QualityDiceRoller();
    private readonly IChitPot _pot = pot ?? new ChitPot();
    private readonly IMatchStore _store = store ?? NoMatchStore.Instance;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Held> _games = RestoreAll(store);

    /// <summary>
    /// Brings back whatever the store was holding, so a restart resumes the games rather than
    /// ending them.
    /// </summary>
    /// <remarks>
    /// A save that cannot be read is skipped rather than allowed to stop the others loading. The
    /// store holds every kind of game side by side, so most of what this skips is simply somebody
    /// else's - a Full Thrust match, or a StarGrunt one.
    /// </remarks>
    private static Dictionary<Guid, Held> RestoreAll(IMatchStore? store)
    {
        var games = new Dictionary<Guid, Held>();
        foreach (var saved in store?.LoadAll() ?? [])
        {
            try
            {
                games[saved.MatchId] = new Held(DirtsideGameSerialization.Restore(saved.State), 1);
            }
            catch (ArgumentException)
            {
                // Not a Dirtside game, or not one this version understands.
            }
        }

        return games;
    }

    /// <inheritdoc />
    public DirtsideGameCreatedResponse CreateGame(CreateDirtsideGameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = string.IsNullOrWhiteSpace(request.Name) ? "Dirtside" : request.Name.Trim();

        lock (_gate)
        {
            var id = Guid.NewGuid();
            var game = DirtsideGame.Create(name);
            _games[id] = new Held(game, 1);
            _store.Save(id, DirtsideGameSerialization.Save(game));
            return new DirtsideGameCreatedResponse(id, ToSnapshot(id, _games[id]));
        }
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto AddPlatoon(Guid gameId, AddDirtsidePlatoonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var held = Find(gameId);
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
                request.Weapon ?? string.Empty,
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

    private Held Find(Guid gameId) =>
        _games.TryGetValue(gameId, out var held) ? held : throw new InvalidOperationException("Game was not found.");

    private DirtsideSnapshotDto Store(Guid gameId, DirtsideGame game)
    {
        var held = new Held(game, _games[gameId].Version + 1);
        _games[gameId] = held;

        // Written inside the lock, for the same reason as everywhere else: the alternative has a
        // window where the game has moved on and the disk has not.
        _store.Save(gameId, DirtsideGameSerialization.Save(game));
        return ToSnapshot(gameId, held);
    }

    /// <summary>Reads a platoon off the wire and onto the table.</summary>
    private static PlatoonDefinition ToDefinition(AddDirtsidePlatoonRequest request) => new(
        new UnitId(Required(request.Id, "A platoon needs an id.")),
        Required(request.Name, "A platoon needs a name."),
        new SideId(Required(request.Side, "A platoon needs a side.")),
        Enum.TryParse<DirtsideUnitKind>(request.Kind, ignoreCase: true, out var kind)
            ? kind
            : throw new InvalidOperationException($"'{request.Kind}' is not a unit kind."),
        request.IsCybertank,
        [.. (request.Elements ?? []).Select(ToElement)]);

    private static ElementDefinition ToElement(DirtsideElementDto element)
    {
        ArgumentNullException.ThrowIfNull(element);

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

    private static string Required(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value.Trim();

    /// <summary>Turns a game into the shape a table reads.</summary>
    /// <remarks>
    /// Everything a screen needs to disable a button is computed here from the same checks the
    /// commands enforce, so the two cannot drift: the client renders, the game decides.
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

    /// <summary>One game and the version it is on.</summary>
    private sealed record Held(DirtsideGame Game, int Version);
}
