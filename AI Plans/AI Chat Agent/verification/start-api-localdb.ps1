# Starts ShabzakAPI against an isolated SQL LocalDB database with SYNTHETIC data only.
# Process-scoped env vars override appsettings.json and user-secrets (the prod connection string is never used).
# -LaunchProfile https (default): https://localhost:7170 + http://localhost:5165 (http redirects to https) - used by verify-p1..p4.
# -LaunchProfile http: http://localhost:5165 only (no redirect) - used by the agent (verify-p5/p6, chat CLI).
param([ValidateSet('https', 'http')][string]$LaunchProfile = 'https')
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'dev.ps1') # creates jwt.key on first use
$env:Jwt__Key = Get-Content -Raw (Join-Path $here 'jwt.key')
$env:ConnectionStrings__ShabzakDB = 'Server=(localdb)\MSSQLLocalDB;Database=ShabzakAgentDev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False'
Set-Location 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\ShabzakAPI\04.ShabzakAPI'
dotnet run --launch-profile $LaunchProfile --no-build
