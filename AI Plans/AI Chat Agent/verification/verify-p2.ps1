# Phase 2 checks against the LOCAL synthetic dev DB (run after seed-localdb.ps1 and verify-p1.ps1).
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'dev.ps1')
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, $expected, $actual) {
    $ok = "$expected" -eq "$actual"
    $results.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $name; Expected = "$expected"; Actual = "$actual" })
}
function Keys($obj) { (@($obj.PSObject.Properties.Name) | Sort-Object) -join ',' }
function ParseDt([string]$s) { [datetime]::ParseExact($s, 'dd/MM/yyyy HH:mm', $null) }
$iso = '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$'

$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3
$today = (Get-Date).Date
function Iso([datetime]$d) { $d.ToString('yyyy-MM-ddTHH:mm:ss') }

# --- GetMissionsSummary --------------------------------------------------------
$r = Invoke-Api -Path '/api/Mission/GetMissionsSummary' -Token $RG
Check 'GetMissionsSummary (Regular)' '200/4' "$($r.Status)/$(@($r.Body).Count)"
$m0 = $r.Body[0]
Check 'MissionSummaryDto keys' 'actualHours,assignmentCount,commandersRequired,description,duration,firstInstanceFrom,fromTime,futureInstanceCount,id,instanceCount,isSpecial,lastInstanceTo,name,positions,requiredInstances,requiredRestAfter,soldiersRequired,toTime' (Keys $m0)
Check 'MissionPositionDto keys' 'count,id,position' (Keys $m0.positions[0])
$full = (Invoke-Api -Path '/api/Mission/GetMissions' -Token $AD).Body
$mismatch = @()
foreach ($s in $r.Body) {
    $fm = $full | Where-Object { $_.id -eq $s.id }
    $instances = @($fm.missionInstances)
    $assignCount = ($instances | ForEach-Object { @($_.soldierMissions).Count } | Measure-Object -Sum).Sum
    $future = @($instances | Where-Object { (ParseDt $_.fromTime) -ge (Get-Date) }).Count
    $first = ($instances | ForEach-Object { ParseDt $_.fromTime } | Measure-Object -Minimum).Minimum
    if ($s.name -ne $fm.name -or $s.instanceCount -ne $instances.Count -or $s.assignmentCount -ne $assignCount -or [math]::Abs($s.futureInstanceCount - $future) -gt 1 -or $s.firstInstanceFrom -ne (Iso $first) -or @($s.positions).Count -ne @($fm.positions).Count) { $mismatch += $s.id }
}
Check 'Summary counts/names match GetMissions for every mission' '' ($mismatch -join ',')
$special = $r.Body | Where-Object { $_.isSpecial }
Check 'Special mission: fromTime/toTime null (N/A placeholder hidden)' 'True/True' "$($null -eq $special.fromTime)/$($null -eq $special.toTime)"
Check 'Summary ISO datetimes' 'True' "$($m0.firstInstanceFrom -match $iso -and $m0.lastInstanceTo -match $iso)"

# --- GetSoldiersLite / GetSoldierById -------------------------------------------
$r = Invoke-Api -Path '/api/Soldiers/GetSoldiersLite' -Token $RG
Check 'GetSoldiersLite (Regular)' '200/16' "$($r.Status)/$(@($r.Body).Count)"
Check 'SoldierLiteDto keys (no phone/PN)' 'active,company,id,name,platoon,positions' (Keys $r.Body[0])
Check 'Positions are numbers' 'True' "$($r.Body[0].positions[0] -is [long] -or $r.Body[0].positions[0] -is [int])"
Check 'GetSoldiersLite includeInactive' 200 (Invoke-Api -Path '/api/Soldiers/GetSoldiersLite?includeInactive=true' -Token $RG).Status
$r = Invoke-Api -Path '/api/Soldiers/GetSoldierById?soldierId=3' -Token $RG
Check 'GetSoldierById self (Regular)' '200/True/True' "$($r.Status)/$([bool]$r.Body.phone)/$($r.Body.hasUserAccount)"
Check 'SoldierDetailsDto keys' 'active,company,hasUserAccount,id,name,personalNumber,phone,platoon,positions' (Keys $r.Body)
Check 'GetSoldierById other (Regular) -> 403' 403 (Invoke-Api -Path '/api/Soldiers/GetSoldierById?soldierId=4' -Token $RG).Status
$r = Invoke-Api -Path '/api/Soldiers/GetSoldierById?soldierId=4' -Token $AD
Check 'GetSoldierById other (Admin)' '200/False' "$($r.Status)/$($r.Body.hasUserAccount)"
Check 'GetSoldierById unknown (Admin) -> 404' 404 (Invoke-Api -Path '/api/Soldiers/GetSoldierById?soldierId=9999' -Token $AD).Status

# --- Schedule/GetAssignments ----------------------------------------------------
$from = $today.AddDays(-1); $to = $today.AddDays(2)
$r = Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $RG -Body @{ from = (Iso $from); to = (Iso $to) }
Check 'GetAssignments (Regular)' 200 $r.Status
$rows = @($r.Body)
$expected = @($full | ForEach-Object { $m = $_; $_.missionInstances | Where-Object { (ParseDt $_.fromTime) -lt $to -and (ParseDt $_.toTime) -gt $from } }).Count
Check 'Instance count equals overlap filter over GetMissions' $expected $rows.Count
Check 'ScheduleInstanceDto keys' 'assignedCount,assignments,from,instanceId,isFilled,missingPositions,missionId,missionName,requiredCount,to' (Keys $rows[0])
$withAssign = $rows | Where-Object { @($_.assignments).Count -gt 0 } | Select-Object -First 1
Check 'Assignment keys' 'missionPositionId,position,soldierId,soldierMissionId,soldierName' (Keys $withAssign.assignments[0])
$withMissing = $rows | Where-Object { @($_.missingPositions).Count -gt 0 } | Select-Object -First 1
Check 'MissingPosition keys' 'missing,missionPositionId,position' (Keys $withMissing.missingPositions[0])
Check 'Sorted by from' 'True' "$((($rows | ForEach-Object { $_.from }) -join '|') -eq ((($rows | ForEach-Object { $_.from }) | Sort-Object) -join '|'))"
Check 'ISO from/to' 'True' "$(@($rows | Where-Object { $_.from -notmatch $iso -or $_.to -notmatch $iso }).Count -eq 0)"
$bad = @($rows | Where-Object { $_.assignedCount -ne @($_.assignments).Count -or $_.isFilled -ne ($_.assignedCount -ge $_.requiredCount) -or ($_.requiredCount - (@($_.missingPositions | ForEach-Object { $_.missing }) | Measure-Object -Sum).Sum) -gt $_.assignedCount })
Check 'assignedCount / isFilled / missing are consistent' 0 $bad.Count
$crossCheck = @()
foreach ($row in $rows) {
    $fi = $full | ForEach-Object { $_.missionInstances } | Where-Object { $_.id -eq $row.instanceId }
    $a = (@($row.assignments | ForEach-Object { $_.soldierId }) | Sort-Object) -join ','
    $b = (@($fi.soldierMissions | ForEach-Object { $_.soldier.id }) | Sort-Object) -join ','
    if ($a -ne $b) { $crossCheck += $row.instanceId }
}
Check 'Assigned soldiers match GetMissions for every instance' '' ($crossCheck -join ',')

$guard = $full | Where-Object { $_.name -eq 'שמירה בש.ג' }
$r = Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $RG -Body @{ from = (Iso $from); to = (Iso $to); missionIds = @($guard.id) }
Check 'missionIds filter' 'True' "$(@($r.Body).Count -gt 0 -and @($r.Body | Where-Object { $_.missionId -ne $guard.id }).Count -eq 0)"
$r = Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $RG -Body @{ from = (Iso $from); to = (Iso $to); soldierIds = @(5) }
$sRows = @($r.Body)
Check 'soldierIds filter: every instance contains soldier 5' 'True' "$($sRows.Count -gt 0 -and @($sRows | Where-Object { -not (@($_.assignments.soldierId) -contains 5) }).Count -eq 0)"
Check 'soldierIds filter: instances keep all their assignments' 'True' "$(@($sRows | Where-Object { @($_.assignments).Count -gt 1 }).Count -gt 0)"
$r = Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $RG -Body @{ from = (Iso $from); to = (Iso $to); onlyUnfilled = $true }
Check 'onlyUnfilled' 'True' "$(@($r.Body).Count -gt 0 -and @($r.Body | Where-Object { $_.isFilled }).Count -eq 0)"
Check 'Range > 62 days -> 400' 400 (Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $RG -Body @{ from = (Iso $today); to = (Iso $today.AddDays(63)) }).Status
Check 'Range of exactly 62 days -> 200' 200 (Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $RG -Body @{ from = (Iso $today); to = (Iso $today.AddDays(62)) }).Status
$r = Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $RG -Body @{ from = (Iso $today); to = (Iso $today) }
Check 'to <= from -> 400 with message' '400/True' "$($r.Status)/$([bool]$r.Body.message)"
Check 'GetAssignments without token -> 401' 401 (Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Body @{ from = (Iso $from); to = (Iso $to) }).Status

# --- Schedule/ValidateAssignments ---------------------------------------------
# Target: tomorrow's 16:00-20:00 guard slot (unassigned in the seed) - neighbours: guard slots assigned until tomorrow 12:00.
$target = @($guard.missionInstances | Where-Object { (ParseDt $_.fromTime) -eq $today.AddDays(1).AddHours(16) })[0]
$assignedTomorrowMorning = @($guard.missionInstances | Where-Object { (ParseDt $_.fromTime) -eq $today.AddDays(1).AddHours(8) })[0].soldierMissions.soldier.id
Check 'ValidateAssignments (Regular) -> 403' 403 (Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $RG -Body @{ missionInstanceId = $target.id; soldierIds = @(5) }).Status
$allIds = 1..16 + @(9999)
$r = Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $target.id; soldierIds = $allIds }
Check 'ValidateAssignments (Admin)' '200/17' "$($r.Status)/$(@($r.Body).Count)"
$v = @{}; foreach ($x in $r.Body) { $v[[int]$x.soldierId] = $x }
Check 'AssignmentValidationDto keys' 'isValid,overlap,positionFit,requiredRestAfterHours,requiredRestBeforeHours,restAfterHours,restBeforeHours,soldierId,soldierName,suggestedMissionPositionId,suggestedPosition,vacation,violations,warnings' (Keys $v[5])
Check 'Unknown soldier -> NOT_FOUND' 'NOT_FOUND' ($v[9999].violations -join ',')
Check 'Pending vacation (soldier 12) -> ON_VACATION/Pending' 'True/Pending' "$($v[12].violations -contains 'ON_VACATION')/$($v[12].vacation.status)"
Check 'Denied vacation (soldier 14) ignored' 'False' "$($v[14].violations -contains 'ON_VACATION')"
Check 'Commander-only soldiers do not fit a Simple slot' 'none/True' "$($v[1].positionFit)/$($v[1].violations -contains 'POSITION_MISMATCH')"
Check 'Simple soldier fits exactly with suggested slot' "exact/$($guard.positions[0].id)" "$($v[16].positionFit)/$($v[16].suggestedMissionPositionId)"
$restCase = $assignedTomorrowMorning | Select-Object -First 1
Check "Soldier $restCase (guard 08-12 tomorrow) -> REST_BEFORE 4h < 8h" 'True/4/8' "$($v[[int]$restCase].violations -contains 'REST_BEFORE')/$($v[[int]$restCase].restBeforeHours)/$($v[[int]$restCase].requiredRestBeforeHours)"
$valid = @($r.Body | Where-Object { $_.isValid }).Count
Check 'At least one valid soldier and isValid == no violations' 'True/0' "$($valid -gt 0)/$(@($r.Body | Where-Object { $_.isValid -ne (@($_.violations).Count -eq 0) }).Count)"

# Overlap: a soldier assigned to the CURRENT guard slot validated for an overlapping patrol slot.
$patrol = $full | Where-Object { $_.name -eq 'סיור' }
$pTomorrow = @($patrol.missionInstances | Where-Object { (ParseDt $_.fromTime) -eq $today.AddDays(1).AddHours(6) })[0]
$gSlot = @($guard.missionInstances | Where-Object { (ParseDt $_.fromTime) -eq $today.AddDays(1).AddHours(8) })[0]
$busy = [int]@($gSlot.soldierMissions.soldier.id)[0]
$r = Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $pTomorrow.id; soldierIds = @($busy) }
Check "Overlap with tomorrow's guard slot -> OVERLAP + details" "True/$($gSlot.id)/True" "$($r.Body[0].violations -contains 'OVERLAP')/$($r.Body[0].overlap.instanceId)/$($r.Body[0].overlap.from -match $iso)"
$r = Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $pTomorrow.id; soldierIds = @($busy); excludeInstanceIds = @($gSlot.id) }
Check 'excludeInstanceIds removes the overlap' 'False' "$($r.Body[0].violations -contains 'OVERLAP')"
# Already assigned + ignoreSoldierId (replacement in the same slot).
$r = Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $gSlot.id; soldierIds = @($busy) }
Check 'Already assigned soldier -> ALREADY_ASSIGNED + INSTANCE_FULL' 'True/True' "$($r.Body[0].violations -contains 'ALREADY_ASSIGNED')/$($r.Body[0].violations -contains 'INSTANCE_FULL')"
$r = Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $gSlot.id; soldierIds = @(16); ignoreSoldierId = $busy }
Check 'ignoreSoldierId frees the slot (no INSTANCE_FULL)' "False/$($guard.positions[0].id)" "$($r.Body[0].violations -contains 'INSTANCE_FULL')/$($r.Body[0].suggestedMissionPositionId)"
# Requested slot of another mission -> 400; unknown instance -> 404; too many soldiers -> 400; empty -> []
Check 'Foreign missionPositionId -> 400' 400 (Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $target.id; soldierIds = @(5); missionPositionId = $patrol.positions[0].id }).Status
Check 'Unknown instance -> 404' 404 (Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = 999999; soldierIds = @(5) }).Status
Check 'More than 1000 soldiers -> 400' 400 (Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $target.id; soldierIds = @(1..1001) }).Status
$r = Invoke-Api -Path '/api/Schedule/ValidateAssignments' -Method POST -Token $AD -Body @{ missionInstanceId = $target.id; soldierIds = @() }
Check 'Empty soldier list -> 200 []' '200/0' "$($r.Status)/$(@($r.Body).Count)"

# --- Existing behaviour unchanged ----------------------------------------------
Check 'GetMissions still works' '200/4' "$((Invoke-Api -Path '/api/Mission/GetMissions' -Token $AD).Status)/$($full.Count)"
Check 'GetMissionInstancesInRange still works' 200 (Invoke-Api -Path '/api/Mission/GetMissionInstancesInRange' -Method POST -Token $AD -Body @{ from = (Iso $today); to = (Iso $today.AddDays(1)); fullDay = $true; unassignedOnly = $false }).Status
Check 'GetAvailableSoldiers still works' 200 (Invoke-Api -Path '/api/Mission/GetAvailableSoldiers' -Method POST -Token $AD -Body @{ missionInstanceId = $target.id }).Status

$results | Format-Table -AutoSize | Out-String -Width 250
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
