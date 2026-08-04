<#
.SYNOPSIS
    Stage the Syxx Windows source tree to the shared folder (Z:\) for the Mac side.
.DESCRIPTION
    Mirrors this repo (source + docs/ledgers, WITHOUT build output) to the destination.
    Reverse direction of the Mac's syxx-stage/syxx-push tooling: run this after a VM work
    session so the host always has the latest coherent tree, including CHANGELOG.md and
    MEMORY.md (which carry the owed macOS-twin worklist).

    NOTE: /MIR mirrors — files deleted here are also deleted from the destination copy.
    The destination is a staging replica, never the only copy of anything.
.PARAMETER Destination
    Target folder. Default: Z:\Syxx-Windows
#>
param(
    [string]$Destination = "Z:\Syxx-Windows"
)

$Root = Split-Path -Parent $PSCommandPath

if (-not (Test-Path (Split-Path -Qualifier $Destination))) {
    Write-Host "Destination drive not available: $Destination" -ForegroundColor Red
    exit 1
}

Write-Host "=== Staging Syxx-Windows to shared folder ===" -ForegroundColor Cyan
Write-Host "From: $Root"        -ForegroundColor Gray
Write-Host "To:   $Destination" -ForegroundColor Gray

# /MIR mirror; exclude build output, IDE state, per-user files, and local captures.
robocopy $Root $Destination /MIR `
    /XD bin obj .vs TestResults packages captures node_modules `
    /XF *.user *.suo `
    /R:2 /W:2 /NFL /NDL /NP

# Robocopy exit codes 0-7 are success variants (0 = nothing to copy, 1 = files copied,
# 2/3 = extras handled, etc.); 8+ are real failures.
if ($LASTEXITCODE -ge 8) {
    Write-Host "STAGING FAILED (robocopy exit $LASTEXITCODE)" -ForegroundColor Red
    exit 1
}
Write-Host "=== Staged OK (robocopy exit $LASTEXITCODE) ===" -ForegroundColor Green
exit 0
