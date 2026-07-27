#!/usr/bin/env bash
# openvr_api.dll を取得して native/win-x64/ に配置する (仕様 §8.2 の exe 内包用)。
#
# ここに置かれた dll があるときだけ FloorLeveler.OpenVr が埋め込みリソースとして
# 取り込む。無い場合は従来どおり「exe と同じディレクトリ / OS の検索パス」から
# 読み込む動作になる (ビルドは通る)。
#
# バージョンと SHA-256 はピン留めし、取得したファイルを必ず検証する。
set -euo pipefail

# 差し替えたい場合は環境変数で上書きする (SHA-256 も併せて更新すること)。
VERSION="${OPENVR_VERSION:-v2.5.1}"
EXPECTED_SHA256="${OPENVR_DLL_SHA256:-54ad7fe4cdb4d88fa818dbf8eb3d1f8ca2ae3b34d2ff115f87191d7d88e0a009}"

url="https://raw.githubusercontent.com/ValveSoftware/openvr/${VERSION}/bin/win64/openvr_api.dll"
dest_dir="$(cd "$(dirname "$0")/.." && pwd)/native/win-x64"
dest="${dest_dir}/openvr_api.dll"

verify() {
  local file="$1"
  local actual
  actual="$(sha256sum "$file" | cut -d' ' -f1)"
  if [ "$actual" != "$EXPECTED_SHA256" ]; then
    echo "openvr_api.dll のチェックサムが一致しません" >&2
    echo "  expected: $EXPECTED_SHA256" >&2
    echo "  actual:   $actual" >&2
    return 1
  fi
}

# 取得済みで検証も通るならそのまま使う。
if [ -f "$dest" ] && verify "$dest" 2>/dev/null; then
  echo "openvr_api.dll は取得済みです ($VERSION): $dest"
  exit 0
fi

mkdir -p "$dest_dir"
tmp="${dest}.download.$$"
trap 'rm -f "$tmp"' EXIT

echo "openvr_api.dll を取得します ($VERSION)"
curl -sSL --fail --retry 3 --retry-delay 2 -o "$tmp" "$url"
verify "$tmp"
mv -f "$tmp" "$dest"
echo "配置しました: $dest"
