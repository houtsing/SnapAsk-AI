#!/bin/zsh
# Removes SnapAsk and everything it stores (including ChatGPT/Gemini sign-ins made inside SnapAsk).
pkill -x SnapAsk 2>/dev/null || true
rm -rf ~/Applications/SnapAsk.app \
       ~/Library/Caches/SnapAsk ~/Library/Caches/com.snapask.app \
       ~/Library/WebKit/com.snapask.app ~/Library/HTTPStorages/com.snapask.app* \
       ~/Library/Logs/SnapAsk.log
defaults delete com.snapask.app 2>/dev/null || true
tccutil reset ScreenCapture com.snapask.app >/dev/null 2>&1 || true
tccutil reset Accessibility com.snapask.app >/dev/null 2>&1 || true
echo "SnapAsk removed."
echo "Optional: delete the signing certificate with:"
echo "  security delete-identity -c 'SnapAsk Local Signing' ~/Library/Keychains/login.keychain-db"
