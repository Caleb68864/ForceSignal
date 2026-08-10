using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Two squads facing each other, which is the smallest thing that is actually a game.
/// </summary>
/// <remarks>
/// Separate from <see cref="SequenceFixtures"/> on purpose. Those build sessions for the activation
/// tests and are the guard that the sequence layer still behaves; these build whole games. Mixing
/// them would put the guard and the thing being guarded in one file.
/// </remarks>
internal static class GameFixtures
{
    public static readonly SideId Blue = new("blue");
    public static readonly SideId Red = new("red");
    public static readonly UnitId Alpha = new("alpha");
    public static readonly UnitId Bravo = new("bravo");

    /// <summary>A rifle squad of eight, all in the same armour.</summary>
    public static UnitDefinition Squad(UnitId id, string name, SideId side, int figures = 8) => new()
    {
        Id = id,
        Name = name,
        Side = side,
        Level = CommandLevel.Squad,
        QualityDie = QualityDie.D8,
        LeadershipDie = QualityDie.D8,
        Figures = [.. Enumerable.Repeat(new FigureProfile(QualityDie.D6), figures)],
        Weapons =
        [
            new WeaponProfile { Name = "Rifles", ImpactDie = QualityDie.D8 },
            new WeaponProfile { Name = "Squad Support", ImpactDie = QualityDie.D10, IsSupport = true },
        ],
    };

    /// <summary>One squad a side, nothing having happened yet.</summary>
    public static StarGruntGame TwoSquadGame() =>
        StarGruntGame.Create("Hill 43")
            .WithUnit(Squad(Alpha, "Alpha Squad", Blue))
            .WithUnit(Squad(Bravo, "Bravo Squad", Red));

    /// <summary>Alpha activated and ready to shoot at Bravo.</summary>
    /// <returns>The game mid-activation.</returns>
    public static StarGruntGame Firefight() =>
        TwoSquadGame()
            .BeginTurn().Value!
            .ChooseFirstActivator(Blue, takeIt: true).Value!
            .BeginActivation(Blue, Alpha).Value!;
}
