<#
.SYNOPSIS
    Build a distributable Syxx installer package for Windows.
.DESCRIPTION
    Creates a self-contained publish directory and optionally wraps it in a
    self-extracting archive or MSI (if WiX toolset is installed).
    Run from the repository root.
.PARAMETER Configuration
    Build configuration: Debug or Release. Default: Release.
.PARAMETER Platform
    Target platform: x64 or arm64. Default: auto-detect from processor.
.NOTES
    Per AGENTS.md: GUI > CLI, both Debug and Release built. This script focuses
    on the Release installer package.
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x64", "arm64")]
    [string]$Platform
)

$Root = Split-Path -Parent $PSCommandPath
Set-Location $Root

if (-not $Platform) {
    $Platform = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
}

# Derive version from SyxxConfig.cs.
#
# BUG FIXED HERE: this searched for 'public static string Version'. The declaration is
# 'public const string Version = "2.0.0";' (Windows/dotNET/Syxx.Kit/SyxxConfig.cs:13), so the
# pattern never matched and the `else` branch silently stamped 0.0.0 onto the artifact NAME —
# a fabricated version presented as the measured one, on the file users would download. The
# pattern now matches the real declaration, and a miss is a hard failure: refusing to build is
# correct, shipping "Syxx-0.0.0-...zip" is not.
$ConfigPath = "Windows\dotNET\Syxx.Kit\SyxxConfig.cs"
$VersionLine = Select-String -Path $ConfigPath -Pattern 'const\s+string\s+Version\s*=' | Select-Object -First 1
if (-not $VersionLine -or $VersionLine.Line -notmatch '"([^"]+)"') {
    Write-Host "FAILED: could not read the version from $ConfigPath." -ForegroundColor Red
    Write-Host "Refusing to stamp a placeholder version onto a distributable artifact." -ForegroundColor Red
    exit 1
}
$Version = $Matches[1]
$Build = git rev-list --count HEAD 2>$null
if (-not $Build) { $Build = 1 }

Write-Host "=== Building Syxx ${Version} (build ${Build}) for Windows ${Platform} ===" -ForegroundColor Cyan

# Step 1: Publish the GUI app as self-contained
Write-Host "--- Publishing self-contained GUI app ---" -ForegroundColor Yellow
$PublishDir = "dist\publish-win-${Platform}"
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }

dotnet publish Windows\dotNET\SyxxUI\SyxxUI.csproj `
    -c $Configuration `
    -p:Platform=$Platform `
    -r win-${Platform} `
    --self-contained true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: dotnet publish SyxxUI" -ForegroundColor Red
    exit 1
}

# D114: publish the syxx CLI into the same directory. CageView.LocateCageBinary looks
# for syxx.exe next to the GUI exe — without this publish step the capture binary is
# structurally absent from every shipped zip and Cage is non-functional.
Write-Host "--- Publishing syxx CLI alongside GUI ---" -ForegroundColor Yellow
dotnet publish Windows\dotNET\Syxx\Syxx.csproj `
    -c $Configuration `
    -r win-${Platform} `
    --self-contained false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: dotnet publish Syxx CLI" -ForegroundColor Red
    exit 1
}

# D114: assert the CLI is present before zipping so the omission cannot recur silently.
if (-not (Test-Path "$PublishDir\Syxx.exe")) {
    Write-Host "FAILED: Syxx.exe not found in $PublishDir after CLI publish." -ForegroundColor Red
    exit 1
}

# Step 2: Create a zip archive for distribution
Write-Host "--- Creating zip archive ---" -ForegroundColor Yellow
$ZipName = "Syxx-${Version}-${Build}-win-${Platform}.zip"
$ZipPath = "dist\${ZipName}"

# Compress to zip
Compress-Archive -Path "${PublishDir}\*" -DestinationPath $ZipPath -Force

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Package: $ZipPath" -ForegroundColor Gray
$Size = (Get-Item $ZipPath).Length / 1MB
Write-Host ("Size: {0:N1} MB" -f $Size) -ForegroundColor Gray
Write-Host ""
Write-Host "To install: Extract and run SyxxUI.exe" -ForegroundColor Gray
# D114: signtool cannot sign a .zip — sign the PE files individually before archiving.
Write-Host "To sign (requires code signing certificate):" -ForegroundColor Gray
Write-Host "  signtool sign /f cert.pfx /p password /tr http://timestamp.digicert.com /td sha256 /fd sha256 ${PublishDir}\SyxxUI.exe" -ForegroundColor DarkGray
Write-Host "  signtool sign /f cert.pfx /p password /tr http://timestamp.digicert.com /td sha256 /fd sha256 ${PublishDir}\Syxx.exe" -ForegroundColor DarkGray

# Step 3: Optional - create MSI if WiX is installed and installer.wxs exists.
# D114: installer.wxs has never been committed; guard the hint on its existence so the
# printed command is not a reference to a nonexistent file.
$WixInstalled = Get-Command candle -ErrorAction SilentlyContinue
$WixSource = "installer.wxs"
if ($WixInstalled -and (Test-Path $WixSource)) {
    Write-Host ""
    Write-Host "WiX toolset detected. To create an MSI:" -ForegroundColor Yellow
    Write-Host "  candle.exe -dPlatform=${Platform} -dVersion=${Version} $WixSource" -ForegroundColor DarkGray
    Write-Host "  light.exe installer.wixobj -o dist\Syxx-${Version}.msi" -ForegroundColor DarkGray
}
