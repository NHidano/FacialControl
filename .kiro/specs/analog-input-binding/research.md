# Research & Design Decisions — analog-input-binding

## Summary
- **Feature**: `analog-input-binding`
- **Discovery Scope**: Extension（既存システム = bone-control / layer-input-source-blending / input-binding-persistence への拡張）
- **Key Findings**:
  - 既存 `TransitionCurve` / `CurveKeyFrame` / `TransitionCalculator.Evaluate` を `Custom` カーブの実体として再利用すれば Domain 層 Unity 非依存制約と AnimationCurve 機能要件を両立できる（R-1）。
  - `BonePose` の既存 ctor は defensive copy + O(N²) 重複検出を行うため毎フレーム alloc が発生する。`bone-control` 側に internal hot-path ctor を追加（既存 public ctor 保持・API 非破壊）するのが最もクリーンな解（R-2 採用案）。
  - `OscReceiver` への `RegisterAnalogListener(string address, Action<float>)` 加算 API が、ポート競合・既存ルーティング破壊を避けつつ任意 OSC アドレスを analog source として購読する最少改変解（R-3）。
  - preview.1 では ARKit / PerfectSync 入力は OSC `/ARKit/{name}` 経由で取り込み、`ArKitOscAnalogSource` が 52ch を 1 つの N-axis IAnalogInputSource として公開する（R-4）。
  - `InputAction.ReadValue<Vector2>` の per-frame alloc は実装時 Profiler 検証案件。Tick で 1 回だけ読みキャッシュする構造で副作用を最小化（R-5）。
  - JSON スキーマは既存 `InputSourceDto.optionsJson` の二段デシリアライズを踏襲しつつ、bindings 配列をトップレベルに置く形で確定（R-6）。

## Research Log

### R-1: AnimationCurve を Domain で扱う方法

- **Context**: requirements.md Req 2.2 は `Custom` カーブを「per-binding `AnimationCurve` を Adapters 層で評価、Domain 型はカーブ識別子のみ保持」と書いているが、既存プロジェクトには `UnityEngine.AnimationCurve` を Domain で参照しないドメイン規約と、`TransitionCurve` / `CurveKeyFrame` / `TransitionCalculator.Evaluate` という Hermite 補間ベースの代替型が既に存在する。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Models/TransitionCurve.cs`
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Models/CurveKeyFrame.cs`
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Models/TransitionCurveType.cs`
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/TransitionCalculator.cs`
  - `.kiro/steering/tech.md`（Domain Unity 非依存契約）
- **Findings**:
  - `TransitionCurveType` は `Linear / EaseIn / EaseOut / EaseInOut / Custom` を提供しており、Req 2.2 の要求カーブ集合と完全一致する。
  - `TransitionCalculator.Evaluate(in TransitionCurve, float t)` は alloc=0 の static 関数で、Custom 時は `CurveKeyFrame[]` の Hermite 補間を行う（Unity AnimationCurve 互換のキー表現）。
  - JSON 永続化も `CurveKeyFrame` レベルで既に整っており、`AnimationCurve.keys` を取り回す必要がない。
- **Implications**:
  - Domain 層 Unity 非依存契約を破らずに `Custom` カーブを表現できる。
  - 二重実装を避け（DRY）、テスト・パフォーマンス両面で既存資産を活用。
  - requirements.md の "AnimationCurve" 表記は design.md / 実装上は "TransitionCurve via CurveKeyFrame[]"（functional equivalent）に置換する。requirements.md は preview 段階のため文言修正は次回更新で同期する想定。

### R-2: BonePose hot-path 構築と GC ゼロ

- **Context**: `AnalogBonePoseProvider.BuildAndPush` は毎フレーム呼ばれ、binding 数 N に対し（boneName, axis）を集約した BonePose を構築する必要がある。既存 `BonePose` ctor は (a) defensive copy `new BonePoseEntry[entries.Length]` を作り、(b) O(N²) で boneName 重複を検出するため、毎フレーム alloc が発生する。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Models/BonePose.cs:31-58`
  - `FacialControl/Tests/EditMode/.../BlendShapeNonRegressionTests.cs`（既存 GC=0 テスト構造）
  - bone-control spec 完了済 API: `IBonePoseProvider.SetActiveBonePose(in BonePose)` は in 渡しで struct コピーは避けられるが、`BonePose` 内部の `_entries: ReadOnlyMemory<BonePoseEntry>` 自体は ctor 時の copy 配列を指す。
- **Findings**:
  - 候補 (a) **「pre-alloc した `BonePoseEntry[]` プールを毎フレーム同じ参照で `new BonePose(id, pool)` に渡す」**: 防御コピー `new BonePoseEntry[entries.Length]` が毎フレーム alloc を起こすため目標未達。
  - 候補 (b) **「`bone-control` core に internal hot-path ctor を追加: `internal BonePose(string id, BonePoseEntry[] entries, bool skipValidation)`」**: 既存 public ctor は保持されるため API 非破壊（加算的変更）。`AnalogBonePoseProvider` だけが利用する想定で、prealloc 済 `_entryBuffer` を `skipValidation=true` で渡せば alloc 0。
  - 候補 (c) **「BoneWriter 側を mutable snapshot 受取に変更（delegate / source pull モデル）」**: 既存 `IBonePoseProvider.SetActiveBonePose(in BonePose)` を破壊する変更。Boundary Commitments と矛盾。
- **Implications**:
  - 採用案: **(b)**。理由は API 非破壊（既存 public ctor 残置）で alloc=0 を達成でき、既存 `OscDoubleBuffer` の pre-alloc + Volatile パターン（D-14）と方針整合。
  - 実装前に `bone-control` メンテナ（同一プロジェクト内 Hidano）の同意を取り、internal ctor の `friend assembly` または同 asmdef 内可視化で `AnalogBonePoseProvider` から到達可能にする。`bone-control` の asmdef と `analog-input-binding` の Adapters 層が同パッケージ（`com.hidano.facialcontrol`）に同居するため `internal` で十分（cross-package 参照不要）。
  - フォールバック: もし internal ctor 追加が見送られた場合、(a) を採用しつつ `BonePose` の防御コピー alloc を「1 回 / フレーム / character」として許容する判断（10 体時 10 alloc/frame）。Profiler 計測で実プロジェクトでの GC スパイクを評価して決定する。

### R-3: OSC analog source の uOscServer 共有

- **Context**: 既存 `OscReceiver` は `_addressToIndex` を init 時に固定構築し、受信メッセージを BlendShape index に直接ルーティングする。analog source は任意の OSC アドレス（VRChat `/avatar/parameters/...` や ARKit `/ARKit/...`）を購読したいが、固定マップでは新規アドレスを追加できない。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscReceiver.cs:132-172`（HandleOscMessage の構造）
  - `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscDoubleBuffer.cs`（Volatile / double-buffer パターン）
- **Findings**:
  - 候補 (a) **「OscReceiver に動的リスナー registry を追加（OSC アドレス → `Action<float>`）」**: 既存ロジックは無改変、`HandleOscMessage` の末尾で listener invoke を 1 行追加する加算的拡張で完結。
  - 候補 (b) **「analog source ごとに独立した uOscServer を立てる」**: ポート競合のリスク、listener 数増加でメモリオーバーヘッド、VRChat 用ポート 9001 を analog 用に占有してしまう問題。
  - 候補 (c) **「同じ uOscServer を bind するが、`onDataReceived` listener を analog source ごとに直接 add する」**: `_addressToIndex` のフィルタリングを通らないため全メッセージが各 source に流れる → 各 source で string compare が走り、scaling が悪い。
- **Implications**:
  - 採用案: **(a) 加算的 RegisterAnalogListener API**。`OscReceiver` に `Dictionary<string, Action<float>> _analogListeners` を 1 つ持たせ、`HandleOscMessage` 末尾で `_analogListeners.TryGetValue(message.address, out var l) ?? l?.Invoke(value)` を呼ぶだけ。既存 `_addressToIndex` ルーティングは保持。
  - 単一ポート、加算的 API、O(1) 通知のため最も軽い。

### R-4: ARKit / PerfectSync 入力経路

- **Context**: requirements.md Req 5.5 は「ArKitBlendShapeAnalogSource は N-axis ソースで、52ch を単一インスタンスで公開する」と書いている。ARKit データの入手経路を OSC（既存 `/ARKit/{name}` 受信）に統一するか、別経路（iOS デバイスからのネイティブ受信）を持つかを確定する必要がある。
- **Sources Consulted**:
  - 既存 `OscReceiver` の `ARKitAddressPrefix = "/ARKit/"` 解析（`OscReceiver.cs:22, 191-196`）
  - product.md「ARKit 52 / PerfectSync: 初回プレリリースから完全対応。自動検出+プロファイル自動生成」
- **Findings**:
  - 既存 `OscReceiver` は `/ARKit/{name}` プレフィックスを認識して BlendShape 名 → index にルーティング済み。
  - preview.1 では ARKit データの入手は外部キャプチャアプリ（iFacialMocap / Live Link Face / Hana 等）から OSC 経由が想定されており、ネイティブ ARKit SDK 受信は preview.2 以降。
  - したがって ArKit analog source は **OSC 受信に依存して構わない**。
- **Implications**:
  - 採用案: `ArKitOscAnalogSource` を `com.hidano.facialcontrol.osc` 配下に置き、ctor で 52 個の `/ARKit/{name}` を `OscReceiver.RegisterAnalogListener` する。内部 cached buffer は `Volatile.Write` で受信スレッド書込 → `Tick` で `Volatile.Read` してメインスレッドキャッシュへ転写。
  - 将来ネイティブ ARKit 受信を追加する際は、`IAnalogInputSource` を実装する別アダプタ `ArKitNativeAnalogSource` を加えるだけで Domain / Adapters/Core 不変（Req 5.6）。

### R-5: InputAction.ReadValue<Vector2> の per-frame allocation

- **Context**: requirements.md Req 8.1 は毎フレーム alloc=0 を要求。`UnityEngine.InputSystem.InputAction.ReadValue<T>()` がジェネリック型・boxing 経路で alloc を起こす可能性がある。
- **Sources Consulted**:
  - Unity InputSystem 1.17.0 ドキュメント（一般的に `ReadValue<T>` は struct 経由で boxing は起きないが、特定 ControlType では differential allocation がある）
  - 既存 `com.hidano.facialcontrol.inputsystem/Runtime/Adapters/Input/InputSystemAdapter.cs` の使用例（discrete trigger 経路で `started`/`canceled` のみ subscribe、ReadValue は呼ばない）
- **Findings**:
  - 一般的なパターン: `ReadValue<Vector2>()` は Vector2 struct を返すため boxing なしで alloc=0。ただし ControlScheme 不一致や composite control では内部で配列生成があり得る。
  - 安全策: `Tick(deltaTime)` 内で `_action.ReadValue<Vector2>()` を 1 回だけ呼び、結果を `_cachedX/_cachedY: float` フィールドに書く。`TryRead*` はフィールド参照のみで alloc 不要。
- **Implications**:
  - 採用案: 1 フレーム 1 回キャッシュ + フィールド参照。実装後に NoAlloc テスト（`Profiler.GetMonoUsedSizeLong` 差分）で検証し、もし alloc が出たら `InputControl<Vector2>.ReadValue()` を直接呼ぶ低レベル API へ切替える。
  - design.md Open Questions に記載済。

### R-6: JSON スキーマ確定

- **Context**: requirements.md Req 6 は SO + JSON 永続化を要求。既存プロジェクトには `InputSourceDto.optionsJson` による「raw JSON sub-string を id 別 typed DTO で 2 段デシリアライズ」する慣行がある（`InputSourceDto.cs`）。本 spec の JSON スキーマもこれに整合させるか検討。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/Json/Dto/InputSourceDto.cs`
  - `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/Json/Dto/OscOptionsDto.cs`
  - `JsonUtility` の制約（`Dictionary<string, string>` 不可、`object` 型不可、`[Serializable]` 必須）
- **Findings**:
  - bindings は型が均一（`AnalogBindingEntryDto`）なので 2 段デシリアライズは不要、トップレベルで `bindings: AnalogBindingEntryDto[]` でよい。
  - mapping のサブオブジェクトは `[Serializable] AnalogMappingDto` で flat に書ける。`curveType` を string、`curveKeyFrames` を `CurveKeyFrame[]` 直接 serialize。
- **Implications**:
  - 採用スキーマ:

```jsonc
{
  "version": "1.0.0",
  "bindings": [
    {
      "sourceId": "analog-bonepose.right_stick",
      "sourceAxis": 0,
      "targetKind": "bonepose",
      "targetIdentifier": "RightEye",
      "targetAxis": "Y",
      "mapping": {
        "deadZone": 0.1,
        "scale": 30.0,
        "offset": 0.0,
        "curveType": "Linear",
        "curveKeyFrames": [],
        "invert": false,
        "min": -45.0,
        "max": 45.0
      }
    }
  ]
}
```

  - DTO 命名: `AnalogInputBindingProfileDto` / `AnalogBindingEntryDto` / `AnalogMappingDto`。
  - 不正エントリは `Debug.LogWarning` + skip + 残余ロード継続（Req 6.5）。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A. Domain 完全分離 + 全 Adapter inputsystem 配置 | IAnalogInputSource 等を Domain に置き、共通 Adapter (BlendShape 値提供型 / BonePoseProvider) も全て inputsystem パッケージへ | 配置がシンプル | Domain は core にあるが共通 Adapter が inputsystem に分離 → osc-only ユーザーが共通 Adapter 部分を使えない | 不採用 |
| B. 全機能を core に押込み | inputsystem / osc サブパッケージは薄いソース実装のみ | パッケージ依存最小 | InputSystem / uOsc の実装詳細が core に流入 | 不採用 |
| **C. Hybrid（採用）** | Domain 抽象 + 共通 Adapter は core、技術別ソース実装と Binder/SO/Editor/Sample は技術パッケージ | core が osc / inputsystem を知らない契約を維持しつつ、osc-only ユーザー・inputsystem-only ユーザーの双方に共通 Adapter を提供 | 配置ルールがやや複雑、ファイル数増 | 採用。gap analysis Option C と一致 |

## Design Decisions

### Decision: Domain は `TransitionCurve` を再利用、独自 `AnimationCurve` 経路を新設しない

- **Context**: R-1 の通り、Custom カーブ要件と Domain Unity 非依存契約を両立する必要がある。
- **Alternatives Considered**:
  1. `Adapters/AnimationCurveTable` を新設し Domain は handle (int) のみ持つ — 複雑、二重実装。
  2. `TransitionCurve` を再利用 — 既存資産活用、既存テスト流用可能。
- **Selected Approach**: 2 を採用。`AnalogMappingFunction.Curve: TransitionCurve` で保持。`AnalogMappingCurveType` enum は新設せず `TransitionCurveType` をそのまま使う。
- **Rationale**: 既存プロジェクトのカーブ抽象は機能的に十分、JSON 表現も既に整っている。
- **Trade-offs**: 利用者が「AnimationCurve そのもの」を Inspector で操作したい場合の体験は劣るが、preview.1 の Editor は読取専用 + JSON 直編集前提のため許容。
- **Follow-up**: Editor の curve エディタ統合は preview.2 以降の別 spec で検討。

### Decision: bone-control core に internal hot-path BonePose ctor を追加（API 非破壊）

- **Context**: R-2 の通り、`AnalogBonePoseProvider.BuildAndPush` で alloc=0 を達成するため。
- **Alternatives Considered**:
  1. 1 frame あたりの defensive copy alloc を許容（10 体で 10 alloc/frame）。
  2. internal ctor `BonePose(string id, BonePoseEntry[] entries, bool skipValidation)` を追加。public ctor は保持。
  3. BoneWriter 側を pull モデルに変更（delegate / IBonePoseSource 化）。
- **Selected Approach**: 2。
- **Rationale**: 既存 public ctor を残すため API 非破壊（Boundary Commitments の「bone-control 公開 API シグネチャ変更しない」を遵守できる、internal は public API ではない）。alloc=0 達成が確実。
- **Trade-offs**: `bone-control` パッケージへの加算的変更が必要。同一プロジェクト内 Hidano メンテナで合意済（自己レビュー前提）。
- **Follow-up**: 実装時に `bone-control` の README / CHANGELOG に internal API として明記。assembly visibility の設定が必要なら `[InternalsVisibleTo]` で `Hidano.FacialControl.Adapters` から見えるようにする（同一 asmdef なら不要）。

### Decision: OscReceiver に加算的 RegisterAnalogListener API を追加

- **Context**: R-3。
- **Alternatives Considered**:
  1. 動的リスナー registry を加算的に追加。
  2. analog source ごとに独立 uOscServer。
  3. `onDataReceived` を直接購読し、各 source 内で string filter。
- **Selected Approach**: 1。
- **Rationale**: 単一ポート、O(1) 通知、既存 `_addressToIndex` ルーティング非破壊。
- **Trade-offs**: `OscReceiver` の責務が「BlendShape index ルータ + analog listener ハブ」に拡張される。両者の責務分離はやや弱まるが、`_addressToIndex` と `_analogListeners` は独立辞書なので相互干渉なし。
- **Follow-up**: 将来 analog ルーティングが `_addressToIndex` を超えてユースケースで主役化したら、OscRouter を抽出する別 spec を立てる。

### Decision: ARKit は preview.1 では OSC 経由のみ

- **Context**: R-4。
- **Alternatives Considered**:
  1. ネイティブ ARKit SDK を別アダプタで受信。
  2. OSC 経由のみで preview.1 をリリース。
- **Selected Approach**: 2。
- **Rationale**: 既存 `OscReceiver` の `/ARKit/` プレフィックス対応が完成しており、外部キャプチャアプリ（iFacialMocap 等）からの OSC 受信は VTuber コミュニティで標準。preview.1 のスケジュール制約に整合。
- **Trade-offs**: iOS ネイティブ受信を望む利用者には preview.2 以降を待たせる。
- **Follow-up**: preview.2 で `ArKitNativeAnalogSource` を別 spec で追加。本 spec の `IAnalogInputSource` を実装するだけで取り込める設計（Req 5.6）。

### Decision: BonePose ターゲットの同一 (bone, axis) は sum、clamp は mapping の min/max のみ

- **Context**: 「multiple binding entries targeting the same (boneName, axis)」のセマンティクスを確定する必要がある（gap analysis 質問 3）。
- **Alternatives Considered**:
  1. last-wins（document order の最後の binding が上書き）。
  2. sum（post-mapping 値を加算）+ mapping の min/max でクランプ。
  3. clamped sum（合算後に absolute min/max でクランプ）。
- **Selected Approach**: 2。理由は requirements.md Req 4.6 が「sum their post-mapping outputs into the single Euler degree value, with no implicit clamp; clamping is the responsibility of the Analog Mapping Function's min / max stage」と明示。
- **Rationale**: BlendShape 側 (Req 3.3) も sum + Aggregator clamp01 で対称。Vector2 を 1 軸ずつ binding に分解した場合に自然に和になる。
- **Trade-offs**: 利用者が意図せず大きな Euler を出した場合は clamp が効かないが、各 binding の mapping.max で個別に制限できる。
- **Follow-up**: EditMode テストでこのセマンティクスを保証。

### Decision: AnalogBonePoseProvider は FacialAnalogInputBinder が直接保持（IFacialControllerExtension にしない）

- **Context**: gap analysis 質問 1。BonePose 側の登録経路を `IFacialControllerExtension` 経由にするか、Binder が直接持つか。
- **Alternatives Considered**:
  1. `IFacialControllerExtension` を実装し `ConfigureFactory` 内で provider を組み立てる。
  2. `FacialAnalogInputBinder` が直接所有し、`LateUpdate` で `BuildAndPush` を呼ぶ。
- **Selected Approach**: 2。
- **Rationale**: `IFacialControllerExtension` は `InputSourceFactory` への入力源登録のためのフックであり、BonePose 注入は別経路（`FacialController.SetActiveBonePose`）。Binder の所有が責務として明快。BlendShape 側のみ `IFacialControllerExtension` を使う。
- **Trade-offs**: Binder が 2 系統（BlendShape + BonePose）の所有点になり責務がやや厚い。ただし profile が両方を含む以上、所有点を一箇所にまとめる方が運用上シンプル。
- **Follow-up**: Binder の責務肥大が問題化したら、`AnalogBlendShapeBinder` / `AnalogBonePoseBinder` の 2 MonoBehaviour に分割する選択肢を preview.2 で検討。

### Decision: サンプルは単一 Scene で BonePose + BlendShape を同居させる

- **Context**: gap analysis 質問 5。サンプル Scene を 1 つにするか 2 つにするか。
- **Alternatives Considered**:
  1. Scene 1（BonePose 単独）+ Scene 2（BlendShape 単独）。
  2. 単一 Scene で両方の binding を active、HUD で個別に enable/disable トグル。
- **Selected Approach**: 2。
- **Rationale**: 既存 `Multi Source Blend Demo` の運用と整合（複数の入力源 / バインディングが共存することを示す）。preview.1 同梱物を 1 Scene にまとめることで利用者の入門コストを下げる。
- **Trade-offs**: 利用者がデバイス（コントローラ + OSC 送信元）を 2 種類用意する必要がある。READMe で要件を明示。
- **Follow-up**: 必要なら HUD で sample binding を選択的に active にできるトグルを足す。

## Risks & Mitigations

- **R-Risk-1: BonePose ctor の internal hot-path ctor 追加が拒否された場合** → 1 alloc/frame/character を許容（10 体 = 10 alloc/frame）。Profiler 計測で実質的な GC スパイクの有無を確認し、preview.1 で許容できるか判断。
- **R-Risk-2: `InputAction.ReadValue<Vector2>` per-frame alloc** → 実装初期に NoAlloc テストで検出。alloc が出たら低レベル `InputControl<Vector2>.ReadValue()` 直叩きへ切替。
- **R-Risk-3: ExecutionOrder 干渉** → `FacialAnalogInputBinder` を `[DefaultExecutionOrder(-50)]` で固定。利用者が `FacialController` に独自 ExecutionOrder を付ける運用が出た場合はドキュメント化で対応。
- **R-Risk-4: OSC アドレス購読の listener 登録順序依存** → `_analogListeners` は Dictionary なので順序非依存だが、同一アドレスに複数 listener を登録した場合のセマンティクスを「全 listener 通知」に統一（OSC 受信側 1 → analog source N 構造を許容）。
- **R-Risk-5: ARKit 52ch のうち未対応パラメータ** → `ArKitOscAnalogSource` は ctor で受け取った name 配列のみ購読し、未知のアドレスからの受信は無視（既存 `OscReceiver._addressToIndex` の dedupe ポリシーと整合）。

## References

- `bone-control` spec: `.kiro/specs/bone-control/` — `IBonePoseProvider` / `IBonePoseSource` / `BonePose` の API 提供元。
- `layer-input-source-blending` spec: `.kiro/specs/layer-input-source-blending/` — D-1 hybrid model、`IInputSource` 契約、`InputSourceId` 規約。
- `input-binding-persistence` spec: `.kiro/specs/input-binding-persistence/` — 並走する離散トリガー経路。本 spec は触らない。
- Unity InputSystem 1.17.0: `com.unity.inputsystem` — `InputAction.ReadValue<T>` の挙動。
- uOsc / `com.hidano.facialcontrol.osc`: 既存 OSC 受信スタック。本 spec は加算 API のみ追加。
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/TransitionCalculator.cs` — Custom カーブ Hermite 補間の再利用元。
