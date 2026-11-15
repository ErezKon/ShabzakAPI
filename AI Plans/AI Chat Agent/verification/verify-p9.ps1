# Phase 9 acceptance: the mission and shift (instance) tools, end to end against the LOCAL synthetic dev API.
# Read tools run through the dev CLI's --tool mode; writes run through the chat API (approval cards, approve / reject) with the
# scripted LLM stub (fake-openai.mjs): the agent, its approvals and every API call are real, only the model's choices are
# scripted (CALL directives). Plan acceptance: creating a mission, extending its shifts to the next days, and deleting a shift
# that has an assignment (showing the impact), all with approvals.
# Prerequisite: the API runs with `start-api-localdb.ps1 -LaunchProfile http`. Users: 1 SuperAdmin, 2 Admin, 3 Regular.
# Data: two synthetic missions on days 12-13 from today (no seed data there); both are deleted at the end.
# Note: PowerShell variables are case-insensitive ($h and $H are one variable), so responses never share a mission ID's name.
param([string]$ApiUrl = 'http://localhost:5165/api')
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
function Say([string]$Tool, $ToolArgs) { "CALL $Tool $($ToolArgs | ConvertTo-Json -Depth 10 -Compress)" }
function Decide([string]$ThreadId, [string]$Token, [string]$Type = 'approve', [string]$Reason) {
    $d = @{ type = $Type }; if ($Reason) { $d.message = $Reason }
    Invoke-Chat '/api/chat/resume' @{ threadId = $ThreadId; decisions = @($d) } $Token
}
function Card($r) { @($r.Approval.actions)[0] }
function Has($list, [string]$text) { @($list) -contains $text }
# The stub answers "done: <tool>=<result>"; the result of a single tool is the JSON after "<tool>=".
function ToolResult($r, [string]$Tool) {
    $text = "$($r.Final.message)"
    $i = $text.IndexOf("$Tool=")
    if ($i -lt 0) { return $null }
    try { $text.Substring($i + $Tool.Length + 1).Trim() | ConvertFrom-Json -Depth 30 } catch { $null }
}

$apiHealth = try { (Invoke-WebRequest "$script:BaseUrl/health" -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
if ($apiHealth -ne 200) { throw "ShabzakAPI is not reachable at $script:BaseUrl (start it with start-api-localdb.ps1 -LaunchProfile http)" }

$SA = New-DevJwt -UserId 1 -Role SuperAdmin -SoldierId 1 -Minutes 60
$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2 -Minutes 60
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3 -Minutes 60
$NAME = 'משימת בדיקה P9'; $RENAMED = 'משימת בדיקה P9 מעודכנת'; $HELPER = 'משימת עזר P9'
$D1 = (Get-Date).Date.AddDays(12).ToString('yyyy-MM-dd'); $D2 = (Get-Date).Date.AddDays(13).ToString('yyyy-MM-dd')
$D3 = (Get-Date).Date.AddDays(14).ToString('yyyy-MM-dd')
function Missions { @((Invoke-Api -Path '/api/Mission/GetMissionsSummary' -Token $AD).Body) }
function Shifts([int]$MissionId) { @((Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $AD -Body @{ from = "${D1}T00:00:00"; to = "${D3}T00:00:00"; missionIds = @($MissionId) }).Body) }
function Instances([int[]]$Ids) { @((Invoke-Api -Path '/api/Schedule/GetInstances' -Method POST -Token $AD -Body @{ instanceIds = $Ids }).Body) }
$leftover = @(Missions | Where-Object { $_.name -in @($NAME, $RENAMED, $HELPER) })
if ($leftover.Count) { throw "A previous run left mission(s) $(@($leftover.id) -join ', '); delete them first (Mission/DeleteMission)." }
$baseline = @(Missions).Count
$M = $null; $H = $null

# --- ShabzakAPI: Schedule/GetInstances (P9 addition) ---------------------------------------------------------------
$r = Invoke-Api -Path '/api/Schedule/GetInstances' -Method POST -Token $RG -Body @{ instanceIds = @(999999, 49, 21, 49) }
Check 'API GetInstances (any user): requested order, repeated and unknown IDs dropped' '200/49,21' "$($r.Status)/$(@($r.Body.instanceId) -join ',')"
Check 'API GetInstances: same row as GetAssignments' $true ((@($r.Body)[0].PSObject.Properties.Name -join ',') -eq 'instanceId,missionId,missionName,from,to,isFilled,requiredCount,assignedCount,assignments,missingPositions')
Check 'API GetInstances: empty -> 400, more than 200 -> 400, no token -> 401' '400/400/401' "$((Invoke-Api -Path '/api/Schedule/GetInstances' -Method POST -Token $AD -Body @{ instanceIds = @() }).Status)/$((Invoke-Api -Path '/api/Schedule/GetInstances' -Method POST -Token $AD -Body @{ instanceIds = @(1..201) }).Status)/$((Invoke-Api -Path '/api/Schedule/GetInstances' -Method POST -Body @{ instanceIds = @(1) }).Status)"

# --- Read tool (CLI --tool mode, no LLM) -------------------------------------------------------------------------------
foreach ($tool in 'get_mission_instances', 'create_mission', 'generate_mission_instances', 'delete_mission_instance') {
    Check "Regular: $tool is not registered" $true ((Invoke-CliTool $RG $tool '{"missionId":2}').Json.error -match 'not available for role Regular')
}
$today = (Get-Date).Date.ToString('yyyy-MM-dd'); $tomorrow = (Get-Date).Date.AddDays(1).ToString('yyyy-MM-dd')
$mi = Invoke-CliTool $AD 'get_mission_instances' "{`"missionId`":2,`"from`":`"$today`",`"to`":`"$tomorrow`"}"
$apiCount = @((Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $AD -Body @{ from = "${today}T00:00:00"; to = "$((Get-Date).Date.AddDays(2).ToString('yyyy-MM-dd'))T00:00:00"; missionIds = @(2) }).Body).Count
Check 'get_mission_instances: the API count, two days per day, rows without the mission name' "$apiCount/2/False" "$($mi.Json.count)/$(@($mi.Json.byDay).Count)/$(@($mi.Json.instances)[0].PSObject.Properties.Name -contains 'mission')"

$stack = Start-AgentStack -Name 'p9' -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '200' }
try {
    # --- create_mission ------------------------------------------------------------------------------------------------
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'create_mission' @{ name = $NAME; shiftHours = 4 }) } $AD
    Check 'Missing positions: refused before asking (no card), the model is told what is missing' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'NOT RUN: create_mission .*positions')"
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'create_mission' @{ name = $NAME; shiftHours = 4; special = $true; positions = @(@{ position = 'מ"מ'; count = 1 }) }) } $AD
    Check 'Special mission without a start: refused before asking' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'A special mission needs "start"')"
    $create = @{ name = $NAME; shiftHours = 4; startTime = '06:00'; endTime = '14:00'; positions = @(@{ position = 'מ"כ'; count = 1 }, @{ position = 'לוחם'; count = 2 }); shiftsFrom = $D1; shiftsTo = $D1; eachDay = $true; restAfterHours = 8 }
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'create_mission' $create) } $AD
    $c = Card $r
    Check 'Create: card with the slots, the derived requirements and the day-by-day shifts' $true (($c.title -eq "יצירת משימה: $NAME") -and (Has $c.details 'תפקידים בכל משמרת: מ"כ×1, חפ"ש×2') -and (Has $c.details 'נדרשים: 2 חיילים ו-1 מפקדים') -and (Has $c.details 'משמרות שייווצרו: 2 (כל יום 06:00–14:00, משמרות בנות 4 שעות)'))
    Check 'Create: nothing written before the approval' $baseline @(Missions).Count
    $r = Decide $r.ThreadId $AD
    $M = @(Missions | Where-Object name -eq $NAME)[0]
    $shifts = Shifts $M.id
    Check 'Create: approved -> the mission (2+1 required) and its 2 shifts 06-10, 10-14; refresh missions' "2/1/06:00:00-10:00:00,10:00:00-14:00:00/missions" "$($M.soldiersRequired)/$($M.commandersRequired)/$((@($shifts | ForEach-Object { "$($_.from.Substring(11))-$($_.to.Substring(11))" }) -join ','))/$(@($r.Final.refresh) -join ',')"

    # --- generate_mission_instances: continuous (rejected) vs day by day ----------------------------------------------------
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'generate_mission_instances' @{ missionId = $M.id; from = $D1; to = $D2 }) } $AD
    $c = Card $r
    Check 'Continuous run: the dry run counts 6 new, 2 existing, and warns about 4 shifts outside 06:00-14:00' $true ((Has $c.details 'משמרות חדשות: 6') -and (Has $c.details 'קיימות כבר (ידלגו עליהן): 2') -and (Has $c.warnings '4 מהמשמרות מחוץ לשעות 06:00–14:00 (למשל בלילה): המשמרות נוצרות ברצף אחד.'))
    $null = Decide $r.ThreadId $AD 'reject' 'רק בשעות היום'
    Check 'Continuous run rejected -> nothing created' 2 @(Shifts $M.id).Count
    $eachDay = @{ missionId = $M.id; from = $D1; to = $D2; eachDay = $true }
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'generate_mission_instances' $eachDay) } $AD
    $c = Card $r
    Check 'Day by day: 2 new (the next day), 2 skipped, no warning' $true ((Has $c.details 'משמרות חדשות: 2') -and (Has $c.details 'קיימות כבר (ידלגו עליהן): 2') -and (Has $c.details 'אופן: כל יום 06:00–14:00, משמרות בנות 4 שעות') -and (@($c.warnings).Count -eq 0))
    $r = Decide $r.ThreadId $AD
    Check 'Day by day approved -> 4 shifts, created 2' '4/2' "$(@(Shifts $M.id).Count)/$((ToolResult $r 'generate_mission_instances').created)"
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'generate_mission_instances' $eachDay) } $AD
    Check 'The same period again: refused before asking (all shifts exist)' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'No new shifts: all 4 shifts of this period already exist')"

    # --- add_mission_instance ----------------------------------------------------------------------------------------------------
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'add_mission_instance' @{ missionId = $M.id; from = "${D1}T16:00" }) } $AD
    $c = Card $r
    Check 'Add: card with the time and the default length' $true ((Has $c.details 'אורך: 4 שעות') -and (@($c.details | Where-Object { $_ -like 'זמן:*16:00–20:00' }).Count -eq 1))
    $null = Decide $r.ThreadId $AD
    Check 'Add approved -> 5 shifts' 5 @(Shifts $M.id).Count
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'add_mission_instance' @{ missionId = $M.id; from = "${D1}T08:00" }) } $AD
    Check 'Add over an existing shift: refused before asking' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'overlaps shift')"

    # --- update_mission_instance: the assigned soldier is checked at the new times ---------------------------------------------------
    # Setup through the API: soldier 8 in the P9 shift D2 06-10 and in a helper mission's shift D2 18-22.
    $X = @(Shifts $M.id | Where-Object { $_.from -eq "${D2}T06:00:00" })[0].instanceId
    $null = Invoke-Api -Path '/api/Schedule/AssignSoldier' -Method POST -Token $AD -Body @{ missionInstanceId = $X; soldierId = 8 }
    # (PowerShell variables are case-insensitive: $H is the helper mission's ID, so the response gets another name.)
    $helperMission = Invoke-Api -Path '/api/Mission/CreateMission' -Method POST -Token $AD -Body @{ name = $HELPER; soldiersRequired = 1; commandersRequired = 0; duration = 4; isSpecial = $true; positions = @(@{ position = 0; count = 1 }); specialStart = "${D2}T18:00:00" }
    $H = $helperMission.Body.value.mission.id
    $HI = @((Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $AD -Body @{ from = "${D2}T00:00:00"; to = "${D3}T00:00:00"; missionIds = @($H) }).Body)[0].instanceId
    $assignH = Invoke-Api -Path '/api/Schedule/AssignSoldier' -Method POST -Token $AD -Body @{ missionInstanceId = $HI; soldierId = 8 }
    Check 'Setup: soldier 8 in the P9 shift and in the helper shift' '200/1' "$($assignH.Status)/$(@(Instances @($X))[0].assignedCount)"
    $move = @{ missionInstanceId = $X; from = "${D2}T18:00"; to = "${D2}T22:00" }
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_mission_instance' $move) } $AD
    $overlapText = 'NOT RUN: update_mission_instance .*נועם פרידמן: חפיפה עם שיבוץ אחר \(' + [regex]::Escape($HELPER)
    Check 'Move onto the soldier''s other shift: refused before asking, with the overlap in Hebrew' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match $overlapText)"
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_mission_instance' ($move + @{ allowViolations = $true })) } $AD
    $c = Card $r
    Check 'With allowViolations: high-risk card that names the overlap' 'high/True' "$($c.risk)/$(Has $c.warnings 'השינוי יבוצע למרות החפיפה: השיבוצים החופפים יישארו כמו שהם.')"
    $null = Decide $r.ThreadId $AD 'reject'
    Check 'Override rejected -> the shift keeps its time' "${D2}T06:00:00" (@(Instances @($X))[0].from)
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_mission_instance' @{ missionInstanceId = $X; from = "${D2}T02:00" }) } $AD
    $c = Card $r
    Check 'A valid move: the card shows old -> new time and the assigned soldier (length kept)' $true ((@($c.details | Where-Object { $_ -like 'זמן:*06:00–10:00 ←*02:00–06:00' }).Count -eq 1) -and (Has $c.details 'משובצים: נועם פרידמן (חפ"ש)'))
    $r = Decide $r.ThreadId $AD
    Check 'Move approved -> new times, the soldier stays; refresh missions + soldiers' "${D2}T02:00:00/${D2}T06:00:00/8/missions,soldiers" "$(@(Instances @($X))[0].from)/$(@(Instances @($X))[0].to)/$(@(Instances @($X))[0].assignments.soldierId)/$((@($r.Final.refresh) | Sort-Object) -join ',')"

    # --- update_mission ----------------------------------------------------------------------------------------------------------------
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_mission' @{ missionId = $M.id; name = $RENAMED; setPositions = @(@{ position = 'חובש'; count = 1 }) }) } $AD
    $c = Card $r
    Check 'Update: diff card (name, slots, requirements that follow the slots)' $true ((Has $c.details "שם: $NAME ← $RENAMED") -and (Has $c.details 'תפקידים בכל משמרת: מ"כ×1, חפ"ש×2 ← מ"כ×1, חפ"ש×2, חובש×1') -and (Has $c.details 'נדרשים: 2 חיילים ו-1 מפקדים ← 3 חיילים ו-1 מפקדים (לפי התפקידים)'))
    $null = Decide $r.ThreadId $AD
    $M2 = @(Missions | Where-Object id -eq $M.id)[0]
    Check 'Update approved -> renamed, 3 slots, 3 soldiers required, shifts kept' "$RENAMED/3/3/5" "$($M2.name)/$(@($M2.positions).Count)/$($M2.soldiersRequired)/$(@(Shifts $M.id).Count)"
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_mission' @{ missionId = $M.id; removePositions = @('חפ"ש') }) } $AD
    Check 'Removing a slot an assignment uses: refused before asking' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'cannot be removed: 1 assignment\(s\) use it')"

    # --- delete_mission_instance: the impact is shown ------------------------------------------------------------------------------------
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'delete_mission_instance' @{ missionInstanceId = $X }) } $AD
    $c = Card $r
    Check 'Delete shift: high-risk card listing the soldier who is removed' 'high/True' "$($c.risk)/$(Has $c.details 'משובצים שיוסרו: נועם פרידמן (חפ"ש)')"
    $r = Decide $r.ThreadId $AD
    Check 'Delete shift approved -> gone with its assignment (1 deleted)' '0/1/4' "$(@(Instances @($X)).Count)/$((ToolResult $r 'delete_mission_instance').deletedAssignments)/$(@(Shifts $M.id).Count)"

    # --- delete_mission ------------------------------------------------------------------------------------------------------------------
    $ids = @(Shifts $M.id | ForEach-Object instanceId)
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'delete_mission' @{ missionId = $M.id }) } $AD
    $c = Card $r
    Check 'Delete mission: high-risk card with its shifts' 'high/True' "$($c.risk)/$(@($c.details | Where-Object { $_ -like 'משמרות: 4 *' }).Count -eq 1)"
    $r = Decide $r.ThreadId $AD
    Check 'Delete mission approved -> the mission and its shifts are gone; refresh missions + soldiers' '0/0/missions,soldiers' "$(@(Missions | Where-Object id -eq $M.id).Count)/$(@(Instances $ids).Count)/$((@($r.Final.refresh) | Sort-Object) -join ',')"
    if (@(Missions | Where-Object id -eq $M.id).Count -eq 0) { $M = $null }
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'delete_mission' @{ missionId = 999999 }) } $AD
    Check 'Delete an unknown mission: refused before asking (no DeleteMission call)' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'Mission 999999 was not found')"

    # --- Regular users ---------------------------------------------------------------------------------------------------------------------
    $null = Invoke-Chat '/api/chat/stream' @{ message = 'שלום' } $RG
    $requests = @(Get-Content $stack.LlmLog -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
    $regularTools = @($requests | Where-Object { $_.lastUser -eq 'שלום' } | Select-Object -Last 1).tools
    Check 'Regular: none of the P9 tools is offered to the model' 0 @($regularTools | Where-Object { $_ -match 'mission_instance|create_mission|update_mission|delete_mission|get_mission_instances' }).Count
}
finally {
    Stop-AgentStack $stack
    if ($M) { $null = Invoke-Api -Path "/api/Mission/DeleteMission?missionId=$($M.id)" -Method POST -Token $AD }
    if ($H) { $null = Invoke-Api -Path "/api/Mission/DeleteMission?missionId=$H" -Method POST -Token $AD }
}
Check 'Cleanup: missions back to the start' $baseline @(Missions).Count

# --- Audit and logs ----------------------------------------------------------------------------------------------------------------------------
$log = Get-AgentLog $stack
$audits = @([regex]::Matches($log, '\[audit\] (\{.*\})') | ForEach-Object { $_.Groups[1].Value | ConvertFrom-Json })
$expected = 'add_mission_instance,create_mission,delete_mission,delete_mission_instance,generate_mission_instances,update_mission,update_mission_instance'
Check 'Audit: one line per executed write (none for the refused or rejected ones), all ok' "$expected/ok" "$((@($audits.tool) | Sort-Object) -join ',')/$((@($audits.result) | Sort-Object -Unique) -join ',')"
Check 'Log: no JWT' $false ($log -match 'eyJ[A-Za-z0-9_-]{4,}\.')
Remove-Item $stack.AgentOut, $stack.AgentErr, $stack.LlmLog, $stack.LlmOut -ErrorAction SilentlyContinue

$results | Format-Table -AutoSize | Out-String -Width 260
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
