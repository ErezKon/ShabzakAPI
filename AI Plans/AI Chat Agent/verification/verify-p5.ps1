# Phase 5 acceptance checks: the ShabzakAgent service against the LOCAL synthetic dev API.
# Prerequisite: the API runs with `start-api-localdb.ps1 -LaunchProfile http` (http://localhost:5165, no https redirect).
# The agent is started here with dummy LLM settings (P5 never calls the LLM), checked, and stopped (process tree).
# Users 1..5 of the seed: 1 SuperAdmin (soldier 1), 2 Admin (soldier 2), 3 Regular (soldier 3), 4 and 5 not activated.
param(
    [string]$AgentDir = 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\ShabzakAgent',
    [int]$Port = 3901,
    [string]$ApiUrl = 'http://localhost:5165/api'
)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'dev.ps1')
$script:BaseUrl = $ApiUrl -replace '/api$', ''
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, $expected, $actual) {
    $ok = "$expected" -eq "$actual"
    $results.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $name; Expected = "$expected"; Actual = "$actual" })
}
$agentUrl = "http://localhost:$Port"
function Invoke-Agent([string]$Path, [string]$Token, [string]$Method = 'GET', [hashtable]$Headers = @{}, [string]$RawAuth) {
    $h = @{} + $Headers
    if ($RawAuth) { $h['Authorization'] = $RawAuth } elseif ($Token) { $h['Authorization'] = "Bearer $Token" }
    $resp = Invoke-WebRequest -Uri "$agentUrl$Path" -Method $Method -Headers $h -SkipHttpErrorCheck -TimeoutSec 60
    $parsed = $null
    try { if ($resp.Content) { $parsed = $resp.Content | ConvertFrom-Json -Depth 20 } } catch { $parsed = $resp.Content }
    [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $parsed; Headers = $resp.Headers }
}

# --- Preconditions -------------------------------------------------------------
$apiHealth = try { (Invoke-WebRequest "$script:BaseUrl/health" -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
if ($apiHealth -ne 200) { throw "ShabzakAPI is not reachable at $script:BaseUrl (start it with start-api-localdb.ps1 -LaunchProfile http)" }

# --- Start the agent -------------------------------------------------------------
$logOut = Join-Path $here 'agent-p5.out.log'
$logErr = Join-Path $here 'agent-p5.err.log'
$envVars = @{
    PORT = "$Port"; SHABZAK_API_URL = $ApiUrl; CORS_ORIGINS = 'http://localhost:5555'; LOG_LEVEL = 'debug'
    LLM_BASE_URL = 'http://127.0.0.1:9/v1'; LLM_MODEL = 'unused-in-p5'; LLM_API_KEY = 'unused-in-p5'
}
$saved = @{}
foreach ($k in $envVars.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $envVars[$k]) }
$proc = Start-Process -FilePath 'node' -ArgumentList 'node_modules/tsx/dist/cli.mjs', 'src/server.ts' -WorkingDirectory $AgentDir -PassThru -NoNewWindow -RedirectStandardOutput $logOut -RedirectStandardError $logErr
foreach ($k in $envVars.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) }
try {
    $up = $false
    for ($i = 0; $i -lt 60 -and -not $up; $i++) {
        Start-Sleep -Milliseconds 500
        try { $up = (Invoke-WebRequest "$agentUrl/health" -SkipHttpErrorCheck -TimeoutSec 2).StatusCode -eq 200 } catch { }
    }
    Check 'Agent starts and /health answers' $true $up

    # --- /health ---------------------------------------------------------------------
    $r = Invoke-Agent '/health'
    Check 'GET /health' '200/ok' "$($r.Status)/$($r.Body.status)"

    # --- Auth middleware ---------------------------------------------------------------
    $r = Invoke-Agent '/api/me'
    Check 'No token -> 401 unauthorized' '401/unauthorized' "$($r.Status)/$($r.Body.code)"
    $r = Invoke-Agent '/api/me' -RawAuth 'Basic dXNlcjpwYXNz'
    Check 'Basic auth -> 401' 401 $r.Status
    $r = Invoke-Agent '/api/me' -Token 'not.a.jwt'
    Check 'Garbage token -> 401 (API rejects it)' '401/unauthorized' "$($r.Status)/$($r.Body.code)"
    $r = Invoke-Agent '/api/me' -Token (New-DevJwt -UserId 3 -Role Regular -SoldierId 3 -Minutes -5)
    Check 'Expired token -> 401' 401 $r.Status
    $r = Invoke-Agent '/api/me' -Token (New-DevJwt -UserId 999 -Role Admin)
    Check 'Token of a non-existent user -> 401' 401 $r.Status

    $SA = New-DevJwt -UserId 1 -Role SuperAdmin -SoldierId 1
    $AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2
    $RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3
    $r = Invoke-Agent '/api/me' -Token $SA
    Check 'SuperAdmin JWT gets through' '200/SuperAdmin/1' "$($r.Status)/$($r.Body.role)/$($r.Body.soldierId)"
    Check '/api/me exposes only id, role, soldierId, soldierName' 'id,role,soldierId,soldierName' (($r.Body.PSObject.Properties.Name | Sort-Object) -join ',')
    $r = Invoke-Agent '/api/me' -Token $AD
    Check 'Admin JWT gets through' '200/Admin/2' "$($r.Status)/$($r.Body.role)/$($r.Body.id)"
    $r = Invoke-Agent '/api/me' -Token $RG
    Check 'Regular JWT gets through (with soldier name)' '200/Regular/3/True' "$($r.Status)/$($r.Body.role)/$($r.Body.soldierId)/$([bool]$r.Body.soldierName)"
    $r = Invoke-Agent '/api/me' -Token (New-DevJwt -UserId 3 -Role Admin -SoldierId 3)
    Check 'Forged Admin claim on a Regular account -> role from User/Me' '200/Regular' "$($r.Status)/$($r.Body.role)"
    $r = Invoke-Agent '/api/me' -Token (New-DevJwt -UserId 4 -Role Regular -SoldierId 5)
    Check 'Not-activated account -> 403 forbidden' '403/forbidden' "$($r.Status)/$($r.Body.code)"

    # Disabled account: the API answers 401 for it; a token cached in the last 60 s keeps working until its entry expires.
    $u = Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 3; enabled = $false }
    Check 'Setup: disable user 3' '200/True' "$($u.Status)/$($u.Body.success)"
    $r = Invoke-Agent '/api/me' -Token (New-DevJwt -UserId 3 -Role Regular -SoldierId 3)
    Check 'Disabled account, new token -> 401' 401 $r.Status
    $r = Invoke-Agent '/api/me' -Token $RG
    Check 'Disabled account, token cached < 60 s ago -> still 200 (by design)' 200 $r.Status
    $u = Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 3; enabled = $true }
    Check 'Cleanup: re-enable user 3' '200/True' "$($u.Status)/$($u.Body.success)"
    $r = Invoke-Agent '/api/me' -Token (New-DevJwt -UserId 3 -Role Regular -SoldierId 3)
    Check 'Re-enabled account -> 200' 200 $r.Status

    # --- CORS and errors ---------------------------------------------------------------
    $r = Invoke-Agent '/api/me' -Method OPTIONS -Headers @{ Origin = 'http://localhost:5555'; 'Access-Control-Request-Method' = 'GET'; 'Access-Control-Request-Headers' = 'authorization' }
    Check 'CORS preflight from the UI origin' '204/http://localhost:5555' "$($r.Status)/$($r.Headers['Access-Control-Allow-Origin'])"
    $r = Invoke-Agent '/health' -Headers @{ Origin = 'http://evil.test' }
    Check 'No CORS header for other origins' '' "$($r.Headers['Access-Control-Allow-Origin'])"
    $r = Invoke-Agent '/does-not-exist'
    Check 'Unknown route -> 404 not_found' '404/not_found' "$($r.Status)/$($r.Body.code)"
}
finally {
    & taskkill /PID $proc.Id /T /F *> $null
}

# --- Log hygiene ---------------------------------------------------------------------
Start-Sleep -Milliseconds 300
$logText = (Get-Content -Raw $logOut -ErrorAction SilentlyContinue) + (Get-Content -Raw $logErr -ErrorAction SilentlyContinue)
Check 'Agent log has a User/Me debug line' $true ($logText -match 'GET User/Me -> 200')
Check 'Agent log contains no JWT' $false ($logText -match 'eyJ[A-Za-z0-9_-]{4,}\.')
Remove-Item $logOut, $logErr -ErrorAction SilentlyContinue

$results | Format-Table -AutoSize | Out-String -Width 220
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
