using ForceSignal.Domain.Rules;

namespace ForceSignal.Domain.Tests;

/// <summary>
/// A ship's class in ForceSignal is free text, so the only normalized size signal is the map icon.
/// Reading a size band off it is an inference, and these tests pin both what it gets right and what
/// it gets wrong, so nobody mistakes it for a lookup.
/// </summary>
public sealed class ShipClassBandsTests
{
    [Theory]
    [InlineData("escort", ShipClassBand.Escort)]
    [InlineData("frigate", ShipClassBand.Escort)]
    [InlineData("destroyer", ShipClassBand.Escort)]
    [InlineData("cruiser", ShipClassBand.Cruiser)]
    [InlineData("dreadnought", ShipClassBand.Capital)]
    [InlineData("carrier", ShipClassBand.Capital)]
    public void FromIconKey_ReadsTheBandOffTheIcon(string iconKey, ShipClassBand expected)
    {
        Assert.Equal(expected, ShipClassBands.FromIconKey(iconKey));
    }

    [Theory]
    // A station is not on the warship size ladder, a fighter group is not a hull at all, and an
    // icon nobody recognises says nothing. All three are "unknown" rather than a guess.
    [InlineData("station")]
    [InlineData("fighter-group")]
    [InlineData("")]
    [InlineData(null)]
    public void FromIconKey_SaysNothingRatherThanGuessingWhenTheIconCannotAnswer(string? iconKey)
    {
        Assert.Null(ShipClassBands.FromIconKey(iconKey));
    }
}
