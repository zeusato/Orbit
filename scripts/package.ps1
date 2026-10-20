param([switch]$SkipTests, [switch]$RequireSignature, [string]$SigningKey)
$ErrorActionPreference = 'Stop'
$orbitRoot = Split-Path -Parent $PSScriptRoot
$orbitMetadata = & (Join-Path $PSScriptRoot 'release-metadata.ps1') -ProjectRoot $orbitRoot -RequireNotes:$RequireSignature
if (!$SigningKey) { $SigningKey = Join-Path $orbitRoot '.release/signing-key.private.pem' }
if ($RequireSignature -and !(Test-Path -LiteralPath $SigningKey)) { throw 'Thiếu private key ký release. Không tự tạo khóa mới.' }
$orbitBuild = Join-Path $orbitRoot 'artifacts/package-build'
& (Join-Path $PSScriptRoot 'prepare-ocr.ps1')
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -Test:(!$SkipTests) -ArtifactsPath $orbitBuild
if ($LASTEXITCODE -ne 0) { throw 'Build hoặc kiểm thử thất bại.' }
Push-Location $orbitRoot
try {
    # Publish into a clean folder so no file from an earlier build enters the release ZIP.
    # Never half-delete a copy someone is running: locked DLLs survive while data files vanish.
    $orbitDist = [IO.Path]::GetFullPath((Join-Path $orbitRoot 'dist/Orbit'))
    $orbitRunning = @(Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($orbitDist + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })
    if ($orbitRunning.Count -gt 0) { throw "Đang chạy $($orbitRunning[0].ProcessName) từ dist/Orbit (PID $($orbitRunning[0].Id)). Thoát Orbit trước khi đóng gói." }
    if (Test-Path -LiteralPath 'dist/Orbit') { Remove-Item -LiteralPath 'dist/Orbit' -Recurse -Force }
    dotnet publish src/Orbit.App/Orbit.App.csproj -c Release -r win-x64 --artifacts-path $orbitBuild --self-contained true -p:SatelliteResourceLanguages=en -p:DebugType=None -p:DebugSymbols=false -o dist/Orbit --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish thất bại.' }
    $orbitHelper = Join-Path $orbitRoot 'artifacts/updater-publish'
    dotnet publish src/Orbit.Updater/Orbit.Updater.csproj -c Release -r win-x64 --artifacts-path $orbitBuild --self-contained true -p:SatelliteResourceLanguages=en -p:DebugType=None -p:DebugSymbols=false -o $orbitHelper --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish updater thất bại.' }
    $orbitHelperOwn = @('Orbit.Updater.exe','Orbit.Updater.dll','Orbit.Updater.deps.json','Orbit.Updater.runtimeconfig.json')
    foreach ($orbitFile in Get-ChildItem -LiteralPath $orbitHelper -File) {
        $orbitTarget = Join-Path $orbitRoot "dist/Orbit/$($orbitFile.Name)"
        if ($orbitHelperOwn -contains $orbitFile.Name) { Copy-Item -LiteralPath $orbitFile.FullName -Destination $orbitTarget -Force; continue }
        # WindowsDesktop replaces these NETCore facades with full assemblies of the same version; the helper never loads them.
        if (@('Microsoft.VisualBasic.dll','System.Drawing.dll','WindowsBase.dll') -contains $orbitFile.Name -and (Test-Path -LiteralPath $orbitTarget) -and (Get-Item -LiteralPath $orbitTarget).VersionInfo.FileVersion -eq $orbitFile.VersionInfo.FileVersion) { continue }
        # The helper shares the app's self-contained runtime; both must come from the same runtime pack.
        if (!(Test-Path -LiteralPath $orbitTarget) -or (Get-FileHash -LiteralPath $orbitTarget).Hash -ne (Get-FileHash -LiteralPath $orbitFile.FullName).Hash) { throw "Runtime của helper khác ứng dụng: $($orbitFile.Name)" }
    }
    # Native NuGet packages can publish their own PDB despite DebugSymbols=false.
    Get-ChildItem -LiteralPath (Join-Path $orbitRoot 'dist\Orbit') -Filter '*.pdb' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    Copy-Item -LiteralPath (Join-Path $orbitRoot 'README.md') -Destination (Join-Path $orbitRoot 'dist\Orbit\README.md') -Force
    Copy-Item -LiteralPath (Join-Path $orbitRoot 'docs') -Destination (Join-Path $orbitRoot 'dist\Orbit') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $orbitRoot 'third-party') -Destination (Join-Path $orbitRoot 'dist\Orbit') -Recurse -Force
    # Exercise the same runner copy the app makes before applying an update; the copied
    # self-contained helper must start without the install folder or a shared .NET.
    $orbitRunner = Join-Path $orbitRoot 'artifacts/runner-smoke'
    if (Test-Path -LiteralPath $orbitRunner) { Remove-Item -LiteralPath $orbitRunner -Recurse -Force }
    & (Join-Path $orbitBuild 'bin/Orbit.ReleaseTools/release/Orbit.ReleaseTools.exe') runner (Join-Path $orbitRoot 'dist/Orbit') $orbitRunner
    if ($LASTEXITCODE -ne 0) { throw 'Không chép được helper cập nhật.' }
    $orbitSavedRoot = $env:DOTNET_ROOT
    try {
        $env:DOTNET_ROOT = 'Z:\no-dotnet'
        $orbitProbe = Start-Process -FilePath (Join-Path $orbitRunner 'Orbit.Updater.exe') -Wait -PassThru -WindowStyle Hidden
    } finally { $env:DOTNET_ROOT = $orbitSavedRoot }
    if ($orbitProbe.ExitCode -ne 2) { throw "Helper chép riêng không khởi động đúng (exit $($orbitProbe.ExitCode))." }
    $orbitVersion = $orbitMetadata.Version
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
