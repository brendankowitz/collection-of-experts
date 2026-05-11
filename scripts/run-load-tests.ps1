<#
.SYNOPSIS
    Run k6 load tests against AgentHost (requires Docker or k6 installed).
.DESCRIPTION
    Starts AgentHost in the background (mock provider), runs both k6 load test
    scripts, then stops the host.
.PARAMETER BaseUrl
    The AgentHost base URL. Defaults to http://localhost:5000.
.PARAMETER UseDocker
    If set, runs k6 via Docker (grafana/k6 image). Otherwise requires local k6.
#>
param(
    [string]$BaseUrl = "http://localhost:5000",
    [switch]$UseDocker
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent

function Invoke-K6 {
    param([string]$Script)
    $fullPath = Join-Path $repoRoot $Script
    Write-Host ""
    Write-Host "=== Running: $Script ===" -ForegroundColor Cyan

    if ($UseDocker) {
        docker run --rm -i --network host `
            -e "BASE_URL=$BaseUrl" `
            grafana/k6 run - < $fullPath
    } else {
        k6 run -e "BASE_URL=$BaseUrl" $fullPath
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Script" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "PASSED: $Script" -ForegroundColor Green
}

Write-Host "Load test runner starting..." -ForegroundColor Yellow
Write-Host "Target: $BaseUrl"

Invoke-K6 "loadtests/agenthost-sse.k6.js"
Invoke-K6 "loadtests/signalr-fanout.k6.js"

Write-Host ""
Write-Host "All load tests completed." -ForegroundColor Green
