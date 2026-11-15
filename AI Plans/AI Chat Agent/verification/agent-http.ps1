# Shared helpers of the agent's live checks (verify-p7 / verify-p8). Dot-source after dev.ps1.
# - Start-AgentStack: the scripted LLM stub (fake-openai.mjs) and the agent (ShabzakAgent, LLM_BASE_URL -> the stub).
# - Invoke-Chat: POSTs to the chat API with curl.exe -N and parses the SSE events (or the JSON error).
# - Gateway mode (Phase 13): with $env:AGENT_GATEWAY_URL (e.g. http://127.0.0.1:5230/agent, the UI's nginx.conf run by
#   gateway.ps1), the chat requests to the agent on $env:AGENT_GATEWAY_PORT (default 3901, the gateway's upstream) go
#   through the gateway instead of straight to the agent. Agents on other ports are still called directly.
# No secrets: the LLM key is a dummy, tokens come from New-DevJwt, data is synthetic.
$script:AgentDir = 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\ShabzakAgent'
$script:VerificationDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:AgentUrl = 'http://localhost:3901'

function Wait-Http([string]$Url, [int]$Seconds = 30) {
    for ($i = 0; $i -lt $Seconds * 4; $i++) {
        try { if ((Invoke-WebRequest $Url -SkipHttpErrorCheck -TimeoutSec 2).StatusCode -lt 500) { return $true } } catch { }
        Start-Sleep -Milliseconds 250
    }
    $false
}

# -RealLlm: no stub; the agent uses the LLM settings of ShabzakAgent/.env (LLM_API_KEY, LLM_MODEL, LLM_BASE_URL).
function Start-AgentStack {
    param([string]$Name, [int]$AgentPort = 3901, [int]$LlmPort = 3902, [string]$ApiUrl = 'http://localhost:5165/api', [hashtable]$ExtraEnv = @{}, [switch]$RealLlm)
    $stack = [pscustomobject]@{
        AgentOut = Join-Path $script:VerificationDir "agent-$Name.out.log"; AgentErr = Join-Path $script:VerificationDir "agent-$Name.err.log"
        LlmLog = Join-Path $script:VerificationDir "llm-$Name.requests.log"; LlmOut = Join-Path $script:VerificationDir "llm-$Name.out.log"
        Agent = $null; Llm = $null; Url = "http://localhost:$AgentPort"
    }
    Remove-Item $stack.AgentOut, $stack.AgentErr, $stack.LlmLog, $stack.LlmOut -ErrorAction SilentlyContinue
    $envVars = @{ PORT = "$AgentPort"; SHABZAK_API_URL = $ApiUrl; CORS_ORIGINS = 'http://localhost:5555'; LOG_LEVEL = 'debug' }
    if (-not $RealLlm) {
        $stack.Llm = Start-Process -FilePath 'node' -ArgumentList "`"$(Join-Path $script:VerificationDir 'fake-openai.mjs')`"", "$LlmPort", "`"$($stack.LlmLog)`"" -PassThru -NoNewWindow -RedirectStandardOutput $stack.LlmOut
        $envVars += @{ LLM_BASE_URL = "http://127.0.0.1:$LlmPort/v1"; LLM_MODEL = 'fake-model'; LLM_API_KEY = 'dummy-key-for-the-local-stub' }
    }
    foreach ($k in $ExtraEnv.Keys) { $envVars[$k] = $ExtraEnv[$k] }
    $saved = @{}
    foreach ($k in $envVars.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $envVars[$k]) }
    try {
        $stack.Agent = Start-Process -FilePath 'node' -ArgumentList 'node_modules/tsx/dist/cli.mjs', 'src/server.ts' -WorkingDirectory $script:AgentDir -PassThru -NoNewWindow -RedirectStandardOutput $stack.AgentOut -RedirectStandardError $stack.AgentErr
    }
    finally { foreach ($k in $envVars.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
    $gatewayPort = if ($env:AGENT_GATEWAY_PORT) { [int]$env:AGENT_GATEWAY_PORT } else { 3901 }
    $script:AgentUrl = if ($env:AGENT_GATEWAY_URL -and $AgentPort -eq $gatewayPort) { $env:AGENT_GATEWAY_URL.TrimEnd('/') } else { $stack.Url }
    if (-not $RealLlm -and -not (Wait-Http "http://127.0.0.1:$LlmPort/v1/models")) { Stop-AgentStack $stack; throw 'The LLM stub did not start' }
    if (-not (Wait-Http "$($stack.Url)/health")) { Stop-AgentStack $stack; throw "The agent did not start (see $($stack.AgentErr))" }
    $stack
}

function Stop-AgentStack($Stack) {
    foreach ($p in @($Stack.Agent, $Stack.Llm)) { if ($p) { & taskkill /PID $p.Id /T /F *> $null } }
    Start-Sleep -Milliseconds 300
}

function Get-AgentLog($Stack) {
    (Get-Content -Raw $Stack.AgentOut -ErrorAction SilentlyContinue) + (Get-Content -Raw $Stack.AgentErr -ErrorAction SilentlyContinue)
}

function ConvertFrom-Sse([string]$Text) {
    $events = [System.Collections.Generic.List[object]]::new()
    foreach ($block in ($Text -split "`r?`n`r?`n")) {
        $name = 'message'; $data = [System.Collections.Generic.List[string]]::new()
        foreach ($line in ($block -split "`r?`n")) {
            if ($line.StartsWith('event:')) { $name = $line.Substring(6).Trim() }
            elseif ($line.StartsWith('data:')) { $data.Add($line.Substring(5).TrimStart()) }
        }
        if ($data.Count) { $events.Add([pscustomobject]@{ Event = $name; Data = (($data -join "`n") | ConvertFrom-Json -Depth 30) }) }
    }
    , $events
}

# POSTs JSON to the agent with curl.exe -N. Returns Status, ContentType, Events (SSE), Json (error body), Names, ThreadId,
# Final (final.data), Approval (approval_required.data), Statuses ("phase:tool"), Tokens (joined token texts), CurlExit.
function Invoke-Chat([string]$Path, $Body, [string]$Token, [int]$MaxSeconds = 120) {
    $file = [IO.Path]::GetTempFileName()
    $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 10 -Compress }
    [IO.File]::WriteAllText($file, $json, [Text.UTF8Encoding]::new($false))
    $curlArgs = @('-sN', '-X', 'POST', "$script:AgentUrl$Path", '-H', 'Content-Type: application/json', '--data-binary', "@$file", '--max-time', "$MaxSeconds", '-w', "`n__META__%{http_code}|%{content_type}")
    if ($Token) { $curlArgs += @('-H', "Authorization: Bearer $Token") }
    try { $raw = (& curl.exe @curlArgs) -join "`n"; $exit = $LASTEXITCODE } finally { Remove-Item $file -ErrorAction SilentlyContinue }
    $meta = [regex]::Match($raw, '__META__(\d+)\|(.*)$')
    $content = if ($meta.Success) { $raw.Substring(0, $meta.Index) } else { $raw }
    $status = if ($meta.Success) { [int]$meta.Groups[1].Value } else { 0 }
    $contentType = if ($meta.Success) { $meta.Groups[2].Value.Trim() } else { '' }
    $events = if ($contentType -like 'text/event-stream*') { ConvertFrom-Sse $content } else { @() }
    $parsed = $null
    if ($contentType -like 'application/json*' -and $content.Trim()) { try { $parsed = $content | ConvertFrom-Json -Depth 30 } catch { } }
    $find = { param($n) ($events | Where-Object Event -eq $n | Select-Object -First 1).Data }
    [pscustomobject]@{
        Status = $status; ContentType = $contentType; Events = $events; Json = $parsed; Raw = $content; CurlExit = $exit
        Names = @($events | ForEach-Object Event)
        ThreadId = (& $find 'thread').threadId
        Final = (& $find 'final'); Approval = (& $find 'approval_required'); Error = (& $find 'error')
        Statuses = @($events | Where-Object Event -eq 'status' | ForEach-Object { "$($_.Data.phase):$($_.Data.tool)" })
        Tokens = (@($events | Where-Object Event -eq 'token' | ForEach-Object { $_.Data.text }) -join '')
    }
}

# Starts a chat request in the background (curl.exe writes the SSE body to a file); returns the process and the file.
function Start-ChatInBackground([string]$Path, $Body, [string]$Token, [int]$MaxSeconds = 60) {
    $file = [IO.Path]::GetTempFileName(); $out = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllText($file, ($Body | ConvertTo-Json -Depth 10 -Compress), [Text.UTF8Encoding]::new($false))
    $argList = @('-sN', '-X', 'POST', "`"$script:AgentUrl$Path`"", '-H', '"Content-Type: application/json"', '-H', "`"Authorization: Bearer $Token`"", '--data-binary', "`"@$file`"", '--max-time', "$MaxSeconds", '-o', "`"$out`"")
    $p = Start-Process -FilePath 'curl.exe' -ArgumentList $argList -PassThru -NoNewWindow
    [pscustomobject]@{ Process = $p; Output = $out; Body = $file }
}

# Runs one tool through the dev CLI (`--tool`, no LLM). Returns Json (parsed stdout), Stdout and Stderr (logs, audit lines).
function Invoke-CliTool([string]$Token, [string]$Name, [string]$ToolArgs = '{}', [switch]$Yes, [string]$ApiUrl = 'http://localhost:5165/api') {
    $env:SHABZAK_TOKEN = $Token
    $env:SHABZAK_API_URL = $ApiUrl
    $errFile = [IO.Path]::GetTempFileName()
    $cliArgs = @('node_modules/tsx/dist/cli.mjs', 'src/scripts/chat-cli.ts', '--tool', $Name, '--args', $ToolArgs)
    if ($Yes) { $cliArgs += '--yes' }
    Push-Location $script:AgentDir
    try { $out = (& node @cliArgs 2> $errFile) -join "`n" } finally { Pop-Location }
    $err = Get-Content -Raw $errFile -ErrorAction SilentlyContinue
    Remove-Item $errFile -ErrorAction SilentlyContinue
    $parsed = try { $out | ConvertFrom-Json -Depth 30 } catch { [pscustomobject]@{ error = "unparsable output: $out" } }
    [pscustomobject]@{ Json = $parsed; Stdout = $out; Stderr = "$err" }
}

function Get-Sha512Hex([string]$Text) {
    (([Security.Cryptography.SHA512]::HashData([Text.Encoding]::UTF8.GetBytes($Text))) | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Wait-ThreadIdInFile([string]$File, [int]$Seconds = 15) {
    for ($i = 0; $i -lt $Seconds * 10; $i++) {
        $text = Get-Content -Raw $File -ErrorAction SilentlyContinue
        if ($text -match '"threadId":"([^"]+)"') { return $Matches[1] }
        Start-Sleep -Milliseconds 100
    }
    $null
}
