#!/bin/bash
# Builds Caffeine Bar.app via an Xcode project (xcodegen + xcodebuild) so the app gets
# a real code signature. On macOS 26, ad-hoc-signed apps get silently denied a real
# menu bar status-item slot even though they launch and run fine.
# Pass --install to copy the result into /Applications.
set -euo pipefail

cd "$(dirname "$0")"

if ! command -v xcodegen >/dev/null; then
	echo "xcodegen is required: brew install xcodegen" >&2
	exit 1
fi

APP_NAME="Caffeine Bar"
DERIVED_DATA=".build/xcode"
APP="$DERIVED_DATA/Build/Products/Release/$APP_NAME.app"

xcodegen generate
xcodebuild -project CaffeineBar.xcodeproj -scheme CaffeineBar -configuration Release -derivedDataPath "$DERIVED_DATA" build

# Re-sign with a real Apple Development identity. xcodebuild already signs the
# bundle, but an ad-hoc signature is silently denied a menu bar status item on
# macOS 26, so prefer a genuine identity whenever one is available.
SIGN_IDENTITY="$(security find-identity -v -p codesigning | grep -m1 'Apple Development' | sed -E 's/^[[:space:]]*[0-9]+\) ([A-F0-9]+) .*/\1/')"
ENTITLEMENTS="$DERIVED_DATA/Build/Intermediates.noindex/CaffeineBar.build/Release/CaffeineBar.build/$APP_NAME.app.xcent"
if [ -n "$SIGN_IDENTITY" ] && [ -f "$ENTITLEMENTS" ]; then
	codesign --force --sign "$SIGN_IDENTITY" --entitlements "$ENTITLEMENTS" --timestamp=none "$APP"
else
	codesign --force --sign - "$APP"
	echo "Warning: no Apple Development identity found; ad-hoc signing may not get a menu bar icon on macOS 26+." >&2
fi

echo "Built $PWD/$APP"

if [[ "${1:-}" == "--install" ]]; then
	rm -rf "/Applications/$APP_NAME.app"
	cp -R "$APP" "/Applications/$APP_NAME.app"

	# Ensure no custom Finder icon override exists
	swift - <<EOF
import AppKit
let appPath = "/Applications/$APP_NAME.app"
_ = NSWorkspace.shared.setIcon(nil, forFile: appPath, options: [])
EOF

	/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "/Applications/$APP_NAME.app"
	touch "/Applications/$APP_NAME.app"

	echo "Installed /Applications/$APP_NAME.app"
fi
