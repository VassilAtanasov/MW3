namespace MW3.Core.Tests;

/// <summary>
/// FR-2: morale feeds live into <see cref="CombatResolver"/>'s attack and defence indices
/// (<c>docs/morale/REQUIREMENTS.md</c> FR-2, <c>docs/morale/ARCHITECTURE.md</c> D-40). Covers what
/// <see cref="CombatResolverTests"/> (the resolver in isolation) and <see cref="MoraleAccrualTests"/>
/// (FR-1's accrual) do not: the live read at arrival, the neutral-defender identity case, the
/// central board-state claim and its asymmetry, and <see cref="AiBrain"/>'s prediction agreeing
/// with <see cref="Match"/>'s actual resolution once both carry real morale.
/// </summary>
public class MoraleCombatTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetMoralePoints(MoraleState state, int points) =>
        typeof(MoraleState).GetProperty(nameof(MoraleState.Points))!.GetSetMethod(nonPublic: true)!.Invoke(state, new object?[] { points });

    /// <summary>
    /// A neutral base (Owner is null, D-11) has no morale and composes its defence at identity
    /// (100%) regardless of how high the attacker's own morale climbs - asserted explicitly per
    /// FR-2's kickoff note that a null-owner morale lookup is exactly the kind of thing that throws
    /// or silently returns a wrong default. The expected result is computed through the same
    /// <see cref="CombatResolver"/> the resolver uses, with the defender's morale term fixed at 100
    /// rather than hardcoded as a raw number.
    /// </summary>
    [Fact]
    public void NeutralDefender_ComposesAtIdentity_RegardlessOfAttackerMorale()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        SetGarrison(human, 30);
        SetGarrison(neutral, 5);
        SetMoralePoints(match.HumanMorale, MoraleTable.PointCeiling); // level 5, 125% attack

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 6)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        var expectedAttackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(5));
        var expectedDefenderIndex = CombatResolver.ComposeDefenderIndex(neutral.DefencePercentage, moraleDefencePercent: 100);
        var expected = CombatResolver.Resolve(expectedAttackerIndex, expectedDefenderIndex, 6, 5);

        Assert.True(expected.Captured); // sanity: the scenario is chosen to capture
        Assert.Equal(expected.Captured, neutral.Owner == match.HumanPlayer);
        Assert.Equal(expected.RemainingGarrison, neutral.GarrisonCount);
    }

    /// <summary>
    /// The attacker's index is read live, at arrival (FR-2), not at submission - no index is stored
    /// on <see cref="Army"/>. A send launched while its owner is at morale 0 that reaches morale 5
    /// before arrival hits with the higher index; the same send, never boosted, does not. Both runs
    /// target a nearby neutral base (short enough travel that production never fires en route,
    /// keeping the defending garrison fixed so only the attacker's index differs between them).
    /// </summary>
    [Fact]
    public void AttackersIndex_IsReadLiveAtArrival_NotAtSubmission()
    {
        var boosted = new Match();
        var boostedHuman = boosted.Bases.Single(b => b.Owner == boosted.HumanPlayer);
        var boostedTarget = boosted.Bases.First(b => b.Owner is null);
        SetGarrison(boostedHuman, 30);
        SetGarrison(boostedTarget, 6);

        Assert.Equal(SendArmyOutcome.Accepted, boosted.Execute(new SendArmyCommand(boosted.HumanPlayer, boostedHuman.Id, boostedTarget.Id, 6)));
        var boostedArmy = boosted.ArmiesInFlight.Single();
        SetMoralePoints(boosted.HumanMorale, MoraleTable.PointCeiling); // boosted after submission, before arrival
        boosted.Advance(boostedArmy.ArrivalTick - boosted.ElapsedTicks);

        var control = new Match();
        var controlHuman = control.Bases.Single(b => b.Owner == control.HumanPlayer);
        var controlTarget = control.Bases.First(b => b.Owner is null);
        SetGarrison(controlHuman, 30);
        SetGarrison(controlTarget, 6);

        Assert.Equal(SendArmyOutcome.Accepted, control.Execute(new SendArmyCommand(control.HumanPlayer, controlHuman.Id, controlTarget.Id, 6)));
        var controlArmy = control.ArmiesInFlight.Single();
        control.Advance(controlArmy.ArrivalTick - control.ElapsedTicks);

        // Same send, same starting garrison, same travel time - the only difference is whether the
        // sender's morale rose between submission and arrival.
        Assert.Equal(boosted.HumanPlayer, boostedTarget.Owner); // captured once boosted
        Assert.NotEqual(control.HumanPlayer, controlTarget.Owner); // held, never boosted
    }

    /// <summary>
    /// The phase's central claim (§3 success criterion 2), as a board-state comparison against the
    /// identical send: the same 7-unit send against the identical level-1 village garrison is
    /// repelled when the defender is at morale 5 and captures when the defender is at morale 0.
    /// Both matches are advanced identically to the send's arrival; the only difference is the
    /// defender's morale.
    /// </summary>
    [Fact]
    public void HighDefenderMorale_RepelsTheSameSendThatCapturesAtMoraleZero()
    {
        Match Build(int aiMoralePoints)
        {
            var m = new Match();
            var human = m.Bases.Single(b => b.Owner == m.HumanPlayer);
            var ai = m.Bases.Single(b => b.Owner == m.AiPlayer);
            SetGarrison(ai, 5);
            SetGarrison(human, 30);
            SetMoralePoints(m.AiMorale, aiMoralePoints);
            return m;
        }

        var atMoraleZero = Build(MoraleTable.PointFloor);
        var targetZero = atMoraleZero.Bases.Single(b => b.Owner == atMoraleZero.AiPlayer);
        Assert.Equal(SendArmyOutcome.Accepted, atMoraleZero.Execute(new SendArmyCommand(atMoraleZero.HumanPlayer, atMoraleZero.Bases.Single(b => b.Owner == atMoraleZero.HumanPlayer).Id, targetZero.Id, 7)));
        var armyZero = atMoraleZero.ArmiesInFlight.Single();
        atMoraleZero.Advance(armyZero.ArrivalTick - atMoraleZero.ElapsedTicks);

        var atMoraleFive = Build(MoraleTable.PointCeiling);
        var targetFive = atMoraleFive.Bases.Single(b => b.Owner == atMoraleFive.AiPlayer);
        Assert.Equal(SendArmyOutcome.Accepted, atMoraleFive.Execute(new SendArmyCommand(atMoraleFive.HumanPlayer, atMoraleFive.Bases.Single(b => b.Owner == atMoraleFive.HumanPlayer).Id, targetFive.Id, 7)));
        var armyFive = atMoraleFive.ArmiesInFlight.Single();
        atMoraleFive.Advance(armyFive.ArrivalTick - atMoraleFive.ElapsedTicks);

        Assert.Equal(atMoraleZero.HumanPlayer, targetZero.Owner); // captures at defender morale 0
        Assert.Equal(atMoraleFive.AiPlayer, targetFive.Owner); // repelled at defender morale 5
    }

    /// <summary>
    /// The companion proof of MW2's central asymmetry (+125pp of defence against only +25pp of
    /// attack, §3 success criterion 2, <c>MW2-RULES.md</c> §5.1): raising the defender's morale from
    /// 0 to 5 flips a capture into a full hold, while raising the attacker's morale by the same five
    /// levels only improves the capture's margin without changing the outcome. Exercised directly
    /// against <see cref="CombatResolver"/> (the same arithmetic <see cref="Match.ResolveArrival"/>
    /// uses) with a fixed base scenario and indices composed from <see cref="MoraleTable"/>, not
    /// hardcoded.
    /// </summary>
    [Fact]
    public void RaisingDefenderMorale_ChangesTheOutcomeFarMoreThan_RaisingAttackerMoraleByTheSameLevels()
    {
        const int baseDefencePercent = 140; // a level-1 tower's own defence (CombatResolverTests)
        const int garrison = 10;
        const int waveUnits = 15;

        var identityAttackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(0));
        var identityDefenderIndex = CombatResolver.ComposeDefenderIndex(baseDefencePercent, MoraleTable.DefencePercentage(0));
        var baseline = CombatResolver.Resolve(identityAttackerIndex, identityDefenderIndex, waveUnits, garrison);

        var boostedAttackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(5));
        var attackerBoosted = CombatResolver.Resolve(boostedAttackerIndex, identityDefenderIndex, waveUnits, garrison);

        var boostedDefenderIndex = CombatResolver.ComposeDefenderIndex(baseDefencePercent, MoraleTable.DefencePercentage(5));
        var defenderBoosted = CombatResolver.Resolve(identityAttackerIndex, boostedDefenderIndex, waveUnits, garrison);

        Assert.True(baseline.Captured); // sanity: identity captures, so there is room to move in both directions

        // +25pp of attack (morale's maximum attack contribution) still captures - only the margin
        // improves.
        Assert.True(attackerBoosted.Captured);
        Assert.True(attackerBoosted.RemainingGarrison > baseline.RemainingGarrison);

        // +125pp of defence (morale's maximum defence contribution) flips the same send from a
        // capture to a full hold - the asymmetry the whole system is built on.
        Assert.False(defenderBoosted.Captured);
    }

    /// <summary>
    /// <see cref="CombatResolver.WouldCapture"/> is the single shared predicate both
    /// <see cref="CombatResolver.Resolve"/> and <see cref="AiBrain"/>'s predictions go through
    /// (introduced by follow-up #68, PR #70). This proves the two agree once both sides carry real,
    /// non-zero morale (FR-2's amendment to the issue): a target the live indices predict capturable
    /// is one <see cref="Match"/> actually takes, and one predicted uncapturable actually holds. The
    /// prediction is computed exactly as <see cref="AiBrain"/> computes it - live indices via
    /// <see cref="Match.MoraleFor"/> and a production-grown garrison prediction - never a raw
    /// snapshot of the current garrison.
    /// </summary>
    [Theory]
    [InlineData(4, true)]
    [InlineData(9, false)]
    public void WouldCapture_AgreesWithMatchsActualResolution_AtNonZeroMoraleBothSides(int startingGarrison, bool expectedCapture)
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var target = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetGarrison(human, 40);
        SetGarrison(target, startingGarrison);
        SetMoralePoints(match.HumanMorale, 2000); // level 3, 115% attack
        SetMoralePoints(match.AiMorale, 500); // level 1, 125% defence

        const int send = 7;
        const long travelTicks = 76; // this map's human<->AI edge (MoraleAccrualTests)
        var period = LevelTable.Village.ProductionPeriodTicks(target.Level);
        var predictedGarrison = startingGarrison + (int)(travelTicks / period);

        var attackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(match.MoraleFor(match.HumanPlayer).Level));
        var defenderIndex = CombatResolver.ComposeDefenderIndex(target.DefencePercentage, MoraleTable.DefencePercentage(match.MoraleFor(match.AiPlayer).Level));
        var predicted = CombatResolver.WouldCapture(attackerIndex, defenderIndex, send, predictedGarrison);

        Assert.Equal(expectedCapture, predicted); // sanity: the chosen garrison lands on the intended side

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, target.Id, send)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        var actuallyCaptured = target.Owner == match.HumanPlayer;
        Assert.Equal(predicted, actuallyCaptured);
    }
}
