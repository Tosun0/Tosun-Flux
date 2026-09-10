#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INPUT_ROOT="${1:-$PROJECT_ROOT/packaged/Mac/input}"
OUTPUT_ROOT="$PROJECT_ROOT/packaged/Mac"
ARM_APP="$(find "$INPUT_ROOT/arm64" -maxdepth 2 -type d -name '*.app' -print -quit)"
X64_APP="$(find "$INPUT_ROOT/x64" -maxdepth 2 -type d -name '*.app' -print -quit)"

[[ -n "$ARM_APP" && -n "$X64_APP" ]] || {
  echo "arm64와 x64 앱 번들이 모두 필요합니다: $INPUT_ROOT" >&2
  exit 1
}
command -v lipo >/dev/null 2>&1 || { echo "lipo를 찾을 수 없습니다." >&2; exit 1; }

UNIVERSAL_APP="$OUTPUT_ROOT/Tosun Flux-universal.app"
rm -rf "$UNIVERSAL_APP"
mkdir -p "$UNIVERSAL_APP/Contents/MacOS" "$UNIVERSAL_APP/Contents/Resources"
cp -R "$ARM_APP/Contents/Resources/." "$UNIVERSAL_APP/Contents/Resources/"
cp "$ARM_APP/Contents/Info.plist" "$UNIVERSAL_APP/Contents/Info.plist"

merge_tree() {
  local arm_root="$1"
  local x64_root="$2"
  local output_root="$3"
  mkdir -p "$output_root"
  cp -R "$arm_root/." "$output_root/"

  while IFS= read -r -d '' x64_file; do
    local relative="${x64_file#"$x64_root/"}"
    local arm_file="$output_root/$relative"
    if [[ -f "$arm_file" ]] && file "$arm_file" | grep -q 'Mach-O' && file "$x64_file" | grep -q 'Mach-O'; then
      local temporary="$arm_file.universal"
      lipo -create "$arm_file" "$x64_file" -output "$temporary"
      mv "$temporary" "$arm_file"
    elif [[ ! -e "$arm_file" ]]; then
      mkdir -p "$(dirname "$arm_file")"
      cp -R "$x64_file" "$arm_file"
    fi
  done < <(find "$x64_root" -type f -print0)
}

merge_tree "$ARM_APP/Contents/MacOS" "$X64_APP/Contents/MacOS" "$UNIVERSAL_APP/Contents/MacOS"

APP_EXECUTABLE="$UNIVERSAL_APP/Contents/MacOS/Tosun Flux"
BACKEND_EXECUTABLE="$UNIVERSAL_APP/Contents/MacOS/backend/TosunFluxBackend/TosunFluxBackend"
lipo -info "$APP_EXECUTABLE" | grep -q 'arm64' || { echo '앱 실행 파일에 arm64 아키텍처가 없습니다.' >&2; exit 1; }
lipo -info "$APP_EXECUTABLE" | grep -q 'x86_64' || { echo '앱 실행 파일에 x86_64 아키텍처가 없습니다.' >&2; exit 1; }
lipo -info "$BACKEND_EXECUTABLE" | grep -q 'arm64' || { echo '백엔드에 arm64 아키텍처가 없습니다.' >&2; exit 1; }
lipo -info "$BACKEND_EXECUTABLE" | grep -q 'x86_64' || { echo '백엔드에 x86_64 아키텍처가 없습니다.' >&2; exit 1; }

ditto -c -k --sequesterRsrc --keepParent "$UNIVERSAL_APP" "$OUTPUT_ROOT/Tosun Flux-universal.zip"
if command -v hdiutil >/dev/null 2>&1; then
  hdiutil create -volname "Tosun Flux" -srcfolder "$UNIVERSAL_APP" -ov -format UDZO "$OUTPUT_ROOT/Tosun Flux-universal.dmg" >/dev/null
fi

echo "Universal Mac package created: $UNIVERSAL_APP"
