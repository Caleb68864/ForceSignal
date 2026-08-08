namespace ForceSignal.Contracts.Features;

/// <summary>
/// Which optional game engines this server is offering. ForceSignal's working game is Full Thrust;
/// the ground-combat engines are built alongside it and are off until they are ready to be played.
/// The client reads this once at startup and hides everything a disabled engine would have shown,
/// so a half-built engine cannot appear in the middle of a real match.
/// </summary>
/// <param name="StarGrunt">True when the infantry-scale ground combat engine is offered.</param>
/// <param name="Dirtside">True when the vehicle-scale ground combat engine is offered.</param>
public sealed record FeatureFlagsDto(bool StarGrunt, bool Dirtside);
