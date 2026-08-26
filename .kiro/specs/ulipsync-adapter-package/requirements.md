# Requirements Document

## Project Description (Input)
com.hidano.facialcontrol.lipsync (uLipSync 連携アダプタパッケージ)

## Introduction

本仕様は、FacialControl コアパッケージ (`com.hidano.facialcontrol`) と uLipSync (ASIO Fork)
パッケージ (`com.hidano.ulipsync-asio` 3.1.5-custom 互換) を橋渡しする新規 UPM 姉妹パッケージ
`com.hidano.facialcontrol.lipsync` の要件を定義する。

コアパッケージは既に受け口となる契約 (`Hidano.FacialControl.Domain.Interfaces.ILipSyncProvider`、
`Hidano.FacialControl.Adapters.InputSources.LipSyncInputSource`、`lipsync` レイヤー、
`Hidano.FacialControl.Application.UseCases.LayerUseCase` の `additionalInputSources`
コンストラクタオーバーロード、`Hidano.FacialControl.Domain.Services.LayerInputSourceAggregator`)
を公開しているため、本パッケージは **既存契約を再定義せず、それを実装・利用する** 立場で
uLipSync の解析結果 (`uLipSync.LipSyncInfo.phonemeRatios`) を BlendShape 値ベクトルへ
マッピングし、`AdapterBindingBase` パターン (`OscReceiverAdapterBinding` を参考とする) に従って
Inspector から `FacialCharacterProfileSO._adapterBindings` に inline serialized 形式で
追加できる形で配布する。

設計方針として、ユーザーの Character Prefab には `uLipSync.uLipSync` / `AudioSource` /
`uLipSyncMicrophone` / `uLipSyncAsioInput` といった uLipSync 系コンポーネントを **一切持たせない**。
バインディング側がランタイム `OnStart` で `ctx.HostGameObject` に動的 `AddComponent` し、
`Dispose` で取り外す。これは `com.hidano.facialcontrol.inputsystem` と同じ
「prefab-clean contract」を踏襲する。

すべての設定は `ULipSyncAdapterBinding` 自身の `[SerializeField]` フィールドとして直列化され、
`FacialCharacterProfileSO._adapterBindings` 経由で保存される。本パッケージは新規
ScriptableObject アセット型を導入しない（音素マップ用の SO を作らない）。例外として、
uLipSync の **Analyzer Profile** (`uLipSync.Profile`) のみ任意の `[SerializeField]`
オブジェクト参照とし、未指定時はパッケージ同梱のデフォルトを `Resources` 経由でフォールバックする
（プロジェクト全体で 1 個共有される音声指紋アセットであるため、キャラ単位で複製しない）。

対象プラットフォームはプレリリース時点では Windows PC のみ。ASIO Fork が Windows 限定の
ためであり、将来の音声入力ソース差し替えに備えた設計余地は確保しつつも、本仕様では
クロスプラットフォーム対応を行わない。

## Boundary Context

- **In scope**:
  - `com.hidano.facialcontrol.lipsync` パッケージ本体（`package.json`, asmdef, README,
    CHANGELOG, LICENSE, `Documentation~/`）。
  - `ULipSyncProvider`（`ILipSyncProvider` 実装。`uLipSync.uLipSync.onLipSyncUpdate` を購読し、
    `LipSyncInfo.phonemeRatios` × `volume` を音素エントリのスナップショット配列で
    重み付け合算して固定長 `Span<float>` に書き込む。GC フリーホットパス）。
  - `ULipSyncAdapterBinding : AdapterBindingBase`（`[FacialAdapterBinding(displayName: "uLipSync")]`
    属性付与。引数なしコンストラクタを公開）。
    - `OnStart` で `ctx.HostGameObject` に `uLipSync.uLipSync`・入力ソースコンポーネント
      （`uLipSyncMicrophone` または `uLipSyncAsioInput`）を `AddComponent` し、`ULipSyncProvider` と
      `LipSyncInputSource` を構築し、`ctx.InputSourceRegistry` に登録（D-3, D-11）するか、
      または `LayerUseCase` の `additionalInputSources` 経路を介して合算ターゲットに加える
      （D4 に従い `ExpressionUseCase` の push/pop 経路は使用しない）。
    - `OnFixedTick` でバッファを進める（必要に応じて）。
    - `Dispose` で `AddComponent` した uLipSync 系コンポーネントを取り外す。
  - 多態的な音素エントリリスト（同一バインディング内に **BlendShape 形式**
    `(phonemeId, blendShapeName, maxWeight)` と **AnimationClip 形式**
    `(phonemeId, AnimationClip, maxWeight)` を混在配置可能。`[SerializeReference]`
    ベース）。`OnStart` で各エントリを `float[blendShapeCount]` スナップショットへ
    展開する（AnimationClip は **time-0 サンプリングのみ**、`AnimationClipCache` の
    時刻 0 スナップショットパターンを再利用）。
  - 入力デバイス記述子 `(deviceName: string, disambiguatorIndex: int)` と、これに基づく
    Mic / ASIO 自動判定ロジック（`AsioOut.GetDriverNames()` と `Microphone.devices` の列挙）。
  - **ランタイムデバイス hot-swap API**（本番中の入力デバイス切替。zero-frame settle で
    現在のレイヤー値を中立へ収束させてから旧コンポーネントを破棄し、新コンポーネントを
    `AddComponent`、`uLipSync.uLipSync` と `ULipSyncProvider` は生かしたままレジストリ
    登録も維持する）。
  - 任意 `uLipSync.Profile` 参照（未指定時はパッケージ同梱の `Runtime/Resources/` 配下の
    既定 Profile をフォールバック）。
  - Editor PropertyDrawer（UI Toolkit 製。`OscReceiverAdapterBinding` Drawer と整合する見た目で
    多態的エントリリスト・デバイス記述子ポップアップ・Analyzer Profile 参照を編集可能）。
  - `Samples~/MicLipSyncDemo`（必須。Mic 入力 + BlendShape 形式の音素エントリ）と、
    任意で `Samples~/AnimationClipLipSyncDemo`（混在エントリのデモ）。
  - EditMode 単体テスト（`ULipSyncProvider`、スナップショット展開、デバイス記述子解決、
    GC アロケーション 0 byte ベンチマーク）と PlayMode 統合テスト（バインディング
    ライフサイクル、hot-swap、デバイス断、10 体マルチキャラクター）。
  - ドキュメント `README.md`、`CHANGELOG.md`、`Documentation~/usage.md`
    （AnimationClip time-0 サンプリングの注意点を明記）、`Documentation~/migration-guide.md`
    （旧 `uLipSyncBlendShape` 手結線からの移行手順）。

- **Out of scope**:
  - 音声解析そのもの（uLipSync 側の責務）。
  - `ILipSyncProvider`・`LipSyncInputSource`・`lipsync` レイヤー・`LayerUseCase`・
    `LayerInputSourceAggregator` 等コア型の再定義／変更。コア (`com.hidano.facialcontrol`)
    のソース改変を伴わず本パッケージは純粋に追加的（additive）である。
  - 新規 ScriptableObject アセット型（`PhonemeBlendShapeMapSO` 等のキャラ単位 SO は作らない）。
  - macOS / Linux / モバイル / WebGL / VR 対応。
  - キャリブレーション UI、音素 ML、ベイクパイプライン（uLipSync 側の Editor ウィンドウが提供）。
  - 音素 AnimationClip の時間軸再生（time-0 のみサンプル、0.5 秒等の口開け遷移再生は不可）。
  - デバイス断時の OS 既定デバイスへの自動フォールバック（他キャラのマイクを奪うリスク回避）。
  - バインディング間でのデバイス共有・ミキサー抽象。
  - `ExpressionUseCase` の push/pop 経路を介した値注入（毎フレーム
    `Dictionary<string, List<Expression>>` 確保が発生するため、ホットパスから除外）。
  - ASIO サンプルシーン（ユーザー固有ドライバが必要なため `Documentation~` の手順記載のみ）。

- **Adjacent expectations**:
  - コアパッケージの `LipSyncInputSource`（無音閾値 `1e-4f`、`false` 時は `output` 非変更）を
    変更しない。本パッケージは provider 側で「`phonemeRatio[phoneme] * volume *
    snapshot[i]` を音素ごとに加算」した値を出力し、無音時に総和が閾値未満となる契約を満たす。
  - `LayerInputSourceAggregator` の毎フレーム `IInputSource.TryWriteValues(Span<float>)` を
    呼び出すホットパス契約に追従し、provider・binding 側でヒープ確保を 0 byte に維持する。
  - `OscReceiverAdapterBinding` (`com.hidano.facialcontrol.osc`) のライフサイクル
    （`OnStart` で `AddComponent` ヘルパーを `ctx.HostGameObject` に追加し、`IInputSource`
    を `ctx.InputSourceRegistry` に登録、`OnFixedTick` でバッファ前進、`Dispose` で
    取り外しと登録解除）に準拠する。
  - `LayerUseCase` の `additionalInputSources` ctor オーバーロードを介して binding が
    供給する `IInputSource` を加算合成対象に追加する（D-1 ハイブリッド入力モデルと整合）。
  - `AnimationClipCache` の「AnimationClip → 時刻 0 BlendShape ウェイトスナップショット」
    パターンを AnimationClip 形式エントリのスナップショット展開に再利用する。

## Requirements

### Requirement 1: パッケージ構成と配布

**Objective:** Unity エンジニアとして、uLipSync 連携を独立した UPM パッケージとして導入し、
コアと OSC と同じインストールフローでプロジェクトに追加できるようにしたい。それにより
リップシンクが不要なプロジェクトには本機能を入れずに済むようにしたい。

#### Acceptance Criteria

1. The ULipSync Adapter Package shall provide a `package.json` declaring
   id `com.hidano.facialcontrol.lipsync`、依存に `com.hidano.facialcontrol`（コア）と
   `com.hidano.ulipsync-asio`（3.1.5-custom 互換）、Unity 6000.3.2f1 以降のランタイムを宣言する。
2. The ULipSync Adapter Package shall ship `Runtime/`、`Editor/`、`Tests/EditMode/`、
   `Tests/PlayMode/`、`Samples~/`、`Documentation~/`、`README.md`、`CHANGELOG.md`、
   `LICENSE.md` を含む標準 UPM レイアウトで構成される。
3. The ULipSync Adapter Package shall expose all public types under namespaces rooted at
   `Hidano.FacialControl.LipSync`（`Hidano.FacialControl.LipSync.Adapters` /
   `Hidano.FacialControl.LipSync.Editor` / `Hidano.FacialControl.LipSync.Tests` 等）。
4. The Runtime asmdef of the ULipSync Adapter Package shall reference
   `com.hidano.facialcontrol`（Domain / Application / Adapters）、`com.hidano.ulipsync-asio` の
   Runtime asmdef、および `Unity.Animation` 等必要最小限のエンジン assembly のみを含み、
   Editor 専用機能を Runtime asmdef へ含めない。
5. If `com.hidano.ulipsync-asio` がプロジェクトに導入されていない、then the ULipSync
   Adapter Package shall コンパイルエラーまたは Package Manager の依存解決エラーで明示的に
   未導入を通知する（silent failure を避ける）。
6. The ULipSync Adapter Package shall mark itself Windows-only in its supported platform
   metadata（`package.json` の `keywords` および README で明記）。

### Requirement 2: Character Prefab 不干渉契約 (Prefab-Clean Contract)

**Objective:** Unity エンジニアとして、ユーザーの Character Prefab に uLipSync 系コンポーネントを
事前付与する作業を排し、`com.hidano.facialcontrol.inputsystem` と同じ「Prefab を汚さない」
契約を維持したい。

#### Acceptance Criteria

1. The ULipSync Adapter Package shall NOT require ユーザーが自身の Character Prefab に
   `uLipSync.uLipSync`、`AudioSource`、`uLipSyncMicrophone`、`uLipSyncAsioInput`
   その他の uLipSync 関連コンポーネントを事前付与すること。
2. When `ULipSyncAdapterBinding.OnStart(in AdapterBuildContext ctx)` が呼ばれる、the
   `ULipSyncAdapterBinding` shall `ctx.HostGameObject` に対し `uLipSync.uLipSync` を
   ランタイムで `AddComponent` する。
3. When `ULipSyncAdapterBinding.OnStart(in AdapterBuildContext ctx)` が呼ばれる、the
   `ULipSyncAdapterBinding` shall 解決済みデバイス記述子に応じて `uLipSyncMicrophone` または
   `uLipSyncAsioInput` のいずれか一方を `ctx.HostGameObject` に `AddComponent` する。
4. When `ULipSyncAdapterBinding.Dispose()` が呼ばれる、the `ULipSyncAdapterBinding` shall
   `OnStart`（および後続の hot-swap）で `AddComponent` した uLipSync 系コンポーネントを
   `UnityEngine.Object.Destroy` で全件取り外す。
5. The ULipSync Adapter Package shall provide a Sample Scene (`MicLipSyncDemo`) whose
   shipped character GameObject prefab には uLipSync 系コンポーネントが 1 つも付与
   されておらず、シーン再生時にバインディングが動的に追加することで Prefab-Clean Contract が
   実証可能であること。

### Requirement 3: 設定の inline serialization (No-New-SO 契約)

**Objective:** Unity エンジニアとして、新規 ScriptableObject アセット（音素マップ SO 等）の
作成・管理コストを増やさず、`OscReceiverAdapterBinding` と同じく `FacialCharacterProfileSO` の
`_adapterBindings` 配列に inline serialized されたバインディング 1 つだけで設定が完結する形を
維持したい。

#### Acceptance Criteria

1. The ULipSync Adapter Package shall NOT define new `ScriptableObject`-derived asset types
   for per-character or per-mapping data（`PhonemeBlendShapeMapSO` 等は作らない）。
2. The `ULipSyncAdapterBinding` shall hold all per-character configuration as
   `[SerializeField]` fields on itself、これを `FacialCharacterProfileSO._adapterBindings`
   経由で inline serialized 形式により永続化させる。
3. The `ULipSyncAdapterBinding` shall expose a `[SerializeField]` polymorphic list of
   phoneme entries（`[SerializeReference]` ベース）を保持する。
4. Where the user wants to share a `uLipSync.Profile`（Analyzer Profile）asset across
   characters、the `ULipSyncAdapterBinding` shall expose an OPTIONAL `[SerializeField]`
   reference of type `uLipSync.Profile`、未指定時は `Runtime/Resources/` 配下に同梱された
   既定 Profile を `Resources.Load` 経由でフォールバックする。
5. The bundled default `uLipSync.Profile` shall be shipped exactly once in the package
   and shared across all bindings（キャラ単位での複製を行わない）。

### Requirement 4: 多態的音素エントリと AnimationClip サポート

**Objective:** Unity エンジニアとして、音素を単一 BlendShape にマップするだけでなく、
複数 BlendShape を含む AnimationClip 1 枚にもマップできるようにしたい。これにより
「`A` で口を大きく開け、同時に頬を上げる」等の複合形状を 1 エントリで指定できるようにしたい。

#### Acceptance Criteria

1. The `ULipSyncAdapterBinding` shall accept polymorphic phoneme entries of two concrete
   shapes that may freely mix within one binding：(a) **BlendShape 形式**
   `(phonemeId: string, blendShapeName: string, maxWeight: float)`、(b) **AnimationClip 形式**
   `(phonemeId: string, clip: AnimationClip, maxWeight: float)`。
2. When `ULipSyncAdapterBinding.OnStart(in AdapterBuildContext ctx)` is invoked、the
   binding shall pre-compute、エントリごとに `float[blendShapeCount]` のスナップショット
   配列を作成する。
3. For BlendShape 形式 entries、the binding shall fill the index corresponding to
   `blendShapeName` with `maxWeight` and zero in all other indices。
4. For AnimationClip 形式 entries、the binding shall extract the BlendShape weight vector
   at time 0 of the clip（`AnimationClipCache` の time-0 サンプリングパターン、または
   `Animator.SampleAnimation(target, clip, 0f)` 後の `SkinnedMeshRenderer.GetBlendShapeWeight`
   読み戻しで実装）し、抽出ベクトルに `maxWeight / 100` を乗じてスナップショットへ格納する。
5. The runtime hot path shall accumulate `phonemeRatios[phoneme] * volume * snapshot[i]`
   across all phoneme entries into the output `Span<float>` and shall behave identically
   regardless of whether each entry is BlendShape 形式 or AnimationClip 形式。
6. The ULipSync Adapter Package shall document explicitly in `Documentation~/usage.md`
   that AnimationClips are sampled at time 0 only（時間軸再生は行わず、0.5 秒等の動的口
   開け遷移は **再生されず最初のフレームのみ使用される**）。
7. If `blendShapeName` for a BlendShape 形式 entry is not present on the target
   `SkinnedMeshRenderer`、then the binding shall log `Debug.LogWarning` with the unresolved
   name once at `OnStart` and skip the entry's snapshot fill（残りのエントリは正常に処理
   する）。

### Requirement 5: ULipSyncProvider と ILipSyncProvider 実装

**Objective:** uLipSync の解析結果（`LipSyncInfo.phonemeRatios` と `volume`）を、コア側
`ILipSyncProvider` の固定長 `Span<float>` 出力契約に橋渡ししたい。

#### Acceptance Criteria

1. The `ULipSyncProvider` shall implement
   `Hidano.FacialControl.Domain.Interfaces.ILipSyncProvider`。
2. When `uLipSync.uLipSync.onLipSyncUpdate` fires、the `ULipSyncProvider` shall
   `LipSyncInfo.phonemeRatios` と `LipSyncInfo.volume` を内部固定長バッファへ反映する。
3. When `GetLipSyncValues(Span<float> output)` is invoked、the `ULipSyncProvider` shall
   音素エントリのスナップショット配列を `phonemeRatio * volume` で重み付けして合算し、
   `output` の長さ分（不足時は overlap のみ）へ書き込む。
4. The `ULipSyncProvider.BlendShapeNames` shall return a `ReadOnlySpan<string>` whose
   ordering is fixed at construction time and corresponds to the snapshot index ordering
   used by `GetLipSyncValues`。
5. While `ULipSyncProvider` is active、the provider shall produce values whose sum
   collapses below `LipSyncInputSource.SilenceThreshold`（`1e-4f`）during silent frames
   so that `LipSyncInputSource.TryWriteValues` returns `false` and `output` remains
   unchanged。
6. If `phonemeRatios` contains a phoneme key that does not match any configured phoneme
   entry、then the `ULipSyncProvider` shall そのキーをログ出力なしでスキップする
   （uLipSync 側の Profile 差異を許容するため）。
7. If a configured phoneme entry's key is absent from the current frame's `phonemeRatios`、
   then the `ULipSyncProvider` shall そのエントリの寄与を 0 として扱う。
8. The `ULipSyncProvider` shall provide a constructor accepting at minimum a
   `(uLipSync.uLipSync source, IReadOnlyList<PhonemeSnapshot> snapshots)` 形式のシグネチャを
   公開し、引数 null 時は `ArgumentNullException` を送出する。
9. When `ULipSyncProvider.Dispose()` is invoked、the provider shall `onLipSyncUpdate` の
   購読を解除し、以降のイベント着信を内部に反映しない。

### Requirement 6: ULipSyncAdapterBinding ライフサイクルと Aggregator 連携

**Objective:** Unity エンジニアとして、Inspector の Add ドロップダウンから「uLipSync」を選んで
追加するだけで provider・`LipSyncInputSource`・`LayerInputSourceAggregator` 連携が自動完了する
バインディングを使いたい。

#### Acceptance Criteria

1. The `ULipSyncAdapterBinding` shall be a `[FacialAdapterBinding(displayName: "uLipSync")]`
   属性付きの `AdapterBindingBase` 派生 sealed class で、引数なしコンストラクタを持つ。
2. When `OnStart(in AdapterBuildContext ctx)` is invoked、the `ULipSyncAdapterBinding` shall
   (a) `ctx.HostGameObject` に `uLipSync.uLipSync` を `AddComponent`、(b) デバイス記述子に
   応じて `uLipSyncMicrophone` または `uLipSyncAsioInput` を `AddComponent`、(c) Analyzer
   Profile を解決して `uLipSync.uLipSync` に注入、(d) 多態的音素エントリのスナップショット
   配列を構築、(e) `ULipSyncProvider` と `LipSyncInputSource` を構築、(f) AdapterSlug を
   キーに `ctx.InputSourceRegistry` に `LipSyncInputSource` を登録する、を順序立てて実行する。
3. The `ULipSyncAdapterBinding` shall integrate with `LayerUseCase` via the
   `additionalInputSources` constructor parameter exposed by the core's `LayerUseCase`、
   `LayerInputSourceAggregator` の毎フレーム加算合成へ供給する経路を採用する。
4. The `ULipSyncAdapterBinding` shall NOT integrate via `ExpressionUseCase` push/pop
   APIs（`Dictionary<string, List<Expression>>` の毎フレーム確保を発生させる経路は
   採用しない）。
5. When `Dispose()` is invoked、the `ULipSyncAdapterBinding` shall (a) provider の
   `onLipSyncUpdate` 購読を解除、(b) `ctx.InputSourceRegistry` から登録解除、
   (c) `OnStart` および hot-swap で `AddComponent` した uLipSync 系コンポーネントを全件
   `UnityEngine.Object.Destroy` で取り外す、を実行する。
6. If `OnStart` 中に Analyzer Profile を解決できない（参照未指定かつパッケージ同梱既定も
   読み込み失敗）、then the `ULipSyncAdapterBinding` shall `Debug.LogError` で原因を記録し、
   `AddComponent` の roll back と早期 return により破壊的副作用を残さず終了する。
7. Where `OscReceiverAdapterBinding` のライフサイクル順序（`OnStart` → 各 Tick → `Dispose`）と
   `IInputSource` 登録 API、the `ULipSyncAdapterBinding` shall 同一の呼び出し順・契約に
   準拠する。

### Requirement 7: 入力デバイス自動判定 (Mic vs ASIO)

**Objective:** Unity エンジニアおよび配信者として、自身の入力デバイスが ASIO 対応か通常マイクかを
意識せず、デバイス名と曖昧性解消インデックスのみを設定すれば動作するようにしたい。

#### Acceptance Criteria

1. The `ULipSyncAdapterBinding` shall expose a `[SerializeField]` device descriptor of
   shape `(deviceName: string, disambiguatorIndex: int)`。
2. When `ULipSyncAdapterBinding.OnStart` resolves the device descriptor、the binding shall
   first enumerate `NAudio.Wave.Asio.AsioOut.GetDriverNames()`（または uLipSync 側が公開する
   等価な API）し、`deviceName` がいずれかの ASIO ドライバ名に一致する場合は
   `uLipSyncAsioInput` を選択する。
3. When the device descriptor does not match any ASIO driver name、the binding shall
   `UnityEngine.Microphone.devices` を列挙し、`deviceName` と `disambiguatorIndex` の組で
   一致する N 番目の Microphone デバイスを選択し、`uLipSyncMicrophone` を `AddComponent` する。
4. While multiple devices share the same `deviceName`（Dante 系の同名デバイス衝突等）、
   the binding shall `disambiguatorIndex` で列挙順の N 番目を選ぶことで一意に解決する。
5. The `ULipSyncAdapterBinding` shall NOT expose a user-facing toggle for "ASIO or Mic"
   selection（自動判定のみ）。

### Requirement 8: ランタイムデバイス hot-swap

**Objective:** 配信者として、本番ストリーム配信中にマイクを USB / ASIO 機器へ差し替えても、
再起動なしに表情制御を維持できるようにしたい。

#### Acceptance Criteria

1. The `ULipSyncAdapterBinding` shall expose a public hot-swap API that accepts a new
   device descriptor `(deviceName, disambiguatorIndex)` and switches the active input
   source at runtime without re-creating the binding instance。
2. When the hot-swap API is invoked、the `ULipSyncAdapterBinding` shall (a) provider に
   1 フレーム分のゼロ値（無音）を明示的に通知し、`LayerInputSourceAggregator` の
   weighted-sum を介して現在のレイヤー値を中立へ収束させる、(b) 既存の
   `uLipSyncMicrophone` または `uLipSyncAsioInput` コンポーネントを
   `UnityEngine.Object.Destroy` で破棄、(c) 新デバイス記述子を解決し適切なコンポーネントを
   `AddComponent`、(d) `uLipSync.uLipSync` 本体と `ULipSyncProvider` を生かしたまま、
   `ctx.InputSourceRegistry` の登録エントリも維持する、を順序立てて実行する。
3. The hot-swap API shall complete its swap sequence without restarting the
   `ULipSyncProvider` 購読や `LipSyncInputSource` 登録を強制しない（同一インスタンスを
   保持する）。
4. While 10 体以上の `FacialCharacter` がそれぞれ独立した `ULipSyncAdapterBinding` を
   保持する、the hot-swap of one binding shall NOT affect the input chain of any other
   binding（インスタンス分離）。
5. If the hot-swap target descriptor cannot be resolved、then the `ULipSyncAdapterBinding`
   shall log `Debug.LogError` with the unresolved descriptor and the available device list、
   ゼロ値 settle のみ行い、新デバイスコンポーネントを `AddComponent` せず silence モードで
   維持する（既存の壊れたコンポーネントを残さない）。

### Requirement 9: デバイス断 / 未接続時の挙動

**Objective:** 配信者として、設定したデバイスが起動時または途中で利用不能になっても、
キャラクターが半開きの口で固まらないようにしたい。

#### Acceptance Criteria

1. If at `OnStart` the configured device descriptor cannot be resolved（`AsioOut.GetDriverNames()`
   と `Microphone.devices` のいずれにも一致しない）、then the `ULipSyncAdapterBinding`
   shall log `Debug.LogError` with the unresolved descriptor and the enumerated
   available device list（Editor / Player の両方で）。
2. If at `OnStart` the configured device cannot be resolved、then the
   `ULipSyncAdapterBinding` shall NOT silently fall back to the OS default device
   （他キャラのマイクを奪うリスクを避けるため）。
3. When a device becomes unresolvable mid-session、the `ULipSyncAdapterBinding` shall
   provider にゼロ値を 1 フレーム分通知して現在のレイヤー値を中立へ収束させ、以降は
   `LipSyncInputSource.TryWriteValues` が無音閾値経路で `false` を返す silence モードで
   維持する（半開きの口が残らないこと）。
4. While in silence mode due to device unavailability、the `ULipSyncAdapterBinding` shall
   NOT auto-recover when the device reappears（USB 再接続等）。
5. To recover from silence mode、the user shall explicitly invoke the hot-swap API
   （Requirement 8）or restart the binding（`Dispose` → 再構築）。

### Requirement 10: マルチキャラクター・複数バインディング動作

**Objective:** Unity エンジニアとして、同時に 10 体以上のキャラクターを制御するという全体性能
目標を本パッケージでも崩さないこと。

#### Acceptance Criteria

1. While 10 体以上の `FacialCharacter` がそれぞれ独立した `ULipSyncAdapterBinding`
   インスタンスを保持する、the ULipSync Adapter Package shall フレーム落ち無く動作可能で
   あり、provider・binding 同士は静的状態を共有しない。
2. The `ULipSyncAdapterBinding` shall identify its instance via AdapterSlug and shall
   register to `ctx.InputSourceRegistry` with a key that does not collide with other
   characters（slug を名前空間化する）。
3. If 同一 `FacialCharacter` に `ULipSyncAdapterBinding` が複数追加される、then the
   2 個目以降のインスタンス shall `OnStart` で `Debug.LogError` を出力し、登録をスキップする。
4. While each binding selects its own device descriptor、the ULipSync Adapter Package
   shall allow per-character device selection without forcing a shared device or mixer
   abstraction（バインディング間でのデバイス共有は提供しない）。

### Requirement 11: GC フリー / 性能契約

**Objective:** プロジェクト全体の性能要件「毎フレームのヒープ確保ゼロ」を本パッケージでも保つ。
GC スパイクで配信のフレーム落ちを起こしたくない。

#### Acceptance Criteria

1. While `ULipSyncProvider` processes `onLipSyncUpdate` events and `GetLipSyncValues`
   calls、the provider shall ヒープ確保 0 byte で動作する（内部バッファ・辞書ルックアップ
   構造は構築時にのみ確保する）。
2. While `ULipSyncAdapterBinding.OnFixedTick` / `OnUpdate` 等が呼ばれる、the binding shall
   ヒープ確保 0 byte で動作する。
3. While `LayerInputSourceAggregator` calls `LipSyncInputSource.TryWriteValues(Span<float>)`
   per frame、the chain `LipSyncInputSource → ULipSyncProvider → snapshot accumulation` shall
   ヒープ確保 0 byte で動作する。
4. The ULipSync Adapter Package shall include an EditMode benchmark test that asserts
   0 byte allocation across N（少なくとも 10000 回）の `ULipSyncProvider.GetLipSyncValues`
   呼び出しを `Unity.PerformanceTesting` または `GC.GetTotalAllocatedBytes` 差分で検証する。
5. Where snapshot extraction is performed at `OnStart`（AnimationClip 形式エントリの time-0
   サンプリング含む）、the binding shall allow heap allocation as a one-time construction
   cost（ホットパスから除外）。
6. The ULipSync Adapter Package shall avoid LINQ・`string.Format`・`new T[]`・
   `new List<T>()`・`new Dictionary<,>()` in any hot path 以下、その方針を README または
   `Documentation~` に明記する。

### Requirement 12: Editor PropertyDrawer

**Objective:** Unity エンジニアとして、Inspector で `ULipSyncAdapterBinding` を編集する際、
`OscReceiverAdapterBinding` Drawer と同等の見た目・操作感で扱えるようにしたい。

#### Acceptance Criteria

1. The ULipSync Adapter Package shall provide an Editor 専用 asmdef
   （`includePlatforms: ["Editor"]`）配下に `ULipSyncAdapterBinding` 用の `PropertyDrawer`
   を実装する。
2. The PropertyDrawer shall be implemented in UI Toolkit（IMGUI を新規 UI に使わない）。
3. The PropertyDrawer shall render the polymorphic phoneme entry list with a per-entry
   type selector（BlendShape 形式 / AnimationClip 形式）、追加・削除・並べ替えを許可する。
4. The PropertyDrawer shall render the device descriptor as a popup populated from
   currently-enumerated ASIO drivers and Microphone devices、加えて手動オーバーライド
   テキストフィールド（接続中でないデバイス名を入力可能）と `disambiguatorIndex` の整数
   フィールドを提供する。
5. The PropertyDrawer shall render the optional `uLipSync.Profile` reference as an
   ObjectField、未指定時は「パッケージ同梱既定」プレースホルダ表示を行う。
6. If a BlendShape 形式 entry's `blendShapeName` is empty、then the PropertyDrawer shall
   HelpBox に「BlendShape 名未設定」の警告を表示し、ランタイム前に修正を促す。
7. The PropertyDrawer shall integrate with the same Drawer style conventions as the
   `OscReceiverAdapterBindingDrawer`（折りたたみ挙動・Validation メッセージ表示・フィールド配置
   と整合）。

### Requirement 13: Samples~ パッケージング

**Objective:** Unity エンジニアとして、自前モデルを差し込むだけで動作確認できる canonical
サンプルを UPM Package Manager から Import 可能な形で配布したい。

#### Acceptance Criteria

1. The ULipSync Adapter Package shall include `Samples~/MicLipSyncDemo/` and shall
   register it in `package.json` の `samples` 配列で Package Manager から Import 可能とする。
2. The `MicLipSyncDemo` sample shall ship: (a) `FacialCharacterProfileSO`
   （`ULipSyncAdapterBinding` を inline serialized で配線済み）、(b) `ULipSyncAdapterBinding`
   内に A/I/U/E/O 等の音素を BlendShape 形式エントリで設定済み、(c) Scene。Scene は
   `Character` という空 GameObject を含み、ユーザーはそこへ自前モデル（FBX/VRM）を
   ドロップするだけで動作する。
3. The Scene of `MicLipSyncDemo` shall demonstrate the Prefab-Clean Contract
   （Requirement 2）：同梱キャラクター GameObject は再生前に uLipSync 系コンポーネントを
   1 つも持たず、再生時にバインディングが動的追加することが目視確認できる。
4. Where the user wants AnimationClip 形式 entries の動作デモ、the ULipSync Adapter
   Package MAY ship `Samples~/AnimationClipLipSyncDemo/`（オプション）。提供する場合は
   `MicLipSyncDemo` と同等の構成で、エントリは AnimationClip 形式と BlendShape 形式の
   混在を含む。
5. The ULipSync Adapter Package shall NOT ship an ASIO sample scene（ユーザー固有
   ドライバが必要なため）。代わりに `Documentation~/usage.md` に ASIO 利用手順を
   記載する。
6. The ULipSync Adapter Package shall document the `Samples~/` ↔ `Assets/Samples/`
   二重管理ルール（プロジェクト規約）を README または `Documentation~` に明記し、
   編集時に両方を同期する手順を含める。

### Requirement 14: テスト網羅 (TDD) と ドキュメント / 規約

**Objective:** プロジェクト全体の TDD 厳守方針と命名規約・言語規約を維持し、`OscReceiverAdapterBinding`
と同等のテスト面とドキュメント面を確保したい。

#### Acceptance Criteria

1. The ULipSync Adapter Package shall provide an EditMode test suite that covers:
   (a) `ULipSyncProvider` 構築時引数 null での `ArgumentNullException`、(b) `phonemeRatios`
   辞書からスナップショット重み付け合算配列への正しいマッピング、(c) マッピングに無い
   音素キーの無視、(d) 設定済み音素エントリが当該フレームに存在しない場合のゼロ寄与、
   (e) 無音時に総和が `LipSyncInputSource.SilenceThreshold` 未満で collapse すること、
   (f) `Dispose` での購読解除、(g) GC アロケーション 0 byte（Requirement 11）、
   (h) BlendShape 形式エントリの直接フィルおよび AnimationClip 形式エントリの time-0
   サンプリング、(i) 形式混在エントリ、(j) 未解決 BlendShape 名のハンドリング、
   (k) デバイス記述子解決ロジック（mock 列挙器で ASIO 一致 / Mic 一致 /
   `disambiguatorIndex` による列挙順選択 / 不一致）。
2. The ULipSync Adapter Package shall provide a PlayMode test suite that covers:
   (a) `ULipSyncAdapterBinding` ライフサイクル（`OnStart` で期待コンポーネントが追加され、
   `Dispose` で取り外される）、(b) Play 中の hot-swap（Mic ↔ ASIO 双方向、ゼロ値 settle 含む）、
   (c) デバイス断 / エラー経路（解決不能 → エラーログ + silence + ゼロ値 settle）、
   (d) 10 体マルチキャラクター分離（同時 10 個の独立バインディングと個別デバイス選択）。
3. The PlayMode test surface shall map 1-to-1 to `OscReceiverAdapterBindingTests` の対応する検証項目
   （少なくともライフサイクル・Registry 操作・例外時の安全停止）。
4. While CI runs tests、the ULipSync Adapter Package shall require EditMode と PlayMode
   両方のテストが失敗ゼロで完了することを必須要件とする。
5. The ULipSync Adapter Package shall follow the project test naming convention
   `{Method}_{Condition}_{Expected}`（例: `GetLipSyncValues_SilentFrame_ProducesSubThresholdSum`、
   `OnStart_AsioDeviceMatch_AddsAsioInputComponent`）。
6. The ULipSync Adapter Package shall ship `README.md`（インストール手順、最小サンプル
   手順、Windows 限定スコープ、既存契約との関係、preview 期の破壊変更可能性、Hot Path
   GC 0 byte 方針）、`CHANGELOG.md`（Keep a Changelog 形式の初版エントリ）、
   `Documentation~/usage.md`（AnimationClip time-0 サンプリングの注意点を必須記載、
   ASIO 利用手順）、`Documentation~/migration-guide.md`（旧 `uLipSyncBlendShape` 手結線
   からの移行 4 ステップ）を提供する。
7. The ULipSync Adapter Package shall write all comments・XML ドキュメント・README・
   CHANGELOG・`Documentation~/` を日本語で記述する（プロジェクト言語契約）。
8. The ULipSync Adapter Package shall follow the project coding conventions: 4 スペース
   インデント、改行時の中括弧、明示的アクセス修飾子、`PascalCase` 型 / enum、
   `I` プレフィックスインターフェース、`_camelCase` プライベートフィールド、すべての public
   型を `Hidano.FacialControl.LipSync` 名前空間ルート配下に配置する。
9. The ULipSync Adapter Package shall handle errors via Unity 標準ログ
   （`Debug.Log` / `Debug.LogWarning` / `Debug.LogError`）のみで処理し、独自例外型を新設
   しない（標準例外 `ArgumentNullException` 等は許容する）。
