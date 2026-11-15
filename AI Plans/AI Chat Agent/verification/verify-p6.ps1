# Phase 6 acceptance checks of the agent's read tools against the LOCAL synthetic dev API (no LLM needed).
# Prerequisite: the API runs with `start-api-localdb.ps1 -LaunchProfile http`. Each tool runs through the dev CLI's --tool mode
# with a dev JWT, and results are cross-checked against the API endpoints. The seed day must be today (re-seed otherwise).
# Users: 1 SuperAdmin (soldier 1), 2 Admin (soldier 2), 3 Regular (soldier 3), 4 not activated.
param(
    [string]$AgentDir = 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\ShabzakAgent',
    [string]$ApiUrl = 'http://localhost:5165/api'
)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'dev.ps1')
$script:BaseUrl = $ApiUrl -replace '/api$', ''
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, $expected, $actual) {
    $ok = "$expected" -eq "$actual"
    $results.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $name; Expected = "$expected"; Actual = "$actual" })
}
$script:outputs = [System.Collections.Generic.List[string]]::new()
# The JSON is on stdout; logs and audit lines are on stderr (kept for the PII scan, whatever LOG_LEVEL the .env sets).
function Invoke-Tool([string]$Token, [string]$Name, [string]$ToolArgs = '{}') {
    $env:SHABZAK_TOKEN = $Token
    $env:SHABZAK_API_URL = $ApiUrl
    $errFile = [IO.Path]::GetTempFileName()
    Push-Location $AgentDir
    try { $raw = (& node node_modules/tsx/dist/cli.mjs src/scripts/chat-cli.ts --tool $Name --args $ToolArgs 2> $errFile) -join "`n" } finally { Pop-Location }
    $script:outputs.Add($raw + "`n" + (Get-Content -Raw $errFile -ErrorAction SilentlyContinue))
    Remove-Item $errFile -ErrorAction SilentlyContinue
    try { $raw | ConvertFrom-Json -Depth 30 } catch { [pscustomobject]@{ error = "unparsable output: $raw" } }
}

$apiHealth = try { (Invoke-WebRequest "$script:BaseUrl/health" -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
if ($apiHealth -ne 200) { throw "ShabzakAPI is not reachable at $script:BaseUrl (start it with start-api-localdb.ps1 -LaunchProfile http)" }

$SA = New-DevJwt -UserId 1 -Role SuperAdmin -SoldierId 1
$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3
$today = (Get-Date).Date
$tomorrow = $today.AddDays(1).ToString('yyyy-MM-dd')
$monthStart = (Get-Date -Day 1).Date.ToString('yyyy-MM-dd')
$monthEnd = (Get-Date -Day 1).Date.AddMonths(1).AddDays(-1).ToString('yyyy-MM-dd')

# --- Role filtering -------------------------------------------------------------------
foreach ($adminTool in 'get_available_soldiers', 'get_fairness_stats') {
    $r = Invoke-Tool $RG $adminTool '{"missionInstanceId":1,"to":"2026-11-30"}'
    Check "Regular: $adminTool is not registered" $true ($r.error -match 'not available for role Regular')
}
$env:SHABZAK_TOKEN = New-DevJwt -UserId 4 -Role Regular -SoldierId 5
Push-Location $AgentDir; $na = (& node node_modules/tsx/dist/cli.mjs src/scripts/chat-cli.ts --tool get_my_profile 2>&1) -join "`n"; $naExit = $LASTEXITCODE; Pop-Location
Check 'Not-activated account is refused by the CLI' '1/True' "$naExit/$($na -match 'not activated')"

# --- Regular user -----------------------------------------------------------------------
$r = Invoke-Tool $RG 'get_my_profile'
Check 'Regular get_my_profile: own soldier and role' '3/Regular/all-time (not date filtered)' "$($r.profile.soldierId)/$($r.profile.role)/$($r.summary.scope)"
$r = Invoke-Tool $RG 'search_soldiers' '{"query":"משה","platoon":"2"}'
Check 'Regular search_soldiers: only id, name, match' 'id,match,name' (($r.soldiers[0].PSObject.Properties.Name | Sort-Object) -join ',')
Check 'Regular search_soldiers finds the soldier' 2 $r.soldiers[0].id
$r = Invoke-Tool $RG 'get_schedule' "{`"from`":`"$tomorrow`"}"
$api = Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $RG -Body @{ from = "$($tomorrow)T00:00:00"; to = $today.AddDays(2).ToString('yyyy-MM-ddT00:00:00') }
Check 'Regular get_schedule tomorrow: same instance count as the API' @($api.Body).Count $r.count
Check 'get_schedule row shape' 'filled,from,instanceId,missing,mission,soldiers,to,weekday' (($r.instances[0].PSObject.Properties.Name | Sort-Object) -join ',')
Check 'get_schedule filled format' $true ($r.instances[0].filled -match '^\d+/\d+$')
$patrol = $r.instances | Where-Object { $_.mission -eq 'סיור' -and $_.from -like '*06:00' } | Select-Object -First 1
Check 'Tomorrow-morning patrol found in the schedule' $true ($null -ne $patrol)
$apiPatrol = @($api.Body) | Where-Object { $_.instanceId -eq $patrol.instanceId }
Check 'Patrol row: filled and missing match the API' "$($apiPatrol.assignedCount)/$($apiPatrol.requiredCount)" $patrol.filled
$r = Invoke-Tool $RG 'get_soldier_summary' '{"soldierId":5}'
Check 'Regular get_soldier_summary ignores another soldierId' 3 $r.soldier.id
$r = Invoke-Tool $RG 'get_vacations'
Check 'Regular get_vacations: own vacations only' '3' ((@($r.vacations.soldierId) | Sort-Object -Unique) -join ',')
$r = Invoke-Tool $RG 'list_missions'
Check 'Regular list_missions: no staffing fields' $false ($null -ne $r.missions[0].positions)
$r = Invoke-Tool $RG 'get_schedule' '{"from":"2026-11-01","to":"2026-11-30"}'
Check 'get_schedule > 14 days -> invalid_input' 'invalid_input' $r.code

# --- Admin ------------------------------------------------------------------------------
$r = Invoke-Tool $AD 'search_soldiers' '{"positions":["חובש"]}'
Check 'Admin search_soldiers by position (medics)' '5,11' ((@($r.soldiers.id) | Sort-Object) -join ',')
$r = Invoke-Tool $AD 'get_available_soldiers' "{`"missionInstanceId`":$($patrol.instanceId),`"limit`":30}"
$active = @((Invoke-Api -Path '/api/Soldiers/GetSoldiersLite' -Token $AD).Body)
Check 'get_available_soldiers considers every active soldier' $active.Count $r.considered
Check 'available + excluded = considered' $r.considered ($r.available + $r.excluded.total)
$validation = @((Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $patrol.instanceId; soldierIds = @($active.id) }).Body)
Check 'Ranked soldiers = the API''s valid soldiers' ((@($validation | Where-Object isValid | ForEach-Object soldierId) | Sort-Object) -join ',') ((@($r.ranked.soldierId) | Sort-Object) -join ',')
Check 'Ranked: exact fits before similar' $true ((@($r.ranked.fit) -join ',') -notmatch 'similar,exact')
$r = Invoke-Tool $AD 'get_available_soldiers' '{"missionInstanceId":999999}'
Check 'get_available_soldiers unknown instance -> not_found' 'not_found' $r.code
$r = Invoke-Tool $AD 'get_fairness_stats' "{`"from`":`"$monthStart`",`"to`":`"$monthEnd`",`"soldierIds`":[5]}"
$hours = @((Invoke-Api -Path '/api/Metadata/GetHoursPerSoldiers' -Method POST -Token $AD -Body @{ from = "$($monthStart)T00:00:00"; to = "$($monthEnd)T00:00:00"; soldierType = 0 }).Body)
Check 'get_fairness_stats hours of soldier 5 = Metadata/GetHoursPerSoldiers' ([math]::Round(($hours | Where-Object { $_.soldier.id -eq 5 }).totalHours, 2)) $r.soldiers[0].hours
Check 'get_fairness_stats: soldiers with assignments' $hours.Count $r.soldiersWithAssignments
$weekStart = $today.AddDays(-[int]$today.DayOfWeek).ToString('yyyy-MM-dd'); $weekEnd = $today.AddDays(6 - [int]$today.DayOfWeek).ToString('yyyy-MM-dd')
$r = Invoke-Tool $AD 'get_soldier_schedule' "{`"soldierId`":5,`"from`":`"$weekStart`",`"to`":`"$weekEnd`"}"
$apiWeek = @((Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $AD -Body @{ from = "$($weekStart)T00:00:00"; to = $today.AddDays(7 - [int]$today.DayOfWeek).ToString('yyyy-MM-ddT00:00:00'); soldierIds = @(5) }).Body)
Check 'get_soldier_schedule count = API (this week, soldier 5)' $apiWeek.Count $r.count
$r = Invoke-Tool $AD 'get_vacations' '{"status":"pending"}'
$v1 = $r.vacations | Where-Object { $_.soldierId -eq 12 } | Select-Object -First 1
Check 'Admin get_vacations attributes vacations to soldiers' 'תומר אזולאי/pending' "$($v1.soldier)/$($v1.statusKey)"
$r = Invoke-Tool $AD 'get_schedule' "{`"from`":`"$($today.AddDays(-2).ToString('yyyy-MM-dd'))`",`"to`":`"$($today.AddDays(4).ToString('yyyy-MM-dd'))`"}"
Check 'Large schedule result is capped with a hint' "True/60/True" "$($r.truncated)/$(@($r.instances).Count)/$([bool]$r.hint)"

# --- SuperAdmin --------------------------------------------------------------------------
$r = Invoke-Tool $SA 'get_available_soldiers' "{`"missionInstanceId`":$($patrol.instanceId)}"
Check 'SuperAdmin can use admin tools' $true ($r.considered -gt 0)

# --- PII hygiene across every tool output --------------------------------------------------
$all = $script:outputs -join "`n"
Check 'No phone number in any tool output' $false ($all -match '0500000\d\d')
Check 'No personal number in any tool output' $false ($all -match '90000\d\d')
Check 'No JWT in any tool output' $false ($all -match 'eyJ[A-Za-z0-9_-]{4,}\.')

$results | Format-Table -AutoSize | Out-String -Width 220
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
