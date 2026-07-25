# Stop hook: Ivan may not end a working turn while the quality gate fails.
# - Skips when this stop was already triggered by a previous Stop-hook block (stop_hook_active),
#   letting Ivan report an unfixable failure instead of looping forever.
# - Skips chat-only turns (no changed .NET source/project files).
# - Skips when .gate-stamp matches the current working tree — gate.ps1 already ran green on this
#   exact code, so re-running it would only repeat a known-good result.
# - Exit 2 + stderr feeds the gate failure back to Ivan, forcing him to keep fixing.

$ErrorActionPreference = 'SilentlyContinue'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
    if ($payload.stop_hook_active) { exit 0 }
} catch { }

# Source = anything the .NET build can see. Layout-agnostic on purpose: /discover settles the
# directory structure, so match by file type rather than by top-level folder.
# --untracked-files=all is load-bearing: plain --porcelain collapses a new untracked directory to a
# single "?? src/" entry, so a brand-new project's .cs files would never match the filter below and
# the gate would be silently skipped on exactly the turn that created them.
$changes = git -C $repoRoot status --porcelain --untracked-files=all 2>$null
$sourceChanged = $changes | Where-Object { $_ -match '\.(cs|csproj|fsproj|sln|slnx|props|targets|razor|axaml|xaml|resx)"?$' }
if (-not $sourceChanged) { exit 0 }

# Hash the exact working-tree content without touching the real index. Must match gate.ps1's copy.
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

$stampPath = Join-Path $repoRoot '.gate-stamp'
if (Test-Path $stampPath) {
    $stamp = (Get-Content $stampPath -TotalCount 1 2>$null)
    $current = Get-WorkingTreeHash $repoRoot
    if ($stamp -and $current -and ("$stamp".Trim() -eq $current)) { exit 0 }
}

$gateOutput = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'gate.ps1') 2>&1
if ($LASTEXITCODE -ne 0) {
    $tail = ($gateOutput | Select-Object -Last 40) -join "`n"
    [Console]::Error.WriteLine("Quality gate FAILED - you cannot finish this turn until gate.ps1 passes. Fix the failures below, re-run ./gate.ps1, and only stop when it is green.`n$tail")
    exit 2
}
exit 0
