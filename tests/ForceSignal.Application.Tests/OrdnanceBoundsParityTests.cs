using System.Text.Json;
using ForceSignal.Application.Matches;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The service's ordnance clamps, held against the ceilings the client normalizes to.
/// </summary>
/// <remarks>
/// <para>
/// Four numbers - speed, endurance, attack dice and maximum range - were each written twice: once as
/// a <c>Math.Clamp</c> here and on the restore path, and once in <c>normalize.ts</c>. All four had
/// drifted, all in the same direction: the client allowed 120 / 48 / 48 / 240 against this service's
/// 72 / 24 / 24 / 120. So a restored snapshot drew a 48-dice salvo on screen that the server halved
/// to 24 the moment anything touched it, and nothing told the table.
/// </para>
/// <para>
/// <c>src/ForceSignal.Web/src/lib/ordnanceBounds.json</c> holds them once.
/// <c>ordnanceBounds.test.ts</c> reads them back through <c>normalizeMatchSnapshot</c> and this reads
/// them off the service through <see cref="InMemoryMatchService.CreateOrdnanceMarker"/> - the front
/// door a caller uses, so the guard cannot pass over a clamp the create path does not apply.
/// </para>
/// </remarks>
public sealed class OrdnanceBoundsParityTests
{
    [Fact]
    public void TheServiceClampsAMarkerToExactlyWhatTheClientDoes()
    {
        var bounds = Bounds("bounds");

        // Reached-the-subject: the fixture really loaded and really still names all four fields. A
        // fixture reduced to nothing would leave every lookup below throwing rather than passing,
        // which is the point of reading it into a dictionary first.
        Assert.Equal(4, bounds.Count);

        var marker = MarkerAsking(9999);

        // And the create really succeeded with the marker asked for; a refusal would leave the
        // assertions below reading a marker from some other request.
        Assert.Equal("Salvo One", marker.Name);

        Assert.Equal(bounds["speed"], marker.Speed);
        Assert.Equal(bounds["enduranceRemaining"], marker.EnduranceRemaining);
        Assert.Equal(bounds["attackDice"], marker.AttackDice);
        Assert.Equal(bounds["maxRange"], marker.MaxRange);
    }

    [Fact]
    public void TheUpdatePathClampsToTheSameCeilingAsTheCreatePath()
    {
        // The dullest way past a guard written against the create path alone: change one clamp and
        // leave the other. There are three copies of these four numbers on this side - create,
        // update and restore - and a marker edited on the table goes through the second of them.
        var bounds = Bounds("bounds");

        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Bounds", Rules: TestRules.Invented));
        var created = service.CreateOrdnanceMarker(owner.MatchId, new CreateOrdnanceMarkerRequest(
            owner.ParticipantToken, "Salvo One", "Salvo", null, null, 20, 24, 1, 4, 4, 4, 4))
            .OrdnanceMarkers.Single();

        var marker = service.UpdateOrdnanceMarker(created.Id, new UpdateOrdnanceMarkerRequest(
            owner.ParticipantToken,
            "Salvo One",
            "Salvo",
            TargetShipId: null,
            PositionX: 20,
            PositionY: 24,
            Course: 1,
            Speed: 9999,
            EnduranceRemaining: 9999,
            AttackDice: 9999,
            MaxRange: 9999)).OrdnanceMarkers.Single();

        Assert.Equal(bounds["speed"], marker.Speed);
        Assert.Equal(bounds["enduranceRemaining"], marker.EnduranceRemaining);
        Assert.Equal(bounds["attackDice"], marker.AttackDice);
        Assert.Equal(bounds["maxRange"], marker.MaxRange);
    }

    [Fact]
    public void TheServiceFloorsAMarkerToExactlyWhatTheClientDoes()
    {
        var floors = Bounds("floors");
        var marker = MarkerAsking(-9999);

        Assert.Equal("Salvo One", marker.Name);

        // Zero rather than one, in all four. An unentered number stays unentered here exactly as it
        // does on the ship form; the salvo reach in particular used to answer a refusal citing a 24
        // nobody had entered.
        Assert.Equal(floors["speed"], marker.Speed);
        Assert.Equal(floors["enduranceRemaining"], marker.EnduranceRemaining);
        Assert.Equal(floors["attackDice"], marker.AttackDice);
        Assert.Equal(floors["maxRange"], marker.MaxRange);
    }

    [Fact]
    public void AMarkerInsideTheBoundsIsPassedThroughUntouched()
    {
        // The control that must be accepted. A service that clamped everything to the ceiling, or
        // refused anything it did not like, would satisfy both tests above and be the over-strict
        // fix this repository has already paid for twice.
        var marker = MarkerAsking(12);

        Assert.Equal(12, marker.Speed);
        Assert.Equal(12, marker.EnduranceRemaining);
        Assert.Equal(12, marker.AttackDice);
        Assert.Equal(12, marker.MaxRange);
    }

    private static OrdnanceMarkerDto MarkerAsking(int value)
    {
        var service = new InMemoryMatchService();
        var owner = service.CreateMatch(new CreateMatchRequest("Blue", "Bounds", Rules: TestRules.Invented));

        return service.CreateOrdnanceMarker(owner.MatchId, new CreateOrdnanceMarkerRequest(
            owner.ParticipantToken,
            "Salvo One",
            "Salvo",
            SourceShipId: null,
            TargetShipId: null,
            PositionX: 20,
            PositionY: 24,
            Course: 1,
            Speed: value,
            EnduranceRemaining: value,
            AttackDice: value,
            MaxRange: value)).OrdnanceMarkers.Single();
    }

    private static Dictionary<string, int> Bounds(string section)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "ForceSignal.Web", "src", "lib", "ordnanceBounds.json");
        Assert.True(File.Exists(path), $"the shared ordnance bounds fixture is not at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty(section).EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetInt32(), StringComparer.Ordinal);
    }

    /// <summary>The repository root, found rather than assumed, so this guard cannot pass by missing it.</summary>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ForceSignal.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"no ForceSignal.slnx above {AppContext.BaseDirectory}, so this guard cannot reach the fixture it is about");
    }
}
