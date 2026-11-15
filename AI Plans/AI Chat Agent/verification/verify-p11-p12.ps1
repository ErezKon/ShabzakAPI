# Phases 11-12 acceptance: the chat widget in a real (headless) Chrome against the real agent and the LOCAL synthetic dev
# API, with the scripted LLM stub (fake-openai.mjs). ui-e2e.mjs drives the browser over the DevTools protocol.
# Prerequisites: the API on `start-api-localdb.ps1 -LaunchProfile http`, and the UI dev server (`npx ng serve --port 5555`
# in Shabzak/: its dev environment points at http://localhost:5165/api and the agent at http://localhost:3001/api).
# Ports 3001, 3902 and 9333 must be free. Users: 2 Admin (soldier 2), 3 Regular (soldier 3). Soldier 8 gets test
# vacations, deleted again at the end (sqlcmd).
param([string]$ApiUrl = 'http://localhost:5165/api', [string]$UiUrl = 'http://localhost:5555', [int]$CdpPort = 9333)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'dev.ps1')
. (Join-Path $here 'agent-http.ps1')
$script:BaseUrl = $ApiUrl -replace '/api$', ''
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, $expected, $actual) {
    $ok = "$expected" -eq "$actual"
    $results.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $name; Expected = "$expected"; Actual = "$actual" })
}

$apiHealth = try { (Invoke-WebRequest "$script:BaseUrl/health" -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
if ($apiHealth -ne 200) { throw "ShabzakAPI is not reachable at $script:BaseUrl (start it with start-api-localdb.ps1 -LaunchProfile http)" }
$uiHealth = try { (Invoke-WebRequest "$UiUrl/login" -SkipHttpErrorCheck -TimeoutSec 60).StatusCode } catch { 0 }
if ($uiHealth -ne 200) { throw "The UI dev server is not reachable at $UiUrl (npx ng serve --port 5555 in Shabzak/)" }
$chromeExe = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe", "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chromeExe) { throw 'Chrome is not installed' }

$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2 -Minutes 60
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3 -Minutes 60
$BAD = $AD.Substring(0, $AD.Length - 6) + 'AAAAAA'
$today = (Get-Date).Date
$dates = @{}
foreach ($i in 1..6) { $dates["D$i"] = $today.AddDays(40 + 2 * $i).ToString('yyyy-MM-dd') }
$dates['D2'] = $today.AddDays(43).ToString('yyyy-MM-dd')
function VacationsOf([int]$soldierId) { @((Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $AD -Body @{ soldierId = $soldierId }).Body) }
$before8 = VacationsOf 8
$before3 = VacationsOf 3

$ssr = (Invoke-WebRequest "$UiUrl/login" -TimeoutSec 60).Content
$emptyHosts = @('app-chat-panel', 'app-chat-launcher' | Where-Object { $ssr -match "<$_[^>]*ngskiphydration[^>]*><!--[a-z]*--></$_>" }).Count
Check 'SSR: both chat hosts are rendered empty (ngSkipHydration, no browser APIs on the server)' 2 $emptyHosts
Check 'SSR: no chat panel or launcher markup' 'False/False' "$($ssr -match 'class="chat-panel"')/$($ssr -match 'aria-label="פתח את העוזר')"

$phaseEnv = @{
    CDP_PORT = "$CdpPort"; UI_URL = $UiUrl; AGENT_URL = 'http://localhost:3001/api'
    ADMIN_TOKEN = $AD; REGULAR_TOKEN = $RG; BAD_TOKEN = $BAD
    ADMIN_USER = '{"id":2,"name":"dev-admin","role":1,"enabled":true,"activated":true,"soldierId":2}'
    REGULAR_USER = '{"id":3,"name":"dev-regular","role":0,"enabled":true,"activated":true,"soldierId":3}'
    TOMORROW = $today.AddDays(1).ToString('yyyy-MM-dd')
} + $dates
function Invoke-Phase([string]$Phase) {
    $saved = @{}
    foreach ($k in $phaseEnv.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $phaseEnv[$k]) }
    $errFile = Join-Path $here "ui-e2e-$Phase.err.log"
    try { $lines = & node (Join-Path $here 'ui-e2e.mjs') $Phase 2> $errFile }
    finally { foreach ($k in $phaseEnv.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
    foreach ($line in @($lines)) {
        if (-not "$line".StartsWith('{')) { continue }
        $r = $line | ConvertFrom-Json
        $results.Add([pscustomobject]@{ Result = $r.result; Check = "[$Phase] $($r.check)"; Expected = $r.expected; Actual = $r.actual })
    }
    $diag = Get-Content -Raw $errFile -ErrorAction SilentlyContinue
    if ($diag) { Write-Host "--- $Phase diagnostics ---`n$diag" }
    Remove-Item $errFile -ErrorAction SilentlyContinue
}

$profileDir = Join-Path ([IO.Path]::GetTempPath()) "shabzak-ui-e2e-$PID"
$chrome = $null
$stack = $null
$logs = ''
try {
    $stack = Start-AgentStack -Name 'ui' -AgentPort 3001 -LlmPort 3902 -ApiUrl $ApiUrl -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '120' }
    $chrome = Start-Process -FilePath $chromeExe -ArgumentList '--headless=new', "--remote-debugging-port=$CdpPort", '--remote-allow-origins=*', "--user-data-dir=`"$profileDir`"", '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--lang=he', 'about:blank' -PassThru
    if (-not (Wait-Http "http://127.0.0.1:$CdpPort/json/version")) { throw 'Chrome did not start its DevTools endpoint' }

    Invoke-Phase 'main'
    Invoke-Phase 'regular'
    Invoke-Phase 'unauthorized'
    & taskkill /PID $stack.Llm.Id /T /F *> $null
    Invoke-Phase 'llm-down'
    $logs += Get-AgentLog $stack
    Stop-AgentStack $stack
    Invoke-Phase 'agent-down'
    $stack = Start-AgentStack -Name 'ui' -AgentPort 3001 -LlmPort 3902 -ApiUrl $ApiUrl -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '120' }
    Invoke-Phase 'agent-back'
    $logs += Get-AgentLog $stack
    Stop-AgentStack $stack
    $stack = Start-AgentStack -Name 'ui-rl' -AgentPort 3001 -LlmPort 3902 -ApiUrl $ApiUrl -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '2' }
    Invoke-Phase 'ratelimit'
    $logs += Get-AgentLog $stack
}
finally {
    if ($stack) { Stop-AgentStack $stack; Remove-Item $stack.AgentOut, $stack.AgentErr, $stack.LlmLog, $stack.LlmOut -ErrorAction SilentlyContinue }
    Get-ChildItem $here -Filter 'agent-ui*.log' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
    Get-ChildItem $here -Filter 'llm-ui*.log' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
    if ($chrome) { & taskkill /PID $chrome.Id /T /F *> $null }
    Start-Sleep -Milliseconds 500
    Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue
    $after8 = VacationsOf 8
    $after3 = VacationsOf 3
    $new8 = @($after8 | Where-Object { $_.id -notin @($before8.id) })
    $new3 = @($after3 | Where-Object { $_.id -notin @($before3.id) })
    foreach ($v in @($new8) + @($new3)) { & sqlcmd -S "(localdb)\MSSQLLocalDB" -d ShabzakAgentDev -E -Q "SET NOCOUNT ON; DELETE FROM Vacations WHERE Id = $($v.id)" | Out-Null }
    $null = Invoke-Api -Path '/api/Soldiers/GetSoldiers?reloadCache=true' -Token $AD
}

$starts = @($new8 | ForEach-Object { "$($_.from)".Substring(0, 10) } | Sort-Object)
Check 'API: exactly the 3 approved vacations were created (approve, the approved half of the mixed batch, after a reload)' "$($dates['D1'])|$($dates['D4'])|$($dates['D6'])" ($starts -join '|')
Check 'API: the rejected vacations were never created' 0 @($new8 | Where-Object { "$($_.from)".StartsWith($dates['D3']) -or "$($_.from)".StartsWith($dates['D5']) }).Count
Check 'API: the Regular user could not create a vacation through the chat' 0 $new3.Count
Check 'Cleanup: the vacations of soldiers 8 and 3 are back to the start' "$($before8.Count)/$($before3.Count)" "$((VacationsOf 8).Count)/$((VacationsOf 3).Count)"
Check 'Agent log: 3 audit lines for the 3 approved writes' 3 ([regex]::Matches($logs, '\[audit\]')).Count
Check 'Agent log: no JWT' $false ($logs -match 'eyJ[A-Za-z0-9_-]{10,}\.eyJ')

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host "$($results.Count - $failed)/$($results.Count) PASS"
