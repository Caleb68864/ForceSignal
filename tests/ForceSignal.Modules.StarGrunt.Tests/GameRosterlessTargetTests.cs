using System.Text.Json;
using System.Text.Json.Nodes;
using ForceSignal.Modules.GroundCombat.Dice;
using ForceSignal.Modules.GroundCombat.Sequence;
using ForceSignal.Modules.StarGrunt.Game;

namespace ForceSignal.Modules.StarGrunt.Tests;

/// <summary>
/// What happens when the unit being shot at has no roster to take the fire on.
/// </summary>
/// <remarks>
/// <para>
/// <c>Fire</c> read the target's armour as
/// <c>target.Figures.IsDefaultOrEmpty ? QualityDie.D6 : target.Figures[0].ArmourDie</c>. That D6 is
/// the die the whole resolution turns on - it is what the impact die is thrown against, so it
/// decides whether each hit is a kill, a wound or nothing at all - and it came off no record card.
/// </para>
/// <para>
/// It is not dead code. A saved game is a surrogate carrying the unit definitions and the unit
/// statuses as two separate lists, so a blob written before <c>Figures</c> was on
/// <c>UnitDefinition</c> restores with the roster empty and the figure count intact: the unit is on
/// the table, is not wiped out, and has no armour anywhere. The fixture below is exactly that -
/// this repository's own save format with the <c>Figures</c> property deleted from the bytes, which
/// is the only way to show what a missing property does. Taking the property off a C# object would
/// not: the deserialiser fills a missing one from the type, and that is the whole mechanism.
/// </para>
/// </remarks>
public sealed class GameRosterlessTargetTests
{
    /// <summary>The two-squad game as it would come back from a server that never stored rosters.</summary>
    private static StarGruntGame FromASaveWithNoRosters()
    {
        var saved = JsonNode.Parse(StarGruntGameSerialization.Save(GameFixtures.TwoSquadGame()))!;
        var units = saved["Units"]!.AsArray();

        // Reached-the-subject: the property this fixture is about really is in the bytes, on every
        // unit, before anything is removed. A renamed property would otherwise leave the blob
        // untouched and every assertion below would be about a game with a perfectly good roster.
        Assert.Equal(2, units.Count);
        foreach (var unit in units)
        {
            Assert.NotNull(unit!["Figures"]);
            unit.AsObject().Remove("Figures");
        }

        return StarGruntGameSerialization.Restore(saved.ToJsonString(new JsonSerializerOptions()));
    }

    [Fact]
    public void ASaveWithNoRostersRestoresAUnitThatIsOnTheTableWithNoArmour()
    {
        var game = FromASaveWithNoRosters();

        // The premise, asserted rather than assumed: the target is present, has no figures on its
        // definition at all, and is not wiped out - so `Fire` reaches the armour line.
        Assert.True(game.Unit(GameFixtures.Bravo).Figures.IsDefaultOrEmpty);
        Assert.Equal(8, game.Status(GameFixtures.Bravo).FiguresAlive);
        Assert.False(game.Status(GameFixtures.Bravo).IsWipedOut);
    }

    [Fact]
    public void ShootingAtAUnitWithNoArmourDieIsRefusedByName()
    {
        var game = FromASaveWithNoRosters()
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;

        var outcome = game.Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.False(outcome.IsAllowed);
        Assert.Contains("Bravo Squad", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsRolledAndNothingIsSpent()
    {
        // Refused before the step is taken, like every other refusal in Fire. A die source with an
        // empty script throws the moment it is asked for a roll, so this is the assertion that the
        // refusal happens before any dice are thrown rather than after.
        var game = FromASaveWithNoRosters()
            .BeginTurn().Value!
            .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
            .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!;

        var outcome = game.Fire(GameFixtures.Volley(), new ScriptedDice(), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.False(outcome.IsAllowed);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public void ASaveThatDoesNotSayWhatQualityAUnitWasIsRefused()
    {
        // The same mechanism one property along, and the sharper one: `UnitDefinition.QualityDie`
        // opened on D8, so a blob that did not carry it restored with average troops nobody had
        // rated - and that die is both the firer's own throw and how far it shoots. There is no
        // unentered rung to fall back to, so the blob is refused rather than filled in.
        var saved = JsonNode.Parse(StarGruntGameSerialization.Save(GameFixtures.TwoSquadGame()))!;
        var units = saved["Units"]!.AsArray();

        // Reached-the-subject: the property is in the bytes before it is taken out.
        Assert.NotNull(units[0]!["QualityDie"]);
        units[0]!.AsObject().Remove("QualityDie");

        var refused = Assert.Throws<ArgumentException>(
            () => StarGruntGameSerialization.Restore(saved.ToJsonString(new JsonSerializerOptions())));
        Assert.Contains("QualityDie", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASaveThatDoesSayComesBackWithTheDieItNamed()
    {
        // The control that must be accepted. A restore that refused every blob would pass the check
        // above and lose every game on the machine.
        var game = GameFixtures.TwoSquadGame()
            .WithUnit(GameFixtures.Squad(new UnitId("charlie"), "Charlie Squad", GameFixtures.Blue) with
            {
                QualityDie = QualityDie.D12,
            });

        var restored = StarGruntGameSerialization.Restore(StarGruntGameSerialization.Save(game));

        Assert.Equal(QualityDie.D12, restored.Unit(new UnitId("charlie")).QualityDie);
        Assert.Equal(game, restored);
    }

    [Fact]
    public void AUnitWhoseRosterIsThereIsStillShotAt()
    {
        // The control that must be accepted. A refusal that refused every volley would pass every
        // check above and make the game unplayable, which is the same policy violation in a coat.
        var outcome = GameFixtures.Firefight()
            .Fire(GameFixtures.Volley(), new ScriptedDice(GameFixtures.AKillAndAStop), TestRangeTable.Invented, GameFixtures.HitsARifleman());

        Assert.True(outcome.IsAllowed);
        Assert.Equal(7, outcome.Value!.Status(GameFixtures.Bravo).FiguresAlive);
    }

    [Fact]
    public void TheArmourThrownIsTheOneOnTheRoster()
    {
        // The other half of the control, and the reason the D6 mattered. The fixture's figures are
        // on D6, which is the very die the fallback chose, so nothing built on that fixture could
        // tell the two apart - which is how the fallback survived a suite of 275 tests.
        //
        // Asserted on the dice actually asked for rather than on the casualties, because a scripted
        // roller hands back the same numbers whatever it is handed: a change of armour can leave
        // the body count identical and still be a different game.
        static IReadOnlyList<QualityDie> DiceThrownAgainst(QualityDie armour)
        {
            var roller = new RecordingDice(GameFixtures.AKillAndAStop);
            StarGruntGame.Create("Hill 43")
                .WithUnit(GameFixtures.Squad(GameFixtures.Alpha, "Alpha Squad", GameFixtures.Blue))
                .WithUnit(GameFixtures.Squad(GameFixtures.Bravo, "Bravo Squad", GameFixtures.Red) with
                {
                    Figures = [.. Enumerable.Repeat(new FigureProfile(armour), 8)],
                })
                .BeginTurn().Value!
                .ChooseFirstActivator(GameFixtures.Blue, takeIt: true).Value!
                .BeginActivation(GameFixtures.Blue, GameFixtures.Alpha).Value!
                .Fire(GameFixtures.Volley(), roller, TestRangeTable.Invented, GameFixtures.HitsARifleman());

            return roller.Thrown;
        }

        var soft = DiceThrownAgainst(QualityDie.D4);
        var hard = DiceThrownAgainst(QualityDie.D12);

        // Reached-the-subject: both volleys really were resolved and really rolled dice.
        Assert.NotEmpty(soft);
        Assert.NotEqual(soft, hard);
    }

    /// <summary>A scripted die source that also remembers which dice it was handed.</summary>
    private sealed class RecordingDice(params int[] rolls) : IQualityDiceRoller
    {
        private readonly Queue<int> _rolls = new(rolls);
        private readonly List<QualityDie> _thrown = [];

        public IReadOnlyList<QualityDie> Thrown => _thrown;

        public int Roll(QualityDie die)
        {
            _thrown.Add(die);
            return _rolls.Count > 0 ? _rolls.Dequeue() : 1;
        }
    }
}
