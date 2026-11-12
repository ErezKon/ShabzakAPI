# Starts ShabzakAPI against an isolated SQL LocalDB database with SYNTHETIC data only.
# Process-scoped env vars override appsettings.json and user-secrets (the prod connection string is never used).
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'dev.ps1') # creates jwt.key on first use
$env:Jwt__Key = Get-Content -Raw (Join-Path $here 'jwt.key')
$env:ConnectionStrings__ShabzakDB = 'Server=(localdb)\MSSQLLocalDB;Database=ShabzakAgentDev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False'
Set-Location 'C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\repos\Shabzak\ShabzakAPI\04.ShabzakAPI'
dotnet run --launch-profile https --no-build
