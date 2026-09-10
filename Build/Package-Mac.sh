#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="1.1.0"
PYTHON_BIN="${TOSUN_PYTHON:-python3}"
FFMPEG_BIN="${TOSUN_FFMPEG:-ffmpeg}"
PDFTOPPM_BIN="${TOSUN_PDFTOPPM:-pdftoppm}"
if [[ -n "${TOSUN_MAC_RUNTIMES:-}" ]]; then
  RUNTIMES_STRING="$TOSUN_MAC_RUNTIMES"
elif [[ "$(uname -m)" == "arm64" ]]; then
  RUNTIMES_STRING="osx-arm64"
else
  RUNTIMES_STRING="osx-x64"
fi

resolve_command() {
  local value="$1"
  if [[ "$value" == */* ]]; then
    [[ -x "$value" ]] || { echo "실행 파일을 찾을 수 없습니다: $value" >&2; exit 1; }
    printf '%s\n' "$value"
    return
  fi
  command -v "$value" || { echo "명령을 찾을 수 없습니다: $value" >&2; exit 1; }
}

PYTHON_BIN="$(resolve_command "$PYTHON_BIN")"
FFMPEG_BIN="$(resolve_command "$FFMPEG_BIN")"
PDFTOPPM_BIN="$(resolve_command "$PDFTOPPM_BIN")"
DOTNET_BIN="$(resolve_command "${DOTNET:-dotnet}")"

OUTPUT_ROOT="$PROJECT_ROOT/packaged/Mac"
INTERMEDIATE_ROOT="$PROJECT_ROOT/Build/Intermediate/TosunFluxMac"
PROJECT="$PROJECT_ROOT/Source/TosunFluxMac/TosunFluxMac.csproj"
BACKEND_ENTRY="$PROJECT_ROOT/Source/TosunFluxBackend/TosunFluxBackend.py"

rm -rf "$OUTPUT_ROOT" "$INTERMEDIATE_ROOT"
mkdir -p "$OUTPUT_ROOT" "$INTERMEDIATE_ROOT"

read -r -a RUNTIMES <<< "$RUNTIMES_STRING"
for runtime in "${RUNTIMES[@]}"; do
  publish_dir="$INTERMEDIATE_ROOT/publish/$runtime"
  backend_dist="$INTERMEDIATE_ROOT/backend/$runtime"
  backend_build="$INTERMEDIATE_ROOT/backend-build/$runtime"
  bundle="$OUTPUT_ROOT/Tosun Flux-$runtime.app"

  mkdir -p "$publish_dir" "$backend_dist" "$backend_build"
  "$DOTNET_BIN" publish "$PROJECT" \
    -c Release \
    -r "$runtime" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:DebugType=None \
    -o "$publish_dir"

  "$PYTHON_BIN" -m PyInstaller \
    --noconfirm \
    --clean \
    --onedir \
    --console \
    --name TosunFluxBackend \
    --distpath "$backend_dist" \
    --workpath "$backend_build" \
    --specpath "$backend_build" \
    --exclude-module numpy \
    --exclude-module scipy \
    --add-binary "$FFMPEG_BIN:vendor" \
    --add-binary "$PDFTOPPM_BIN:vendor" \
    "$BACKEND_ENTRY"

  app_host="$(find "$publish_dir" -maxdepth 1 -type f -perm -111 ! -name '*.dll' | head -n 1)"
  [[ -n "$app_host" ]] || { echo "macOS apphost를 찾을 수 없습니다: $publish_dir" >&2; exit 1; }
  app_executable="$(basename "$app_host")"

  mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
  cp -R "$publish_dir/." "$bundle/Contents/MacOS/"
  mkdir -p "$bundle/Contents/MacOS/backend"
  cp -R "$backend_dist/TosunFluxBackend" "$bundle/Contents/MacOS/backend/"
  chmod +x "$bundle/Contents/MacOS/$app_executable" "$bundle/Contents/MacOS/backend/TosunFluxBackend/TosunFluxBackend"

  if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
    iconset="$INTERMEDIATE_ROOT/TosunFlux.iconset"
    rm -rf "$iconset"
    mkdir -p "$iconset"
    for size in 16 32 128 256 512; do
      sips -z "$size" "$size" "$PROJECT_ROOT/Content/TosunFlux/tosun-floating-v1.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
      retina_size=$((size * 2))
      sips -z "$retina_size" "$retina_size" "$PROJECT_ROOT/Content/TosunFlux/tosun-floating-v1.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
    done
    iconutil -c icns "$iconset" -o "$bundle/Contents/Resources/TosunFlux.icns"
  fi

  cat > "$bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key>
  <string>Tosun Flux</string>
  <key>CFBundleExecutable</key>
  <string>$app_executable</string>
  <key>CFBundleIdentifier</key>
  <string>com.tosunstudio.tosunflux</string>
  <key>CFBundleName</key>
  <string>Tosun Flux</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundleVersion</key>
  <string>$VERSION</string>
  <key>CFBundleIconFile</key>
  <string>TosunFlux.icns</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
</dict>
</plist>
PLIST

  ditto -c -k --sequesterRsrc --keepParent "$bundle" "$OUTPUT_ROOT/Tosun Flux-$runtime.zip"
  if command -v hdiutil >/dev/null 2>&1; then
    hdiutil create -volname "Tosun Flux" -srcfolder "$bundle" -ov -format UDZO "$OUTPUT_ROOT/Tosun Flux-$runtime.dmg" >/dev/null
  fi
done

echo "Mac packages created: $OUTPUT_ROOT"
