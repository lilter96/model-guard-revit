[CmdletBinding()]
param([string]$RevitApiRoot = "")
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
foreach ($year in 2021..2025) {
    $arguments = @("build", (Join-Path $repo "BimPortfolio.sln"), "-c", "Release", "-p:RevitVersion=$year", "--nologo")
    if ($RevitApiRoot) { $arguments += "-p:RevitApiDir=$(Join-Path $RevitApiRoot $year)" }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed for Revit $year" }
}
& dotnet test (Join-Path $repo "tests/BimPortfolio.Core.Tests/BimPortfolio.Core.Tests.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Core tests failed" }

& dotnet test (Join-Path $repo "tests/BimPortfolio.Presentation.Tests/BimPortfolio.Presentation.Tests.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Presentation tests failed" }
