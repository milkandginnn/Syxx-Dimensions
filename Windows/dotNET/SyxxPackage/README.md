# Syxx MSIX Packaging

## Build (requires Windows)

```powershell
# From the Windows/ directory:
dotnet build dotNET/SyxxPackage/SyxxPackage.wapproj -c Release -p:AppxPackageDir=..\dist
```

## Requirements

- .NET 10 SDK
- Windows 10 SDK (10.0.19041.0+)
- Visual Studio 2022+ with Windows Application Packaging workload

## Signing

For Microsoft Store submission, sign with the Store key.
For sideloading, sign with a self-signed certificate:

```powershell
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=MilkandGin" -KeyUsage DigitalSignature -FriendlyName "Syxx" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
Set-AuthenticodeSignature -FilePath dist/Syxx.msix -Certificate $cert
```

## Assets

Placeholder assets are in the `Assets/` directory. Replace with the canonical Syxx brand assets
from `Design/logos/` before shipping.