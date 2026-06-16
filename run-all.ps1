# Launches all three JobAgents front ends in parallel, each in its own window:
#   • Blazor Server  → http://localhost:5221
#   • React API      → http://localhost:5300
#   • React dev      → http://localhost:5173
#
# Usage:  pwsh ./run-all.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Start-Process pwsh -ArgumentList '-NoExit', '-Command', "dotnet run --project `"$root/src/JobAgents.Web`""
Start-Process pwsh -ArgumentList '-NoExit', '-Command', "dotnet run --project `"$root/src/JobAgents.Api`""

$react = Join-Path $root 'web-react'
if (-not (Test-Path (Join-Path $react 'node_modules'))) {
    Write-Host 'Installing React dependencies (first run)...' -ForegroundColor Cyan
    Push-Location $react; npm install; Pop-Location
}
Start-Process pwsh -ArgumentList '-NoExit', '-Command', "Set-Location `"$react`"; npm run dev"

Write-Host ''
Write-Host 'Started:' -ForegroundColor Green
Write-Host '  Blazor  http://localhost:5221'
Write-Host '  API     http://localhost:5300'
Write-Host '  React   http://localhost:5173'
