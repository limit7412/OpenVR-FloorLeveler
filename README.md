# OpenVR-FloorLeveler

SteamVR (Lighthouse トラッキング環境) の床面の傾きを検出・補正するデスクトップツール。
仕様は [#1](https://github.com/limit7412/OpenVR-FloorLeveler/issues/1) を参照。

## 構成

Functional Core / Imperative Shell 構成を採用している。

- `src/FloorLeveler.Core` — 純粋関数のみのロジック層。平面フィット (PCA / RANSAC)、
  補正変換の算出 (モード A: 重力水平化 / モード B: 実測床面合わせ)、剛体変換
  (`HmdMatrix34_t` 相当との相互変換)、サンプル棄却判定、Chaperone 境界頂点の変換。
  OpenVR の型には依存しない。
- `src/FloorLeveler.OpenVr` — OpenVR interop 層。`openvr_api.dll` の FnTable を
  P/Invoke で呼び出す薄いラッパー (`OpenVrSession` / `VrSystem` / `ChaperoneTuner`)。
- `src/FloorLeveler.Poc` — M0 PoC コンソール。SteamVR 実機での S→R 行列の読み書き、
  微小回転の適用、符号規約の検証、スナップショットの保存/復元を行う。
- `src/FloorLeveler.App` — Avalonia UI のデスクトップ GUI (`FloorLeveler.exe`)。
  接続状態バー / サンプリング (手動・静置方式・連続方式) / 推定結果 /
  点群・床面の可視化 (俯瞰・側面ビュー) / 補正 / バックアップの縦積み構成 (仕様 §6)。
  自動サンプリングの判定ロジックは Core の `StillnessSampler` / `ContinuousSampler`
  に純粋関数として実装し、UI 側はタイマーでポーズを供給するだけ。可視化 (F-4) も
  Core の `FloorProjection` が 2D への投影を純粋関数で行い、`FloorPlotControl` は
  メートル座標をピクセルに写して描画するだけの薄い Shell。
  OpenVR へのアクセスは `ISessionGateway` の背後に隠し、UI ロジックを実機なしで
  テスト可能にしている。設定 (F-7)・スナップショットのバックアップ/復元 (F-6)・
  ローテーション付きログ (NF-4) を `%LOCALAPPDATA%\FloorLeveler` 配下に保存する。
  復元は「最新のバックアップを復元」(自動退避を除く最新へ) に加え、バックアップ
  一覧から任意時点を選んで復元できる (F-6)。
  キーボードショートカット (記録=Space、適用=Ctrl+Enter、元に戻す=Ctrl+Z)。
- `tests/FloorLeveler.Core.Tests` — Core の単体テスト (xUnit)。
- `tests/FloorLeveler.OpenVr.Tests` — interop 層の構造体レイアウト・変換テスト。
- `tests/FloorLeveler.App.Tests` — GUI の ViewModel テスト (fake gateway 使用)。

## M0 PoC の実行 (Windows + SteamVR)

```bash
dotnet build
# openvr_api.dll (SteamVR 同梱 bin\win64 または openvr リリース) を
# src/FloorLeveler.Poc/bin/Debug/net10.0/ に配置してから:
dotnet run --project src/FloorLeveler.Poc -- status
dotnet run --project src/FloorLeveler.Poc -- backup
dotnet run --project src/FloorLeveler.Poc -- tilt --roll 0.5   # プレビューのみ、Enter で revert
dotnet run --project src/FloorLeveler.Poc -- level --commit    # 重力水平化を Live へ反映
```

## 開発

.NET 10 SDK が必要。

```bash
dotnet build
dotnet test
```

## 単一 exe の発行 (仕様 §8)

```bash
# openvr_api.dll を exe に内包する場合 (仕様 §8.2)。省略も可 (下記参照)。
./scripts/fetch-openvr-dll.sh

dotnet publish src/FloorLeveler.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o publish
```

`publish/FloorLeveler.exe` が生成される。CI でもサイズ予算 (NF-2: 60 MB 以下) を
検証している。

### openvr_api.dll の扱い (仕様 §8.2)

実行時の解決順は次のとおり (`OpenVrNativeLibrary`)。

1. **既定の探索** — exe と同じディレクトリ、または OS の検索パス。ここで見つかれば
   そのまま使う (開発時や、任意のバージョンを差し替えたい場合に有効)
2. **exe への内包** — 見つからない場合、exe に内包した dll を
   `%LOCALAPPDATA%\FloorLeveler\native\{内容ハッシュ}\` へ展開してロードする
   (書き込みは `%LOCALAPPDATA%` 配下のみ: NF-3)。内容が同じなら再展開しない

内包は `native/win-x64/openvr_api.dll` が存在するときだけ行われる。`scripts/fetch-openvr-dll.sh`
がピン留めしたバージョン (既定 `v2.5.1`) を取得し SHA-256 を検証して配置する
(バイナリはリポジトリにコミットしない)。このファイルを置かずにビルドした場合は
内包されず、実行時は 1. の探索のみになる。リリース (§8.4) では必ず取得してから
発行するため、配布 exe は単体で動作する (NF-1)。

内包した `openvr_api.dll` は Valve Corporation の OpenVR SDK (BSD 3-Clause) であり、
バイナリ再配布にあたって著作権表示・ライセンス条件・免責条項の添付が必要になる。
全文は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にあり、リリース (§8.4) では
Release の資産としても添付し、リリースノートにも記載する。

`FloorLeveler.exe --version` はバージョンを出力して終了する (GUI を起動しない)。
発行時に `-p:Version=1.2.3` を渡すと、その値が exe のファイルプロパティ
(ProductVersion) と `--version` の出力になる。

## リリース (仕様 §8.4)

`v` から始まるタグを push すると `.github/workflows/release.yml` が動き、
タグ駆動でリリースが作られる。

```bash
git tag v0.1.0
git push origin v0.1.0
```

ワークフローは Windows ランナー上で テスト → 単一 exe の発行
(`-p:Version=` にタグの値を渡す) → サイズ予算の確認 → スモークテスト
(exe を起動し、`--version` が正常終了して**タグと同じバージョンを出力**すること、
ファイルプロパティのバージョンも一致することを確認) を行い、`FloorLeveler.exe` を
添付した GitHub Release を作成する。

対応するタグ形式は `vMAJOR.MINOR.PATCH[-プレリリース識別子]` (例: `v1.2.3`、
`v1.2.3-rc.1`)。プレリリース識別子を含むタグは prerelease として公開される。
ビルドメタデータ (`v1.2.3+build.1`) を含むタグは、埋め込まれるバージョンとタグが
食い違ったまま公開されるのを避けるため、ワークフローの冒頭で明示的に拒否する。
