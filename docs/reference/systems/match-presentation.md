# Match presentation

> Visual layer for the live match screen: buildings, armies in flight, and (in later features) HUD
> chrome. Draw-only — nothing here changes simulation state or `--dump-state` output.

## 2. How it behaves today

### 2.3 Armies in flight (FR-3)

`MatchScreen.DrawArmiesInFlight` renders each in-flight `ArmySnapshot` as a ranked file of unit
sprites along the path the simulation already owns (`ArmyPathMath.PositionAt` at the army centre).
`ArmyCrowdPresentation` decides how many sprites to draw, their grid layout, march direction, and
trailing-wave taper; `UnitSpriteRasterizer` supplies the one character silhouette (round head, small
body, feet on the baseline), owner-tinted via `UnitSpriteAtlas`. Strength is read from crowd area,
not a number on a disc. Opposing columns pass through each other because each army is drawn
independently with no transit interaction.

## 3. Tuning values

| Symbol | Value | Role |
|---|---|---|
| `ArmyCrowdPresentation.MaxSpritesPerArmy` | 64 | Cap on individual bodies drawn per army |
| `ArmyCrowdPresentation.FileRankAspectRatio` | 1.6 | Wider-than-deep bias when choosing file count |
| `ArmyCrowdPresentation.SpriteDiameterFractionOfMinDimension` | 0.028 | Unit sprite size vs viewport |
| `ArmyCrowdPresentation.FileSpacingMultiplier` | 0.85 | Lateral spacing between files |
| `ArmyCrowdPresentation.RankSpacingMultiplier` | 0.90 | Forward spacing between ranks |
| `ArmyCrowdPresentation.FormationScale` trailing endpoint | 0.55 | Trailing-wave footprint taper (reuses FR-4's lead/trail ratio endpoints) |
| `UnitSpriteRasterizer.Size` | 48 | Raster resolution for the marching unit |
