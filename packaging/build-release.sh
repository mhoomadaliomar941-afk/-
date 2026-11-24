#!/usr/bin/env bash
# Builds the mod, runs the unit tests and assembles a ready-to-install package in dist/.
# Works on Linux/macOS/Windows (Git Bash) with the .NET 8 SDK installed.
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)"
OUT="dist/OrganizationEmpire-v${VERSION}"
echo "== Organization Empire ${VERSION}"

echo "== Running unit tests"
dotnet test tests/OrganizationEmpire.Core.Tests/OrganizationEmpire.Core.Tests.csproj -c Release --nologo

echo "== Building script DLL (net48, x64)"
dotnet build src/OrganizationEmpire/OrganizationEmpire.csproj -c Release --nologo

rm -rf "$OUT" "$OUT.zip"
mkdir -p "$OUT/scripts/OrganizationEmpire"
cp src/OrganizationEmpire/bin/Release/net48/OrganizationEmpire.dll "$OUT/scripts/"

echo "== Generating default config and sample flags"
dotnet run --project tools/OrganizationEmpire.PackTool -c Release -- "$OUT/scripts/OrganizationEmpire" < /dev/null

cp README.md "$OUT/README.md"
cp -r docs "$OUT/docs"
cp LICENSE "$OUT/LICENSE" 2>/dev/null || true

echo "== Zipping"
if command -v zip >/dev/null 2>&1; then
  (cd dist && zip -qr "OrganizationEmpire-v${VERSION}.zip" "OrganizationEmpire-v${VERSION}")
else
  python3 - "$OUT" <<'PY'
import os, sys, zipfile
root = sys.argv[1]
base = os.path.dirname(root)
with zipfile.ZipFile(root + ".zip", "w", zipfile.ZIP_DEFLATED) as z:
    for d, _, files in os.walk(root):
        for f in files:
            p = os.path.join(d, f)
            z.write(p, os.path.relpath(p, base))
PY
fi
echo "== Done: $OUT.zip"
