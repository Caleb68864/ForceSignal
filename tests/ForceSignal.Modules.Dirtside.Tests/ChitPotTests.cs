using ForceSignal.Modules.Dirtside.Chits;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// The pot's one subtlety: a draw is without replacement inside a single damage resolution, and the
/// pot is whole again before the next one. Get that wrong in either direction and every damage
/// probability in the game moves.
/// </summary>
public sealed class ChitPotTests
{
    private static readonly DamageChit Boom = DamageChit.Of(ChitSpecial.Boom);

    /// <summary>One scarce special among a lot of filler, so a duplicate is impossible to miss.</summary>
    private static ChitPotComposition OneBoomInTen() => ChitPotComposition.Of(
        [Boom, .. Enumerable.Repeat(DamageChit.Numerical(ChitColour.Red, 1), 9)]);

    [Fact]
    public void ADrawCannotContainMoreOfAScarceChitThanThePotHolds()
    {
        // The property that says "without replacement" out loud. With replacement, a five-chit draw
        // from a pot holding a single Boom would double it up now and again - roughly one draw in
        // eight here - so a few thousand draws would find it comfortably.
        var random = new Random(20250808);
        var pot = new ChitPot(OneBoomInTen(), random.Next);

        for (var shot = 0; shot < 5000; shot++)
        {
            var drawn = pot.Draw(5);
            Assert.True(drawn.Count(c => c == Boom) <= 1);
        }
    }

    [Fact]
    public void DrawingTheWholePotYieldsExactlyWhatIsInIt()
    {
        var pot = new ChitPot(OneBoomInTen(), new Random(7).Next);

        var drawn = pot.Draw(10);

        Assert.Equal(10, drawn.Count);
        Assert.Equal(1, drawn.Count(c => c == Boom));
        Assert.Equal(9, drawn.Count(c => c == DamageChit.Numerical(ChitColour.Red, 1)));
    }

    [Fact]
    public void ThePotIsWholeAgainBeforeTheNextShot()
    {
        // A twin mount scoring two hits makes two draws of its own size with a restore between, not
        // one draw of twice the size. If the restore were missing, the second full draw would come
        // back empty.
        var pot = new ChitPot(OneBoomInTen(), new Random(11).Next);

        var first = pot.Draw(10);
        var second = pot.Draw(10);

        Assert.Equal(first.Order(Comparer<DamageChit>.Create(Compare)), second.Order(Comparer<DamageChit>.Create(Compare)));
    }

    [Fact]
    public void TwoDrawsOfThreeAreNotOneDrawOfSix()
    {
        // The distributions genuinely differ, and this is the cheapest way to show it: with one Boom
        // in ten, a single draw of six finds it 60% of the time, while two independent draws of
        // three find it in at least one draw about 51% of the time. Same six chits, different game.
        var composition = OneBoomInTen();
        var oneBigDraw = 0;
        var twoSmallDraws = 0;
        var random = new Random(4242);
        var pot = new ChitPot(composition, random.Next);

        for (var trial = 0; trial < 20000; trial++)
        {
            if (pot.Draw(6).Contains(Boom))
            {
                oneBigDraw++;
            }

            if (pot.Draw(3).Contains(Boom) || pot.Draw(3).Contains(Boom))
            {
                twoSmallDraws++;
            }
        }

        Assert.True(oneBigDraw > twoSmallDraws + 500,
            $"one draw of six found {oneBigDraw}, two draws of three found {twoSmallDraws}");
    }

    [Fact]
    public void AScriptedSourceMakesTheDrawReplayable()
    {
        // Always taking index zero draws the composition in the order it was written, which is what
        // lets a test or a replay pin an exact hand.
        var composition = ChitPotComposition.Of([
            DamageChit.Numerical(ChitColour.Red, 3),
            DamageChit.Numerical(ChitColour.Green, 2),
            Boom,
        ]);

        var drawn = new ChitPot(composition, _ => 0).Draw(3);

        Assert.Equal(composition.Chits, drawn);
    }

    [Fact]
    public void AnIndexSourceThatOverstepsIsCorrectedRatherThanThrowing()
    {
        // The same bargain the die source strikes: a source handing back nonsense is a bug in the
        // source, not something the damage rules should have to reason about.
        var composition = ChitPotComposition.Of([
            DamageChit.Numerical(ChitColour.Red, 3),
            DamageChit.Numerical(ChitColour.Green, 2),
        ]);

        var drawn = new ChitPot(composition, _ => 99).Draw(2);

        Assert.Equal(2, drawn.Count);
        Assert.Contains(DamageChit.Numerical(ChitColour.Red, 3), drawn);
        Assert.Contains(DamageChit.Numerical(ChitColour.Green, 2), drawn);
    }

    [Fact]
    public void AHandBiggerThanThePotIsRefusedRatherThanDrawingAChitTwice()
    {
        var pot = new ChitPot(OneBoomInTen());

        Assert.Throws<ArgumentOutOfRangeException>(() => pot.Draw(11));
        Assert.Throws<ArgumentOutOfRangeException>(() => pot.Draw(-1));
    }

    [Fact]
    public void DrawingNothingIsAllowedAndYieldsNothing() =>
        Assert.Empty(new ChitPot(OneBoomInTen()).Draw(0));

    private static int Compare(DamageChit left, DamageChit right) =>
        StringComparer.Ordinal.Compare(left.ToString(), right.ToString());
}
