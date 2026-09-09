using ForceSignal.Contracts.Matches;
using ForceSignal.Domain.Rules;

namespace ForceSignal.Application.Matches;

/// <content>
/// Taking whatever a caller sent and making it something a match can hold.
///
/// Everything here is total: it returns a usable value for any input rather than refusing one.
/// That is deliberate on the import path - a fleet file may have been written by an older version
/// or edited by hand, and losing one unreadable field beats losing the whole import - which is
/// also why the callers do their own refusing where a bad value should actually stop the action.
/// </content>
public sealed partial class InMemoryMatchService
{    private static int ClampFireControl(int value) => Math.Clamp(value, 0, 6);
    private static int ClampPointDefense(int value) => Math.Clamp(value, 0, 12);
    private static int ClampFighterBays(int value) => Math.Clamp(value, 0, 12);
    private static int ClampDamageControl(int value) => Math.Clamp(value, 0, 12);
    private static int ClampPoints(int value) => Math.Clamp(value, 0, 99999);
    private static int ClampDamage(int value, int max) => Math.Clamp(value, 0, max);
    private static decimal ClampPosition(decimal value, int max) => Math.Clamp(value, 0, max);
    private static string NormalizeFleetColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#47f1ff";
        }

        var color = value.Trim();
        return color.Length == 7
            && color[0] == '#'
            && color.Skip(1).All(Uri.IsHexDigit)
                ? color
                : "#47f1ff";
    }
    private static string NormalizeIconKey(string? iconKey, string? className)
    {
        var normalized = NormalizeIconText(iconKey);
        if (IsKnownIcon(normalized))
        {
            return normalized;
        }

        var classText = NormalizeIconText(className);
        if (classText.Contains("escort", StringComparison.Ordinal))
        {
            return "escort";
        }

        if (classText.Contains("frigate", StringComparison.Ordinal))
        {
            return "frigate";
        }

        if (classText.Contains("destroyer", StringComparison.Ordinal))
        {
            return "destroyer";
        }

        if (classText.Contains("carrier", StringComparison.Ordinal))
        {
            return "carrier";
        }

        if (classText.Contains("dreadnought", StringComparison.Ordinal) || classText.Contains("battleship", StringComparison.Ordinal))
        {
            return "dreadnought";
        }

        if (classText.Contains("fighter", StringComparison.Ordinal))
        {
            return "fighter-group";
        }

        if (classText.Contains("station", StringComparison.Ordinal) || classText.Contains("base", StringComparison.Ordinal))
        {
            return "station";
        }

        return "cruiser";
    }
    private static string NormalizeIconText(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
    private static bool IsKnownIcon(string iconKey) => iconKey is
        "escort" or
        "frigate" or
        "destroyer" or
        "cruiser" or
        "carrier" or
        "dreadnought" or
        "fighter-group" or
        "station";
    private static int NormalizeFighterEnduranceMax(int value, string iconKey, string? className) =>
        IsFighterGroup(iconKey, className)
            ? Math.Clamp(value <= 0 ? 6 : value, 1, 24)
            : 0;
    private static int NormalizeFighterEnduranceUsed(int value, int max) =>
        Math.Clamp(value, 0, Math.Max(0, max));
    private static int NormalizeFighterMaxRange(int value, string iconKey, string? className) =>
        IsFighterGroup(iconKey, className)
            ? Math.Clamp(value <= 0 ? 24 : value, 1, 120)
            : 0;
    private static string NormalizeFighterStatus(string? value, string iconKey, string? className)
    {
        if (!IsFighterGroup(iconKey, className))
        {
            return "Docked";
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "airborne" or "launched" or "active" => "Airborne",
            "recovering" or "returning" or "return" => "Recovering",
            _ => "Docked",
        };
    }
    private static int NormalizeCourse(int course)
    {
        var zeroBased = ((course - 1) % 12 + 12) % 12;
        return zeroBased + 1;
    }
    private static string NormalizeOrdnanceText(string? value, string fallback) => NormalizeText(value, fallback);
    /// <summary>
    /// Trims caller-supplied display text, falling back when it is blank. Text longer than a ship
    /// name has any business being is truncated rather than refused: a name is pasted from a fleet
    /// file as often as it is typed, and losing the tail of an over-long one is friendlier than
    /// rejecting the import - but it is not allowed to grow the snapshot without limit.
    /// </summary>
    private static string NormalizeText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : Truncate(value.Trim());
    /// <summary>Trims optional display text, collapsing blank input to null.</summary>
    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim());
    private static string Truncate(string value) =>
        value.Length <= MaxDisplayTextLength ? value : value[..MaxDisplayTextLength];
    /// <summary>
    /// Trims a battle-log line coming in from a snapshot. Held to a longer ceiling than a name,
    /// because the server's own lines - a ship's state at the start of a phase, say - run well past
    /// what a name is allowed, and a restore that cut them off would hand back a log with the
    /// numbers missing. Still bounded: a log line is not a place to store a file.
    /// </summary>
    private static string NormalizeLogMessage(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim() is var trimmed && trimmed.Length <= MaxLogMessageLength
                ? trimmed
                : trimmed[..MaxLogMessageLength];
    private static string NormalizeOrdnanceStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "spent" or "resolved" or "hit" => "Resolved",
            "expired" or "ended" => "Expired",
            _ => "Active",
        };
    /// <summary>
    /// Normalizes weapons for a restore. NormalizeWeapons drops blank-named mounts, which is right
    /// for a form row a user left empty but is silent data loss when recovering a fleet, so every
    /// mount is kept and an unnamed one gets a placeholder name instead.
    /// </summary>
    private static WeaponMountState[] NormalizeRestoredWeapons(IReadOnlyList<WeaponMountDto>? weapons) =>
        weapons is null or { Count: 0 }
            ? NormalizeWeapons(weapons)
            : NormalizeWeapons([.. weapons.Select(w => w with { Name = NormalizeText(w.Name, "Unnamed Mount") })]);
    /// <summary>
    /// Resolves the arcs a mount bears through. An explicit set wins; otherwise the legacy
    /// four-arc name is expanded. The aft arc is always removed, because every weapon has it
    /// blacked out, and a mount left with nothing is treated as bearing fore.
    /// </summary>
    private static FiringArc[] NormalizeArcs(WeaponMountDto weapon)
    {
        var arcs = weapon.Arcs is { Count: > 0 }
            ? weapon.Arcs
            : ExpandLegacyArc(weapon.Arc);
        // Keep the canonical clockwise order however the caller listed them.
        var firable = FiringArcs.Firable.Where(arcs.Contains).ToArray();
        return firable.Length > 0 ? firable : [FiringArc.Fore];
    }
    /// <summary>
    /// Expands a pre-six-arc mount name. The old arcs were 90 degrees wide, so each side arc
    /// becomes the two 60 degree arcs on that side, and the old aft arc becomes the two quarters
    /// either side of the blind spot.
    /// </summary>
    private static IReadOnlyList<FiringArc> ExpandLegacyArc(string? legacyArc)
    {
        var name = legacyArc?.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return name switch
        {
            "all" => FiringArcs.Firable,
            "port" => [FiringArc.ForePort, FiringArc.AftPort],
            "starboard" => [FiringArc.ForeStarboard, FiringArc.AftStarboard],
            "aft" => [FiringArc.AftPort, FiringArc.AftStarboard],
            null or "" => [FiringArc.Fore],
            _ => FiringArcJsonConverter.TryParse(name, out var arc) && FiringArcs.CanFireThrough(arc)
                ? [arc]
                : [FiringArc.Fore],
        };
    }
    private static WeaponMountState[] NormalizeWeapons(IReadOnlyList<WeaponMountDto>? weapons)
    {
        // A ship that named no mounts has none. It used to be handed a "Class-2 Beam" firing two
        // dice out to twenty-four: a class name, a damage rating and a reach, none of which anybody
        // entered and all three of which are exactly the kind of number this project does not ship.
        // Worse than the client's copies of the same mount, because it was invisible - a ship
        // created with an empty weapons list came back armed, and the player had no way to know the
        // server had written the stats for them.
        if (weapons is null || weapons.Count == 0)
        {
            return [];
        }

        return weapons
            .Where(w => !string.IsNullOrWhiteSpace(w.Name))
            .Take(MaxWeaponsPerShip)
            .Select(w => new WeaponMountState(
                w.Id == Guid.Empty ? Guid.NewGuid() : w.Id,
                Truncate(w.Name.Trim()),
                Math.Clamp(w.AttackDice, 1, 12),
                Math.Clamp(w.MaxRange, 1, 72),
                NormalizeArcs(w),
                Math.Clamp(w.AmmoMax, 0, 99),
                Math.Clamp(w.AmmoUsed, 0, Math.Max(0, w.AmmoMax)),
                Math.Clamp(w.ReloadTurns, 0, 12),
                w.Kind)
            {
                IsDestroyed = w.IsDestroyed,
                // A mount a needle cut out stays cut out across an export. Only a knocked-out mount
                // can be needled, so the flag never survives on a working one.
                IsNeedleKilled = w.IsDestroyed && w.IsNeedleKilled,
            })
            .ToArray();
    }
    private static string NextCopyName(string sourceName, IEnumerable<string> existingNames)
    {
        var index = 2;
        string candidate;
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        do
        {
            candidate = $"{sourceName} {index}";
            index++;
        } while (names.Contains(candidate));

        return candidate;
    }
}
