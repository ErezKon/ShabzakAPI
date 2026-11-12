# Phase 3 checks (schedule writes) against the LOCAL synthetic dev DB (run after seed-localdb.ps1 and verify-p1.ps1).
# Every change made here is reverted at the end of its scenario, so verify-p2.ps1 / verify-p3.ps1 can be re-run.
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

$missions = (Invoke-Api -Path '/api/Mission/GetMissionsSummary' -Token $AD).Body
$guard = $missions | Where-Object { $_.name -eq 'שמירה בש.ג' }
$patrol = $missions | Where-Object { $_.name -eq 'סיור' }
$standby = $missions | Where-Object { $_.name -eq 'כוננות' }
$guardSlot = $guard.positions[0].id
$pSimple = ($patrol.positions | Where-Object { $_.position -eq 0 }).id
$pMedic = ($patrol.positions | Where-Object { $_.position -eq 3 }).id

function Schedule { (Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $AD -Body @{ from = (Iso $today.AddDays(-2)); to = (Iso $today.AddDays(5)) }).Body }
function At($mission, [datetime]$from) { Schedule | Where-Object { $_.missionId -eq $mission.id -and $_.from -eq (Iso $from) } | Select-Object -First 1 }
function Inst([int]$id) { Schedule | Where-Object { $_.instanceId -eq $id } }
function Ids($instance) { (@($instance.assignments | ForEach-Object { $_.soldierId }) | Sort-Object) -join ',' }
function SlotOf($instance, [int]$soldierId) { ($instance.assignments | Where-Object { $_.soldierId -eq $soldierId }).missionPositionId }
function StoredInstance([int]$id) { (Invoke-Api -Path '/api/Mission/GetMissions' -Token $AD).Body | ForEach-Object { $_.missionInstances } | Where-Object { $_.id -eq $id } }
function Post([string]$path, $body, $token = $AD) { Invoke-Api -Path "/api/Schedule/$path" -Method POST -Token $token -Body $body }
function ValidIds([int]$instanceId, $slot = $null) {
    $body = @{ missionInstanceId = $instanceId; soldierIds = @(1..16) }
    if ($slot) { $body.missionPositionId = $slot }
    @((Post 'ValidateAssignments' $body).Body | Where-Object { $_.isValid -and $_.positionFit -eq 'exact' } | ForEach-Object { [int]$_.soldierId } | Sort-Object)
}

$T = At $guard $today.AddDays(1).AddHours(12)    # empty guard slot, tomorrow 12:00-16:00
$G2 = At $guard $today.AddDays(1).AddHours(16)   # empty guard slot, tomorrow 16:00-20:00
$G3 = At $guard $today.AddDays(1).AddHours(20)   # empty guard slot, tomorrow 20:00-24:00
$G20 = At $guard $today.AddDays(1).AddHours(4)   # staffed guard slot, tomorrow 04:00-08:00
$G21 = At $guard $today.AddDays(1).AddHours(8)   # staffed guard slot, tomorrow 08:00-12:00
$G17 = At $guard $today.AddHours(16)             # staffed guard slot, today 16:00-20:00
$P47 = At $patrol $today.AddHours(6)             # staffed patrol, today 06:00-14:00
$P48 = At $patrol $today.AddHours(14)            # empty patrol, today 14:00-22:00
$P51 = At $patrol $today.AddDays(2).AddHours(6)  # empty patrol, in two days 06:00-14:00
$S60 = At $standby $today.AddDays(1).AddHours(8) # empty standby, tomorrow 08:00-20:00
Check 'Scenario instances found' 'True' "$([bool]($T -and $G2 -and $G3 -and $G20 -and $G21 -and $G17 -and $P47 -and $P48 -and $P51 -and $S60))"

# --- Authorization --------------------------------------------------------------------------------------------
foreach ($p in 'AssignSoldier', 'UnassignSoldier', 'ReplaceSoldier', 'MoveSoldier') {
    Check "Regular $p -> 403" 403 (Post $p @{ missionInstanceId = $T.instanceId; soldierId = 5 } $RG).Status
}
Check 'AssignSoldier without token -> 401' 401 (Invoke-Api -Path '/api/Schedule/AssignSoldier' -Method POST -Body @{ missionInstanceId = $T.instanceId; soldierId = 5 }).Status

# --- AssignSoldier: success, live + stored IsFilled, UI data ---------------------------------------------------
$valid = ValidIds $T.instanceId
Check 'At least 3 valid soldiers for the target guard slot' 'True' "$($valid.Count -ge 3)"
$V1, $V2, $V3 = $valid[0], $valid[1], $valid[2]
$r = Post 'AssignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V1 }
Check 'Assign V1 -> 200/success/1 of 2/valid' '200/True/1/False/True' "$($r.Status)/$($r.Body.success)/$($r.Body.value.instance.assignedCount)/$($r.Body.value.instance.isFilled)/$($r.Body.value.validation.isValid)"
Check 'Assign V1: auto slot = the guard slot (= suggested)' "$guardSlot/$guardSlot" "$(SlotOf $r.Body.value.instance $V1)/$($r.Body.value.validation.suggestedMissionPositionId)"
Check 'Assign V1: stored IsFilled false (1 of 2)' 'False' "$((StoredInstance $T.instanceId).isFilled)"
$r = Post 'AssignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V2 }
Check 'Assign V2 -> 200, instance filled' '200/2/True' "$($r.Status)/$($r.Body.value.instance.assignedCount)/$($r.Body.value.instance.isFilled)"
$stored = StoredInstance $T.instanceId
Check 'Stored IsFilled true after the second seat (single SaveChanges, recomputed)' 'True' "$($stored.isFilled)"
Check 'Legacy GetMissions (UI source) shows both soldiers' "$((@($V1, $V2) | Sort-Object) -join ',')" "$((@($stored.soldierMissions | ForEach-Object { $_.soldier.id }) | Sort-Object) -join ',')"
$sol = (Invoke-Api -Path '/api/Soldiers/GetSoldiers?reloadCache=true' -Token $AD).Body | Where-Object { $_.id -eq $V1 }
Check 'GetSoldiers(reloadCache) lists the new mission for V1' 'True' "$(@($sol.missions | Where-Object { $_.missionInstance.id -eq $T.instanceId }).Count -eq 1)"

# --- Blocked writes and overrides -----------------------------------------------------------------------------
$r = Post 'AssignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V3 }
Check 'Assign into a full instance -> 409, success=false, no instance, INSTANCE_FULL' '409/False/True/True' "$($r.Status)/$($r.Body.success)/$($null -eq $r.Body.value.instance)/$($r.Body.value.validation.violations -contains 'INSTANCE_FULL')"
Check 'Blocked message names the code and the override' 'True' "$($r.Body.errorMessage -match 'INSTANCE_FULL' -and $r.Body.errorMessage -match 'allowViolations')"
Check 'Blocked assign wrote nothing' '2' "$((Inst $T.instanceId).assignedCount)"
$r = Post 'AssignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V3; allowViolations = $true }
Check 'allowViolations over-fills the instance -> 200, 3 of 2' '200/3/True' "$($r.Status)/$($r.Body.value.instance.assignedCount)/$($r.Body.value.instance.isFilled)"
$r = Post 'UnassignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V3 }
Check 'Unassign the override -> 200, 2 of 2' '200/2' "$($r.Status)/$($r.Body.value.assignedCount)"
$r = Post 'AssignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V1; allowViolations = $true }
Check 'ALREADY_ASSIGNED cannot be overridden -> 409' '409/True/True' "$($r.Status)/$($r.Body.value.validation.violations -contains 'ALREADY_ASSIGNED')/$($r.Body.errorMessage -match 'cannot be overridden')"
$restSoldier = [int](@($G21.assignments)[0].soldierId)   # guard 08:00-12:00 tomorrow -> 4h before 16:00
$r = Post 'AssignSoldier' @{ missionInstanceId = $G2.instanceId; soldierId = $restSoldier }
Check "Soldier $restSoldier into the 16:00 slot -> 409 REST_BEFORE 4h/8h" '409/True/4/8' "$($r.Status)/$($r.Body.value.validation.violations -contains 'REST_BEFORE')/$($r.Body.value.validation.restBeforeHours)/$($r.Body.value.validation.requiredRestBeforeHours)"
$r = Post 'AssignSoldier' @{ missionInstanceId = $G2.instanceId; soldierId = $restSoldier; allowViolations = $true }
Check 'REST_BEFORE with allowViolations -> 200 (validation still reported)' '200/True/False' "$($r.Status)/$($r.Body.success)/$($r.Body.value.validation.isValid)"
Check 'Undo the override' 200 (Post 'UnassignSoldier' @{ missionInstanceId = $G2.instanceId; soldierId = $restSoldier }).Status
Check 'Unknown soldier -> 404' 404 (Post 'AssignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = 9999 }).Status
Check 'Unknown instance -> 404' 404 (Post 'AssignSoldier' @{ missionInstanceId = 999999; soldierId = $V3 }).Status
$r = Post 'AssignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V3; missionPositionId = $pMedic }
Check 'Slot of another mission -> 400 GeneralResponse' '400/False/True' "$($r.Status)/$($r.Body.success)/$([bool]$r.Body.errorMessage)"

# --- Requested slot, automatic slot, and the vacated-slot preference of ReplaceSoldier -------------------------
$medics = ValidIds $P51.instanceId $pMedic
Check 'Two valid medics for the patrol in two days' 'True' "$($medics.Count -ge 2)"
$M1, $M2 = $medics[0], $medics[1]
$suggested = (Post 'ValidateAssignments' @{ missionInstanceId = $P51.instanceId; soldierIds = @($M1) }).Body[0].suggestedMissionPositionId
$r = Post 'AssignSoldier' @{ missionInstanceId = $P51.instanceId; soldierId = $M1 }
Check 'Automatic slot = ValidateAssignments suggestion (Simple: more missing)' "$suggested/$pSimple" "$(SlotOf $r.Body.value.instance $M1)/$pSimple"
[void](Post 'UnassignSoldier' @{ missionInstanceId = $P51.instanceId; soldierId = $M1 })
$r = Post 'AssignSoldier' @{ missionInstanceId = $P51.instanceId; soldierId = $M1; missionPositionId = $pMedic }
Check 'Requested Medic slot is used' "200/$pMedic" "$($r.Status)/$(SlotOf $r.Body.value.instance $M1)"
$withoutPreference = (Post 'ValidateAssignments' @{ missionInstanceId = $P51.instanceId; soldierIds = @($M2); ignoreSoldierId = $M1 }).Body[0].suggestedMissionPositionId
$r = Post 'ReplaceSoldier' @{ missionInstanceId = $P51.instanceId; oldSoldierId = $M1; newSoldierId = $M2 }
Check 'Replace M1 -> M2 keeps the vacated Medic slot (plain suggestion would be Simple)' "200/$pMedic/$pSimple" "$($r.Status)/$(SlotOf $r.Body.value.instances[0] $M2)/$withoutPreference"
Check 'Replace returns one instance and one validation (the new soldier)' "1/1/$M2/True" "$(@($r.Body.value.instances).Count)/$(@($r.Body.value.validations).Count)/$($r.Body.value.validations[0].soldierId)/$($r.Body.value.validations[0].isValid)"
Check 'Restore the patrol' 200 (Post 'UnassignSoldier' @{ missionInstanceId = $P51.instanceId; soldierId = $M2 }).Status

# --- UnassignSoldier -------------------------------------------------------------------------------------------
$r = Post 'UnassignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V2 }
Check 'Unassign V2 -> 200, 1 of 2, not filled' '200/True/1/False' "$($r.Status)/$($r.Body.success)/$($r.Body.value.assignedCount)/$($r.Body.value.isFilled)"
Check 'Stored IsFilled back to false' 'False' "$((StoredInstance $T.instanceId).isFilled)"
Check 'Unassign a soldier who is not assigned -> 404' 404 (Post 'UnassignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V2 }).Status

# --- ReplaceSoldier (no swap) ------------------------------------------------------------------------------------
$r = Post 'ReplaceSoldier' @{ missionInstanceId = $T.instanceId; oldSoldierId = $V1; newSoldierId = $V2 }
Check 'Replace V1 -> V2 -> 200, V2 in the slot' "200/$V2/$guardSlot" "$($r.Status)/$(Ids $r.Body.value.instances[0])/$(SlotOf $r.Body.value.instances[0] $V2)"
$r = Post 'ReplaceSoldier' @{ missionInstanceId = $T.instanceId; oldSoldierId = $V2; newSoldierId = $restSoldier }
Check "Replace with soldier $restSoldier (0h rest) -> 409, nothing changed" "409/True/0/$V2" "$($r.Status)/$($r.Body.value.validations[0].violations -contains 'REST_BEFORE')/$(@($r.Body.value.instances).Count)/$(Ids (Inst $T.instanceId))"
Check 'Replace with the same soldier -> 400' 400 (Post 'ReplaceSoldier' @{ missionInstanceId = $T.instanceId; oldSoldierId = $V2; newSoldierId = $V2 }).Status
Check 'Replace a soldier who is not assigned -> 404' 404 (Post 'ReplaceSoldier' @{ missionInstanceId = $T.instanceId; oldSoldierId = $V3; newSoldierId = $V1 }).Status
Check 'Restore the target slot (empty)' '200/0' "$((Post 'UnassignSoldier' @{ missionInstanceId = $T.instanceId; soldierId = $V2 }).Status)/$((Inst $T.instanceId).assignedCount)"

# --- Swap: back-to-back guard slots (each soldier's own instance is excluded) -------------------------------------
$a = [int](@($G21.assignments)[0].soldierId); $b = [int](@($G20.assignments)[0].soldierId)
$plain = (Post 'ValidateAssignments' @{ missionInstanceId = $G20.instanceId; soldierIds = @($a); ignoreSoldierId = $b }).Body[0]
Check "Without exclusion, soldier $a in the 04:00 slot violates REST_AFTER (own 08:00 slot)" 'True' "$($plain.violations -contains 'REST_AFTER')"
$r = Post 'ReplaceSoldier' @{ missionInstanceId = $G21.instanceId; oldSoldierId = $a; newSoldierId = $b; swap = $true; swapMissionInstanceId = $G20.instanceId }
Check 'Swap -> 200, two instances, both placements valid' '200/2/True/True' "$($r.Status)/$(@($r.Body.value.instances).Count)/$($r.Body.value.validations[0].isValid)/$($r.Body.value.validations[1].isValid)"
Check 'Swap: soldiers exchanged' "True/True/False/False" "$((Ids $r.Body.value.instances[0]).Split(',') -contains "$b")/$((Ids $r.Body.value.instances[1]).Split(',') -contains "$a")/$((Ids $r.Body.value.instances[0]).Split(',') -contains "$a")/$((Ids $r.Body.value.instances[1]).Split(',') -contains "$b")"
Check 'Swap: both instances stay filled (stored)' 'True/True' "$((StoredInstance $G20.instanceId).isFilled)/$((StoredInstance $G21.instanceId).isFilled)"
$r = Post 'ReplaceSoldier' @{ missionInstanceId = $G21.instanceId; oldSoldierId = $b; newSoldierId = $a; swap = $true; swapMissionInstanceId = $G20.instanceId }
Check 'Swap back -> 200, original soldiers restored' "200/$(Ids $G20)/$(Ids $G21)" "$($r.Status)/$(Ids (Inst $G20.instanceId))/$(Ids (Inst $G21.instanceId))"
Check 'Swap without swapMissionInstanceId -> 400' 400 (Post 'ReplaceSoldier' @{ missionInstanceId = $G21.instanceId; oldSoldierId = $a; newSoldierId = $b; swap = $true }).Status
Check 'Swap when the new soldier is not in the swap instance -> 404' 404 (Post 'ReplaceSoldier' @{ missionInstanceId = $G21.instanceId; oldSoldierId = $a; newSoldierId = $V3; swap = $true; swapMissionInstanceId = $G20.instanceId }).Status

# --- MoveSoldier ---------------------------------------------------------------------------------------------------
$m = [int](@($G21.assignments)[1].soldierId)
$r = Post 'MoveSoldier' @{ soldierId = $m; fromMissionInstanceId = $G21.instanceId; toMissionInstanceId = $S60.instanceId }
Check "Move soldier $m guard 08-12 -> overlapping standby 08-20 -> 200 (source excluded)" '200/True/2' "$($r.Status)/$($r.Body.value.validations[0].isValid)/$(@($r.Body.value.instances).Count)"
Check 'Move: source lost the soldier, target has them' "False/True" "$((Ids $r.Body.value.instances[0]).Split(',') -contains "$m")/$((Ids $r.Body.value.instances[1]).Split(',') -contains "$m")"
Check 'Move: stored IsFilled of source false, target false (1 of 3)' 'False/False' "$((StoredInstance $G21.instanceId).isFilled)/$((StoredInstance $S60.instanceId).isFilled)"
$r = Post 'MoveSoldier' @{ soldierId = $m; fromMissionInstanceId = $S60.instanceId; toMissionInstanceId = $G21.instanceId }
Check 'Move back -> 200, guard slot restored and filled' "200/$(Ids $G21)/True/0" "$($r.Status)/$(Ids (Inst $G21.instanceId))/$((StoredInstance $G21.instanceId).isFilled)/$((Inst $S60.instanceId).assignedCount)"
$busy = [int](@($G17.assignments | Where-Object { (Ids $P47).Split(',') -contains "$($_.soldierId)" })[0].soldierId)
$r = Post 'MoveSoldier' @{ soldierId = $busy; fromMissionInstanceId = $G17.instanceId; toMissionInstanceId = $P48.instanceId }
Check "Move soldier $busy into the patrol right after their 06-14 patrol -> 409 REST_BEFORE" '409/True/0' "$($r.Status)/$($r.Body.value.validations[0].violations -contains 'REST_BEFORE')/$(@($r.Body.value.instances).Count)"
Check 'Move to the same instance -> 400' 400 (Post 'MoveSoldier' @{ soldierId = $m; fromMissionInstanceId = $G21.instanceId; toMissionInstanceId = $G21.instanceId }).Status
Check 'Move a soldier who is not in the source -> 404' 404 (Post 'MoveSoldier' @{ soldierId = $V3; fromMissionInstanceId = $G21.instanceId; toMissionInstanceId = $S60.instanceId }).Status
Check 'Move with a slot of another mission -> 400' 400 (Post 'MoveSoldier' @{ soldierId = $m; fromMissionInstanceId = $G21.instanceId; toMissionInstanceId = $S60.instanceId; toMissionPositionId = $guardSlot }).Status

# --- Concurrency: two parallel requests for the last free seat ------------------------------------------------------
$free = ValidIds $G2.instanceId
[void](Post 'AssignSoldier' @{ missionInstanceId = $G2.instanceId; soldierId = $free[0] })
$base = 'https://localhost:7170'; $tok = $AD; $gid = $G2.instanceId
$statuses = @($free[1], $free[2]) | ForEach-Object -Parallel {
    $body = @{ missionInstanceId = $using:gid; soldierId = $_ } | ConvertTo-Json -Compress
    [int](Invoke-WebRequest -Uri "$using:base/api/Schedule/AssignSoldier" -Method POST -Headers @{ Authorization = "Bearer $using:tok" } -Body $body -ContentType 'application/json' -SkipHttpErrorCheck).StatusCode
} -ThrottleLimit 2
Check 'Parallel requests for the last seat -> one 200, one 409' '200,409' "$((@($statuses) | Sort-Object) -join ',')"
Check 'The slot is not over-filled' '2' "$((Inst $G2.instanceId).assignedCount)"
foreach ($s in (Inst $G2.instanceId).assignments) { [void](Post 'UnassignSoldier' @{ missionInstanceId = $G2.instanceId; soldierId = $s.soldierId }) }
Check 'Restore the 16:00 slot (empty)' 0 (Inst $G2.instanceId).assignedCount

# --- Legacy UI endpoints still work (RemoveSoldierFromMissionInstance was broken before P3) -------------------------
$x = (ValidIds $G3.instanceId)[0]
[void](Post 'AssignSoldier' @{ missionInstanceId = $G3.instanceId; soldierId = $x })
$r = Invoke-Api -Path "/api/Mission/RemoveSoldierFromMissionInstance?soldierId=$x&missionInstanceId=$($G3.instanceId)" -Method POST -Token $AD
Check 'Legacy RemoveSoldierFromMissionInstance -> 200 and removed' '200/0' "$($r.Status)/$((Inst $G3.instanceId).assignedCount)"
$y = (ValidIds $G3.instanceId)[1]
[void](Post 'AssignSoldier' @{ missionInstanceId = $G3.instanceId; soldierId = $x })
$r = Invoke-Api -Path '/api/Mission/ReplaceSoldierInMissionInstance' -Method POST -Token $AD -Body @{ missionInstanceId = $G3.instanceId; oldSoldierId = $x; newSoldierId = $y; swap = $false }
Check 'Legacy ReplaceSoldierInMissionInstance -> 200 and replaced' "200/$y" "$($r.Status)/$(Ids (Inst $G3.instanceId))"
[void](Post 'UnassignSoldier' @{ missionInstanceId = $G3.instanceId; soldierId = $y })
Check 'Restore the 20:00 slot (empty)' 0 (Inst $G3.instanceId).assignedCount

# --- Audit log: every write logged with IDs only ---------------------------------------------------------------------
$log = Get-ChildItem 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\ShabzakAPI\04.ShabzakAPI\Logs' -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$lines = @(Get-Content $log.FullName -Encoding utf8 | Where-Object { $_ -match ' - Schedule/(Assign|Unassign|Replace|Move)Soldier ' })
Check 'Schedule writes are logged (with status and user ID)' 'True' "$($lines.Count -ge 30 -and @($lines | Where-Object { $_ -notmatch '-> \d{3} by user ID=\d+$' }).Count -eq 0)"
Check 'No Hebrew text (names) in the write log lines' 0 @($lines | Where-Object { $_ -match '[\u0590-\u05FF]' }).Count

$results | Format-Table -AutoSize | Out-String -Width 260
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
