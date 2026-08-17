using System.Reflection;

namespace MW3.Core.Tests;

/// <summary>
/// Phase 7 FR-6 (issue #108): the AI's judgement reads the same routes its armies actually fly.
/// <see cref="TowerThreatEstimator"/> sums its chord over an <see cref="ArmyPath"/>'s segments,
/// <see cref="AiBrain"/>'s one distance rule measures route length, and clause 4 orders its targets
/// by route length. Covers the summed-then-floored-once rule, the two heuristics that flip on an
/// obstacle, and the bit-identity that protects Small and Big.
/// </summary>
public class AiRouteAwareThreatTests
{
    private static readonly MapObstacle[] _noObstacles = Array.Empty<MapObstacle>();

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetType(Base b, BaseType type) =>
        typeof(Base).GetProperty(nameof(Base.Type))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { type });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static BrainDecision InvokeClause(string methodName, AiBrain brain, Match match) =>
        (BrainDecision)typeof(AiBrain)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(brain, new object[] { match, match.Bases.Where(b => b.Owner == brain.Player).OrderBy(b => b.Id).ToList() })!;

    // --- A. Tower threat is estimated along the army's real route ---

    /// <summary>
    /// The hand-computed multi-segment case. A level-1 tower fires every
    /// <c>FirePeriodTicks(1) = 6</c> ticks with a range of <c>RangeUnits(1) = 0.20</c>, and an army
    /// at <see cref="Match.ArmySpeedUnitsPerTick"/> = 0.01 covers 0.06 units between shots, so the
    /// estimate is <c>floor(chord / 0.06)</c>.
    /// <para>
    /// Endpoints (0.10, 0.50) and (0.90, 0.50); tower at (0.50, 0.75). The straight line between the
    /// endpoints is the horizontal y = 0.50, a constant 0.25 from the tower - outside 0.20 - so its
    /// chord is 0 and the <b>old two-point estimate is 0</b>. The detour
    /// (0.10, 0.50) - (0.10, 0.75) - (0.90, 0.75) - (0.90, 0.50) runs its middle segment straight
    /// through the tower's centre, so that segment's chord is the full diameter 2 x 0.20 = 0.40
    /// (both intersections, x = 0.30 and x = 0.70, lie inside the segment), while the two vertical
    /// segments never come within 0.40 of the tower and contribute nothing. The <b>new estimate is
    /// floor(0.40 / 0.06) = floor(6.66..) = 6</b>.
    /// </para>
    /// </summary>
    [Fact]
    public void EstimateUnitsLost_ATowerOnTheDetourButNotOnTheStraightLine_GoesFromZeroToSix()
    {
        var tower = new MapPoint(0.50, 0.75);
        const int towerLevel = LevelTable.MinLevel;

        var straight = new ArmyPath(new[] { new MapPoint(0.10, 0.50), new MapPoint(0.90, 0.50) }, 0.80);
        var detour = new ArmyPath(
            new[]
            {
                new MapPoint(0.10, 0.50),
                new MapPoint(0.10, 0.75),
                new MapPoint(0.90, 0.75),
                new MapPoint(0.90, 0.50),
            },
            0.25 + 0.80 + 0.25);

        Assert.Equal(0, TowerThreatEstimator.EstimateUnitsLost(straight, tower, towerLevel, Match.ArmySpeedUnitsPerTick));
        Assert.Equal(6, TowerThreatEstimator.EstimateUnitsLost(detour, tower, towerLevel, Match.ArmySpeedUnitsPerTick));
    }

    /// <summary>
    /// A tower in range across a waypoint join: the chords are summed and converted to shots once,
    /// never floored per segment. Tower at (0.50, 0.50), range 0.20; the path
    /// (0.35, 0.50) - (0.50, 0.50) - (0.50, 0.35) has both segments entirely inside the range circle,
    /// so each contributes its full length of 0.15. Summed first: <c>floor(0.30 / 0.06) = 5</c>.
    /// Floored per segment: <c>floor(0.15 / 0.06) + floor(0.15 / 0.06) = 2 + 2 = 4</c>, which
    /// discards both fractional tails and understates the threat. The estimate must be 5.
    /// </summary>
    [Fact]
    public void EstimateUnitsLost_ATowerInRangeAcrossAWaypointJoin_SumsBeforeFlooring()
    {
        var tower = new MapPoint(0.50, 0.50);
        const int towerLevel = LevelTable.MinLevel;

        var join = new ArmyPath(
            new[] { new MapPoint(0.35, 0.50), new MapPoint(0.50, 0.50), new MapPoint(0.50, 0.35) },
            0.30);

        const int summedThenFloored = 5;
        const int flooredPerSegment = 4; // 2 + 2 - the wrong answer this criterion exists to forbid

        Assert.Equal(summedThenFloored, TowerThreatEstimator.EstimateUnitsLost(join, tower, towerLevel, Match.ArmySpeedUnitsPerTick));
        Assert.NotEqual(flooredPerSegment, TowerThreatEstimator.EstimateUnitsLost(join, tower, towerLevel, Match.ArmySpeedUnitsPerTick));
    }

    // --- B. The AI's distance heuristics measure the route, not the line ---

    /// <summary>
    /// Clause 5 (consolidate) feeds the front - the own base whose nearest not-owned base is closest.
    /// On a layout walled by an obstacle spanning x 0.40..0.60, y 0.05..0.90, the AI owns base 1 at
    /// (0.65, 0.50) and base 2 at (0.02, 0.50); the neutral base 3 sits at (0.35, 0.50) and the human
    /// start at (0.98, 0.98).
    /// <para>
    /// <b>Straight-line lengths</b>: base 1 to base 3 is 0.30 and base 1 to the human start is
    /// 0.5825, so base 1's nearest is <b>0.30</b>; base 2 to base 3 is 0.33 and base 2 to the human
    /// start is 1.0733, so base 2's nearest is <b>0.33</b>. Straight-line distance therefore makes
    /// <b>base 1</b> the front.
    /// </para>
    /// <para>
    /// <b>Route lengths</b>: base 1's line to base 3 is blocked, so its route runs
    /// (0.65, 0.50) - (0.62, 0.92) - (0.38, 0.92) - (0.35, 0.50) = 0.4211 + 0.24 + 0.4211 = 1.0821,
    /// while its line to the human start never enters the obstacle's x band and stays 0.5825; base
    /// 1's nearest becomes <b>0.5825</b>. Base 2's line to base 3 is clear, so its nearest stays
    /// <b>0.33</b>. Route length therefore makes <b>base 2</b> the front - a different base, which is
    /// what this test pins.
    /// </para>
    /// </summary>
    [Fact]
    public void TryConsolidate_OnAnObstacleLayout_FeedsTheRouteFrontNotTheStraightLineFront()
    {
        var match = WalledMatch();
        var ai = match.AiPlayer;
        var acrossTheWall = match.Bases[1]; // (0.65, 0.50) - the straight-line front
        var besideTheTarget = match.Bases[2]; // (0.02, 0.50) - the route front
        var neutral = match.Bases[3]; // (0.35, 0.50)
        var human = match.Bases[0]; // (0.98, 0.98)

        SetOwner(besideTheTarget, ai);
        SetGarrison(acrossTheWall, 12); // the larger own base, so it is the source and the front is the target
        SetGarrison(besideTheTarget, 4);

        // The straight-line ordering this test claims to overturn, asserted rather than asserted-by-comment.
        Assert.True(StraightLine(acrossTheWall, neutral) < StraightLine(besideTheTarget, neutral));
        Assert.True(StraightLine(acrossTheWall, neutral) < StraightLine(acrossTheWall, human));

        var decision = InvokeClause("TryConsolidate", new AiBrain(ai), match);

        Assert.True(decision.IsSend);
        Assert.Equal(besideTheTarget.Id, decision.Command.TargetBaseId);
        Assert.Equal(acrossTheWall.Id, decision.Command.SourceBaseId);
    }

    /// <summary>
    /// Clause 4 orders its candidate targets by route length. On the same walled layout, the AI's
    /// only base sits at (0.65, 0.50) and both neutral forges are winnable and identical in every
    /// other respect - same type, same level, same garrison, both neutral - so their predicted morale
    /// swings tie exactly and their expected tower losses are both zero (the layout has no tower).
    /// The order is therefore the only thing that decides, which is the point.
    /// <para>
    /// <b>Straight-line order</b>: base 2 at (0.35, 0.50) is 0.30 away, base 3 at (0.95, 0.05) is
    /// 0.5408 away - base 2 comes first and would be attacked. <b>Route order</b>: base 2's line is
    /// blocked, so its route is 1.0821, while base 3's line is clear at 0.5408 - base 3 comes first
    /// and is attacked instead.
    /// </para>
    /// </summary>
    [Fact]
    public void TryAttack_OnAnObstacleLayout_OrdersTargetsByRouteLengthNotStraightLine()
    {
        var match = ForgeTargetMatch();
        var ai = match.AiPlayer;
        var aiHome = match.Bases[1]; // (0.65, 0.50)
        var acrossTheWall = match.Bases[2]; // (0.35, 0.50) - nearer in a straight line, far by route
        var aroundTheCorner = match.Bases[3]; // (0.95, 0.05) - further in a straight line, nearer by route

        SetGarrison(aiHome, 60);
        SetGarrison(match.Bases[0], 200); // the human start stays unwinnable, so only the two forges compete

        Assert.True(StraightLine(aiHome, acrossTheWall) < StraightLine(aiHome, aroundTheCorner));

        var decision = InvokeClause("TryAttack", new AiBrain(ai), match);

        Assert.True(decision.IsSend);
        Assert.Equal(aroundTheCorner.Id, decision.Command.TargetBaseId);
    }

    /// <summary>
    /// The junction the other tests leave open: that <see cref="AiBrain"/> itself hands
    /// <see cref="TowerThreatEstimator"/> the <b>routed</b> path. The two estimator tests above never
    /// go through <c>AiBrain</c>, and the two heuristic tests carry no tower, so between them a
    /// regression that passed a straight two-waypoint path into <c>TotalExpectedTowerLoss</c> would
    /// go unnoticed. This test is the one layout carrying both an obstacle and a tower.
    /// <para>
    /// Medium's real geometry, with the human holding base 0, base 2 as a <b>tower</b>, and base 3;
    /// the AI attacks from base 4 at (0.65, 0.25). Its three candidate targets:
    /// </para>
    /// <list type="bullet">
    /// <item>base 2 (0.35, 0.25) - 0.30 away, line clear of the obstacle, and the tower is the target
    /// itself, so both forms score the same <b>3</b>.</item>
    /// <item>base 3 (0.35, 0.75) - straight-line 0.5831, whose line passes 0.2572 from the tower,
    /// outside its 0.20 range, so the old form scores <b>0</b>. Its route is blocked and turns at the
    /// inset corner (0.40, 0.28), 0.0583 from the tower: 0.7244 long, chords 0.150707 + 0.168337 =
    /// 0.319044, and floor(0.319044 / 0.06) = <b>5</b>.</item>
    /// <item>base 0 (0.12, 0.50) - 0.5860 straight / 0.6079 routed, scoring 5 and 6.</item>
    /// </list>
    /// <para>
    /// Straight-line scoring therefore orders the targets 2, 3, 0 and picks <b>base 3</b> for costing
    /// nothing. Route scoring orders them 2, 0, 3 and picks <b>base 2</b>, the cheapest at 3. The
    /// assertion is base 2: it fails the moment the brain costs a straight line instead of the
    /// polyline, whatever the estimator itself does correctly in isolation.
    /// </para>
    /// </summary>
    [Fact]
    public void TryAttack_WithBothAnObstacleAndATower_CostsTheDetourAndAvoidsTheTaxedTarget()
    {
        var match = new Match(MapCatalog.Medium);
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;

        foreach (var id in new[] { 1, 4, 5, 6, 7 })
        {
            SetOwner(match.Bases[id], ai);
            SetGarrison(match.Bases[id], 6);
        }

        SetOwner(match.Bases[2], human);
        SetType(match.Bases[2], BaseType.Tower);
        SetGarrison(match.Bases[2], 4);
        SetOwner(match.Bases[3], human);
        SetGarrison(match.Bases[3], 4);
        SetGarrison(match.Bases[0], 30);
        SetGarrison(match.Bases[4], 20); // the AI's largest, so clause 4 sources from base 4

        var tower = match.Bases[2];
        var source = match.Bases[4].Position;
        var taxedTarget = match.Bases[3].Position;
        var speed = Match.EffectiveArmySpeedUnitsPerTick(match.MoraleFor(ai).Level);

        // The two numbers the decision below turns on, asserted rather than only asserted-by-comment.
        var straightToBase3 = new ArmyPath(new[] { source, taxedTarget }, 0.0);
        var routedToBase3 = PathCalculator.ComputePath(source, taxedTarget, match.Obstacles);
        Assert.Equal(0, TowerThreatEstimator.EstimateUnitsLost(straightToBase3, tower.Position, tower.Level, speed));
        Assert.Equal(5, TowerThreatEstimator.EstimateUnitsLost(routedToBase3, tower.Position, tower.Level, speed));

        var decision = InvokeClause("TryAttack", new AiBrain(ai), match);

        Assert.True(decision.IsSend);
        Assert.Equal(match.Bases[4].Id, decision.Command.SourceBaseId);
        Assert.Equal(match.Bases[2].Id, decision.Command.TargetBaseId);
    }

    /// <summary>
    /// <c>AiBrain</c> no longer carries a private straight-line <c>Distance</c> helper - every
    /// measurement goes through <see cref="PathCalculator"/> (ARCHITECTURE.md §5). Dead code is
    /// deleted, not commented out, so its absence is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void AiBrain_HasNoPrivateStraightLineDistanceHelperLeft()
    {
        var leftovers = typeof(AiBrain)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == "Distance")
            .ToList();

        Assert.Empty(leftovers);
    }

    // --- D. Nothing else changes ---

    /// <summary>
    /// The bit-identity that protects Small and Big: with no obstacles, <c>ComputePath</c> returns
    /// the two-waypoint straight path whose length is <c>sqrt(dx^2 + dy^2)</c> - the very arithmetic
    /// <c>AiBrain.Distance</c> performed before this feature deleted it. Asserted with <c>==</c>, not
    /// an epsilon compare: an approximate agreement would let a rounding difference change an AI
    /// decision on the two obstacle-free maps, which is exactly what "every existing test passes
    /// unchanged" rests on.
    /// </summary>
    [Fact]
    public void ComputePath_WithNoObstacles_HasExactlyTheStraightLineLength()
    {
        var slots = MapCatalog.Small.Slots;

        for (var i = 0; i < slots.Count; i++)
        {
            for (var j = 0; j < slots.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var from = slots[i].Position;
                var to = slots[j].Position;
                var dx = to.X - from.X;
                var dy = to.Y - from.Y;
                var straightLength = Math.Sqrt((dx * dx) + (dy * dy));

                var path = PathCalculator.ComputePath(from, to, _noObstacles);

                Assert.Equal(new[] { from, to }, path.Waypoints);
                Assert.True(
                    path.Length == straightLength,
                    FormattableString.Invariant($"Slot {i} to slot {j}: route {path.Length:R} is not bit-identical to the straight line {straightLength:R}."));
            }
        }
    }

    /// <summary>
    /// The same identity where it actually bites: on an obstacle-free map an unowned tower's
    /// estimated toll is identical whether the estimate walks the computed route or a hand-built
    /// two-waypoint path, because they are the same two points.
    /// </summary>
    [Fact]
    public void EstimateUnitsLost_OnAnObstacleFreeMap_MatchesTheHandBuiltStraightPath()
    {
        var match = new Match(MapCatalog.Big);
        var tower = match.Bases.First(b => b.Type == BaseType.Tower);
        var from = match.Bases[0].Position;
        var to = match.Bases[1].Position;

        var routed = PathCalculator.ComputePath(from, to, match.Obstacles);
        var handBuilt = new ArmyPath(new[] { from, to }, routed.Length);

        Assert.Equal(
            TowerThreatEstimator.EstimateUnitsLost(handBuilt, tower.Position, tower.Level, Match.ArmySpeedUnitsPerTick),
            TowerThreatEstimator.EstimateUnitsLost(routed, tower.Position, tower.Level, Match.ArmySpeedUnitsPerTick));
    }

    // --- Layouts ---

    /// <summary>
    /// A wall spanning x 0.40..0.60, y 0.05..0.90, with a base either side of it. Slot order is the
    /// base id order: 0 human start, 1 AI start, 2 and 3 neutral.
    /// </summary>
    private static Match WalledMatch() => new(new MapDefinition(
        new[]
        {
            new MapSlot(new MapPoint(0.98, 0.98), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.65, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.02, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.35, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        },
        new[] { new MapObstacle(minX: 0.40, minY: 0.05, maxX: 0.60, maxY: 0.90) }));

    /// <summary>
    /// The same wall, with the two candidate targets as neutral forges: a forge never produces, so
    /// both targets' predicted garrisons - and therefore their predicted morale swings - are equal
    /// whatever their arrival ticks, leaving the target order as the only deciding key.
    /// </summary>
    private static Match ForgeTargetMatch() => new(new MapDefinition(
        new[]
        {
            new MapSlot(new MapPoint(0.98, 0.98), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.65, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.35, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Forge, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.95, 0.05), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Forge, LevelTable.MinLevel),
        },
        new[] { new MapObstacle(minX: 0.40, minY: 0.05, maxX: 0.60, maxY: 0.90) }));

    private static double StraightLine(Base a, Base b)
    {
        var dx = b.Position.X - a.Position.X;
        var dy = b.Position.Y - a.Position.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
