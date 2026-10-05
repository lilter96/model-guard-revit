[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet(2021,2022,2023,2024,2025)][int]$RevitVersion)
$ErrorActionPreference="Stop"
if (-not $env:APPDATA) { throw "Run on Windows." }
$ids=@{AccessRoute="79e1b985-13b7-46da-a08e-e81b8b7181bb";ModelGuard="779f99a5-61bf-4a8d-ae2d-b5b6ae381eca"}
foreach($name in @("ModelGuard")) {
    $manifest=Join-Path $env:APPDATA "Autodesk/Revit/Addins/$RevitVersion/BimPortfolio.$name.addin"
    if(Test-Path $manifest) {
        if(-not (Get-Content $manifest -Raw).Contains($ids[$name])) { throw "Unexpected manifest ownership: $manifest" }
        Remove-Item $manifest
    }
}
Write-Host "Add-ins disabled. Restart Revit. Model data and binaries are preserved."
