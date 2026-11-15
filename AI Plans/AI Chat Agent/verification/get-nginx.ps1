# Downloads the official nginx for Windows (same version as the UI image, Shabzak/Dockerfile) into a temp folder for the
# Phase 13 gateway checks, verifies its PGP signature against the keys published on nginx.org (Git for Windows' gpg), and
# extracts it. Prints the path of nginx.exe. Nothing is installed; delete the folder to remove it (-Remove does that).
param([string]$Version = '1.30.6', [string]$Root = (Join-Path ([IO.Path]::GetTempPath()) 'shabzak-nginx'), [switch]$Remove)
$ErrorActionPreference = 'Stop'
if ($Remove) { Remove-Item -Recurse -Force $Root -ErrorAction SilentlyContinue; return }
$exe = Join-Path $Root "nginx-$Version\nginx.exe"
if (Test-Path $exe) { return $exe }

$gpg = 'C:\Program Files\Git\usr\bin\gpg.exe'
if (-not (Test-Path $gpg)) { throw 'gpg not found (Git for Windows): cannot verify the download' }
New-Item -ItemType Directory -Force -Path $Root | Out-Null
$zip = Join-Path $Root "nginx-$Version.zip"
foreach ($file in @("nginx-$Version.zip", "nginx-$Version.zip.asc")) {
    & curl.exe -sS --ssl-no-revoke -f -m 120 -o (Join-Path $Root $file) "https://nginx.org/download/$file"
    if ($LASTEXITCODE) { throw "Download of $file failed" }
}
$gnupgHome = Join-Path $Root 'gnupg'
New-Item -ItemType Directory -Force -Path $gnupgHome | Out-Null
foreach ($key in @('nginx_signing', 'arut', 'pluknet', 'sb', 'thresh')) {
    $keyFile = Join-Path $Root "$key.key"
    & curl.exe -sS --ssl-no-revoke -f -m 60 -o $keyFile "https://nginx.org/keys/$key.key"
    if ($LASTEXITCODE) { throw "Download of the key $key failed" }
    & $gpg --homedir $gnupgHome --batch --quiet --import $keyFile 2>$null
}
$verify = & $gpg --homedir $gnupgHome --batch --status-fd 1 --verify "$zip.asc" $zip 2>&1
if (-not ($verify -match '\[GNUPG:\] GOODSIG') -or ($verify -match '\[GNUPG:\] (BADSIG|ERRSIG)')) { throw "The signature of nginx-$Version.zip does not verify:`n$($verify -join "`n")" }
$signer = ($verify | Select-String '\[GNUPG:\] VALIDSIG (\w+)').Matches.Groups[1].Value
Write-Host "nginx-$Version.zip: good signature (key $signer, nginx.org/keys)"
Expand-Archive -Path $zip -DestinationPath $Root -Force
$exe
