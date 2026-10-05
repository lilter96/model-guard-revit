#!/usr/bin/env bash
set -euo pipefail
portfolio_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
portfolio_dotnet="dotnet"
if [[ -x "$portfolio_root/.tools/dotnet/dotnet" ]]; then
  portfolio_dotnet="$portfolio_root/.tools/dotnet/dotnet"
fi
cd "$portfolio_root"
"$portfolio_dotnet" test tests/BimPortfolio.Core.Tests/BimPortfolio.Core.Tests.csproj -c Release --nologo
"$portfolio_dotnet" test tests/BimPortfolio.Presentation.Tests/BimPortfolio.Presentation.Tests.csproj -c Release --nologo
"$portfolio_dotnet" run --project tools/BimPortfolio.Demo -c Release -- samples demo-output
