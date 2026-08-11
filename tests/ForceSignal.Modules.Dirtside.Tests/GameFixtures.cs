using System.Collections.Immutable;
using ForceSignal.Modules.Dirtside.Chits;
using ForceSignal.Modules.Dirtside.Combat;
using ForceSignal.Modules.Dirtside.Game;
using ForceSignal.Modules.Dirtside.Morale;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;

namespace ForceSignal.Modules.Dirtside.Tests;

/// <summary>
/// A small table to play games of Dirtside on: two armoured platoons of two vehicles each, facing
/// one another.
/// </summary>
/// <remarks>
/// Every number here is invented, for the same reason the Full Thrust fixtures are: a fixture copied
/// out of a rulebook would put the rulebook in the repository and would prove nothing about whether
/// the code read it.
/// </remarks>
internal static class GameFixtures
{
    internal static SideId Blue { get; } = new("blue");
    internal static SideId Red { get; } = new("red");

    internal static UnitId Alpha { get; } = new("alpha");
    internal static UnitId Bravo { get; } = new("bravo");

    internal static ElementId AlphaOne { get; } = new("alpha-1");
    internal static ElementId AlphaTwo { get; } = new("alpha-2");
    internal static ElementId BravoOne { get; } = new("bravo-1");
    internal static ElementId BravoTwo { get; } = new("bravo-2");

    /// <summary>A weapon that can hurt anything at any range, so a test can reach the interesting part.</summary>
    internal static WeaponDefinition MainGun { get; } = new(
        "Main Gun",
        ChitCount: 3,
        Barrels: 1,
        IsFixedMount: false,
        WeaponValidityCard.Flat(new ChitValidity(ChitColours.All)));

    /// <summary>A weapon aimed by pointing the vehicle, for the one rule that reads step order.</summary>
    internal static WeaponDefinition HullGun { get; } = new(
        "Hull Gun",
        ChitCount: 2,
        Barrels: 1,
        IsFixedMount: true,
        WeaponValidityCard.Flat(new ChitValidity(ChitColours.All)));

    /// <summary>One vehicle.</summary>
    internal static ElementDefinition Vehicle(
        ElementId id,
        string name,
        int armour = 3,
        FireControlLevel fireControl = FireControlLevel.Basic,
        params WeaponDefinition[] weapons) =>
        new(id, name, fireControl, Signature: 3, ArmourValue: armour, Movement: 12,
            weapons.Length == 0 ? [MainGun] : [.. weapons]);

    /// <summary>A platoon of two vehicles.</summary>
    internal static PlatoonDefinition Platoon(
        UnitId id,
        string name,
        SideId side,
        ElementDefinition first,
        ElementDefinition second) =>
        new(id, name, side, DirtsideUnitKind.Armour, IsCybertank: false, [first, second]);

    /// <summary>Two platoons facing one another, nobody having done anything yet.</summary>
    internal static DirtsideGame TwoPlatoonGame() =>
        DirtsideGame.Create("Table")
            .WithUnit(Platoon(
                Alpha, "Alpha Troop", Blue,
                Vehicle(AlphaOne, "Alpha One"),
                Vehicle(AlphaTwo, "Alpha Two")))
            .WithUnit(Platoon(
                Bravo, "Bravo Troop", Red,
                Vehicle(BravoOne, "Bravo One"),
                Vehicle(BravoTwo, "Bravo Two")));

    /// <summary>The same table, with Alpha's activation open and ready to take steps.</summary>
    internal static DirtsideGame Activated() =>
        TwoPlatoonGame()
            .BeginTurn().Value!
            .ChooseFirstActivator(Blue, takeIt: true).Value!
            .BeginActivation(Blue, Alpha).Value!;
}

/// <summary>A chit pot that hands out exactly the chits a test asks for, in order.</summary>
internal sealed class ScriptedChitPot(params DamageChit[] chits) : IChitPot
{
    private readonly DamageChit[] _chits = chits;
    private int _index;

    /// <summary>Draws the next chits in the script, cycling when it runs out.</summary>
    /// <param name="count">How many to draw.</param>
    /// <returns>The chits, in the order they came out.</returns>
    public IReadOnlyList<DamageChit> Draw(int count) =>
        _chits.Length == 0
            ? []
            : [.. Enumerable.Range(0, Math.Max(0, count)).Select(_ => _chits[_index++ % _chits.Length])];
}

/// <summary>
/// A die source that hands out exactly the faces a test asks for, in order.
/// </summary>
/// <remarks>
/// The same shape two of the engine test files already keep privately. It is shared here rather than
/// copied a third time, because the game tests roll through several layers at once and a script that
/// runs out should say so loudly rather than quietly returning something.
/// </remarks>
/// <param name="rolls">The faces, in the order they should come up.</param>
internal sealed class ScriptedDice(params int[] rolls) : IQualityDiceRoller
{
    private readonly Queue<int> _rolls = new(rolls);

    /// <summary>How many scripted faces are left.</summary>
    public int Remaining => _rolls.Count;

    /// <summary>The next scripted face.</summary>
    /// <param name="die">The die being rolled, named only so a failure can say which.</param>
    /// <returns>The face.</returns>
    /// <exception cref="InvalidOperationException">The script ran out.</exception>
    public int Roll(QualityDie die) => _rolls.Count > 0
        ? _rolls.Dequeue()
        : throw new InvalidOperationException($"The script ran out while rolling a {die}.");
}
