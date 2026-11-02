using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForceSignal.Application.Tests;

/// <summary>
/// The half of the dice guard that needs the compiler: a die used as a number, in any spelling.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EngineDiceContentPolicyTests"/> reads text, and said on its own face what text cannot
/// do. <c>QualityDie</c> is an enum whose values are face counts, and C# lets an enum take an integer
/// without a cast, so <c>die + 2</c> is a D10 when <c>die</c> is a D8: a whole rules table of one row,
/// naming no die, calling nothing on the ladder, and indistinguishable from <c>count + 2</c> until
/// something knows what type <c>die</c> has.
/// </para>
/// <para>
/// So this compiles both modules from their own source and asks the semantic model. It is a test
/// rather than a build analyser because the rest of this guard is, and because a planted control
/// beside it is what shows it can see anything at all.
/// </para>
/// <para>
/// <b>What it refuses.</b> Arithmetic with a die as an operand, a compound assignment or increment on
/// one, and a conversion either way between a die and a number - the last whatever the type is called,
/// which is what an alias would otherwise walk round. Comparing two dice is not on the list: asking
/// which die is better chooses nothing. Ladder calls are not either; those are the text guard's, with
/// a count and a reason per line.
/// </para>
/// <para>
/// <b>What it still cannot see.</b> A die boxed or passed as <c>object</c> or <c>IConvertible</c> into
/// something like <c>Convert.ToInt32</c> carries no conversion at the call that names a number.
/// Nothing in either module does it.
/// </para>
/// </remarks>
public sealed class EngineDiceCompilerGuardTests
{
    private const string DieType = "ForceSignal.Modules.GroundCombat.Dice.QualityDie";

    /// <summary>The usings the SDK generates for a project with implicit usings on.</summary>
    private const string ImplicitUsings =
        """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    [Theory]
    [InlineData("ForceSignal.Modules.StarGrunt")]
    [InlineData("ForceSignal.Modules.Dirtside")]
    public void NoEngineTreatsADieAsANumber(string module)
    {
        var directory = Path.Combine(RepositoryRoot(), "src", module);
        var sources = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Path.GetRelativePath(directory, path), File.ReadAllText(path)))
            .ToList();

        // Reached-the-subject: a module that yielded nothing would report no dice and pass.
        Assert.True(sources.Count > 10, $"{module} yielded {sources.Count} source files");

        var uses = DieNumberUses(module, sources, out var diceSeen);

        // And the other half: the compilation really resolved the type this guard is about. Without
        // this, a renamed enum leaves every expression looking like plain arithmetic.
        Assert.True(diceSeen > 0, $"{module} compiled and no expression had the type {DieType}");
        Assert.Equal([], uses);
    }

    [Fact]
    public void TheShapeTheTextScanCouldNotSeeIsCaught()
    {
        var uses = Plant(
            """
            public static class Invented
            {
                public static QualityDie Better(QualityDie die) => die + 2;
            }
            """);

        Assert.Equal(["Invented.cs:4 arithmetic: die + 2"], uses);
    }

    [Fact]
    public void EverySpellingOfADieAsANumberIsCaught()
    {
        var uses = Plant(
            """
            using Rungs = ForceSignal.Modules.GroundCombat.Dice.QualityDie;
            public static class Invented
            {
                public static void Walk(QualityDie die, QualityDie? maybe, int rung)
                {
                    die += 2;
                    die++;
                    --die;
                    var inferred = die - 1;
                    var lifted = maybe + 1;
                    var picked = (Rungs)rung;
                    var named = (QualityDie)rung;
                    var counted = (int)die + rung;
                }
            }
            """);

        Assert.Equal(
            [
                "Invented.cs:7 arithmetic: die += 2",
                "Invented.cs:8 arithmetic: die++",
                "Invented.cs:9 arithmetic: --die",
                "Invented.cs:10 arithmetic: die - 1",
                "Invented.cs:11 arithmetic: maybe + 1",
                "Invented.cs:12 number to die: (Rungs)rung",
                "Invented.cs:13 number to die: (QualityDie)rung",
                "Invented.cs:14 die to number: (int)die",
            ],
            uses);
    }

    [Fact]
    public void WhatIsNotADieAsANumberIsLeftAlone()
    {
        // The controls that must stay quiet. A guard that fired on these would be argued with until
        // someone narrowed it, and the enum beside the die is the one a text match could not tell apart.
        var uses = Plant(
            """
            public enum Band { Close = 1, Medium = 0 }
            public static class Invented
            {
                public static int Quiet(QualityDie die, QualityDie other, Band band, int count)
                {
                    var same = die == other;
                    var better = die > other;
                    var shifted = count + 2;
                    var walked = band + 1;
                    var asked = (int)band;
                    var kept = (QualityDie)other;
                    return same && better ? shifted + asked + (int)walked : (int)count;
                }
            }
            """);

        Assert.Equal([], uses);
    }

    [Fact]
    public void ACompilationThatDoesNotBuildFailsRatherThanPasses()
    {
        // A semantic model over broken code answers "unknown type" for everything, which reads as
        // "no dice here". That is the quiet pass this guard must not have.
        var failure = Assert.ThrowsAny<Exception>(() =>
            DieNumberUses("Invented", [("Invented.cs", "public class C { Missing field; }")], out _));

        Assert.Contains("CS0246", failure.Message, StringComparison.Ordinal);
    }

    private static List<string> Plant(string source) =>
        DieNumberUses(
            "Invented",
            [("Invented.cs", "using ForceSignal.Modules.GroundCombat.Dice;\n" + source)],
            out _);

    /// <summary>Compiles a module's sources and lists every place a die is used as a number.</summary>
    private static List<string> DieNumberUses(
        string assemblyName,
        IReadOnlyList<(string Name, string Text)> sources,
        out int diceSeen)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.Text, options, source.Name))
            .Append(CSharpSyntaxTree.ParseText(ImplicitUsings, options, "ImplicitUsings.cs"))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            References(assemblyName),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"{assemblyName} did not compile, so nothing this guard read can be trusted:\n"
                + string.Join('\n', errors.Take(10)));
        }

        var uses = new List<string>();
        diceSeen = 0;
        foreach (var tree in trees)
        {
            var walker = new DieWalker(compilation.GetSemanticModel(tree), uses);
            walker.Visit(tree.GetRoot());
            diceSeen += walker.DiceSeen;
        }

        return uses;
    }

    /// <summary>What the test host can see, less the assembly being compiled from source.</summary>
    private static IEnumerable<MetadataReference> References(string assemblyName) =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !string.Equals(Path.GetFileNameWithoutExtension(path), assemblyName, StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path));

    private sealed class DieWalker(SemanticModel model, List<string> uses) : CSharpSyntaxWalker
    {
        public int DiceSeen { get; private set; }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (!IsComparisonOrLogic(node) && (IsDie(node.Left) || IsDie(node.Right)))
            {
                Report(node, "arithmetic");
            }

            base.VisitBinaryExpression(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && !node.IsKind(SyntaxKind.CoalesceAssignmentExpression)
                && IsDie(node.Left))
            {
                Report(node, "arithmetic");
            }

            base.VisitAssignmentExpression(node);
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            if (IsDie(node.Operand))
            {
                Report(node, "arithmetic");
            }

            base.VisitPrefixUnaryExpression(node);
        }

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.SuppressNullableWarningExpression) && IsDie(node.Operand))
            {
                Report(node, "arithmetic");
            }

            base.VisitPostfixUnaryExpression(node);
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            var target = model.GetTypeInfo(node.Type).Type;
            var from = IsDie(node.Expression);
            var to = IsDie(target);
            if (to && !from)
            {
                Report(node, "number to die");
            }
            else if (from && !to && IsNumeric(target))
            {
                Report(node, "die to number");
            }

            base.VisitCastExpression(node);
        }

        private static bool IsComparisonOrLogic(BinaryExpressionSyntax node) =>
            node.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
                or SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
                or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression
                or SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression
                or SyntaxKind.CoalesceExpression or SyntaxKind.IsExpression or SyntaxKind.AsExpression;

        private bool IsDie(ExpressionSyntax expression) => IsDie(model.GetTypeInfo(expression).Type);

        private static bool IsDie(ITypeSymbol? type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                type = nullable.TypeArguments[0];
            }

            return type?.ToDisplayString() == DieType;
        }

        // Every name that has the type, whatever is done with it: the count that shows the walk
        // reached the dice in a module where, correctly, nothing does arithmetic on one.
        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (IsDie(node))
            {
                DiceSeen++;
            }

            base.VisitIdentifierName(node);
        }

        private static bool IsNumeric(ITypeSymbol? type) =>
            (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                ? nullable.TypeArguments[0]
                : type)?.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte
                or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

        private void Report(SyntaxNode node, string what)
        {
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            uses.Add($"{node.SyntaxTree.FilePath}:{line} {what}: {node}");
        }
    }

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
