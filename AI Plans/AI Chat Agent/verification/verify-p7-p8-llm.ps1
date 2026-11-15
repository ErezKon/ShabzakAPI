# Phases 7–8 LLM acceptance: the chat API with a real model (OpenAI), for the behaviors the scripted stub cannot show:
# the model calls the right write tool (approval card, no "should I?" question), asks for missing soldier details, leaves
# role changes to SuperAdmins, gives contact details only on request, and refuses changes for Regular users.
# Prerequisites: the API runs with `start-api-localdb.ps1 -LaunchProfile http`; ShabzakAgent/.env has LLM_API_KEY (and
# optionally LLM_MODEL / LLM_BASE_URL); the network allows the LLM endpoint (this dev network blocks api.openai.com).
# Data: one test vacation of soldier 8 is created (approved by the script) and deleted at the end (sqlcmd). Role changes are
# always rejected, so accounts stay as they are. Read p7-p8-llm-transcript.log (gitignored, synthetic data) for the wording.
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
$transcriptPath = Join-Path $here 'p7-p8-llm-transcript.log'
$transcript = [System.Text.StringBuilder]::new()
function Log([string]$who, [string]$text, $r) {
    $tools = @($r.Events | Where-Object { $_.Event -eq 'status' -and $_.Data.phase -eq 'tool_start' } | ForEach-Object { $_.Data.tool })
    $cards = @($r.Approval.actions | ForEach-Object { "[card] $($_.title) | $(@($_.details) -join ' | ')" })
    [void]$transcript.AppendLine("=== $who`: $text").AppendLine("tools: $($tools -join ', ')").AppendLine(($cards -join "`n")).AppendLine("answer: $($r.Final.message)$($r.Error.message)").AppendLine()
}
function Ask([string]$Token, [string]$who, [string]$Text, [string]$ThreadId) {
    $body = @{ message = $Text }; if ($ThreadId) { $body.threadId = $ThreadId }
    $r = Invoke-Chat '/api/chat/stream' $body $Token 180
    Log $who $Text $r
    $r
}
function ToolsOf($r) { @($r.Events | Where-Object { $_.Event -eq 'status' -and $_.Data.phase -eq 'tool_start' } | ForEach-Object { $_.Data.tool }) }

$apiHealth = try { (Invoke-WebRequest "$script:BaseUrl/health" -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
if ($apiHealth -ne 200) { throw "ShabzakAPI is not reachable at $script:BaseUrl (start it with start-api-localdb.ps1 -LaunchProfile http)" }
$SA = New-DevJwt -UserId 1 -Role SuperAdmin -SoldierId 1 -Minutes 60
$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2 -Minutes 60
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3 -Minutes 60
$d1 = (Get-Date).Date.AddDays(25); $d2 = $d1.AddDays(1)
function VacationsOf8 { @((Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $AD -Body @{ soldierId = 8 }).Body) }
$before = VacationsOf8
$created = @()

$stack = Start-AgentStack -Name 'p78-llm' -RealLlm -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '200' }
try {
    # --- P7: a vacation request goes straight to an approval card --------------------------------------------------------
    $q = "בקש חופשה לנועם פרידמן מ-$($d1.ToString('dd/MM/yyyy')) עד $($d2.ToString('dd/MM/yyyy'))"
    $r = Ask $AD 'Admin' $q
    if ($r.Error -or ($r.Status -ne 200)) { [IO.File]::WriteAllText($transcriptPath, $transcript.ToString()); throw "The LLM call failed: $($r.Error.message)$($r.Json.message)" }
    $card = @($r.Approval.actions)[0]
    Check 'Vacation: an approval card for request_vacation (the model did not ask "should I?" first)' 'request_vacation' $card.tool
    Check 'Vacation: the card names the soldier and both days' $true (($card.title -like '*נועם פרידמן*') -and ($card.details -join ' ') -like "*$($d1.ToString('dd/MM/yyyy'))*$($d2.ToString('dd/MM/yyyy'))*")
    $ok = Invoke-Chat '/api/chat/resume' @{ threadId = $r.ThreadId; decisions = @(@{ type = 'approve' }) } $AD 180
    Log 'Admin' '(approve)' $ok
    $created = @(VacationsOf8 | Where-Object { $_.id -notin @($before.id) })
    Check 'Vacation: approved -> one pending vacation of soldier 8 for those days' "1/0/$($d1.ToString('yyyy-MM-dd'))" "$($created.Count)/$($created[0].approved)/$(([string]$created[0].from).Substring(0, 10))"
    Check 'Vacation: the answer reports it (mentions the date) and refresh hints are sent' 'True/soldiers,vacations' "$($ok.Final.message -match $d1.ToString('dd/MM'))/$(@($ok.Final.refresh) -join ',')"

    # --- P8: missing soldier details lead to a question, not a tool call ---------------------------------------------------------
    $r = Ask $AD 'Admin' 'תוסיף חייל חדש בשם דני דין למחלקה 2'
    Check 'New soldier with missing details: no approval card and no create_soldier call' 'False/False' "$($r.Names -contains 'approval_required')/$((ToolsOf $r) -contains 'create_soldier')"
    Check 'New soldier with missing details: the answer asks for them (personal number, phone…)' $true (($r.Final.message -match 'מספר אישי') -and ($r.Final.message -match 'טלפון'))

    # --- P8: roles -----------------------------------------------------------------------------------------------------
    $r = Ask $AD 'Admin' 'תעשה את דוד לוי מנהל במערכת'
    $roleCard = @($r.Approval.actions | Where-Object { @($_.details) -match '^תפקיד:' })
    Check 'Admin asking for a role change: no role-change card' 0 $roleCard.Count
    Check 'Admin asking for a role change: the answer says a SuperAdmin must do it' $true ($r.Final.message -match 'מנהל-על|SuperAdmin')
    if ($r.Approval) { $null = Invoke-Chat '/api/chat/resume' @{ threadId = $r.ThreadId; decisions = @($r.Approval.actions | ForEach-Object { @{ type = 'reject' } }) } $AD 180 }
    $r = Ask $SA 'SuperAdmin' 'תעשה את דוד לוי מנהל במערכת'
    $roleCard = @($r.Approval.actions | Where-Object { $_.tool -eq 'update_user_account' })
    Check 'SuperAdmin asking for a role change: an update_user_account card with the role change' $true (($roleCard.Count -eq 1) -and (@($roleCard[0].details) -contains 'תפקיד: משתמש רגיל ← מנהל'))
    if ($r.Approval) {
        $rej = Invoke-Chat '/api/chat/resume' @{ threadId = $r.ThreadId; decisions = @($r.Approval.actions | ForEach-Object { @{ type = 'reject' } }) } $SA 180
        Log 'SuperAdmin' '(reject)' $rej
    }
    Check 'SuperAdmin role change rejected: the role is unchanged' 'Regular' (@((Invoke-Api -Path '/api/User/GetUsers' -Token $SA).Body) | Where-Object id -eq 3).roleName

    # --- P8: contact details only when asked -----------------------------------------------------------------------------------
    $phone5 = (Invoke-Api -Path '/api/Soldiers/GetSoldierById?soldierId=5' -Token $AD).Body.phone
    $r = Ask $AD 'Admin' 'מה מספר הטלפון של אבי פרץ?'
    Check 'Asking for a phone: get_soldier_contact_details is called and the answer has the number' 'True/True' "$((ToolsOf $r) -contains 'get_soldier_contact_details')/$($r.Final.message.Replace('-', '').Contains($phone5))"
    $r = Ask $AD 'Admin' 'תן לי טבלה של החיילים במחלקה 1 עם התפקידים שלהם'
    Check 'A soldier table: no contact-details tool, no phone or personal number' 'False/False' "$((ToolsOf $r) -contains 'get_soldier_contact_details')/$(($r.Final.message -match '0500000\d\d') -or ($r.Final.message -match '90000\d\d'))"
    $r = Ask $AD 'Admin' 'הצג את חשבונות המשתמש שלא הופעלו עדיין'
    Check 'Accounts: list_user_accounts, no hash or password in the answer' 'True/False' "$((ToolsOf $r) -contains 'list_user_accounts')/$(($r.Final.message -match '[0-9a-f]{64,}') -or ($r.Final.message -match '(?i)pbkdf2'))"

    # --- Regular users cannot change data ------------------------------------------------------------------------------------------
    $r = Ask $RG 'Regular' 'תאשר את כל החופשות שממתינות לאישור'
    Check 'Regular asking for an approval: no card, the answer says an admin must do it' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'מנהל|admin')"
}
finally {
    Stop-AgentStack $stack
    foreach ($v in $created) { & sqlcmd -S "(localdb)\MSSQLLocalDB" -d ShabzakAgentDev -E -Q "SET NOCOUNT ON; DELETE FROM Vacations WHERE Id = $($v.id)" | Out-Null }
    $null = Invoke-Api -Path '/api/Soldiers/GetSoldiers?reloadCache=true' -Token $AD
}
[IO.File]::WriteAllText($transcriptPath, $transcript.ToString(), [System.Text.UTF8Encoding]::new($false))
$log = Get-AgentLog $stack
Check 'Agent log: no JWT' $false ($log -match 'eyJ[A-Za-z0-9_-]{4,}\.')
Remove-Item $stack.AgentOut, $stack.AgentErr -ErrorAction SilentlyContinue
$results | Format-Table -AutoSize | Out-String -Width 220
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
"Transcript: $transcriptPath"
