namespace ForceSignal.Application;

/// <summary>
/// Reads an enum member out of a word a request or a saved row carries, by name only.
/// </summary>
/// <remarks>
/// Not <c>Enum.TryParse</c>, which reads a number ("7", or "1" for whatever happens to be 1) or a
/// comma list it ORs together as readily as a name, and so put values on a card that no name
/// gives - some of them not members at all. Checking <c>IsDefined</c> afterwards still let "1"
/// through as whatever happens to be 1. Every word these read is written as a name, so only a name
/// is read: ignoring case and surrounding space, and only the members the enum defines.
/// </remarks>
internal static class EnumNames
{
    /// <summary>The member a word names, ignoring case, or null when it names none.</summary>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="word">The word, which may be null.</param>
    /// <returns>The member, or null.</returns>
    public static TEnum? ByName<TEnum>(string? word)
        where TEnum : struct, Enum
    {
        var name = Enum.GetNames<TEnum>().FirstOrDefault(name => string.Equals(name, word?.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is null ? null : Enum.Parse<TEnum>(name);
    }

    /// <summary>The member a word names, matching case exactly, or null when it names none.</summary>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="word">The word, which may be null.</param>
    /// <returns>The member, or null.</returns>
    /// <remarks>For words this server wrote itself, which it always writes in the member's own spelling.</remarks>
    public static TEnum? ByExactName<TEnum>(string? word)
        where TEnum : struct, Enum
    {
        var name = Enum.GetNames<TEnum>().FirstOrDefault(name => string.Equals(name, word, StringComparison.Ordinal));
        return name is null ? null : Enum.Parse<TEnum>(name);
    }
}
