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
/// <b>What it counts, and why it counts that much.</b> It began by counting <c>QualityDie.Dn</c>,
/// then learned to count a direct index into the ladder when Dirtside's signature table turned out to
/// be written as <c>QualityDice.Ladder[5 - signature]</c>. Neither could see StarGrunt's range page,
/// which was three rules written as arithmetic on the ladder without naming a die or indexing it the
/// obvious way: a band was <c>QualityDice.Faces(quality)</c> inches wide, the reach was
/// <c>QualityDice.Ladder.Count</c> bands, and each cover level was worth its own enum value in rungs.
/// A guard that looks for a particular spelling of a die catches the last violation and not the next.
/// </para>
/// <para>
/// So it now counts every way a module's code can reach the ladder at all: a die named, the ladder
/// touched in any form, any call on <c>QualityDice</c> - every shift, every face count, every rung -
/// an integer cast to a die, the dice enumerated, and a <c>using</c> that would let any of those be
/// written without the name this scan looks for. Each occurrence has to be classified, with a count,
/// in one of the two lists below. Most calls are innocent - a shift by rungs the player entered is
/// the procedure this project owns - but "most are innocent" is exactly the argument that has to be
/// written down per call, because the one that is not looks the same from a distance.
/// </para>
/// <para>
/// <b>There are two lists below, and the split is the honest part.</b> <c>NotARulesDie</c> is an
/// argument that a use of the ladder does not carry a rules number. <c>NotYetThePlayers</c> is a
/// record of one that does and has not been moved - putting it in the first list would make it look
/// settled, and leaving it out would make this guard fail on a shipping tree and teach the next person
/// to narrow the scan.
/// </para>
/// <para>
/// <b>What it does not cover, and why that is not a hole.</b> The shared <c>GroundCombat</c> project
/// is not scanned: <c>QualityDie.cs</c> declares the ladder - which dice exist and in what order -
/// and declaring that a d8 is a thing is not the same act as deciding a d8 is what a superior sight
/// rolls. Nothing in that project chooses a die; every choice is made in one of the two modules
/// scanned here. If a rule ever moves down into it, this comment is wrong and the scan should follow.
/// </para>
/// <para>
/// <b>What it cannot see, which is a hole.</b> <c>QualityDie</c> is an enum whose values are face counts, and C# lets an enum take an integer added to it without a
/// cast: <c>die + 2</c> is a D10 when <c>die</c> is a D8, and no text scan can tell that expression
/// from arithmetic on a number without knowing the types. Nothing in either module does it today.
/// Closing it would need a compiler-backed analyser rather than this file.
/// </para>
/// </remarks>
public sealed partial class EngineDiceContentPolicyTests
{
    /// <summary>Every mention of a rung of the quality ladder, by name.</summary>
    [GeneratedRegex(@"QualityDie\.D\d+")]
    private static partial Regex DieLiteral();

    /// <summary>
    /// Every use of the ladder itself: an index, its length, an enumeration of it.
    /// </summary>
    /// <remarks>
    /// This was <c>Ladder\s*\[</c>, added when Dirtside's signature table was written as an index.
    /// It could not see <c>QualityDice.Ladder.Count</c>, which is how StarGrunt's reach was worked
    /// out: the ladder's length was the number of bands a rifle carried, a rules number that happened
    /// to be spelled as a property.
    /// </remarks>
    [GeneratedRegex(@"QualityDice\.Ladder\b")]
    private static partial Regex LadderUse();

    /// <summary>
    /// Every call on the ladder's arithmetic: shifts, face counts, rungs, and anything added later.
    /// </summary>
    /// <remarks>
    /// Any method, not a list of the ones that exist today, so a helper added to <c>QualityDice</c>
    /// next year is counted the day it is first called rather than the day somebody remembers to add
    /// it here. <c>QualityDice.Faces(quality)</c> was a band width; a shift by a literal is a table of
    /// one row; both look like plumbing.
    /// </remarks>
    [GeneratedRegex(@"QualityDice\.(?<call>\w+)\s*\(")]
    private static partial Regex LadderCall();

    /// <summary>An integer turned into a die, which picks a rung without naming one.</summary>
    /// <remarks>
    /// Not the parentheses of <c>typeof</c>, <c>nameof</c> or <c>default</c>, which name the type
    /// rather than convert anything to it; the first is counted below as an enumeration instead.
    /// </remarks>
    [GeneratedRegex(@"(?<!\b(?:typeof|nameof|default|sizeof)\s*)\(\s*QualityDie\s*\)")]
    private static partial Regex DieCast();

    /// <summary>The dice enumerated, which picks from the ladder by position.</summary>
    [GeneratedRegex(@"Enum\.\w+\s*<\s*QualityDie\s*>|typeof\s*\(\s*QualityDie\s*\)")]
    private static partial Regex DieEnumeration();

    /// <summary>
    /// A <c>using static</c> or an alias for the ladder's types, either of which lets every shape
    /// above be written without the name this scan keys on.
    /// </summary>
    [GeneratedRegex(@"using\s+(?:static\s+)?(?:\w+\s*=\s*)?[\w.]*\bQualityDi(?:ce|e)\s*;")]
    private static partial Regex LadderAlias();

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

    /// <summary>The two engines this guard reads, relative to the checkout, and the name each is keyed by.</summary>
    /// <remarks>
    /// Keyed by module and path rather than by file name. Both modules have a <c>CloseAssault.cs</c>,
    /// and a key of the file name alone added the two together - so an exemption written for one
    /// would have covered a new use in the other as long as the old one was deleted in the same change.
    /// </remarks>
    private static readonly (string Directory, string Key)[] Modules =
    [
        ("ForceSignal.Modules.StarGrunt", "StarGrunt"),
        ("ForceSignal.Modules.Dirtside", "Dirtside"),
    ];

    /// <summary>
    /// Every use of the ladder the engines may make in their own source, with how many and why none of
    /// them carries a rules number.
    /// </summary>
    /// <remarks>
    /// An entry here is a claim that the die and every rung it moves came from the player - off a
    /// record card, off the profile, or off a request - and that what the line owns is the procedure:
    /// which die moves, which way, and what happens when the ladder runs out. If that claim cannot be
    /// made honestly the line belongs in <see cref="NotYetThePlayers"/> instead. The count keeps the
    /// argument honest: "this one shift" cannot quietly become two.
    /// </remarks>
    private static readonly Dictionary<string, (int Count, string Reason)> NotARulesDie =
        new(StringComparer.Ordinal)
        {
            ["StarGrunt/Combat/RangeBands.cs:Shift()"] =
                (1, "the target's range die and the rungs its cover and posture move it are both read "
                    + "off the players' StarGruntRulesProfile; the line owns only that cover moves the "
                    + "die up and that running off the top of the ladder is no effective shot"),
            ["StarGrunt/Combat/FireCombat.cs:Faces()"] =
                (1, "the divisor is the type of a range die the players' profile chose; dividing the fire "
                    + "total by the die's type rather than its roll is the procedure"),
            ["StarGrunt/Combat/FireCombat.cs:ShiftOpposed()"] =
                (1, "impact and armour are off the record card and the rungs off the profile's cover "
                    + "shifts; the zero is 'the weapon is not shifted', and the open crossover is the "
                    + "procedure"),
        };

    /// <summary>
    /// Uses of the ladder that <b>do</b> carry a rules number and have not been moved yet, each with
    /// where it is and what moving it would cost.
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
            // Found by the widening that moved StarGrunt's range page onto its profile, and the same
            // shape as that page: a rule's size written as arithmetic on the ladder.
            //
            // Charging in cover is worth `CoverShift = 1` rung to the defender in the first round.
            // Its own comment argued it was "the one shift in a close combat this engine owns"; it is
            // a cover shift, the same kind of number as the three that just moved onto the profile,
            // and the old guard could not see it, which is the only reason that argument stood.
            ["StarGrunt/Assault/CloseAssault.cs:ShiftOpposed()"] =
                (1, "a defender in cover is worth CoverShift = 1 rung in the first round of a melee - a "
                    + "cover shift like the three on the range page, and still the engine's"),

            // Every command level skipped costs the message one rung: `-levelsBypassed`. A walk of one
            // rung per level, the same shape as the range walk. No production caller today.
            ["StarGrunt/Sequence/CommandLevel.cs:ShiftClosed()"] =
                (1, "a message loses one rung per command level it skips - a walk of one rung per step, "
                    + "the same shape as the range walk"),

            // Dirtside is out of scope for this pass (its die tables were settled the pass before),
            // and these two are recorded rather than touched. They are the same shape the StarGrunt
            // range walk was: the size of a shift written into the engine.
            //
            // `(int)band + (firerMovedOverHalf ? -1 : 0)`: the WeaponRangeBand enum's values ARE the
            // table - close +1, medium 0, long -1 - so the firer's die walks one rung per band, and a
            // hurried shot costs one more. The player enters the medium-range die; the walk from it is
            // still this app's.
            ["Dirtside/Combat/HitResolution.cs:Shift()"] =
                (1, "the firer's die walks one rung per range band (the enum values close +1, medium 0, "
                    + "long -1) and one more for moving over half - shift sizes still written in the engine"),

            // `underFire ? ShiftClosed(quality, -1) : quality`: an Under Fire marker costs one rung on
            // the fire-effectiveness check. No production caller today.
            ["Dirtside/Combat/InfantryCombat.cs:ShiftClosed()"] =
                (1, "an Under Fire marker costs one rung on the fire-effectiveness die - a shift size "
                    + "still written in the engine"),
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
        foreach (var (module, _) in Modules)
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
        var found = Scan(
            """
            // A comment about QualityDie.D12, which is prose and not a decision.
            /// <summary>So is <c>QualityDie.D10</c>.</summary>
            var armour = QualityDie.D6;
            var other = QualityDie.D6; /* and QualityDie.D4 in a block */
            var sneaky = QualityDice.Ladder[5 - signature];
            """);

        Assert.Equal(2, found["Invented.cs:D6"]);
        // The table written as arithmetic is caught too, which is the hole Dirtside's signature
        // ladder would have gone through.
        Assert.Equal(1, found["Invented.cs:Ladder"]);
        // The rest of the control: the three in prose are read past, so the guard does not teach
        // people to stop writing down what a number used to be.
        Assert.Equal(
            ["Invented.cs:D6", "Invented.cs:Ladder"],
            found.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void TheDullestShiftIsCaught()
    {
        // The shape this pass widened the scan for, at its most boring: a die moved one rung by a
        // literal. It names no die and indexes nothing, so the scan before this pass read straight
        // past it - and it is a complete rules table of one row.
        var found = Scan("var die = QualityDice.ShiftClosed(quality, 1);");

        Assert.Equal(["Invented.cs:ShiftClosed()"], found.Keys.ToArray());
    }

    [Fact]
    public void TheRangePageAsItWasWrittenIsCaughtLineByLine()
    {
        // The three rules StarGrunt's range page used to carry, in the spellings it used, so that
        // putting any one of them back fails this guard rather than only the one the old scan knew.
        var found = Scan(
            """
            public static int BandInches(QualityDie firerQuality) => QualityDice.Faces(firerQuality);
            var bands = QualityDice.Ladder.Count - posture.Shifts;
            var walked = QualityDice.Shift(profile.RangeDie(1), bandsOut - 1);
            """);

        Assert.Equal(1, found["Invented.cs:Faces()"]);
        Assert.Equal(1, found["Invented.cs:Ladder"]);
        Assert.Equal(1, found["Invented.cs:Shift()"]);
    }

    [Fact]
    public void EveryOtherWayOntoTheLadderIsCaught()
    {
        // A die built from an integer, the dice enumerated, and the two usings that would let every
        // shape above be written without the name this scan keys on. None is in either module; each
        // would be a way round the scan if it were not counted.
        var found = Scan(
            """
            using static ForceSignal.Modules.GroundCombat.Dice.QualityDice;
            using Rungs = ForceSignal.Modules.GroundCombat.Dice.QualityDie;
            using ForceSignal.Modules.GroundCombat.Dice;
            var cast = (QualityDie)(2 * bands + 4);
            var every = Enum.GetValues<QualityDie>();
            var named = typeof(QualityDie);
            """);

        Assert.Equal(2, found["Invented.cs:using"]);
        Assert.Equal(1, found["Invented.cs:(QualityDie)"]);
        Assert.Equal(2, found["Invented.cs:Enum<QualityDie>"]);

        // The control: the ordinary namespace import every file carries is not a way round anything,
        // and a guard that counted it would be a guard with a hundred exemptions nobody reads.
        Assert.Equal(3, found.Keys.Count);
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
        // through under a familiar-looking name.
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

        foreach (var (module, key) in Modules)
        {
            var directory = Path.Combine(RepositoryRoot(), "src", module);
            Assert.True(Directory.Exists(directory), $"{module} is not at {directory}");
            files += SourceFiles(directory).Count;
            foreach (var (where, count) in DiceIn(directory))
            {
                var named = $"{key}/{where}";
                found[named] = found.GetValueOrDefault(named) + count;
            }
        }

        return (files, found);
    }

    /// <summary>Scans one invented file the way the modules are scanned, for the controls.</summary>
    private static Dictionary<string, int> Scan(string source)
    {
        var planted = Path.Combine(Path.GetTempPath(), $"forcesignal-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(planted);
        try
        {
            File.WriteAllText(Path.Combine(planted, "Invented.cs"), source);
            return DiceIn(planted);
        }
        finally
        {
            Directory.Delete(planted, recursive: true);
        }
    }

    /// <summary>
    /// Every use of the ladder under a directory, keyed by the file's path below it and by how the
    /// ladder was reached.
    /// </summary>
    private static Dictionary<string, int> DiceIn(string directory)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(directory))
        {
            var code = Comment().Replace(File.ReadAllText(file), string.Empty);
            var name = Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/');

            foreach (Match match in DieLiteral().Matches(code))
            {
                Count(found, $"{name}:{match.Value.Split('.')[1]}");
            }

            foreach (var _ in LadderUse().Matches(code))
            {
                Count(found, $"{name}:Ladder");
            }

            foreach (Match match in LadderCall().Matches(code))
            {
                Count(found, $"{name}:{match.Groups["call"].Value}()");
            }

            foreach (var _ in DieCast().Matches(code))
            {
                Count(found, $"{name}:(QualityDie)");
            }

            foreach (var _ in DieEnumeration().Matches(code))
            {
                Count(found, $"{name}:Enum<QualityDie>");
            }

            foreach (var _ in LadderAlias().Matches(code))
            {
                Count(found, $"{name}:using");
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
