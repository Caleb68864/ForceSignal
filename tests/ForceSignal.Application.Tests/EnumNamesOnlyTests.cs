using ForceSignal.Application.Ground;
using ForceSignal.Contracts.Ground;
using ForceSignal.TestSupport;
using static ForceSignal.Application.Tests.DirtsideGameServiceTests;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Every Dirtside word a request carries for an engine enum is read by name only.
/// </summary>
/// <remarks>
/// These were read with <c>Enum.TryParse</c>, which takes a number ("7", "-1") or a comma list it
/// ORs together as readily as a name. Some checked <c>IsDefined</c> afterwards, which still lets
/// "1" through as whatever happens to be 1; the rest let an undefined value onto the card. Each
/// case below was accepted before. Unit kind and the element's fire control were fixed first and
/// are covered in <see cref="DirtsideGameServiceTests"/>.
/// </remarks>
public sealed class EnumNamesOnlyTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("Red, Green")]
    public void AChitColourGivenAsANumberOrAListIsRefused(string colour)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => CreateWithPot(
            new DirtsideChitPotDto([new DirtsideNumericalChitsDto(colour, 2, 10)], [])));

        Assert.Contains("is not a chit colour", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASpecialChitGivenAsANumberIsRefused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => CreateWithPot(
            new DirtsideChitPotDto([new DirtsideNumericalChitsDto("Red", 2, 10)], [new DirtsideSpecialChitsDto("1", 2)])));

        Assert.Contains("is not a special chit", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AChitPotIsStillReadByNameInAnyCase()
    {
        var created = CreateWithPot(new DirtsideChitPotDto(
            [new DirtsideNumericalChitsDto("yellow", 2, 10)], [new DirtsideSpecialChitsDto("boom", 1)]));

        Assert.Contains(created.Snapshot.ChitPot!.Numericals, row => row is { Colour: "Yellow", Count: 10 });
        Assert.Contains(created.Snapshot.ChitPot!.Specials, row => row is { Special: "Boom", Count: 1 });
    }

    [Theory]
    [InlineData("7")]
    [InlineData("8")]
    [InlineData("0")]
    public void AColourSetGivenAsANumberIsRefused(string colours)
    {
        // "7" was every colour and "8" a colour no chit is printed in.
        var refusal = Assert.Throws<InvalidOperationException>(() => AddWithValidity(new DirtsideValidityDto(colours, "FaceValue")));

        Assert.Contains("is not a set of chit colours", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("7")]
    [InlineData("Doubled, Halved")]
    public void AValueScaleGivenAsANumberOrAListIsRefused(string scale)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => AddWithValidity(new DirtsideValidityDto("All", scale)));

        Assert.Contains("is not a chit value scale", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("Red, Yellow")]
    [InlineData("none")]
    public void AColourSetIsStillReadByNameInAnyCaseAndAsAPairOfNames(string colours)
    {
        // A pair is what most cards print; the screens offer only single words, but a pair of names
        // has always been read and is still the way to say one.
        var snapshot = AddWithValidity(new DirtsideValidityDto(colours, "facevalue"));

        Assert.Single(snapshot.Units);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("-1")]
    [InlineData("1")]
    public void AMeasuredBandGivenAsANumberIsRefused(string band)
    {
        // "5" was a band that does not exist; "-1" meant Long and "1" Close.
        var service = new DirtsideGameService();
        var game = Activated(service);

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", band)));

        Assert.Contains("is not a range band", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("9")]
    [InlineData("SoftCover, Evading")]
    public void ATargetPostureGivenAsANumberOrAListIsRefused(string posture)
    {
        var service = new DirtsideGameService();
        var game = Activated(service);

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "Close", TargetPosture: posture)));

        Assert.Contains("is not a defensive posture", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABandAndPostureAreStillReadByNameInAnyCase()
    {
        var service = new DirtsideGameService();
        var game = Activated(service);

        var fired = service.Fire(game, new DirtsideFireRequest("alpha-1", "Main Gun", "bravo", "bravo-1", "close", TargetPosture: "softcover"));

        Assert.NotEmpty(fired.Log);
    }

    [Fact]
    public void ACommandMarkerDieGivenAsAFaceCountIsRefused()
    {
        // "8" was D8, because D8's value is its face count. Dirtside dice are named on the wire.
        var service = new DirtsideGameService();
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge"));

        var refusal = Assert.Throws<InvalidOperationException>(() => service.AddPlatoon(created.GameId,
            Platoon("alpha", "Alpha Troop", "blue") with { QualityDie = "8" }));

        Assert.Contains("is not a quality die", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("Basic, Enhanced")]
    public void AProfileFireControlRowGivenAsANumberOrAListIsRefused(string key)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => CreateWithProfile(
            DirtsideTestProfile.Invented with { FireControl = [.. DirtsideTestProfile.Invented.FireControl!, new DirtsideDieRowDto(key, "D6")] }));

        Assert.Contains("is not a fire control level", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("4")]
    [InlineData("HullDown, Evading")]
    public void AProfilePostureRowGivenAsANumberOrAListIsRefused(string key)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => CreateWithProfile(
            DirtsideTestProfile.Invented with { Posture = [.. DirtsideTestProfile.Invented.Posture!, new DirtsideDieRowDto(key, "D6")] }));

        Assert.Contains("is not a posture a die table has a row for", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileDieGivenAsAFaceCountIsRefused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => CreateWithProfile(
            DirtsideTestProfile.Invented with { SystemsDownRecoveryDie = "8" }));

        Assert.Contains("is not a quality die", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileIsStillReadByNameInAnyCaseAndReportedBackByName()
    {
        var created = CreateWithProfile(DirtsideTestProfile.Invented with
        {
            FireControl = [new DirtsideDieRowDto("basic", "d4"), new DirtsideDieRowDto("ENHANCED", "D8")],
            Posture = [new DirtsideDieRowDto("hulldown", "d10")],
        });

        var dto = created.Snapshot.Profile!;

        Assert.Contains(dto.FireControl!, row => row is { Key: "Enhanced", Die: "D8" });
        Assert.Contains(dto.Posture!, row => row is { Key: "HullDown", Die: "D10" });
    }

    private static DirtsideGameCreatedResponse CreateWithPot(DirtsideChitPotDto pot) =>
        new DirtsideGameService().CreateGame(DirtsideTestProfile.CreateGame("Ridge") with { ChitPot = pot });

    private static DirtsideGameCreatedResponse CreateWithProfile(DirtsideRulesProfileDto profile) =>
        new DirtsideGameService().CreateGame(new CreateDirtsideGameRequest("Ridge", null, profile));

    private static DirtsideSnapshotDto AddWithValidity(DirtsideValidityDto row)
    {
        var service = new DirtsideGameService();
        var created = service.CreateGame(DirtsideTestProfile.CreateGame("Ridge"));
        var platoon = Platoon("alpha", "Alpha Troop", "blue");
        var element = platoon.Elements[0];

        return service.AddPlatoon(created.GameId, platoon with
        {
            Elements = [element with { Weapons = [MainGun with { Close = row }] }],
        });
    }
}
