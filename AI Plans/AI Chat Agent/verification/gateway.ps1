# Phase 13: runs the UI container's real nginx.conf (Shabzak/nginx.conf) on nginx for Windows (get-nginx.ps1) in front of
# the local stack, so the production UI build, /api/ and /agent/ are served from one origin like in docker compose.
# Only the Docker-specific values are replaced, and each must match exactly once (a changed nginx.conf fails loudly):
#   listen 80                      -> listen 127.0.0.1:<Port> (loopback only: no firewall prompt)
#   /etc/nginx/mime.types          -> mime.types of the Windows build
#   root /usr/share/nginx/html     -> a copy of Shabzak/dist/shabzak/browser (the production build)
#   shabzak-api:8080               -> the local API (http launch profile)
#   shabzak-agent:3000             -> the local agent (the chat suites' port 3901 by default)
# The resolver line (Docker's DNS) stays: with IP upstreams nginx never queries it.
$script:UiDir = 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\Shabzak'

function Get-GatewayConfig([string]$Port, [string]$ApiUpstream, [string]$AgentUpstream, [string]$HtmlDir) {
    $text = Get-Content -Raw (Join-Path $script:UiDir 'nginx.conf')
    $replacements = [ordered]@{
        'listen 80;'                            = "listen 127.0.0.1:$Port;"
        'include /etc/nginx/mime.types;'        = 'include mime.types;'
        'root /usr/share/nginx/html;'           = "root $HtmlDir;"
        'http://shabzak-api:8080;'              = "http://$ApiUpstream;"
        'http://shabzak-agent:3000;'            = "http://$AgentUpstream;"
    }
    foreach ($from in $replacements.Keys) {
        $count = ([regex]::Matches($text, [regex]::Escape($from))).Count
        if ($count -ne 1) { throw "nginx.conf: expected '$from' exactly once, found $count" }
        $text = $text.Replace($from, $replacements[$from])
    }
    $text
}

# Starts nginx with the generated config. -SkipCopy reuses the copied build. Returns { Url, Prefix, Exe, Conf, ErrorLog, AccessLog }.
function Start-Gateway {
    param([Parameter(Mandatory)][string]$NginxExe, [int]$Port = 5230, [string]$ApiUpstream = '127.0.0.1:5165', [string]$AgentUpstream = '127.0.0.1:3901', [switch]$SkipCopy)
    $prefix = Split-Path -Parent $NginxExe
    $build = Join-Path $script:UiDir 'dist\shabzak\browser'
    if (-not (Test-Path (Join-Path $build 'index.html'))) { throw "No production build in $build (run npm run build in Shabzak/)" }
    $html = Join-Path $prefix 'html-p13'
    if (-not $SkipCopy -or -not (Test-Path $html)) {
        Remove-Item -Recurse -Force $html -ErrorAction SilentlyContinue
        Copy-Item -Recurse $build $html
    }
    $conf = Join-Path $prefix 'conf\nginx-p13.conf'
    [IO.File]::WriteAllText($conf, (Get-GatewayConfig "$Port" $ApiUpstream $AgentUpstream 'html-p13'), [Text.UTF8Encoding]::new($false))
    $gateway = [pscustomobject]@{
        Url = "http://127.0.0.1:$Port"; Prefix = $prefix; PrefixArg = $prefix.Replace('\', '/') + '/'; Exe = $NginxExe; Conf = 'conf/nginx-p13.conf'
        ErrorLog = Join-Path $prefix 'logs\error.log'; AccessLog = Join-Path $prefix 'logs\access.log'; Test = ''
    }
    if ($gateway.PrefixArg -match '\s') { throw "The nginx folder must not contain spaces: $prefix" }
    Remove-Item $gateway.ErrorLog, $gateway.AccessLog -ErrorAction SilentlyContinue
    Push-Location $prefix
    try {
        $gateway.Test = (& $NginxExe -p $gateway.PrefixArg -c $gateway.Conf -t 2>&1 | ForEach-Object { "$_" }) -join "`n"
        if ($LASTEXITCODE) { throw "nginx -t failed:`n$($gateway.Test)" }
        Start-Process -FilePath $NginxExe -ArgumentList '-p', $gateway.PrefixArg, '-c', $gateway.Conf -WorkingDirectory $prefix -WindowStyle Hidden | Out-Null
    }
    finally { Pop-Location }
    for ($i = 0; $i -lt 40; $i++) {
        try { if ((Invoke-WebRequest "$($gateway.Url)/" -SkipHttpErrorCheck -TimeoutSec 2).StatusCode -eq 200) { return $gateway } } catch { }
        Start-Sleep -Milliseconds 250
    }
    Stop-Gateway $gateway
    throw "nginx did not start (see $($gateway.ErrorLog))"
}

function Stop-Gateway($Gateway) {
    if (-not $Gateway) { return }
    Push-Location $Gateway.Prefix
    try { & $Gateway.Exe -p $Gateway.PrefixArg -c $Gateway.Conf -s stop 2>&1 | Out-Null } finally { Pop-Location }
    Start-Sleep -Milliseconds 500
    Get-Process nginx -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Gateway.Exe } | Stop-Process -Force -ErrorAction SilentlyContinue
}
