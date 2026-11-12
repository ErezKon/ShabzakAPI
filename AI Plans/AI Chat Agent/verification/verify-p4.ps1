# Phase 4 checks (mission / instance writes, SetSoldierActive) against the LOCAL synthetic dev DB (after seed-localdb.ps1 and verify-p1.ps1).
# The script creates its own missions and deletes them at the end (legacy DeleteMission), so the seed is left as it was.
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'dev.ps1')
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, $expected, $actual) {
    $ok = "$expected" -eq "$actual"
    $results.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $name; Expected = "$expected"; Actual = "$actual" })
}
$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3
$today = (Get-Date).Date
function Iso([datetime]$d) { $d.ToString('yyyy-MM-ddTHH:mm:ss') }
function At([int]$days, [double]$hours = 0) { Iso $today.AddDays($days).AddHours($hours) }
function Day([int]$days) { $today.AddDays($days).ToString('yyyy-MM-dd') }
function Mission([string]$path, $body, $token = $AD) { Invoke-Api -Path "/api/Mission/$path" -Method POST -Token $token -Body $body }
function Summaries { @((Invoke-Api -Path '/api/Mission/GetMissionsSummary' -Token $AD).Body) }
function Summary([int]$id) { Summaries | Where-Object { $_.id -eq $id } }
function Instances([int]$missionId) { @((Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $AD -Body @{ from = (At -2); to = (At 40); missionIds = @($missionId) }).Body) }
function Instance([int]$missionId, [int]$instanceId) { Instances $missionId | Where-Object { $_.instanceId -eq $instanceId } }
function InstanceIds([int]$missionId) { (@(Instances $missionId | ForEach-Object { $_.instanceId }) | Sort-Object) -join ',' }
function StoredInstance([int]$id) { (Invoke-Api -Path '/api/Mission/GetMissions' -Token $AD).Body | ForEach-Object { $_.missionInstances } | Where-Object { $_.id -eq $id } }
function Validate([int]$instanceId, $soldierIds, $slot = $null) {
    $body = @{ missionInstanceId = $instanceId; soldierIds = @($soldierIds) }
    if ($slot) { $body.missionPositionId = $slot }
    @((Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body $body).Body)
}
function Sql([string]$query) { @(sqlcmd -S '(localdb)\MSSQLLocalDB' -d ShabzakAgentDev -E -l 30 -h -1 -W -Q "SET NOCOUNT ON; $query") | Where-Object { $_.Trim() } }
function Contiguous($rows) { for ($i = 1; $i -lt @($rows).Count; $i++) { if ($rows[$i].from -ne $rows[$i - 1].to) { return $false } }; $true }
function With([hashtable]$base, [hashtable]$changes) { $h = $base.Clone(); foreach ($k in $changes.Keys) { $h[$k] = $changes[$k] }; $h }
$baseline = (Summaries).Count

# --- Authorization --------------------------------------------------------------------------------------------------
foreach ($p in 'Mission/CreateMission', 'Mission/UpdateMissionDetails', 'Mission/GenerateMissionInstances', 'Mission/AddMissionInstances', 'Mission/UpdateMissionInstance', 'Mission/DeleteMissionInstance?missionInstanceId=1', 'Soldiers/SetSoldierActive') {
    Check "Regular $p -> 403" 403 (Invoke-Api -Path "/api/$p" -Method POST -Token $RG -Body @{}).Status
}

# --- CreateMission ----------------------------------------------------------------------------------------------------
$create = @{ name = 'בדיקת P4 מתגלגלת'; description = 'משימה סינתטית לבדיקה'; soldiersRequired = 2; commandersRequired = 1; duration = 6; fromTime = '06:00'; toTime = '18:00'
    isSpecial = $false; actualHours = 5; requiredRestAfter = 10; positions = @(@{ position = 13; count = 1 }, @{ position = 0; count = 2 }); generate = @{ from = (Day 10); to = (Day 11) } }
$r = Mission 'CreateMission' $create
Check 'CreateMission (recurring, generate over two dates) -> 200, 6 instances' '200/True/6' "$($r.Status)/$($r.Body.success)/$($r.Body.value.generatedInstances)"
$M = $r.Body.value.mission
Check 'Summary round-trips name/description/daily times/rest/hours/flags' 'בדיקת P4 מתגלגלת/משימה סינתטית לבדיקה/06:00/18:00/10/5/False/True' "$($M.name)/$($M.description)/$($M.fromTime)/$($M.toTime)/$($M.requiredRestAfter)/$($M.actualHours)/$($M.isSpecial)/$($M.requiredInstances)"
Check 'Summary: 2 slots, 6 instances from FromTime of the first date to ToTime of the last' "2/6/$(At 10 6)/$(At 11 18)/0" "$(@($M.positions).Count)/$($M.instanceCount)/$($M.firstInstanceFrom)/$($M.lastInstanceTo)/$($M.assignmentCount)"
$cmdSlot = ($M.positions | Where-Object { $_.position -eq 13 }).id
$simpleSlot = ($M.positions | Where-Object { $_.position -eq 0 }).id
$rows = Instances $M.id
Check 'Generated instances are contiguous 6h slots' '6/True/0' "$($rows.Count)/$(Contiguous $rows)/$(@($rows | Where-Object { ([datetime]$_.to - [datetime]$_.from).TotalHours -ne 6 }).Count)"
$legacy = (Invoke-Api -Path '/api/Mission/GetMissions' -Token $AD).Body | Where-Object { $_.id -eq $M.id }
Check 'Legacy GetMissions (UI) decrypts the new mission' 'בדיקת P4 מתגלגלת/משימה סינתטית לבדיקה/06:00/6/2' "$($legacy.name)/$($legacy.description)/$($legacy.fromTime)/$(@($legacy.missionInstances).Count)/$(@($legacy.positions).Count)"
Check 'Stored IsFilled false for the generated (empty) instances' 0 @($legacy.missionInstances | Where-Object { $_.isFilled }).Count

$special = @{ name = 'תדריך P4 מיוחד'; soldiersRequired = 0; commandersRequired = 1; duration = 3; isSpecial = $true; positions = @(@{ position = 15; count = 1 }); specialStart = (At 12 10) }
$r = Mission 'CreateMission' $special
$SM = $r.Body.value.mission
Check 'CreateMission (special) -> one instance of Duration hours from specialStart' "200/1/$(At 12 10)/$(At 12 13)/True" "$($r.Status)/$($r.Body.value.generatedInstances)/$($SM.firstInstanceFrom)/$($SM.lastInstanceTo)/$($SM.isSpecial)"

$bad = [ordered]@{
    'missing name' = With $create @{ name = ' ' }; 'no positions' = With $create @{ positions = @() }; 'invalid fromTime' = With $create @{ fromTime = '25:00' }
    'repeated position' = With $create @{ positions = @(@{ position = 0; count = 1 }, @{ position = 0; count = 1 }) }; 'unknown position' = With $create @{ positions = @(@{ position = 42; count = 1 }) }
    'slot count 0' = With $create @{ positions = @(@{ position = 0; count = 0 }) }; 'duration 0' = With $create @{ duration = 0 }; 'negative rest' = With $create @{ requiredRestAfter = -1 }
    'generate over 62 days' = With $create @{ generate = @{ from = (Day 10); to = (Day 80) } }; 'generate with dd/MM date' = With $create @{ generate = @{ from = '22/11/2026'; to = (Day 11) } }
    'recurring with specialStart' = With $create @{ specialStart = (At 12) }; 'special with generate' = With $special @{ generate = @{ from = (Day 12); to = (Day 13) } }; 'special without specialStart' = With $special @{ specialStart = $null }
}
foreach ($k in $bad.Keys) {
    $r = Mission 'CreateMission' $bad[$k]
    Check "CreateMission $k -> 400 GeneralResponse" '400/False/True' "$($r.Status)/$($r.Body.success)/$([bool]$r.Body.errorMessage)"
}
Check 'Rejected creates added nothing' ($baseline + 2) (Summaries).Count

# --- GenerateMissionInstances -------------------------------------------------------------------------------------------
$r = Mission 'GenerateMissionInstances' @{ missionId = $M.id; from = (Day 12); to = (Day 13); dryRun = $true }
$g = $r.Body.value
Check 'dryRun: count, skipped, resolved range, preview, lastSlot' "200/6/0/$(At 12 6)/$(At 13 18)/6/$(At 12 6)/$(At 13 12)" "$($r.Status)/$($g.count)/$($g.skippedExisting)/$($g.from)/$($g.to)/$(@($g.preview).Count)/$($g.preview[0].from)/$($g.lastSlot.from)"
Check 'dryRun saved nothing' 6 (Summary $M.id).instanceCount
$r = Mission 'GenerateMissionInstances' @{ missionId = $M.id; from = (Day 12); to = (Day 13) }
Check 'Generate -> 6 created' '200/6/12' "$($r.Status)/$($r.Body.value.count)/$((Summary $M.id).instanceCount)"
$r = Mission 'GenerateMissionInstances' @{ missionId = $M.id; from = (Day 12); to = (Day 13) }
Check 'Same range again -> nothing created, 6 skipped, no lastSlot' '200/0/6/True' "$($r.Status)/$($r.Body.value.count)/$($r.Body.value.skippedExisting)/$($null -eq $r.Body.value.lastSlot)"
$r = Mission 'GenerateMissionInstances' @{ missionId = $M.id; from = (Day 11); to = (Day 12); dryRun = $true }
Check 'Range overlapping both series -> only the 2 missing slots, 4 skipped' '2/4' "$($r.Body.value.count)/$($r.Body.value.skippedExisting)"
[void](Mission 'GenerateMissionInstances' @{ missionId = $M.id; from = (Day 11); to = (Day 12) })
$rows = Instances $M.id
Check 'The mission now has 14 contiguous instances' '14/True' "$($rows.Count)/$(Contiguous $rows)"
$r = Mission 'GenerateMissionInstances' @{ missionId = $M.id; from = "$(Day 14)T08:00:00"; to = "$(Day 14)T20:00"; dryRun = $true }
Check 'Explicit datetimes are used as is' "2/$(At 14 8)/$(At 14 20)" "$($r.Body.value.count)/$($r.Body.value.from)/$($r.Body.value.to)"
Check 'Generate over 62 days -> 400' 400 (Mission 'GenerateMissionInstances' @{ missionId = $M.id; from = (Day 1); to = (Day 70); dryRun = $true }).Status
Check 'Generate with to before from -> 400' 400 (Mission 'GenerateMissionInstances' @{ missionId = $M.id; from = (Day 14); to = (Day 13); dryRun = $true }).Status
Check 'Generate with an offset -> 400' 400 (Mission 'GenerateMissionInstances' @{ missionId = $M.id; from = "$(Day 14)T08:00:00Z"; to = (Day 15); dryRun = $true }).Status
Check 'Generate for a special mission -> 400' 400 (Mission 'GenerateMissionInstances' @{ missionId = $SM.id; from = (Day 14); to = (Day 15); dryRun = $true }).Status
Check 'Generate for an unknown mission -> 404' 404 (Mission 'GenerateMissionInstances' @{ missionId = 999999; from = (Day 14); to = (Day 15); dryRun = $true }).Status

# --- AddMissionInstances ----------------------------------------------------------------------------------------------------
$r = Mission 'AddMissionInstances' @{ missionId = $M.id; instances = @(@{ from = (At 15 8); to = (At 15 12) }) }
$added = $r.Body.value[0]
Check 'AddMissionInstances -> 200 with the new instance' "200/$(At 15 8)/$(At 15 12)/0/3/2" "$($r.Status)/$($added.from)/$($added.to)/$($added.assignedCount)/$($added.requiredCount)/$(@($added.missingPositions).Count)"
$r = Mission 'AddMissionInstances' @{ missionId = $M.id; instances = @(@{ from = (At 10 7); to = (At 10 8) }) }
Check 'Overlapping an existing instance -> 409' '409/False/True' "$($r.Status)/$($r.Body.success)/$($r.Body.errorMessage -match 'overlaps instance')"
Check 'Overlapping each other -> 400' 400 (Mission 'AddMissionInstances' @{ missionId = $M.id; instances = @(@{ from = (At 16 8); to = (At 16 12) }, @{ from = (At 16 11); to = (At 16 13) }) }).Status
Check 'to before from -> 400' 400 (Mission 'AddMissionInstances' @{ missionId = $M.id; instances = @(@{ from = (At 16 12); to = (At 16 8) }) }).Status
Check 'No instances -> 400' 400 (Mission 'AddMissionInstances' @{ missionId = $M.id; instances = @() }).Status
Check 'Unknown mission -> 404' 404 (Mission 'AddMissionInstances' @{ missionId = 999999; instances = @(@{ from = (At 16 8); to = (At 16 12) }) }).Status

# --- UpdateMissionInstance ----------------------------------------------------------------------------------------------------
$r = Mission 'UpdateMissionInstance' @{ missionInstanceId = $added.instanceId; from = (At 15 9); to = (At 15 13) }
Check 'Reschedule an empty instance -> 200, new times, no validations' "200/$(At 15 9)/$(At 15 13)/0" "$($r.Status)/$($r.Body.value.instance.from)/$($r.Body.value.instance.to)/$(@($r.Body.value.validations).Count)"
Check 'Reschedule onto a sibling instance -> 409' 409 (Mission 'UpdateMissionInstance' @{ missionInstanceId = $added.instanceId; from = (At 10 10); to = (At 10 11) }).Status
Check 'Reschedule with to before from -> 400' 400 (Mission 'UpdateMissionInstance' @{ missionInstanceId = $added.instanceId; from = (At 15 13); to = (At 15 9) }).Status
Check 'Reschedule an unknown instance -> 404' 404 (Mission 'UpdateMissionInstance' @{ missionInstanceId = 999999; from = (At 15 9); to = (At 15 13) }).Status

$guard = Summaries | Where-Object { $_.name -eq 'שמירה בש.ג' }
$g21 = @((Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $AD -Body @{ from = (At 1 8); to = (At 1 12); missionIds = @($guard.id) }).Body) | Where-Object { $_.from -eq (At 1 8) }
$busy = [int]@($g21.assignments)[0].soldierId
$I = (Mission 'AddMissionInstances' @{ missionId = $M.id; instances = @(@{ from = (At 1 22); to = (At 1 23) }) }).Body.value[0].instanceId
$r = Invoke-Api -Path '/api/Schedule/AssignSoldier' -Method POST -Token $AD -Body @{ missionInstanceId = $I; soldierId = $busy }
Check "Soldier $busy (guard 08-12 tomorrow) assigned to a new 22:00 instance" '200/True' "$($r.Status)/$($r.Body.value.validation.isValid)"
$overlap = @{ missionInstanceId = $I; from = (At 1 11); to = (At 1 13) }
$r = Mission 'UpdateMissionInstance' (With $overlap @{ dryRun = $true })
Check 'dryRun onto the soldiers guard slot -> 409 OVERLAP with details' "409/True/$($g21.instanceId)" "$($r.Status)/$($r.Body.value.validations[0].violations -contains 'OVERLAP')/$($r.Body.value.validations[0].overlap.instanceId)"
$r = Mission 'UpdateMissionInstance' $overlap
Check 'Reschedule onto the soldiers guard slot -> 409, instance unchanged' "409/True/$(At 1 22)" "$($r.Status)/$($null -eq $r.Body.value.instance)/$((Instance $M.id $I).from)"
$r = Mission 'UpdateMissionInstance' (With $overlap @{ allowViolations = $true })
Check 'allowViolations applies it' "200/$(At 1 11)" "$($r.Status)/$($r.Body.value.instance.from)"
$r = Mission 'UpdateMissionInstance' @{ missionInstanceId = $I; from = (At 1 14); to = (At 1 15) }
Check 'Only OVERLAP blocks: REST_BEFORE 2h/8h is reported and the change applies' '200/True/2/8' "$($r.Status)/$($r.Body.value.validations[0].violations -contains 'REST_BEFORE')/$($r.Body.value.validations[0].restBeforeHours)/$($r.Body.value.validations[0].requiredRestBeforeHours)"
$r = Mission 'UpdateMissionInstance' @{ missionInstanceId = $I; from = (At 1 21); to = (At 1 23); dryRun = $true }
Check 'dryRun returns the would-be instance and saves nothing' "200/$(At 1 21)/1/$(At 1 14)" "$($r.Status)/$($r.Body.value.instance.from)/$($r.Body.value.instance.assignedCount)/$((Instance $M.id $I).from)"

# --- DeleteMissionInstance (also drops pending auto-assign candidate rows of the instance) -----------------------------------------
[void](Sql "INSERT INTO SoldierMissionCandidates (SoldierId, MissionInstanceId, MissionPositionId, CandidateId) VALUES ($busy, $I, $simpleSlot, 'p4-verify')")
$r = Mission "DeleteMissionInstance?missionInstanceId=$I" $null
Check 'DeleteMissionInstance -> 200, 1 assignment deleted' '200/True/1' "$($r.Status)/$($r.Body.success)/$($r.Body.value.deletedAssignments)"
Check 'The instance is gone' 0 @(Instance $M.id $I).Count
Check 'Its pending candidate row was removed' '0' "$(Sql "SELECT COUNT(*) FROM SoldierMissionCandidates WHERE CandidateId = 'p4-verify'")"
$sol = (Invoke-Api -Path '/api/Soldiers/GetSoldiers?reloadCache=true' -Token $AD).Body | Where-Object { $_.id -eq $busy }
Check 'The soldier no longer has that assignment (UI data)' 0 @($sol.missions | Where-Object { $_.missionInstance.id -eq $I }).Count
Check 'Delete an unknown instance -> 404' 404 (Mission 'DeleteMissionInstance?missionInstanceId=999999' $null).Status

# --- UpdateMissionDetails ----------------------------------------------------------------------------------------------------
$idsBefore = InstanceIds $M.id
$r = Mission 'UpdateMissionDetails' @{ id = $M.id; name = 'בדיקת P4 שעודכנה'; description = 'תיאור חדש'; requiredRestAfter = 12; fromTime = '7:00' }
Check 'UpdateMissionDetails -> 200, fields updated, fromTime normalized' '200/בדיקת P4 שעודכנה/תיאור חדש/12/07:00/18:00/6' "$($r.Status)/$($r.Body.value.name)/$($r.Body.value.description)/$($r.Body.value.requiredRestAfter)/$($r.Body.value.fromTime)/$($r.Body.value.toTime)/$($r.Body.value.duration)"
Check 'Instances untouched (same IDs)' $idsBefore (InstanceIds $M.id)
Check 'Legacy GetMissions shows the new name' 'בדיקת P4 שעודכנה' "$(((Invoke-Api -Path '/api/Mission/GetMissions' -Token $AD).Body | Where-Object { $_.id -eq $M.id }).name)"

$target = Instances $M.id | Where-Object { $_.from -eq (At 10 6) }
$cmd = Validate $target.instanceId (1..16) $cmdSlot | Where-Object { $_.isValid -and $_.positionFit -eq 'exact' } | Select-Object -First 1
$simples = @(Validate $target.instanceId (1..16) $simpleSlot | Where-Object { $_.isValid -and $_.positionFit -eq 'exact' } | Select-Object -First 2)
foreach ($pick in @($cmd) + $simples) { [void](Invoke-Api -Path '/api/Schedule/AssignSoldier' -Method POST -Token $AD -Body @{ missionInstanceId = $target.instanceId; soldierId = $pick.soldierId }) }
Check 'Fully staffed instance: stored IsFilled true' '3/True' "$((Instance $M.id $target.instanceId).assignedCount)/$((StoredInstance $target.instanceId).isFilled)"
$r = Mission 'UpdateMissionDetails' @{ id = $M.id; positions = @(@{ id = $cmdSlot; position = 13; count = 1 }, @{ id = $simpleSlot; position = 0; count = 3 }, @{ position = 3; count = 1 }) }
$medicSlot = ($r.Body.value.positions | Where-Object { $_.position -eq 3 }).id
Check 'Upsert: Simple count 3 and a new Medic slot' '200/3/3/True' "$($r.Status)/$(@($r.Body.value.positions).Count)/$(($r.Body.value.positions | Where-Object { $_.id -eq $simpleSlot }).count)/$([bool]$medicSlot)"
$live = Instance $M.id $target.instanceId
Check 'IsFilled recomputed for the new required count (stored and live)' 'False/False/5' "$((StoredInstance $target.instanceId).isFilled)/$($live.isFilled)/$($live.requiredCount)"
Check 'Instances still untouched' $idsBefore (InstanceIds $M.id)
$r = Mission 'UpdateMissionDetails' @{ id = $M.id; positions = @(@{ id = $cmdSlot; position = 13; count = 1 }, @{ id = $medicSlot; position = 3; count = 1 }) }
Check 'Removing the Simple slot (2 assignments use it) -> 409' '409/False/True' "$($r.Status)/$($r.Body.success)/$($r.Body.errorMessage -match 'referenced by 2 assignment')"
[void](Sql "INSERT INTO SoldierMissionCandidates (SoldierId, MissionInstanceId, MissionPositionId, CandidateId) VALUES ($busy, $($target.instanceId), $medicSlot, 'p4-verify')")
$restore = @(@{ id = $cmdSlot; position = 13; count = 1 }, @{ id = $simpleSlot; position = 0; count = 2 })
$r = Mission 'UpdateMissionDetails' @{ id = $M.id; positions = $restore }
Check 'Removing the Medic slot while a pending candidate uses it -> 409' '409/True' "$($r.Status)/$($r.Body.errorMessage -match '1 pending auto-assign candidate')"
[void](Sql "DELETE FROM SoldierMissionCandidates WHERE CandidateId = 'p4-verify'")
$r = Mission 'UpdateMissionDetails' @{ id = $M.id; positions = $restore }
Check 'Removing the unused Medic slot and restoring Simple x2 -> 200, filled again' '200/2/True' "$($r.Status)/$(@($r.Body.value.positions).Count)/$((StoredInstance $target.instanceId).isFilled)"
$r = Mission 'UpdateMissionDetails' @{ id = $M.id; description = '' }
Check 'An empty description clears it' '200/True' "$($r.Status)/$($null -eq $r.Body.value.description)"
Check 'Nothing to update -> 400' 400 (Mission 'UpdateMissionDetails' @{ id = $M.id }).Status
Check 'Unknown mission -> 404' 404 (Mission 'UpdateMissionDetails' @{ id = 999999; name = 'x' }).Status
Check 'Invalid toTime -> 400' 400 (Mission 'UpdateMissionDetails' @{ id = $M.id; toTime = '7pm' }).Status
Check 'Slot ID of another mission -> 400' 400 (Mission 'UpdateMissionDetails' @{ id = $M.id; positions = @(@{ id = $SM.positions[0].id; position = 15; count = 1 }) }).Status
Check 'Empty slot list -> 400' 400 (Mission 'UpdateMissionDetails' @{ id = $M.id; positions = @() }).Status

# --- SetSoldierActive ----------------------------------------------------------------------------------------------------------------
$sid = 15
function LiteActive { "$((@((Invoke-Api -Path '/api/Soldiers/GetSoldiersLite?includeInactive=true' -Token $AD).Body) | Where-Object { $_.id -eq $sid }).active)" }
$r = Invoke-Api -Path '/api/Soldiers/SetSoldierActive' -Method POST -Token $AD -Body @{ soldierId = $sid; active = $false }
Check 'Deactivate soldier 15 -> 200, active=false' '200/True/False' "$($r.Status)/$($r.Body.success)/$($r.Body.value.active)"
Check 'GetSoldiersLite hides the inactive soldier; includeInactive shows it' '0/False' "$(@((Invoke-Api -Path '/api/Soldiers/GetSoldiersLite' -Token $AD).Body | Where-Object { $_.id -eq $sid }).Count)/$(LiteActive)"
Check 'Validation sees INACTIVE immediately' 'True' "$((Validate $target.instanceId @($sid))[0].violations -contains 'INACTIVE')"
$full = (Invoke-Api -Path '/api/Soldiers/GetSoldiers' -Token $AD).Body | Where-Object { $_.id -eq $sid }
$upd = @{ id = $sid; name = $full.name; personalNumber = $full.personalNumber; phone = $full.phone; platoon = $full.platoon; company = $full.company; active = $true; positions = @($full.positions); vacations = @(); missions = @() }
$r = Invoke-Api -Path '/api/Soldiers/UpdateSoldier' -Method POST -Token $AD -Body $upd
Check 'Legacy UpdateSoldier with active=true -> 200, Active unchanged (still ignored)' '200/False' "$($r.Status)/$(LiteActive)"
$r = Invoke-Api -Path '/api/Soldiers/SetSoldierActive' -Method POST -Token $AD -Body @{ soldierId = $sid; active = $true }
Check 'Reactivate -> 200, no INACTIVE anymore' '200/True/False' "$($r.Status)/$($r.Body.value.active)/$((Validate $target.instanceId @($sid))[0].violations -contains 'INACTIVE')"
Check 'SetSoldierActive unknown soldier -> 404' 404 (Invoke-Api -Path '/api/Soldiers/SetSoldierActive' -Method POST -Token $AD -Body @{ soldierId = 9999; active = $true }).Status

# --- Legacy UI mission endpoints still work (and UpdateMission is still destructive) ----------------------------------------------
$legacyEdit = @{ id = $M.id; name = 'בדיקת P4 דרך העריכה הישנה'; description = 'עריכה מהממשק'; soldiersRequired = 2; commandersRequired = 1; duration = 6; fromTime = '07:00'; toTime = '18:00'
    isSpecial = $false; requiredRestAfter = 12; positions = @($M.positions | ForEach-Object { @{ id = $_.id; missionId = $M.id; position = $_.position; count = $_.count } })
    missionInstances = @(@{ id = 0; fromTime = (At 20 6); toTime = (At 20 12); soldierMissions = @() }) }
$r = Mission 'UpdateMission' $legacyEdit
$after = Summary $M.id
Check 'Legacy UpdateMission works on a P4 mission and recreates every instance (assignments lost)' '200/בדיקת P4 דרך העריכה הישנה/1/0' "$($r.Status)/$($after.name)/$($after.instanceCount)/$($after.assignmentCount)"
$r = Mission 'AddMission' @{ id = 0; name = 'P4 legacy add'; description = 'legacy'; soldiersRequired = 0; commandersRequired = 1; duration = 2; isSpecial = $true; positions = @(@{ id = 0; missionId = 0; position = 15; count = 1 }); missionInstances = @(@{ id = 0; fromTime = (At 21 10); toTime = (At 21 12); isFilled = $false; soldierMissions = @() }) }
$legacyId = $r.Body.id
Check 'Legacy AddMission still works' '200/1' "$($r.Status)/$((Summary $legacyId).instanceCount)"
foreach ($id in @($M.id, $SM.id, $legacyId)) { Check "Cleanup: legacy DeleteMission($id)" 200 (Invoke-Api -Path "/api/Mission/DeleteMission?missionId=$id" -Method POST -Token $AD).Status }
Check 'Missions back to the baseline' $baseline (Summaries).Count
Check 'No test candidate rows left' '0' "$(Sql "SELECT COUNT(*) FROM SoldierMissionCandidates WHERE CandidateId = 'p4-verify'")"

# --- Audit log -----------------------------------------------------------------------------------------------------------------------
$log = Get-ChildItem 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\ShabzakAPI\04.ShabzakAPI\Logs' -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$lines = @(Get-Content $log.FullName -Encoding utf8 | Where-Object { $_ -match ' - (Mission/(CreateMission|UpdateMissionDetails|GenerateMissionInstances|AddMissionInstances|UpdateMissionInstance|DeleteMissionInstance)|Soldiers/SetSoldierActive) ' })
Check 'P4 writes are logged with status and user ID' 'True' "$(@($lines | Where-Object { $_ -match '-> \d{3} by user ID=\d+$' }).Count -ge 50)"
Check 'No Hebrew text (names) in the P4 write log lines' 0 @($lines | Where-Object { $_ -match '[\u0590-\u05FF]' }).Count

$results | Format-Table -AutoSize | Out-String -Width 260
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
