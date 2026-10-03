#!/usr/bin/env bash
set -e

ARCH="${1:-osx-arm64}"
VERSION="${2:-0.1.0}"
APP_NAME="Advanced Clipboard Manager"
BUNDLE_DIR="dist/$ARCH/$APP_NAME.app"

echo "==> Building $APP_NAME for $ARCH (v$VERSION)"

dotnet publish src/ClipboardManager.Mac -c Release -r "$ARCH" --self-contained true -p:PublishSingleFile=true -p:Version="$VERSION" -o "publish/$ARCH"

rm -rf "$BUNDLE_DIR"
mkdir -p "$BUNDLE_DIR/Contents/MacOS"
mkdir -p "$BUNDLE_DIR/Contents/Resources"

cp "publish/$ARCH/ClipboardManager" "$BUNDLE_DIR/Contents/MacOS/"
chmod +x "$BUNDLE_DIR/Contents/MacOS/ClipboardManager"
cp packaging/mac/Info.plist "$BUNDLE_DIR/Contents/"

# Replace version in Info.plist
if [[ "$OSTYPE" == "darwin"* ]]; then
  sed -i '' "s/0.1.0/$VERSION/g" "$BUNDLE_DIR/Contents/Info.plist"
else
  sed -i "s/0.1.0/$VERSION/g" "$BUNDLE_DIR/Contents/Info.plist"
fi

mkdir -p dist
cd "dist/$ARCH"
zip -r "../../dist/AdvancedClipboardManager-$ARCH.zip" "$APP_NAME.app"
cd ../..

echo "==> Done: dist/AdvancedClipboardManager-$ARCH.zip"
