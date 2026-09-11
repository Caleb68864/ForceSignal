using ForceSignal.Modules.GroundCombat.Dice;

namespace ForceSignal.Modules.StarGrunt.Sequence;

/// <summary>
/// A unit's place in the chain of command.
/// </summary>
/// <remarks>
/// The values ascend so that "senior to" is a comparison. Levels above company are here because
/// off-table support is organised at them, not because anything at that level ever stands on the
/// table.
/// </remarks>
public enum CommandLevel
{
    /// <summary>The fighting unit.</summary>
    Squad = 0,

    /// <summary>A handful of squads and their commander.</summary>
    Platoon = 1,

    /// <summary>Several platoons. Usually the top of anything present on the table.</summary>
    Company = 2,

    /// <summary>Where off-table support usually lives.</summary>
    Battalion = 3,

    /// <summary>Higher support still.</summary>
    Regiment = 4,
}

/// <summary>
/// Talking down the chain of command.
/// </summary>
/// <remarks>
/// The design here is that talking along the chain is easy and skipping links is progressively
/// harder - never forbidden. So this counts what is being skipped and leaves the caller to apply it;
/// refusing a bypass outright would be a stricter game than the one in the book.
/// </remarks>
public static class CommandChain
{
    /// <summary>True when the message is going down the chain rather than up or sideways.</summary>
    /// <param name="sender">The level speaking.</param>
    /// <param name="receiver">The level being spoken to.</param>
    /// <returns>True when the sender is senior to the receiver.</returns>
    /// <remarks>
    /// Transferring an activation only ever goes down. A subordinate cannot hand his commander an
    /// extra go, and two squads cannot hand each other one back and forth - which is one of the three
    /// things that bound how deep nesting can get.
    /// </remarks>
    public static bool IsDownward(CommandLevel sender, CommandLevel receiver) => sender > receiver;

    /// <summary>
    /// How many command levels the message jumps over, counting only levels that are actually
    /// represented on the table.
    /// </summary>
    /// <param name="sender">The level speaking.</param>
    /// <param name="receiver">The level being spoken to.</param>
    /// <param name="onTable">The levels present in this game.</param>
    /// <returns>The number of intervening levels that exist and are being skipped.</returns>
    /// <remarks>
    /// Levels nobody has brought are not obstacles. A force built as a company of squads with no
    /// platoon commanders on the table has nothing between the two, so the company commander talks to
    /// a squad at no penalty at all - which is the difference between modelling the ladder and
    /// modelling the gaps in it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="onTable"/> is null.</exception>
    public static int LevelsBypassed(CommandLevel sender, CommandLevel receiver, IReadOnlySet<CommandLevel> onTable)
    {
        ArgumentNullException.ThrowIfNull(onTable);

        var (low, high) = sender < receiver ? (sender, receiver) : (receiver, sender);
        return Enum.GetValues<CommandLevel>()
            .Count(level => level > low && level < high && onTable.Contains(level));
    }

    /// <summary>
    /// The die a communication is rolled on after the chain has taken its toll.
    /// </summary>
    /// <param name="senderQuality">The sending unit's quality die.</param>
    /// <param name="levelsBypassed">Levels skipped, from <see cref="LevelsBypassed"/>.</param>
    /// <param name="rungsPerLevel">
    /// What each skipped level costs the sender, in rungs, off the players' own rules.
    /// </param>
    /// <returns>The die to roll.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rungsPerLevel"/> is negative.</exception>
    /// <remarks>
    /// <para>
    /// A shift rather than a modifier, as everywhere else in this game, and a closed one - a message
    /// that gets harder than the bottom of the ladder is simply very unlikely, not impossible in a way
    /// that would feed back onto the listener.
    /// </para>
    /// <para>
    /// The cost per level is a parameter. It was one rung, written as <c>-levelsBypassed</c> - a walk
    /// of one rung per step, the same shape as the range walk that just left this module. Nothing
    /// outside the tests calls this yet, so the number is the caller's to pass rather than a field on
    /// a profile no screen could fill in: the precedent Dirtside's rider check set.
    /// </para>
    /// </remarks>
    public static QualityDie CommunicationDie(QualityDie senderQuality, int levelsBypassed, int rungsPerLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rungsPerLevel);
        return QualityDice.ShiftClosed(senderQuality, -Math.Max(0, levelsBypassed) * rungsPerLevel);
    }
}
