param([switch]$SkipTests, [switch]$RequireSignature, [string]$SigningKey)
$ErrorActionPreference = 'Stop'
$orbitRoot = Split-Path -Parent $PSScriptRoot
if (!$SigningKey) { $SigningKey = Join-Path $orbitRoot '.release/signing-key.private.pem' }
if ($RequireSignature -and !(Test-Path -LiteralPath $SigningKey)) { throw 'Thiếu private key ký release. Không tự tạo khóa mới.' }
$orbitBuild = Join-Path $orbitRoot 'artifacts/package-build'
& (Join-Path $PSScriptRoot 'prepare-ocr.ps1')
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -Test:(!$SkipTests) -ArtifactsPath $orbitBuild
if ($LASTEXITCODE -ne 0) { throw 'Build hoặc kiểm thử thất bại.' }
Push-Location $orbitRoot
try {
    dotnet publish src/Orbit.App/Orbit.App.csproj -c Release -r win-x64 --artifacts-path $orbitBuild --self-contained false -p:DebugType=None -p:DebugSymbols=false -o dist/Orbit --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish thất bại.' }
    $orbitHelper = Join-Path $orbitRoot 'artifacts/updater-publish'
    dotnet publish src/Orbit.Updater/Orbit.Updater.csproj -c Release -r win-x64 --artifacts-path $orbitBuild --self-contained false -p:DebugType=None -p:DebugSymbols=false -o $orbitHelper --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish updater thất bại.' }
    foreach ($orbitName in @('Orbit.Updater.exe','Orbit.Updater.dll','Orbit.Updater.deps.json','Orbit.Updater.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $orbitHelper $orbitName) -Destination (Join-Path $orbitRoot 'dist/Orbit') -Force
    }
    # Native NuGet packages can publish their own PDB despite DebugSymbols=false.
    Get-ChildItem -LiteralPath (Join-Path $orbitRoot 'dist\Orbit') -Filter '*.pdb' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    Copy-Item -LiteralPath (Join-Path $orbitRoot 'README.md') -Destination (Join-Path $orbitRoot 'dist\Orbit\README.md') -Force
    Copy-Item -LiteralPath (Join-Path $orbitRoot 'docs') -Destination (Join-Path $orbitRoot 'dist\Orbit') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $orbitRoot 'third-party') -Destination (Join-Path $orbitRoot 'dist\Orbit') -Recurse -Force
    $orbitVersion = ([xml](Get-Content -LiteralPath 'src/Orbit.App/Orbit.App.csproj' -Raw)).Project.PropertyGroup.Version
    Compress-Archive -LiteralPath (Join-Path $orbitRoot 'dist\Orbit') -DestinationPath (Join-Path $orbitRoot "dist\Orbit-$orbitVersion-win-x64.zip") -Force
    $orbitZip = Join-Path $orbitRoot "dist/Orbit-$orbitVersion-win-x64.zip"
    $orbitHash = (Get-FileHash -LiteralPath $orbitZip -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$orbitZip.sha256" -Value "$orbitHash  Orbit-$orbitVersion-win-x64.zip"
    if (Test-Path -LiteralPath $SigningKey) {
        & (Join-Path $orbitBuild 'bin/Orbit.ReleaseTools/release/Orbit.ReleaseTools.exe') sign $SigningKey $orbitZip $orbitVersion (Join-Path $orbitRoot 'dist/orbit-update.json')
        if ($LASTEXITCODE -ne 0) { throw 'Ký manifest thất bại.' }
        & (Join-Path $orbitBuild 'bin/Orbit.ReleaseTools/release/Orbit.ReleaseTools.exe') verify (Join-Path $orbitRoot 'dist/orbit-update.json') (Join-Path $orbitRoot 'dist/orbit-update.json.sig') (Join-Path $orbitRoot 'assets/update-public-key.pem')
        if ($LASTEXITCODE -ne 0) { throw 'Private key không khớp public key trong ứng dụng.' }
    } else { Write-Warning 'ZIP phát triển chưa ký; client sẽ không nhận tự cập nhật.' }
    Get-ChildItem -LiteralPath (Join-Path $orbitRoot 'dist\Orbit') | Select-Object Name,Length
} finally { Pop-Location }
