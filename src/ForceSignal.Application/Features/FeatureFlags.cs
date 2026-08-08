using ForceSignal.Contracts.Features;

namespace ForceSignal.Application.Features;

/// <summary>
/// Which optional game engines are switched on for this server.
/// </summary>
/// <remarks>
/// <para>
/// ForceSignal's working game is Full Thrust. The ground-combat engines - StarGrunt II at infantry
/// scale, Dirtside II at vehicle scale - are being built alongside it, which means at any given
/// moment one of them may be half-finished. A half-finished engine must not be able to reach a
/// table that turned up to play Full Thrust.
/// </para>
/// <para>
/// So both default to <c>false</c>, and the flag is checked in three places rather than one: the
/// endpoints an engine adds are not mapped at all when it is off, the client is told what is
/// available and hides the rest, and a match cannot be created under a ruleset that is not
/// offered. Turning a flag off is enough to make the engine cease to exist as far as a running
/// match is concerned - no partially-wired state is left behind to interfere.
/// </para>
/// <para>
/// Configuration comes from the <c>Features</c> section, or from the matching environment
/// variables for a container: <c>FORCESIGNAL_FEATURES_STARGRUNT</c> and
/// <c>FORCESIGNAL_FEATURES_DIRTSIDE</c>. Anything that does not read as true leaves the engine off,
/// because the safe reading of an unclear setting is "not ready".
/// </para>
/// </remarks>
public sealed class FeatureFlags
{
    /// <summary>Configuration section these flags are bound from.</summary>
    public const string SectionName = "Features";

    /// <summary>True when the infantry-scale StarGrunt engine is offered by this server.</summary>
    public bool StarGrunt { get; init; }

    /// <summary>True when the vehicle-scale Dirtside engine is offered by this server.</summary>
    public bool Dirtside { get; init; }

    /// <summary>True when no optional engine is switched on, which is the default posture.</summary>
    public bool IsFullThrustOnly => !StarGrunt && !Dirtside;

    /// <summary>Projects the flags into the shape the web client reads at startup.</summary>
    public FeatureFlagsDto ToDto() => new(StarGrunt, Dirtside);

    /// <summary>
    /// Reads the flags from configuration. An engine is on only when its setting reads as an
    /// explicit true; a blank, missing, or unparseable value leaves it off, because an unclear
    /// setting should never be the reason an unfinished engine reaches a game.
    /// </summary>
    /// <param name="configuration">Application configuration to read from.</param>
    /// <returns>The flags this server is running with.</returns>
    public static FeatureFlags Read(IConfigurationSource configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new FeatureFlags
        {
            StarGrunt = ReadFlag(configuration, "StarGrunt", "FORCESIGNAL_FEATURES_STARGRUNT"),
            Dirtside = ReadFlag(configuration, "Dirtside", "FORCESIGNAL_FEATURES_DIRTSIDE"),
        };
    }

    private static bool ReadFlag(IConfigurationSource configuration, string key, string environmentKey)
    {
        var value = configuration.GetValue($"{SectionName}:{key}")
            ?? configuration.GetValue(environmentKey);
        return bool.TryParse(value?.Trim(), out var parsed) && parsed;
    }

    /// <summary>
    /// The slice of configuration these flags need, kept as a one-method interface so the
    /// Application layer does not take a dependency on the hosting configuration stack.
    /// </summary>
    public interface IConfigurationSource
    {
        /// <summary>Reads a configuration value, or null when it is not set.</summary>
        /// <param name="key">Configuration key to read.</param>
        /// <returns>The configured value, or null.</returns>
        string? GetValue(string key);
    }
}
