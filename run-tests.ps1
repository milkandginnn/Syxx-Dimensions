<#
.SYNOPSIS
    Builds and runs all Syxx Windows tests.
    Run from the Syxx-Windows directory.
#>

$Root = Split-Path -Parent $PSCommandPath
Set-Location $Root

Write-Host "=== Syxx Windows Test Runner ===" -ForegroundColor Cyan
Write-Host "Root: $Root" -ForegroundColor Gray
Write-Host ""

# ── Restore NuGet packages ──────────────────────────────────────────────
Write-Host ">>> dotnet restore Windows/Syxx.sln" -ForegroundColor Yellow
dotnet restore Windows/Syxx.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: dotnet restore" -ForegroundColor Red
    exit 1
}
Write-Host ""

# ── Build (no tests) ────────────────────────────────────────────────────
Write-Host ">>> dotnet build Windows/Syxx.sln" -ForegroundColor Yellow
dotnet build Windows/Syxx.sln --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: dotnet build" -ForegroundColor Red
    exit 1
}
Write-Host ""

# ── Run tests ───────────────────────────────────────────────────────────
Write-Host ">>> dotnet test Windows/Syxx.sln --no-build -v n" -ForegroundColor Yellow
dotnet test Windows/Syxx.sln --no-build -v n
$exitCode = $LASTEXITCODE
Write-Host ""

if ($exitCode -eq 0) {
    Write-Host "=== ALL TESTS PASSED ===" -ForegroundColor Green
} else {
    Write-Host "=== SOME TESTS FAILED (exit code: $exitCode) ===" -ForegroundColor Red
    Write-Host "Check output above for details." -ForegroundColor Red
}

exit $exitCode
