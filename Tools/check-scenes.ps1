# PowerShell 7. Runs actual scene/controller code against deliberately limited test adapters.
# This is not a Unity build, graphics test or native input test.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$files = @('Tools/exhibits/SceneAdapters.cs', 'Tools/exhibits/SceneChecks.cs',
    'Assets/FogOfWar/Scripts/SimScene.cs', 'Assets/FogOfWar/Scripts/Controls.cs')
$files += @('ThreeBodyModel','DoublePendulumModel','ThreeBodyScene','DoublePendulumScene','LabTrail',
    'LabMath','LabScene','LorenzModel','LorenzScene','ReactionDiffusionModel','ReactionDiffusionScene',
    'WaveTankModel','WaveTankScene','CentralForceModel','GravityAssistScene','LagrangeModel','LagrangeScene',
    'ChargedParticleModel','ChargedParticleScene','RutherfordScene','ResonanceModel','ResonanceScene',
    'FlockingModel','FlockingScene','GalaxyEncounterModel','GalaxyEncounterScene') | ForEach-Object {
    'Assets/FogOfWar/Scripts/Scenes/' + $_ + '.cs'
}
Add-Type -Path ($files | ForEach-Object { Join-Path $repo $_ })
[SceneChecks]::Run()
