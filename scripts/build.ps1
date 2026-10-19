param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$Test, [switch]$OfficeTest, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$orbitRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $orbitRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $orbitRoot '.nuget\packages'
Push-Location $orbitRoot
try {
    if ($ArtifactsPath) {
        $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
        dotnet build Orbit.sln --configuration $Configuration --artifacts-path $ArtifactsPath --nologo
    } else {
        dotnet restore Orbit.sln --configfile (Join-Path $orbitRoot 'NuGet.Config') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Restore thất bại.' }
        dotnet build Orbit.sln --configuration $Configuration --no-restore --nologo
    }
    if ($LASTEXITCODE -ne 0) { throw 'Build thất bại.' }
    if ($Test) {
        $orbitTestArgs = @((Join-Path $orbitRoot 'artifacts\tests'))
        if ($OfficeTest) { $orbitTestArgs += '--office' }
        if ($ArtifactsPath) { & (Join-Path $ArtifactsPath "bin/Orbit.Tests/$($Configuration.ToLowerInvariant())/Orbit.Tests.exe") @orbitTestArgs }
        else { dotnet run --project tests/Orbit.Tests --configuration $Configuration --no-build -- @orbitTestArgs }
        if ($LASTEXITCODE -ne 0) { throw 'Kiểm thử thất bại.' }
    }
} finally { Pop-Location }
