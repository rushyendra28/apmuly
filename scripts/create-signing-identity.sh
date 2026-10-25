#!/bin/zsh
# Optional, for people who build Apmuly themselves and rebuild often.
#
# macOS remembers privacy permissions (control Music, audio capture, speech) per code signature.
# Ad-hoc builds get a new signature every build, so macOS asks again each time. This creates a
# stable self-signed code-signing identity, "Apmuly Local Signing", in your login keychain.
# Afterwards: cp Config/Local.xcconfig.example Config/Local.xcconfig, uncomment the
# CODE_SIGN_IDENTITY line, and run `make project`.
#
# macOS will ask you to approve trusting the certificate for code signing.
set -euo pipefail

NAME="Apmuly Local Signing"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"

if security find-identity -v -p codesigning | grep -q "\"$NAME\""; then
  echo "\"$NAME\" already exists."
  exit 0
fi

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
cat > "$WORK/cert.cnf" <<EOF
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = $NAME
[ext]
basicConstraints = critical,CA:false
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
EOF

# The system LibreSSL writes PKCS#12 bundles macOS can import (OpenSSL 3 defaults can't be).
/usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -config "$WORK/cert.cnf" \
  -keyout "$WORK/key.pem" -out "$WORK/cert.pem" 2>/dev/null
PASSWORD=$(/usr/bin/openssl rand -hex 16)
/usr/bin/openssl pkcs12 -export -inkey "$WORK/key.pem" -in "$WORK/cert.pem" -name "$NAME" \
  -out "$WORK/identity.p12" -passout "pass:$PASSWORD"
security import "$WORK/identity.p12" -k "$KEYCHAIN" -P "$PASSWORD" -T /usr/bin/codesign

echo "Approve the macOS dialog to trust the certificate for code signing…"
security add-trusted-cert -r trustRoot -p codeSign -k "$KEYCHAIN" "$WORK/cert.pem"
security find-identity -v -p codesigning | grep "\"$NAME\""
echo "Created \"$NAME\"."
