<#
.SYNOPSIS
    Build the Syxx Windows app. Delegates to the canonical build.ps1 at the repo root.
.DESCRIPTION
    D115: this file was a stale duplicate of build.ps1 with pre-reorganisation paths
    (Windows\SyxxUI\SyxxUI.csproj instead of Windows\dotNET\SyxxUI\SyxxUI.csproj),
    a shadowing $args parameter, and a transposed output path. Every invocation failed
    at the first dotnet build. It is now a one-line delegation to the canonical script
    so running it from scripts\ still works, and the divergence cannot recur.
#>

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
& "$repoRoot\build.ps1" @args
