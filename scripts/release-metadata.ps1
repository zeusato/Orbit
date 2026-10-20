param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$RequireNotes
)
$ErrorActionPreference = 'Stop'
$orbitRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$orbitVersions = foreach ($orbitProject in @('src/Orbit.App/Orbit.App.csproj', 'src/Orbit.Updater/Orbit.Updater.csproj')) {
    $orbitXml = [xml](Get-Content -LiteralPath (Join-Path $orbitRoot $orbitProject) -Raw)
    $orbitNodes = $orbitXml.SelectNodes('/Project/PropertyGroup/Version')
    if ($orbitNodes.Count -ne 1) { throw "$orbitProject phải có đúng một Version." }
    $orbitValue = $orbitNodes[0].InnerText
    if ($orbitValue -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
        @($orbitValue.Split('.') | Where-Object { [long]$_ -gt 65534 }).Count -gt 0) {
        throw "$orbitProject có Version không hợp lệ; dùng X.Y.Z, mỗi phần từ 0 đến 65534."
    }
    $orbitValue
}
if ($orbitVersions[0] -ne $orbitVersions[1]) { throw 'Version ứng dụng và updater không khớp.' }
$orbitNotesPath = Join-Path $orbitRoot "docs/RELEASE-$($orbitVersions[0]).md"
if ($RequireNotes -and (!(Test-Path -LiteralPath $orbitNotesPath -PathType Leaf) -or
    [string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($orbitNotesPath)))) {
    throw "Thiếu release notes: $orbitNotesPath. Chạy scripts/prepare-release.ps1 trước khi commit release."
}
[pscustomobject]@{ Version = $orbitVersions[0]; NotesPath = $orbitNotesPath }
