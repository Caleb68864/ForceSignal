using System.Globalization;
using System.Reflection;
using ForceSignal.Contracts.Matches;

namespace ForceSignal.Api.Tests;

/// <summary>
/// Every number on the wire, held to the content policy by a walk rather than by a reading.
/// </summary>
/// <remarks>
/// <para>
/// The client has a guard of this shape - <c>contentPolicy.test.ts</c> enumerates the numeric fields
/// of the new-ship form and requires each to be zero or to carry a written exemption - and it cannot
/// see this side. The application-level <c>ContentPolicyTests</c> cannot see it either, because a
/// C# caller who omits an argument is doing so knowingly.
/// </para>
/// <para>
/// A default parameter value on a contract record <b>is</b> the wire contract. System.Text.Json
/// fills a missing JSON property from it, so a <c>= 6</c> here is a number this app hands to a
/// caller who never asked for one. That is where <c>CreateShipRequest.FireControlMax = 1</c> and
/// <c>StarGruntWeaponDto.SupportFirepowerDie = 6</c> both lived, unseen by either existing guard.
/// </para>
/// <para>
/// So this names no bad value. It walks every record in the contracts assembly, finds every numeric
/// constructor parameter whose default is not zero, and requires each one to be on the list below
/// with the reason it is not a rules number written next to it. A new non-zero default added
/// tomorrow fails the suite until somebody classifies it, which is the only kind of guard that
/// catches the next one rather than the last one.
/// </para>
/// </remarks>
public sealed class WireDefaultsContentPolicyTests
{
    /// <summary>
    /// The only non-zero numbers a contract record may open on, each with its argument.
    /// </summary>
    /// <remarks>
    /// To add an entry here you have to write down why the number is not something the player owns.
    /// That is the point: the list is short, and every line is an argument rather than a value. Two
    /// arguments are allowed and no others - a quantity with no meaningful zero, and a floor the
    /// engine itself enforces in the same place.
    /// </remarks>
    private static readonly Dictionary<string, string> NotARulesNumber = new(StringComparer.Ordinal)
    {
        // A heading, not a quantity. The engine reads a twelve-point course clock and there is no
        // zero on it. Same argument the client form's `currentCourse` had to make.
        ["CreateOrdnanceMarkerRequest.Course"] = "a heading on a 12-point clock, which has no zero",
        ["UpdateOrdnanceMarkerRequest.Course"] = "a heading on a 12-point clock, which has no zero",

        // The felt, not the rules. Nothing reads the table's size as a rule; it is read to keep a
        // model on the table, and a table of no size has nowhere to put one - so zero here is not
        // "unentered", it is unplayable. Measured with a tape, like a model's place on it.
        ["CreateMatchRequest.TableWidth"] = "the size of the table, which a tape measure settles and which cannot be zero",
        ["CreateMatchRequest.TableDepth"] = "the size of the table, which a tape measure settles and which cannot be zero",

        // A floor, and one the service enforces in the same breath: DirtsideGameService clamps
        // barrels to at least one and the combat resolver throws below one, because a mount that
        // fires nothing is not a mount. The wire default states that floor rather than a number off
        // anybody's card.
        ["DirtsideWeaponDto.Barrels"] = "a floor the service clamps to anyway: a mount of no barrels is not a mount",
    };

    [Fact]
    public void NoContractRecordOpensOnANumberNobodyEntered()
    {
        var walked = NumericDefaults().ToList();

        // Reached-the-subject: the walk really found the contracts assembly and really found
        // numbers in it. A reflection walk that matched nothing would pass silently and prove
        // nothing at all, which is the failure mode this whole file exists to avoid elsewhere.
        Assert.True(walked.Count > 100, $"only {walked.Count} numeric contract parameters were walked");

        var shipped = walked
            .Where(found => found.Default != 0m && !NotARulesNumber.ContainsKey(found.Key))
            .Select(found => $"{found.Key} = {found.Default}")
            .ToList();

        Assert.Equal([], shipped);
    }

    [Fact]
    public void EveryExemptionNamesARealNumberThatIsStillNotZero()
    {
        // The control that must be accepted, and the one that stops this list rotting into
        // decoration. An exemption whose parameter has been renamed, or has since been zeroed,
        // is an argument nobody is making any more - and a list of those would quietly let the
        // next real one through under a familiar-looking name.
        var walked = NumericDefaults().ToDictionary(found => found.Key, found => found.Default);

        foreach (var (key, reason) in NotARulesNumber)
        {
            Assert.True(walked.ContainsKey(key), $"{key} is exempted and no longer exists");
            Assert.NotEqual(0m, walked[key]);
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{key} is exempted with no reason");
        }
    }

    /// <summary>Every numeric constructor parameter of every contract record, with its default.</summary>
    private static IEnumerable<(string Key, decimal Default)> NumericDefaults()
    {
        var records = typeof(CreateShipRequest).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.GetMethod("<Clone>$") is not null);

        foreach (var record in records)
        {
            // The primary constructor, which is the one the wire is deserialised through.
            var constructor = record.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(candidate => candidate.GetParameters().Length)
                .First();

            foreach (var parameter in constructor.GetParameters())
            {
                var kind = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                if (kind != typeof(int) && kind != typeof(long)
                    && kind != typeof(decimal) && kind != typeof(double))
                {
                    continue;
                }

                var value = parameter.HasDefaultValue && parameter.DefaultValue is not null
                    ? Convert.ToDecimal(parameter.DefaultValue, CultureInfo.InvariantCulture)
                    : 0m;

                yield return ($"{record.Name}.{parameter.Name}", value);
            }
        }
    }
}
