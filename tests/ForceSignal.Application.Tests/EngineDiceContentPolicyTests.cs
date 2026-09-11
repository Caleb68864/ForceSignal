using System.Text.RegularExpressions;

namespace ForceSignal.Application.Tests;

/// <summary>
/// Every die named in either ground engine's own source, held to the content policy.
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
/// <b>It used to cover StarGrunt alone.</b> That module ships no table at all, so a die literal there
/// was a defect by its own contract; Dirtside was a different design, and the note at the foot of the
/// old file recorded why extending the guard over it would have meant "either breaking that module or
/// writing four exemptions that are not arguments". Dirtside's three die tables are now on
/// <c>DirtsideRulesProfile</c>, entered by the players, so the exemptions are not needed and the
/// guard covers both engines.
/// </para>
/// <para>
/// <b>There are two lists below, and the split is the honest part.</b> <c>NotARulesDie</c> is an
/// argument that a die is not a rules number, and it is empty. <c>NotYetThePlayers</c> is a record of
/// one that is - StarGrunt's band-to-rung walk, which widening the scan to cover tables written as
/// arithmetic uncovered, and which needs a StarGrunt profile rather than a field. Putting it in the
/// first list would have made it look settled; leaving it out would have made this guard fail on a
/// shipping tree and taught the next person to narrow the scan.
/// </para>
/// <para>
/// <b>What it does not cover, and why that is not a hole.</b> The shared <c>GroundCombat</c> project
/// is not scanned: <c>QualityDie.cs</c> declares the ladder - which dice exist and in what order -
/// and declaring that a d8 is a thing is not the same act as deciding a d8 is what a superior sight
/// rolls. Nothing in that project chooses a die; every choice is made in one of the two modules
/// scanned here. If a rule ever moves down into it, this comment is wrong and the scan should follow.
/// </para>
/// <para>
/// It names no bad value. It counts every <c>QualityDie.Dn</c> and every direct index into the
/// ladder, and requires each file and die to carry a written exemption with the count it is allowed.
/// A new one, an extra one, or a changed one fails the suite until somebody classifies it - which is
/// the only kind of guard that catches the next one rather than the last one.
/// </para>
/// </remarks>
public sealed partial class EngineDiceContentPolicyTests
{
    /// <summary>Every mention of a rung of the quality ladder, by name.</summary>
    [GeneratedRegex(@"QualityDie\.D\d+")]
    private static partial Regex DieLiteral();

    /// <summary>
    /// Every direct index into the ladder, which is a die chosen without naming one.
    /// </summary>
    /// <remarks>
    /// Added when Dirtside came under this guard, because its signature table was written as
    /// <c>QualityDice.Ladder[5 - signature]</c> - a complete die table, expressed as arithmetic, and
    /// invisible to a scan that only looks for <c>QualityDie.Dn</c>. A guard that can be walked
    /// around by subtraction is not a guard.
    /// </remarks>
    [GeneratedRegex(@"QualityDice\.Ladder\s*\[")]
    private static partial Regex LadderIndex();

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

    /// <summary>The two engines this guard reads, relative to the checkout.</summary>
    private static readonly string[] Modules =
        ["ForceSignal.Modules.StarGrunt", "ForceSignal.Modules.Dirtside"];

    /// <summary>
    /// The only dice the engines may name in their own source, with how many of each and why.
    /// </summary>
    /// <remarks>
    /// Empty, and that is the point: every occurrence that was here has been removed rather than
    /// argued for. An entry added tomorrow has to say where the number came from if not a record
    /// card, and the count keeps the argument honest - "this one placeholder" cannot quietly become
    /// four. An entry here is a claim that the die is <b>not</b> a rules number; if that claim
    /// cannot be made honestly the line belongs in <see cref="NotYetThePlayers"/> instead.
    /// </remarks>
    private static readonly Dictionary<string, (int Count, string Reason)> NotARulesDie =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Dice that <b>are</b> the player's and have not been moved yet, each with where it is and what
    /// moving it would cost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second list, kept apart from <see cref="NotARulesDie"/> on purpose. The rule this project
    /// works to is that "an exemption list that launders a violation is worse than no list" - so a
    /// violation that is being carried rather than argued away does not get to sit in a list called
    /// <i>not a rules die</i> and look settled. It sits here, where the name says what it is.
    /// </para>
    /// <para>
    /// The count is what keeps this honest in both directions: an entry cannot quietly grow, and
    /// <see cref="EveryExemptionNamesADieThatIsStillThere"/> fails once the line is finally fixed,
    /// which is what makes the debt come off the list instead of outliving it.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, (int Count, string Reason)> NotYetThePlayers =
        new(StringComparer.Ordinal)
        {
            // StarGrunt reads the target's range die by walking up the ladder one rung per band
            // out, starting at the bottom: `QualityDice.Ladder[bandsOut - 1 + posture.Shifts]`.
            // That the walk is one rung per band, and that it starts at the bottom, are readings
            // off the rulebook exactly as Dirtside's three tables were - and the ladder's length
            // then sets the maximum effective range, so it is load-bearing rather than incidental.
            //
            // It is here and not fixed because this pass was scoped to Dirtside's tables, and
            // because moving it means giving StarGrunt a rules profile of its own: the band-to-rung
            // walk, the band width in inches, and the posture shifts all come off the same page and
            // splitting one out would leave a half-entered table, which this project has already
            // established is worse than an empty one. Recorded in the roadmap under the same name.
            ["RangeBands.cs:Ladder[]"] =
                (1, "StarGrunt's band-to-rung walk: a real rules table, scoped out of the Dirtside "
                    + "pass and needing a StarGrunt profile rather than a single field"),
        };

    /// <summary>Both lists together, which is what the scan is actually allowed to find.</summary>
    private static Dictionary<string, (int Count, string Reason)> Allowed() =>
        NotARulesDie.Concat(NotYetThePlayers).ToDictionary(StringComparer.Ordinal);

    [Fact]
    public void TheEnginesNameNoDieOfTheirOwn()
    {
        var (files, found) = DiceInTheModules();

        // Reached-the-subject: the walk really found both modules' source. A scan that matched no
        // files would report no dice and pass, proving nothing whatever - which is exactly how a
        // probe in this project's history died on a wrong glob key and still claimed a clean run.
        Assert.True(files > 40, $"only {files} source files were scanned; the modules were not found");

        var allowedNow = Allowed();
        var shipped = found
            .Where(entry => !allowedNow.TryGetValue(entry.Key, out var allowed) || allowed.Count != entry.Value)
            .Select(entry => $"{entry.Key} x{entry.Value}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([], shipped);
    }

    [Fact]
    public void BothModulesAreReallyBeingRead()
    {
        // The other half of reached-the-subject, and the half the file count cannot give: forty
        // files is forty files whether they came from one module or two, so a scan that lost
        // Dirtside to a renamed directory would still clear the count above. Each module is asked
        // for its own files by name.
        foreach (var module in Modules)
        {
            var directory = Path.Combine(RepositoryRoot(), "src", module);
            Assert.True(Directory.Exists(directory), $"{module} is not at {directory}");
            Assert.True(SourceFiles(directory).Count > 10, $"{module} yielded almost no source files");
        }
    }

    [Fact]
    public void TheScanCanSeeADieWhenThereIsOneToSee()
    {
        // The control that must fail, and without it the check above is indistinguishable from a
        // scan that reads nothing. A file holding a die literal is scanned the same way the modules
        // are, and has to come back holding it.
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
                var sneaky = QualityDice.Ladder[5 - signature];
                """);

            var found = DiceIn(planted);

            Assert.Equal(2, found["Invented.cs:D6"]);
            // The table written as arithmetic is caught too, which is the hole Dirtside's signature
            // ladder would have gone through.
            Assert.Equal(1, found["Invented.cs:Ladder[]"]);
            // The rest of the control: the three in prose are read past, so the guard does not teach
            // people to stop writing down what a number used to be.
            Assert.Equal(
                ["Invented.cs:D6", "Invented.cs:Ladder[]"],
                found.Keys.Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            Directory.Delete(planted, recursive: true);
        }
    }

    [Fact]
    public void TheTwoListsDoNotOverlap()
    {
        // A line in both lists would be claiming to be not-a-rules-die and to be an unpaid rules
        // die at once, and whichever list the next reader opened would be the one they believed.
        Assert.Equal([], NotARulesDie.Keys.Intersect(NotYetThePlayers.Keys, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void EveryExemptionNamesADieThatIsStillThere()
    {
        // The other half of the list not rotting: an exemption for a line somebody has since
        // deleted is an argument nobody is making, and a list of those would let the next real one
        // through under a familiar-looking name. Vacuously true while the list is empty, and it
        // stops being vacuous the moment anybody adds to it.
        var (_, found) = DiceInTheModules();

        // Both lists, because a stale entry in either is an argument nobody is making any more -
        // and a stale entry in the debt list is worse, because it is a debt somebody has already
        // paid and the list is still telling the next reader it is outstanding.
        foreach (var (key, allowed) in Allowed())
        {
            Assert.True(found.ContainsKey(key), $"{key} is exempted and is no longer in the source");
            Assert.Equal(allowed.Count, found[key]);
            Assert.False(string.IsNullOrWhiteSpace(allowed.Reason), $"{key} is exempted with no reason");
        }
    }

    private static (int Files, Dictionary<string, int> Found) DiceInTheModules()
    {
        var files = 0;
        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var module in Modules)
        {
            var directory = Path.Combine(RepositoryRoot(), "src", module);
            Assert.True(Directory.Exists(directory), $"{module} is not at {directory}");
            files += SourceFiles(directory).Count;
            foreach (var (key, count) in DiceIn(directory))
            {
                found[key] = found.GetValueOrDefault(key) + count;
            }
        }

        return (files, found);
    }

    /// <summary>Every die chosen under a directory, keyed by file and by how it was chosen.</summary>
    private static Dictionary<string, int> DiceIn(string directory)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(directory))
        {
            var code = Comment().Replace(File.ReadAllText(file), string.Empty);
            var name = Path.GetFileName(file);
            foreach (Match match in DieLiteral().Matches(code))
            {
                Count(found, $"{name}:{match.Value.Split('.')[1]}");
            }

            foreach (var _ in LadderIndex().Matches(code))
            {
                Count(found, $"{name}:Ladder[]");
            }
        }

        return found;
    }

    private static void Count(Dictionary<string, int> found, string key) =>
        found[key] = found.GetValueOrDefault(key) + 1;

    /// <summary>A module's own sources, without the build output it also sits on top of.</summary>
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
