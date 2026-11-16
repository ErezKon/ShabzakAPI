# Phase 1 acceptance checks against the LOCAL synthetic dev DB (users 1..5 = soldiers 1,2,3,5,8).
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'dev.ps1')
$results = [System.Collections.Generic.List[object]]::new()
function Check([string]$name, $expected, $actual) {
    $ok = "$expected" -eq "$actual"
    $results.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Check = $name; Expected = "$expected"; Actual = "$actual" })
}
function Sha512Hex([string]$s) { -join ([System.Security.Cryptography.SHA512]::HashData([Text.Encoding]::UTF8.GetBytes($s)) | ForEach-Object { $_.ToString('x2') }) }

$SA = New-DevJwt -UserId 1 -Role SuperAdmin -SoldierId 1
# Setup via the new API: user 2 -> Admin + activated, user 3 -> activated (Regular).
$r = Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 2; role = 1; activated = $true }
Check 'SA promotes user 2 to Admin' '200/True/Admin' "$($r.Status)/$($r.Body.success)/$($r.Body.value.roleName)"
$r = Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 3; activated = $true }
Check 'SA activates user 3' '200/True/True' "$($r.Status)/$($r.Body.success)/$($r.Body.value.activated)"
$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3

# --- Me -------------------------------------------------------------------
$r = Invoke-Api -Path '/api/User/Me' -Token $SA
Check 'Me (SA) role/roleName/soldierId' '200/2/SuperAdmin/1' "$($r.Status)/$($r.Body.role)/$($r.Body.roleName)/$($r.Body.soldierId)"
Check 'Me has no secret fields' '' (@($r.Body.PSObject.Properties.Name | Where-Object { $_ -in 'name', 'password', 'salt', 'token' }) -join ',')
$r = Invoke-Api -Path '/api/User/Me' -Token $RG
Check 'Me (Regular)' '200/0/Regular/True' "$($r.Status)/$($r.Body.role)/$($r.Body.roleName)/$([bool]$r.Body.soldierName)"
Check 'Me without token' 401 (Invoke-Api -Path '/api/User/Me').Status

# --- Regular: admin endpoints -> 403 ----------------------------------------
$adminCalls = @(
    @('GET', '/api/User/GetUsers'), @('POST', '/api/User/UpdateUser'), @('POST', '/api/User/DeleteUser?userId=4'), @('POST', '/api/User/ResetPasswordToDefault?userId=4'),
    @('POST', '/api/User/CreateUsersForSoldiers'), @('GET', '/api/Soldiers/ReloadCache'), @('GET', '/api/Soldiers/LoadCSV'), @('POST', '/api/Soldiers/AddSoldier'), @('POST', '/api/Soldiers/UpdateSoldier'),
    @('POST', '/api/Soldiers/DeleteSoldier?soldierId=4'), @('POST', '/api/Soldiers/RespondToVacationRequest'), @('POST', '/api/Mission/AddMission'), @('POST', '/api/Mission/UpdateMission'),
    @('POST', '/api/Mission/DeleteMission?missionId=1'), @('POST', '/api/Mission/AssignSoldiers'), @('POST', '/api/Mission/UnassignSoldier?missionId=1&missionInstanceId=1&soldierId=1'),
    @('POST', '/api/Mission/GetAvailableSoldiers'), @('POST', '/api/Mission/AssignSoldiersToMissionInstance'), @('POST', '/api/Mission/AutoAssign'), @('POST', '/api/Mission/AcceptAssignCandidate?candidateId=x'),
    @('GET', '/api/Mission/GetAllCandidates'), @('POST', '/api/Mission/GetCandidate?guid=x'), @('POST', '/api/Mission/RemoveSoldierFromMissionInstance?soldierId=1&missionInstanceId=1'),
    @('POST', '/api/Mission/StartInteractiveAutoAssign'), @('POST', '/api/Mission/ContinueInteractiveAutoAssign'), @('POST', '/api/Mission/CancelInteractiveAutoAssign?sessionId=x'),
    @('POST', '/api/Mission/GetReplacementCandidates'), @('POST', '/api/Mission/ReplaceSoldierInMissionInstance'),
    @('POST', '/api/Metadata/GetAssignmentsPerSoldiers'), @('POST', '/api/Metadata/GetHoursPerSoldiers'), @('POST', '/api/Metadata/GetAssignmentsBreakdownPerSoldiers')
)
$forbidden = 0; $notForbidden = @()
foreach ($c in $adminCalls) {
    $body = if ($c[0] -eq 'POST') { @{} } else { $null }
    $s = (Invoke-Api -Path $c[1] -Method $c[0] -Token $RG -Body $body).Status
    if ($s -eq 403) { $forbidden++ } else { $notForbidden += "$($c[1])=$s" }
}
Check "Regular gets 403 on all $($adminCalls.Count) admin endpoints" $adminCalls.Count $forbidden
if ($notForbidden) { Check 'Regular non-403 admin endpoints' '' ($notForbidden -join '; ') }

# --- Regular: reads -> 200 ----------------------------------------------------
$today = (Get-Date).Date
function AssignedSoldiers($missions) { @($missions | ForEach-Object { $_.missionInstances } | ForEach-Object { $_.soldierMissions } | ForEach-Object { $_.soldier } | Where-Object { $_ }) }
$r = Invoke-Api -Path '/api/Mission/GetMissions' -Token $RG
Check 'Regular GetMissions' 200 $r.Status
$assignedForRegular = AssignedSoldiers $r.Body
Check 'Regular GetMissions: assigned soldiers listed, none with phone/PN' 'True/0' "$($assignedForRegular.Count -gt 0)/$(@($assignedForRegular | Where-Object { $_.phone -or $_.personalNumber }).Count)"
$assignedForAdmin = AssignedSoldiers (Invoke-Api -Path '/api/Mission/GetMissions' -Token $AD).Body
Check 'Admin GetMissions still has phones/PN' "$($assignedForAdmin.Count)/$($assignedForAdmin.Count)" "$(@($assignedForAdmin | Where-Object { $_.phone }).Count)/$(@($assignedForAdmin | Where-Object { $_.personalNumber }).Count)"
Check 'Regular GetMissionInstances' 200 (Invoke-Api -Path '/api/Mission/GetMissionInstances?missionId=1' -Method POST -Token $RG).Status
Check 'Regular GetMissionInstancesInRange' 200 (Invoke-Api -Path '/api/Mission/GetMissionInstancesInRange' -Method POST -Token $RG -Body @{ from = $today.ToString('s'); to = $today.AddDays(1).ToString('s'); fullDay = $true; unassignedOnly = $false }).Status
$r = Invoke-Api -Path '/api/Soldiers/GetSoldiers?reloadCache=true' -Token $RG
Check 'Regular GetSoldiers' 200 $r.Status
Check 'Regular GetSoldiers: soldiers with phone/PN' 0 @($r.Body | Where-Object { $_.phone -or $_.personalNumber }).Count
Check 'Regular GetSoldiers: other soldiers with vacations' 0 @($r.Body | Where-Object { $_.id -ne 3 -and @($_.vacations).Count -gt 0 }).Count
$ra = Invoke-Api -Path '/api/Soldiers/GetSoldiers' -Token $AD
Check 'Admin GetSoldiers still has phones' $ra.Body.Count @($ra.Body | Where-Object { $_.phone }).Count
Check 'Admin GetSoldiers still has other soldiers vacations' $true (@($ra.Body | Where-Object { $_.id -ne 3 -and @($_.vacations).Count -gt 0 }).Count -gt 0)

# --- Self-scope -------------------------------------------------------------------
Check 'Regular GetSummary own' 200 (Invoke-Api -Path '/api/Soldiers/GetSummary?soldierId=3' -Method POST -Token $RG).Status
Check 'Regular GetSummary other' 403 (Invoke-Api -Path '/api/Soldiers/GetSummary?soldierId=4' -Method POST -Token $RG).Status
Check 'Admin GetSummary other' 200 (Invoke-Api -Path '/api/Soldiers/GetSummary?soldierId=4' -Method POST -Token $AD).Status
$r = Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $RG -Body @{ soldierId = 12 }
$own = (Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $AD -Body @{ soldierId = 3 }).Body
$other = Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $AD -Body @{ soldierId = 12 }
$regularIds = (@($r.Body | ForEach-Object { $_.id }) | Sort-Object) -join ','
Check 'Regular GetVacations(soldier 12) returns exactly own (soldier 3) rows' "200/$((@($own | ForEach-Object { $_.id }) | Sort-Object) -join ',')" "$($r.Status)/$regularIds"
Check 'Regular GetVacations(soldier 12) shares no rows with soldier 12' 0 @($r.Body | Where-Object { $_.id -in @($other.Body | ForEach-Object { $_.id }) }).Count
Check 'Admin GetVacations(12) returns rows' '200/True' "$($other.Status)/$(@($other.Body).Count -gt 0)"
Check 'Regular RequestVacation other' 403 (Invoke-Api -Path '/api/Soldiers/RequestVacation' -Method POST -Token $RG -Body @{ soldierId = 4; from = $today.AddDays(10).ToString('s'); to = $today.AddDays(11).ToString('s') }).Status
Check 'Regular RequestVacation own' 200 (Invoke-Api -Path '/api/Soldiers/RequestVacation' -Method POST -Token $RG -Body @{ soldierId = 3; from = $today.AddDays(10).ToString('s'); to = $today.AddDays(11).ToString('s') }).Status
Check 'Regular ResetPassword other' 403 (Invoke-Api -Path '/api/User/ResetPassword' -Method POST -Token $RG -Body @{ username = (Sha512Hex '9000002'); password = (Sha512Hex 'x'); encrypted = $true }).Status
Check 'Regular ResetPassword unknown user' 403 (Invoke-Api -Path '/api/User/ResetPassword' -Method POST -Token $RG -Body @{ username = (Sha512Hex 'nope'); password = (Sha512Hex 'x'); encrypted = $true }).Status
$r = Invoke-Api -Path '/api/User/ResetPassword' -Method POST -Token $RG -Body @{ username = (Sha512Hex '9000003'); password = (Sha512Hex 'NewPass#1'); encrypted = $true }
Check 'Regular ResetPassword own' '200/True' "$($r.Status)/$($r.Body.success)"
$r = Invoke-Api -Path '/api/User/Login' -Method POST -Body @{ userName = (Sha512Hex '9000003'); password = (Sha512Hex 'NewPass#1') }
Check 'Login with the new own password' 'True' "$($r.Body.success)"

# --- Admin account management ------------------------------------------------------
$r = Invoke-Api -Path '/api/User/GetUsers' -Token $AD
Check 'Admin GetUsers' '200/5' "$($r.Status)/$(@($r.Body).Count)"
Check 'GetUsers has no secret fields' '' (@($r.Body | ForEach-Object { $_.PSObject.Properties.Name } | Where-Object { $_ -in 'name', 'password', 'salt' } | Sort-Object -Unique) -join ',')
$r = Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $AD -Body @{ userId = 4; role = 1 }
Check 'Admin role change -> 403' '403/False' "$($r.Status)/$($r.Body.success)"
Check 'Admin disables SuperAdmin -> 403' 403 (Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $AD -Body @{ userId = 1; enabled = $false }).Status
Check 'Admin deletes SuperAdmin -> 403' 403 (Invoke-Api -Path '/api/User/DeleteUser?userId=1' -Method POST -Token $AD).Status
Check 'Admin ResetPasswordToDefault(SuperAdmin) -> 403' 403 (Invoke-Api -Path '/api/User/ResetPasswordToDefault?userId=1' -Method POST -Token $AD).Status
Check 'Admin ResetPassword(SuperAdmin username) -> 403' 403 (Invoke-Api -Path '/api/User/ResetPassword' -Method POST -Token $AD -Body @{ username = (Sha512Hex '9000001'); password = (Sha512Hex 'x'); encrypted = $true }).Status
Check 'Admin self-disable -> 400' 400 (Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $AD -Body @{ userId = 2; enabled = $false }).Status
Check 'Admin self-delete -> 400' 400 (Invoke-Api -Path '/api/User/DeleteUser?userId=2' -Method POST -Token $AD).Status
Check 'UpdateUser unknown user -> 404' 404 (Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $AD -Body @{ userId = 999; enabled = $true }).Status
Check 'UpdateUser nothing to update -> 400' 400 (Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $AD -Body @{ userId = 4 }).Status
$r = Invoke-Api -Path '/api/User/ResetPasswordToDefault?userId=3' -Method POST -Token $AD
Check 'Admin ResetPasswordToDefault(user 3)' '200/True/Password reset to default.' "$($r.Status)/$($r.Body.success)/$($r.Body.value)"
$r = Invoke-Api -Path '/api/User/Login' -Method POST -Body @{ userName = (Sha512Hex '9000003'); password = (Sha512Hex '0500000003') }
Check 'Login with default password after reset' 'True' "$($r.Body.success)"

# --- SuperAdmin ---------------------------------------------------------------------
$r = Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 4; role = 1 }
Check 'SA role change -> 200' '200/Admin' "$($r.Status)/$($r.Body.value.roleName)"
$r = Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 4; role = 0 }
Check 'SA role change back -> 200' '200/Regular' "$($r.Status)/$($r.Body.value.roleName)"
Check 'SA self-demote -> 400' 400 (Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 1; role = 1 }).Status
Check 'SA self-disable -> 400' 400 (Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 1; enabled = $false }).Status
Check 'SA self-delete -> 400' 400 (Invoke-Api -Path '/api/User/DeleteUser?userId=1' -Method POST -Token $SA).Status
Check 'Unknown role value -> 400' 400 (Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 4; role = 7 }).Status

# --- Token revalidation ------------------------------------------------------------
$forged = New-DevJwt -UserId 3 -Role Admin -SoldierId 3
Check 'Stale/forged Admin role claim for a Regular account -> 403 (role refreshed)' 403 (Invoke-Api -Path '/api/User/GetUsers' -Token $forged).Status
Check 'Token for a non-existent user -> 401' 401 (Invoke-Api -Path '/api/User/Me' -Token (New-DevJwt -UserId 424242 -Role SuperAdmin)).Status
$U5 = New-DevJwt -UserId 5 -Role Regular -SoldierId 8
Check 'User 5 before disable' 200 (Invoke-Api -Path '/api/User/Me' -Token $U5).Status
[void](Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $AD -Body @{ userId = 5; enabled = $false })
Check 'Disabled account token -> 401 immediately' 401 (Invoke-Api -Path '/api/User/Me' -Token $U5).Status
[void](Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $AD -Body @{ userId = 5; enabled = $true })
Check 'Re-enabled account token -> 200' 200 (Invoke-Api -Path '/api/User/Me' -Token $U5).Status

# --- CreateUsersForSoldiers idempotency + DeleteUser ---------------------------------
$r = Invoke-Api -Path '/api/User/CreateUsersForSoldiers' -Method POST -Token $AD -Body @(1, 4)
Check 'CreateUsersForSoldiers([1 has account, 4 new])' '200/1' "$($r.Status)/$($r.Body.value)"
$r = Invoke-Api -Path '/api/User/CreateUsersForSoldiers' -Method POST -Token $AD -Body @(4, 4)
Check 'CreateUsersForSoldiers again -> 0 created' '200/0' "$($r.Status)/$($r.Body.value)"
$new = (Invoke-Api -Path '/api/User/GetUsers' -Token $AD).Body | Where-Object { $_.soldierId -eq 4 }
$r = Invoke-Api -Path "/api/User/DeleteUser?userId=$($new.id)" -Method POST -Token $AD
Check 'Admin DeleteUser(new account)' "200/True/$($new.id)" "$($r.Status)/$($r.Body.success)/$($r.Body.value)"
Check 'GetUsers after delete' 5 @((Invoke-Api -Path '/api/User/GetUsers' -Token $AD).Body).Count

# --- Admin regression: admin flows still work ------------------------------------------
Check 'Admin GetAvailableSoldiers' 200 (Invoke-Api -Path '/api/Mission/GetAvailableSoldiers' -Method POST -Token $AD -Body @{ missionInstanceId = 1 }).Status
Check 'Admin Metadata GetHoursPerSoldiers' 200 (Invoke-Api -Path '/api/Metadata/GetHoursPerSoldiers' -Method POST -Token $AD -Body @{ to = $today.AddDays(7).ToString('s'); soldierType = 0 }).Status
Check 'Admin GetAllCandidates' 200 (Invoke-Api -Path '/api/Mission/GetAllCandidates' -Token $AD).Status

$results | Format-Table -AutoSize | Out-String -Width 220
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
