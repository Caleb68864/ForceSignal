using System.Text.RegularExpressions;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// Every die named in the StarGrunt engine's own source, held to the content policy.
/// </summary>
/// <remarks>
/// <para>
/// The third guard, and it had to be a different kind of thing from the first two. The client's
/// <c>contentPolicy.test.ts</c> walks form objects. <c>WireDefaultsContentPolicyTests</c> walks the
/// contracts assembly by reflection, because a default parameter value on a contract record <b>is</b>
/// the wire contract. Neither can see <c>target.Figures.IsDefaultOrEmpty ? QualityDie.D6 : ...</c>,
/// which is where the die that decided whether a hit killed came from when a unit arrived with no
/// roster: it is not on the wire, it is not on a type, and it is not in any metadata. It is a choice
/// made inside a method body, and a method body has nowhere to hang an attribute.
/// </para>
/// <para>
/// So this one reads the source. That is the only instrument that can see it, and the cost is that
/// it must be told where the source is - so it fails loudly rather than passing quietly when it
/// cannot find it, which is the failure mode every guard in this repository has been bitten by.
/// </para>
/// <para>
/// Scoped to the StarGrunt module on purpose, and not to every engine. This module's whole design is
/// that it ships no table at all - <c>UnitDefinition</c> "has somewhere to put an impact die and
/// nowhere to look one up" - so a die literal here is a defect by that module's own contract. The
/// Dirtside module is a different design and would need its own argument; see the note at the foot
/// of this file.
/// </para>
/// <para>
/// It names no bad value. It counts every <c>QualityDie.Dn</c> in the module and requires each file
/// and die to carry a written exemption with the count it is allowed. A new one, an extra one, or a
/// changed one fails the suite until somebody classifies it - which is the only kind of guard that
/// catches the next one rather than the last one.
/// </para>
/// </remarks>
public sealed partial class EngineDiceContentPolicyTests
{
    /// <summary>Every mention of a rung of the quality ladder, by name.</summary>
    [GeneratedRegex(@"QualityDie\.D\d+")]
    private static partial Regex DieLiteral();

    /// <summary>
    /// Comments, which are read past rather than scanned.
    /// </summary>
    /// <remarks>
    /// A guard that fired on prose would be a guard that taught people to stop writing the sentence
    /// explaining what the number used to be and why it is gone - and those sentences are most of
    /// what makes a fix like this survivable. Every removal in this pass left one behind naming the
    /// die it replaced, and all three tripped this scan until it learned to read code only.
    /// </remarks>
    [GeneratedRegex(@"/\*.*?\*/|//[^\r\n]*", RegexOptions.Singleline)]
    private static partial Regex Comment();

    /// <summary>
    /// The only dice the engine may name in its own source, with how many of each and why.
    /// </summary>
    /// <remarks>
    /// Empty, and that is the point: every occurrence that was here has been removed rather than
    /// argued for. An entry added tomorrow has to say where the number came from if not a record
    /// card, and the count keeps the argument honest - "this one placeholder" cannot quietly become
    /// four.
    /// </remarks>
    private static readonly Dictionary<string, (int Count, string Reason)> NotARulesDie =
        new(StringComparer.Ordinal);

    [Fact]
    public void TheEngineNamesNoDieOfItsOwn()
    {
        var (files, found) = DiceInTheModule();

        // Reached-the-subject: the walk really found this module's source. A scan that matched no
        // files would report no dice and pass, proving nothing whatever - which is exactly how a
        // probe in this project's history died on a wrong glob key and still claimed a clean run.
        Assert.True(files > 20, $"only {files} source files were scanned; the module was not found");

        var shipped = found
            .Where(entry => !NotARulesDie.TryGetValue(entry.Key, out var allowed) || allowed.Count != entry.Value)
            .Select(entry => $"{entry.Key} x{entry.Value}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([], shipped);
    }

    [Fact]
    public void TheScanCanSeeADieWhenThereIsOneToSee()
    {
        // The control that must fail, and without it the check above is indistinguishable from a
        // scan that reads nothing. A file holding a die literal is scanned the same way the module
        // is, and has to come back holding it.
        var planted = Path.Combine(Path.GetTempPath(), $"forcesignal-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(planted);
        try
        {
            var file = Path.Combine(planted, "Invented.cs");
            File.WriteAllText(
                file,
                """
                // A comment about QualityDie.D12, which is prose and not a decision.
                /// <summary>So is <c>QualityDie.D10</c>.</summary>
                var armour = QualityDie.D6;
                var other = QualityDie.D6; /* and QualityDie.D4 in a block */
                """);

            var found = DiceIn(planted);

            Assert.Equal(2, found["Invented.cs:D6"]);
            // The other half of the control: the three in prose are read past, so the guard does
            // not teach people to stop writing down what a number used to be.
            Assert.Equal(["Invented.cs:D6"], found.Keys);
        }
        finally
        {
            Directory.Delete(planted, recursive: true);
        }
    }

    [Fact]
    public void EveryExemptionNamesADieThatIsStillThere()
    {
        // The other half of the list not rotting: an exemption for a line somebody has since
        // deleted is an argument nobody is making, and a list of those would let the next real one
        // through under a familiar-looking name. Vacuously true while the list is empty, and it
        // stops being vacuous the moment anybody adds to it.
        var (_, found) = DiceInTheModule();

        foreach (var (key, allowed) in NotARulesDie)
        {
            Assert.True(found.ContainsKey(key), $"{key} is exempted and is no longer in the source");
            Assert.Equal(allowed.Count, found[key]);
            Assert.False(string.IsNullOrWhiteSpace(allowed.Reason), $"{key} is exempted with no reason");
        }
    }

    private static (int Files, Dictionary<string, int> Found) DiceInTheModule()
    {
        var module = Path.Combine(RepositoryRoot(), "src", "ForceSignal.Modules.StarGrunt");
        Assert.True(Directory.Exists(module), $"the StarGrunt module is not at {module}");

        return (SourceFiles(module).Count, DiceIn(module));
    }

    /// <summary>Every <c>QualityDie.Dn</c> under a directory, keyed by file and die.</summary>
    private static Dictionary<string, int> DiceIn(string directory)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(directory))
        {
            var code = Comment().Replace(File.ReadAllText(file), string.Empty);
            foreach (Match match in DieLiteral().Matches(code))
            {
                var key = $"{Path.GetFileName(file)}:{match.Value.Split('.')[1]}";
                found[key] = found.GetValueOrDefault(key) + 1;
            }
        }

        return found;
    }

    /// <summary>The module's own sources, without the build output it also sits on top of.</summary>
    private static List<string> SourceFiles(string directory) =>
        [.. Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

    /// <summary>The checkout, found by the solution file rather than by counting "..".</summary>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ForceSignal.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"no ForceSignal.slnx above {AppContext.BaseDirectory}, so this guard cannot reach the source it is about");
    }
}

// A note on what this guard deliberately does not cover, so the next reader does not mistake its
// silence for a clean bill of health.
//
// The Dirtside module ships three die tables in `HitResolution.cs`: a fire-control level is worth a
// D6, a D8 or a D10; a defensive posture is worth a D6, a D8, a D10 or a D12; and a target's
// signature indexes the ladder directly. Those are readings off somebody's card in exactly the sense
// this policy is about, and they are a far larger finding than anything in this pass - that module's
// whole combat model is built on them, so removing them is a design change rather than an edit.
// Extending this guard over Dirtside would have meant either breaking that module or writing four
// exemptions that are not arguments, and an exemption list that launders a violation is worse than
// no list. It is recorded here instead, where whoever takes it next will find it.
