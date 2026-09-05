#!/usr/bin/env pwsh
# Run all Robot Framework tests
# Usage: .\run_tests.ps1 [-TestFile <path>]
#   - No args: runs all tests in Tests/ directory
#   -TestFile: runs specific test file

param(
    [string]$TestFile = ""
)

Write-Host "========================================"
Write-Host "WPFTestAuto - Robot Framework Test Runner"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$env:PYTHONPATH = "TestAutoLayer;$env:PYTHONPATH"

if ($TestFile -eq "") {
    Write-Host "Running all tests..." -ForegroundColor Yellow
    robot --outputdir output Tests/
} else {
    Write-Host "Running: $TestFile" -ForegroundColor Yellow
    robot --outputdir output $TestFile
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Tests complete!" -ForegroundColor Green
