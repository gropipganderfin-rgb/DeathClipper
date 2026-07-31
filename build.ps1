$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot
try {
    dotnet restore .\DeathClipper.csproj
    dotnet build .\DeathClipper.csproj -c Release -p:Platform=x64 --no-restore

    Write-Host ""
    Write-Host "Build complete. Look in:"
    Write-Host "  $PSScriptRoot\bin\x64\Release\DeathClipper"
}
finally {
    Pop-Location
}
