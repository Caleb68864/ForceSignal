using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// A unit whose record card never said what its Leadership Value was.
/// </summary>
/// <remarks>
/// <para>
/// <c>UnitDefinition.LeadershipValue</c> opened on <c>= 2</c>, which is the same shape
/// <c>QualityDie</c> was fixed for: a saved game is JSON, and the deserialiser fills a property the
/// bytes do not carry from exactly that expression. A blob written before the property existed -
/// StarGrunt's units carried a <c>LeadershipDie</c> first - comes back with a rating nobody gave,
/// and it is the number every roll in the game that matters is measured against: nerve, reaction,
/// charging, standing to receive a charge, shaking off suppression, and being rallied.
/// </para>
/// <para>
/// The fix is the range page's rather than <c>QualityDie</c>'s: the value is nullable and the
/// <em>action</em> that reads it is refused by name, so a stored game still opens and then says what
/// is missing. Refusing the blob outright would retire the game; substituting a 2 is the defect.
/// </para>
/// <para>
/// Every refusal here has an accept control beside it, because a guard that refused everything would
/// pass all six of the refusal tests on its own and fail nothing.
/// </para>
/// </remarks>
public sealed class GameLeadershipTests
{
    private const string Named = "Leadership Value";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ANerveTestIsRefusedByName(bool absent)
    {
        var game = Unrated(absent);

        var refused = game.TakeConfidenceTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(1));

        Assert.False(refused.IsAllowed);
        Assert.Contains(Named, refused.Reason!, StringComparison.Ordinal);
        Assert.Contains("Alpha Squad", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ANerveTestIsStillTakenByAUnitWhoseCardDoesSay()
    {
        // The accept control. Threat 2 against leadership 2 is a score of 4, and a 6 beats it.
        var held = GameFixtures.TwoSquadGame()
            .TakeConfidenceTest(GameFixtures.Alpha, threatLevel: 2, new ScriptedDice(6));

        Assert.True(held.IsAllowed);
        Assert.Equal(ConfidenceLevel.Confident, held.Value!.Status(GameFixtures.Alpha).Confidence);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReactionTestIsRefusedByNameAndTheActionIsNotSpent(bool absent)
    {
        var game = Activated(Unrated(absent))
            .SetNextMoveLeavesCover(GameFixtures.Alpha, leavesCover: true).Value!;

        var refused = game.TakeReactionTest(GameFixtures.Alpha, threatLevel: 1, new ScriptedDice(1));

        Assert.False(refused.IsAllowed);
        Assert.Contains(Named, refused.Reason!, StringComparison.Ordinal);

        // Nothing was spent. A failed reaction test *is* a spent action, so a refusal that landed
        // after the roll would be indistinguishable from the troops declining the order.
        Assert.Empty(game.Session.CurrentFrame!.Steps);
    }

    [Fact]
    public void AReactionTestIsStillTakenByAUnitWhoseCardDoesSay()
    {
        var game = Activated(GameFixtures.TwoSquadGame())
            .SetNextMoveLeavesCover(GameFixtures.Alpha, leavesCover: true).Value!;

        var steeled = game.TakeReactionTest(GameFixtures.Alpha, threatLevel: 1, new ScriptedDice(8));

        Assert.True(steeled.IsAllowed);
        Assert.True(steeled.Value!.Status(GameFixtures.Alpha).ReactionTestCleared);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACharageIsRefusedByNameAndNothingIsSpent(bool absent)
    {
        var game = Activated(Unrated(absent));

        var refused = game.DeclareCloseAssault(GameFixtures.Alpha, GameFixtures.Bravo, threatLevel: 2, new ScriptedDice(1));

        Assert.False(refused.IsAllowed);
        Assert.Contains(Named, refused.Reason!, StringComparison.Ordinal);
        Assert.Empty(game.Session.CurrentFrame!.Steps);
    }

    [Fact]
    public void AChargeIsStillDeclaredByAUnitWhoseCardDoesSay()
    {
        var game = Activated(GameFixtures.TwoSquadGame());

        var charged = game.DeclareCloseAssault(GameFixtures.Alpha, GameFixtures.Bravo, threatLevel: 2, new ScriptedDice(8));

        Assert.True(charged.IsAllowed);
        Assert.Contains("went in on", charged.Value!.Log[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StandingToReceiveAChargeIsRefusedByName(bool absent)
    {
        var game = Unrated(absent);

        var refused = game.DefenderStands(GameFixtures.Alpha, GameFixtures.Bravo, terror: false, new ScriptedDice(1));

        Assert.False(refused.IsAllowed);
        Assert.Contains(Named, refused.Reason!, StringComparison.Ordinal);

        // The defender's card is the one being read, so the defender is the one named.
        Assert.Contains("Bravo Squad", refused.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void StandingToReceiveAChargeStillHappensWhenTheCardSays()
    {
        var stood = GameFixtures.TwoSquadGame()
            .DefenderStands(GameFixtures.Alpha, GameFixtures.Bravo, terror: false, new ScriptedDice(8));

        Assert.True(stood.IsAllowed);
        Assert.Contains("stood", stood.Value!.Log[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShakingOffSuppressionIsRefusedByNameAndTheActionIsNotSpent(bool absent)
    {
        var game = Activated(Unrated(absent))
            .WithStatus(GameFixtures.Alpha, status => status with { SuppressionMarkers = 2 });

        var refused = game.RemoveSuppression(GameFixtures.Alpha, new ScriptedDice(6));

        Assert.False(refused.IsAllowed);
        Assert.Contains(Named, refused.Reason!, StringComparison.Ordinal);

        // This command spends the action before it rolls, on purpose: trying and failing is how a
        // pinned unit loses its turn. A gap in what the players typed is not a try.
        Assert.Empty(game.Session.CurrentFrame!.Steps);
    }

    [Fact]
    public void ShakingOffSuppressionStillWorksWhenTheCardSays()
    {
        var game = Activated(GameFixtures.TwoSquadGame())
            .WithStatus(GameFixtures.Alpha, status => status with { SuppressionMarkers = 2 });

        var relieved = game.RemoveSuppression(GameFixtures.Alpha, new ScriptedDice(6));

        Assert.True(relieved.IsAllowed);
        Assert.Equal(1, relieved.Value!.Status(GameFixtures.Alpha).SuppressionMarkers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARallyIsRefusedByNameWhicheverHalfOfTheScoreIsMissing(bool commanderIsTheGap)
    {
        // The score to beat is both leadership values added together, so either card can be the gap
        // and the refusal has to name the unit whose it is.
        var game = Rallying(commanderIsTheGap);

        var refused = game.Rally(GameFixtures.Alpha, Charlie, new ScriptedDice(8));

        Assert.False(refused.IsAllowed);
        Assert.Contains(Named, refused.Reason!, StringComparison.Ordinal);
        Assert.Contains(commanderIsTheGap ? "Alpha Squad" : "Charlie Squad", refused.Reason!, StringComparison.Ordinal);

        // The commander's action is spent first and stays spent on a failure, so a refusal landing
        // late would cost a platoon commander its turn for a gap in the typing.
        Assert.Empty(game.Session.CurrentFrame!.Steps);
    }

    [Fact]
    public void ARallyStillHappensWhenBothCardsSay()
    {
        var game = Rallying(commanderIsTheGap: null);

        var rallied = game.Rally(GameFixtures.Alpha, Charlie, new ScriptedDice(8));

        Assert.True(rallied.IsAllowed);
        Assert.Equal(ConfidenceLevel.Steady, rallied.Value!.Status(Charlie).Confidence);
    }

    [Fact]
    public void NothingBeyondTheLeadershipValueIsAskedFor()
    {
        // The over-strict failure this project has paid for twice. The guard reads one entry, so a
        // unit that has it takes its nerve test on a game with nothing else entered anywhere: no
        // range table, no weapons, one figure and no opponent.
        var bare = StarGruntGame.Create("Hill 43")
            .WithUnit(new UnitDefinition
            {
                Id = GameFixtures.Alpha,
                Name = "Alpha Squad",
                Side = GameFixtures.Blue,
                QualityDie = QualityDie.D8,
                LeadershipValue = 3,
                Figures = [new FigureProfile(QualityDie.D6)],
            });

        var held = bare.TakeConfidenceTest(GameFixtures.Alpha, threatLevel: 0, new ScriptedDice(6));

        Assert.True(held.IsAllowed);
    }

    private static readonly UnitId Charlie = new("charlie");

    /// <summary>
    /// The two-squad game, saved and read back with the Leadership Values taken out of the bytes.
    /// </summary>
    /// <param name="absent">
    /// True to remove the property outright, the way a blob written before it existed carries it.
    /// False to leave it present and null, which a hand-edited or third-party row may.
    /// </param>
    /// <remarks>
    /// Written through the serialiser rather than by constructing a definition without one, because
    /// the round trip is the whole defect: a C# caller who omits a value is doing so knowingly, and
    /// a JSON caller who never had the property is not.
    /// </remarks>
    private static StarGruntGame Unrated(bool absent) =>
        StarGruntGameSerialization.Restore(WithoutLeadership(GameFixtures.TwoSquadGame(), absent, 2));

    /// <summary>Alpha commanding Charlie, activated, with Charlie wavering and one card silent.</summary>
    /// <param name="commanderIsTheGap">
    /// True to take Alpha's value out, false to take Charlie's, null to leave both in.
    /// </param>
    /// <remarks>
    /// Alpha is promoted to platoon command because rallying comes from above, which is the same
    /// arrangement <c>GameMoraleTests</c> uses. Charlie carries a 3 so that exactly one of the two
    /// values can be taken out of the bytes.
    /// </remarks>
    private static StarGruntGame Rallying(bool? commanderIsTheGap)
    {
        var ready = GameFixtures.TwoSquadGame()
            .WithUnit(GameFixtures.Squad(Charlie, "Charlie Squad", GameFixtures.Blue) with { LeadershipValue = 3 })
            .WithUnitEdit(GameFixtures.Alpha, unit => unit with { Level = CommandLevel.Platoon })
            .WithStatus(Charlie, status => status with { Confidence = ConfidenceLevel.Shaken })
            .BeginTurn().Value!
            // Blue has two units now, so the choice belongs to Red, who hands it over.
            .ChooseFirstActivator(GameFixtures.Red, takeIt: false).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;

        return commanderIsTheGap is not { } gap
            ? ready
            : StarGruntGameSerialization.Restore(WithoutLeadership(ready, absent: true, gap ? 2 : 3));
    }

    /// <summary>Saves a game and takes one Leadership Value out of the bytes.</summary>
    /// <param name="game">The game to write out.</param>
    /// <param name="absent">True to drop the property, false to leave it there and null.</param>
    /// <param name="value">The value to rewrite, which is how one unit's card is picked out.</param>
    private static string WithoutLeadership(StarGruntGame game, bool absent, int value)
    {
        var saved = StarGruntGameSerialization.Save(game);
        var property = $"\"LeadershipValue\":{value}";
        var rewritten = absent
            ? saved.Replace(property + ",", string.Empty, StringComparison.Ordinal)
            : saved.Replace(property, "\"LeadershipValue\":null", StringComparison.Ordinal);

        // Reached-the-subject: a rewrite that matched nothing would leave every assertion above
        // running against an ordinary game and passing for the wrong reason.
        Assert.NotEqual(saved, rewritten);
        return rewritten;
    }

    /// <summary>Alpha's activation open, on whatever game it is handed.</summary>
    private static StarGruntGame Activated(StarGruntGame game) =>
        game.BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;
}
