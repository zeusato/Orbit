#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'Bump')]
param(
    [Parameter(ParameterSetName = 'Bump')]
    [ValidateSet('patch', 'minor', 'major')][string]$Bump = 'patch',
    [Parameter(Mandatory, ParameterSetName = 'Version')][string]$Version,
    [string[]]$Notes,
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$orbitRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
function Invoke-OrbitGit([string[]]$Arguments) {
    $orbitResult = & git -C $orbitRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git thất bại: $($Arguments[0])." }
    return $orbitResult
}
$orbitGitRoot = Invoke-OrbitGit @('rev-parse', '--show-toplevel')
if (![IO.Path]::GetFullPath($orbitGitRoot).TrimEnd('\', '/').Equals($orbitRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ProjectRoot phải là thư mục gốc repository Orbit.'
}
$orbitMetadata = & (Join-Path $PSScriptRoot 'release-metadata.ps1') -ProjectRoot $orbitRoot
$orbitCurrent = [version]$orbitMetadata.Version
if ($PSCmdlet.ParameterSetName -eq 'Bump') {
    $Version = switch ($Bump) {
        'major' { "$($orbitCurrent.Major + 1).0.0" }
        'minor' { "$($orbitCurrent.Major).$($orbitCurrent.Minor + 1).0" }
        default { "$($orbitCurrent.Major).$($orbitCurrent.Minor).$($orbitCurrent.Build + 1)" }
    }
}
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
    @($Version.Split('.') | Where-Object { [long]$_ -gt 65534 }).Count -gt 0) {
    throw 'Version phải có dạng X.Y.Z, mỗi phần từ 0 đến 65534.'
}
if ([version]$Version -le $orbitCurrent) { throw "Version mới phải lớn hơn $orbitCurrent." }
if (Invoke-OrbitGit @('tag', '--list', "v$Version")) { throw "Tag v$Version đã tồn tại; không phát hành lại cùng version." }
$orbitNotesPath = Join-Path $orbitRoot "docs/RELEASE-$Version.md"
if (Test-Path -LiteralPath $orbitNotesPath) { throw "Release notes đã tồn tại: $orbitNotesPath. Không ghi đè." }
$orbitChanges = @(Invoke-OrbitGit @('status', '--porcelain'))
$orbitNoteLines = @($Notes | Where-Object { ![string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
if ($orbitChanges.Count -gt 0 -and $orbitNoteLines.Count -eq 0) {
    throw 'Có thay đổi chưa commit. Commit code trước để tự tạo notes từ lịch sử, hoặc truyền -Notes "Mô tả thay đổi" khi chuẩn bị cả code và release trong cùng một commit.'
}
if ($orbitNoteLines.Count -eq 0) {
    $orbitBaseTag = "v$orbitCurrent"
    if (!(Invoke-OrbitGit @('tag', '--list', $orbitBaseTag))) {
        throw "Chưa có tag $orbitBaseTag ở máy này. Chạy git fetch --tags, hoặc truyền -Notes."
    }
    & git -C $orbitRoot merge-base --is-ancestor $orbitBaseTag HEAD
    if ($LASTEXITCODE -ne 0) { throw "HEAD không tiếp nối $orbitBaseTag; kiểm tra nhánh trước khi chuẩn bị release." }
    $orbitNoteLines = @(Invoke-OrbitGit @('log', "$orbitBaseTag..HEAD", '--no-merges', '--reverse', '--format=%s'))
    if ($orbitNoteLines.Count -eq 0) { throw "Chưa có commit mới sau $orbitBaseTag để phát hành." }
}
$orbitUtf8 = [Text.UTF8Encoding]::new($false)
$orbitOriginal = @{}
$orbitUpdated = @{}
foreach ($orbitProject in @('src/Orbit.App/Orbit.App.csproj', 'src/Orbit.Updater/Orbit.Updater.csproj')) {
    $orbitPath = Join-Path $orbitRoot $orbitProject
    $orbitOriginal[$orbitPath] = [IO.File]::ReadAllBytes($orbitPath)
    $orbitText = [IO.File]::ReadAllText($orbitPath)
    $orbitPattern = '<Version>' + [regex]::Escape($orbitCurrent.ToString()) + '</Version>'
    if ([regex]::Matches($orbitText, $orbitPattern).Count -ne 1) { throw "Không xác định được Version trong $orbitProject." }
    $orbitUpdated[$orbitPath] = [regex]::Replace($orbitText, $orbitPattern, "<Version>$Version</Version>")
}
$orbitReadmePath = Join-Path $orbitRoot 'README.md'
$orbitOriginal[$orbitReadmePath] = [IO.File]::ReadAllBytes($orbitReadmePath)
$orbitReadme = [IO.File]::ReadAllText($orbitReadmePath)
$orbitTitle = '(?m)^# Orbit ' + [regex]::Escape($orbitCurrent.ToString()) + '(?=\r?$)'
if (![regex]::IsMatch($orbitReadme, $orbitTitle)) { throw 'Version trên tiêu đề README không khớp ứng dụng.' }
$orbitUpdated[$orbitReadmePath] = [regex]::Replace($orbitReadme, $orbitTitle, "# Orbit $Version").Replace(
    "Orbit-$orbitCurrent-win-x64.zip", "Orbit-$Version-win-x64.zip").Replace(
    "[bản $orbitCurrent](docs/RELEASE-$orbitCurrent.md)", "[bản $Version](docs/RELEASE-$Version.md)")
$orbitNotesText = "# Orbit $Version`n`n" + (($orbitNoteLines | ForEach-Object { "- $_" }) -join "`n") + "`n"
Write-Host "Orbit $orbitCurrent -> $Version"
Write-Host "Cập nhật version ứng dụng/updater, README và docs/RELEASE-$Version.md."
if (!$PSCmdlet.ShouldProcess($orbitRoot, "Chuẩn bị Orbit $Version")) { return }
# Read and validate every input before writing; never overwrite existing release notes.
$orbitCreatedNotes = $false
$orbitStaged = @{}
$orbitWritten = [Collections.Generic.List[string]]::new()
try {
    $orbitNotesFile = [IO.File]::Open($orbitNotesPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    $orbitCreatedNotes = $true
    try {
        $orbitBytes = $orbitUtf8.GetBytes($orbitNotesText)
        $orbitNotesFile.Write($orbitBytes, 0, $orbitBytes.Length)
    } finally { $orbitNotesFile.Dispose() }
    foreach ($orbitPath in $orbitUpdated.Keys) {
        $orbitTemporary = $orbitPath + '.release-' + [guid]::NewGuid().ToString('N') + '.tmp'
        $orbitStaged[$orbitPath] = $orbitTemporary
        [IO.File]::WriteAllText($orbitTemporary, $orbitUpdated[$orbitPath], $orbitUtf8)
    }
    foreach ($orbitPath in $orbitStaged.Keys) {
        [IO.File]::Move($orbitStaged[$orbitPath], $orbitPath, $true)
        $orbitWritten.Add($orbitPath)
    }
} catch {
    foreach ($orbitPath in $orbitWritten) { [IO.File]::WriteAllBytes($orbitPath, $orbitOriginal[$orbitPath]) }
    if ($orbitCreatedNotes) { Remove-Item -LiteralPath $orbitNotesPath -Force }
    throw
} finally {
    foreach ($orbitTemporary in $orbitStaged.Values) {
        if (Test-Path -LiteralPath $orbitTemporary) { Remove-Item -LiteralPath $orbitTemporary -Force }
    }
}
Write-Host "Đã chuẩn bị $Version. Xem git diff và release notes, rồi commit và push lên main."
Write-Host 'Lệnh này không commit, push, tạo tag hoặc phát hành.'
