# Seeds a LOCAL dev database (SQL LocalDB "ShabzakAgentDev") with SYNTHETIC data through the API.
# Never run this against a real/production API.
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'dev.ps1')

$ErrorActionPreference = 'Stop'
$t = New-DevJwt -UserId 999999 -Role SuperAdmin

function Assert-Ok($r, $what) { if ($r.Status -ne 200) { throw "$what failed: HTTP $($r.Status) $($r.Body | ConvertTo-Json -Depth 3 -Compress)" } }

$existing = Invoke-Api -Path '/api/Soldiers/GetSoldiers?reloadCache=true' -Token $t
Assert-Ok $existing 'GetSoldiers'
if (@($existing.Body).Count -gt 0) { Write-Host "DB already has $(@($existing.Body).Count) soldiers - skipping seed."; return }

# --- Soldiers (synthetic) ---------------------------------------------------
$soldiers = @(
    @('ישראל ישראלי', 'מפקדה', @(17)), @('משה כהן', '1', @(15)), @('דוד לוי', '1', @(14)), @('יוסי מזרחי', '1', @(13)),
    @('אבי פרץ', '1', @(0, 3)), @('רון ביטון', '1', @(0, 4)), @('עמית אברהם', '1', @(0, 1)), @('נועם פרידמן', '1', @(0)),
    @('איתי שפירא', '2', @(13)), @('אורי גולן', '2', @(0, 6)), @('יונתן דהן', '2', @(0, 3)), @('תומר אזולאי', '2', @(0)),
    @('עידו מלכה', '2', @(0, 2)), @('גיא חדד', '2', @(0)), @('ליאור עמר', 'מפקדה', @(5)), @('שי בן דוד', 'מפקדה', @(0, 10))
)
$i = 0
foreach ($s in $soldiers) {
    $i++
    $body = @{ id = 0; name = $s[0]; personalNumber = ('90000{0:D2}' -f $i); phone = ('05000000{0:D2}' -f $i); platoon = $s[1]; company = 'א'; active = $true; positions = $s[2]; vacations = @(); missions = @() }
    Assert-Ok (Invoke-Api -Path '/api/Soldiers/AddSoldier' -Method POST -Token $t -Body $body) "AddSoldier $i"
}
Start-Sleep -Seconds 2
$sol = (Invoke-Api -Path '/api/Soldiers/GetSoldiers?reloadCache=true' -Token $t).Body | Sort-Object id
$S = @{}; $k = 0; foreach ($x in $sol) { $k++; $S[$k] = $x.id }
Write-Host "Soldiers: $($sol.Count)"

# --- Missions --------------------------------------------------------------
$today = (Get-Date).Date
function Iso([datetime]$d) { $d.ToString('yyyy-MM-ddTHH:mm:ss') }
function Slots([datetime]$from, [datetime]$to, [int]$hours) { $list = @(); $c = $from; while ($c -lt $to) { $e = $c.AddHours($hours); if ($e -gt $to) { $e = $to }; $list += @{ id = 0; fromTime = (Iso $c); toTime = (Iso $e); isFilled = $false; soldierMissions = @() }; $c = $e }; $list }

$guardInstances = Slots $today.AddDays(-2) $today.AddDays(5) 4
$patrolInstances = @(); $standbyInstances = @()
for ($d = -2; $d -lt 5; $d++) {
    $day = $today.AddDays($d)
    $patrolInstances += Slots $day.AddHours(6) $day.AddHours(22) 8
    $standbyInstances += Slots $day.AddHours(8) $day.AddHours(20) 12
}
$missions = @(
    @{ id = 0; name = 'שמירה בש.ג'; description = 'שמירה בשער הראשי'; soldiersRequired = 2; commandersRequired = 0; duration = 4; fromTime = '00:00'; toTime = '00:00'; isSpecial = $false; requiredRestAfter = 8; positions = @(@{ id = 0; missionId = 0; position = 0; count = 2 }); missionInstances = $guardInstances },
    @{ id = 0; name = 'סיור'; description = 'סיור גזרה'; soldiersRequired = 3; commandersRequired = 1; duration = 8; fromTime = '06:00'; toTime = '22:00'; isSpecial = $false; requiredRestAfter = 12; positions = @(@{ id = 0; missionId = 0; position = 13; count = 1 }, @{ id = 0; missionId = 0; position = 0; count = 2 }, @{ id = 0; missionId = 0; position = 3; count = 1 }); missionInstances = $patrolInstances },
    @{ id = 0; name = 'כוננות'; description = 'כיתת כוננות'; soldiersRequired = 3; commandersRequired = 0; duration = 12; fromTime = '08:00'; toTime = '20:00'; isSpecial = $false; actualHours = 4; positions = @(@{ id = 0; missionId = 0; position = 0; count = 3 }); missionInstances = $standbyInstances },
    @{ id = 0; name = 'תדריך מיוחד'; description = 'תדריך מפקדים'; soldiersRequired = 0; commandersRequired = 1; duration = 2; isSpecial = $true; positions = @(@{ id = 0; missionId = 0; position = 15; count = 1 }); missionInstances = @(@{ id = 0; fromTime = (Iso $today.AddDays(1).AddHours(10)); toTime = (Iso $today.AddDays(1).AddHours(12)); isFilled = $false; soldierMissions = @() }) }
)
foreach ($m in $missions) { Assert-Ok (Invoke-Api -Path '/api/Mission/AddMission' -Method POST -Token $t -Body $m) "AddMission $($m.name)" }
Start-Sleep -Seconds 2
$ms = (Invoke-Api -Path '/api/Mission/GetMissions' -Token $t).Body
Write-Host "Missions: $($ms.Count); instances: $(($ms | ForEach-Object { $_.missionInstances.Count } | Measure-Object -Sum).Sum)"

function ParseDt([string]$s) { [datetime]::ParseExact($s, 'dd/MM/yyyy HH:mm', $null) }
function PosId($mission, [int]$pos) { ($mission.positions | Where-Object { $_.position -eq $pos } | Select-Object -First 1).id }
function Assign($mission, $instance, [hashtable]$soldierToPosition) {
    $body = @{ missionId = $mission.id; missionInstanceId = $instance.id; soldiers = @($soldierToPosition.GetEnumerator() | ForEach-Object { @{ soldierId = [int]$_.Key; missionPositionId = [int]$_.Value } }) }
    Assert-Ok (Invoke-Api -Path '/api/Mission/AssignSoldiers' -Method POST -Token $t -Body $body) "AssignSoldiers $($instance.id)"
}

$guard = $ms | Where-Object { $_.name -eq 'שמירה בש.ג' }
$patrol = $ms | Where-Object { $_.name -eq 'סיור' }
$standby = $ms | Where-Object { $_.name -eq 'כוננות' }

# Guard: round-robin pairs until tomorrow 12:00 (later slots stay empty).
$pool = @(5, 6, 7, 8, 10, 12, 13, 14, 16, 11) | ForEach-Object { $S[$_] }
$gSimple = PosId $guard 0
$n = 0
foreach ($gi in ($guard.missionInstances | Sort-Object { ParseDt $_.fromTime })) {
    if ((ParseDt $gi.fromTime) -ge $today.AddDays(1).AddHours(12)) { break }
    $a = $pool[(2 * $n) % $pool.Count]; $b = $pool[(2 * $n + 1) % $pool.Count]; $n++
    Assign $guard $gi @{ ($a) = $gSimple; ($b) = $gSimple }
}
# Patrol: fully staffed until today 14:00, tomorrow morning only the commander.
$pCmd = PosId $patrol 13; $pSimple = PosId $patrol 0; $pMedic = PosId $patrol 3
$p = 0
foreach ($pi in ($patrol.missionInstances | Sort-Object { ParseDt $_.fromTime })) {
    $from = ParseDt $pi.fromTime
    if ($from -lt $today.AddHours(14)) {
        $cmd = if ($p % 2 -eq 0) { $S[4] } else { $S[9] }; $med = if ($p % 2 -eq 0) { $S[5] } else { $S[11] }
        $s1 = if ($p % 2 -eq 0) { $S[8] } else { $S[12] }; $s2 = if ($p % 2 -eq 0) { $S[7] } else { $S[14] }
        Assign $patrol $pi @{ ($cmd) = $pCmd; ($s1) = $pSimple; ($s2) = $pSimple; ($med) = $pMedic }; $p++
    }
    elseif ($from -eq $today.AddDays(1).AddHours(6)) { Assign $patrol $pi @{ ($S[4]) = $pCmd } }
}
# Standby today: two of three slots.
$sSimple = PosId $standby 0
$st = $standby.missionInstances | Where-Object { (ParseDt $_.fromTime) -eq $today.AddHours(8) }
if ($st) { Assign $standby $st @{ ($S[13]) = $sSimple; ($S[16]) = $sSimple } }

# --- Vacations ------------------------------------------------------------
$v1 = Invoke-Api -Path '/api/Soldiers/RequestVacation' -Method POST -Token $t -Body @{ soldierId = $S[12]; from = (Iso $today.AddDays(1)); to = (Iso $today.AddDays(3)) }; Assert-Ok $v1 'RequestVacation 1'
$v2 = Invoke-Api -Path '/api/Soldiers/RequestVacation' -Method POST -Token $t -Body @{ soldierId = $S[13]; from = (Iso $today.AddDays(3)); to = (Iso $today.AddDays(5)) }; Assert-Ok $v2 'RequestVacation 2'
$v3 = Invoke-Api -Path '/api/Soldiers/RequestVacation' -Method POST -Token $t -Body @{ soldierId = $S[14]; from = (Iso $today.AddDays(1)); to = (Iso $today.AddDays(2)) }; Assert-Ok $v3 'RequestVacation 3'
Assert-Ok (Invoke-Api -Path '/api/Soldiers/RespondToVacationRequest' -Method POST -Token $t -Body @{ vacationId = $v2.Body.id; response = 1 }) 'Approve vacation'
Assert-Ok (Invoke-Api -Path '/api/Soldiers/RespondToVacationRequest' -Method POST -Token $t -Body @{ vacationId = $v3.Body.id; response = 2 }) 'Deny vacation'

# --- Users (Regular; roles are set later via User/UpdateUser) --------------
$u = Invoke-Api -Path '/api/User/CreateUsersForSoldiers' -Method POST -Token $t -Body @($S[1], $S[2], $S[3], $S[5], $S[8])
Assert-Ok $u 'CreateUsersForSoldiers'
Write-Host "Users created: $($u.Body.value)"
Write-Host 'Seed complete.'
