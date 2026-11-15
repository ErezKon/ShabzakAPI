# Phase 10 acceptance: the schedule tools and workflows, end to end against the LOCAL synthetic dev API.
# CREATE: resolve the pool, check coverage, run auto-assign (approval), inspect and accept the proposal (approval).
# FIX/CHANGE: a rule violation refused before asking; move within a mission and to another mission; replace; swap; unassign.
# SUGGEST: suggest_replacements, cross-checked with ValidateAssignments, then replace_soldier with the first suggestion.
# Reads run through the dev CLI's --tool mode; writes through the chat API with the scripted LLM stub (fake-openai.mjs).
# Also checks the P10 API fixes: accepting a proposal never duplicates existing assignments (legacy rows too), and AutoAssign
# answers 409 while an interactive auto-assign session is open.
# Prerequisite: the API runs with `start-api-localdb.ps1 -LaunchProfile http`. Users: 1 SuperAdmin, 2 Admin, 3 Regular.
# Data: two synthetic missions on days 16-19 from today (no seed data there); both are deleted at the end, with the proposal
# file the run wrote (AssignmentsResults/<id>.json, synthetic names only). It refuses to start while proposals are pending.
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
    Invoke-Chat '/api/chat/resume' @{ threadId = $ThreadId; decisions = @($d) } $Token -MaxSeconds 600
}
function Card($r) { @($r.Approval.actions)[0] }
function Has($list, [string]$text) { @($list) -contains $text }
function ToolResult($r, [string]$Tool) {
    $text = "$($r.Final.message)"
    $i = $text.IndexOf("$Tool=")
    if ($i -lt 0) { return $null }
    try { $text.Substring($i + $Tool.Length + 1).Trim() | ConvertFrom-Json -Depth 30 } catch { $null }
}
function Sql([string]$query) { @(sqlcmd -S '(localdb)\MSSQLLocalDB' -d ShabzakAgentDev -E -l 30 -h -1 -W -Q "SET NOCOUNT ON; $query") | Where-Object { $_.Trim() } }

$apiHealth = try { (Invoke-WebRequest "$script:BaseUrl/health" -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
if ($apiHealth -ne 200) { throw "ShabzakAPI is not reachable at $script:BaseUrl (start it with start-api-localdb.ps1 -LaunchProfile http)" }

$SA = New-DevJwt -UserId 1 -Role SuperAdmin -SoldierId 1 -Minutes 90
$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2 -Minutes 90
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3 -Minutes 90
$NAME = 'סיור בדיקה P10'; $OTHER = 'כוננות בדיקה P10'
$day = { param([int]$n) (Get-Date).Date.AddDays($n).ToString('yyyy-MM-dd') }
$D1 = & $day 16; $D2 = & $day 17; $D3 = & $day 18; $D4 = & $day 19; $D5 = & $day 20
$resultsDir = 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\ShabzakAPI\04.ShabzakAPI\AssignmentsResults'
function Missions { @((Invoke-Api -Path '/api/Mission/GetMissionsSummary' -Token $AD).Body) }
function Instance([int]$Id) { @((Invoke-Api -Path '/api/Schedule/GetInstances' -Method POST -Token $AD -Body @{ instanceIds = @($Id) }).Body)[0] }
function SoldiersIn([int]$Id) { @((Instance $Id).assignments | ForEach-Object soldierId) }
function Pending { @((Invoke-Api -Path '/api/Mission/GetAllCandidates' -Token $AD).Body) }
function Lite { @((Invoke-Api -Path '/api/Soldiers/GetSoldiersLite' -Token $AD).Body) }
$leftover = @(Missions | Where-Object { $_.name -in @($NAME, $OTHER) })
if ($leftover.Count) { throw "A previous run left mission(s) $(@($leftover.id) -join ', '); delete them first (Mission/DeleteMission)." }
if (@(Pending).Count) { throw "There are pending auto-assign proposals; accepting one in this suite would discard them. Accept or clear them first." }
$baseline = @(Missions).Count
$M = $null; $M2 = $null; $createdCandidates = [System.Collections.Generic.List[string]]::new()

# --- Setup through the API: a patrol-like mission with 4 empty shifts on D1-D2; soldier 4 (מ"כ) already in the first one ----
# (PowerShell variables are case-insensitive: $M is the mission ID, so the response gets another name.)
$createdMission = Invoke-Api -Path '/api/Mission/CreateMission' -Method POST -Token $AD -Body @{ name = $NAME; soldiersRequired = 2; commandersRequired = 1; duration = 8; fromTime = '06:00'; toTime = '22:00'; isSpecial = $false; requiredRestAfter = 8; positions = @(@{ position = 13; count = 1 }, @{ position = 0; count = 2 }) }
$M = $createdMission.Body.value.mission.id
$added = @((Invoke-Api -Path '/api/Mission/AddMissionInstances' -Method POST -Token $AD -Body @{ missionId = $M; instances = @(@{ from = "${D1}T06:00:00"; to = "${D1}T14:00:00" }, @{ from = "${D1}T14:00:00"; to = "${D1}T22:00:00" }, @{ from = "${D2}T06:00:00"; to = "${D2}T14:00:00" }, @{ from = "${D2}T14:00:00"; to = "${D2}T22:00:00" }) }).Body.value)
$E1, $E2, $E3, $E4 = @($added | ForEach-Object instanceId)
$pre = Invoke-Api -Path '/api/Schedule/AssignSoldier' -Method POST -Token $AD -Body @{ missionInstanceId = $E1; soldierId = 4 }
Check 'Setup: mission with 4 shifts, soldier 4 in the first' '200/4/200' "$($createdMission.Status)/$(@($added).Count)/$($pre.Status)"

try {
    # --- Read tools (CLI) ---------------------------------------------------------------------------------------------------
    foreach ($tool in 'resolve_soldier_pool', 'get_schedule_coverage', 'run_auto_assign', 'suggest_replacements', 'assign_soldier', 'move_soldier') {
        Check "Regular: $tool is not registered" $true ((Invoke-CliTool $RG $tool '{}').Json.error -match 'not available for role Regular')
    }
    $pool = Invoke-CliTool $AD 'resolve_soldier_pool' '{"platoons":["1","2"],"soldierNames":["יוני","שמעון"]}'
    $expectedPool = @(Lite | Where-Object { $_.platoon -in @('1', '2') } | ForEach-Object id | Sort-Object)
    Check 'resolve_soldier_pool: the active soldiers of platoons 1-2 (= API), "יוני" ambiguous, "שמעון" unknown' "$($expectedPool -join ',')/True/שמעון" "$((@($pool.Json.soldierIds) | Sort-Object) -join ',')/$(@($pool.Json.ambiguous).Count -eq 1)/$(@($pool.Json.unresolved).name -join ',')"
    $poolIds = @($pool.Json.soldierIds)
    $coverage = Invoke-CliTool $AD 'get_schedule_coverage' "{`"from`":`"$D1`",`"to`":`"$D2`",`"missionIds`":[$M]}"
    Check 'get_schedule_coverage: 4 shifts, 3 to schedule, 1 kept (partly staffed)' '4/3/1/1' "$($coverage.Json.totals.shifts)/$($coverage.Json.totals.toSchedule)/$($coverage.Json.totals.keptAsTheyAre)/$($coverage.Json.totals.partlyStaffed)"

    $stack = Start-AgentStack -Name 'p10' -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '300' }

    # --- CREATE: run auto-assign, inspect, accept -------------------------------------------------------------------------------
    $run = @{ from = $D1; to = $D2; missionIds = @($M); soldierIds = $poolIds; maxSchedules = 1 }
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'run_auto_assign' $run) } $AD
    $c = Card $r
    Check 'Auto-assign: card with the shifts to fill, the kept shift and the pool' $true ((Has $c.details "משימות: $NAME") -and (Has $c.details 'משמרות לשיבוץ: 3 (מתוך 4 בתקופה)') -and (Has $c.details 'משמרות שכבר יש בהן חיילים (יישארו כמו שהן): 1') -and (@($c.details | Where-Object { $_ -like "חיילים: $($poolIds.Count) (*" }).Count -eq 1))
    Check 'Auto-assign: the card warns that partly staffed shifts stay short; nothing ran before the approval' 'True/0' "$(@($c.warnings | Where-Object { $_ -like '1 משמרות מאוישות חלקית*' }).Count -eq 1)/$(@(Pending).Count)"
    $r = Decide $r.ThreadId $AD
    $ran = ToolResult $r 'run_auto_assign'
    $CID = $ran.best
    if ($CID) { $createdCandidates.Add($CID) }
    Check 'Auto-assign approved: one proposal (pending), status label of the run, refresh candidates' "1/True/True/candidates" "$(@($ran.proposals).Count)/$(@(Pending) -contains $CID)/$($r.Statuses -contains 'tool_start:run_auto_assign')/$(@($r.Final.refresh) -join ',')"
    $proposal = @($ran.proposals)[0]
    Check 'Proposal summary: 4 shifts, 1 kept, the rest fully staffed or faulty, load per soldier' "4/1/3/True" "$($proposal.shifts.total)/$($proposal.shifts.keptAsTheyWere)/$($proposal.shifts.fullyStaffed + $proposal.shifts.faulty)/$($proposal.load.soldiers -eq $poolIds.Count)"
    Check 'Proposal summary: no contact details' $false ($r.Final.message -match '0500000\d\d|90000\d\d')
    $list = Invoke-CliTool $AD 'list_schedule_candidates' '{}'
    Check 'list_schedule_candidates: the pending proposal, summarized' "1/$CID/4" "$($list.Json.count)/$(@($list.Json.proposals)[0].candidateId)/$(@($list.Json.proposals)[0].shifts.total)"
    $kept = Invoke-CliTool $AD 'get_candidate_details' "{`"candidateId`":`"$CID`",`"status`":`"skipped`"}"
    Check 'get_candidate_details: the kept shift is the one with soldier 4' "1/$E1/True" "$($kept.Json.count)/$(@($kept.Json.shifts)[0].instanceId)/$($kept.Json.pending)"

    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'accept_schedule_candidate' @{ candidateId = $CID }) } $AD
    $c = Card $r
    Check 'Accept: high-risk card with the new assignments' 'high/True' "$($c.risk)/$(Has $c.details "שיבוצים חדשים: $($proposal.newAssignments)")"
    $before = [int](Sql "SELECT COUNT(*) FROM SoldierMission WHERE MissionInstanceId IN ($E1,$E2,$E3,$E4)")
    $r = Decide $r.ThreadId $AD
    $after = [int](Sql "SELECT COUNT(*) FROM SoldierMission WHERE MissionInstanceId IN ($E1,$E2,$E3,$E4)")
    Check 'Accept approved: exactly the proposal''s new assignments, soldier 4 once in the kept shift, no proposal left' "$($proposal.newAssignments)/1/0/candidates,missions,soldiers" "$($after - $before)/$(@(SoldiersIn $E1 | Where-Object { $_ -eq 4 }).Count)/$(@(Pending).Count)/$((@($r.Final.refresh) | Sort-Object) -join ',')"
    $dupes = Sql "SELECT COUNT(*) FROM (SELECT SoldierId, MissionInstanceId FROM SoldierMission WHERE MissionInstanceId IN ($E1,$E2,$E3,$E4) GROUP BY SoldierId, MissionInstanceId HAVING COUNT(*) > 1) d"
    Check 'Accept: no soldier twice in a shift' '0' "$dupes"

    # --- API: legacy proposal rows (duplicate of an existing assignment, deleted shift) are skipped on acceptance --------------------
    $cmdSlot = @(@(Missions | Where-Object id -eq $M)[0].positions | Where-Object position -eq 13)[0].id
    [void](Sql "INSERT INTO SoldierMissionCandidates (SoldierId, MissionInstanceId, MissionPositionId, CandidateId) VALUES (4, $E1, $cmdSlot, 'p10-legacy'), (5, 999999, $cmdSlot, 'p10-legacy')")
    $legacy = Invoke-Api -Path '/api/Mission/AcceptAssignCandidate?candidateId=p10-legacy' -Method POST -Token $AD
    Check 'API accept of legacy rows: 200, soldier 4 still once in the shift, rows removed' "200/1/0" "$($legacy.Status)/$(@(SoldiersIn $E1 | Where-Object { $_ -eq 4 }).Count)/$(Sql "SELECT COUNT(*) FROM SoldierMissionCandidates WHERE CandidateId = 'p10-legacy'")"

    # --- FIX / CHANGE -------------------------------------------------------------------------------------------------------------------
    $P = @((Instance $E2).assignments | Where-Object { $_.position -eq 0 -and $_.soldierId -ne 4 })[0].soldierId
    $Pname = @(Lite | Where-Object id -eq $P)[0].name
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'assign_soldier' @{ missionInstanceId = $E1; soldierId = $P }) } $AD
    Check 'Assign right before their next shift: refused before asking, with the rest rule in Hebrew' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'NOT RUN: assign_soldier .*מנוחה לא מספקת')"
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'assign_soldier' @{ missionInstanceId = $E1; soldierId = $P; allowViolations = $true }) } $AD
    Check 'With allowViolations: a high-risk card' 'high/True' "$((Card $r).risk)/$(Has (Card $r).warnings 'השיבוץ יבוצע למרות ההפרות (לבקשתך).')"
    $null = Decide $r.ThreadId $AD 'reject'
    Check 'Override rejected -> not assigned' $false ((SoldiersIn $E1) -contains $P)

    # Setup: two empty shifts of the mission (D3, D4) and one shift of another mission (D3 14-22).
    $more = @((Invoke-Api -Path '/api/Mission/AddMissionInstances' -Method POST -Token $AD -Body @{ missionId = $M; instances = @(@{ from = "${D3}T06:00:00"; to = "${D3}T14:00:00" }, @{ from = "${D4}T06:00:00"; to = "${D4}T14:00:00" }) }).Body.value)
    $E5, $E6 = @($more | ForEach-Object instanceId)
    $o = Invoke-Api -Path '/api/Mission/CreateMission' -Method POST -Token $AD -Body @{ name = $OTHER; soldiersRequired = 1; commandersRequired = 0; duration = 8; isSpecial = $true; positions = @(@{ position = 0; count = 1 }); specialStart = "${D3}T14:00:00" }
    $M2 = $o.Body.value.mission.id
    $E7 = @((Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $AD -Body @{ from = "${D3}T00:00:00"; to = "${D4}T00:00:00"; missionIds = @($M2) }).Body)[0].instanceId

    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'move_soldier' @{ soldierId = $P; fromMissionInstanceId = $E2; toMissionInstanceId = $E5 }) } $AD
    $c = Card $r
    Check 'Move within the mission: the card keeps the seat and warns that the old shift is left short' $true ((Has $c.details 'תפקיד במשמרת החדשה: חפ"ש (התאמה מלאה)') -and (@($c.warnings | Where-Object { $_ -like '*תחסר חפ"ש*' }).Count -eq 1))
    $r = Decide $r.ThreadId $AD
    Check 'Move approved -> in the new shift, out of the old one; refresh missions + soldiers' "True/False/missions,soldiers" "$((SoldiersIn $E5) -contains $P)/$((SoldiersIn $E2) -contains $P)/$((@($r.Final.refresh) | Sort-Object) -join ',')"

    # --- SUGGEST -------------------------------------------------------------------------------------------------------------------------------
    $s = Invoke-CliTool $AD 'suggest_replacements' "{`"missionInstanceId`":$E5,`"soldierId`":$P}"
    $Q = @($s.Json.suggestions)[0].soldierId
    $simpleSlot = @((Instance $E5).assignments | Where-Object soldierId -eq $P)[0].missionPositionId
    $apiCheck = @((Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $E5; soldierIds = @($s.Json.suggestions.soldierId); ignoreSoldierId = $P; missionPositionId = $simpleSlot }).Body)
    Check 'suggest_replacements: suggestions with reasons, every one valid per ValidateAssignments (same seat, old soldier ignored)' "True/True/True" "$(@($s.Json.suggestions).Count -gt 0)/$(@($apiCheck | Where-Object { -not $_.isValid }).Count -eq 0)/$(@(@($s.Json.suggestions)[0].reasons).Count -ge 2)"
    Check 'suggest_replacements: no contact details' $false ($s.Stdout -match '0500000\d\d|90000\d\d')
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'replace_soldier' @{ missionInstanceId = $E5; oldSoldierId = $P; newSoldierId = $Q }) } $AD
    Check 'Replace with the first suggestion: card' $true ((Card $r).title -like "החלפת חייל: $Pname ← *")
    $null = Decide $r.ThreadId $AD
    Check 'Replace approved -> the new soldier in, the old one out' 'True/False' "$((SoldiersIn $E5) -contains $Q)/$((SoldiersIn $E5) -contains $P)"

    # --- Swap: Q (D3 shift) trades with A (D4 shift) -------------------------------------------------------------------------------------------
    $candidatesForE6 = @((Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $E6; soldierIds = $poolIds }).Body | Where-Object { $_.isValid -and $_.soldierId -notin @($P, $Q) -and $_.positionFit -eq 'exact' -and $_.suggestedPosition -eq 0 })
    $A = $candidatesForE6[0].soldierId
    $null = Invoke-Api -Path '/api/Schedule/AssignSoldier' -Method POST -Token $AD -Body @{ missionInstanceId = $E6; soldierId = $A }
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'replace_soldier' @{ missionInstanceId = $E5; oldSoldierId = $Q; newSoldierId = $A; swap = $true; swapMissionInstanceId = $E6 }) } $AD
    Check 'Swap: card for both shifts (medium risk)' 'True/medium' "$((Card $r).title -like 'החלפת משמרות:*')/$((Card $r).risk)"
    $null = Decide $r.ThreadId $AD
    Check 'Swap approved -> each soldier in the other''s shift' 'True/True/False/False' "$((SoldiersIn $E5) -contains $A)/$((SoldiersIn $E6) -contains $Q)/$((SoldiersIn $E5) -contains $Q)/$((SoldiersIn $E6) -contains $A)"

    # --- Move to another mission ("move X from mission A to mission B"), then unassign ----------------------------------------------------
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'move_soldier' @{ soldierId = $A; fromMissionInstanceId = $E5; toMissionInstanceId = $E7 }) } $AD
    $c = Card $r
    Check 'Move to another mission (right after the shift they leave): the card, no rule broken' $true ((@($c.details | Where-Object { $_ -like "למשמרת: $OTHER*" }).Count -eq 1) -and ($c.risk -ne 'high'))
    $null = Decide $r.ThreadId $AD
    Check 'Cross-mission move approved -> in the other mission''s shift' 'True/False' "$((SoldiersIn $E7) -contains $A)/$((SoldiersIn $E5) -contains $A)"
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'unassign_soldier' @{ missionInstanceId = $E7; soldierId = $A }) } $AD
    Check 'Unassign: the card warns the shift is left short' $true ((@((Card $r).warnings | Where-Object { $_ -like 'המשמרת תחסר*' }).Count -eq 1))
    $null = Decide $r.ThreadId $AD
    Check 'Unassign approved -> the shift is empty' 0 @(SoldiersIn $E7).Count

    # --- Interactive auto-assign session open: AutoAssign -> 409, and the agent says so -----------------------------------------------------
    $session = Invoke-Api -Path '/api/Mission/StartInteractiveAutoAssign' -Method POST -Token $AD -Body @{ startDate = "${D3}T00:00:00"; endDate = "${D5}T00:00:00"; missions = @($M); soldiers = $poolIds; pauseOn = 'EveryInstance'; showAllSoldiersOnPause = $false }
    $sessionId = $session.Body.sessionId
    $direct = Invoke-Api -Path '/api/Mission/AutoAssign' -Method POST -Token $AD -Body @{ startDate = "${D3}T00:00:00"; endDate = "${D5}T00:00:00"; missions = @($M); soldiers = $poolIds; maxSchedules = 1 }
    Check 'API: AutoAssign while an interactive session is open -> 409 with the reason' '200/True/409/True' "$($session.Status)/$([bool]$sessionId)/$($direct.Status)/$($direct.Body.message -match 'interactive sessions are active')"
    $r = Invoke-Chat '/api/chat/stream' @{ message = (Say 'run_auto_assign' @{ from = $D3; to = $D4; missionIds = @($M); soldierIds = $poolIds; maxSchedules = 1 }) } $AD
    $r = Decide $r.ThreadId $AD
    Check 'Agent: the run explains the open interactive session (nothing changed)' $true ($r.Final.message -match 'an interactive auto-assign session is open')
    if ($sessionId) { $null = Invoke-Api -Path "/api/Mission/CancelInteractiveAutoAssign?sessionId=$sessionId" -Method POST -Token $AD }
    Check 'No proposal left behind' 0 @(Pending).Count
}
finally {
    if ($stack) { Stop-AgentStack $stack }
    foreach ($id in @($M, $M2)) { if ($id) { $null = Invoke-Api -Path "/api/Mission/DeleteMission?missionId=$id" -Method POST -Token $AD } }
    foreach ($id in $createdCandidates) { Remove-Item (Join-Path $resultsDir "$id.json") -ErrorAction SilentlyContinue }
}
Check 'Cleanup: missions back to the start, the run''s proposal file removed' "$baseline/False" "$(@(Missions).Count)/$(@($createdCandidates | Where-Object { Test-Path (Join-Path $resultsDir "$_.json") }).Count -gt 0)"

# --- Audit and logs ----------------------------------------------------------------------------------------------------------------------------
$log = Get-AgentLog $stack
$audits = @([regex]::Matches($log, '\[audit\] (\{.*\})') | ForEach-Object { $_.Groups[1].Value | ConvertFrom-Json })
$expected = 'accept_schedule_candidate:ok,move_soldier:ok,move_soldier:ok,replace_soldier:ok,replace_soldier:ok,run_auto_assign:invalid_input,run_auto_assign:ok,unassign_soldier:ok'
Check 'Audit: one line per executed write (none for refused or rejected ones); the blocked run is recorded with its error' $expected ((@($audits | ForEach-Object { "$($_.tool):$($_.result)" }) | Sort-Object) -join ',')
Check 'Log: no JWT, no phone, no personal number' $false (($log -match 'eyJ[A-Za-z0-9_-]{4,}\.') -or ($log -match '0500000\d\d') -or ($log -match '90000\d\d'))
Remove-Item $stack.AgentOut, $stack.AgentErr, $stack.LlmLog, $stack.LlmOut -ErrorAction SilentlyContinue

$results | Format-Table -AutoSize | Out-String -Width 260
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
