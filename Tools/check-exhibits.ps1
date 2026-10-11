# PowerShell 7; compiles the production equations without Unity.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sources = @('LabMath', 'LorenzModel', 'ReactionDiffusionModel', 'WaveTankModel',
    'CentralForceModel', 'LagrangeModel', 'ChargedParticleModel', 'ResonanceModel',
    'FlockingModel', 'GalaxyEncounterModel') | ForEach-Object {
    Join-Path $repo ('Assets/FogOfWar/Scripts/Scenes/' + $_ + '.cs')
}
$sources += Join-Path $PSScriptRoot 'exhibits/ModelChecks.cs'
Add-Type -Path $sources
[Observatory.Checks.ExhibitChecks]::Run()
