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
- `tests/FloorLeveler.Core.Tests` — Core の単体テスト (xUnit)。
- `tests/FloorLeveler.OpenVr.Tests` — interop 層の構造体レイアウト・変換テスト。

GUI (`FloorLeveler.App`) は M2 で追加する。

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
