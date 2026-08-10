using System.Collections.Immutable;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Ground;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Combat;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Application.Ground;

/// <summary>Application boundary for a StarGrunt game.</summary>
public interface IStarGruntGameService
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
/// </remarks>
/// <param name="rollDie">Die source, injectable so tests can script a firefight.</param>
/// <param name="store">
/// Where games are written so they survive a restart. Defaults to keeping nothing, which is what a
/// test and a throwaway session want.
/// </param>
public sealed class StarGruntGameService(IQualityDiceRoller? rollDie = null, IMatchStore? store = null)
    : IStarGruntGameService
{
    private readonly IQualityDiceRoller _dice = rollDie ?? new QualityDiceRoller();
    private readonly IMatchStore _store = store ?? NoMatchStore.Instance;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Held> _games = RestoreAll(store);

    /// <summary>
    /// Brings back whatever the store was holding, so a restart resumes the games rather than
    /// ending them.
    /// </summary>
    /// <remarks>
    /// A save that cannot be read is skipped rather than allowed to stop the others loading. It is
    /// almost always a game written by an older shape of the code, and losing one is better than
    /// refusing to start.
    /// </remarks>
    private static Dictionary<Guid, Held> RestoreAll(IMatchStore? store)
    {
        var games = new Dictionary<Guid, Held>();
        foreach (var saved in store?.LoadAll() ?? [])
        {
            try
            {
                games[saved.MatchId] = new Held(StarGruntGameSerialization.Restore(saved.State), 1);
            }
            catch (ArgumentException)
            {
                // Not a StarGrunt game, or not one this version understands.
            }
        }

        return games;
    }

    /// <inheritdoc />
    public StarGruntGameCreatedResponse CreateGame(CreateStarGruntGameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = string.IsNullOrWhiteSpace(request.Name) ? "StarGrunt" : request.Name.Trim();

        lock (_gate)
        {
            var id = Guid.NewGuid();
            var game = StarGruntGame.Create(name);
            _games[id] = new Held(game, 1);
            _store.Save(id, StarGruntGameSerialization.Save(game));
            return new StarGruntGameCreatedResponse(id, ToSnapshot(id, _games[id]));
        }
    }

    /// <inheritdoc />
    public StarGruntSnapshotDto AddUnit(Guid gameId, AddStarGruntUnitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var held = Find(gameId);
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
        var command = new FireCommand
        {
            Firer = new UnitId(request.FirerId),
            Target = new UnitId(request.TargetId),
            WeaponName = request.WeaponName,
            FirepowerDie = Die(request.FirepowerDie, nameof(request.FirepowerDie)),
            SupportDice = [.. (request.SupportDice ?? []).Select(die => Die(die, nameof(request.SupportDice)))],
            DistanceInches = request.DistanceInches,
            TargetPosture = new TargetPosture(Cover(request.Cover), request.InPosition),
        };

        return Command(gameId, game => game.Fire(command, _dice));
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

    private Held Find(Guid gameId) =>
        _games.TryGetValue(gameId, out var held) ? held : throw new InvalidOperationException("Game was not found.");

    private StarGruntSnapshotDto Store(Guid gameId, StarGruntGame game)
    {
        var held = new Held(game, _games[gameId].Version + 1);
        _games[gameId] = held;

        // Written inside the lock, like the Full Thrust store: the alternative has a window where
        // the game has moved on and the disk has not, and a crash in that window loses the turn
        // nobody wants to replay.
        _store.Save(gameId, StarGruntGameSerialization.Save(game));
        return ToSnapshot(gameId, held);
    }

    private static UnitDefinition ToDefinition(AddStarGruntUnitRequest request) => new()
    {
        Id = new UnitId(Required(request.Id, "A unit needs an id.")),
        Name = Required(request.Name, "A unit needs a name."),
        Side = new SideId(Required(request.Side, "A unit needs a side.")),
        Level = Enum.TryParse<CommandLevel>(request.Level, ignoreCase: true, out var level)
            ? level
            : throw new InvalidOperationException($"'{request.Level}' is not a command level."),
        QualityDie = Die(request.QualityDie, nameof(request.QualityDie)),
        LeadershipDie = Die(request.LeadershipDie, nameof(request.LeadershipDie)),
        Figures = [.. (request.Figures ?? []).Select(figure => new FigureProfile(Die(figure.ArmourDie, "armour")))],
        Weapons = [.. (request.Weapons ?? []).Select(weapon => new WeaponProfile
        {
            Name = Required(weapon.Name, "A weapon needs a name."),
            ImpactDie = Die(weapon.ImpactDie, "impact"),
            IsSupport = weapon.IsSupport,
            IsCloseRange = weapon.IsCloseRange,
        })],
    };

    private static string Required(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value.Trim();

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
    /// Fire and TransferAction are not reachable here: both have to name what they spend, so both
    /// have their own command. Sending either through this route would build a step with an empty
    /// resource set, which is exactly how a per-activation limit stops working.
    /// </remarks>
    private static ActivationStep StepFor(string? action)
    {
        if (!Enum.TryParse<StarGruntAction>(action, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException($"'{action}' is not an action a unit can take.");
        }

        return parsed is StarGruntAction.Fire or StarGruntAction.TransferAction
            ? throw new InvalidOperationException($"{parsed} has to name what it spends, so it has a command of its own.")
            : StarGruntSteps.Simple(parsed);
    }

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
            // Asked only once both sides exist. A game still being built has an empty session, and
            // the guard reads the two sides to compare their strengths.
            game.Session.Sides.Length == 2 ? SequenceGuards.FirstActivationChooser(game.Session)?.Value : null,
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
            (int)unit.LeadershipDie,
            status.FiguresAlive,
            unit.FullStrength,
            status.FiguresWounded,
            status.SuppressionMarkers,
            status.Confidence.ToString(),
            status.IsDisorganised,
            status.IsInCover,
            hasActivated,
            [.. unit.Weapons.Select(weapon => new StarGruntWeaponDto(
                weapon.Name,
                (int)weapon.ImpactDie,
                weapon.IsSupport,
                weapon.IsCloseRange))],
            legality.CanActivate,
            legality.ActivationBlocker,
            [.. legality.Weapons.Select(weapon => new StarGruntWeaponLegalityDto(weapon.Name, weapon.CanFire, weapon.Blocker))]);
    }

    /// <summary>A game and how many times it has changed.</summary>
    private readonly record struct Held(StarGruntGame Game, long Version);
}
