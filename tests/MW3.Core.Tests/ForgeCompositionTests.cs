using System.Text;

namespace MW3.Core.Tests;

/// <summary>
/// Phase 6 FR-3: the forge term composed into <see cref="CombatResolver"/>'s indices - identity when
/// nobody holds a forge, the first reachable arithmetic remainder (D-46), and the standing promise
/// that a match with no player-owned forge produces exactly the board state it produced before this
/// feature existed. See issue #87's acceptance criteria.
/// </summary>
public class ForgeCompositionTests
{
    // The six original slots, verbatim from the pre-FR-2 MapLayout - the map the pre-FR-3 baseline
    // below was captured against. Held here rather than sliced off MapLayout.Slots so a future map
    // change cannot silently move the thing this baseline is a baseline of.
    private static readonly MapSlot[] _sixOriginalSlots =
    {
        new(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.35, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.35, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.65, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.65, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
    };

    /// <summary>
    /// Identity survives the new term: at zero forges and morale level 0 on both sides, both indices
    /// are still exactly 10000 - the basis-point identity every phase since FR-3b has composed to.
    /// </summary>
    [Fact]
    public void AtZeroForgesAndMoraleZero_BothIndicesAreStillExactlyTenThousand()
    {
        var attackerIndex = CombatResolver.ComposeAttackerIndex(
            MoraleTable.AttackPercentage(MoraleTable.MinLevel),
            ForgeTable.AttackPercentage(ForgeTable.MinForgeCount));
        var defenderIndex = CombatResolver.ComposeDefenderIndex(
            LevelTable.Village.DefencePercentage(LevelTable.MinLevel),
            MoraleTable.DefencePercentage(MoraleTable.MinLevel),
            ForgeTable.DefencePercentage(ForgeTable.MinForgeCount));

        Assert.Equal(10000, attackerIndex);
        Assert.Equal(10000, defenderIndex);
    }

    /// <summary>
    /// <b>The first reachable remainder, pinned by name (D-46).</b> A level-2 village (110%) held at
    /// morale 1 (125%) by a player with three forges (145%) composes
    /// <c>110 × 125 × 145 = 1 993 750</c>, which divided by 100 is <c>19937.5</c> - the first
    /// three-term product in this codebase's history that does not divide evenly, unreachable before
    /// this feature because the forge term was pinned at identity.
    /// <para>
    /// <b>Truncation is kept, deliberately.</b> The discarded half-unit is under one basis point of a
    /// five-figure index - well under 0.003% - so no reachable exchange turns on it in any way a
    /// player could perceive. And because truncation only ever shrinks the index it is computed for,
    /// a truncated <i>defender</i> index biases a knife-edge exchange toward the attacker. Correcting
    /// that bias is explicitly out of scope for FR-3 and is not a defect; changing the rounding mode
    /// would move every composed index in the game at once.
    /// </para>
    /// </summary>
    [Fact]
    public void ComposeDefenderIndex_FirstReachableRemainder_TruncatesToward19937_BiasingTowardTheAttacker()
    {
        var index = CombatResolver.ComposeDefenderIndex(
            baseDefencePercent: 110,
            moraleDefencePercent: 125,
            forgeDefencePercent: 145);

        Assert.Equal(19937, index);

        // The direction of the loss, against the undiscarded value at twice the scale (39875 is
        // 19937.5 doubled): truncation shrinks the defender's index and never grows it, which is why
        // the bias runs toward the attacker.
        Assert.True(index * 2 < 39875, "truncation shrinks the defender's index, never grows it");
    }

    /// <summary>
    /// The forge term must be read from <see cref="ForgeTable"/> and not from
    /// <see cref="MoraleTable"/>. The product itself is commutative, so a positional swap of two
    /// arguments cannot be caught arithmetically - what a caller can get wrong is which <i>table</i>
    /// it reads, and at one forge and morale level 1 the two tables disagree sharply (150 against
    /// 105), so that mistake composes a visibly different index.
    /// </summary>
    [Fact]
    public void TheForgeTerm_ReadFromTheWrongTable_ComposesADifferentIndex()
    {
        var correct = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(1), ForgeTable.AttackPercentage(1));
        var wrongTable = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(1), MoraleTable.AttackPercentage(1));

        Assert.NotEqual(correct, wrongTable);
        Assert.True(correct > wrongTable);
    }

    /// <summary>
    /// RE-AUTHORED at phase 6 FR-6 (issue #93). This scenario was never actually forge-free by
    /// construction - "zero forge" was an observed fact of pre-FR-6 code, not a property of the
    /// scripted commands themselves. By tick 1320 the AI has captured enough neutrals to own four
    /// producers (bases 1, 2, 4, plus whichever of base 5 it was about to take), which is exactly
    /// <see cref="ForgeTable.ProducersPerForge"/> - so FR-6's ratio gate now fires on the very same
    /// clause-3 decision that used to build a tower there. Every other value - every other base's
    /// owner, garrison and level, the elapsed tick, the outcome, and both morale totals - is
    /// unchanged: this is the ratio gate choosing <see cref="BaseType.Forge"/> over
    /// <see cref="BaseType.Tower"/> at one single decision, not a wider behaviour change. See
    /// <c>AiForgeBrainTests</c> for the ratio gate's own dedicated coverage; what this test still
    /// protects is that nothing *else* about the forge composition term moves this scenario off the
    /// path it was already on.
    /// </summary>
    [Fact]
    public void ZeroForgeBaseline_OnTheSixOriginalBases_MatchesPreFr3ExceptTheOneBaseFr6NowBuildsAsAForge()
    {
        const string postFr6 =
            "Ticks=1320 Outcome=InProgress HM=0 AM=120 " +
            "[0:1,27,2,Producer] [1:2,7,1,Producer] [2:2,5,1,Producer] " +
            "[3:1,17,1,Producer] [4:2,6,1,Producer] [5:2,11,1,Forge]";

        var match = new Match(_sixOriginalSlots);
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        runner.Advance(120);
        runner.Execute(new SendArmyCommand(match.HumanPlayer, 0, 2, 6));
        runner.Advance(200);
        runner.Execute(new UpgradeCommand(match.HumanPlayer, 0));
        runner.Advance(400);
        runner.Execute(new SendArmyCommand(match.HumanPlayer, 0, 3, 9));
        runner.Advance(600);

        // The human never reaches four producers in this scenario, so the ratio gate never fires
        // for them; the AI does, exactly once, in place of the tower FR-7 used to build here.
        Assert.Equal(0, match.ForgeCountFor(match.HumanPlayer));
        Assert.Equal(1, match.ForgeCountFor(match.AiPlayer));

        Assert.Equal(postFr6, Snapshot(match));
    }

    private static string Snapshot(Match match)
    {
        var sb = new StringBuilder();
        sb.Append(FormattableString.Invariant(
            $"Ticks={match.ElapsedTicks} Outcome={match.Outcome} HM={match.HumanMorale.Points} AM={match.AiMorale.Points}"));

        foreach (var b in match.Bases)
        {
            var owner = b.Owner is Player o ? o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "n";
            sb.Append(FormattableString.Invariant($" [{b.Id}:{owner},{b.GarrisonCount},{b.Level},{b.Type}]"));
        }

        return sb.ToString();
    }
}
