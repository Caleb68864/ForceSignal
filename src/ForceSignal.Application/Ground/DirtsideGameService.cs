using System.Collections.Immutable;
using System.Text.Json;
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

    /// <summary>Tries to get an element's Systems Down marker off.</summary>
    DirtsideSnapshotDto RecoverSystems(Guid gameId, DirtsideRecoverSystemsRequest request);

    /// <summary>Orders the activated platoon in against a position, and rolls its nerve to go.</summary>
    DirtsideSnapshotDto LaunchAssault(Guid gameId, LaunchDirtsideAssaultRequest request);

    /// <summary>Rolls the assaulted platoon's nerve to stand, or give up the position.</summary>
    DirtsideSnapshotDto DefenderStands(Guid gameId, DirtsideAssaultStandRequest request);

    /// <summary>Fights one round of the open assault.</summary>
    DirtsideSnapshotDto FightAssaultRound(Guid gameId);

    /// <summary>Takes the tests after a round: who, if anybody, has had enough.</summary>
    DirtsideSnapshotDto ResolveAssaultAftermath(Guid gameId, DirtsideAssaultAftermathRequest request);

    /// <summary>The winner's test to drive on through the position it has taken.</summary>
    DirtsideSnapshotDto FollowThrough(Guid gameId, DirtsideFollowThroughRequest request);

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
    private readonly Func<ChitPotComposition, IChitPot> _pot;
    private readonly IMatchStore _store;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Held> _games = [];
    private readonly List<SkippedSave> _skippedSaves = [];

    /// <summary>
    /// How the settings blob is written into the stored row. Web defaults so the field names match
    /// the ones the same DTO carries on the wire - a row and a snapshot describing the same pot in
    /// two spellings is the kind of drift this codebase has already paid for once.
    /// </summary>
    private static readonly JsonSerializerOptions SettingsJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds the service and brings back whatever the store was holding, so a restart resumes the
    /// games rather than ending them.
    /// </summary>
    /// <param name="rollDie">Die source, injectable so tests can script a firefight.</param>
    /// <param name="store">Where games are written so they survive a restart.</param>
    /// <param name="pot">
    /// How a pot is made from a composition, injectable so a test can put a known chit on the table.
    /// A factory rather than a pot, because the composition is the players' and belongs to one game:
    /// a single injected pot was a process-wide singleton, which meant every table on the server drew
    /// from the same bag of chits and none of them could say what was in it. Production builds a
    /// properly shuffled <see cref="ChitPot"/> around whatever that game's players counted.
    /// </param>
    public DirtsideGameService(
        IQualityDiceRoller? rollDie = null,
        IMatchStore? store = null,
        Func<ChitPotComposition, IChitPot>? pot = null)
    {
        _dice = rollDie ?? new QualityDiceRoller();
        _pot = pot ?? (composition => new ChitPot(composition));
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

                // A row stored before the pot became the players' carries no settings at all, and
                // falls back exactly as a create request that names no pot does. That fallback is
                // the whole reason the default survives this release: without it every stored
                // Dirtside game on the machine would have been retired by this change.
                var (composition, isDefault) = ReadStoredPot(row.Settings);

                _games[saved.MatchId] = new Held(
                    DirtsideGameSerialization.Restore(row.Game),
                    1,
                    row.Token,
                    row.LastActivity,
                    composition,
                    isDefault,
                    _pot(composition));
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

        // Read before the lock is taken: a pot that does not describe a bag is a refusal, and there
        // is no reason to hold every other table up while it is worked out.
        var (composition, isDefault) = DirtsideChitPotMapping.FromRequest(request.ChitPot);

        lock (_gate)
        {
            EvictIdleGames();
            var id = Guid.NewGuid();
            var held = new Held(
                DirtsideGame.Create(name),
                1,
                GroundGameGuards.NewToken(),
                DateTimeOffset.UtcNow,
                composition,
                isDefault,
                _pot(composition));
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
            var held = Find(gameId);
            var game = held.Game;
            var firing = game.Session.CurrentFrame?.Unit
                ?? throw new InvalidOperationException("Nothing is activated, so nothing can fire.");

            var command = new FireCommand(
                firing,
                new ElementId(request.ElementId),
                GroundGameGuards.TruncateOptional(request.Weapon) ?? string.Empty,
                new UnitId(request.TargetUnitId),
                new ElementId(request.TargetElementId),
                Band(request.MeasuredBand),
                request.WillMoveOverHalf);

            var outcome = game.Fire(command, _dice, held.Pot);
            return outcome.IsAllowed
                ? Store(gameId, outcome.Value!)
                : throw new InvalidOperationException(outcome.Reason);
        }
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto RecoverSystems(Guid gameId, DirtsideRecoverSystemsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.RecoverSystems(new ElementId(request.ElementId), _dice));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto LaunchAssault(Guid gameId, LaunchDirtsideAssaultRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var commitment = ToCommitment(request.ElementIds, request.ThreatLevel, request.Validity, request.HandToHandValidity);
        return Command(gameId, game => game.LaunchAssault(new UnitId(request.TargetUnitId ?? string.Empty), commitment, _dice));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto DefenderStands(Guid gameId, DirtsideAssaultStandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var commitment = ToCommitment(request.ElementIds, request.ThreatLevel, request.Validity, request.HandToHandValidity);
        return Command(gameId, game => game.DefenderStands(commitment, _dice));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto FightAssaultRound(Guid gameId) =>
        CommandWithPot(gameId, (game, pot) => game.FightAssaultRound(pot));

    /// <inheritdoc />
    public DirtsideSnapshotDto ResolveAssaultAftermath(Guid gameId, DirtsideAssaultAftermathRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.ResolveAssaultAftermath(request.LightCasualtyThreat, request.HeavyCasualtyThreat, _dice));
    }

    /// <inheritdoc />
    public DirtsideSnapshotDto FollowThrough(Guid gameId, DirtsideFollowThroughRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Command(gameId, game => game.FollowThrough(request.ThreatLevel, _dice));
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
    /// The same, for the commands that draw chits. Separate rather than folded in because the pot is
    /// this game's and has to be looked up with it: a command that reached for a field on the service
    /// would be drawing from whatever bag the last table happened to leave there.
    /// </summary>
    private DirtsideSnapshotDto CommandWithPot(Guid gameId, Func<DirtsideGame, IChitPot, GameOutcome<DirtsideGame>> command)
    {
        lock (_gate)
        {
            var held = Find(gameId);
            var outcome = command(held.Game, held.Pot);
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
        _store.Save(
            gameId,
            GroundGameRecord.Wrap(
                held.Token,
                held.LastActivity,
                DirtsideGameSerialization.Save(held.Game),
                WriteSettings(held)));

    /// <summary>
    /// What this service knows about a game that the module's document does not: the chit pot its
    /// players counted.
    /// </summary>
    /// <remarks>
    /// Written into the row's optional settings field rather than into the module's document, for the
    /// same reason the token is: the module takes a pot as an argument to the two commands that draw
    /// from one and has no field for it, and giving it one would make the game value carry something
    /// no rule reads.
    /// </remarks>
    private static string WriteSettings(Held held) =>
        JsonSerializer.Serialize(
            new StoredSettings(DirtsideChitPotMapping.ToDto(held.Composition, held.ChitPotIsBuiltInDefault)),
            SettingsJson);

    /// <summary>Reads the settings back, or nothing when the row carried none.</summary>
    private static StoredSettings? ReadSettings(string? settings) =>
        string.IsNullOrWhiteSpace(settings) ? null : JsonSerializer.Deserialize<StoredSettings>(settings, SettingsJson);

    /// <summary>
    /// Reads a stored row's chit pot, falling back the way a row that carries no settings does.
    /// </summary>
    /// <param name="settings">The row's settings blob, or null when it carries none.</param>
    /// <returns>The composition, and whether it is the built-in guess rather than the players'.</returns>
    /// <remarks>
    /// The settings blob is what this service knows about a game beyond the module's document; it
    /// is not the game. So a blob that cannot be read costs the settings and nothing else.
    /// <para>
    /// This used to be read inside the per-row try whose catch is <c>catch (Exception)</c>, which
    /// meant a settings field that was present and not this version's shape - <c>{"chitPot":[]}</c>,
    /// drift rather than garbage - retired the whole game: document, token, turn and all, on a row
    /// that was perfectly readable. The optional-field design was chosen precisely so a settings
    /// mismatch would not do that, and it survived the field being *absent* and not the field being
    /// *present and different*, which is what the next change to this blob produces.
    /// </para>
    /// <para>
    /// Falling back is not silent: the pot comes back flagged as the built-in guess, which readiness
    /// warns about and the screen prints in red beside the turn number, so a table is told the
    /// counts are not theirs before the first shot.
    /// </para>
    /// </remarks>
    private static (ChitPotComposition Composition, bool IsBuiltInDefault) ReadStoredPot(string? settings)
    {
        try
        {
            var stored = ReadSettings(settings);
            return stored?.ChitPot is { } counts
                ? (DirtsideChitPotMapping.FromDto(counts), counts.IsBuiltInDefaultGuess)
                : (ChitPotComposition.Default, true);
        }
        // JsonException is a blob of another shape; InvalidOperationException is a blob whose counts
        // no longer describe a pot this version can build - a colour that has been renamed, say.
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return (ChitPotComposition.Default, true);
        }
    }

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

    /// <summary>
    /// Reads what one side commits to an assault off the wire.
    /// </summary>
    /// <remarks>
    /// The validity row is required rather than defaulted to ineffective, as a weapon card's missing
    /// row is: a side that commits to an assault with no idea what its chits count has made a
    /// mistake worth refusing, not a choice worth honouring.
    /// </remarks>
    private static AssaultCommitment ToCommitment(
        IReadOnlyList<string>? elementIds,
        int threatLevel,
        DirtsideValidityDto? validity,
        DirtsideValidityDto? handToHand)
    {
        GroundGameGuards.RequireAtMost(elementIds?.Count ?? 0, GroundGameGuards.MaxMembersPerUnit, "elements");
        if (validity is null)
        {
            throw new InvalidOperationException("An assault has to say what its chits may count.");
        }

        return new AssaultCommitment(
            [.. (elementIds ?? []).Select(id => new ElementId(GroundGameGuards.TruncateOptional(id) ?? string.Empty))],
            ToValidity(validity),
            handToHand is null ? null : ToValidity(handToHand),
            threatLevel);
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
            [.. (request.Elements ?? []).Select(ToElement)],
            Die(request.QualityDie),
            request.LeadershipValue);
    }

    /// <summary>Reads the die off a command marker, or nothing when the card does not say.</summary>
    private static QualityDie? Die(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
        : Enum.TryParse<QualityDie>(name, ignoreCase: true, out var die) && Enum.IsDefined(die) ? die
        : throw new InvalidOperationException(
            $"'{name}' is not a quality die ({string.Join(", ", DirtsideWire.QualityDice)}).");

    private static ElementDefinition ToElement(DirtsideElementDto element)
    {
        ArgumentNullException.ThrowIfNull(element);
        GroundGameGuards.RequireAtMost(element.Weapons?.Count ?? 0, GroundGameGuards.MaxWeaponsPerUnit, "weapons");

        return new ElementDefinition(
            new ElementId(Required(element.Id, "An element needs an id.")),
            Required(element.Name, "An element needs a name."),
            Enum.TryParse<FireControlLevel>(element.FireControl, ignoreCase: true, out var control)
                ? control
                : throw new InvalidOperationException(
                    $"'{element.FireControl}' is not a fire control level ({string.Join(", ", DirtsideWire.FireControls)})."),
            element.Signature,
            element.ArmourValue,
            element.Movement,
            [.. (element.Weapons ?? []).Select(ToWeapon)],
            element.HasBackupSystems,
            element.AssaultChits,
            element.KillThreshold);
    }

    private static WeaponDefinition ToWeapon(DirtsideWeaponDto weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        return new WeaponDefinition(
            Required(weapon.Name, "A weapon needs a name."),
            weapon.ChitCount,
            // Clamped at both ends. The floor is what DirectFire.Resolve requires; the ceiling is
            // there because the barrel count is the trip count of a dice loop that runs inside the
            // service's lock, so a mount declaring two billion barrels rolls two billion dice with
            // every other game on the server waiting behind it. StarGruntGame.Assault.cs clamps its
            // downed-figure count for exactly this, and this is the same loop one engine over.
            Math.Clamp(weapon.Barrels, 1, GroundGameGuards.MaxBarrelsPerMount),
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
            : throw new InvalidOperationException(
                $"'{band}' is not a range band ({string.Join(", ", DirtsideWire.Bands)}).");

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
            game.Assault is { } assault ? ToAssaultState(assault) : null,
            [.. game.Units.Values.Select(platoon => ToPlatoonState(game, platoon, frame))],
            [.. game.Log],
            held.Version,

            // On every snapshot rather than only on create, because the composition is the single
            // most sensitive input in the damage model and a table settling an argument about a draw
            // should be able to read what is in the bag without going back to whoever started the
            // game. It carries its own honesty flag: these counts are either theirs or the guess.
            DirtsideChitPotMapping.ToDto(held.Composition, held.ChitPotIsBuiltInDefault));
    }

    private static DirtsideAssaultDto ToAssaultState(DirtsideAssault assault) => new(
        assault.Attacker.Unit.Value,
        assault.DefenderUnit.Value,
        assault.Stage.ToString(),
        assault.Round,
        [.. assault.Attacker.Stands.Select(element => element.Value)],
        assault.Defender is { } defender ? [.. defender.Stands.Select(element => element.Value)] : []);

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
            [.. platoon.Elements.Select(element => ToElementState(game, status, element, chosen, frameForThisUnit))],
            platoon.Quality?.ToString(),
            platoon.LeadershipValue);
    }

    private static DirtsideElementStateDto ToElementState(
        DirtsideGame game,
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

        // Only asked of the platoon whose activation is open: for anybody else the answer is
        // "nothing is activated", which is true and not what a screen wants beside every vehicle.
        var recovery = frame is null ? "Nothing is activated." : game.WhyRecoverSystemsIsRefused(element.Id);

        // What the tape may measure out to now, rather than what the record card says. A DMG marker
        // halves an element's movement, and the engine had that rule written and tested and applied
        // to nothing: the card's number went into the roster and came back out unchanged, so a
        // damaged vehicle reported the same movement it had when it was whole and the player was
        // left to remember. It is a halving of the player's own number, not a number of ours.
        var movement = state.IsDamaged ? DamagedEffects.Movement(element.Movement) : element.Movement;

        return new DirtsideElementStateDto(
            element.Id.Value,
            element.Name,
            state.IsDestroyed,
            state.IsDamaged,
            state.IsSystemsDown,
            state.IsImmobilised,
            state.MovedOverHalf,
            state.AreaDefenceSensorsLive,
            movement,
            chosen.Contains(element.Id),
            hasMoved,
            hasActed,
            hasStoodDown,
            [.. element.Weapons.Select(weapon => weapon.Name)],
            element.HasBackupSystems,
            element.AssaultChits,
            element.KillThreshold,
            recovery is null,
            recovery);
    }

    /// <summary>
    /// One game, the version it is on, the token that opens it, when it was last touched, and the
    /// pot its players counted.
    /// </summary>
    /// <param name="Game">The game itself.</param>
    /// <param name="Version">Bumped on every change.</param>
    /// <param name="Token">What a caller has to present to touch it.</param>
    /// <param name="LastActivity">When it was last read or changed.</param>
    /// <param name="Composition">What is in this game's chit pot.</param>
    /// <param name="ChitPotIsBuiltInDefault">
    /// True when nobody counted a pot for this game and it fell back to the built-in default, whose
    /// special counts are a guess. Carried so the table can be told, and so the fact survives a
    /// restart rather than being re-derived by comparing counts.
    /// </param>
    /// <param name="Pot">
    /// The pot built around <paramref name="Composition"/>, built once when the game is created or
    /// restored rather than per command: a pot is cheap but it holds the shuffle's randomness, and
    /// rebuilding it every draw would hand each resolution a brand-new source.
    /// </param>
    private sealed record Held(
        DirtsideGame Game,
        int Version,
        string Token,
        DateTimeOffset LastActivity,
        ChitPotComposition Composition,
        bool ChitPotIsBuiltInDefault,
        IChitPot Pot);

    /// <summary>What the stored row's settings field holds for this engine.</summary>
    /// <param name="ChitPot">The pot this game draws from.</param>
    private sealed record StoredSettings(DirtsideChitPotDto? ChitPot);
}
