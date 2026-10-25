#!/bin/zsh
# Builds a universal (Apple silicon + Intel) Release Apmuly.app and packages it as a DMG in dist/.
#
# Default: ad-hoc signed — works for anyone; downloaders approve it once in System Settings.
# With an Apple Developer ID (removes that step):
#   SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" TEAM_ID=TEAMID \
#   NOTARY_PROFILE=apmuly-notary scripts/build-release.sh
# (create the notary profile once with: xcrun notarytool store-credentials apmuly-notary)
set -euo pipefail
cd "$(dirname "$0")/.."

SIGN_IDENTITY="${SIGN_IDENTITY:--}"
TEAM_ID="${TEAM_ID:-}"
NOTARY_PROFILE="${NOTARY_PROFILE:-}"
DERIVED=build/release

command -v xcodegen >/dev/null || { echo "XcodeGen is required: brew install xcodegen" >&2; exit 1; }
if [[ -n "$NOTARY_PROFILE" && "$SIGN_IDENTITY" == "-" ]]; then
  echo "Notarization needs a Developer ID: set SIGN_IDENTITY and TEAM_ID." >&2
  exit 1
fi

echo "==> Generating Xcode project"
xcodegen generate --quiet

echo "==> Building Release (universal, signing: $SIGN_IDENTITY)"
build_args=(
  -project Apmuly.xcodeproj -scheme Apmuly -configuration Release -derivedDataPath "$DERIVED"
  ARCHS="arm64 x86_64" ONLY_ACTIVE_ARCH=NO CODE_SIGN_IDENTITY="$SIGN_IDENTITY"
)
if [[ -n "$TEAM_ID" ]]; then
  build_args+=(DEVELOPMENT_TEAM="$TEAM_ID" OTHER_CODE_SIGN_FLAGS="--timestamp")
else
  build_args+=(DEVELOPMENT_TEAM="" OTHER_CODE_SIGN_FLAGS="--timestamp=none")
fi
xcodebuild "${build_args[@]}" clean build -quiet

APP="$DERIVED/Build/Products/Release/Apmuly.app"
echo "==> Verifying signature"
codesign --verify --deep --strict "$APP"
lipo -info "$APP/Contents/MacOS/Apmuly"

VERSION=$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$APP/Contents/Info.plist")
DMG="dist/Apmuly-$VERSION.dmg"
echo "==> Packaging $DMG"
mkdir -p dist
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
ditto "$APP" "$STAGE/Apmuly.app"
ln -s /Applications "$STAGE/Applications"
rm -f "$DMG"
hdiutil create -volname "Apmuly $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null

if [[ -n "$NOTARY_PROFILE" ]]; then
  echo "==> Notarizing"
  codesign --sign "$SIGN_IDENTITY" --timestamp "$DMG"
  xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$DMG"
fi

# Name only (no dist/ prefix), so `shasum -c` works next to the downloaded DMG.
(cd dist && shasum -a 256 "${DMG:t}" | tee "${DMG:t}.sha256")
echo "==> Done: $DMG"
