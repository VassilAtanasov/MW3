# gate.ps1 — the single quality gate for MW3.
# Run by: developers, Ivan (Claude Code), the Stop hook, and GitHub Actions CI.
# Auto-detects project state: passes trivially until application code exists.
# Stack is .NET end-to-end (MonoGame client, no server yet), so there is a single solution leg.
# Style and analyzer rules are NOT a separate step: .editorconfig severities become build
# diagnostics via EnforceCodeStyleInBuild in Directory.Build.props, so `dotnet build -warnaserror`
# below is what fails on them.
# On success, writes .gate-stamp (a hash of the working tree) so the Stop hook can skip
# re-running the gate when nothing changed since the last green run.
# Compatible with Windows PowerShell 5.1 and PowerShell Core (pwsh, incl. Linux CI).

$ErrorActionPreference = 'Continue'
$repoRoot = $PSScriptRoot

# Hash the exact working-tree content (tracked + untracked, staged + unstaged) without touching
# the real index. Returns '' if git is unavailable. Must match the copy in stop-gate.ps1.
function Get-WorkingTreeHash {
    param([string]$Root)
    $tmpIndex = Join-Path ([IO.Path]::GetTempPath()) ("gate-index-" + [Guid]::NewGuid().ToString('N'))
    $prev = $env:GIT_INDEX_FILE
    try {
        $env:GIT_INDEX_FILE = $tmpIndex
        git -C $Root read-tree HEAD 2>$null
        git -C $Root add -A 2>$null
        $tree = git -C $Root write-tree 2>$null
        if ($tree) { return "$tree".Trim() } else { return '' }
    } catch {
        return ''
    } finally {
        $env:GIT_INDEX_FILE = $prev   # $null assignment removes the variable
        Remove-Item $tmpIndex -Force -ErrorAction SilentlyContinue
    }
}

# Runs one leg's steps sequentially.
$legRunner = {
    param($LegName, $Steps)
    $failures = @()
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($step in $Steps) {
        $lines.Add("")
        $lines.Add("=== $($step.Name) ===")
        Push-Location $step.WorkDir
        try {
            try {
                $out = @(Invoke-Expression $step.Command 2>&1 | ForEach-Object { "$_" })
                $exit = $LASTEXITCODE
            } catch {
                $out = @("$_")
                $exit = 1
            }
            foreach ($line in $out) { $lines.Add($line) }
            if ($exit -ne 0) {
                $failures += $step.Name
                $lines.Add("--- FAILED: $($step.Name) (exit $exit)")
            } else {
                $lines.Add("--- OK: $($step.Name)")
            }
        } finally {
            Pop-Location
        }
    }
    [PSCustomObject]@{ Leg = $LegName; Failures = $failures; Transcript = ($lines -join "`n") }
}

# Find the solution: repo root first, then any first-level subdirectory (src/, server/, game/...).
# Layout is settled by /discover; this detection deliberately does not assume one.
# .slnx is the .NET 10 default solution format; .sln is still accepted.
$slnPattern = '\.slnx?$'
$solution = Get-ChildItem -Path $repoRoot -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match $slnPattern } | Select-Object -First 1
if (-not $solution) {
    $solution = Get-ChildItem -Path $repoRoot -File -Depth 1 -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match $slnPattern -and $_.FullName -notmatch '[\\/](bin|obj|artifacts)[\\/]' } |
                Select-Object -First 1
}

if (-not $solution) {
    Write-Host "GATE: no application code yet (no *.sln/*.slnx found) - gate passes trivially." -ForegroundColor Green
    exit 0
}

$sln = $solution.FullName

$steps = @(
    # Formatting drift fails here rather than depending on the PostToolUse hook having fired.
    # Style/analyzer rules themselves are enforced by the build: .editorconfig severities are
    # promoted to build diagnostics by EnforceCodeStyleInBuild in Directory.Build.props.
    @{ Name = 'dotnet format (verify no changes)'; WorkDir = $repoRoot; Command = "dotnet format `"$sln`" --verify-no-changes --verbosity minimal" },
    # -m:1 forces MSBuild to a single node. Without it, building this solution in parallel with
    # MonoGame content in play (MW3.Desktop and MW3.Android both driving MonoGame.Content.Builder.Task
    # against src/MW3.Game/Content/Content.mgcb) reliably crashes with a raw IOException writing
    # .mgcontent - a known upstream race (MonoGame/MonoGame#7409), not something in this repo's
    # control. Serializing the build is the documented-nowhere-else but verified fix.
    @{ Name = 'dotnet build (warnings as errors)'; WorkDir = $repoRoot; Command = "dotnet build `"$sln`" -warnaserror -m:1 --nologo" }
)

# Coverage runs only when the test projects actually reference coverlet.collector, so this
# degrades to a plain `dotnet test` in projects that have not opted in.
$projFiles = @(Get-ChildItem -Path $repoRoot -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|artifacts)[\\/]' })
$hasCoverlet = $false
if ($projFiles.Count -gt 0) {
    $hasCoverlet = [bool](Select-String -Path $projFiles.FullName -Pattern 'coverlet\.collector' -Quiet -ErrorAction SilentlyContinue)
}

# Minimum line coverage percentage. 0 = report only. Raise it via the environment (CI or shell)
# once the suite is established; the gate then fails when coverage drops below it.
$coverageMin = 0
if ($env:GATE_COVERAGE_MIN) { $coverageMin = [double]$env:GATE_COVERAGE_MIN }

if ($hasCoverlet) {
    $coverageDir = Join-Path $repoRoot '.coverage'
    # Note: the leg runner discards a step's collected output if the step throws, so the test
    # transcript is carried in the exception message rather than written to the pipeline.
    $coverageCmd = @'
Remove-Item -Recurse -Force "__DIR__" -ErrorAction SilentlyContinue
$testOut = @(dotnet test "__SLN__" --nologo --no-build --results-directory "__DIR__" --collect "XPlat Code Coverage" 2>&1 | ForEach-Object { "$_" })
$testExit = $LASTEXITCODE
function Fail([string]$Reason) { throw (($testOut + $Reason) -join "`n") }
if ($testExit -ne 0) { Fail "dotnet test failed (exit $testExit)" }
$reports = @(Get-ChildItem -Path "__DIR__" -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue)
if ($reports.Count -eq 0) { Fail "coverage: no cobertura report was produced" }
$covered = 0; $total = 0
foreach ($r in $reports) {
    $xml = [xml](Get-Content $r.FullName -Raw)
    $covered += [int]$xml.coverage.GetAttribute('lines-covered')
    $total   += [int]$xml.coverage.GetAttribute('lines-valid')
}
$pct = if ($total -gt 0) { [math]::Round(100.0 * $covered / $total, 2) } else { 0 }
$summary = "coverage: $pct% ($covered/$total lines, minimum __MIN__%)"
if ($pct -lt __MIN__) { Fail $summary }
$testOut + $summary
'@
    $coverageCmd = $coverageCmd.Replace('__DIR__', $coverageDir).Replace('__SLN__', $sln).Replace('__MIN__', "$coverageMin")
    $steps += @{ Name = "dotnet test (coverage, min ${coverageMin}%)"; WorkDir = $repoRoot; Command = $coverageCmd }
} else {
    $steps += @{ Name = 'dotnet test'; WorkDir = $repoRoot; Command = "dotnet test `"$sln`" --nologo --no-build" }
}

$results = @(& $legRunner 'dotnet' $steps)

$failures = @()
foreach ($result in $results) {
    Write-Host ""
    Write-Host "===== LEG: $($result.Leg) =====" -ForegroundColor Cyan
    Write-Host $result.Transcript
    $failures += @($result.Failures)
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "GATE FAILED - $($failures.Count) step(s):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

$treeHash = Get-WorkingTreeHash $repoRoot
if ($treeHash) {
    Set-Content -Path (Join-Path $repoRoot '.gate-stamp') -Value $treeHash -Encoding Ascii
}
Write-Host "GATE PASSED - all steps green." -ForegroundColor Green
exit 0
