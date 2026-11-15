# Phase 8 acceptance: the soldier and login-account tools, end to end against the LOCAL synthetic dev API.
# Read tools run through the dev CLI's --tool mode; writes run through the chat API (approval cards, approve / reject) with the
# scripted LLM stub (fake-openai.mjs), so the agent, its approvals and every API call are real; only the model's choices are
# scripted (CALL directives). The LLM-only behavior (asking for missing fields) is checked by verify-p7-p8-llm.ps1.
# Prerequisite: the API runs with `start-api-localdb.ps1 -LaunchProfile http`. Users: 1 SuperAdmin, 2 Admin, 3 Regular.
# The suite creates one synthetic soldier (and its account) and deletes both again through the agent (API cleanup on failure).
# It also checks that Soldiers/DeleteSoldier deletes the soldier's login account under the account guard: a synthetic
# SuperAdmin account protects its soldier from an Admin (agent and API) and from its own deletion (API).
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
$finals = [System.Collections.Generic.List[string]]::new()
function Track($r) { if ($r.Final) { $finals.Add($r.Final.message) }; $r }

$apiHealth = try { (Invoke-WebRequest "$script:BaseUrl/health" -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
if ($apiHealth -ne 200) { throw "ShabzakAPI is not reachable at $script:BaseUrl (start it with start-api-localdb.ps1 -LaunchProfile http)" }

$SA = New-DevJwt -UserId 1 -Role SuperAdmin -SoldierId 1 -Minutes 60
$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2 -Minutes 60
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3 -Minutes 60
$PN = '9912345'; $PHONE = '0525550199'; $NAME = 'חייל בדיקה'; $RENAMED = 'חייל בדיקה שני'
function Users { @((Invoke-Api -Path '/api/User/GetUsers' -Token $SA).Body) }
function Lite([bool]$Inactive = $true) { @((Invoke-Api -Path "/api/Soldiers/GetSoldiersLite?includeInactive=$($Inactive.ToString().ToLower())" -Token $AD).Body) }
# AddSoldier / UpdateSoldier / DeleteSoldier reload the API's soldier cache in the background: poll until the change shows.
function Wait-Until([scriptblock]$Condition, [int]$Seconds = 5) {
    for ($i = 0; $i -lt $Seconds * 4; $i++) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 250 }
    $false
}
$leftover = @(Lite | Where-Object { $_.name -in @($NAME, $RENAMED) })
if ($leftover.Count) { throw "A previous run left soldier(s) $(@($leftover.id) -join ', '); delete them first (Soldiers/DeleteSoldier)." }
$soldiersBefore = @(Lite).Count; $usersBefore = @(Users).Count
$newId = $null; $newUserId = $null

# --- Read tools (CLI --tool mode, no LLM) -------------------------------------------------------------------
foreach ($tool in 'list_user_accounts', 'get_soldier_contact_details', 'create_soldier', 'update_user_account') {
    Check "Regular: $tool is not registered" $true ((Invoke-CliTool $RG $tool '{"soldierId":3,"userId":3}').Json.error -match 'not available for role Regular')
}
$list = Invoke-CliTool $AD 'list_user_accounts' '{"limit":100}'
Check 'list_user_accounts: one row per account (= User/GetUsers)' $usersBefore $list.Json.count
Check 'list_user_accounts: whitelisted fields only' 'activated,company,enabled,id,platoon,role,roleHe,soldierId,soldierName' ((@($list.Json.accounts)[0].PSObject.Properties.Name | Sort-Object) -join ',')
Check 'list_user_accounts: no username hash, password or salt' $false (($list.Stdout -match '[0-9a-f]{64,}') -or ($list.Stdout -match '(?i)"(password|salt)"'))
$without = Invoke-CliTool $AD 'list_user_accounts' '{"soldiersWithoutAccount":true,"limit":100}'
$expectedWithout = @(Lite $false | Where-Object { $_.id -notin @(Users | ForEach-Object soldierId) } | ForEach-Object id | Sort-Object)
Check 'list_user_accounts soldiersWithoutAccount = active soldiers without an account (API)' ($expectedWithout -join ',') ((@($without.Json.soldiersWithoutAccount.id) | Sort-Object) -join ',')
$contact = Invoke-CliTool $AD 'get_soldier_contact_details' '{"soldierId":8}'
$details = (Invoke-Api -Path '/api/Soldiers/GetSoldierById?soldierId=8' -Token $AD).Body
Check 'get_soldier_contact_details: phone and personal number of the API' "$($details.phone)/$($details.personalNumber)" "$($contact.Json.contact.phone)/$($contact.Json.contact.personalNumber)"
Check 'get_soldier_contact_details: audit line (tool, soldier ID, ok) without the contact details' $true (($contact.Stderr -match '\[audit\] .*"tool":"get_soldier_contact_details","args":\{"soldierId":8\},"result":"ok"') -and -not $contact.Stderr.Contains($details.phone))
$card = Invoke-CliTool $AD 'create_soldier' (@{ name = $NAME; personalNumber = $PN; phone = '052-555-0199'; platoon = '2'; company = 'א'; positions = @('חובש') } | ConvertTo-Json -Compress)
Check 'CLI --tool on a write tool shows the card only (nothing created)' "הוספת חייל: $NAME/$soldiersBefore" "$($card.Json.approval.title)/$(@(Lite).Count)"

$stack = Start-AgentStack -Name 'p8' -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '200' }
try {
    # --- create_soldier ------------------------------------------------------------------------------------------
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'create_soldier' @{ name = $NAME; personalNumber = $PN; platoon = '2'; company = 'א'; positions = @('חובש') }) } $AD)
    Check 'Missing phone: refused before asking (no card), the model is told what is missing' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'NOT RUN: create_soldier .*phone')"
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'create_soldier' @{ name = $NAME; personalNumber = '9000005'; phone = $PHONE; platoon = '2'; company = 'א'; positions = @('חובש') }) } $AD)
    Check 'Duplicate personal number: refused before asking' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'already has this personal number')"
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'create_soldier' @{ name = $NAME; personalNumber = $PN; phone = '052-555-0199'; platoon = '2'; company = 'א'; positions = @('חובש') }) } $AD)
    $c = Card $r
    Check 'Create: approval card with the normalized input and the added חפ"ש' $true (($c.title -eq "הוספת חייל: $NAME") -and (@($c.details) -contains 'תפקידים: חפ"ש, חובש') -and (@($c.details) -contains "טלפון: $PHONE") -and (@($c.warnings) -contains 'לחייל אין תפקיד פיקודי, ולכן יתווסף לו גם התפקיד חפ"ש.'))
    Check 'Create: nothing written before the approval' $soldiersBefore @(Lite).Count
    $r = Track (Decide $r.ThreadId $AD)
    $null = Wait-Until { @(Lite | Where-Object name -eq $NAME).Count -gt 0 }
    $new = @(Lite | Where-Object name -eq $NAME)
    $newId = $new[0].id
    Check 'Create: approved -> the soldier exists (חפ"ש + חובש, active), refresh soldiers' '1/0,3/True/soldiers' "$($new.Count)/$(@($new[0].positions) -join ',')/$($new[0].active)/$(@($r.Final.refresh) -join ',')"
    $nd = (Invoke-Api -Path "/api/Soldiers/GetSoldierById?soldierId=$newId" -Token $AD).Body
    Check 'Create: phone and personal number stored normalized' "$PHONE/$PN" "$($nd.phone)/$($nd.personalNumber)"
    Check 'Create: the tool result has no contact details' $false ($r.Final.message.Contains($PHONE) -or $r.Final.message.Contains($PN))

    # --- update_soldier ----------------------------------------------------------------------------------------------
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_soldier' @{ soldierId = $newId; name = $RENAMED; addPositions = @('נגב') }) } $AD)
    $c = Card $r
    Check 'Update: the card shows the diff (name, positions)' $true ((@($c.details) -contains "שם: $NAME ← $RENAMED") -and (@($c.details) -contains 'תפקידים: חפ"ש, חובש ← חפ"ש, חובש, נגב'))
    $null = Track (Decide $r.ThreadId $AD)
    $null = Wait-Until { @(Lite | Where-Object id -eq $newId)[0].name -eq $RENAMED }
    $after = @(Lite | Where-Object id -eq $newId)[0]
    Check 'Update: approved -> name and positions changed' "$RENAMED/0,3,4" "$($after.name)/$(@($after.positions) -join ',')"
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_soldier' @{ soldierId = $newId; platoon = '3' }) } $AD)
    $null = Track (Decide $r.ThreadId $AD 'reject' 'לא עכשיו')
    Check 'Update rejected -> the platoon is unchanged' '2' (@(Lite | Where-Object id -eq $newId)[0].platoon)
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_soldier' @{ soldierId = $newId; active = $false }) } $AD)
    Check 'Deactivate: the card shows the status change' $true (@((Card $r).details) -contains 'סטטוס: פעיל ← לא פעיל')
    $null = Track (Decide $r.ThreadId $AD)
    Check 'Deactivate: approved -> inactive (SetSoldierActive), hidden from the active list' 'False/False' "$(@(Lite | Where-Object id -eq $newId)[0].active)/$(@(Lite $false).id -contains $newId)"
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_soldier' @{ soldierId = $newId; active = $true }) } $AD)
    $null = Track (Decide $r.ThreadId $AD)
    Check 'Reactivate: approved -> active again' $true (@(Lite | Where-Object id -eq $newId)[0].active)

    # --- create_user_accounts ------------------------------------------------------------------------------------------
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'create_user_accounts' @{ soldierIds = @($newId, 3) }) } $AD)
    $c = Card $r
    Check 'Accounts: the card lists who gets an account and who is skipped' $true ((@($c.details) -contains "ייווצרו חשבונות ל: $RENAMED (#$newId)") -and (@($c.details) -contains 'כבר יש חשבון (יידלגו): דוד לוי (#3)'))
    $r = Track (Decide $r.ThreadId $AD)
    $acc = @(Users | Where-Object soldierId -eq $newId)
    $newUserId = $acc[0].id
    Check 'Accounts: approved -> one Regular account, not activated, refresh users' '1/Regular/False/users' "$($acc.Count)/$($acc[0].roleName)/$($acc[0].activated)/$(@($r.Final.refresh) -join ',')"

    # --- update_user_account: roles are SuperAdmin-only ------------------------------------------------------------------
    $requests = @(Get-Content $stack.LlmLog -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
    $adminParams = @($requests | Where-Object { $_.tools -contains 'update_user_account' })[0].params.update_user_account
    Check "Admin: update_user_account has no role field" 'userId,enabled,activated' (@($adminParams) -join ',')
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_user_account' @{ userId = $newUserId; role = 'Admin' }) } $AD)
    Check 'Admin role change: the role is dropped and nothing is left to change (refused, no card)' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'Only a SuperAdmin can change roles')"
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_user_account' @{ userId = 1; enabled = $false }) } $AD)
    Check "Admin disabling the SuperAdmin's account: refused" $true ($r.Final.message -match 'NOT RUN: .*Only a SuperAdmin can change, reset or delete a SuperAdmin account')
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_user_account' @{ userId = 2; enabled = $false }) } $AD)
    Check 'Admin disabling their own account: refused' $true ($r.Final.message -match 'cannot disable or deactivate your own account')
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_user_account' @{ userId = $newUserId; activated = $true }) } $AD)
    Check 'Activate: the card shows the change' $true (@((Card $r).details) -contains 'הפעלה: לא מופעל ← מופעל')
    $null = Track (Decide $r.ThreadId $AD)
    Check 'Activate: approved -> activated' $true (@(Users | Where-Object id -eq $newUserId)[0].activated)
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'update_user_account' @{ userId = $newUserId; role = 'Admin' }) } $SA)
    $superParams = @(Get-Content $stack.LlmLog -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.lastUser -like '*"role":"Admin"*' -and $_.params.update_user_account -contains 'role' }).Count
    Check 'SuperAdmin: update_user_account has the role field' $true ($superParams -gt 0)
    Check 'SuperAdmin role change: card (medium risk)' 'תפקיד: משתמש רגיל ← מנהל/medium' "$(@((Card $r).details)[1])/$((Card $r).risk)"
    $null = Track (Decide $r.ThreadId $SA)
    Check 'SuperAdmin role change: approved -> Admin' 'Admin' (@(Users | Where-Object id -eq $newUserId)[0].roleName)

    # --- reset_user_password: only to the default; then the default login works --------------------------------------------
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'reset_user_password' @{ userId = $newUserId }) } $AD)
    Check 'Reset by an Admin of an Admin account: allowed, the card names the default (masked phone)' $true (@((Card $r).details) -contains 'הסיסמה החדשה: מספר הטלפון של החייל, ספרות בלבד (••••••0199)')
    $r = Track (Decide $r.ThreadId $AD)
    $login = Invoke-Api -Path '/api/User/Login' -Method POST -Body @{ userName = (Get-Sha512Hex $PN); password = (Get-Sha512Hex $PHONE) }
    Check 'Reset: login with the personal number and the phone (default password) works' 'True' "$($login.Body.success)"
    Check 'Reset: the result never contains the phone' $false ($r.Final.message.Contains($PHONE))

    # --- delete_user_account, delete_soldier ----------------------------------------------------------------------------------
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'delete_user_account' @{ userId = $newUserId }) } $SA)
    Check 'Delete account: card (Admin account -> high risk)' "מחיקת חשבון משתמש: $RENAMED/high" "$((Card $r).title)/$((Card $r).risk)"
    $null = Track (Decide $r.ThreadId $SA)
    Check 'Delete account: approved -> gone' 0 @(Users | Where-Object soldierId -eq $newId).Count
    if (@(Users | Where-Object soldierId -eq $newId).Count -eq 0) { $newUserId = $null }

    # --- Deleting a soldier deletes their login account too (API cascade, under the account guard) -------------------------------
    # Setup through the API: a new account for the soldier, made SuperAdmin, so the guard refusals can be checked safely.
    $null = Invoke-Api -Path '/api/User/CreateUsersForSoldiers' -Method POST -Token $SA -Body (, @($newId))
    $newUserId = @(Users | Where-Object soldierId -eq $newId)[0].id
    $promote = Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = $newUserId; role = 2 }
    Check 'Setup: the soldier has a SuperAdmin account again' '200/SuperAdmin' "$($promote.Status)/$($promote.Body.value.roleName)"
    function Still { "$(@(Lite | Where-Object id -eq $newId).Count)/$(@(Users | Where-Object id -eq $newUserId).Count)" }
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'delete_soldier' @{ soldierId = $newId }) } $AD)
    Check "Agent: an Admin deleting the soldier of a SuperAdmin account -> refused before asking" 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'NOT RUN: delete_soldier .*also deletes their login account.*Only a SuperAdmin')"
    $r = Invoke-Api -Path "/api/Soldiers/DeleteSoldier?soldierId=$newId" -Method POST -Token $AD
    Check 'API: an Admin deleting that soldier -> 403, nothing deleted' '403/True/1/1' "$($r.Status)/$($r.Body.errorMessage -match 'Only a SuperAdmin')/$(Still)"
    $SELF = New-DevJwt -UserId $newUserId -Role SuperAdmin -SoldierId $newId -Minutes 10
    $r = Invoke-Api -Path "/api/Soldiers/DeleteSoldier?soldierId=$newId" -Method POST -Token $SELF
    Check "API: deleting one's own soldier -> 400 (own account), nothing deleted" '400/True/1/1' "$($r.Status)/$($r.Body.errorMessage -match 'cannot delete your own account')/$(Still)"
    $r = Track (Invoke-Chat '/api/chat/stream' @{ message = (Say 'delete_soldier' @{ soldierId = $newId }) } $SA)
    $c = Card $r
    Check 'Delete soldier (SuperAdmin): high-risk card with the history and the account deleted with it' 'high/True/True' "$($c.risk)/$(@($c.details) -contains 'שיבוצים (כל הזמנים): 0')/$(@($c.details) -contains "חשבון משתמש: #$newUserId (מנהל-על) — יימחק יחד עם החייל")"
    $r = Track (Decide $r.ThreadId $SA)
    $null = Wait-Until { @(Lite | Where-Object id -eq $newId).Count -eq 0 }
    Check 'Delete soldier: approved -> the soldier and their account are gone, refresh missions + soldiers + users' '0/0/missions,soldiers,users' "$(Still)/$((@($r.Final.refresh) | Sort-Object) -join ',')"
    if (@(Lite | Where-Object id -eq $newId).Count -eq 0) { $newId = $null }
    if (@(Users | Where-Object id -eq $newUserId).Count -eq 0) { $newUserId = $null }
}
finally {
    Stop-AgentStack $stack
    if ($newUserId) { $null = Invoke-Api -Path "/api/User/DeleteUser?userId=$newUserId" -Method POST -Token $SA }
    if ($newId) { $null = Invoke-Api -Path "/api/Soldiers/DeleteSoldier?soldierId=$newId" -Method POST -Token $SA }
}
$null = Wait-Until { @(Lite).Count -eq $soldiersBefore }
Check 'Cleanup: soldiers and accounts are back to the start' "$soldiersBefore/$usersBefore" "$(@(Lite).Count)/$(@(Users).Count)"

# --- PII and logs ------------------------------------------------------------------------------------------------------------
$allFinals = $finals -join "`n"
Check 'Tool results (final messages): no stored phone or personal number' $false (($allFinals -match '0500000\d\d') -or ($allFinals -match '90000\d\d') -or $allFinals.Contains($PHONE) -or $allFinals.Contains($PN))
Check 'Tool results: no password, salt or username hash' $false (($allFinals -match '[0-9a-f]{64,}') -or ($allFinals -match '(?i)"(password|salt)"'))
$log = Get-AgentLog $stack
$audits = @([regex]::Matches($log, '\[audit\] (\{.*\})') | ForEach-Object { $_.Groups[1].Value | ConvertFrom-Json })
$expected = 'create_soldier,create_user_accounts,delete_soldier,delete_user_account,reset_user_password,update_soldier,update_soldier,update_soldier,update_user_account,update_user_account'
Check 'Audit: one line per executed write (no line for the refused or rejected ones), all ok' "$expected/ok" "$((@($audits.tool) | Sort-Object) -join ',')/$((@($audits.result) | Sort-Object -Unique) -join ',')"
Check 'Audit: contact details masked in the arguments' $true (($log -match '"phone":"\[masked\]"') -and ($log -match '"personalNumber":"\[masked\]"'))
Check 'Log: no JWT, no phone, no personal number' $false (($log -match 'eyJ[A-Za-z0-9_-]{4,}\.') -or $log.Contains($PHONE) -or $log.Contains($PN) -or ($log -match '0500000\d\d'))
Remove-Item $stack.AgentOut, $stack.AgentErr, $stack.LlmLog, $stack.LlmOut -ErrorAction SilentlyContinue

$results | Format-Table -AutoSize | Out-String -Width 260
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
