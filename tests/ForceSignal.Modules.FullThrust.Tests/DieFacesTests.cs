using ForceSignal.Domain.Rules;
using ForceSignal.Modules.FullThrust.Combat;
using ForceSignal.Modules.FullThrust.Damage;

namespace ForceSignal.Modules.FullThrust.Tests;

/// <summary>
/// The game is played on the die the players said they were playing on.
/// </summary>
/// <remarks>
/// <para>
/// Every resolver used to take a die source that produced 1-6 and no way to tell it otherwise; the
/// profile's face count was applied afterwards, as a clamp on the result. That is not a die. A table
/// playing d10s could never roll above a 6, so the rows they had entered for 7 through 10 were dead
/// and every beam scored off the bottom half of their own table. A table playing d4s got the
/// opposite: three of the six faces landed on 4, so their top result came up half the time instead
/// of a quarter.
/// </para>
/// <para>
/// These tests deliberately use the <b>default</b> die source rather than a scripted one, because
/// the defect was in the default. A scripted source hides it - the script decides what comes back
/// regardless - which is why a suite full of scripted rolls stayed green over this for so long.
/// </para>
/// </remarks>
public sealed class DieFacesTests
{
    /// <summary>The invented profile, re-cut for a die of a given size. Numbers are this test's own.</summary>
    private static RulesProfile OnA(int faces) => TestRules.Invented with
    {
        DieFaces = faces,
        // One row per face so that whatever comes up is scoreable, and so no row names a face the
        // die does not have - the profile refuses that, rightly.
        BeamDamage = [.. Enumerable.Range(1, faces).Select(face => new BeamDamageEntry(face, 0, 1))],
        PointDefenseKills = [],
        Turnaround = [],
        CarrierTurnaroundRoll = false,
    };

    private static FiringSolution Volley(int dice) => new(
        new WeaponAttackProfile("Test Battery", dice, 36, [FiringArc.Fore]),
        Range: 1,
        TargetScreenRating: 0,
        AttackerWeaponDamage: 0);

    [Fact]
    public void ATablePlayingTensCanRollAboveASix()
    {
        var rules = OnA(10);
        var beam = new FullThrustLightFiringRules();

        // Four hundred dice off the real source. Under a six-sided source the highest face possible
        // is a 6, so this is not a matter of luck - it is a matter of whether the die has ten sides.
        var faces = Enumerable.Range(0, 100)
            .SelectMany(_ => beam.Resolve(Volley(4), rules).DiceRolls)
            .ToArray();

        Assert.All(faces, face => Assert.InRange(face, 1, 10));
        Assert.Contains(faces, face => face > 6);
        Assert.Contains(faces, face => face == 10);
    }

    [Fact]
    public void ATablePlayingFoursDoesNotGetItsTopFaceHalfTheTime()
    {
        var rules = OnA(4);
        var beam = new FullThrustLightFiringRules();

        var faces = Enumerable.Range(0, 500)
            .SelectMany(_ => beam.Resolve(Volley(4), rules).DiceRolls)
            .ToArray();

        Assert.All(faces, face => Assert.InRange(face, 1, 4));

        // Clamping a six-sided roll into four faces folds 4, 5 and 6 onto the 4, so the top face
        // landed half the time instead of a quarter. Two thousand dice put the true share within a
        // couple of points of 25%, so a third is a boundary no fair d4 crosses and no clamped d6
        // stays under.
        var topFaceShare = faces.Count(face => face == 4) / (double)faces.Length;
        Assert.True(topFaceShare < 0.34, $"Top face came up {topFaceShare:P1} of the time; a fair d4 gives about 25%.");
        Assert.True(topFaceShare > 0.16, $"Top face came up {topFaceShare:P1} of the time; a fair d4 gives about 25%.");
    }

    [Fact]
    public void EveryResolverIsHandedTheProfilesFaceCountRatherThanAssumingSix()
    {
        // One recording source shared by the resolvers that take one, so this fails if any of them
        // is left behind on a future pass - which is exactly how this defect spread in the first
        // place: one lesson learned in one file and not carried to its siblings.
        var asked = new List<int>();
        int Record(int faces)
        {
            asked.Add(faces);
            return faces;
        }

        var rules = OnA(12);
        new FullThrustLightFiringRules(Record).Resolve(Volley(2), rules);
        new FullThrustDamageControlRules(Record).Resolve(new RepairJob(ShipSystemKind.Drive, null, 1), rules);

        Assert.NotEmpty(asked);
        Assert.All(asked, faces => Assert.Equal(12, faces));
    }
}
