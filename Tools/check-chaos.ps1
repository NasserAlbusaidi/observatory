# Run with PowerShell 7: pwsh -File Tools/check-chaos.ps1
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sources = @(
    'Assets/FogOfWar/Scripts/Scenes/ThreeBodyModel.cs',
    'Assets/FogOfWar/Scripts/Scenes/DoublePendulumModel.cs',
    'Tools/chaos/PhysicsChecks.cs'
) | ForEach-Object { Join-Path $repo $_ }
Add-Type -Path $sources
[Observatory.Checks.PhysicsChecks]::Run()
