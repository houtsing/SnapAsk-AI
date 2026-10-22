#!/bin/zsh
# Creates a self-signed "SnapAsk Local Signing" code-signing certificate in your login keychain.
# Optional, but recommended: with a stable signature, macOS keeps SnapAsk's Screen Recording and
# Accessibility permissions across rebuilds (ad-hoc signed builds lose them every time).
set -euo pipefail
if security find-identity -p codesigning | grep -q "SnapAsk Local Signing"; then
  echo "Certificate already exists."; exit 0
fi
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT
cat > "$TMP/c.cnf" <<'CNF'
[req]
distinguished_name=dn
x509_extensions=ext
prompt=no
[dn]
CN=SnapAsk Local Signing
[ext]
basicConstraints=critical,CA:false
keyUsage=critical,digitalSignature
extendedKeyUsage=critical,codeSigning
CNF
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$TMP/k.pem" -out "$TMP/c.pem" -days 3650 -config "$TMP/c.cnf" 2>/dev/null
PASS=$(openssl rand -hex 12)
openssl pkcs12 -export -legacy -inkey "$TMP/k.pem" -in "$TMP/c.pem" -out "$TMP/c.p12" -passout "pass:$PASS" -name "SnapAsk Local Signing"
security import "$TMP/c.p12" -k ~/Library/Keychains/login.keychain-db -P "$PASS" -T /usr/bin/codesign
echo "Created 'SnapAsk Local Signing'. Now run ./build.sh"
