# gate.ps1 — the single quality gate for MW3.
# Run by: developers, Ivan (Claude Code), the Stop hook, and GitHub Actions CI.
# Auto-detects project state: passes trivially until application code exists.
# Stack is .NET end-to-end (game client + server), so there is a single solution leg.
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

$legs = @(
    @{
        Name  = 'dotnet'
        Steps = @(
            @{ Name = 'dotnet build (warnings as errors)'; WorkDir = $repoRoot; Command = "dotnet build `"$($solution.FullName)`" -warnaserror --nologo" },
            @{ Name = 'dotnet format (verify no changes)'; WorkDir = $repoRoot; Command = "dotnet format `"$($solution.FullName)`" --verify-no-changes --verbosity minimal" },
            @{ Name = 'dotnet test'; WorkDir = $repoRoot; Command = "dotnet test `"$($solution.FullName)`" --nologo --no-build" }
        )
    }
)

$results = @(& $legRunner $legs[0].Name $legs[0].Steps)

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
