using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>A platoon commander, two squads, and an enemy with something to shoot back with.</summary>
internal static class SequenceFixtures
{
    public static readonly SideId Blue = new("blue");
    public static readonly SideId Red = new("red");

    public static readonly UnitId Commander = new("blue-command");
    public static readonly UnitId SquadA = new("blue-a");
    public static readonly UnitId SquadB = new("blue-b");
    public static readonly UnitId Watcher = new("red-1");
    public static readonly UnitId Reserve = new("red-2");

    public static GroundCombatSession Forces() =>
        GroundCombatSession.Start(
            SideState.Of(Blue, [Commander, SquadA, SquadB]),
            SideState.Of(Red, [Watcher, Reserve]));

    /// <summary>A board with the commander a level up from his squads and everybody in good order.</summary>
    public static StarGruntBoard Board() =>
        new StarGruntBoard()
            .Set(Commander, new StarGruntUnitState { Level = CommandLevel.Platoon })
            .Set(SquadA, new StarGruntUnitState { Level = CommandLevel.Squad })
            .Set(SquadB, new StarGruntUnitState { Level = CommandLevel.Squad })
            .Set(Watcher, new StarGruntUnitState { Level = CommandLevel.Squad })
            .Set(Reserve, new StarGruntUnitState { Level = CommandLevel.Squad });

    /// <summary>A turn under way with Blue holding the first activation.</summary>
    public static GroundCombatSession BlueToPlay()
    {
        var begun = GroundCombatSequence.BeginTurn(Forces());

        // Red is the smaller force, so the choice is Red's; it gives the first activation away.
        var chooser = SequenceGuards.FirstActivationChooser(begun) ?? Blue;
        return GroundCombatSequence.ChooseFirstActivator(begun, chooser, takeIt: chooser == Blue);
    }

    /// <summary>A turn under way with the named Blue unit part-way through its activation.</summary>
    public static GroundCombatSession Activating(UnitId unit) =>
        GroundCombatSequence.BeginActivation(BlueToPlay(), Blue, unit);
}
