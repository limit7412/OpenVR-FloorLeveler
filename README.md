# OpenVR-FloorLeveler

SteamVR (Lighthouse トラッキング環境) の床面の傾きを検出・補正するデスクトップツール。
仕様は [#1](https://github.com/limit7412/OpenVR-FloorLeveler/issues/1) を参照。

## 構成

Functional Core / Imperative Shell 構成を採用している。

- `src/FloorLeveler.Core` — 純粋関数のみのロジック層。平面フィット (PCA / RANSAC)、
  補正変換の算出 (モード A: 重力水平化 / モード B: 実測床面合わせ)、剛体変換
  (`HmdMatrix34_t` 相当との相互変換)、サンプル棄却判定、Chaperone 境界頂点の変換。
  OpenVR の型には依存しない。
- `tests/FloorLeveler.Core.Tests` — Core の単体テスト (xUnit)。

OpenVR interop (`FloorLeveler.OpenVr`) と GUI (`FloorLeveler.App`) は今後追加する。

## 開発

.NET 10 SDK が必要。

```bash
dotnet build
dotnet test
```
