using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Application.Tests;

/// <summary>
/// A room code is read aloud and typed back. Playtest M7: "ZENITH TEMPO LUMEN FALCON" - the right
/// code, typed with spaces - was told there was no such room.
/// </summary>
public sealed class JoinCodeTypingTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData(" - ")]
    [InlineData(".")]
    [InlineData("_")]
    public void ACodeTypedWithSpacesOrOtherSeparatorsStillFindsTheRoom(string separator)
    {
        var service = new InMemoryMatchService(_ => 4);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Typed"));
        var typed = string.Join(separator, owner.JoinCode.Split('-')).ToLowerInvariant();

        Assert.Equal(owner.MatchId, service.FindMatchByCode(typed).MatchId);
        Assert.Equal(owner.MatchId, service.JoinMatch(new JoinMatchRequest(typed, "Red")).MatchId);
    }

    [Fact]
    public void ACodeWithAWordWrongIsStillNotFound()
    {
        var service = new InMemoryMatchService(_ => 4);
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Typed"));
        var wrong = string.Join(' ', owner.JoinCode.Split('-').Take(3).Append("NOTAWORD"));

        Assert.Throws<NotFoundException>(() => service.JoinMatch(new JoinMatchRequest(wrong, "Red")));
    }
}

/// <summary>Two players with one name made a log nobody could follow (playtest m1).</summary>
public sealed class DuplicateDisplayNameTests
{
    [Fact]
    public void ASecondPlayerWithTheSameNameIsNumbered()
    {
        var service = new InMemoryMatchService(_ => 4);
        var owner = service.CreateMatch(new CreateMatchRequest("Cobalt", "Names"));

        service.JoinMatch(new JoinMatchRequest(owner.JoinCode, "cobalt"));

        var names = service.GetSnapshot(owner.MatchId).Participants.Select(p => p.DisplayName).ToArray();
        Assert.Equal(["Cobalt", "cobalt 2"], names);
    }
}
