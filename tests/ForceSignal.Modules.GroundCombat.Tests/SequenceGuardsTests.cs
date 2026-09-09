using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.GroundCombat.Tests;

/// <summary>
/// The two rules both games state in the same words. One implementation, so one set of tests.
/// </summary>
public class SequenceGuardsTests
{
    [Theory]
    [InlineData(3, 5, true)]
    [InlineData(5, 3, false)]
    [InlineData(4, 4, false)]
    [InlineData(1, 2, true)]
    [InlineData(0, 1, true)]
    public void PassingNeedsStrictlyFewerUnactivatedUnits(int mine, int theirs, bool allowed)
    {
        var session = SequenceFixtures.Game(mine, theirs);

        Assert.Equal(allowed, SequenceGuards.MayPass(session, SequenceFixtures.Blue).IsAllowed);
    }

    [Fact]
    public void LevelUnactivatedCountsRefuseWithAReasonRatherThanThrowing()
    {
        var session = SequenceFixtures.Game(4, 4);

        var check = SequenceGuards.MayPass(session, SequenceFixtures.Blue);

        Assert.False(check.IsAllowed);
        Assert.NotNull(check.Reason);
    }

    [Fact]
    public void PassingReadsUnactivatedUnitsRatherThanUnitsOnTable()
    {
        // Equal forces, but one side has already spent two markers. The larger pool of unactivated
        // units is what the rule looks at, not the larger force.
        var session = SequenceFixtures.Game(4, 4);
        var spent = SequenceFixtures.Units("blue", 2);
        var blue = session.Side(SequenceFixtures.Blue);
        var withSpent = session.Sides.SetItem(0, blue with { Activated = [.. spent] });

        var after = session with { Sides = withSpent };

        Assert.True(SequenceGuards.MayPass(after, SequenceFixtures.Blue).IsAllowed);
        Assert.False(SequenceGuards.MayPass(after, SequenceFixtures.Red).IsAllowed);
    }

    [Fact]
    public void TheSmallerForceChoosesWhoActivatesFirst()
    {
        var session = SequenceFixtures.Game(3, 7);

        Assert.Equal(SequenceFixtures.Blue, SequenceGuards.FirstActivationChooser(session));
    }

    [Fact]
    public void LevelForcesLeaveTheChoiceToTheCaller()
    {
        var session = SequenceFixtures.Game(5, 5);

        Assert.Null(SequenceGuards.FirstActivationChooser(session));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void AGameThatIsNotTwoSidedNamesNobodyRatherThanFaulting(int sideCount)
    {
        // A session with any other number of sides has no "side with fewer units" for the rule to
        // name. Reading the two sides directly made the first turn of such a game a server fault -
        // and a save file edited by hand is all it takes to produce one.
        var session = SequenceFixtures.Game(3, 7) with
        {
            Sides =
            [
                .. Enumerable.Range(0, sideCount)
                    .Select(index => SideState.Of(new SideId($"side-{index}"), SequenceFixtures.Units($"side-{index}", index + 1))),
            ],
        };

        Assert.Null(SequenceGuards.FirstActivationChooser(session));
    }

    [Fact]
    public void ATurnCanBegunOnAGameThatIsNotTwoSidedWithoutFaulting()
    {
        // BeginTurn walks straight into the guard through CanChooseFirstActivator, which is the
        // path a lopsided save takes on the very first thing anyone asks of it.
        var session = SequenceFixtures.Game(3, 7) with { Sides = [] };

        var begun = GroundCombatSequence.BeginTurn(session);

        Assert.True(GroundCombatSequence.CanChooseFirstActivator(begun, SequenceFixtures.Blue).IsAllowed);
    }

    [Fact]
    public void TheChoiceIsCountedFreshEachTurnSoLossesCanMoveIt()
    {
        var session = SequenceFixtures.Game(6, 4);
        Assert.Equal(SequenceFixtures.Red, SequenceGuards.FirstActivationChooser(session));

        // Three of Red's units are lost. The choice moves across without anything being reset.
        var red = session.Side(SequenceFixtures.Red);
        var thinned = session with
        {
            Sides = session.Sides.SetItem(1, red with { OnTable = [new UnitId("red-1")] }),
        };

        Assert.Equal(SequenceFixtures.Red, SequenceGuards.FirstActivationChooser(thinned));
        Assert.Equal(1, thinned.Side(SequenceFixtures.Red).UnitsOnTable);
    }

    [Fact]
    public void TheEntitledSideIsTheOnlyOneAllowedToChoose()
    {
        var session = GroundCombatSequence.BeginTurn(SequenceFixtures.Game(3, 7));

        Assert.True(GroundCombatSequence.CanChooseFirstActivator(session, SequenceFixtures.Blue).IsAllowed);
        Assert.False(GroundCombatSequence.CanChooseFirstActivator(session, SequenceFixtures.Red).IsAllowed);
    }

    [Fact]
    public void GivingTheFirstActivationAwayHandsItToTheOpponent()
    {
        var session = GroundCombatSequence.BeginTurn(SequenceFixtures.Game(3, 7));

        var settled = GroundCombatSequence.ChooseFirstActivator(session, SequenceFixtures.Blue, takeIt: false);

        Assert.Equal(SequenceFixtures.Red, settled.FirstActivator);
        Assert.Equal(SequenceFixtures.Red, settled.ActiveSide);
        Assert.Equal(TurnPhase.Activating, settled.Phase);
    }
}
