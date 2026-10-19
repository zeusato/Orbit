param([string]$SigningKey)
$ErrorActionPreference = 'Stop'
$orbitRoot = Split-Path -Parent $PSScriptRoot
if (!$SigningKey) { $SigningKey = Join-Path $orbitRoot '.release/signing-key.private.pem' }
$orbitTestRoot = Join-Path $orbitRoot ('artifacts/updater-tests/' + [guid]::NewGuid().ToString('N'))
$orbitBuild = Join-Path $orbitTestRoot 'build'
$env:DOTNET_CLI_HOME = Join-Path $orbitRoot '.dotnet'
$env:NUGET_PACKAGES = Join-Path $orbitRoot '.nuget/packages'
foreach ($orbitPair in @(@('0.0.1','old'),@('0.0.2','new'))) {
    dotnet publish (Join-Path $orbitRoot 'tests/Orbit.UpdateProbe') -c Release --artifacts-path $orbitBuild "-p:Version=$($orbitPair[0])" -o (Join-Path $orbitTestRoot $orbitPair[1]) --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
}
dotnet publish (Join-Path $orbitRoot 'src/Orbit.Updater') -c Release --artifacts-path $orbitBuild -o (Join-Path $orbitTestRoot 'helper') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Helper build failed' }
dotnet build (Join-Path $orbitRoot 'tools/Orbit.ReleaseTools') -c Release --artifacts-path $orbitBuild --nologo
if ($LASTEXITCODE -ne 0) { throw 'Signing tool build failed' }
foreach ($orbitCase in @('success','rollback')) {
    $orbitCaseRoot = Join-Path $orbitTestRoot $orbitCase
    $orbitInstall = Join-Path $orbitCaseRoot 'install'
    $orbitWork = Join-Path $orbitCaseRoot 'work'
    $orbitRunner = Join-Path $orbitWork 'runner'
    $orbitBundle = Join-Path $orbitCaseRoot 'bundle/Orbit'
    New-Item -ItemType Directory -Path $orbitInstall,$orbitRunner,$orbitBundle -Force | Out-Null
    Copy-Item -Path (Join-Path $orbitTestRoot 'old/*') -Destination $orbitInstall -Recurse
    Copy-Item -Path (Join-Path $orbitTestRoot 'new/*') -Destination $orbitBundle -Recurse
    Copy-Item -Path (Join-Path $orbitTestRoot 'helper/*') -Destination $orbitRunner -Recurse
    Copy-Item -Path (Join-Path $orbitTestRoot 'helper/*') -Destination $orbitBundle -Recurse
    Set-Content -LiteralPath (Join-Path $orbitInstall 'user-note.txt') -Value 'Preserve this user file'
    if ($orbitCase -eq 'rollback') { Set-Content -LiteralPath (Join-Path $orbitBundle 'fail-health.flag') -Value 'test' }
    $orbitZip = Join-Path $orbitCaseRoot 'Orbit-0.0.2-win-x64.zip'
    Compress-Archive -LiteralPath $orbitBundle -DestinationPath $orbitZip
    & (Join-Path $orbitBuild 'bin/Orbit.ReleaseTools/release/Orbit.ReleaseTools.exe') sign $SigningKey $orbitZip '0.0.2' (Join-Path $orbitWork 'manifest.json')
    if ($LASTEXITCODE -ne 0) { throw 'Signing failed' }
    Copy-Item -LiteralPath (Join-Path $orbitWork 'manifest.json.sig') -Destination (Join-Path $orbitWork 'manifest.sig')
    Copy-Item -LiteralPath $orbitZip -Destination (Join-Path $orbitWork 'package.zip')
    $orbitParent = Start-Process -FilePath (Join-Path $orbitInstall 'Orbit.exe') -WindowStyle Hidden -PassThru
    $orbitId = [guid]::NewGuid().ToString('N')
    $orbitReady = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, "Local\Orbit.Update.Ready.$orbitId")
    try {
        @{ Root=$orbitInstall; ParentPid=$orbitParent.Id; ParentStartTicks=$orbitParent.StartTime.ToUniversalTime().Ticks; Id=$orbitId } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $orbitWork 'request.json')
        $orbitHelper = Start-Process -FilePath (Join-Path $orbitRunner 'Orbit.Updater.exe') -ArgumentList '--apply' -WindowStyle Hidden -PassThru
        if (!$orbitReady.WaitOne(15000)) { throw "Helper readiness failed: $orbitCase" }
        Set-Content -LiteralPath (Join-Path $orbitInstall 'probe-exit.txt') -Value 'exit'
        if (!$orbitHelper.WaitForExit(20000)) { throw "Helper timed out: $orbitCase" }
        $orbitVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $orbitInstall 'Orbit.dll')).Version.ToString()
        $orbitExpected = if ($orbitCase -eq 'success') { '0.0.2.0' } else { '0.0.1.0' }
        if ($orbitVersion -ne $orbitExpected) { throw "Version mismatch $orbitCase : $orbitVersion" }
        $orbitResult = Get-Content -LiteralPath (Join-Path $orbitInstall '.orbit-update-result.json') -Raw | ConvertFrom-Json
        if ($orbitResult.success -ne ($orbitCase -eq 'success')) { throw "Unexpected update result $orbitCase" }
        if ((Get-Content -LiteralPath (Join-Path $orbitInstall 'user-note.txt')).Trim() -ne 'Preserve this user file') { throw 'User file changed' }
        if ($orbitCase -eq 'rollback' -and (Test-Path -LiteralPath (Join-Path $orbitInstall 'fail-health.flag'))) { throw 'New files not rolled back' }
        Write-Output "PASS updater process integration: $orbitCase"
    } finally {
        $orbitReady.Dispose()
        if (!$orbitParent.HasExited) { $orbitParent.Kill() }
    }
}
Write-Output "Updater integration artifacts: $orbitTestRoot"
