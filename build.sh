#!/usr/bin/env bash
# build.sh — builds a local self-contained binary
# Usage: ./build.sh [linux-x64|win-x64|osx-x64]

RID=${1:-linux-x64}
OUT="./dist/$RID"

echo "Building nexus for $RID..."

dotnet publish src/Nexus.Cli/Nexus.Cli.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o "$OUT"

BINARY="$OUT/nexus"
if [ "$RID" = "win-x64" ]; then
  BINARY="$OUT/nexus.exe"
fi

chmod +x "$BINARY" 2>/dev/null || true

echo ""
echo "✅ Binary ready: $BINARY"
echo ""
echo "To install globally:"
echo "  sudo cp $BINARY /usr/local/bin/nexus"
