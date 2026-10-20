$ErrorActionPreference = 'Stop'
$orbitRoot = Split-Path -Parent $PSScriptRoot
$orbitTestRoot = Join-Path $orbitRoot ('artifacts/release-tests/' + [guid]::NewGuid().ToString('N'))
$orbitPrepare = Join-Path $PSScriptRoot 'prepare-release.ps1'
$orbitMetadata = Join-Path $PSScriptRoot 'release-metadata.ps1'
$orbitPassed = 0
New-Item -ItemType Directory -Path $orbitTestRoot -Force | Out-Null

function Run-Git([string]$Root, [string[]]$Arguments) {
    $value = & git -C $Root -c core.autocrlf=false -c commit.gpgsign=false -c tag.gpgsign=false -c user.name=OrbitTest -c user.email=release-fixture@localhost @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $($Arguments[0])" }
    return $value
}
function New-Fixture([switch]$NoChanges) {
    $root = Join-Path $orbitTestRoot ([guid]::NewGuid().ToString('N') + ' with spaces')
    foreach ($dir in @('src/Orbit.App', 'src/Orbit.Updater', 'docs', 'empty-hooks')) {
        New-Item -ItemType Directory -Path (Join-Path $root $dir) -Force | Out-Null
    }
    foreach ($name in @('Orbit.App', 'Orbit.Updater')) {
        [IO.File]::WriteAllText((Join-Path $root "src/$name/$name.csproj"), '<Project><PropertyGroup><Version>0.5.0</Version></PropertyGroup></Project>')
    }
    [IO.File]::WriteAllText((Join-Path $root 'README.md'), "# Orbit 0.5.0`nZIP: Orbit-0.5.0-win-x64.zip`nFrom 0.5.0: .NET 10`n[bản 0.5.0](docs/RELEASE-0.5.0.md)`n")
    [IO.File]::WriteAllText((Join-Path $root 'docs/RELEASE-0.5.0.md'), '# Orbit 0.5.0')
    $null = Run-Git $root @('init', '--quiet', '--initial-branch=main')
    $null = Run-Git $root @('config', 'core.hooksPath', (Join-Path $root 'empty-hooks'))
    $null = Run-Git $root @('config', 'core.excludesFile', (Join-Path $root '.git/info/exclude'))
    $null = Run-Git $root @('add', '.')
    $null = Run-Git $root @('commit', '--quiet', '-m', 'Initial release')
    $null = Run-Git $root @('tag', 'v0.5.0')
    if (!$NoChanges) {
        [IO.File]::WriteAllText((Join-Path $root 'feature.txt'), 'new feature')
        $null = Run-Git $root @('add', '.')
        $null = Run-Git $root @('commit', '--quiet', '-m', 'feat: thêm QR, giữ $ và ` trong nội dung')
    }
    return $root
}
function Assert([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}
function Snapshot([string]$Root) {
    return ((Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object { !$_.FullName.Contains([IO.Path]::DirectorySeparatorChar + '.git' + [IO.Path]::DirectorySeparatorChar) } |
        Sort-Object FullName | ForEach-Object { $_.FullName + '=' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n")
}
function Expect-Rejected([string]$Root, [hashtable]$Parameters) {
    $before = Snapshot $Root
    $rejected = $false
    try { & $orbitPrepare -ProjectRoot $Root @Parameters 6>$null }
    catch { $rejected = $true }
    Assert $rejected 'Expected preparation to reject the request'
    Assert ((Snapshot $Root) -eq $before) 'Rejected preparation must not leave changed or temporary files'
}
function Check([string]$Name, [scriptblock]$Test) {
    & $Test
    $script:orbitPassed++
    Write-Output "PASS $Name"
}

Check 'Patch preparation updates both projects, current README links and Unicode notes, without changing Git history' {
    $root = New-Fixture
    $head = Run-Git $root @('rev-parse', 'HEAD')
    & $orbitPrepare -ProjectRoot $root 6>$null
    $metadata = & $orbitMetadata -ProjectRoot $root -RequireNotes
    Assert ($metadata.Version -eq '0.5.1') 'Patch version'
    $readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
    Assert ($readme.Contains('# Orbit 0.5.1') -and $readme.Contains('Orbit-0.5.1-win-x64.zip') -and $readme.Contains('RELEASE-0.5.1.md')) 'Current README references'
    Assert ($readme.Contains('From 0.5.0: .NET 10')) 'Historical compatibility text must remain'
    Assert ((Get-Content -LiteralPath $metadata.NotesPath -Raw).Contains('feat: thêm QR, giữ $ và ` trong nội dung')) 'Notes must preserve commit text literally'
    Assert ((Run-Git $root @('rev-parse', 'HEAD')) -eq $head) 'Preparation must not commit'
    Assert (!(Run-Git $root @('tag', '--list', 'v0.5.1'))) 'Preparation must not tag'
}
foreach ($case in @(@('minor', '0.6.0'), @('major', '1.0.0'))) {
    Check "$($case[0]) bump uses the correct version" {
        $root = New-Fixture
        & $orbitPrepare -ProjectRoot $root -Bump $case[0] 6>$null
        Assert ((& $orbitMetadata -ProjectRoot $root -RequireNotes).Version -eq $case[1]) 'Bump version'
    }
}
Check 'Explicit version and WhatIf' {
    $root = New-Fixture
    $before = Snapshot $root
    & $orbitPrepare -ProjectRoot $root -Version '2.3.4' -WhatIf 6>$null
    Assert ((Snapshot $root) -eq $before) 'WhatIf must not write'
    & $orbitPrepare -ProjectRoot $root -Version '2.3.4' 6>$null
    Assert ((& $orbitMetadata -ProjectRoot $root -RequireNotes).Version -eq '2.3.4') 'Explicit version'
}
Check 'Preparation preserves CRLF line endings in existing files' {
    $root = New-Fixture
    $readmePath = Join-Path $root 'README.md'
    $readme = [IO.File]::ReadAllText($readmePath).Replace("`n", "`r`n")
    [IO.File]::WriteAllText($readmePath, $readme)
    & $orbitPrepare -ProjectRoot $root -Notes 'New release' 6>$null
    $expected = $readme.Replace('# Orbit 0.5.0', '# Orbit 0.5.1').Replace('Orbit-0.5.0-win-x64.zip', 'Orbit-0.5.1-win-x64.zip').Replace('[bản 0.5.0](docs/RELEASE-0.5.0.md)', '[bản 0.5.1](docs/RELEASE-0.5.1.md)')
    Assert ([IO.File]::ReadAllText($readmePath) -ceq $expected) 'README line endings must remain unchanged'
}
Check 'Uncommitted changes need explicit notes; provided notes keep other changes intact' {
    $root = New-Fixture
    [IO.File]::WriteAllText((Join-Path $root 'feature.txt'), 'work in progress')
    Expect-Rejected $root @{}
    & $orbitPrepare -ProjectRoot $root -Notes 'Thêm ảnh → PDF', 'Thêm QR' 6>$null
    Assert ((Get-Content -LiteralPath (Join-Path $root 'feature.txt') -Raw) -eq 'work in progress') 'Uncommitted source preserved'
    Assert ((Get-Content -LiteralPath (Join-Path $root 'docs/RELEASE-0.5.1.md') -Raw).Contains('- Thêm QR')) 'Explicit notes'
}
Check 'Invalid versions, downgrade and same version never mutate files' {
    $root = New-Fixture
    foreach ($version in @('0.5.0', '0.4.9', '0.6', 'v0.6.0', '0.06.0', '0.6.0-beta', '65535.0.0', '0.6.0/../bad')) {
        Expect-Rejected $root @{ Version = $version }
    }
}
Check 'Existing target tag or release notes are never overwritten' {
    $root = New-Fixture
    $null = Run-Git $root @('tag', 'v0.5.1')
    Expect-Rejected $root @{}
    $root = New-Fixture
    [IO.File]::WriteAllText((Join-Path $root 'docs/RELEASE-0.5.1.md'), 'existing release notes')
    Expect-Rejected $root @{ Notes = @('new notes') }
}
Check 'Version mismatch and invalid README fail before writes' {
    $root = New-Fixture
    [IO.File]::WriteAllText((Join-Path $root 'src/Orbit.Updater/Orbit.Updater.csproj'), '<Project><PropertyGroup><Version>0.4.0</Version></PropertyGroup></Project>')
    Expect-Rejected $root @{ Notes = @('new notes') }
    $root = New-Fixture
    [IO.File]::WriteAllText((Join-Path $root 'README.md'), '# Unknown version')
    Expect-Rejected $root @{ Notes = @('new notes') }
}
Check 'Missing or unrelated base tag cannot generate misleading commit notes' {
    $root = New-Fixture
    $null = Run-Git $root @('tag', '-d', 'v0.5.0')
    Expect-Rejected $root @{}
    & $orbitPrepare -ProjectRoot $root -Notes 'Explicit release description' 6>$null
    Assert ((& $orbitMetadata -ProjectRoot $root -RequireNotes).Version -eq '0.5.1') 'Explicit notes work without historical tags'
    $root = New-Fixture
    $null = Run-Git $root @('switch', '--quiet', '--orphan', 'unrelated')
    [IO.File]::WriteAllText((Join-Path $root 'unrelated.txt'), 'other history')
    $null = Run-Git $root @('add', '.')
    $null = Run-Git $root @('commit', '--quiet', '-m', 'Unrelated history')
    $null = Run-Git $root @('tag', '-f', 'v0.5.0')
    $null = Run-Git $root @('switch', '--quiet', 'main')
    Expect-Rejected $root @{}
}
Check 'An unchanged release needs new commits or explicit notes' {
    $root = New-Fixture -NoChanges
    Expect-Rejected $root @{}
}
Check 'A locked project rolls back prepared files and removes temporary notes' {
    $root = New-Fixture
    $lock = [IO.File]::Open((Join-Path $root 'src/Orbit.App/Orbit.App.csproj'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Expect-Rejected $root @{} } finally { $lock.Dispose() }
}
Check 'Signed release preflight requires matching versions and nonempty notes' {
    $root = New-Fixture
    $null = & $orbitMetadata -ProjectRoot $root -RequireNotes
    [IO.File]::WriteAllText((Join-Path $root 'docs/RELEASE-0.5.0.md'), '')
    $rejected = $false
    try { $null = & $orbitMetadata -ProjectRoot $root -RequireNotes } catch { $rejected = $true }
    Assert $rejected 'Empty release notes must block signed release'
}
Write-Output "Release preparation checks passed: $orbitPassed"
Write-Output "Fixtures: $orbitTestRoot"
