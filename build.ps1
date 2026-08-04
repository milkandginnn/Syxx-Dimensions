<#
.SYNOPSIS
    Build the Syxx Windows app: GUI (SyxxUI) + Syxx.Kit + Syxx console + tests, Debug AND Release.
.DESCRIPTION
    Run from the shared build folder (C:\Users\masterrabbit\Software\Syxx\Syxx-Windows).
    Per AGENTS.md the GUI is built first (GUI > CLI) and both Debug and Release are built.
    WinUI/SyxxUI has no "Any CPU" platform, so a bare `dotnet build Syxx.sln` can skip or error on it;
    this builds SyxxUI explicitly for the machine architecture (x64 or arm64).
.NOTES
    Tests: run .\run-tests.ps1 after this.
#>

$Root = Split-Path -Parent $PSCommandPath
Set-Location $Root

$gui  = "Windows\dotNET\SyxxUI\SyxxUI.csproj"
$kit  = "Windows\dotNET\Syxx.Kit\Syxx.Kit.csproj"
$cli  = "Windows\dotNET\Syxx\Syxx.csproj"
$test = "Windows\dotNET\Syxx.Tests\Syxx.Tests.csproj"
$plat = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }

Write-Host "=== Syxx Windows Build ===" -ForegroundColor Cyan
Write-Host "Root:     $Root"        -ForegroundColor Gray
Write-Host "GUI arch: $plat"        -ForegroundColor Gray
Write-Host "Order: GUI first (AGENTS.md GUI > CLI); Debug and Release both built." -ForegroundColor Gray

function Invoke-Build($label, $buildArgs) {
    Write-Host ""
    Write-Host ">>> $label" -ForegroundColor Yellow
    dotnet build @buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $label" -ForegroundColor Red
        exit 1
    }
}

foreach ($cfg in @("Debug", "Release")) {
    # GUI first. WinUI needs an explicit platform (Any CPU is not valid for it).
    Invoke-Build "GUI  SyxxUI     $cfg ($plat)" @($gui, "-c", $cfg, "-p:Platform=$plat")
    # Shared core + console + tests (net10.0, Any CPU).
    Invoke-Build "Kit  Syxx.Kit   $cfg"         @($kit,  "-c", $cfg)
    Invoke-Build "CLI  Syxx       $cfg"         @($cli,  "-c", $cfg)
    Invoke-Build "Test Syxx.Tests $cfg"         @($test, "-c", $cfg)
}

Write-Host ""
Write-Host "=== BUILD SUCCEEDED: GUI + Kit + CLI + Tests, Debug and Release ===" -ForegroundColor Green
$exe = "Windows\dotNET\SyxxUI\bin\$plat\Release\net10.0-windows10.0.26100.0\SyxxUI.exe"
Write-Host "GUI exe: $exe" -ForegroundColor Gray
Write-Host "Run tests with: .\run-tests.ps1" -ForegroundColor Gray
