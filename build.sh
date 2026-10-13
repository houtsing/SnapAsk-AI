#!/bin/zsh
# Builds SnapAsk.app and installs it to ~/Applications.
set -euo pipefail
cd "$(dirname "$0")"

APP="build/SnapAsk.app"
rm -rf build && mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

swiftc -O -swift-version 5 -target arm64-apple-macos14.0 \
  -framework Cocoa -framework SwiftUI -framework Carbon -framework ServiceManagement \
  SnapAsk.swift -o "$APP/Contents/MacOS/SnapAsk"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>com.snapask.app</string>
  <key>CFBundleName</key><string>SnapAsk</string>
  <key>CFBundleExecutable</key><string>SnapAsk</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>NSAppleEventsUsageDescription</key><string>SnapAsk opens Terminal so you can sign in to Claude, ChatGPT or Gemini.</string>
</dict></plist>
PLIST

# Stable local identity so macOS permissions survive rebuilds (falls back to ad-hoc).
ID=$(security find-identity -p codesigning | awk '/SnapAsk Local Signing/ {print $2; exit}')
codesign --force --sign "${ID:--}" "$APP"

mkdir -p ~/Applications
pkill -x SnapAsk 2>/dev/null || true
rm -rf ~/Applications/SnapAsk.app
cp -R "$APP" ~/Applications/
echo "Installed ~/Applications/SnapAsk.app"
