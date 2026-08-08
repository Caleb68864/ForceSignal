using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForceSignal.Domain.Rules;

/// <summary>
/// Reads firing arcs by name, accepting the four-arc names ForceSignal used before it moved to the
/// six sixty-degree arcs. Snapshots exported by an older build, and fleets saved in a browser
/// library, still carry "Port", "Starboard" and "All", so they are mapped rather than rejected.
/// Always writes the canonical name.
/// </summary>
public sealed class FiringArcJsonConverter : JsonConverter<FiringArc>
{
    /// <summary>Maps a stored arc name, legacy or canonical, onto a single arc.</summary>
    public static bool TryParse(string? name, out FiringArc arc)
    {
        switch (name?.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant())
        {
            case "fore":
            case "all": // An all-round mount that fired: the specific arc used is no longer known.
                arc = FiringArc.Fore;
                return true;
            case "forestarboard":
            case "starboard": // The old 90-degree starboard arc straddled both starboard arcs.
                arc = FiringArc.ForeStarboard;
                return true;
            case "aftstarboard":
                arc = FiringArc.AftStarboard;
                return true;
            case "aft":
                arc = FiringArc.Aft;
                return true;
            case "aftport":
                arc = FiringArc.AftPort;
                return true;
            case "foreport":
            case "port":
                arc = FiringArc.ForePort;
                return true;
            default:
                arc = FiringArc.Fore;
                return false;
        }
    }

    /// <inheritdoc />
    public override FiringArc Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // An unknown arc is refused whichever way it is written. The numeric path used to fall back
        // to Fore, which is the most permissive arc there is - so a garbled fleet file silently
        // granted a mount forward coverage it was never built with, and did it without a word.
        // Refusing means a malformed file is reported as one instead of being quietly repaired into
        // something playable but wrong.
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.TryGetInt32(out var ordinal) && Enum.IsDefined(typeof(FiringArc), ordinal)
                ? (FiringArc)ordinal
                : throw new JsonException("That is not a firing arc.");
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            // Reading a string out of a boolean or an array throws the wrong kind of exception,
            // which surfaces as a server fault rather than a bad request.
            throw new JsonException($"A firing arc must be a name or a number, not {reader.TokenType}.");
        }

        var name = reader.GetString();
        return TryParse(name, out var arc)
            ? arc
            : throw new JsonException($"'{name}' is not a firing arc.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, FiringArc value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
