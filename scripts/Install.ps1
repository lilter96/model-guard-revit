[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet(2021,2022,2023,2024,2025)][int]$RevitVersion)
$ErrorActionPreference = "Stop"
if (-not $env:APPDATA -or -not $env:LOCALAPPDATA) { throw "Run this script on Windows." }
$repo = Split-Path $PSScriptRoot -Parent
$tfm = if ($RevitVersion -eq 2025) { "net8.0-windows" } else { "net48" }
$ids = @{ AccessRoute = "79e1b985-13b7-46da-a08e-e81b8b7181bb"; ModelGuard = "779f99a5-61bf-4a8d-ae2d-b5b6ae381eca" }
$plans = @()
foreach ($name in @("ModelGuard")) {
    $source = Join-Path $repo "artifacts/$RevitVersion/$name/Release/$tfm"
    if (-not (Test-Path (Join-Path $source "$name.dll"))) { throw "Build $name for Revit $RevitVersion first." }
    if ((Test-Path (Join-Path $source "RevitAPI.dll")) -or (Test-Path (Join-Path $source "RevitAPIUI.dll"))) { throw "Host API DLLs must not be deployed." }
    $manifest = Join-Path $env:APPDATA "Autodesk/Revit/Addins/$RevitVersion/BimPortfolio.$name.addin"
    if ((Test-Path $manifest) -and -not ((Get-Content $manifest -Raw).Contains($ids[$name]))) { throw "Manifest $manifest belongs to another add-in." }
    $target = Join-Path $env:LOCALAPPDATA "BimPortfolio/$RevitVersion/$name"
    $plans += @{ Name=$name; Source=$source; Target=$target; Manifest=$manifest }
}
foreach ($plan in $plans) {
    New-Item -ItemType Directory -Path $plan.Target -Force | Out-Null
    Copy-Item (Join-Path $plan.Source "*") $plan.Target -Recurse -Force
    New-Item -ItemType Directory -Path (Split-Path $plan.Manifest -Parent) -Force | Out-Null
    $assembly = [System.Security.SecurityElement]::Escape((Join-Path $plan.Target "$($plan.Name).dll"))
    $name = $plan.Name
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>$name</Name><Assembly>$assembly</Assembly>
    <AddInId>$($ids[$name])</AddInId><FullClassName>$name.App</FullClassName>
    <VendorId>BIMP</VendorId><VendorDescription>BIM portfolio projects</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
    [System.IO.File]::WriteAllText($plan.Manifest, $xml, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Installed $name for Revit $RevitVersion -> $($plan.Manifest)"
}
Write-Host "Restart Revit. The panel appears on the Add-Ins tab. Close Revit before reinstalling."
