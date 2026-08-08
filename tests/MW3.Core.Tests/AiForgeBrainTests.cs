using System.Reflection;

namespace MW3.Core.Tests;

/// <summary>
/// Phase 6 FR-6 (issue #93, G-21): the AI opponent builds, contests, and defends forges. Clause 3
/// (<c>TryConvert</c>) now builds a forge before a tower whenever one is owed
/// (<see cref="ForgeTable.ProducersPerForge"/>), and clause 1 (<c>TryDefend</c>) now prefers a
/// threatened forge over any threatened non-forge. Clause 4 (<c>TryAttack</c>) needed no new
/// comparison key - FR-3 already put the forge term into <c>PredictedMoraleSwing</c> - so its own
/// test here only pins that the existing tiebreak still reads <see cref="Base.Type"/>. No source
/// describes how MW2's AI plays: every rule below is MW3's own original design, not a port.
/// </summary>
public class AiForgeBrainTests
{
    private static BrainDecision InvokeClause(string methodName, AiBrain brain, Match match, List<Base> ownBases)
    {
        var method = typeof(AiBrain).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (BrainDecision)method.Invoke(brain, new object[] { match, ownBases })!;
    }

    private static bool InvokeIsConvertCandidate(AiBrain brain, Match match, Base candidate)
    {
        var method = typeof(AiBrain).GetMethod("IsConvertCandidate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (bool)method.Invoke(brain, new object[] { match, candidate })!;
    }

    private static List<Base> OwnBases(Match match, Player player) =>
        match.Bases.Where(b => b.Owner == player).OrderBy(b => b.Id).ToList();

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    // --- Clause 3: building a forge ---

    [Fact]
    public void TryConvert_FourProducersAndNoForges_ConvertsOneToAForge()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1

        // Own four producers: aiBase plus the three flank neutrals (ids 2, 3, 4); id 5 stays neutral.
        foreach (var id in new[] { 2, 3, 4 })
        {
            SetOwner(match.Bases[id], ai);
            SetGarrison(match.Bases[id], LevelTable.ConversionCost);
        }

        SetGarrison(aiBase, LevelTable.ConversionCost);

        Assert.Equal(0, match.ForgeCountFor(ai));

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.True(decision.IsConvert);
        Assert.Equal(BaseType.Forge, decision.Convert.TargetType);
    }

    [Fact]
    public void TryConvert_ThreeProducersAndNoForges_DoesNotBuildAForge()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1

        // Own three producers: aiBase plus two flank neutrals (ids 2, 3).
        foreach (var id in new[] { 2, 3 })
        {
            SetOwner(match.Bases[id], ai);
            SetGarrison(match.Bases[id], LevelTable.ConversionCost);
        }

        SetGarrison(aiBase, LevelTable.ConversionCost);

        Assert.Equal(0, match.ForgeCountFor(ai));

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        // 0 < 3/4 (integer division: 0) is false - the ratio gate does not fire, so today's tower
        // conversion runs instead: a command may still issue, but never a Forge.
        if (decision.HasCommand)
        {
            Assert.True(decision.IsConvert);
            Assert.Equal(BaseType.Tower, decision.Convert.TargetType);
        }
    }

    [Fact]
    public void TryConvert_NoOscillation_AfterBuildingAForgeTheResultingRatioIsNotConvertingAgain()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1

        foreach (var id in new[] { 2, 3, 4 })
        {
            SetOwner(match.Bases[id], ai);
            SetGarrison(match.Bases[id], LevelTable.ConversionCost);
        }

        SetGarrison(aiBase, LevelTable.ConversionCost);

        var brain = new AiBrain(ai);
        var firstDecision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));
        Assert.True(firstDecision.HasCommand);
        Assert.Equal(BaseType.Forge, firstDecision.Convert.TargetType);

        Assert.Equal(ConvertOutcome.Accepted, match.Execute(firstDecision.Convert));
        match.Advance(LevelTable.ConversionBuildDurationTicks);

        // Exactly 3 producers and 1 forge now: 1 < 3/4 (0) is false - not a converting state.
        Assert.Equal(3, OwnBases(match, ai).Count(b => b.Type == BaseType.Producer));
        Assert.Equal(1, match.ForgeCountFor(ai));

        var secondDecision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));
        if (secondDecision.HasCommand)
        {
            Assert.True(secondDecision.IsConvert);
            Assert.Equal(BaseType.Tower, secondDecision.Convert.TargetType);
        }
    }

    [Fact]
    public void TryConvert_CapturedForgesCountTowardTheRatio_HoldingOneForgeAndFourProducersDoesNotBuildASecond()
    {
        var match = new Match(PhaseSixEightSlotFixture.Slots);
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var forge = match.Bases[6];

        SetOwner(forge, ai); // the shipped neutral forge, captured
        foreach (var id in new[] { 2, 3, 4 })
        {
            SetOwner(match.Bases[id], ai);
            SetGarrison(match.Bases[id], LevelTable.ConversionCost);
        }

        SetGarrison(aiBase, LevelTable.ConversionCost);

        Assert.Equal(1, match.ForgeCountFor(ai));

        var brain = new AiBrain(ai);
        // 1 < 4/4 (1) is false - a second forge is not owed.
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        if (decision.HasCommand)
        {
            Assert.True(decision.IsConvert);
            Assert.Equal(BaseType.Tower, decision.Convert.TargetType);
        }
    }

    /// <summary>
    /// An injected layout (D-44) where the rear-most convert candidate and the front are different
    /// own bases: the tower branch (<c>TryConvertToTower</c>) picks the front (nearest to the only
    /// not-owned base), the forge branch (<c>TryConvertToForge</c>) must pick the opposite end.
    /// </summary>
    [Fact]
    public void TryConvertToForge_AmongCandidates_PicksTheRearMost_WhereTheTowerBranchWouldPickTheFront()
    {
        var slots = new[]
        {
            new MapSlot(new MapPoint(0.05, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.95, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.70, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.75, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.80, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        };

        var match = new Match(slots);
        var ai = match.AiPlayer;

        // Own every base except the human's: ids 1 (AiStart, 0.95), 2 (0.70), 3 (0.75), 4 (0.80).
        // The only not-owned base is id 0 at 0.05, so distances are: id1=0.90, id2=0.65, id3=0.70,
        // id4=0.75. id1 is the rear-most (greatest distance); id2 is the front (nearest).
        foreach (var id in new[] { 2, 3, 4 })
        {
            SetOwner(match.Bases[id], ai);
            SetGarrison(match.Bases[id], LevelTable.ConversionCost);
        }

        SetGarrison(match.Bases[1], LevelTable.ConversionCost);

        var brain = new AiBrain(ai);
        var ownBases = OwnBases(match, ai);

        var forgeDecision = InvokeClause("TryConvertToForge", brain, match, ownBases);
        Assert.True(forgeDecision.HasCommand);
        Assert.Equal(1, forgeDecision.Convert.BaseId);
        Assert.Equal(BaseType.Forge, forgeDecision.Convert.TargetType);

        var towerDecision = InvokeClause("TryConvertToTower", brain, match, ownBases);
        Assert.True(towerDecision.HasCommand);
        Assert.Equal(2, towerDecision.Convert.BaseId);
        Assert.Equal(BaseType.Tower, towerDecision.Convert.TargetType);
    }

    [Fact]
    public void IsConvertCandidate_AnOwnedForge_IsNeverACandidate_EvenAtHighGarrison()
    {
        var match = new Match(PhaseSixEightSlotFixture.Slots);
        var ai = match.AiPlayer;
        var forge = match.Bases[6];
        SetOwner(forge, ai);
        SetGarrison(forge, 999);

        var brain = new AiBrain(ai);

        Assert.False(InvokeIsConvertCandidate(brain, match, forge));
    }

    // --- Clause 1: defending a forge ---

    [Fact]
    public void TryDefend_AThreatenedForge_IsPreferredOverAThreatenedNonForge_WhateverTheirIds()
    {
        var match = new Match(PhaseSixEightSlotFixture.Slots);
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var forge = match.Bases[6];
        var humanBase = match.Bases.Single(b => b.Owner == human);

        SetOwner(forge, ai);
        SetOwner(match.Bases[2], ai); // reinforcement source, close to both threatened bases
        SetGarrison(match.Bases[2], 100);
        SetGarrison(aiBase, 5);
        SetGarrison(forge, 5);
        SetGarrison(humanBase, 2000);

        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 1000));
        match.Execute(new SendArmyCommand(human, humanBase.Id, forge.Id, 1000));

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryDefend", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(forge.Id, decision.Command.TargetBaseId);
    }

    [Fact]
    public void TryDefend_TwoThreatenedNonForges_StillSelectsTheLowerId_ExactlyAsToday()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var humanBase = match.Bases.Single(b => b.Owner == human);

        SetOwner(match.Bases[2], ai);
        SetOwner(match.Bases[4], ai); // reinforcement source
        SetGarrison(match.Bases[4], 100);
        SetGarrison(aiBase, 5);
        SetGarrison(match.Bases[2], 5);
        SetGarrison(humanBase, 2000);

        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 1000));
        match.Execute(new SendArmyCommand(human, humanBase.Id, match.Bases[2].Id, 1000));

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryDefend", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(aiBase.Id, decision.Command.TargetBaseId); // id 1, lower than id 2
    }

    // --- Clause 4: contesting the neutral forge, no new comparison key ---

    /// <summary>
    /// AC (contesting): two winnable, untargeted candidates tied on zero expected tower loss - the
    /// shipped map's neutral forge (id 6) and a neutral level-1 producer (id 4) - are otherwise
    /// isolated from every other base on the map (driven to an unwinnable garrison) so the tiebreak
    /// between exactly these two is what decides the outcome. This must fail if the tiebreak stops
    /// reading <see cref="Base.Type"/>, since the forge and the producer would then compose the same
    /// predicted morale swing.
    /// </summary>
    [Fact]
    public void TryAttack_TiedOnExpectedTowerLoss_NeutralForgeVersusNeutralProducer_PrefersTheForge()
    {
        var match = new Match(PhaseSixEightSlotFixture.Slots);
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var forge = match.Bases[6];
        var neutral4 = match.Bases[4];

        foreach (var other in match.Bases.Where(b => b.Id != forge.Id && b.Id != neutral4.Id && b.Owner != ai))
        {
            SetGarrison(other, 1000);
        }

        SetGarrison(aiBase, 42); // unclampedHalf = 21, winnable against both (garrison 10 and 5)

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(forge.Id, decision.Command.TargetBaseId);
    }

    // --- Determinism and the zero-forge baseline ---

    /// <summary>
    /// <b>The zero-forge baseline is bit-identical.</b> An injected six-base layout (D-44) with only
    /// three <see cref="BaseType.Producer"/> slots on the whole map - the human start, the AI start,
    /// and one neutral - and three <see cref="BaseType.Tower"/> neutrals filling the rest. The AI's
    /// producer count is capped at three by <i>type</i>, permanently, however the match plays out,
    /// even in the limit where it captures every base on the map: <see cref="AiBrain"/> never
    /// converts a tower into a producer (no such command exists in <c>TryConvert</c>'s vocabulary -
    /// it only ever turns a producer into a forge or a tower), so there are simply never more than
    /// three producer-typed bases in existence to own. Three divided by
    /// <see cref="ForgeTable.ProducersPerForge"/> (integer division) is zero, so
    /// <c>ForgeCountFor(ai) &lt; producerCount / ProducersPerForge</c> can never hold - a type-level
    /// guarantee independent of ownership or outcome, not a lucky tick count that happens not to
    /// reach it (contrast <c>ForgeCompositionTests</c>' own scenario, which the ratio gate does reach
    /// and which was re-authored to say so). An earlier version of this test tried to enforce the cap
    /// by pre-assigning two neutrals to the human instead, leaving four producer-typed bases on the
    /// map in total (both starts plus two neutrals) - <see cref="Base.Type"/> never changed hands,
    /// only <see cref="Base.Owner"/>, so once the passive, brainless human was eventually eliminated
    /// the AI was free to capture all four; that it never actually reached four owned producers even
    /// so was a coincidence of clause 3's own conversion cadence draining producer count back down,
    /// not the structural bound this test claimed - caught in review. With the gate permanently
    /// closed, clause 3 always falls through to <c>TryConvertToTower</c> - byte-for-byte the pre-FR-6
    /// <c>TryConvert</c> body - and clause 1's forge/non-forge split is vacuous with no forge ever on
    /// the board, reducing to the pre-FR-6 <c>TryDefend</c> exactly. No base of either player's ever
    /// becomes a <see cref="BaseType.Forge"/> for the life of the match, played out to a decisive
    /// outcome rather than cut off at an arbitrary tick.
    /// </summary>
    [Fact]
    public void ZeroForgeBaseline_WhenTheAiCanStructurallyNeverReachFourProducers_NoBaseEverBecomesAForge()
    {
        var slots = new[]
        {
            new MapSlot(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.35, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Tower, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.35, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Tower, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.65, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Tower, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.65, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        };

        var match = new Match(slots);
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        // Play to a decisive outcome, not an arbitrary cutoff: the cap is on producer count, not on
        // ownership or elapsed time, so it must hold even once one side has taken everything.
        var maxTicks = 200_000L;
        for (var elapsed = 0L; elapsed < maxTicks && match.Outcome == MatchOutcome.InProgress; elapsed += MatchRunner.DecisionIntervalTicks)
        {
            runner.Advance(MatchRunner.DecisionIntervalTicks);
        }

        Assert.NotEqual(MatchOutcome.InProgress, match.Outcome);
        Assert.DoesNotContain(match.Bases, b => b.Type == BaseType.Forge);
        Assert.Equal(0, match.ForgeCountFor(match.AiPlayer));
        Assert.Equal(0, match.ForgeCountFor(match.HumanPlayer));
        Assert.True(OwnBases(match, match.AiPlayer).Count(b => b.Type == BaseType.Producer) <= 3);
    }

    [Fact]
    public void ForgeAwareDecisions_ReplayedTwice_ProduceIdenticalOutcomes()
    {
        static string RunAndSnapshot()
        {
            var match = new Match();
            var brain = new AiBrain(match.AiPlayer);
            var runner = new MatchRunner(match, brain);

            for (var elapsed = 0L; elapsed < 3000 && match.Outcome == MatchOutcome.InProgress; elapsed += MatchRunner.DecisionIntervalTicks)
            {
                runner.Advance(MatchRunner.DecisionIntervalTicks);
            }

            var header = FormattableString.Invariant(
                $"Ticks={match.ElapsedTicks} Outcome={match.Outcome} HM={match.HumanMorale.Points} AM={match.AiMorale.Points} HF={match.ForgeCountFor(match.HumanPlayer)} AF={match.ForgeCountFor(match.AiPlayer)}");

            return header + string.Concat(match.Bases.Select(b => FormattableString.Invariant(
                $" [{b.Id}:{(b.Owner is Player o ? o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "n")},{b.GarrisonCount},{b.Level},{b.Type}]")));
        }

        Assert.Equal(RunAndSnapshot(), RunAndSnapshot());
    }
}
