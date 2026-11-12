# Dev-only helpers for local verification of ShabzakAPI (NOT part of any repo).
# - The JWT signing key is a random per-machine dev key stored next to this file.
# - Tokens are short-lived (30 min) and only valid against an API process started with Start-DevApi.

$script:DevDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:KeyFile = Join-Path $script:DevDir 'jwt.key'
if (-not (Test-Path $script:KeyFile)) {
    # Random per-machine dev signing key (gitignored). Only valid for an API started with start-api-localdb.ps1.
    [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) | Set-Content -NoNewline $script:KeyFile
}
$script:ApiProject = 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\ShabzakAPI\04.ShabzakAPI\04.ShabzakAPI.csproj'
$script:BaseUrl = 'https://localhost:7170'

function ConvertTo-B64Url([byte[]]$bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-DevJwt {
    param(
        [Parameter(Mandatory)][int]$UserId,
        [Parameter(Mandatory)][ValidateSet('Regular', 'Admin', 'SuperAdmin')][string]$Role,
        [int]$SoldierId = 0,
        [bool]$Enabled = $true,
        [bool]$Activated = $true,
        [int]$Minutes = 30
    )
    $key = Get-Content -Raw $script:KeyFile
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $payload = [ordered]@{
        sub                                                      = "$UserId"
        jti                                                      = [guid]::NewGuid().ToString()
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name' = "dev-test-user-$UserId"
        'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' = $Role
        enabled                                                  = if ($Enabled) { 'True' } else { 'False' }
        activated                                                = if ($Activated) { 'True' } else { 'False' }
        exp                                                      = $now + 60 * $Minutes
        iss                                                      = 'ShabzakAPI'
        aud                                                      = 'ShabzakApp'
    }
    if ($SoldierId -gt 0) { $payload['soldierId'] = "$SoldierId" }
    $header = '{"alg":"HS256","typ":"JWT"}'
    $enc = [Text.Encoding]::UTF8
    $signingInput = (ConvertTo-B64Url $enc.GetBytes($header)) + '.' + (ConvertTo-B64Url $enc.GetBytes(($payload | ConvertTo-Json -Compress)))
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($enc.GetBytes($key))
    try { $sig = ConvertTo-B64Url $hmac.ComputeHash($enc.GetBytes($signingInput)) } finally { $hmac.Dispose() }
    "$signingInput.$sig"
}

# Calls the API and returns @{ Status; Body (parsed JSON or raw string); Raw }.
function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Method = 'GET',
        [string]$Token,
        $Body
    )
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Uri = "$script:BaseUrl$Path"; Method = $Method; Headers = $headers; SkipHttpErrorCheck = $true; TimeoutSec = 300 }
    if ($null -ne $Body) { $params['Body'] = ($Body | ConvertTo-Json -Depth 10 -Compress); $params['ContentType'] = 'application/json' }
    elseif ($Method -eq 'POST') { $params['Body'] = ''; $params['ContentType'] = 'application/json' }
    $resp = Invoke-WebRequest @params
    $parsed = $null
    try { if ($resp.Content) { $parsed = $resp.Content | ConvertFrom-Json -Depth 50 -DateKind String } } catch { $parsed = $resp.Content }
    [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $parsed; Length = $resp.RawContentLength }
}
