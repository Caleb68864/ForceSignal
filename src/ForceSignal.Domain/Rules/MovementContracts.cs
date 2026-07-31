using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForceSignal.Domain.Rules;

/// <summary>Current movement state used as the starting point for resolving a movement order.</summary>
public sealed record ShipMovementState(int Velocity, int Course);

/// <summary>One ordered turn maneuver within a multi-turn movement plot.</summary>
public sealed record TurnManeuver(TurnDirection Direction, int Steps);

/// <summary>Movement order submitted during the hidden order phase.</summary>
public sealed record MovementOrder(
    int VelocityDelta,
    int TurnSteps,
    TurnDirection TurnDirection,
    IReadOnlyList<TurnManeuver>? TurnManeuvers = null);

/// <summary>Direction of a turn on the twelve-point course clock.</summary>
public enum TurnDirection
{
    /// <summary>No turn is ordered.</summary>
    None,

    /// <summary>Turn counter-clockwise on the course clock.</summary>
    Port,

    /// <summary>Turn clockwise on the course clock.</summary>
    Starboard
}

/// <summary>One resolved movement segment after turn sequencing is applied.</summary>
public sealed record MovementSegment(int Course, decimal Distance);

/// <summary>Resolved movement output for a ship order.</summary>
public sealed record MovementResult(
    int StartingVelocity,
    int StartingCourse,
    int EndingVelocity,
    int EndingCourse,
    IReadOnlyList<MovementSegment>? Segments = null);

/// <summary>Validation result for a movement order.</summary>
public sealed record OrderValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>Reusable successful validation result.</summary>
    public static OrderValidationResult Success { get; } = new(true, Array.Empty<string>());

    /// <summary>Creates a failed validation result with one or more human-readable errors.</summary>
    public static OrderValidationResult Failure(params string[] errors) => new(false, errors);
}

/// <summary>Normalizes movement orders into stable JSON for commitment hashing.</summary>
public interface IOrderNormalizer
{
    /// <summary>Returns deterministic JSON for semantically equivalent movement orders.</summary>
    string Normalize(MovementOrder order);
}

/// <summary>Validates movement orders against a rules profile.</summary>
public interface IOrderValidator
{
    /// <summary>Validates an order from the supplied ship state and thrust rating.</summary>
    OrderValidationResult Validate(ShipMovementState shipState, int thrustRating, MovementOrder order);
}

/// <summary>Resolves movement orders into final velocity, course, and movement segments.</summary>
public interface IMovementResolver
{
    /// <summary>Resolves an order from the supplied starting movement state.</summary>
    MovementResult Resolve(ShipMovementState shipState, MovementOrder order);
}

/// <summary>Creates and verifies salted hidden-order commitments.</summary>
public interface ICommitmentService
{
    /// <summary>Creates a commitment hash from normalized order JSON and a salt.</summary>
    string CreateHash(string normalizedOrderJson, string salt);

    /// <summary>Verifies a commitment hash against normalized order JSON and a salt.</summary>
    bool Verify(string commitmentHash, string normalizedOrderJson, string salt);

    /// <summary>Creates a cryptographically random salt for a hidden order.</summary>
    string CreateSalt();
}

/// <summary>SHA-256 implementation of hidden-order commitment hashing.</summary>
public sealed class Sha256CommitmentService : ICommitmentService
{
    /// <inheritdoc />
    public string CreateHash(string normalizedOrderJson, string salt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{normalizedOrderJson}.{salt}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <inheritdoc />
    public bool Verify(string commitmentHash, string normalizedOrderJson, string salt)
    {
        var expected = CreateHash(normalizedOrderJson, salt);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(commitmentHash.ToLowerInvariant()));
    }

    /// <inheritdoc />
    public string CreateSalt() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}

/// <summary>Shared stable JSON settings used for deterministic commitment inputs.</summary>
public static class StableJson
{
    /// <summary>Stable serializer options for commitment JSON.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    static StableJson()
    {
        Options.Converters.Add(new JsonStringEnumConverter());
    }
}
