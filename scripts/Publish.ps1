<#
.SYNOPSIS
    Publishes a self-contained DeskKit build.

.DESCRIPTION
    Produces a single-file, self-contained executable so the result runs on a
    machine with no .NET installed. Trimming is deliberately left off: Avalonia
    relies on reflection in a few places, and the saving is not worth the risk of
    a widget failing to load on someone else's machine.

.EXAMPLE
    pwsh -File scripts/Publish.ps1
    pwsh -File scripts/Publish.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [string] $OutputPath = 'artifacts/publish'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { (Get-Location).Path }
Push-Location $repositoryRoot

try {
    dotnet publish src/DeskKit.App/DeskKit.App.csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        --output $OutputPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    Get-ChildItem $OutputPath | Select-Object Name, Length
}
finally {
    Pop-Location
}
