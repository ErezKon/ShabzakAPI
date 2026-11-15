# Phase 13 acceptance without Docker (not installed on this machine): the docker compose topology on localhost.
# One origin, like the UI container: the production UI build behind the UI's real nginx.conf (nginx 1.30.6 for Windows,
# get-nginx.ps1 + gateway.ps1), /api/ -> ShabzakAPI (http profile, the synthetic LocalDB), /agent/ -> ShabzakAgent on
# port 3901 with the scripted LLM stub (fake-openai.mjs: the dev network blocks OpenAI). Steps:
#   1. static checks of the deployment files (compose-check.mjs: compose, .env.example, Dockerfiles, Node engines)
#   2. the production build (npm run build) and its URLs
#   3. HTTP checks through the gateway (p13-http.mjs): routing, SSE without buffering, pings, disconnects, CORS, 502
#   4. the UI in headless Chrome through the gateway (ui-e2e.mjs): the P11-P12 phases, real logins, the agent down
#   5. the chat suites verify-p7 .. verify-p10 with every chat request through the gateway (agent-http.ps1 gateway mode)
# Prerequisites: the API on `start-api-localdb.ps1 -LaunchProfile http`; Chrome; ports 5230, 3901-3904 and 9333 free.
# Data: soldiers 8 and 3 get test vacations (deleted at the end); a SuperAdmin role change of user 3 is rejected; users
# 1-3 get their default password back only if their default login fails. The chat suites clean up after themselves.
param([string]$ApiUrl = 'http://localhost:5165/api', [int]$GatewayPort = 5230, [int]$CdpPort = 9333, [switch]$SkipBuild, [switch]$SkipChatSuites)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'dev.ps1')
. (Join-Path $here 'agent-http.ps1')
. (Join-Path $here 'gateway.ps1')
$script:BaseUrl = $ApiUrl -replace '/api$', ''
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, $expected, $actual) {
    $ok = "$expected" -eq "$actual"
    $results.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $name; Expected = "$expected"; Actual = "$actual" })
}
function Add-JsonLines([string]$Tag, $Lines) {
    foreach ($line in @($Lines)) {
        if (-not "$line".StartsWith('{')) { continue }
        $r = $line | ConvertFrom-Json
        $results.Add([pscustomobject]@{ Result = $r.result; Check = "[$Tag] $($r.check)"; Expected = $r.expected; Actual = $r.actual })
    }
}
# Runs a node script with extra environment variables; its JSON check lines are added under [Tag].
function Invoke-NodeChecks([string]$Script, [string]$Tag, [string[]]$NodeArgs, [hashtable]$Environment) {
    $saved = @{}
    foreach ($k in $Environment.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $Environment[$k]) }
    $errFile = Join-Path $here "p13-$($Tag -replace '[^\w.-]', '-').err.log"
    try { $lines = & node (Join-Path $here $Script) @NodeArgs 2> $errFile }
    finally { foreach ($k in $Environment.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
    Add-JsonLines $Tag $lines
    $diag = Get-Content -Raw $errFile -ErrorAction SilentlyContinue
    if ($diag) { Write-Host "--- $Tag diagnostics ---`n$diag" }
    Remove-Item $errFile -ErrorAction SilentlyContinue
}

# --- Prerequisites ------------------------------------------------------------------------------------------------------
$apiHealth = try { (Invoke-WebRequest "$script:BaseUrl/health" -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
if ($apiHealth -ne 200) { throw "ShabzakAPI is not reachable at $script:BaseUrl (start it with start-api-localdb.ps1 -LaunchProfile http)" }
$chromeExe = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe", "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chromeExe) { throw 'Chrome is not installed' }
$nginx = & (Join-Path $here 'get-nginx.ps1')
$SA = New-DevJwt -UserId 1 -Role SuperAdmin -SoldierId 1 -Minutes 120
$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2 -Minutes 120
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3 -Minutes 120
$BAD = $AD.Substring(0, $AD.Length - 6) + 'AAAAAA'
$today = (Get-Date).Date
$dates = @{}
foreach ($i in 1..6) { $dates["D$i"] = $today.AddDays(40 + 2 * $i).ToString('yyyy-MM-dd') }
$dates['D2'] = $today.AddDays(43).ToString('yyyy-MM-dd')
function VacationsOf([int]$soldierId) { @((Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $AD -Body @{ soldierId = $soldierId }).Body) }
$before8 = VacationsOf 8
$before3 = VacationsOf 3

# --- 1. Static checks of the deployment files -----------------------------------------------------------------------------
Invoke-NodeChecks 'compose-check.mjs' 'static' @() @{}

# --- 2. The production build ---------------------------------------------------------------------------------------------
$build = Join-Path $script:UiDir 'dist\shabzak\browser'
if (-not $SkipBuild) {
    Push-Location $script:UiDir
    try { $null = (& npm run build 2>&1 | Out-String); $buildExit = $LASTEXITCODE } finally { Pop-Location }
    Check 'Build: npm run build (production configuration, SSR prerender) passes' 0 $buildExit
}
$bundles = @(Get-ChildItem $build -Filter *.js | ForEach-Object { Get-Content -Raw $_.FullName })
Check 'Bundle: the production URLs (serverURL /api, agentURL /agent/api)' 1 @($bundles | Where-Object { $_.Contains('serverURL:"/api",agentURL:"/agent/api"') }).Count
Check 'Bundle: no development URL (localhost:3001 / 5165 / 7170)' 0 @($bundles | Where-Object { $_ -match 'localhost:(3001|5165|7170)' }).Count
Check 'Build: the 7 routes are prerendered' 7 @('login', 'personal-page', 'soldiers', 'missions', 'assignments', 'assignments-management', 'justice' | Where-Object { Test-Path (Join-Path $build "$_\index.html") }).Count

# --- The three login accounts (synthetic: personal number + phone of their soldier) ---------------------------------------
$accounts = @(
    [pscustomobject]@{ role = 'Admin'; roleNumber = 1; id = 2; soldierId = 2 },
    [pscustomobject]@{ role = 'SuperAdmin'; roleNumber = 2; id = 1; soldierId = 1 },
    [pscustomobject]@{ role = 'Regular'; roleNumber = 0; id = 3; soldierId = 3 }
)
$users = @((Invoke-Api -Path '/api/User/GetUsers' -Token $SA).Body)
foreach ($a in $accounts) {
    $u = $users | Where-Object id -eq $a.id
    Check "Setup: user $($a.id) is the $($a.role) account of soldier $($a.soldierId), enabled and activated" "$($a.role)/$($a.soldierId)/True/True" "$($u.roleName)/$($u.soldierId)/$($u.enabled)/$($u.activated)"
    $soldier = (Invoke-Api -Path "/api/Soldiers/GetSoldierById?soldierId=$($a.soldierId)" -Token $SA).Body
    $a | Add-Member -NotePropertyName user -NotePropertyValue "$($soldier.personalNumber)"
    $a | Add-Member -NotePropertyName password -NotePropertyValue ("$($soldier.phone)" -replace '\D', '')
    $login = Invoke-Api -Path '/api/User/Login' -Method POST -Body @{ username = (Get-Sha512Hex $a.user); password = (Get-Sha512Hex $a.password) }
    if (-not $login.Body.success) {
        $reset = Invoke-Api -Path "/api/User/ResetPasswordToDefault?userId=$($a.id)" -Method POST -Token $SA
        Write-Host "Setup: user $($a.id) had another password; reset to the default -> $($reset.Status)"
        $login = Invoke-Api -Path '/api/User/Login' -Method POST -Body @{ username = (Get-Sha512Hex $a.user); password = (Get-Sha512Hex $a.password) }
    }
    Check "Setup: user $($a.id) ($($a.role)) logs in with the default credentials" 'True' "$($login.Body.success)"
}
$admin = $accounts[0]

# --- 3-4. The gateway, HTTP checks and the UI -------------------------------------------------------------------------------
# CORS_ORIGINS ' ' = the compose default (no origin). Blank, not empty, so dotenv cannot fill it from ShabzakAgent/.env.
$agentEnv = @{ RATE_LIMIT_PER_MINUTE = '300'; CORS_ORIGINS = ' ' }
$httpEnv = @{ GATEWAY_URL = "http://127.0.0.1:$GatewayPort"; ADMIN_TOKEN = $AD; LOGIN_USER = $admin.user; LOGIN_PASSWORD = $admin.password; LOGIN_ROLE = 'Admin'; LISTED_ORIGIN = 'http://localhost:5555' }
$uiEnv = @{
    CDP_PORT = "$CdpPort"; UI_URL = "http://127.0.0.1:$GatewayPort"; AGENT_URL = "http://127.0.0.1:$GatewayPort/agent/api"
    ADMIN_TOKEN = $AD; REGULAR_TOKEN = $RG; BAD_TOKEN = $BAD
    ADMIN_USER = '{"id":2,"name":"dev-admin","role":1,"enabled":true,"activated":true,"soldierId":2}'
    REGULAR_USER = '{"id":3,"name":"dev-regular","role":0,"enabled":true,"activated":true,"soldierId":3}'
    TOMORROW = $today.AddDays(1).ToString('yyyy-MM-dd'); ROLE_TARGET_USER = '3'
    LOGINS = (@($accounts | ForEach-Object { @{ role = $_.role; roleNumber = $_.roleNumber; id = $_.id; user = $_.user; password = $_.password } }) | ConvertTo-Json -Compress -AsArray)
} + $dates
$profileDir = Join-Path ([IO.Path]::GetTempPath()) "shabzak-p13-e2e-$PID"
$chrome = $null; $stack = $null; $gateway = $null; $logs = ''; $uiDone = $false
try {
    $stack = Start-AgentStack -Name 'p13' -ApiUrl $ApiUrl -ExtraEnv $agentEnv
    $gateway = Start-Gateway -NginxExe $nginx -Port $GatewayPort
    Check "nginx -t: the UI image's nginx.conf (only the Docker values replaced) is valid on nginx 1.30.6" $true ($gateway.Test -match 'test is successful')

    Invoke-NodeChecks 'p13-http.mjs' 'http' @('routes') $httpEnv
    Invoke-NodeChecks 'p13-http.mjs' 'http' @('sse') $httpEnv
    Invoke-NodeChecks 'p13-http.mjs' 'http' @('cors-closed') $httpEnv

    $prerendered = (Invoke-WebRequest "$($gateway.Url)/login" -TimeoutSec 30).Content
    Check 'Prerendered /login: both chat hosts are empty (no browser APIs at build time)' 2 @('app-chat-panel', 'app-chat-launcher' | Where-Object { $prerendered -match "<$_[^>]*ngskiphydration[^>]*><!--[a-z]*--></$_>" }).Count

    $chrome = Start-Process -FilePath $chromeExe -ArgumentList '--headless=new', "--remote-debugging-port=$CdpPort", '--remote-allow-origins=*', "--user-data-dir=`"$profileDir`"", '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--lang=he', 'about:blank' -PassThru
    if (-not (Wait-Http "http://127.0.0.1:$CdpPort/json/version")) { throw 'Chrome did not start its DevTools endpoint' }
    foreach ($phase in 'main', 'regular', 'unauthorized', 'prod-login') { Invoke-NodeChecks 'ui-e2e.mjs' "ui:$phase" @($phase) $uiEnv }

    & taskkill /PID $stack.Llm.Id /T /F *> $null
    Invoke-NodeChecks 'ui-e2e.mjs' 'ui:llm-down' @('llm-down') $uiEnv
    $logs += Get-AgentLog $stack
    Stop-AgentStack $stack
    Invoke-NodeChecks 'p13-http.mjs' 'http' @('agent-down') $httpEnv
    Invoke-NodeChecks 'ui-e2e.mjs' 'ui:gateway-agent-down' @('gateway-agent-down') $uiEnv
    $stack = Start-AgentStack -Name 'p13' -ApiUrl $ApiUrl -ExtraEnv $agentEnv
    Invoke-NodeChecks 'ui-e2e.mjs' 'ui:agent-back' @('agent-back') $uiEnv
    $logs += Get-AgentLog $stack
    Stop-AgentStack $stack

    # AGENT_CORS_ORIGINS set, and a 2-per-minute rate limit (the SuperAdmin's bucket for CORS, the UI's Admin for the 429).
    $stack = Start-AgentStack -Name 'p13-cors' -ApiUrl $ApiUrl -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '2'; CORS_ORIGINS = 'http://localhost:5555' }
    $corsEnv = $httpEnv.Clone(); $corsEnv.ADMIN_TOKEN = $SA
    Invoke-NodeChecks 'p13-http.mjs' 'http' @('cors-listed') $corsEnv
    Invoke-NodeChecks 'ui-e2e.mjs' 'ui:ratelimit' @('ratelimit') $uiEnv
    $logs += Get-AgentLog $stack
    Stop-AgentStack $stack
    $stack = $null
    $uiDone = $true
}
finally {
    if ($stack) { $logs += Get-AgentLog $stack; Stop-AgentStack $stack }
    if (-not $uiDone) { Stop-Gateway $gateway }
    if ($chrome) { & taskkill /PID $chrome.Id /T /F *> $null }
    Get-ChildItem $here -Filter 'agent-p13*.log' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
    Get-ChildItem $here -Filter 'llm-p13*.log' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue
    $after8 = VacationsOf 8
    $after3 = VacationsOf 3
    $new8 = @($after8 | Where-Object { $_.id -notin @($before8.id) })
    $new3 = @($after3 | Where-Object { $_.id -notin @($before3.id) })
    foreach ($v in @($new8) + @($new3)) { & sqlcmd -S "(localdb)\MSSQLLocalDB" -d ShabzakAgentDev -E -Q "SET NOCOUNT ON; DELETE FROM Vacations WHERE Id = $($v.id)" | Out-Null }
    $null = Invoke-Api -Path '/api/Soldiers/GetSoldiers?reloadCache=true' -Token $AD
}

Check 'Every HTTP and UI phase ran to the end (a step that stops the run early fails here)' $true $uiDone
$phasesSeen = @($results | ForEach-Object { if ($_.Check -match '^\[(ui:[\w-]+|http|static)\]') { $Matches[1] } } | Sort-Object -Unique)
Check 'Checks were reported by every UI phase' 'ui:agent-back,ui:gateway-agent-down,ui:llm-down,ui:main,ui:prod-login,ui:ratelimit,ui:regular,ui:unauthorized' (($phasesSeen | Where-Object { $_ -like 'ui:*' }) -join ',')
$starts = @($new8 | ForEach-Object { "$($_.from)".Substring(0, 10) } | Sort-Object)
Check 'API: exactly the 3 approved vacations were created through the gateway' "$($dates['D1'])|$($dates['D4'])|$($dates['D6'])" ($starts -join '|')
Check 'API: the Regular user could not create a vacation through the chat' 0 $new3.Count
Check 'API: the rejected SuperAdmin role change left user 3 Regular' 'Regular' (@((Invoke-Api -Path '/api/User/GetUsers' -Token $SA).Body) | Where-Object id -eq 3).roleName
Check 'Cleanup: the vacations of soldiers 8 and 3 are back to the start' "$($before8.Count)/$($before3.Count)" "$((VacationsOf 8).Count)/$((VacationsOf 3).Count)"
Check 'Agent log: 3 audit lines for the 3 approved writes (none for the rejected role change)' 3 ([regex]::Matches($logs, '\[audit\]')).Count
Check 'Agent log: the client that left through the gateway stopped its run' $true ($logs -match 'Client left thread [0-9a-f-]+; stopping the run')
Check 'Agent log: no JWT, phone number or personal number' 'False/False' "$($logs -match 'eyJ[A-Za-z0-9_-]{10,}\.eyJ')/$(($logs -match '0500000\d\d') -or ($logs -match '90000\d\d'))"

# --- 5. The chat suites through the gateway ---------------------------------------------------------------------------------
try {
    if (-not $SkipChatSuites) {
        $env:AGENT_GATEWAY_URL = "$($gateway.Url)/agent"; $env:AGENT_GATEWAY_PORT = '3901'
        try {
            foreach ($suite in 'verify-p7', 'verify-p8', 'verify-p9', 'verify-p10') {
                $log = Join-Path $here "p13-$suite.log"
                & pwsh -NoProfile -File (Join-Path $here "$suite.ps1") -ApiUrl $ApiUrl *> $log
                $summary = [regex]::Match((Get-Content -Raw $log), 'PASS: (\d+)\s+FAIL: (\d+)')
                $failed = if ($summary.Success) { [int]$summary.Groups[2].Value } else { -1 }
                $results.Add([pscustomobject]@{ Result = $(if ($failed -eq 0) { 'PASS' } else { 'FAIL' }); Check = "Chat suite $suite.ps1, every chat request through the gateway (log: p13-$suite.log)"; Expected = 'FAIL: 0'; Actual = $(if ($summary.Success) { $summary.Value } else { 'no summary' }) })
            }
        }
        finally { Remove-Item Env:AGENT_GATEWAY_URL, Env:AGENT_GATEWAY_PORT -ErrorAction SilentlyContinue }
    }
}
finally { Stop-Gateway $gateway }

# --- nginx logs -------------------------------------------------------------------------------------------------------------
$access = Get-Content -Raw $gateway.AccessLog -ErrorAction SilentlyContinue
$errors = Get-Content -Raw $gateway.ErrorLog -ErrorAction SilentlyContinue
Check 'nginx: no emerg / alert / crit in the error log' 0 ([regex]::Matches("$errors", '\[(emerg|alert|crit)\]')).Count
Check 'nginx: the chat streams went through the gateway (access log)' $true ($access -match '"POST /agent/api/chat/stream HTTP/1\.1" 200')
Check 'nginx: no token in the access log (tokens travel in headers only)' $false ($access -match 'eyJ[A-Za-z0-9_-]{10,}')

$results | Format-Table -AutoSize -Wrap | Out-String -Width 260
$failedCount = @($results | Where-Object Result -eq 'FAIL').Count
"PASS: $($results.Count - $failedCount)  FAIL: $failedCount"
