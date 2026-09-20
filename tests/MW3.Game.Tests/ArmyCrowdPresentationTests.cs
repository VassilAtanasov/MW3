namespace MW3.Game.Tests;

/// <summary>
/// Headless coverage of FR-3's marching-crowd layout - no graphics device.
/// </summary>
public class ArmyCrowdPresentationTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(4, 3, 2)]
    [InlineData(8, 4, 2)]
    [InlineData(12, 5, 3)]
    public void ComputeGrid_SeatsEverySprite(int spriteCount, int expectedFiles, int expectedRanks)
    {
        ArmyCrowdPresentation.ComputeGrid(spriteCount, out var files, out var ranks);

        Assert.Equal(expectedFiles, files);
        Assert.Equal(expectedRanks, ranks);
        Assert.True(files * ranks >= spriteCount);
        Assert.True(files >= ranks, "ranked files should be wider than deep");
    }

    [Fact]
    public void ComputeSlots_OneUnit_IsSingleBodyAtCentre()
    {
        var slots = new List<ArmyCrowdPresentation.Slot>();
        ArmyCrowdPresentation.ComputeSlots(unitCount: 1, slots);

        var slot = Assert.Single(slots);
        Assert.Equal(0f, slot.Forward);
        Assert.Equal(0f, slot.Lateral);
    }

    [Fact]
    public void ComputeSlots_EightUnits_FormRankedFiles()
    {
        var slots = new List<ArmyCrowdPresentation.Slot>();
        ArmyCrowdPresentation.ComputeSlots(unitCount: 8, slots);

        Assert.Equal(8, slots.Count);
        var maxForward = slots.Max(s => s.Forward);
        var minForward = slots.Min(s => s.Forward);
        var maxLateral = slots.Max(s => Math.Abs(s.Lateral));
        Assert.True(maxLateral > 0f, "multiple files across");
        Assert.True(maxForward > minForward, "multiple ranks deep");
    }

    [Fact]
    public void SpriteCount_CapsAtMaxSpritesPerArmy()
    {
        Assert.Equal(ArmyCrowdPresentation.MaxSpritesPerArmy, ArmyCrowdPresentation.SpriteCount(200));
        Assert.Equal(5, ArmyCrowdPresentation.SpriteCount(5));
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(4, 20)]
    [InlineData(8, 40)]
    public void FootprintArea_GrowsWithUnitCount(int smaller, int larger)
    {
        var smallArea = ArmyCrowdPresentation.FootprintArea(smaller, waveIndex: 1, waveCount: 1);
        var largeArea = ArmyCrowdPresentation.FootprintArea(larger, waveIndex: 1, waveCount: 1);

        Assert.True(largeArea > smallArea, "crowd area should communicate strength");
    }

    [Fact]
    public void FormationScale_TrailingWave_IsSmallerThanLead()
    {
        var lead = ArmyCrowdPresentation.FormationScale(waveIndex: 1, waveCount: 3);
        var trail = ArmyCrowdPresentation.FormationScale(waveIndex: 3, waveCount: 3);

        Assert.True(lead > trail);
        Assert.Equal(1f, lead);
    }

    [Fact]
    public void ComputeMarchDirection_OnStraightPath_PointsTowardTarget()
    {
        var waypoints = new[] { new MapPoint(0.0, 0.5), new MapPoint(1.0, 0.5) };
        ArmyCrowdPresentation.ComputeMarchDirection(
            waypoints,
            length: 1.0,
            launchTick: 0,
            arrivalTick: 100,
            currentTick: 50,
            out var forwardX,
            out var forwardY);

        Assert.True(forwardX > 0.9);
        Assert.True(Math.Abs(forwardY) < 0.1);
    }

    [Fact]
    public void ComputeSlots_ClearsOutputFirst()
    {
        var slots = new List<ArmyCrowdPresentation.Slot>
        {
            new(9f, 9f),
            new(9f, 9f),
        };

        ArmyCrowdPresentation.ComputeSlots(unitCount: 2, slots);

        Assert.Equal(2, slots.Count);
        Assert.DoesNotContain(slots, s => s.Forward == 9f);
    }
}
