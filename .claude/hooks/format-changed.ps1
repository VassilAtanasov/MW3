# PostToolUse hook (Edit|Write): auto-format the file that was just changed.
# Reads the hook payload from stdin; always exits 0 (formatting must never block work).
# Stack is .NET only — see gate.ps1 for the matching solution-detection rule.

$ErrorActionPreference = 'SilentlyContinue'
try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $file = $payload.tool_input.file_path
    if (-not $file -or -not (Test-Path $file)) { exit 0 }

    $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $ext = [System.IO.Path]::GetExtension($file).ToLowerInvariant()

    if ($ext -eq '.cs') {
        $slnPattern = '\.slnx?$'
        $sln = Get-ChildItem -Path $repoRoot -File |
               Where-Object { $_.Name -match $slnPattern } | Select-Object -First 1
        if (-not $sln) {
            $sln = Get-ChildItem -Path $repoRoot -File -Depth 1 -Recurse |
                   Where-Object { $_.Name -match $slnPattern -and $_.FullName -notmatch '[\\/](bin|obj|artifacts)[\\/]' } |
                   Select-Object -First 1
        }
        if ($sln) { dotnet format $sln.FullName --include $file --verbosity quiet | Out-Null }
    }
} catch { }
exit 0
