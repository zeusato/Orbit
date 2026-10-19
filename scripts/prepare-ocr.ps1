$ErrorActionPreference = 'Stop'
$orbitRoot = Split-Path -Parent $PSScriptRoot
$orbitModelDir = Join-Path $orbitRoot 'assets\tessdata'
New-Item -ItemType Directory -Path $orbitModelDir -Force | Out-Null
$orbitModelCommit = 'e12c65a915945e4c28e237a9b52bc4a8f39a0cec'
$orbitHashes = @{
    eng = '8280AED0782FE27257A68EA10FE7EF324CA0F8D85BD2FD145D1C2B560BCB66BA'
    vie = 'B6B49293D95D0B6DBD8780174627E82C75BE957B6F4ED9862155540D6B00BB45'
}
foreach ($orbitLanguage in $orbitHashes.Keys) {
    $orbitModel = Join-Path $orbitModelDir ($orbitLanguage + '.traineddata')
    if ((Test-Path -LiteralPath $orbitModel) -and (Get-FileHash -LiteralPath $orbitModel).Hash -eq $orbitHashes[$orbitLanguage]) { continue }
    $orbitDownload = $orbitModel + '.download'
    try {
        curl.exe --silent --show-error --fail --location --retry 3 --output $orbitDownload "https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/$orbitModelCommit/$orbitLanguage.traineddata"
        if ($LASTEXITCODE -ne 0) { throw "Không tải được mô hình $orbitLanguage." }
        if ((Get-FileHash -LiteralPath $orbitDownload).Hash -ne $orbitHashes[$orbitLanguage]) { throw "Mô hình $orbitLanguage sai SHA-256." }
        Move-Item -LiteralPath $orbitDownload -Destination $orbitModel -Force
    } finally { if (Test-Path -LiteralPath $orbitDownload) { Remove-Item -LiteralPath $orbitDownload -Force } }
}
Write-Output 'OCR tiếng Việt và tiếng Anh đã sẵn sàng; SHA-256 hợp lệ.'
