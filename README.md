# OpenVR-FloorLeveler

SteamVR (Lighthouse トラッキング環境) の床面の傾きを検出・補正するデスクトップツール。

ルームセットアップ後に「床がわずかに傾いて感じる」「トラッカーを床に置くと沈む/浮く」
といった症状を、数分で解消することを目的としている。

OpenVR Advanced Settings の "Fix Floor" が高さ (Y オフセット) のみを補正するのに対し、
本ツールは **roll / pitch の回転成分を含めた床面姿勢**を補正する。

補正は `IVRChaperoneSetup` を通じて standing universe → raw tracking universe の変換行列
(`StandingZeroPoseToRawTrackingPose`) を書き換えることで行う。standing / seated 座標を
基準とするアプリケーション (VRChat を含む一般的な VR アプリ) には、個別の設定なしで
透過的に反映される。

一方、raw tracking universe (`RawAndUncalibrated`) を直接使うアプリはこの行列を経由
しないため、補正の対象外になる。

> **状態**: 実装は一通り完了しているが、**SteamVR 実機での検証は未実施**。
> 補正は Chaperone 設定を書き換えるため、はじめて使う場合は必ずバックアップ
> (自動で取得される) と「元に戻す」の動作を確認してから本適用すること。

## 動作環境

- Windows 10 22H2 以降 / Windows 11 (x64)
- SteamVR がインストール済みかつ起動中
- Lighthouse トラッキングのデバイスが 1 台以上接続されていること
  (コントローラーまたは Vive Tracker 系)

床面のサンプリングには、床に接地させられるトラッキングデバイスが必要。
コントローラー 1 本を床上で動かして複数点を取る方式と、複数デバイスを同時に
床置きする方式のどちらにも対応している。

## インストール

[Releases](https://github.com/limit7412/OpenVR-FloorLeveler/releases) から
`FloorLeveler.exe` をダウンロードして実行するだけ。インストーラーもランタイムの
事前導入も不要 (.NET ランタイムは exe に同梱)。

`openvr_api.dll` は SteamVR に付属するものを読み込むため、別途用意する必要はない。

## 使い方

1. **接続** — SteamVR を起動した状態で `FloorLeveler.exe` を実行し、「接続 / 再試行」を押す
   - 接続時に、現在の Chaperone 設定が自動でバックアップされる。ただしこれは
     ベストエフォートで、保存に失敗しても接続は続行し、警告も出ない。
     確実に退避しておきたい場合は「現在の設定を退避」を押し、
     バックアップ一覧に項目が増えたことを確認すること
2. **デバイス選択** — サンプリングに使うコントローラー / トラッカーを選ぶ
3. **サンプリング** — 床の複数点を記録する。方式は 3 つ

   | 方式 | 操作 |
   | --- | --- |
   | 手動 | デバイスを床に置いて「記録」(または Space) を押す |
   | 静置方式 | 床に置いて静止させると自動で記録される |
   | 連続方式 | 床に接地させたまま引きずると、前回の記録点から**約 5 cm 動くごとに**自動記録される |

   連続方式では、持ち上げた瞬間など**速度が閾値を超えたフレーム (1 m/s 超) は自動で
   棄却される**。手動方式にはこの判定が無いため、記録操作をした瞬間の位置がそのまま
   入る (床に着いた状態で押すこと)。

   > **制約**: 現状はデバイス原点をそのまま記録する。デバイス種別ごとの接地オフセットは
   > 枠組みだけがあり、内蔵プロファイルの値はすべて 0 のため補正されない。
   > デバイスの姿勢が点ごとに違うと、推定される高さや傾きもその分ずれる。
   > **記録時はデバイスの向きを揃える**と誤差を抑えられる。

4. **推定結果の確認** — 傾き角・方位・フィット残差と、点群/床面の俯瞰・側面ビューを確認する
   - 有効サンプルが **3 点以上**かつ水平方向の**広がりが 30 cm 以上**になるまで、
     精度不足の旨が表示される
   - 残差が大きい場合は「RANSAC で外れ値除去」を有効にする (既定の閾値 3 mm)

5. **補正モードを選ぶ**

   | モード | 用途 | サンプリング |
   | --- | --- | --- |
   | **A: 重力水平化** | ルームセットアップの誤差で仮想床が傾いた場合。仮想床を重力水平に戻す | 不要 (取得済みなら高さ合わせに使う) |
   | **B: 実測床面合わせ** | 物理床自体が傾いている環境で、床に置いたものの接地感を正しくしたい場合 | 必須 |

6. **プレビュー → 適用** — 「プレビュー」で working copy に反映して確認し、
   問題なければ「適用」(Ctrl+Enter) で Live へ反映する
   - 補正量が微小 (回転 0.05° 未満かつ並進 1 mm 未満) の場合は「補正不要」として適用を抑止する
   - 回転が 10° を超える場合は誤サンプリングの可能性が高いため、確認チェックを要求する
   - 適用の直前にもバックアップが取られる

7. **元に戻す** — 「元に戻す」(Ctrl+Z) で直前の適用を打ち消す。
   アプリを再起動した後でも、バックアップ一覧から任意の時点へ復元できる

Chaperone の境界 (コリジョン境界) は standing 座標で保存されているため、補正時に
逆変換して書き戻す。物理的な壁の位置に対して境界がずれることはない。

### キーボードショートカット

| 操作 | キー |
| --- | --- |
| 記録 | `Space` |
| 適用 | `Ctrl+Enter` |
| 元に戻す | `Ctrl+Z` |

### 言語

日本語が既定。画面上部の切替で英語にできる。選択は保存され、次回起動時も維持される。
切替に再起動は不要で、表示中のメッセージもその場で書き換わる。

## バックアップと復旧

補正の適用前・接続時に、Chaperone のスナップショット (S→R 行列 / seated / 境界頂点) が
自動で保存される。

- 保存先: `%LOCALAPPDATA%\FloorLeveler\backups\`
- 「最新のバックアップを復元」— 接続時の自動退避を除いた最新へ戻す
- バックアップ一覧から任意の時点を選んで復元することもできる

書き込みは `%LOCALAPPDATA%\FloorLeveler\` 配下のみで、管理者権限は不要。
設定は `settings.json`、ログは `logs\` に出力される (ローテーションあり)。
適用操作は変更前後の行列値をログに記録する。

## 既知の制約

- モード B は standing universe を重力非整列にするため、スカイボックスや一部アプリの
  水平表現に違和感が生じうる。**数度以内の補正を想定範囲**とする
- SteamVR 側でルームセットアップを再実行すると本ツールの補正は上書きされる
  (正常動作。再補正が必要になる)
- `IVRChaperoneSetup` は Valve のバージョン間で挙動差が報告されているため、
  SteamVR のメジャーアップデート後は動作を確認すること
- UI は通常のデスクトップウィンドウのみで、VR 内オーバーレイは提供しない。
  HMD 装着中は SteamVR のデスクトップビュー経由での操作を想定している
- raw tracking universe を直接使うアプリには補正が反映されない (上記のとおり)
- デバイスの接地オフセットが未実装で、デバイス原点をそのまま記録する
- 接続時の自動バックアップは失敗しても通知されない (ベストエフォート)

## ライセンス

[MIT License](LICENSE)

`openvr_api.dll` は同梱・再配布しておらず、実行時に SteamVR 付属のものを読み込む。

---

# 開発

.NET 10 SDK が必要。

```bash
dotnet build
dotnet test
```

## 構成

Functional Core / Imperative Shell 構成を採用している。

- `src/FloorLeveler.Core` — 純粋関数のみのロジック層。平面フィット (PCA / RANSAC)、
  補正変換の算出 (モード A / B)、剛体変換 (`HmdMatrix34_t` 相当との相互変換)、
  サンプル棄却判定、Chaperone 境界頂点の変換、2D 投影。OpenVR の型には依存しない
- `src/FloorLeveler.OpenVr` — OpenVR interop 層。`openvr_api.dll` の FnTable を
  P/Invoke で呼び出す薄いラッパー (`OpenVrSession` / `VrSystem` / `ChaperoneTuner`)
- `src/FloorLeveler.Poc` — PoC コンソール。実機での S→R 行列の読み書き、微小回転の適用、
  符号規約の検証、スナップショットの保存/復元を行う
- `src/FloorLeveler.App` — Avalonia UI のデスクトップ GUI (`FloorLeveler.exe`)。
  OpenVR へのアクセスは `ISessionGateway` の背後に隠し、UI ロジックを実機なしで
  テストできるようにしている
- `tests/` — Core / interop / ViewModel の単体テスト (xUnit)

自動サンプリングの判定も可視化の投影も Core に純粋関数として置き、Shell 側は
タイマーでポーズを供給する / メートル座標をピクセルに写して描くだけに留めている。

## PoC コンソールの実行 (Windows + SteamVR)

符号規約の検証や、GUI を介さない読み書きの確認に使う。

```bash
dotnet run --project src/FloorLeveler.Poc -- status
dotnet run --project src/FloorLeveler.Poc -- backup
dotnet run --project src/FloorLeveler.Poc -- tilt --roll 0.5   # プレビューのみ、Enter で revert
dotnet run --project src/FloorLeveler.Poc -- level --commit    # 重力水平化を Live へ反映
```

`--commit` を付けない場合、変更は working copy とプレビュー表示のみに留まる。

## UI 文字列の追加

UI 文字列は `src/FloorLeveler.App/Localization/` に言語ごとの resx として置き、
`Strings` クラスが「メンバー名 = リソースキー」の型付きアクセサを提供する。XAML は
`{Binding L.ApplyButton}` のように束縛するため、キーの綴り誤りはコンパイル済み
バインディングによりビルド時に落ちる。

文言を追加するときは 2 つの resx と `Strings` のメンバーを揃えて足すこと
(片方だけだと、日英のキー集合が一致することを検証しているテストが落ちる)。

## 単一 exe の発行

```bash
dotnet publish src/FloorLeveler.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o publish
```

`publish/FloorLeveler.exe` (約 48 MB) が生成される。CI でもサイズ予算 (60 MB 以下) を
検証している。

`FloorLeveler.exe --version` はバージョンを出力して終了する (GUI を起動しない)。
発行時に `-p:Version=1.2.3` を渡すと、その値が exe のファイルプロパティ
(ProductVersion) と `--version` の出力になる。

### openvr_api.dll の解決順

1. **既定の探索** — exe と同じディレクトリ、または OS の検索パス。
   任意のバージョンを使いたい場合は exe の隣に置けばそちらが優先される
2. **SteamVR 付属のもの** — 上で見つからない場合、OpenVR ランタイムと同じ規約で
   `<ランタイム>\bin\win64\openvr_api.dll` を探す
   - `VR_OVERRIDE` — ランタイムのルートを直接指定する環境変数
   - `openvrpaths.vrpath` の `runtime` — SteamVR が書き出す設定ファイル。場所は
     `VR_PATHREG_OVERRIDE`、既定では `%LOCALAPPDATA%\openvr\`

どちらでも見つからない場合は、SteamVR のインストールか dll の併置を促すメッセージを表示する。

## ビルドとリリース (GitHub Actions)

| ワークフロー | 起動条件 | 内容 |
| --- | --- | --- |
| `ci.yml` | push / PR | ビルド・テストと、単一 exe 発行が壊れていないことの確認 (Linux) |
| `build.yml` | 手動 / `release.yml` から | Windows でテスト → 単一 exe 発行 → サイズ予算 → スモークテスト |
| `release.yml` | バージョンタグの push | `build.yml` を呼び、成果物を GitHub Release へ添付 |

### 手動ビルド

公開リリースを作らずにリリース用の exe を得たい場合 (実機確認用など) は、
Actions → **Build** → Run workflow を実行し、完了後に Artifacts からダウンロードする。
`version` を省略すると `0.0.0` になる。

### リリース

バージョンのタグを push すると `release.yml` が動く。接頭辞 `v` の有無はどちらでもよい。

```bash
git tag 0.1.0
git push origin 0.1.0
```

GitHub の UI からリリースを作った場合もタグが作られるのでワークフローは動く。
その場合はリリースが既に存在するため、**成果物の添付だけ**を行い、リリースノートは
上書きしない。

対応するタグ形式は `[v]MAJOR.MINOR.PATCH[-プレリリース識別子]` (例: `1.2.3`、`v1.2.3-rc.1`)。
プレリリース識別子を含むタグは prerelease として公開される。ビルドメタデータ
(`1.2.3+build.1`) を含むタグは、埋め込まれるバージョンとタグが食い違ったまま公開されるのを
避けるため、ワークフローの冒頭で明示的に拒否する。

## 経緯

当初の仕様と実装の経緯は [#1](https://github.com/limit7412/OpenVR-FloorLeveler/issues/1) を参照。
本 README が現在の仕様の一次情報であり、issue の記述と食い違う場合は本 README を正とする。
