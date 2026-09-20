namespace MW3.Game;

/// <summary>
/// Pure layout arithmetic for FR-3's marching crowds - decides nothing about the match and draws
/// nothing, mirroring <see cref="WaveColumnPresentation"/>. <see cref="MatchScreen"/> owns every
/// MonoGame type and every reused buffer; this class only ever sees plain data.
/// </summary>
internal static class ArmyCrowdPresentation
{
    /// <summary>
    /// One sprite per simulated unit up to this cap. Above it the formation still grows in area via
    /// spacing rather than by hiding bodies, so a large send reads larger without one disc.
    /// </summary>
    public const int MaxSpritesPerArmy = 64;

    /// <summary>
    /// Wider-than-deep bias when choosing file count: sqrt(count * ratio) files, then enough ranks to
    /// seat every sprite. Values above 1 favour ranked files over a deep single file.
    /// </summary>
    public const float FileRankAspectRatio = 1.6f;

    /// <summary>
    /// Sprite diameter as a fraction of the viewport's smaller dimension - kept below a base radius
    /// so a crowd never dwarfs the building it launched from.
    /// </summary>
    public const float SpriteDiameterFractionOfMinDimension = 0.028f;

    /// <summary>
    /// Centre-to-centre spacing between files, as a multiple of sprite diameter.
    /// </summary>
    public const float FileSpacingMultiplier = 0.85f;

    /// <summary>
    /// Centre-to-centre spacing between ranks, as a multiple of sprite diameter.
    /// </summary>
    public const float RankSpacingMultiplier = 0.90f;

    /// <summary>
    /// A slot in the army's local formation: <see cref="Forward"/> steps toward the destination,
    /// <see cref="Lateral"/> steps to the marcher's left. Both are in sprite-spacing units.
    /// </summary>
    public readonly struct Slot
    {
        public Slot(float forward, float lateral)
        {
            Forward = forward;
            Lateral = lateral;
        }

        public float Forward { get; }

        public float Lateral { get; }
    }

    /// <summary>
    /// How many individual sprites to draw for an army carrying <paramref name="unitCount"/> units.
    /// </summary>
    public static int SpriteCount(int unitCount) =>
        unitCount <= 0 ? 0 : Math.Min(unitCount, MaxSpritesPerArmy);

    /// <summary>
    /// The ranked-file grid that seats <paramref name="spriteCount"/> bodies. Files run lateral;
    /// ranks run forward toward the destination.
    /// </summary>
    public static void ComputeGrid(int spriteCount, out int files, out int ranks)
    {
        if (spriteCount <= 0)
        {
            files = 0;
            ranks = 0;
            return;
        }

        if (spriteCount == 1)
        {
            files = 1;
            ranks = 1;
            return;
        }

        files = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(spriteCount * FileRankAspectRatio)));
        ranks = (spriteCount + files - 1) / files;
    }

    /// <summary>
    /// Fills <paramref name="output"/> with one <see cref="Slot"/> per sprite, front rank first and
    /// centred on the army's path position. Clears <paramref name="output"/> first and never
    /// allocates.
    /// </summary>
    public static void ComputeSlots(int unitCount, List<Slot> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();

        var spriteCount = SpriteCount(unitCount);
        if (spriteCount <= 0)
        {
            return;
        }

        ComputeGrid(spriteCount, out var files, out var ranks);

        var placed = 0;
        for (var rank = 0; rank < ranks && placed < spriteCount; rank++)
        {
            var inRank = Math.Min(files, spriteCount - placed);
            var forward = ((ranks - 1 - rank) - ((ranks - 1) * 0.5f)) * RankSpacingMultiplier;
            for (var file = 0; file < inRank; file++)
            {
                var lateral = (file - ((inRank - 1) * 0.5f)) * FileSpacingMultiplier;
                output.Add(new Slot(forward, lateral));
                placed++;
            }
        }
    }

    /// <summary>
    /// Scales the formation footprint for trailing waves of a multi-wave send, reusing FR-4's taper
    /// endpoints so a single-wave send stays at scale 1.
    /// </summary>
    public static float FormationScale(int waveIndex, int waveCount) =>
        WaveColumnPresentation.RadiusFraction(waveIndex, waveCount, leadRadiusFraction: 1f, trailingRadiusFraction: 0.55f);

    /// <summary>
    /// Unit direction along the army's path at <paramref name="currentTick"/>, in normalized map
    /// space. When the path has no length at this tick, returns (1, 0).
    /// </summary>
    public static void ComputeMarchDirection(
        IReadOnlyList<MapPoint> waypoints,
        double length,
        long launchTick,
        long arrivalTick,
        long currentTick,
        out double forwardX,
        out double forwardY)
    {
        ArgumentNullException.ThrowIfNull(waypoints);

        var progress = ArmyPathMath.ProgressAt(launchTick, arrivalTick, currentTick);
        const double epsilon = 0.01;
        var from = ArmyPathMath.PositionAtProgress(waypoints, length, Math.Max(0.0, progress - epsilon));
        var to = ArmyPathMath.PositionAtProgress(waypoints, length, Math.Min(1.0, progress + epsilon));
        forwardX = to.X - from.X;
        forwardY = to.Y - from.Y;
        var magnitude = Math.Sqrt((forwardX * forwardX) + (forwardY * forwardY));
        if (magnitude < 1e-9)
        {
            forwardX = 1.0;
            forwardY = 0.0;
            return;
        }

        forwardX /= magnitude;
        forwardY /= magnitude;
    }

    /// <summary>
    /// Approximate crowd footprint area in normalized viewport-fraction squared units, for tests
    /// that guard "larger send = larger crowd" without a graphics device.
    /// </summary>
    public static float FootprintArea(int unitCount, int waveIndex, int waveCount)
    {
        var spriteCount = SpriteCount(unitCount);
        if (spriteCount <= 0)
        {
            return 0f;
        }

        ComputeGrid(spriteCount, out var files, out var ranks);
        var scale = FormationScale(waveIndex, waveCount);
        var diameter = SpriteDiameterFractionOfMinDimension * scale;
        var width = Math.Max(diameter, ((files - 1) * FileSpacingMultiplier * diameter) + diameter);
        var depth = Math.Max(diameter, ((ranks - 1) * RankSpacingMultiplier * diameter) + diameter);
        return width * depth;
    }
}
