# Phase 7 acceptance (curl -N): the SSE chat API, sessions, approvals and the vacation write tools, end to end against the
# LOCAL synthetic dev API. The LLM is the scripted stub (fake-openai.mjs: the dev network blocks OpenAI), so the agent,
# its approval flow and every API call are real; only the model's choices are scripted (CALL directives).
# Prerequisite: the API runs with `start-api-localdb.ps1 -LaunchProfile http` on today's seed.
# Users: 1 SuperAdmin, 2 Admin, 3 Regular. Soldier 8 (נועם פרידמן) gets test vacations, deleted again at the end (sqlcmd).
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

$apiHealth = try { (Invoke-WebRequest "$script:BaseUrl/health" -SkipHttpErrorCheck -TimeoutSec 10).StatusCode } catch { 0 }
if ($apiHealth -ne 200) { throw "ShabzakAPI is not reachable at $script:BaseUrl (start it with start-api-localdb.ps1 -LaunchProfile http)" }

$SA = New-DevJwt -UserId 1 -Role SuperAdmin -SoldierId 1 -Minutes 60
$AD = New-DevJwt -UserId 2 -Role Admin -SoldierId 2 -Minutes 60
$RG = New-DevJwt -UserId 3 -Role Regular -SoldierId 3 -Minutes 60
$today = (Get-Date).Date
$tomorrow = $today.AddDays(1).ToString('yyyy-MM-dd')
$d1 = $today.AddDays(20).ToString('yyyy-MM-dd'); $d2 = $today.AddDays(21).ToString('yyyy-MM-dd'); $d3 = $today.AddDays(23).ToString('yyyy-MM-dd')
$patrol = @((Invoke-Api -Path '/api/Mission/GetMissionsSummary' -Token $AD).Body) | Where-Object { $_.name -eq 'סיור' }
function VacationsOf8 { @((Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $AD -Body @{ soldierId = 8 }).Body) }
$created = [System.Collections.Generic.List[int]]::new()
$before = VacationsOf8

$stack = Start-AgentStack -Name 'p7' -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '60' }
try {
    # --- Auth and validation -------------------------------------------------------------
    $r = Invoke-Chat '/api/chat/stream' @{ message = 'x' }
    Check 'No token -> 401 unauthorized' '401/unauthorized' "$($r.Status)/$($r.Json.code)"
    $r = Invoke-Chat '/api/chat/stream' @{ message = '   ' } $AD
    Check 'Empty message -> 400 bad_request' '400/bad_request' "$($r.Status)/$($r.Json.code)"
    $r = Invoke-Chat '/api/chat/stream' @{ threadId = 'no-such-thread'; message = 'x' } $AD
    Check 'Unknown thread -> 404 thread_not_found' '404/thread_not_found' "$($r.Status)/$($r.Json.code)"

    # --- Streaming a read-only answer --------------------------------------------------------
    $r = Invoke-Chat '/api/chat/stream' @{ message = "CALL get_schedule {`"from`":`"$tomorrow`",`"missionIds`":[$($patrol.id)]}" } $AD
    Check 'Read flow: 200 text/event-stream' '200/text/event-stream; charset=utf-8' "$($r.Status)/$($r.ContentType)"
    Check 'Read flow: thread is the first event, done the last' 'thread/done' "$($r.Names[0])/$($r.Names[-1])"
    Check 'Read flow: the thread ID is a UUID' $true ($r.ThreadId -match '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
    Check 'Read flow: status thinking -> tool_start -> tool_end -> thinking' 'thinking:,tool_start:get_schedule,tool_end:get_schedule,thinking:' ($r.Statuses -join ',')
    Check 'Read flow: Hebrew tool label' 'בודק את לוח השיבוצים…' (($r.Events | Where-Object { $_.Event -eq 'status' -and $_.Data.phase -eq 'tool_start' }).Data.label)
    Check 'Read flow: the streamed tokens add up to the final message' $true ($r.Tokens -eq $r.Final.message)
    $apiPatrol = @((Invoke-Api -Path '/api/Schedule/GetAssignments' -Method POST -Token $AD -Body @{ from = "$($tomorrow)T06:00:00"; to = "$($tomorrow)T07:00:00"; missionIds = @($patrol.id) }).Body)[0]
    Check "Read flow: the answer has tomorrow's patrol from the API (#$($apiPatrol.instanceId) $($apiPatrol.assignedCount)/$($apiPatrol.requiredCount))" $true ($r.Final.message.Contains("`"instanceId`":$($apiPatrol.instanceId)") -and $r.Final.message.Contains("$($apiPatrol.assignedCount)/$($apiPatrol.requiredCount)"))
    Check 'Read flow: no refresh hints' 0 @($r.Final.refresh).Count

    # --- Vacation request: approval first, API only after approve -------------------------------
    $ask = Invoke-Chat '/api/chat/stream' @{ message = "CALL request_vacation {`"soldierId`":8,`"from`":`"$d1`",`"to`":`"$d2`"}" } $AD
    $T1 = $ask.ThreadId
    Check 'Request: ends with approval_required then done, no final' 'approval_required/done/False' "$($ask.Names[-2])/$($ask.Names[-1])/$($ask.Names -contains 'final')"
    $card = @($ask.Approval.actions)[0]
    Check 'Request: one action card (index 0, request_vacation, low risk)' '1/0/request_vacation/low' "$(@($ask.Approval.actions).Count)/$($card.index)/$($card.tool)/$($card.risk)"
    Check 'Request: the card is built by the server (title, soldier, period)' $true (($card.title -eq 'בקשת חופשה עבור נועם פרידמן') -and ($card.details[0] -like '*נועם פרידמן (#8*') -and ($card.details[1] -like '*(יומיים)'))
    Check 'Request: the API was not called before the approval' $before.Count (VacationsOf8).Count
    $r = Invoke-Chat '/api/chat/stream' @{ threadId = $T1; message = 'עוד שאלה' } $AD
    Check 'A message while the approval is pending -> 409 approval_pending' '409/approval_pending' "$($r.Status)/$($r.Json.code)"
    $r = Invoke-Chat '/api/chat/resume' @{ threadId = $T1; decisions = @(@{ type = 'approve' }, @{ type = 'approve' }) } $AD
    Check 'Resume with the wrong number of decisions -> 400' '400/bad_request' "$($r.Status)/$($r.Json.code)"
    $r = Invoke-Chat '/api/chat/resume' @{ threadId = $T1; decisions = @(@{ type = 'approve' }) } $RG
    Check "Another user's resume -> 403 forbidden" '403/forbidden' "$($r.Status)/$($r.Json.code)"
    $r = Invoke-Chat '/api/chat/stream' @{ threadId = $T1; message = 'x' } $RG
    Check "Another user's message on the thread -> 403 forbidden" '403/forbidden' "$($r.Status)/$($r.Json.code)"
    $ok = Invoke-Chat '/api/chat/resume' @{ threadId = $T1; decisions = @(@{ type = 'approve' }) } $AD
    Check 'Approve: the write runs (tool_start/tool_end request_vacation)' 'tool_start:request_vacation,tool_end:request_vacation' (($ok.Statuses | Select-Object -First 2) -join ',')
    Check 'Approve: final with refresh soldiers + vacations' 'soldiers,vacations' (@($ok.Final.refresh) -join ',')
    $after = VacationsOf8
    $new = @($after | Where-Object { $_.id -notin @($before.id) })
    if ($new.Count -eq 1) { $created.Add($new[0].id) }
    Check 'Approve: exactly one vacation created, pending' '1/0' "$($new.Count)/$($new[0].approved)"
    Check 'Approve: the period covers both days (00:00 to 23:59:59 of the last day)' "$($d1)T00:00:00/$($d2)T23:59:59" "$($new[0].from)/$($new[0].to)"
    $r = Invoke-Chat '/api/chat/resume' @{ threadId = $T1; decisions = @(@{ type = 'approve' }) } $AD
    Check 'Resume without a pending approval -> 400' '400/bad_request' "$($r.Status)/$($r.Json.code)"

    # --- respond_to_vacation: approve, then deny the approved vacation ---------------------------------
    $vid = $new[0].id
    $ask = Invoke-Chat '/api/chat/stream' @{ threadId = $T1; message = "CALL respond_to_vacation {`"vacationId`":$vid,`"decision`":`"approve`",`"soldierId`":8}" } $AD
    $card = @($ask.Approval.actions)[0]
    Check 'Respond: the approve card shows the status change' $true (($card.title -eq 'אישור חופשה של נועם פרידמן') -and (@($card.details) -contains 'סטטוס: ממתינה לאישור ← מאושרת'))
    $null = Invoke-Chat '/api/chat/resume' @{ threadId = $T1; decisions = @(@{ type = 'approve' }) } $AD
    Check 'Respond: the vacation is approved in the API' 1 (@(VacationsOf8) | Where-Object id -eq $vid).approved
    $ask = Invoke-Chat '/api/chat/stream' @{ threadId = $T1; message = "CALL respond_to_vacation {`"vacationId`":$vid,`"decision`":`"deny`"}" } $AD
    $card = @($ask.Approval.actions)[0]
    Check 'Respond: denying an approved vacation is medium risk with a warning (lookup without soldierId)' 'medium/True' "$($card.risk)/$(@($card.warnings) -contains 'החופשה מאושרת כרגע: הדחייה תבטל אותה.')"
    $null = Invoke-Chat '/api/chat/resume' @{ threadId = $T1; decisions = @(@{ type = 'approve' }) } $AD
    Check 'Respond: the vacation is denied in the API' 2 (@(VacationsOf8) | Where-Object id -eq $vid).approved

    # --- Reject: nothing is written ------------------------------------------------------------------
    $countBefore = (VacationsOf8).Count
    $ask = Invoke-Chat '/api/chat/stream' @{ message = "CALL request_vacation {`"soldierId`":8,`"from`":`"$d3`",`"to`":`"$d3`"}" } $AD
    $T2 = $ask.ThreadId
    $rej = Invoke-Chat '/api/chat/resume' @{ threadId = $T2; decisions = @(@{ type = 'reject'; message = 'לא בתאריך הזה' }) } $AD
    Check 'Reject: the model is told it was rejected, with the reason' $true ($rej.Final.message -match 'REJECTED: the user did not approve request_vacation.*Their reason: "לא בתאריך הזה"')
    Check 'Reject: no API call, no refresh' "$countBefore/0" "$((VacationsOf8).Count)/$(@($rej.Final.refresh).Count)"

    # --- Invalid action: refused without asking ------------------------------------------------------------
    $r = Invoke-Chat '/api/chat/stream' @{ message = "CALL request_vacation {`"soldierId`":99999,`"from`":`"$d1`",`"to`":`"$d2`"}" } $AD
    Check 'Invalid soldier: no approval card, the model gets NOT RUN' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'NOT RUN: request_vacation .*Soldier 99999 was not found')"

    # --- Regular users have no write tools -----------------------------------------------------------------------
    $own = @((Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $AD -Body @{ soldierId = 3 }).Body).Count
    $r = Invoke-Chat '/api/chat/stream' @{ message = "CALL request_vacation {`"soldierId`":3,`"from`":`"$d1`",`"to`":`"$d2`"}" } $RG
    Check 'Regular: a write call is not a tool of theirs (no card, no API call)' 'False/True' "$($r.Names -contains 'approval_required')/$($r.Final.message -match 'request_vacation is not a valid tool')"
    Check "Regular: the own soldier's vacations are unchanged" $own @((Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $AD -Body @{ soldierId = 3 }).Body).Count
    $requests = @(Get-Content $stack.LlmLog -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
    $regularRequests = @($requests | Where-Object { $_.lastUser -like '*"soldierId":3*' })
    $adminRequests = @($requests | Where-Object { $_.lastUser -like '*"soldierId":8*' })
    Check 'Model requests of the Regular user never offer write tools' 0 @($regularRequests | Where-Object { ($_.tools -contains 'request_vacation') -or ($_.tools -contains 'respond_to_vacation') }).Count
    Check 'Model requests of the Admin offer both vacation write tools' $true (@($adminRequests | Where-Object { ($_.tools -contains 'request_vacation') -and ($_.tools -contains 'respond_to_vacation') }).Count -gt 0)
    Check 'The system prompt has the write workflows for the Admin' $true (($adminRequests[0].system -like '*Changes (each one runs only after the user approves its card in the chat)*'))

    # --- One run per thread; leaving stops the run ----------------------------------------------------------------
    $bg = Start-ChatInBackground '/api/chat/stream' @{ message = 'SLOW 4000 hello' } $AD
    $T3 = Wait-ThreadIdInFile $bg.Output
    $r = Invoke-Chat '/api/chat/stream' @{ threadId = $T3; message = 'x' } $AD
    Check 'A second message while the first is answered -> 409 thread_busy' '409/thread_busy' "$($r.Status)/$($r.Json.code)"
    $bg.Process.WaitForExit(20000) | Out-Null
    $slow = ConvertFrom-Sse (Get-Content -Raw $bg.Output)
    Check 'The first message still completes' 'echo: hello' (($slow | Where-Object Event -eq 'final').Data.message)
    Remove-Item $bg.Output, $bg.Body -ErrorAction SilentlyContinue
    $cut = Invoke-Chat '/api/chat/stream' @{ threadId = $T3; message = 'SLOW 8000 bye' } $AD 2
    Check 'Client gives up after 2 s (curl timeout)' 28 $cut.CurlExit
    $resumed = $null
    for ($i = 0; $i -lt 15; $i++) { $resumed = Invoke-Chat '/api/chat/stream' @{ threadId = $T3; message = 'שוב' } $AD; if ($resumed.Status -ne 409) { break }; Start-Sleep -Milliseconds 200 }
    Check 'After the client left, the thread is free again (the run was stopped)' '200/echo: שוב' "$($resumed.Status)/$($resumed.Final.message)"

    # --- Role change: a thread is not continued under another role (review 2026-11-16) ----------------------------------------
    $countBefore = (VacationsOf8).Count
    $ask = Invoke-Chat '/api/chat/stream' @{ message = "CALL request_vacation {`"soldierId`":8,`"from`":`"$d3`",`"to`":`"$d3`"}" } $AD
    $T4 = $ask.ThreadId
    $demoted = $false
    try {
        $demoted = (Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 2; role = 0 }).Status -eq 200
        $asRegular = New-DevJwt -UserId 2 -Role Admin -SoldierId 2 -Minutes 60
        $next = Invoke-Chat '/api/chat/stream' @{ threadId = $T4; message = 'עוד שאלה' } $asRegular
        $resume = Invoke-Chat '/api/chat/resume' @{ threadId = $T4; decisions = @(@{ type = 'approve' }) } $asRegular
        $countNow = @((Invoke-Api -Path '/api/Soldiers/GetVacations' -Method POST -Token $SA -Body @{ soldierId = 8 }).Body).Count
        Check 'Role change (Admin -> Regular): stream and resume on the old thread -> 404 thread_not_found, nothing written' "True/404/thread_not_found/404/thread_not_found/$countBefore" "$demoted/$($next.Status)/$($next.Json.code)/$($resume.Status)/$($resume.Json.code)/$countNow"
        $fresh = Invoke-Chat '/api/chat/stream' @{ message = 'שלום' } $asRegular
        Check 'Role change: a new thread works with the new role' '200/True/echo: שלום' "$($fresh.Status)/$($fresh.ThreadId -ne $T4)/$($fresh.Final.message)"
    }
    finally {
        if ($demoted) { $null = Invoke-Api -Path '/api/User/UpdateUser' -Method POST -Token $SA -Body @{ userId = 2; role = 1 } }
    }
    $asAdmin = New-DevJwt -UserId 2 -Role Admin -SoldierId 2 -Minutes 60
    $back = Invoke-Chat '/api/chat/resume' @{ threadId = $T4; decisions = @(@{ type = 'reject' }) } $asAdmin
    Check 'Back to Admin: the thread is usable again (its card rejected, nothing written)' "200/True/$countBefore" "$($back.Status)/$($back.Final.message -match 'REJECTED: the user did not approve request_vacation')/$((VacationsOf8).Count)"

    # --- Reset ---------------------------------------------------------------------------------------------------------
    $r = Invoke-Chat '/api/chat/reset' @{ threadId = $T2 } $RG
    Check "Reset of another user's thread -> 403" '403/forbidden' "$($r.Status)/$($r.Json.code)"
    $r = Invoke-Chat '/api/chat/reset' @{ threadId = $T2 } $AD
    Check 'Reset -> 204' 204 $r.Status
    $r = Invoke-Chat '/api/chat/stream' @{ threadId = $T2; message = 'x' } $AD
    Check 'A reset thread is gone -> 404 thread_not_found' '404/thread_not_found' "$($r.Status)/$($r.Json.code)"
}
finally {
    Stop-AgentStack $stack
    foreach ($id in $created) { & sqlcmd -S "(localdb)\MSSQLLocalDB" -d ShabzakAgentDev -E -Q "SET NOCOUNT ON; DELETE FROM Vacations WHERE Id = $id" | Out-Null }
    $null = Invoke-Api -Path '/api/Soldiers/GetSoldiers?reloadCache=true' -Token $AD
}
Check 'Cleanup: the vacations of soldier 8 are back to the start' $before.Count (VacationsOf8).Count

# --- Rate limit: a second agent with RATE_LIMIT_PER_MINUTE=3 (3 tokens, one more every 20 s) ----------------------------
$mini = Start-AgentStack -Name 'p7-rate' -AgentPort 3903 -LlmPort 3904 -ExtraEnv @{ RATE_LIMIT_PER_MINUTE = '3' }
try {
    $codes = @(1..4 | ForEach-Object { (Invoke-Chat '/api/chat/resume' @{} $SA).Status })
    $limited = Invoke-WebRequest -Uri "$($mini.Url)/api/chat/stream" -Method POST -Headers @{ Authorization = "Bearer $SA" } -ContentType 'application/json' -Body '{"message":"x"}' -SkipHttpErrorCheck
    $retryAfter = [int]("$($limited.Headers['Retry-After'])")
    Check 'Rate limit: 3 requests pass, then 429 rate_limited with Retry-After (stream and resume share the bucket)' '400,400,400,429/429/rate_limited/True' "$($codes -join ',')/$($limited.StatusCode)/$(($limited.Content | ConvertFrom-Json).code)/$($retryAfter -ge 1)"
    $other = Invoke-Chat '/api/chat/stream' @{ message = 'x' } $AD
    Check 'Rate limit: per user (another user is not limited)' 200 $other.Status
}
finally { Stop-AgentStack $mini; Remove-Item $mini.AgentOut, $mini.AgentErr, $mini.LlmLog, $mini.LlmOut -ErrorAction SilentlyContinue }
$script:AgentUrl = $stack.Url

# --- Logs --------------------------------------------------------------------------------------------------------------------
$log = Get-AgentLog $stack
$audits = @([regex]::Matches($log, '\[audit\] (\{.*\})') | ForEach-Object { $_.Groups[1].Value | ConvertFrom-Json })
Check 'Audit: one line per executed write (1 request + 2 responses), all ok' '3/ok' "$($audits.Count)/$((@($audits.result) | Sort-Object -Unique) -join ',')"
Check 'Audit: user, role and thread of every line' $true (@($audits | Where-Object { $_.userId -eq 2 -and $_.role -eq 'Admin' -and $_.threadId -eq $T1 }).Count -eq 3)
Check 'Audit: no line for the rejected or refused actions' 0 @($audits | Where-Object { $_.threadId -eq $T2 }).Count
Check "Log: the client's disconnect stopped the run" $true ($log -match "Client left thread $T3; stopping the run")
Check 'Log: no JWT' $false ($log -match 'eyJ[A-Za-z0-9_-]{4,}\.')
Check 'Log: no phone number or personal number' $false (($log -match '0500000\d\d') -or ($log -match '90000\d\d'))
Remove-Item $stack.AgentOut, $stack.AgentErr, $stack.LlmLog, $stack.LlmOut -ErrorAction SilentlyContinue

$results | Format-Table -AutoSize | Out-String -Width 260
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)  FAIL: $(@($results | Where-Object Result -eq 'FAIL').Count)"
