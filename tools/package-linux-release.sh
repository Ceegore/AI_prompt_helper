#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <version> <publish-dir> <output-dir>" >&2
  exit 2
fi

version="$1"
publish_dir="$(realpath "$2")"
output_dir="$3"
repo_root="$(cd "$(dirname "$0")/.." && pwd)"

mkdir -p "$output_dir"
output_dir="$(realpath "$output_dir")"

app="$publish_dir/PromptHelper"
license="$publish_dir/LICENSE"
notices="$publish_dir/THIRD_PARTY_NOTICES.md"
checksums="$publish_dir/SHA256SUMS.txt"
sbom="$publish_dir/_manifest/spdx_2.2/manifest.spdx.json"

for required in "$app" "$license" "$notices" "$checksums" "$sbom"; do
  if [[ ! -e "$required" ]]; then
    echo "Required Linux release payload is missing: $required" >&2
    exit 1
  fi
done

if [[ ! -x "$app" ]]; then
  echo "PromptHelper is not executable: $app" >&2
  exit 1
fi

tar_name="PromptHelper-v${version}-linux-x64.tar.gz"
tar_path="$output_dir/$tar_name"
deb_name="prompthelper_${version}_amd64.deb"
deb_path="$output_dir/$deb_name"

tar -C "$publish_dir" -czf "$tar_path" .

tar_listing="$(tar -tzf "$tar_path")"
for required_entry in   "./PromptHelper"   "./LICENSE"   "./THIRD_PARTY_NOTICES.md"   "./SHA256SUMS.txt"   "./_manifest/spdx_2.2/manifest.spdx.json"; do
  if ! grep -Fxq "$required_entry" <<<"$tar_listing"; then
    echo "Portable Linux archive is missing $required_entry" >&2
    exit 1
  fi
done

package_root="$(mktemp -d)"
trap 'rm -rf "$package_root"' EXIT

mkdir -p   "$package_root/DEBIAN"   "$package_root/opt/prompthelper"   "$package_root/usr/bin"   "$package_root/usr/share/applications"   "$package_root/usr/share/icons/hicolor/scalable/apps"   "$package_root/usr/share/doc/prompthelper"

cp -a "$publish_dir/." "$package_root/opt/prompthelper/"
cp "$repo_root/packaging/linux/prompthelper.desktop"   "$package_root/usr/share/applications/prompthelper.desktop"
cp "$repo_root/src/PromptHelper/Assets/PromptHelperLogo.svg"   "$package_root/usr/share/icons/hicolor/scalable/apps/prompthelper.svg"
cp "$repo_root/LICENSE"   "$package_root/usr/share/doc/prompthelper/copyright"

cat > "$package_root/usr/bin/prompthelper" <<'EOF'
#!/bin/sh
exec /opt/prompthelper/PromptHelper "$@"
EOF
chmod 0755 "$package_root/usr/bin/prompthelper"
chmod 0755 "$package_root/opt/prompthelper/PromptHelper"

installed_size="$(du -sk "$package_root/opt/prompthelper" | cut -f1)"

cat > "$package_root/DEBIAN/control" <<EOF
Package: prompthelper
Version: ${version}
Section: utils
Priority: optional
Architecture: amd64
Installed-Size: ${installed_size}
Depends: libx11-6, libice6, libsm6, libfontconfig1, ca-certificates, tzdata, libc6, libgcc1 | libgcc-s1, libgssapi-krb5-2, libstdc++6, zlib1g, libssl1.0.0 | libssl1.0.2 | libssl1.1 | libssl3, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu69 | libicu68 | libicu67 | libicu66 | libicu65 | libicu63 | libicu60 | libicu57 | libicu55 | libicu52
Maintainer: Ceegore
Homepage: https://github.com/Ceegore/AI_prompt_helper
Description: Local desktop organizer for reusable AI prompts
 Prompt Helper stores prompt libraries locally and copies prompt text to the
 desktop clipboard. This package contains the self-contained Linux x64 build.
EOF

dpkg-deb --root-owner-group --build "$package_root" "$deb_path" >/dev/null

dpkg-deb --info "$deb_path" >/dev/null
dpkg-deb --contents "$deb_path" >/dev/null

extract_root="$(mktemp -d)"
dpkg-deb -x "$deb_path" "$extract_root"
test -x "$extract_root/opt/prompthelper/PromptHelper"
test -x "$extract_root/usr/bin/prompthelper"
test -f "$extract_root/usr/share/applications/prompthelper.desktop"
test -f "$extract_root/usr/share/icons/hicolor/scalable/apps/prompthelper.svg"
rm -rf "$extract_root"

tar_hash="$(sha256sum "$tar_path" | cut -d' ' -f1)"
deb_hash="$(sha256sum "$deb_path" | cut -d' ' -f1)"
printf '%s  %s\n' "$tar_hash" "$tar_name" > "$tar_path.sha256"
printf '%s  %s\n' "$deb_hash" "$deb_name" > "$deb_path.sha256"

printf '%s\n' "$tar_path" "$tar_path.sha256" "$deb_path" "$deb_path.sha256"
