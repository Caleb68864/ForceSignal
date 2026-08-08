namespace ForceSignal.Modules.Dirtside.Combat;

/// <summary>
/// The band a shot is actually resolved at, together with the band the tape measured and the reason
/// the two differ.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because the band is read twice - once to pick the firer's die and once to pick
/// which chit colours count - and those two readings must not be computed independently. A damaged
/// firer's close shot resolves as medium, and for a weapon whose valid colours narrow with range
/// that is not a to-hit penalty at all: it is a different damage profile. Working the degradation
/// out in stage one and forgetting it in stage two produces a shot that is harder to land and yet
/// still lands with its close-range chits, which is a bug no assertion about the to-hit die would
/// ever catch.
/// </para>
/// <para>
/// So it is computed once, carried as a value, and handed to both stages. It keeps
/// <see cref="Measured"/> as well as <see cref="Band"/> because an after-action log needs to say
/// "he shot at close range and it resolved as medium, because he was carrying a DMG marker" rather
/// than merely reporting medium.
/// </para>
/// </remarks>
/// <param name="Measured">The band the shot really falls in, off the tape and the record card.</param>
/// <param name="Resolved">
/// The band it resolves at, or null when the degradation has pushed the shot off the end and there
/// is no shot to take.
/// </param>
/// <param name="FirerIsDamaged">Whether the firer was carrying a DMG marker when it fired.</param>
public readonly record struct EffectiveRangeBand(
    WeaponRangeBand Measured,
    WeaponRangeBand? Resolved,
    bool FirerIsDamaged)
{
    /// <summary>True when there is still a band to resolve at.</summary>
    public bool CanFire => Resolved is not null;

    /// <summary>True when the shot resolves at a worse band than it was measured at.</summary>
    public bool WasDegraded => Resolved != Measured;

    /// <summary>The band both stages read.</summary>
    /// <exception cref="InvalidOperationException">There is no shot at all.</exception>
    public WeaponRangeBand Band => Resolved
        ?? throw new InvalidOperationException("That shot has no band to resolve at.");

    /// <summary>Works the effective band out once, for both stages to share.</summary>
    /// <param name="measured">The band the shot really falls in.</param>
    /// <param name="firerIsDamaged">Whether the firer is carrying a DMG marker.</param>
    /// <returns>The band to resolve at, and the provenance to explain it.</returns>
    /// <remarks>
    /// Damage is a flag rather than a counter, so this shifts by one band at most however many
    /// damaging hits the firer has taken. That is <see cref="DamagedEffects"/>' rule, consulted here
    /// rather than restated.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The band is not one of the three.</exception>
    public static EffectiveRangeBand For(WeaponRangeBand measured, bool firerIsDamaged)
    {
        if (!Enum.IsDefined(measured))
        {
            throw new ArgumentOutOfRangeException(
                nameof(measured), measured, "Not one of the three range bands.");
        }

        return new EffectiveRangeBand(
            measured,
            firerIsDamaged ? DamagedEffects.Band(measured) : measured,
            firerIsDamaged);
    }
}
