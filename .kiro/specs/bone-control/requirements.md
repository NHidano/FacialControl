# Requirements Document

## Project Description (Input)
bone-control: 3D キャラクターの顔回り（特に眼球・頭・首）に対するボーン回転制御の基盤パイプライン。preview.2 で予定される視線追従・カメラ目線、および後続 spec の analog-input-binding（スティック入力でボーン駆動）の前提となる。

## 背景・動機
- 現状の FacialControl は BlendShape 値の合成・書込のみ対応で、ボーン回転を扱う本番コードは Runtime 層に 1 行も存在しない（Expression.cs は BlendShapeValues と LayerSlots のみ保持）。
- ARKit / PerfectSync 互換 BlendShape では eyeLookIn/Out/Up/Down がモデル側に必要だが、実用上は目線をボーンで動かしたいケースが多く、要件定義 (docs/requirements.md:144-145, technical-spec.md:28,872) でも「ボーン制御 / BlendShape 制御の両対応」が明記されている。

## スコープ
- BonePose ドメインモデル（Expression に紐付かない独立した姿勢データ構造）
  - ボーン参照は string 名指定（VRM / FBX 双方で使えるよう Humanoid 非前提）
  - Humanoid モデルの場合のみ HumanBodyBones.LeftEye / RightEye 等から「目に設定されたボーンを自動アサインする」オプション機能を提供する
  - 回転は Euler (degrees) 指定。X/Y/Z 各軸の角度を保持する
  - **重要**: 軸基準は **顔（head/neck）からの相対ローカル軸**。理由: 既に身体のモーションが入った状態で初期化されるケースがあり、ワールド軸前提だと身体の傾きが混入して目線がずれる
- ボーン Transform 書戻しアダプタ（Adapters/Bone レイヤー）
  - 毎フレーム BonePose を Transform.localRotation 等へ反映
  - 既存 BlendShape パイプラインと並走可能（FacialControlMixer 同様の合成ステージとして組込み可能であること）
- JSON 永続化スキーマ拡張（BonePose を JSON で保存・ロード可能にする）
- ScriptableObject 変換（BonePose のランタイム JSON パース + Editor アセット保存）
- Editor 拡張（FacialProfileSO で BonePose を編集する UI）

## スコープ外
- スティック入力からボーン駆動への接続は analog-input-binding spec の責務（C → A の順で、本 spec が C 側の基盤）
- 視線追従の Vector3 ターゲット指定（preview.2 後半で別 spec を切る）
- カメラ目線の自動制御（同上）
- 物理演算ベースの首振り（MagicaCloth2 等）

## 制約・前提
- Domain 層は Unity 非依存。Adapters/Bone のみ Transform / Animator / HumanBodyBones を参照する
- 毎フレームのヒープ確保ゼロ（Req 6.1 と整合）
- 既存 BlendShape ベース機能と互換性維持（BonePose を持たない既存 FacialProfile はそのまま動く）
- Domain 層 Bone 参照は名前のみ（Transform 参照は Adapters 層で解決）

## 用語
- **BonePose**: ボーンに対する姿勢オーバーライド情報。1 つの BonePose は (boneName, relativeEulerXYZ) を 0 個以上の bone について保持する集合。
- **顔相対軸**: BonePose 適用時に基準となる head/neck ボーンの localRotation を起点とした相対座標系。BonePose の Euler 値はこの座標系上のオフセット回転として解釈される。
- **自動アサイン**: Animator が Humanoid Avatar を持つ場合に、HumanBodyBones.LeftEye / RightEye / Head / Neck 等の標準スロットから目用ボーン名を取り出す Editor / ランタイム補助機能。

## 関連 spec
- 本 spec (bone-control) 完了後、analog-input-binding spec で「Vector2 入力 → BonePose 駆動」を組む（C → A の順）。
- 本 spec の API は analog-input-binding に対し「現在の BonePose を上書き・取得する」手段を公開する責務がある。

## Introduction

bone-control は、3D キャラクターの顔回りボーン（眼球・頭・首を主対象）に対する回転制御を、既存 BlendShape パイプラインから独立した第一級のサブシステムとして導入する基盤 spec である。本 spec は後続 spec `analog-input-binding`（C → A の C 側）の前提となり、スティック入力でのボーン駆動を可能にするために必要な「BonePose ドメインモデル」「Adapters/Bone 書戻しアダプタ」「JSON / ScriptableObject 永続化」「Editor 編集 UI」を提供する。設計の核は次の 3 点である:
1. ボーン参照は **名前 (string)** が一次手段であり、Humanoid モデル向けに `HumanBodyBones` ベースの自動アサインを **オプション** として上乗せする（VRM / FBX の非 Humanoid を排除しない）。
2. `BonePose` は `Expression` から **完全に分離** された独立データ構造であり、Expression は BonePose を参照することはあっても内包しない。理由はモデルごとに base/rest 回転が異なり、Expression 単位での埋込みは再利用性を損なうため。
3. Euler 指定の **基準軸は head（または neck）の現在 localRotation** であり、ワールド軸ではない。実装は `head.localRotation * RotationFromEuler(bonePose.relativeEulerXYZ)` 相当の合成によって対象ボーンへ適用しなければならない。

## Boundary Context

- **In scope**:
  - `BonePose` ドメインモデル（Unity 非依存）と関連バリデーション
  - 名前ベースのボーン参照、および Humanoid 向け自動アサイン補助
  - 顔相対 Euler の合成ロジック仕様
  - `Adapters/Bone` における Transform 書戻し
  - 既存 BlendShape ミキシングパイプラインへの並走組込み
  - BonePose の JSON シリアライズ / デシリアライズ
  - BonePose の ScriptableObject 化（ランタイム JSON パース + Editor アセット保存）
  - `FacialProfileSO` Inspector での BonePose 編集 UI（UI Toolkit）
  - 既存 FacialProfile（BonePose 未保有）の後方互換動作
- **Out of scope**:
  - スティック / アナログ入力 → BonePose 駆動（`analog-input-binding` spec 側）
  - Vector3 ターゲット / カメラ目線追従の自動化（preview.2 後半の別 spec）
  - 物理ベースの首振り・揺れもの（MagicaCloth2 等）
- **Adjacent expectations**:
  - `analog-input-binding` spec は本 spec が公開する「現在の BonePose を上書き・取得する API」を消費する
  - 既存 BlendShape Mixer / FacialProfile / FacialProfileSO 周辺は破壊的変更を受けず、BonePose 機能は加算的に提供される

## Requirements

### Requirement 1: BonePose ドメインモデル（独立エンティティ）
**Objective:** Unity 非依存ドメインを設計するライブラリ開発者として、`BonePose` を `Expression` から分離した独立エンティティとして定義したい。これによりモデルごとに異なる base/rest 回転に左右されず、BonePose を単独で構築・永続化・適用・再利用できるようにする。

#### Acceptance Criteria
1. The BonePose Domain Model shall expose `BonePose` as a first-class domain entity that is independently constructible, persistable, and applicable, decoupled from `Expression`.
2. The BonePose Domain Model shall represent a `BonePose` as a collection of zero or more entries, where each entry pairs a bone name (string) with a relative Euler rotation (X, Y, Z in degrees).
3. The BonePose Domain Model shall reside in the Unity-independent Domain layer and shall not reference `UnityEngine.Transform`, `UnityEngine.Animator`, or `UnityEngine.HumanBodyBones`.
4. Where a `FacialProfile` or `Expression` opts in, the BonePose Domain Model shall allow that profile/expression to **reference** a `BonePose` by identity without **embedding** the BonePose data inside the `Expression` structure itself.
5. When an existing `FacialProfile` or `Expression` does not reference any `BonePose`, the BonePose Domain Model shall treat it as a profile with no bone overrides and shall not require schema migration.
6. If a `BonePose` entry is constructed with a null or empty bone name, then the BonePose Domain Model shall reject the entry as invalid.
7. If two entries within the same `BonePose` declare the identical bone name, then the BonePose Domain Model shall reject construction as invalid (one bone, one entry).

### Requirement 2: 名前ベースのボーン参照（一次手段）
**Objective:** VRM / FBX を含む非 Humanoid モデルもサポートしたいライブラリ利用者として、ボーンを名前 (string) で指定したい。これにより Humanoid Avatar を持たないモデルでも BonePose を適用できる。

#### Acceptance Criteria
1. The Bone Reference Resolver shall accept a bone name (string) as the primary identifier of a bone in `BonePose` entries.
2. The Bone Reference Resolver shall support bone names containing multi-byte characters (e.g., 日本語) and special symbols, consistent with the project-wide blend-shape naming policy.
3. When the Bone Reference Resolver receives a non-Humanoid rig (VRM / FBX without Humanoid Avatar), it shall still resolve bones by name without requiring `HumanBodyBones`.
4. If the specified bone name does not exist in the target rig at apply-time, then the Adapters/Bone layer shall log a warning via `Debug.LogWarning` and shall skip that entry without throwing.
5. The Bone Reference Resolver shall not impose any naming convention (no fixed prefix/suffix) on bone names.

### Requirement 3: Humanoid 向け自動アサイン補助（オプトイン）
**Objective:** Humanoid Avatar を使うライブラリ利用者として、目・頭・首ボーン名を `HumanBodyBones` から自動取得するオプション機能を使いたい。これによりボーン名の手入力を省略できる。

#### Acceptance Criteria
1. Where the target rig has a Humanoid `Animator` and the user opts in to auto-assign, the Humanoid Auto-Assign Helper shall resolve eye bone names from `HumanBodyBones.LeftEye` and `HumanBodyBones.RightEye`.
2. Where the target rig has a Humanoid `Animator` and the user opts in to auto-assign, the Humanoid Auto-Assign Helper shall resolve the relative-axis basis bone name from `HumanBodyBones.Head` (with `HumanBodyBones.Neck` as a configurable fallback).
3. The Humanoid Auto-Assign Helper shall be **opt-in** and shall not be the only path to populate a `BonePose`; name-based binding shall remain fully usable without invoking the helper.
4. If the target rig is non-Humanoid or the corresponding `HumanBodyBones` slot is unmapped, then the Humanoid Auto-Assign Helper shall return an empty result for that slot and shall log a warning via `Debug.LogWarning` instead of throwing.
5. The Humanoid Auto-Assign Helper shall reside in the Adapters/Bone layer (not in Domain) because it depends on `UnityEngine.Animator` and `UnityEngine.HumanBodyBones`.

### Requirement 4: 顔相対 Euler 軸の解釈
**Objective:** 身体モーション込みの初期姿勢で BonePose を運用したいライブラリ利用者として、Euler 値が **head（または neck）の現在 localRotation を基準としたローカル軸** として解釈されることを保証したい。これにより身体の傾きが目線へ漏れない。

#### Acceptance Criteria
1. The BonePose Domain Model shall represent rotation as Euler degrees (X, Y, Z) as the surface (serialization / authoring) representation.
2. The Bone Apply Pipeline shall interpret each `BonePose` entry's Euler value as an offset rotation in the **face-relative local axis** whose basis is the basis bone's (`Head`, or `Neck` when configured) current `localRotation` at apply-time.
3. The Bone Apply Pipeline shall **not** interpret BonePose Euler values as world-axis rotations.
4. When applying a `BonePose` entry to a target bone, the Bone Apply Pipeline shall compose the final rotation as `basisBone.localRotation * RotationFromEuler(entry.relativeEulerXYZ)` (or a mathematically equivalent composition) before writing to the target bone.
5. While body / neck animation is already driving the rig at the moment of apply, the Bone Apply Pipeline shall still produce eye-gaze offsets that remain stable relative to the face and shall not let upstream body tilt leak into the gaze direction.
6. If the configured basis bone (Head / Neck) cannot be resolved on the target rig, then the Bone Apply Pipeline shall log a warning via `Debug.LogWarning` and shall skip BonePose application for that frame instead of falling back to world axis.

### Requirement 5: Adapters/Bone 書戻しアダプタ
**Objective:** Unity ランタイム上でボーン姿勢を実反映したいライブラリ利用者として、BonePose を毎フレーム Transform に書き戻すアダプタが欲しい。これにより BlendShape と同じ更新タイミングで顔ボーンが動く。

#### Acceptance Criteria
1. The Adapters/Bone layer shall be the **only** layer in the codebase that touches `UnityEngine.Transform`, `UnityEngine.Animator`, and `UnityEngine.HumanBodyBones`.
2. When a frame's facial mixing pipeline runs, the Adapters/Bone Writer shall write the resolved per-bone `localRotation` for every bone referenced by the active `BonePose`.
3. The Adapters/Bone Writer shall execute as a stage that runs alongside the existing BlendShape mixing pipeline (analogous to `FacialControlMixer`), so that bone control and blend-shape control can be applied in the same frame.
4. If no active `BonePose` is set for the current frame, then the Adapters/Bone Writer shall not modify any bone Transform and shall leave upstream animation results untouched.
5. The Adapters/Bone Writer shall apply rotations in a deterministic, documented order (e.g., basis bone resolution first, then per-entry composition) so that results are reproducible across frames.
6. The Adapters/Bone Writer shall expose an API surface allowing external callers (notably the future `analog-input-binding` spec) to **set / override / read** the currently active `BonePose` at runtime.

### Requirement 6: 毎フレーム ゼロヒープ確保
**Objective:** プロジェクト全体の GC スパイク対策方針に整合させたいライブラリ開発者として、BonePose の評価・適用が毎フレーム ヒープ確保ゼロで動作することを保証したい。

#### Acceptance Criteria
1. The Bone Apply Pipeline shall not allocate managed heap memory in its per-frame hot path (BonePose lookup, basis-bone composition, Transform write).
2. The Adapters/Bone Writer shall reuse pre-allocated buffers / structs for intermediate rotation values across frames.
3. While the same `BonePose` instance is active across multiple frames, the Bone Apply Pipeline shall not re-allocate its internal entry collection.
4. If a profiling test detects per-frame heap allocation in the Bone Apply Pipeline, then the test suite shall fail.

### Requirement 7: JSON 永続化スキーマ拡張
**Objective:** JSON ファースト永続化方針を維持したいライブラリ利用者として、BonePose を JSON で保存・ロードしたい。これによりビルド後でも JSON 差し替えで顔ボーン姿勢を変更できる。

#### Acceptance Criteria
1. The JSON Schema shall serialize a `BonePose` as a structure containing an entry list, where each entry stores `boneName` (string) and `relativeEulerXYZ` (three float degrees).
2. The JSON Loader shall deserialize a `BonePose` JSON document into a Domain `BonePose` instance at runtime without requiring Editor-only code paths.
3. When the JSON Loader encounters a JSON document that omits any `BonePose` field, it shall produce a `FacialProfile` / `Expression` that has no bone-pose reference and shall preserve full backward compatibility with existing JSON files authored before bone-control.
4. If the JSON Loader encounters a malformed `BonePose` entry (missing `boneName`, non-numeric Euler component, etc.), then it shall log a warning via `Debug.LogWarning`, skip that entry, and continue loading the remaining document.
5. The JSON Schema shall remain forward-evolvable in the preview phase (additive fields allowed; breaking changes acceptable per project-wide preview policy).

### Requirement 8: ScriptableObject 変換
**Objective:** Unity 標準ワークフローを使いたいライブラリ利用者として、BonePose を ScriptableObject アセットとして保存・参照したい。これにより Editor 上で他アセットから参照可能になる。

#### Acceptance Criteria
1. The BonePose ScriptableObject Converter shall convert a Domain `BonePose` instance to a `ScriptableObject` representation usable as a Unity asset.
2. The BonePose ScriptableObject Converter shall convert a `ScriptableObject` representation back to a Domain `BonePose` instance for runtime application.
3. When invoked at runtime, the BonePose ScriptableObject Converter shall perform JSON-to-`ScriptableObject` parsing without requiring Editor-only assemblies.
4. Where Editor mode is active, the BonePose ScriptableObject Converter shall additionally support saving the `ScriptableObject` as a `.asset` file under the project's asset hierarchy.
5. The BonePose ScriptableObject Converter shall preserve round-trip equality of bone names and Euler values (JSON → SO → Domain → JSON).

### Requirement 9: Editor 拡張（FacialProfileSO 上の BonePose 編集 UI）
**Objective:** Editor で表情を編集する Unity エンジニアとして、`FacialProfileSO` Inspector から BonePose を直接編集したい。これにより BonePose の追加・削除・回転値編集を一元的に扱える。

#### Acceptance Criteria
1. The FacialProfileSO Inspector Extension shall provide a UI Toolkit-based editor panel for managing `BonePose` references on a `FacialProfileSO`.
2. When the user adds a bone entry via the Inspector, the FacialProfileSO Inspector Extension shall accept a bone name (string) and X / Y / Z Euler degrees as input.
3. Where the bound rig is Humanoid and the user clicks the auto-assign action, the FacialProfileSO Inspector Extension shall populate eye / head / neck bone names via the Humanoid Auto-Assign Helper (Requirement 3).
4. When the user removes a bone entry, the FacialProfileSO Inspector Extension shall remove that entry from the underlying `BonePose` and persist the change.
5. The FacialProfileSO Inspector Extension shall support importing a `BonePose` from a JSON file and exporting the current `BonePose` to a JSON file, consistent with the project's JSON-first persistence convention.
6. The FacialProfileSO Inspector Extension shall not provide any runtime UI; bone editing is Editor-only (consistent with the project-wide policy of no runtime UI).

### Requirement 10: 既存パイプラインとの後方互換性
**Objective:** 既存 BlendShape ベース機能を壊したくないライブラリ利用者として、BonePose を持たない既存 `FacialProfile` がそのまま動作することを保証したい。

#### Acceptance Criteria
1. While a `FacialProfile` has no `BonePose` reference, the Bone Apply Pipeline shall not alter any bone Transform on the target rig.
2. When loading a JSON file authored before bone-control was introduced, the JSON Loader shall succeed without errors and shall produce a `FacialProfile` whose runtime behavior is identical to the pre-bone-control behavior.
3. The Adapters/Bone Writer shall not change the existing BlendShape mixing pipeline's outputs (BlendShape values, layer slots) when activated.
4. If a future schema change to `BonePose` would break existing JSON files outside the preview window, then the project shall publish a migration path; during preview, breaking changes are acceptable per project-wide preview policy.

### Requirement 11: 後続 spec (analog-input-binding) 向け API 公開
**Objective:** 次の spec で Vector2 入力からボーンを駆動したい開発者として、本 spec が「現在の BonePose を上書き・取得する」ランタイム API を公開していることを保証したい。

#### Acceptance Criteria
1. The Adapters/Bone Writer shall expose a runtime API for setting the currently active `BonePose` (override) and for retrieving the currently active `BonePose` (read).
2. When an external caller sets a new `BonePose` via the API, the Adapters/Bone Writer shall apply the new pose starting from the next frame's apply step.
3. The exposed API shall be callable from the future `analog-input-binding` spec without requiring changes to Domain-layer types.
4. The exposed API shall not assume any specific input source (stick, OSC, gaze target, etc.); it shall accept any caller that produces a valid `BonePose`.
5. The exposed API shall not allocate managed heap memory per call in its hot path (consistent with Requirement 6).
