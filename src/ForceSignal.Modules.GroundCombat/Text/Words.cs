namespace ForceSignal.Modules.GroundCombat.Text;

/// <summary>
/// Small pieces of English the log and the refusals are built from, so a name or a count reads
/// right rather than as "Red Lancers's" or "1 hit(s)" (playtest p1).
/// </summary>
public static class Words
{
    /// <summary>A name in the possessive: "Anvil's", and "Red Lancers'" for a name ending in s.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The possessive.</returns>
    public static string Possessive(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.EndsWith('s') || name.EndsWith('S') ? $"{name}'" : $"{name}'s";
    }

    /// <summary>A count and its noun, singular or plural: "1 hit", "3 hits".</summary>
    /// <param name="count">How many.</param>
    /// <param name="singular">The noun for one.</param>
    /// <param name="plural">The noun for any other number; the singular with an s when left out.</param>
    /// <returns>The phrase.</returns>
    public static string Count(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";
}
