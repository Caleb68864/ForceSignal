using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// The close-assault exchange. Two properties carry it: both sides fight at once with casualties
/// marked rather than removed, and chits are never pooled. Everything else about an assault is a
/// confidence test and is deliberately not here.
/// </summary>
public sealed class CloseAssaultTests
{
    private static readonly ChitValidity RedOnly = new(ChitColours.Red);
    private static readonly ChitValidity RedAndYellow = new(ChitColours.Red | ChitColours.Yellow);

    private static DamageChit Red(int value) => DamageChit.Numerical(ChitColour.Red, value);

    private static DamageChit Yellow(int value) => DamageChit.Numerical(ChitColour.Yellow, value);

    private static InfantryStand Stand(string id) =>
        new(id, FirefightChits: 2, AssaultChits: 3, KillThreshold: 4);

    private static AssaultSide Side(string id, int stands, ChitValidity validity, ChitValidity? handToHand = null) =>
        new(id, [.. Enumerable.Range(1, stands).Select(i => Stand($"{id}{i}"))], validity, handToHand);

    [Fact]
    public void EveryStandOnBothSidesFightsWithNoEffectivenessCheck()
    {
        // At this range nobody is deciding whether to join in. Two stands a side is four draws.
        var pot = new CyclingPot(Red(0));

        var round = CloseAssault.ResolveRound(
            Side("att", 2, RedOnly), Side("def", 2, RedOnly), pot);

        Assert.Equal(2, round.Attacker.Draws.Count);
        Assert.Equal(2, round.Defender.Draws.Count);
        Assert.Equal(4, pot.Draws);
    }

    [Fact]
    public void AStandKilledThisRoundStillFiresThisRound()
    {
        // Casualties are marked, not removed. Both defenders die to the attacker's draws, and both
        // still get their own draws in - they fired as they died. Resolving one side first with the
        // dead already off the table would hand whoever was resolved first a free advantage.
        var pot = new CyclingPot(Red(2));

        var round = CloseAssault.ResolveRound(
            Side("att", 2, RedOnly), Side("def", 2, RedOnly), pot);

        Assert.Equal(2, round.Defender.Losses);
        Assert.Equal(2, round.Defender.Draws.Count);
        Assert.Equal(2, round.Attacker.Losses);
        Assert.Equal(2, round.Attacker.Draws.Count);
    }

    [Fact]
    public void ChitsAreNeverPooledHereEither()
    {
        // Three stands drawing three chits of 1 each is three totals of 3 against a kill total of 4:
        // nobody dies. Pooled it would be 9 and the position would fall.
        var pot = new CyclingPot(Red(1));

        var round = CloseAssault.ResolveRound(
            Side("att", 3, RedOnly), Side("def", 1, RedOnly), pot);

        Assert.All(round.Attacker.Draws, d => Assert.Equal(3, d.Tally.ValidTotal));
        Assert.Equal(0, round.Defender.Losses);
    }

    [Fact]
    public void AssaultDrawsUseTheAssaultCountAndNotTheFirefightOne()
    {
        // The two categorisations are not the same rule reused, so they are not the same field. A
        // stand drawing two in a firefight can draw three here.
        var pot = new CountingPot(Red(0));

        CloseAssault.ResolveRound(Side("att", 1, RedOnly), Side("def", 1, RedOnly), pot);

        Assert.All(pot.Requests, r => Assert.Equal(3, r));
    }

    [Fact]
    public void TheSecondRoundStopsCountingTheDefendersCover()
    {
        // Hand to hand now: what the defender was hiding behind has stopped mattering. The attacker
        // draws the same yellow chits and they only count from round two.
        var attacker = Side("att", 1, RedOnly, handToHand: RedAndYellow);
        var defender = Side("def", 1, RedOnly);

        var first = CloseAssault.ResolveRound(attacker, defender, new CyclingPot(Yellow(2)), round: 1);
        var second = CloseAssault.ResolveRound(attacker, defender, new CyclingPot(Yellow(2)), round: 2);

        Assert.Equal(0, first.Attacker.Draws[0].Tally.ValidTotal);
        Assert.Equal(6, second.Attacker.Draws[0].Tally.ValidTotal);
        Assert.True(first.DefenderCoverStillCounted);
        Assert.False(second.DefenderCoverStillCounted);
    }

    [Fact]
    public void ASideWithNothingToLoseKeepsItsOwnRowInTheSecondRound()
    {
        // The defender's enemy had no cover to lose, so leaving the hand-to-hand row null must leave
        // that side's draws exactly as they were.
        var defender = Side("def", 1, RedOnly);

        Assert.Equal(RedOnly, defender.ValidityForRound(1));
        Assert.Equal(RedOnly, defender.ValidityForRound(2));
    }

    [Theory]
    // The one number the confidence test that follows the round needs. It is computed here and the
    // test itself is left to the morale layer.
    [InlineData(4, 1, false)]
    [InlineData(4, 2, true)]
    [InlineData(3, 2, true)]
    public void TheHeavyCasualtyShareIsWorkedOutButNotActedOn(int stands, int losses, bool expected)
    {
        var result = new AssaultSideResult("s", Array.Empty<StandFireResult>(), stands, losses);

        Assert.Equal(expected, result.LostHalfOrMore);
        Assert.Equal((double)losses / stands, result.CasualtyFraction);
    }

    [Fact]
    public void SpecialsDoNothingInAnInfantryAssault()
    {
        var pot = new CyclingPot(DamageChit.Of(ChitSpecial.Boom));

        var round = CloseAssault.ResolveRound(
            Side("att", 1, new ChitValidity(ChitColours.All)), Side("def", 1, RedOnly), pot);

        Assert.False(round.Attacker.Draws[0].CatastrophicKill);
        Assert.Equal(0, round.Defender.Losses);
    }

    [Fact]
    public void AMixedSideIsRefusedRatherThanSilentlyTakingTheFirstStandsToughness()
    {
        // The alternative is a wrong answer nobody would ever notice.
        var mixed = new AssaultSide("def", [Stand("d1"), new InfantryStand("d2", 2, 3, KillThreshold: 5)], RedOnly);

        Assert.Throws<ArgumentException>(() =>
            CloseAssault.ResolveRound(Side("att", 1, RedOnly), mixed, new CyclingPot(Red(0))));
    }

    [Fact]
    public void ASideWithNoStandsLeftDrawsNothingAndTakesNothing()
    {
        var round = CloseAssault.ResolveRound(
            Side("att", 1, RedOnly), new AssaultSide("def", [], RedOnly), new CyclingPot(Red(3)));

        Assert.Empty(round.Attacker.Draws);
        Assert.Empty(round.Defender.Draws);
        Assert.Equal(0, round.Defender.Losses);
        Assert.Equal(0, round.Attacker.Losses);
    }

    [Fact]
    public void ANonsenseRoundIsRefusedRatherThanGuessedAt()
    {
        var pot = new CyclingPot(Red(0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CloseAssault.ResolveRound(Side("a", 1, RedOnly), Side("d", 1, RedOnly), pot, round: 0));
        Assert.Throws<ArgumentNullException>(() =>
            CloseAssault.ResolveRound(null!, Side("d", 1, RedOnly), pot));
        Assert.Throws<ArgumentNullException>(() =>
            CloseAssault.ResolveRound(Side("a", 1, RedOnly), Side("d", 1, RedOnly), null!));
    }

    /// <summary>A pot that hands out the same chit forever, so a draw's size is all that varies.</summary>
    private sealed class CyclingPot(DamageChit chit) : IChitPot
    {
        public int Draws { get; private set; }

        public IReadOnlyList<DamageChit> Draw(int count)
        {
            Draws++;
            return [.. Enumerable.Repeat(chit, count)];
        }
    }

    private sealed class CountingPot(DamageChit chit) : IChitPot
    {
        private readonly List<int> _requests = [];

        public IReadOnlyList<int> Requests => _requests;

        public IReadOnlyList<DamageChit> Draw(int count)
        {
            _requests.Add(count);
            return [.. Enumerable.Repeat(chit, count)];
        }
    }
}
