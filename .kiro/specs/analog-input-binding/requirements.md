# Requirements Document

## Project Description (Input)
analog-input-binding: 連続値（アナログ）入力源（ゲームパッドのスティック軸、ARKit / PerfectSync の BlendShape 値、OSC の float パラメータ等）から、表情の **BlendShape 値** および **顔ボーンの BonePose** を駆動するためのバインディングサブシステム。離散トリガーで Expression を切り替える既存の `ExpressionTriggerInputSourceBase` 系（`controller-expr` / `keyboard-expr`）とは別の経路として、**連続値 → 連続値** のマッピングを第一級で扱う。本 spec は直前に完了した `bone-control` spec が公開した `IBonePoseProvider` / `IBonePoseSource` / `FacialController.SetActiveBonePose` API の **最初の消費者** であり、C → A の C 側（基盤）→ A 側（消費者）フローの A 側を担当する。

## 背景・動機

- 既存の入力経路は **離散イベントドリブン**（`InputAction` の `performed` / `canceled` で `TriggerOn` / `TriggerOff`）に最適化されており、`InputBinding` も `(ActionName, ExpressionId)` の 2 タプルで「ボタン → 表情 ID」のスイッチにしか使えない。
- 一方、VTuber 配信・ゲーム内表情制御の現場では「右スティック X/Y で目線を動かす」「ARKit `jawOpen` 0.0〜1.0 を `mouthOpen` BlendShape に直結する」「OSC `/avatar/parameters/eyeBrowsY` の float 値を眉 BlendShape へ送る」といった **連続値 → 連続値の継続駆動** が一般的なユースケースである。
- `bone-control` spec は `IBonePoseProvider.SetActiveBonePose(in BonePose)` という外部から BonePose を注入する拡張点を**意図的に空けた**状態で完了している。本 spec はその拡張点を最初に消費し、「Vector2 入力 → BonePose（Head / Eye の Euler）」の経路を実装する。
- 既存 `layer-input-source-blending` spec はレイヤー内で複数入力源の `float[BlendShapeCount]` を加重和合成する基盤を提供しており、アナログ入力源は **BlendShape 値提供型アダプタ** としてこの基盤に乗ることができる（D-1 ハイブリッドモデルの「BlendShape 値提供型」側）。
- 結論として、本 spec は **(1) アナログ入力 → BlendShape 値マッピング** と **(2) アナログ入力 → BonePose Euler マッピング** の 2 系統を、共通の「アナログバインディング」抽象の下で提供する。

## スコープ

- アナログ入力ソースの抽象化（`IAnalogInputSource`：1 軸の float / 2 軸の Vector2 / N 軸の `float[]` を返す共通契約）
- 入力種別ごとのソース実装：
  - InputSystem の `InputAction`（`Vector2` / `float` / `Axis` ControlType）
  - OSC の float パラメータ（`/avatar/parameters/{name}` 互換、既存 `com.hidano.facialcontrol.osc` を再利用）
  - 外部キャプチャの BlendShape 値（ARKit / PerfectSync 由来の `float[BlendShapeCount]`）
- アナログバインディングの 2 系統：
  - **BlendShape ターゲット**：`IAnalogInputSource` の出力を BlendShape 値（`float[BlendShapeCount]`）に変換し、`layer-input-source-blending` の BlendShape 値提供型アダプタとしてレイヤーに供給する
  - **BonePose ターゲット**：`IAnalogInputSource` の出力を `BonePose`（`bone-control` の Domain モデル）に変換し、`IBonePoseProvider.SetActiveBonePose` 経由で `FacialController` に注入する
- マッピング関数（カーブ・スケール・オフセット・デッドゾーン・反転・クランプ）とその JSON 表現
- 永続化：`AnalogInputBindingProfileSO`（ScriptableObject）+ JSON。`InputBindingProfileSO`（離散トリガー側）とは別アセット
- ランタイムでの有効化／無効化／プロファイル差替え API
- preview.1 のサンプル：右スティック → 目線 BonePose、ARKit `jawOpen` → 口開き BlendShape

## スコープ外

- 離散トリガー（`(ActionName, ExpressionId)` ペア）による表情切替 — 既存 `input-binding-persistence` spec の責務
- 新規入力デバイス層の追加（独自センサー、Web カメラトラッキング等） — 既存 InputSystem / OSC / ILipSyncProvider の範囲で対応
- BonePose の **複数同時アクティブ**（複数 provider のブレンド）— 本 spec は `SetActiveBonePose` の単一最終 pose を組み立てる側に責任を持ち、provider 間の合成は将来 spec
- 視線追従の Vector3 ターゲット指定（カメラ目線・対象ターゲット注視）— preview.2 後半の別 spec
- BonePose ターゲット側のマルチレイヤー化・BonePose Mixer — 現状 BonePose は単一 active のみ（`bone-control` 設計）
- Editor 拡張 UI のフルセット — preview 段階では JSON 直編集 + 最低限の Inspector 表示で運用、フル GUI 編集は将来
- AnimationClip / Timeline からのアナログ値駆動

## 制約・前提

- Domain 層は Unity 非依存。`UnityEngine.InputSystem` 参照は Adapters/Input 層に閉じる
- 毎フレームのヒープ確保ゼロ（プロジェクト全体方針 / Req 6.1）
- `bone-control` 公開 API（`IBonePoseProvider.SetActiveBonePose(in BonePose)`、`FacialController.SetActiveBonePose`、`IBonePoseSource.GetActiveBonePose`）を **そのまま** 消費する。bone-control 側に追加 API を要求しない
- `layer-input-source-blending` の D-1 ハイブリッドモデル（Expression トリガー型 vs BlendShape 値提供型）を踏襲する。アナログ入力 → BlendShape は **BlendShape 値提供型アダプタ** として実装する
- BlendShape 命名規則を固定しない（2 バイト文字・特殊記号 OK）
- ボーン参照は string 名指定（VRM / FBX 双方で使えるよう Humanoid 非前提、`bone-control` Req 2 と整合）
- エラーハンドリングは Unity 標準ログ（`Debug.Log/Warning/Error`）のみ
- preview 段階では破壊的変更を許容（JSON スキーマの追加フィールド・改名は preview 中可）

## 用語

- **アナログ入力 (Analog Input)**: 連続値（float / Vector2 / `float[]`）として時間変化する入力。離散トリガー（ボタン押下 → Expression 切替）と対比される
- **アナログバインディング (Analog Binding)**: 1 つのアナログ入力ソース（`IAnalogInputSource`）と 1 つのターゲット（BlendShape 値 / BonePose Euler）を結ぶマッピング定義
- **マッピング関数 (Mapping)**: 入力 float から出力 float への変換。デッドゾーン・スケール・オフセット・カーブ・反転・クランプを 1 セットで含む
- **BlendShape ターゲット**: アナログ値が最終的に BlendShape の値として書込まれるバインディングのターゲット種別
- **BonePose ターゲット**: アナログ値が最終的に `BonePose` の `relativeEulerXYZ` 成分として書込まれるバインディングのターゲット種別
- **アナログ入力源アダプタ (Analog Input Source Adapter)**: `IAnalogInputSource` を実装する具体クラス（`InputActionAnalogSource` / `OscAnalogSource` / `ArKitBlendShapeAnalogSource` 等）

## 関連 spec

- 前提（C 側）: `.kiro/specs/bone-control/` — 完了済。`IBonePoseProvider` / `IBonePoseSource` / `FacialController.SetActiveBonePose` を公開済み
- 兄弟（同 D-1 モデル下）: `.kiro/specs/layer-input-source-blending/` — BlendShape ターゲット側はこの基盤の **BlendShape 値提供型アダプタ** として接続する
- 兄弟（離散トリガー側）: `.kiro/specs/input-binding-persistence/` — 離散トリガー（`InputBindingProfileSO`）は別アセット・別ランナーで運用する
- 後続（preview.2 以降）: 視線追従ターゲット指定 spec（Vector3 ターゲット → BonePose 自動算出）。本 spec はその基盤として `IBonePoseProvider` 経由の BonePose 注入経路を確立する

## Introduction

analog-input-binding は、ゲームパッドのスティック・ARKit / PerfectSync の BlendShape 値・OSC の float パラメータといった **連続値（アナログ）入力** を、(1) BlendShape 値（`float[BlendShapeCount]`）または (2) `BonePose`（顔ボーンの相対 Euler）にマッピングして表情・顔ボーン姿勢に反映するサブシステムである。本 spec は直前に完了した `bone-control` spec の API 消費者であり、`IBonePoseProvider.SetActiveBonePose(in BonePose)` 経由で BonePose を注入する経路を最初に実装する。BlendShape ターゲット側は `layer-input-source-blending` spec の D-1 ハイブリッドモデルの「BlendShape 値提供型アダプタ」として接続し、既存の `LayerInputSourceAggregator` パイプラインに自然に乗る。離散トリガー（ボタン → Expression 切替）の既存経路（`InputBindingProfileSO` + `FacialInputBinder`）には**手を入れず**、別アセット `AnalogInputBindingProfileSO` として並走する設計を取る。

## Boundary Context

- **In scope**:
  - Domain 層の `IAnalogInputSource` 抽象（Unity 非依存、float / Vector2 / `float[]` を返す）と関連バリデーション
  - Domain 層のマッピング関数（デッドゾーン / スケール / オフセット / カーブ / 反転 / クランプ）の値型表現と評価ロジック
  - Adapters/Input 層のアナログ入力源アダプタ実装：
    - `InputActionAnalogSource`（`UnityEngine.InputSystem.InputAction` 経由）
    - `OscAnalogSource`（既存 `com.hidano.facialcontrol.osc` の OSC 受信を経由）
    - `ArKitBlendShapeAnalogSource`（ARKit / PerfectSync の BlendShape 値配列を 1 つの N 軸ソースとして公開）
  - BlendShape ターゲット用アダプタ：`AnalogBlendShapeInputSource`（`layer-input-source-blending` の BlendShape 値提供型アダプタとしてレイヤーに供給）
  - BonePose ターゲット用アダプタ：`AnalogBonePoseProvider`（`IBonePoseProvider.SetActiveBonePose` 経由で `FacialController` に注入）
  - `AnalogInputBindingProfileSO` ScriptableObject + JSON 永続化スキーマ
  - ランタイムでのプロファイル有効化／無効化／差替え API（離散トリガー側 `FacialInputBinder` と分離）
  - preview.1 サンプル：右スティック → 目線 BonePose、ARKit `jawOpen` → 口開き BlendShape
- **Out of scope**:
  - 離散トリガーによる Expression 切替（`input-binding-persistence` の責務）
  - 複数 BonePose Provider 間のブレンド（本 spec は単一 active BonePose を組み立てる）
  - 視線追従の Vector3 ターゲット指定 / カメラ目線追従（別 spec）
  - 新規入力デバイス層追加（独自センサー等）
  - フル GUI のマッピング編集 Editor（JSON 直編集前提、Inspector は読み取り専用 + 簡易編集に留める）
  - Timeline / AnimationClip からのアナログ値駆動
  - `bone-control` 公開 API 自体の変更要求
- **Adjacent expectations**:
  - `bone-control` の `IBonePoseProvider.SetActiveBonePose(in BonePose)` / `FacialController.SetActiveBonePose` / `IBonePoseSource.GetActiveBonePose` は本 spec によって**初めて外部から消費**される。本 spec は bone-control 側の API シグネチャに**破壊的変更を加えない**
  - `layer-input-source-blending` の `LayerInputSourceAggregator` / `inputSources` 必須化（D-5）/ 識別子規約 `[a-zA-Z0-9_.-]{1,64}`（D-6）/ ダブルバッファ + Volatile スレッドポリシー（D-7）/ 事前確保プール（D-14）のすべてを尊重する
  - `input-binding-persistence` の `InputBindingProfileSO` / `FacialInputBinder` には触れず、別アセット・別バインダーとして並走する
  - 既存 `FacialControlDefaultActions.inputactions` の `Trigger1〜Trigger12` 離散スロットは保持。アナログバインディング用 `InputAction` は本 spec で別 ActionMap（例: `Analog`）に追加する想定（具体名は design 段階で確定）

---

## Requirements

### Requirement 1: アナログ入力源の抽象化（Domain）

**Objective:** クリーンアーキテクチャ契約を維持したいライブラリ開発者として、アナログ入力源を Unity 非依存の Domain 抽象として表現したい。これにより EditMode テストで Fake を差し込めるようにし、Adapters 層には Engine 依存実装のみを置けるようにする。

#### Acceptance Criteria

1. The Analog Input Binding Service shall define `IAnalogInputSource` in the Domain layer as a Unity-independent contract that returns continuous-valued samples per frame, with shape variants for scalar (`float`), 2-axis (`Vector2`-equivalent value tuple), and N-axis (`ReadOnlySpan<float>`) outputs.
2. The Analog Input Binding Service shall require every `IAnalogInputSource` implementation to expose a stable identifier (string) matching the pattern `[a-zA-Z0-9_.-]{1,64}` consistent with the layer-input-source-blending identifier policy (D-6).
3. The Analog Input Binding Service shall require every `IAnalogInputSource` implementation to expose a validity flag (`IsValid`) so that disconnected / stale / not-yet-initialized sources can be treated as a zero contribution without raising an exception.
4. If an `IAnalogInputSource` reports an N-axis output whose length does not match the consumer's expected width, then the Analog Input Binding Service shall process only the overlapping range and shall leave the remaining indices unchanged, consistent with `layer-input-source-blending` Requirement 1.3.
5. The Analog Input Binding Service shall reside in the Unity-independent Domain layer for the abstraction itself and shall not reference `UnityEngine.InputSystem`, `UnityEngine.Transform`, or `UnityEngine.Animator` from the Domain layer.
6. When an `IAnalogInputSource` is queried in a frame where no fresh sample has arrived, the Analog Input Binding Service shall continue to return the most recently received sample value (last-valid policy) unless the adapter explicitly opts in to a staleness timeout, consistent with the OSC adapter's policy in `layer-input-source-blending` D-8.

### Requirement 2: マッピング関数（Domain 値型）

**Objective:** ライブラリ利用者として、生のアナログ入力をデッドゾーン・スケール・オフセット・カーブで補正してから出力に渡したいので、マッピング関数を宣言的な値型として表現できるようにしたい。

#### Acceptance Criteria

1. The Analog Mapping Function shall be a Unity-independent Domain value type that encapsulates the parameters dead-zone, scale (gain), offset (bias), curve type, inversion flag, output minimum, and output maximum.
2. The Analog Mapping Function shall support at least the curve types `Linear`, `EaseIn`, `EaseOut`, `EaseInOut`, and `Custom` (where `Custom` references a per-binding `AnimationCurve` evaluated by the Adapters layer; the Domain type itself stores only the curve identifier / handle, not the `AnimationCurve` instance).
3. When the Analog Mapping Function evaluates an input value `x`, it shall apply the transformations in the documented order (`dead-zone` → `scale` → `offset` → `curve` → `inversion` → `clamp(min, max)`) so that results are reproducible across frames and across implementations.
4. While the absolute value of the input is below the dead-zone threshold, the Analog Mapping Function shall output exactly zero (after re-centering) regardless of subsequent transformations, so that small noise does not cross subsequent stages.
5. If an Analog Mapping Function is configured with `min` greater than `max`, then the Analog Mapping Function shall reject construction with an `ArgumentException`.
6. The Analog Mapping Function shall not allocate managed heap memory in its per-frame evaluation hot path.
7. Where a binding is configured with no explicit mapping, the Analog Mapping Function shall default to identity behavior (dead-zone = 0, scale = 1, offset = 0, curve = `Linear`, inversion = false, min = 0, max = 1) so that defaults remain predictable.

### Requirement 3: BlendShape ターゲットへのバインディング

**Objective:** ARKit / PerfectSync の BlendShape 値や OSC の float パラメータで BlendShape を直接駆動したいライブラリ利用者として、アナログ入力源を `layer-input-source-blending` の BlendShape 値提供型アダプタとして接続したい。これにより既存のレイヤー内加重和ブレンドの恩恵を受けられる。

#### Acceptance Criteria

1. The Analog BlendShape Binding Adapter shall integrate as a BlendShape-value-provider type input source under the layer-input-source-blending hybrid model (D-1), exposing a `float[BlendShapeCount]` per frame consistent with the existing `LayerInputSourceAggregator` contract.
2. The Analog BlendShape Binding Adapter shall map each binding entry from one source axis (or one element of an N-axis array) to one target BlendShape index, applying the binding's Analog Mapping Function (Requirement 2) before writing.
3. When multiple binding entries target the same BlendShape index within the same adapter instance, the Analog BlendShape Binding Adapter shall combine them by summing their post-mapping outputs and shall rely on the layer-input-source-blending stage to perform the final `clamp01` (D-2) so that no double-clamping occurs at the adapter level.
4. The Analog BlendShape Binding Adapter shall reference target BlendShapes by name (string) and shall resolve the index at initialization time using the layer's BlendShape name list, consistent with the project-wide policy of not enforcing a BlendShape naming convention.
5. If a target BlendShape name does not exist in the active rig at initialization time, then the Analog BlendShape Binding Adapter shall log a warning via `Debug.LogWarning` and shall skip that binding entry without throwing.
6. The Analog BlendShape Binding Adapter shall reuse a single pre-allocated `float[BlendShapeCount]` buffer across frames and shall not allocate managed heap memory in its per-frame hot path (consistent with `layer-input-source-blending` Req 6.1 and the project-wide GC-zero target).
7. Where the input source is N-axis (e.g., the full ARKit BlendShape vector), the Analog BlendShape Binding Adapter shall support a "passthrough" binding mode that maps source-axis-`i` to target-BlendShape-`f(i)` via a name-based map, without requiring one binding entry per axis.
8. The Analog BlendShape Binding Adapter shall use a reserved input-source identifier in the namespace `analog-blendshape` (with optional sub-id like `analog-blendshape.arkit`) following the `[a-zA-Z0-9_.-]{1,64}` rule, distinct from the existing reserved ids `osc`, `lipsync`, `controller-expr`, `keyboard-expr`, and `input` (D-6).

### Requirement 4: BonePose ターゲットへのバインディング

**Objective:** スティック入力で目線・首の傾きを動かしたいライブラリ利用者として、アナログ入力を `BonePose` の Euler 成分にマッピングし、`bone-control` が公開した `IBonePoseProvider.SetActiveBonePose` 経由で `FacialController` に注入したい。これにより目線追従や顔ボーン制御が連続値で滑らかに動く。

#### Acceptance Criteria

1. The Analog BonePose Binding Adapter shall consume one or more `IAnalogInputSource` instances and shall produce a single `BonePose` (defined in `bone-control` Domain) per frame by mapping each binding entry to one (boneName, axis) pair where axis ∈ { X, Y, Z }.
2. The Analog BonePose Binding Adapter shall apply the Analog Mapping Function (Requirement 2) to convert the source value into a relative Euler degree along the specified axis, consistent with the face-relative axis interpretation defined in `bone-control` Requirement 4.
3. The Analog BonePose Binding Adapter shall reference target bones by name (string) consistent with `bone-control` Requirement 2 (name-based primary identifier, no naming convention enforced, multi-byte / special characters allowed).
4. Where the target rig is Humanoid and the user opts in, the Analog BonePose Binding Adapter shall accept bone names resolved via `bone-control`'s `HumanoidBoneAutoAssigner` (Adapters/Bone) so that callers do not have to type bone names manually for standard slots.
5. When the Analog BonePose Binding Adapter has computed the per-frame `BonePose`, it shall call `IBonePoseProvider.SetActiveBonePose(in BonePose)` exactly once per frame so that bone-control's BoneWriter applies the new pose starting from the next frame's apply step (consistent with `bone-control` Requirement 11.2).
6. If multiple binding entries target the same (boneName, axis) pair within the same adapter instance, then the Analog BonePose Binding Adapter shall sum their post-mapping outputs into the single Euler degree value, with no implicit clamp; clamping is the responsibility of the Analog Mapping Function's `min` / `max` stage (Requirement 2.3).
7. The Analog BonePose Binding Adapter shall reuse a pre-allocated `BonePose` entry buffer across frames so that no managed heap allocation occurs in its per-frame hot path, consistent with `bone-control` Requirement 6.1 and 11.5.
8. While the adapter has zero registered bindings or all sources report `IsValid == false`, the Analog BonePose Binding Adapter shall publish an empty `BonePose` (zero entries) so that `bone-control`'s BoneWriter leaves bone Transforms untouched (consistent with `bone-control` Requirement 5.4 and 10.1).
9. The Analog BonePose Binding Adapter shall not require any modification to `bone-control`'s public API (`IBonePoseProvider`, `IBonePoseSource`, `FacialController.SetActiveBonePose`); it shall consume those interfaces as-is.

### Requirement 5: 入力源アダプタ実装（InputSystem / OSC / ARKit）

**Objective:** Unity ランタイムで実機入力を取り回したいライブラリ利用者として、InputSystem / OSC / ARKit の各入力経路を `IAnalogInputSource` の具体実装として提供してほしい。

#### Acceptance Criteria

1. The InputAction Analog Source shall implement `IAnalogInputSource` and shall read continuous values from a `UnityEngine.InputSystem.InputAction` of `ControlType` `Axis`, `Vector2`, or float-compatible type, exposing the value through the appropriate scalar / 2-axis / N-axis variant.
2. When the bound `InputAction` is disabled or unbound, the InputAction Analog Source shall report `IsValid == false` and shall not raise an exception.
3. The OSC Analog Source shall implement `IAnalogInputSource` and shall consume float parameter values received via the existing `com.hidano.facialcontrol.osc` package (VRChat-compatible `/avatar/parameters/{name}` format), exposing each parameter as a scalar source identified by the parameter name.
4. Where the OSC Analog Source is configured with an `options.staleness_seconds` value greater than zero, the OSC Analog Source shall mark itself `IsValid == false` when the most recent OSC packet for the bound parameter is older than that threshold, consistent with `layer-input-source-blending` Requirement 5.5 / D-8; when the option is absent or zero, the OSC Analog Source shall maintain the last-valid-value policy indefinitely (Requirement 1.6).
5. The ARKit BlendShape Analog Source shall implement `IAnalogInputSource` as an N-axis source whose axis count equals the number of detected ARKit / PerfectSync BlendShape parameters, so that all 52 ARKit channels can be exposed through a single source instance and consumed by the Analog BlendShape Binding Adapter's passthrough mode (Requirement 3.7).
6. The Input Source Adapters layer shall not require Domain-layer changes when adding a new analog source type; new source types shall be added by implementing `IAnalogInputSource` in the Adapters layer only.
7. The Input Source Adapters layer shall reside under `Adapters/Input` (for InputSystem) and `Adapters/InputSources` (for OSC / ARKit / lipsync-equivalent analog) following the existing project structure, and shall reference Engine APIs only from the Adapters layer.

### Requirement 6: 永続化（AnalogInputBindingProfileSO + JSON）

**Objective:** JSON ファースト永続化方針を維持したいライブラリ利用者として、アナログバインディングを ScriptableObject および JSON で永続化し、ビルド後の差し替えにも対応したい。

#### Acceptance Criteria

1. The Analog Input Binding Persistence shall provide an `AnalogInputBindingProfileSO` ScriptableObject distinct from the existing `InputBindingProfileSO` (which handles discrete trigger bindings only), so that analog bindings and discrete trigger bindings can be authored and assigned independently.
2. The `AnalogInputBindingProfileSO` shall serialize a list of binding entries where each entry contains: source identifier (string), source axis selector (scalar / vector index / blendshape name), target kind (`BlendShape` or `BonePose`), target identifier (BlendShape name or bone name), target axis (for BonePose: X / Y / Z; for BlendShape: ignored), and the Analog Mapping Function parameters (Requirement 2).
3. The Analog Input Binding JSON Schema shall serialize an `AnalogInputBindingProfile` as a JSON document whose top-level key set includes at minimum `bindings` (array of binding entry objects), and shall stay forward-evolvable in the preview phase (additive fields allowed; breaking changes acceptable per project-wide preview policy).
4. The Analog Input Binding JSON Loader shall deserialize a JSON document into a runtime `AnalogInputBindingProfile` Domain instance at runtime without requiring Editor-only code paths, consistent with the project's runtime-JSON-parse policy.
5. If the JSON Loader encounters a malformed entry (unknown source id, unknown target kind, missing target identifier, non-numeric mapping parameter), then it shall log a warning via `Debug.LogWarning`, skip that entry, and continue loading the remaining document.
6. The Analog Input Binding Persistence shall support importing a JSON file into an `AnalogInputBindingProfileSO` and exporting an `AnalogInputBindingProfileSO` to a JSON file, consistent with the project's JSON-first import/export convention used by `FacialProfileSO`.
7. When loading an `AnalogInputBindingProfileSO` whose binding list is empty, the Analog Input Binding Persistence shall produce a profile that contributes zero to BlendShape values and an empty `BonePose`, without raising an exception.
8. The Analog Input Binding JSON Schema shall not duplicate identifiers reserved by `layer-input-source-blending` (D-6: `osc`, `lipsync`, `controller-expr`, `keyboard-expr`, `input`); analog source identifiers introduced by this spec shall use distinct namespaces (e.g., `analog-blendshape`, `analog-bonepose`, or sub-ids under those namespaces) and third-party adapters shall use the `x-` prefix convention.

### Requirement 7: ランタイム制御 API

**Objective:** シーンの状況に応じてアナログバインディングを切替えたいライブラリ利用者として、ランタイム API でプロファイルの有効化・無効化・差替えを安全に行いたい。

#### Acceptance Criteria

1. The Analog Input Binding Runtime shall expose a `FacialAnalogInputBinder` MonoBehaviour-equivalent component that holds references to a `FacialController` and an `AnalogInputBindingProfileSO`, analogous to the existing `FacialInputBinder` for discrete trigger bindings but operating independently.
2. When the `FacialAnalogInputBinder` is enabled (`OnEnable`), it shall instantiate the configured input source adapters from the profile, register the BlendShape-target bindings as a BlendShape-value-provider input source on the corresponding layers (per Requirement 3), and connect the BonePose-target bindings to the `FacialController`'s `IBonePoseProvider` (per Requirement 4).
3. When the `FacialAnalogInputBinder` is disabled (`OnDisable`), it shall unregister all of its bindings and shall release any input source adapters it owns, leaving other input sources (controller-expr, keyboard-expr, OSC discrete, lipsync, etc.) untouched.
4. While a binding profile swap is requested at runtime, the Analog Input Binding Runtime shall apply the new profile starting from the next frame's evaluation without interrupting ongoing expression transitions on other layers, consistent with `layer-input-source-blending` Requirement 4.2.
5. If a runtime profile assignment refers to a layer that does not exist on the active `FacialController` profile, then the Analog Input Binding Runtime shall log a warning via `Debug.LogWarning` and shall skip the binding entries targeting that layer without throwing.
6. While the `FacialAnalogInputBinder` is operating concurrently with the discrete-trigger `FacialInputBinder` on the same scene, the Analog Input Binding Runtime shall not interfere with discrete trigger binding registration; both binders shall coexist as independent layers / input sources.
7. The Analog Input Binding Runtime shall not require modifications to `bone-control`'s `FacialController.SetActiveBonePose` API surface; it shall consume that API as-is.

### Requirement 8: パフォーマンスと GC 非発生

**Objective:** 毎フレームで連続値を流し続ける性質上、性能劣化を避けたいライブラリ利用者として、アナログバインディング全体が毎フレーム ヒープ確保ゼロで動作することを保証したい。

#### Acceptance Criteria

1. The Analog Input Binding Service shall perform per-frame mapping and dispatch with zero managed heap allocations when the set of (sources, bindings, targets) is unchanged.
2. The Analog Input Binding Service shall pre-allocate, at profile load time, all per-source intermediate buffers (`float[]` for N-axis, scratch BlendShape buffer, BonePose entry buffer) so that the per-frame hot path performs only value updates, consistent with `layer-input-source-blending` D-14 (pre-allocated pool).
3. When the number of binding entries is N, the Analog Input Binding Service shall complete per-frame evaluation in O(N) time without hidden quadratic behavior (e.g., no per-frame name → index linear scans; index resolution shall be cached at initialization time).
4. The Analog Input Binding Service shall not introduce per-character initialization work that scales worse than linearly in `bindingsPerChar * charCount` (consistent with `layer-input-source-blending` Requirement 6.5 and the project-wide 10-character simultaneous control target).
5. If a profiling test detects per-frame heap allocation in the Analog Input Binding Service's hot path (mapping evaluation, BlendShape buffer write, BonePose entry buffer write, `SetActiveBonePose` call), then the test suite shall fail.
6. While the input source layer requires non-main-thread updates (e.g., OSC receiver thread writing latest sample), the Analog Input Binding Service shall use a double-buffered pending / active sample buffer with volatile read / write semantics consistent with `layer-input-source-blending` D-7, without requiring callers to take locks explicitly.

### Requirement 9: 既存 spec API との非破壊統合

**Objective:** プロジェクト全体の安定性を維持したいライブラリ開発者として、本 spec の導入が `bone-control` / `layer-input-source-blending` / `input-binding-persistence` の公開 API・JSON スキーマを破壊しないことを保証したい。

#### Acceptance Criteria

1. The Analog Input Binding Service shall consume `bone-control`'s `IBonePoseProvider`, `IBonePoseSource`, and `FacialController.SetActiveBonePose` interfaces without modifying their signatures, parameter shapes, or call conventions (`in BonePose` pass-by-readonly-reference must be preserved as-is).
2. The Analog Input Binding Service shall integrate with `layer-input-source-blending`'s `LayerInputSourceAggregator` exclusively through its existing BlendShape-value-provider input-source contract (`float[BlendShapeCount]` per frame) and shall not require modifications to the aggregator's public API.
3. The Analog Input Binding Service shall not require changes to the existing `InputBindingProfileSO` (discrete trigger bindings) or `FacialInputBinder` (discrete trigger binder); discrete trigger bindings and analog bindings shall coexist as independent assets and runtime components.
4. When the existing `FacialControlDefaultActions.inputactions` is loaded by an end user, the Analog Input Binding Service shall not require existing `Trigger1`〜`Trigger12` slots to be repurposed; analog bindings shall consume separately authored `InputAction`s (in a separate ActionMap such as `Analog`) so that the discrete trigger workflow remains unchanged.
5. While an existing scene uses only discrete trigger bindings (no analog binder attached), the Analog Input Binding Service shall not be loaded or activated, and shall not introduce per-frame cost for that scene.
6. If a future schema change to `AnalogInputBindingProfile` JSON would break existing JSON files outside the preview window, then the project shall publish a migration path; during preview, breaking changes are acceptable per project-wide preview policy (`docs/requirements.md` FR-001).

### Requirement 10: Editor 拡張（最小スコープ）

**Objective:** Unity Editor で簡易的にアナログバインディングを確認・編集したいライブラリ利用者として、JSON 直編集を前提としつつ、最低限の Inspector 表示とインポート / エクスポート操作を提供してほしい。

#### Acceptance Criteria

1. The AnalogInputBindingProfileSO Inspector shall provide a UI Toolkit-based read-only summary view that lists current binding entries with source id, target kind, target identifier, and mapping summary (curve type, scale, offset).
2. The AnalogInputBindingProfileSO Inspector shall provide JSON import and JSON export buttons, consistent with the project's JSON-first import / export convention used by `FacialProfileSO`.
3. Where the bound rig is Humanoid and the user opts in, the AnalogInputBindingProfileSO Inspector shall offer a one-click action that populates eye / head / neck bone names for BonePose-target bindings via `bone-control`'s `HumanoidBoneAutoAssigner`, consistent with `bone-control` Requirement 9.3.
4. The AnalogInputBindingProfileSO Inspector shall not provide any runtime UI; analog binding editing is Editor-only, consistent with the project-wide policy of no runtime UI.
5. The AnalogInputBindingProfileSO Inspector shall be implemented in UI Toolkit; IMGUI shall not be used for new UI, consistent with the project-wide tech standard (steering `tech.md`).
6. Where a full GUI for mapping curve editing is requested, the AnalogInputBindingProfileSO Inspector shall defer that scope to a later spec; for the preview release, JSON direct editing is the canonical authoring path.

### Requirement 11: サンプル提供（preview.1 同梱）

**Objective:** ライブラリ利用者として、最小構成のアナログバインディング動作例をサンプルから即座に参照したいので、preview.1 リリースに 2 系統のアナログバインディングサンプルを同梱してほしい。

#### Acceptance Criteria

1. The Analog Input Binding Sample shall provide a sample asset under `Packages/com.hidano.facialcontrol.inputsystem/Samples~/AnalogBindingDemo/` (UPM canonical) and a mirror under `FacialControl/Assets/Samples/` (dev mirror) per the project's Samples dual-management rule.
2. The Analog Input Binding Sample shall demonstrate a BonePose-target binding that maps the right gamepad analog stick (`Vector2`) to the LeftEye / RightEye bones' Y / X Euler degrees through `IBonePoseProvider.SetActiveBonePose`, so that the user can verify gaze control with a controller.
3. The Analog Input Binding Sample shall demonstrate a BlendShape-target binding that maps an ARKit / PerfectSync `jawOpen` parameter (or an OSC `/avatar/parameters/jawOpen` float, configurable via the sample's binding JSON) to the rig's mouth-open BlendShape via the layer-input-source-blending pipeline.
4. The Analog Input Binding Sample shall ship a sample `AnalogInputBindingProfileSO` asset and a corresponding `analog_binding_demo.json` file demonstrating the JSON schema for the bindings above.
5. When the user runs the sample scene, the Analog Input Binding Sample shall produce visible eye-gaze movement following the right stick and visible mouth-open driven by `jawOpen`, without requiring code modifications by the user.
6. The Analog Input Binding Sample shall not depend on any specific 3D model bundled with the package; the sample shall document that the user must supply a Humanoid-compatible rig with at least eye bones and a mouth-open BlendShape, consistent with the project's release policy that models are user-supplied.
