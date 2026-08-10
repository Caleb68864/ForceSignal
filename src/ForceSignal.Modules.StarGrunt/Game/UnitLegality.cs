using System.Collections.Immutable;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Sequence;

namespace ForceSignal.Modules.StarGrunt.Game;

/// <summary>Whether one weapon may be fired right now, and why not.</summary>
/// <param name="Name">The weapon, as the record card names it.</param>
/// <param name="CanFire">True when a volley from it would be allowed.</param>
/// <param name="Blocker">Why it would not be, in the words the command would refuse with.</param>
public sealed record WeaponLegality(string Name, bool CanFire, string? Blocker);

/// <summary>
/// What one unit may do right now, and why not.
/// </summary>
/// <param name="Unit">The unit this is about.</param>
/// <param name="CanActivate">True when this unit could be activated.</param>
/// <param name="ActivationBlocker">Why it could not be.</param>
/// <param name="Weapons">Each weapon it carries, and whether it may fire.</param>
public sealed record UnitLegality(
    UnitId Unit,
    bool CanActivate,
    string? ActivationBlocker,
    ImmutableArray<WeaponLegality> Weapons)
{
    /// <inheritdoc />
    public bool Equals(UnitLegality? other) =>
        other is not null
        && Unit == other.Unit
        && CanActivate == other.CanActivate
        && ActivationBlocker == other.ActivationBlocker
        && StructuralEquality.Sequence(Weapons, other.Weapons);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Unit, CanActivate, ActivationBlocker, StructuralEquality.SequenceHash(Weapons));
}

public sealed partial record StarGruntGame
{
    /// <summary>
    /// What every unit on the table may do right now.
    /// </summary>
    /// <returns>One answer per unit.</returns>
    public ImmutableDictionary<UnitId, UnitLegality> Legality() =>
        Units.Keys.ToImmutableDictionary(unit => unit, LegalityFor);

    /// <summary>
    /// What one unit may do right now, and why not.
    /// </summary>
    /// <param name="unit">The unit to ask about.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException">No such unit is on the table.</exception>
    /// <remarks>
    /// <para>
    /// Every answer here comes from the same call the matching command makes, so the two cannot
    /// disagree - the reason shown on a disabled button is the reason the command would have given.
    /// A test asserts that they are the same string, which is only possible while there is one
    /// implementation of each rule.
    /// </para>
    /// <para>
    /// This is a question, so it describes rather than refuses, and it changes nothing.
    /// </para>
    /// </remarks>
    public UnitLegality LegalityFor(UnitId unit)
    {
        var definition = Unit(unit);
        var activation = BeginActivation(definition.Side, unit);

        return new UnitLegality(
            unit,
            activation.IsAllowed,
            activation.Reason,
            [.. definition.Weapons.Select(weapon => FireLegality(unit, weapon))]);
    }

    /// <summary>Whether this unit could fire this weapon, asked exactly as firing asks it.</summary>
    private WeaponLegality FireLegality(UnitId unit, WeaponProfile weapon)
    {
        var status = Status(unit);
        if (status.IsWipedOut)
        {
            return new WeaponLegality(weapon.Name, false, $"{Unit(unit).Name} has nobody left to shoot with.");
        }

        // The same step Fire spends, checked the same way. Asking the sequence rather than reasoning
        // about frames here is what keeps the per-activation limit in one place.
        var check = GroundCombatSequence.CanTakeStep(Session, StarGruntSteps.Fire(weapon.Name), Policy());
        return new WeaponLegality(weapon.Name, check.IsAllowed, check.Reason);
    }
}
