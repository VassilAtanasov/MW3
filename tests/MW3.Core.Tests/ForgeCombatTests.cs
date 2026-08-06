namespace MW3.Core.Tests;

/// <summary>
/// Phase 6 FR-3: the forge count derived from the board (D-45), the global buff it buys, the cap it
/// stops at, and the standing requirement that <see cref="AiBrain"/>'s predictions compose the same
/// forge term <see cref="Match"/>'s resolution does. See issue #87's acceptance criteria.
/// </summary>
public class ForgeCombatTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    // A short-range layout: the target sits 0.20 from the attacker, so a send arrives in 20 ticks at
    // the morale-0 speed - well inside the level-1 village production period (60 ticks), which keeps
    // the defending garrison fixed at whatever the test sets it to. Forge slots sit off the firing
    // line and are assigned an owner per-test.
    private static MapSlot[] LayoutWithForgeSlots(int forgeSlotCount)
    {
        var slots = new MapSlot[2 + forgeSlotCount];
        slots[0] = new MapSlot(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel);
        slots[1] = new MapSlot(new MapPoint(0.32, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel);

        for (var i = 0; i < forgeSlotCount; i++)
        {
            // Spread along the bottom edge, far from both the attacker and the target.
            var x = 0.10 + (0.15 * i);
            slots[2 + i] = new MapSlot(new MapPoint(x, 0.90), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel);
        }

        return slots;
    }

    private static void GiveForgesTo(Match match, Player owner, int count)
    {
        var given = 0;
        foreach (var b in match.Bases)
        {
            if (b.Type == BaseType.Forge && given < count)
            {
                SetOwner(b, owner);
                given++;
            }
        }

        Assert.Equal(count, given); // the layout must actually contain that many forge slots
        Assert.Equal(count, match.ForgeCountFor(owner));
    }

    // ---------------------------------------------------------------- deriving the count

    /// <summary>
    /// The count is derived from the board on every read (D-45), so it is correct after a conversion
    /// completes, after a capture, and after a loss - all within one match, with no re-construction
    /// and nothing to keep in sync.
    /// </summary>
    [Fact]
    public void TheCount_TracksConversionCaptureAndLoss_WithinOneMatch()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutralForge = match.Bases.Single(b => b.Type == BaseType.Forge && b.Owner is null);

        Assert.Equal(0, match.ForgeCountFor(match.HumanPlayer));
        Assert.Equal(0, match.ForgeCountFor(match.AiPlayer));

        // Conversion: the count moves only when the build completes, not when it is ordered.
        SetGarrison(human, 70);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, human.Id, BaseType.Forge)));
        Assert.Equal(0, match.ForgeCountFor(match.HumanPlayer));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Forge, human.Type);
        Assert.Equal(1, match.ForgeCountFor(match.HumanPlayer));

        // Capture: the map's own neutral forge changes hands.
        SetOwner(neutralForge, match.HumanPlayer);
        Assert.Equal(2, match.ForgeCountFor(match.HumanPlayer));

        // Loss: the same base taken by the opponent moves the count on both sides at once.
        SetOwner(neutralForge, match.AiPlayer);
        Assert.Equal(1, match.ForgeCountFor(match.HumanPlayer));
        Assert.Equal(1, match.ForgeCountFor(match.AiPlayer));

        // Converting back out of a forge drops it again.
        SetGarrison(human, 70);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, human.Id, BaseType.Producer)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(0, match.ForgeCountFor(match.HumanPlayer));
    }

    /// <summary>
    /// A forge owned by nobody counts for nobody (D-47): the shipped map's contested centre forge
    /// buffs neither side until somebody takes it, and it composes its own defence at identity.
    /// </summary>
    [Fact]
    public void ANeutralForge_CountsForNobody_AndDefendsAtIdentity()
    {
        var match = new Match();
        var neutralForge = match.Bases.Single(b => b.Type == BaseType.Forge && b.Owner is null);

        Assert.Equal(0, match.ForgeCountFor(match.HumanPlayer));
        Assert.Equal(0, match.ForgeCountFor(match.AiPlayer));
        Assert.Equal(ForgeTable.DefencePercentage(ForgeTable.MinForgeCount), match.ForgeDefencePercentFor(neutralForge.Owner));
    }

    /// <summary>
    /// Counting allocates nothing - it runs on the per-tick combat path (REQUIREMENTS.md §5), where a
    /// per-call LINQ query or temporary list would be a garbage source proportional to the tick rate.
    /// </summary>
    [Fact]
    public void CountingAllocatesNothing()
    {
        var match = new Match();
        GiveForgesTo(match, match.HumanPlayer, 1);

        // Warm up so JIT-time allocation is not measured.
        var sink = match.ForgeCountFor(match.HumanPlayer) + match.ForgeAttackPercentFor(match.HumanPlayer);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            sink += match.ForgeCountFor(match.HumanPlayer);
            sink += match.ForgeAttackPercentFor(match.AiPlayer);
            sink += match.ForgeDefencePercentFor(null);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(sink > 0); // keep the loop from being optimized away

        // A budget rather than an exact zero: tiered compilation can re-JIT inside the loop and
        // charge this thread a few hundred bytes for it, which would make an exact-zero assertion
        // flake on a CI machine under load rather than catch anything. The budget is still
        // load-bearing by three orders of magnitude - the collection-per-call this rules out would
        // be at least 32 bytes on each of 3000 calls, roughly 96 KB.
        Assert.True(allocated < 4096, FormattableString.Invariant($"3000 calls allocated {allocated} bytes; expected effectively none."));
    }

    // ---------------------------------------------------------------- the buff

    /// <summary>
    /// <b>The buff decides an exchange</b> (§3 success criterion 2), compared as board state rather
    /// than as a percentage: the identical 8-unit send against the identical 10-unit level-1
    /// defender is repelled when the attacker holds no forge (8 × 10000 is not greater than
    /// 10 × 10000) and captures when the attacker holds one (8 × 15000 = 120000 &gt; 100000). Nothing
    /// differs between the two runs but who owns the forge slot.
    /// </summary>
    [Fact]
    public void OneForge_TurnsARepelledSendIntoACapture()
    {
        Match Build(int humanForges)
        {
            var m = new Match(LayoutWithForgeSlots(forgeSlotCount: 1));
            var human = m.Bases[0];
            var target = m.Bases[1];
            SetGarrison(human, 40);
            SetGarrison(target, 10);
            GiveForgesTo(m, m.HumanPlayer, humanForges);
            Assert.Equal(SendArmyOutcome.Accepted, m.Execute(new SendArmyCommand(m.HumanPlayer, human.Id, target.Id, 8)));
            return m;
        }

        var withoutForge = Build(humanForges: 0);
        var withForge = Build(humanForges: 1);

        // A single wave in both runs (8 is the single-wave ceiling), so the exchange resolves once.
        var arrivalTick = withoutForge.ArmiesInFlight.Single().ArrivalTick;
        Assert.Equal(arrivalTick, withForge.ArmiesInFlight.Single().ArrivalTick);

        withoutForge.Advance(arrivalTick - withoutForge.ElapsedTicks);
        withForge.Advance(arrivalTick - withForge.ElapsedTicks);

        var repelled = withoutForge.Bases[1];
        var captured = withForge.Bases[1];

        Assert.Equal(withoutForge.AiPlayer, repelled.Owner); // held: the defender survives at 10 - 8
        Assert.Equal(2, repelled.GarrisonCount);

        Assert.Equal(withForge.HumanPlayer, captured.Owner); // taken: (120000 - 100000) / 10000
        Assert.Equal(2, captured.GarrisonCount);
    }

    /// <summary>
    /// <b>The cap is observable</b> (§3 success criterion 3): the same send against the same defender
    /// produces an identical <see cref="CombatResult"/> - captured flag and remaining garrison - at
    /// four forges and at five, because <see cref="ForgeTable"/> clamps. The three-forge arm is not
    /// decoration: it proves this scenario is genuinely sensitive to the count just below the cap
    /// (a three-forge attack index of 19000 leaves 5 behind where four forges' 20000 leaves 6), so
    /// the four-equals-five claim is a clamp rather than a scenario too blunt to notice a difference.
    /// </summary>
    [Fact]
    public void AtAndBeyondTheCap_TheSameSendProducesAnIdenticalResult()
    {
        (bool Captured, int Remaining) Run(int humanForges)
        {
            var m = new Match(LayoutWithForgeSlots(forgeSlotCount: 5));
            var human = m.Bases[0];
            var target = m.Bases[1];
            SetGarrison(human, 40);
            SetGarrison(target, 10);
            GiveForgesTo(m, m.HumanPlayer, humanForges);
            Assert.Equal(SendArmyOutcome.Accepted, m.Execute(new SendArmyCommand(m.HumanPlayer, human.Id, target.Id, 8)));
            var arrivalTick = m.ArmiesInFlight.Single().ArrivalTick;
            m.Advance(arrivalTick - m.ElapsedTicks);
            return (target.Owner == m.HumanPlayer, target.GarrisonCount);
        }

        var atFour = Run(ForgeTable.MaxContributingForges);
        var atFive = Run(ForgeTable.MaxContributingForges + 1);
        var atThree = Run(ForgeTable.MaxContributingForges - 1);

        Assert.Equal(atFour, atFive); // the fifth forge buys nothing at all
        Assert.NotEqual(atThree, atFour); // ...but the fourth still did
        // 8 x 20000 = 160000 against 10 x 10000: captured, (160000 - 100000) / 10000 = 6 left.
        Assert.Equal((true, 6), atFour);

        // 8 x 19000 = 152000 against the same defence: captured, but only 5 left.
        Assert.Equal((true, 5), atThree);
    }

    // ---------------------------------------------------------------- prediction agrees with resolution

    /// <summary>
    /// <b>The third occurrence of the desync hazard</b> follow-up #68 closed against building defence
    /// and phase 5 FR-2 patched against morale (D-45): the indices composed the way
    /// <see cref="AiBrain"/> composes them must predict what <see cref="Match"/> actually resolves,
    /// with non-zero forge counts on both sides.
    /// <para>
    /// This pins the <i>arithmetic</i> half of that claim only - it recomputes the indices from
    /// <see cref="Match"/>'s accessors rather than driving <see cref="AiBrain"/>. The <i>wiring</i>
    /// half - that each of the brain's four forge terms is actually supplied - is pinned separately
    /// and behaviourally by <see cref="TheAttackPath_ReadsItsOwnForgeTerm"/>,
    /// <see cref="TheAttackPath_ReadsTheDefendersForgeTerm"/>,
    /// <see cref="TheThreatPath_ReadsTheAttackersForgeTerm"/> and
    /// <see cref="TheThreatPath_ReadsTheDefendersForgeTerm"/>, each of which fails if its own term is
    /// replaced by identity.
    /// </para>
    /// <para>
    /// The two cases are chosen so that dropping the forge term flips the answer in each direction.
    /// The attacker holds 2 forges (attack index 17500) and the defender 1 (defence index 12500). At
    /// a garrison of 10 the send captures - but composed without the <i>attacker's</i> forge term it
    /// would be 80000 against 125000 and predict a repel. At 12 it is repelled - but composed
    /// without the <i>defender's</i> forge term it would be 140000 against 120000 and predict a
    /// capture. Either omission is therefore caught.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(10, true)]
    [InlineData(12, false)]
    public void PredictionAgreesWithResolution_AtNonZeroForgeCountsOnBothSides(int targetGarrison, bool expectedCapture)
    {
        var match = new Match(LayoutWithForgeSlots(forgeSlotCount: 3));
        var human = match.Bases[0];
        var target = match.Bases[1];
        SetGarrison(human, 40);
        SetGarrison(target, targetGarrison);
        GiveForgesTo(match, match.HumanPlayer, 2);
        SetOwner(match.Bases.Last(b => b.Type == BaseType.Forge), match.AiPlayer);
        Assert.Equal(1, match.ForgeCountFor(match.AiPlayer));

        const int send = 8;

        // Composed exactly as AiBrain composes them - live, through Match's own accessors.
        var attackerIndex = CombatResolver.ComposeAttackerIndex(
            MoraleTable.AttackPercentage(match.MoraleFor(match.HumanPlayer).Level),
            match.ForgeAttackPercentFor(match.HumanPlayer));
        var defenderIndex = CombatResolver.ComposeDefenderIndex(
            target.DefencePercentage,
            MoraleTable.DefencePercentage(match.MoraleFor(match.AiPlayer).Level),
            match.ForgeDefencePercentFor(target.Owner));

        Assert.Equal(17500, attackerIndex);
        Assert.Equal(12500, defenderIndex);

        var predicted = CombatResolver.WouldCapture(attackerIndex, defenderIndex, send, targetGarrison);
        Assert.Equal(expectedCapture, predicted); // sanity: the garrison lands on the intended side

        // Dropping either forge term flips this exact case - what makes the agreement below load-bearing.
        var identity = ForgeTable.AttackPercentage(ForgeTable.MinForgeCount);
        var withoutAttackerForge = CombatResolver.ComposeAttackerIndex(
            MoraleTable.AttackPercentage(match.MoraleFor(match.HumanPlayer).Level), identity);
        var withoutDefenderForge = CombatResolver.ComposeDefenderIndex(
            target.DefencePercentage, MoraleTable.DefencePercentage(match.MoraleFor(match.AiPlayer).Level), identity);
        Assert.NotEqual(
            predicted,
            expectedCapture
                ? CombatResolver.WouldCapture(withoutAttackerForge, defenderIndex, send, targetGarrison)
                : CombatResolver.WouldCapture(attackerIndex, withoutDefenderForge, send, targetGarrison));

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, target.Id, send)));
        var arrivalTick = match.ArmiesInFlight.Single().ArrivalTick;
        match.Advance(arrivalTick - match.ElapsedTicks);

        Assert.Equal(predicted, target.Owner == match.HumanPlayer);
    }

    /// <summary>
    /// <see cref="AiBrain"/>'s <i>attack</i> path (clause 4) reads the AI's own forge term: a target
    /// is winnable only because the AI holds two forges. With them it attacks; without them it sees
    /// nothing worth attacking and its decision goes elsewhere. Fails if that term is replaced by
    /// identity - which is the point, since an AI silently blind to its own buff would simply play a
    /// weaker game with every gate still green.
    /// </summary>
    [Theory]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public void TheAttackPath_ReadsItsOwnForgeTerm(int aiForges, bool expectsAttack)
    {
        // The AI's 16 garrison gives TryAttack an unclamped half of 8. Against the neutral target's
        // 10 that is 80000 against 100000 at identity - not winnable - and 8 x 17500 = 140000 at two
        // forges, which is. The human capital is stocked far past anything 8 units could take, so
        // this target is the only candidate either way, and neither the upgrade clause (garrison
        // below cap) nor the convert clause (garrison below the conversion cost) preempts it.
        var layout = new[]
        {
            new MapSlot(new MapPoint(0.10, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.50, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.60, 0.50), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.20, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.35, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
        };

        var match = new Match(layout);
        SetGarrison(match.Bases[0], 40);
        SetGarrison(match.Bases[1], 16);
        SetGarrison(match.Bases[2], 10);
        GiveForgesTo(match, match.AiPlayer, aiForges);

        var decision = new AiBrain(match.AiPlayer).Decide(match);

        var attacksTheTarget = decision.HasCommand && decision.IsSend && decision.Command.TargetBaseId == 2;
        Assert.Equal(expectsAttack, attacksTheTarget);
    }

    /// <summary>
    /// <see cref="AiBrain"/>'s attack path also reads the <i>defender's</i> forge term, so a base
    /// made harder by its owner's forges stops being a target rather than absorbing armies the AI
    /// predicted would take it. Fails if that term is replaced by identity.
    /// </summary>
    [Theory]
    [InlineData(4, false)]
    [InlineData(0, true)]
    public void TheAttackPath_ReadsTheDefendersForgeTerm(int humanForges, bool expectsAttack)
    {
        // 8 attacking units against the human capital's 7: 80000 against 70000 at identity, which is
        // winnable - and against 7 x 15000 = 105000 once the human holds four forges, which is not.
        // The forges themselves are never a candidate at either count (10 garrison is already out of
        // reach of 8 units at identity), so the capital is the only target that can move.
        var layout = new[]
        {
            new MapSlot(new MapPoint(0.10, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.20, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.60, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.70, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.80, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.90, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
        };

        var match = new Match(layout);
        SetGarrison(match.Bases[0], 7);
        SetGarrison(match.Bases[1], 16);
        GiveForgesTo(match, match.HumanPlayer, humanForges);

        var decision = new AiBrain(match.AiPlayer).Decide(match);

        var attacksTheCapital = decision.HasCommand && decision.IsSend && decision.Command.TargetBaseId == 0;
        Assert.Equal(expectsAttack, attacksTheCapital);
    }

    /// <summary>
    /// <see cref="AiBrain"/>'s threat path reads the <i>defender's</i> forge term - its own, here -
    /// so a base its forges already make safe does not pull a reinforcement it does not need. Fails
    /// if that term is replaced by identity.
    /// </summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public void TheThreatPath_ReadsTheDefendersForgeTerm(int aiForges, bool expectsReinforcement)
    {
        // 8 human units in flight against the AI's 6-garrison base: 80000 against 60000 at identity
        // captures, so the base is threatened - and against 6 x 13500 = 81000 with two AI forges it
        // does not, so it is not. The margin is one thousandth of the defence index, which is
        // exactly the kind of edge a dropped term flips.
        var layout = new[]
        {
            new MapSlot(new MapPoint(0.10, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.50, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.40, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.20, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.35, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
        };

        var match = new Match(layout);
        var human = match.Bases[0];
        var threatened = match.Bases[1];
        var reinforcer = match.Bases[2];

        SetOwner(reinforcer, match.AiPlayer);
        SetGarrison(human, 40);
        SetGarrison(threatened, 6);
        SetGarrison(reinforcer, 12);
        GiveForgesTo(match, match.AiPlayer, aiForges);

        // 8 units is the single-wave ceiling, so the whole send is in flight at once and the threat
        // total is not split across a pending wave the brain cannot see yet.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, threatened.Id, 8)));
        Assert.Single(match.ArmiesInFlight);

        var decision = new AiBrain(match.AiPlayer).Decide(match);

        var reinforces = decision.HasCommand
            && decision.IsSend
            && decision.Command.TargetBaseId == threatened.Id
            && decision.Command.SourceBaseId == reinforcer.Id;

        Assert.Equal(expectsReinforcement, reinforces);
    }

    /// <summary>
    /// <see cref="AiBrain"/>'s <i>threat</i> path (clause 1, reinforce a threatened base) reads the
    /// attacker's forge term too: the same in-flight human send is a threat worth answering only
    /// because the human holds two forges. Without them the AI sees no threat to this base and its
    /// decision goes elsewhere. Exercised through the brain itself, not through the arithmetic, so
    /// the wiring is covered and not merely the formula.
    /// </summary>
    [Theory]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public void TheThreatPath_ReadsTheAttackersForgeTerm(int humanForges, bool expectsReinforcement)
    {
        // 0 human capital, 1 the AI's threatened base, 2 the AI's reinforcing base (nearer the
        // human, so it - not the threatened base - is the "front" every other clause aims at), then
        // two forge slots well out of the way.
        var layout = new[]
        {
            new MapSlot(new MapPoint(0.10, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.50, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.40, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.15, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.30, 0.95), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
        };

        var match = new Match(layout);
        var human = match.Bases[0];
        var threatened = match.Bases[1];
        var reinforcer = match.Bases[2];

        SetOwner(reinforcer, match.AiPlayer);
        SetGarrison(human, 40);
        SetGarrison(threatened, 10);
        SetGarrison(reinforcer, 20);
        GiveForgesTo(match, match.HumanPlayer, humanForges);

        // 8 units against a garrison of 10: 8 x 10000 loses, 8 x 17500 wins. The whole difference is
        // the two forges.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, threatened.Id, 8)));

        var decision = new AiBrain(match.AiPlayer).Decide(match);

        var reinforces = decision.HasCommand
            && decision.IsSend
            && decision.Command.TargetBaseId == threatened.Id
            && decision.Command.SourceBaseId == reinforcer.Id;

        Assert.Equal(expectsReinforcement, reinforces);
    }

    // ---------------------------------------------------------------- determinism

    /// <summary>
    /// Determinism survives the new term (D-12, S-8): the same command stream replayed against the
    /// same starting state, chunked differently across <see cref="Match.Advance"/> calls, produces
    /// the same outcome and the same forge counts on every tick.
    /// </summary>
    [Fact]
    public void ForgeCounts_AreDeterministic_TickByTick_AcrossDifferentAdvanceChunking()
    {
        Match Build()
        {
            var m = new Match(LayoutWithForgeSlots(forgeSlotCount: 2));
            var human = m.Bases[0];
            var target = m.Bases[1];
            SetGarrison(human, 40);
            SetGarrison(target, 10);
            GiveForgesTo(m, m.HumanPlayer, 1);
            Assert.Equal(SendArmyOutcome.Accepted, m.Execute(new SendArmyCommand(m.HumanPlayer, human.Id, target.Id, 8)));
            return m;
        }

        const long totalTicks = 200;

        var oneShot = Build();
        oneShot.Advance(totalTicks);

        var chunked = Build();
        for (var tick = 1; tick <= totalTicks; tick++)
        {
            chunked.Advance(1);
        }

        Assert.Equal(oneShot.ForgeCountFor(oneShot.HumanPlayer), chunked.ForgeCountFor(chunked.HumanPlayer));
        Assert.Equal(oneShot.ForgeCountFor(oneShot.AiPlayer), chunked.ForgeCountFor(chunked.AiPlayer));
        Assert.Equal(oneShot.Outcome, chunked.Outcome);
        Assert.Equal(oneShot.Bases[1].Owner?.Id, chunked.Bases[1].Owner?.Id);
        Assert.Equal(oneShot.Bases[1].GarrisonCount, chunked.Bases[1].GarrisonCount);

        // Tick by tick, not merely at the end: a third run stepped one tick at a time must agree
        // with a fourth stepped in tens at every boundary the two share.
        var byOne = Build();
        var byTen = Build();
        for (var boundary = 10; boundary <= totalTicks; boundary += 10)
        {
            for (var step = 0; step < 10; step++)
            {
                byOne.Advance(1);
            }

            byTen.Advance(10);

            Assert.Equal(byOne.ForgeCountFor(byOne.HumanPlayer), byTen.ForgeCountFor(byTen.HumanPlayer));
            Assert.Equal(byOne.ForgeCountFor(byOne.AiPlayer), byTen.ForgeCountFor(byTen.AiPlayer));
            Assert.Equal(byOne.Bases[1].GarrisonCount, byTen.Bases[1].GarrisonCount);
        }
    }

    /// <summary>
    /// A null player is a caller bug, not a neutral holding - <see cref="Match.ForgeCountFor"/>
    /// rejects it rather than quietly counting neutral bases as somebody's.
    /// </summary>
    [Fact]
    public void ForgeCountFor_Null_Throws()
    {
        var match = new Match();
        Assert.Throws<ArgumentNullException>(() => match.ForgeCountFor(null!));
    }
}
