# Research & Design Decisions: inspector-and-data-model-redesign

---
**Purpose**: Inspector UX 改善とデータモデル簡素化（破壊的改修）の設計判断を支える discovery 結果と決定根拠を記録する。

**Usage**:
- `design.md` の本文では分量制約のため概要しか書けない決定の根拠・代替案・トレードオフをここに残す。
- 設計レビュー時の audit trail。
- 実装フェーズで再判断が必要になったときの参照点。
---

## Summary

- **Feature**: `inspector-and-data-model-redesign`
- **Discovery Scope**: Complex Integration（既存 brownfield コードベースに対する破壊的改修。13 Requirements, gap-analysis 済み）
- **Key Findings**:
  - **AnimationClip 参照を Domain に持ち込めない**（Req 13.1 / 13.4 の asmdef 契約）。Editor 専用サンプラを Adapters/Editor 境界に配置し、Domain は中間 JSON snapshot だけを参照する。
  - **Unity InputSystem の `InputProcessor<T>` は stateless 必須かつ AnimationCurve をパラメータとして安全に持てない**。よって "curve" processor は preset 4 種 + 簡易キーフレーム文字列で実装し、AnimationClip の curve 完全復元は中間 JSON 側に任せる。
  - **AnimationClip メタデータ運搬は AnimationEvent (`functionName="FacialControlMeta_Set"` + `stringParameter` + `floatParameter`) を採用**する。clip events は Editor で編集可能・float / string を埋め込める・AnimationClip 単体に同梱できる、という 3 条件を満たす唯一の手段。
  - **中間 JSON schema v2.0 は "snapshot table" 形式**（時刻 0 における BlendShape 値・bone 姿勢 + 遷移メタ）。curve-preserving 案は 0-alloc 契約と合わない。
  - **`AnalogBindingEntry` は Domain / Adapters の二層分離**: Domain は `SourceId + targetIdentifier + targetAxis + targetKind` のみ、Adapters Serializable は `InputActionReference + targetIdentifier + targetAxis` のみ。`Mapping` は両層から完全撤去。

## Research Log

### Topic 1: AnimationClip メタデータ（TransitionDuration / TransitionCurve）の運搬手段（Req 2.4–2.6）

- **Context**: Expression は AnimationClip 1 つから派生プロパティを全て自動取得するが、AnimationClip 自体は BlendShape / Transform カーブしか持てないため、TransitionDuration（秒）と TransitionCurve（補間カーブ）をどこに埋めるかを決めなければ Req 2.4–2.6 を満たせない。
- **Sources Consulted**:
  - [Unity ScriptReference: AnimationEvent](https://docs.unity3d.com/ScriptReference/AnimationEvent.html)
  - [Unity ScriptReference: AnimationEvent.floatParameter](https://docs.unity3d.com/ScriptReference/AnimationEvent-floatParameter.html)
  - [Unity Manual: Use Animation Events](https://docs.unity3d.com/Manual/script-AnimationWindowEvent.html)
- **Findings**:
  - `AnimationEvent` は `functionName: string` + `floatParameter: float` + `intParameter: int` + `stringParameter: string` + `objectReferenceParameter: Object` を 1 イベントに同梱できる。
  - `AnimationUtility.GetAnimationEvents(clip)` で Editor から取得可能。`AnimationUtility.SetAnimationEvents(clip, events)` で書き戻せる（Editor 専用）。
  - clip events は AnimationClip アセットに同梱されるため、AnimationClip を別プロジェクトへコピーしてもメタデータが失われない（companion ScriptableObject 案の最大の弱点を解消）。
  - clip events は MonoBehaviour 駆動のコールバックを意図しているが、本仕様ではコールバック発火を一切意図しない（Editor サンプラは event 配列を「メタデータの読み書き専用」として参照するのみ）。Runtime 側は AnimationClip を再生しないため、未解決 functionName による警告は発生しない。
- **Implications**:
  - **採用**: `functionName == "FacialControlMeta_Set"` を予約し、`stringParameter` で `"transitionDuration"` / `"transitionCurvePreset"` のキーを、`floatParameter` で値（秒数 / preset enum 整数）を運ぶ。
  - **却下**: naming convention（"Smile_dur0.5_easeIn.anim" のような命名）は人為的ミス・特殊文字制約・解析複雑性で却下。
  - **却下**: companion ScriptableObject は AnimationClip 単独移動で参照が切れるため却下。

### Topic 2: 中間 JSON schema v2.0 形式（Req 9.7）

- **Context**: Editor 保存時に AnimationClip → 中間 JSON へサンプリング。Runtime はこの中間 JSON のみを参照する。0-alloc / 同時 10 体制御という performance 契約（Req 11）と整合しなければならない。
- **Sources Consulted**:
  - [Unity ScriptReference: AnimationUtility.GetCurveBindings](https://docs.unity3d.com/ScriptReference/AnimationUtility.GetCurveBindings.html)
  - [Unity ScriptReference: AnimationUtility.GetEditorCurve](https://docs.unity3d.com/ScriptReference/AnimationUtility.GetEditorCurve.html)
  - [Unity ScriptReference: EditorCurveBinding](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorCurveBinding.html)
- **Findings**:
  - `AnimationUtility.GetCurveBindings(clip)` で `EditorCurveBinding[]` を取得できる。各 binding は `path` (string), `propertyName` (string), `type` (Type) を持つ。
  - `AnimationUtility.GetEditorCurve(clip, binding)` で AnimationCurve を取得できる。`curve.Evaluate(0f)` でキー時刻 0 の値を 0-alloc で評価可能（AnimationCurve.Evaluate は keys が 0 件のときデフォルト値を返す）。
  - BlendShape は propertyName 接頭 `"blendShape."` で識別できる（例: `blendShape.Smile`）。Transform は `"m_LocalPosition.x"` / `"m_LocalRotation.x"` / `"m_LocalScale.x"` で識別。
  - 案 A（snapshot table）: 時刻 0 の値だけ持つ。Runtime は配列直読みで 0-alloc。
  - 案 B（curve-preserving）: AnimationCurve.keys 全体を JSON にシリアライズ。Runtime で `AnimationCurve.Evaluate(t)` を毎フレーム呼ぶと boxing は発生しないが、AnimationCurve 自体が managed object のためヒープ上に存在し続ける。さらに AnimationClip 復元 API は Runtime asmdef では不可視。
- **Implications**:
  - **採用**: snapshot table 形式 (`schemaVersion: "2.0"`)。各 Expression は次のフラットな構造で表す。
    - `id`, `name`, `layer`
    - `layerOverrideMask: int` (`[Flags]` 値)
    - `transitionDuration: float`
    - `transitionCurvePreset: enum` (Linear / EaseIn / EaseOut / EaseInOut)
    - `blendShapes: [{ rendererPath, name, value }]`
    - `bones: [{ bonePath, position, rotationEuler, scale }]`
  - **却下**: curve-preserving。Runtime の JSON 経路は preview 段階を経て 1.0.0 で固定したいので、Editor だけが保持する豊富なメタはサンプリング段階で snapshot に圧縮する。
  - 将来 64-key curve や `LinearKeyValue` を必要としたら schema を `"2.1"` に bump して field 追加。

### Topic 3: AnimationCurve を InputProcessor のパラメータとして渡す方法（Req 6.1）

- **Context**: Req 6.1 は deadzone / scale / offset / curve / invert / clamp の 6 種の InputProcessor を提供せよと言っている。"curve" processor は AnimationCurve に類するカーブ入力を受けて値を変換するもの。
- **Sources Consulted**:
  - [Unity Discussions: Using AnimationCurve in an Input Processor](https://discussions.unity.com/t/using-animationcurve-in-an-input-processor/820926)
  - [Unity Discussions: Custom Input Processor cannot serialize String value](https://discussions.unity.com/t/custom-input-processor-cannot-serialize-string-value/907703)
  - [Unity InputSystem 1.12 InputProcessor API](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.12/api/UnityEngine.InputSystem.InputProcessor.html)
  - [Unity InputSystem Processors documentation (1.11)](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/Processors.html)
- **Findings**:
  - `InputProcessor<T>` は **stateless 必須**（Unity 公式設計方針）。インスタンスが共有・再利用されるため、`Process` 呼び出しごとに変わる状態は持てない。
  - AnimationCurve をパラメータにすると `ArgumentException: Don't know how to convert PrimitiveValue to 'Object'` が発生し、`PrimitiveValue` で扱えない型は serialize できない（公式 InputSystem のシリアライザは primitive のみ対応）。
  - 対応策（Unity Forum で確認された pattern）: float 系プリミティブで preset enum を持ち、`Process` 内で hard-coded カーブを評価する。
- **Implications**:
  - **採用**: `AnalogCurveProcessor` は `int preset` (Linear=0 / EaseIn=1 / EaseOut=2 / EaseInOut=3) のみを serialize し、Process 内で hard-coded 評価する。これは `TransitionCurvePreset` enum と共有する。
  - **却下**: AnimationCurve 直接保持は InputSystem の制約で実装不可。
  - **却下**: 文字列でキーフレーム列を受ける案は parser 実装コスト・stateless 違反リスクで却下。
  - Custom Hermite カーブが必要な場合は、ユーザー側で「複数 processor を直列に並べる」 or 「processor を自前拡張」する逃げ道を残す。preview 段階では 4 preset で十分。

### Topic 4: AnalogBindingEntry の Domain ↔ Adapters 境界（Req 13.3）

- **Context**: Req 6.2 は `AnalogBindingEntry` を `InputActionReference + targetIdentifier + targetAxis` のみに簡素化せよと指示。一方で Domain（Hidano.FacialControl.Domain）は `UnityEngine.InputSystem.InputActionReference` を参照できない（asmdef 契約 Req 13.4）。
- **Findings**:
  - Domain `AnalogBindingEntry` は元々 `SourceId` 文字列（`InputSourceId.Value`）で入力源を指していた。`InputActionReference` 概念は元々 Adapters 側にしか存在しない。
  - Adapters Serializable (`AnalogBindingEntrySerializable`) は Inspector 上の編集対象であり、`UnityEngine.InputSystem.InputActionReference` を直接 SerializeField に持つことが可能（既存パッケージで前例あり）。
  - InputAction Asset 側の `processors` 文字列に deadzone/scale/offset/curve/invert/clamp が直接埋め込まれるため、`AnalogBindingEntry` から `Mapping` フィールドを削除しても入力経路は破壊されない。Tick 時点で processor 通過後の値が来る。
- **Implications**:
  - **採用（2 層分離）**:
    - **Domain `AnalogBindingEntry`**: `SourceId, SourceAxis, TargetKind, TargetIdentifier, TargetAxis` の 5 フィールド（`Mapping` を撤去）。
    - **Adapters `AnalogBindingEntrySerializable`**: `InputActionReference inputActionRef, string targetIdentifier, AnalogTargetAxis targetAxis, AnalogBindingTargetKind targetKind` の 4 フィールド（Inspector 表示用、`Mapping` 撤去）。
    - 変換時に `inputActionRef.action.id.ToString()` を Domain 側 `SourceId` に詰める（または専用の `InputActionReference → InputSourceId` ヘルパー）。`SourceAxis` は targetAxis から派生せず、scalar=0 / Vector2 X=0/Y=1 を従来通り保持。
  - **撤去**: `Domain.Models.AnalogMappingFunction` / `Domain.Services.AnalogMappingEvaluator` / `Adapters.ScriptableObject.Serializable.AnalogMappingFunctionSerializable`。これらは Req 6.3 で全廃。
  - 将来 OSC など InputSystem 非経由 source が再び来た場合、processor チェーンに相当する変換は Adapters 側の OSC アダプタが内製する（Domain には戻さない）。

### Topic 5: AnimationClip.SampleAnimation のサンプリング戦略（referenceModel 連携）

- **Context**: AnimationClip 内の BlendShape / Transform 値を抽出するには 2 つの方法がある。(a) `AnimationUtility.GetCurveBindings + GetEditorCurve + AnimationCurve.Evaluate(0f)` でキーフレーム値を取り出す方法。(b) `clip.SampleAnimation(GameObject, time)` で実 GameObject に値を適用してから実モデルから読む方法。
- **Sources Consulted**:
  - [Unity ScriptReference: AnimationClip](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/AnimationClip.html)
  - [Unity ScriptReference: AnimationUtility.GetCurveBindings](https://docs.unity3d.com/ScriptReference/AnimationUtility.GetCurveBindings.html)
  - [UnityCsReference AnimationUtility.bindings.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Animation/AnimationUtility.bindings.cs/)
- **Findings**:
  - 案 A（GetCurveBindings 経路）は **GameObject 不要**で動作する。Editor only。サンプリング時刻 0 での float 値が直接得られる。Read-only サマリで RendererPath を表示する Req 4.3 にもこの経路の `binding.path` 列がそのまま流用できる。
  - 案 B（SampleAnimation 経路）は GameObject + Animator + SkinnedMeshRenderer が揃わないと機能しない。referenceModel が未割当のとき機能できない。
- **Implications**:
  - **採用**: 案 A（`GetCurveBindings + GetEditorCurve + Evaluate(0f)` 経路）を主として採用。GameObject 依存を排除。
  - **補助**: ExpressionCreatorWindow はライブプレビュー用に referenceModel 上で `SampleAnimation` を呼ぶが、これは Inspector 保存経路とは独立。
  - 0-alloc 影響なし（Editor 専用処理のため）。
- **Performance Benchmark（Phase 2.4）**:
  - 計測対象: `AnimationClipExpressionSampler.SampleSnapshot` / `SampleSummary`
  - クリップ構成: 10 BlendShape カーブ + 1 ボーン × 9 軸（Position/Euler/Scale 各 X/Y/Z）= 19 binding
  - 計測条件: ウォームアップ 3 回 + 計測 30 回、`System.Diagnostics.Stopwatch` 平均
  - 結果（Windows 11 / Unity 6000.3.2f1, EditMode）:
    - `SampleSnapshot`: avg ≈ 0.04ms（min 0.030ms / max 0.068ms）
    - `SampleSummary`: avg ≈ 0.011ms
  - 50ms 予算（Req 11.2 / 9.4）に対して 1000 倍以上の余裕。OQ5（0-alloc 制約緩和度合い）は緩和不要と判断。
  - テスト: `Tests/EditMode/Editor/Sampling/AnimationClipExpressionSamplerBenchmarkTests.cs`

### Topic 6: Inspector MaskField for LayerOverrideMask（Req 3.3）

- **Context**: LayerOverrideMask は Layer の `[Flags]` enum に縮退（Req 3.1）。Inspector では multi-select の checkbox UI を描画したい（Req 3.3）。
- **Findings**:
  - UI Toolkit `MaskField` はラベル文字列リスト + `int value` を取り、bit ごとに toggle する UI を出す。動的にラベルを差し替えられるため、`FacialCharacterProfileSO.Layers` の `name` を実行時に流し込めば「定義済みレイヤーのみが選択肢に出る」UI が成立する。
  - bit 数は `int` ベースで 32 まで。本仕様の対象は preview 段階の 3 レイヤー（emotion / lipsync / eye）+ ユーザー追加レイヤー数体程度なので 32 で十分。
- **Implications**:
  - **採用**: `[Flags] LayerOverrideMask : int` で 32 bit。Inspector では `MaskField` をデフォルトレイヤー定義から動的にバインド。
  - bit position は **Layers リストのインデックス順**で決まる（emotion=bit0, lipsync=bit1, eye=bit2 のような自然対応）。これが Layers の並び替え時に bit 配置がずれる脆弱性を生むため、JSON シリアライズ時には **layer 名のリストとして保存**し、bit 値で永続化しない（Req 9.7 の「version-tag each entry」と整合）。

### Topic 7: AssetModificationProcessor.OnWillSaveAssets で EditorUtility.DisplayProgressBar（Req 9.5）

- **Context**: 200ms を超える可能性のある AnimationClip サンプリングを SO 保存時に行うため、進捗表示が必要（Req 9.5）。一方で `AssetModificationProcessor.OnWillSaveAssets` は asset save の前段でブロッキング呼び出しされる特殊コンテキストである。
- **Findings**:
  - `EditorUtility.DisplayProgressBar(title, info, progress)` は同期 UI 更新で、**save callback 内からも安全に呼べる**（Unity Editor 公式実装で複数 sample が確認できる）。
  - 終了時は `EditorUtility.ClearProgressBar()` を必ず呼ぶ。例外で抜けた場合に残るリスクがあるため try/finally 必須。
  - `OnWillSaveAssets` は ReadOnly な paths を返すが、サンプリング失敗時は paths から該当 SO を除外することで save を abort できる（Unity 公式パターン）。
- **Implications**:
  - **採用**: `AutoExporter.OnWillSaveAssets` 内で 200ms 経過した場合のみ `DisplayProgressBar` を発火。try/finally で `ClearProgressBar` を保証。
  - サンプリング失敗時は `Debug.LogError` + paths から除外で save abort（Req 9.6 の「abort the save operation for that Expression entry」を「該当 SO 全体の save 取消」と解釈する。1 expression のみ skip ではなく SO 単位で取消）。

### Topic 8: 既存 BonePoses 移行（Req 10.5）

- **Context**: 既存ユーザーの BonePose（Domain `FacialProfile.BonePoses`）を AnimationClip transform curves へどう変換するか。
- **Findings**:
  - 既存 BonePose は `BonePoseEntry { boneName, eulerX, eulerY, eulerZ }` の集合。
  - 新方式では AnimationClip 内に `m_LocalRotation.x/y/z/w` または `localEulerAnglesRaw.x/y/z` のキーフレームとして表現される。
  - 既存値は離散値であり時刻 0 の単一スナップショットしかないため、自動移行（Editor menu command）は技術的に簡単（既存 BonePose を 1 件 1 件読み、新規 AnimationClip を 1 個生成して clip events も詰める）。但し AnimationClip ファイル名規則・配置 path・対応 Expression との結線は preview 段階のユーザーに任せた方が安全。
- **Implications**:
  - **採用**: **自動移行 menu command は preview 段階では提供しない**（Req 10.5 の「note their absence explicitly」を採用）。代わりに移行ガイドに「Animation Window で再録音する手順」「BonePose JSON 列を AnimationClip にどう詰めるか」を詳述する。
  - 移行ガイド `Documentation~/MIGRATION-v0.x-to-v1.0.md` を新規作成。

### Topic 9: LayerOverrideMask の Domain 表現（32-bit vs 拡張可能）

- **Context**: Domain で LayerOverrideMask を保持する具体型（int / long / ulong / 構造体）。
- **Findings**:
  - 現実的なレイヤー数は 3〜10。preview 段階の上限を 32 と決めても運用に支障はない。
  - 32 を超えるレイヤーが必要になる例（VTuber アバター）でも 64 で十分。`ulong` で 64 bit を確保するのが折衷案。
  - C# `[Flags] enum` は backing 型を `int / long / ulong` で選べる。`ulong` だと bit 操作で boxing を起こさない場合がある（Domain 側で span へのコピーで使う場合）。
- **Implications**:
  - **採用**: `[Flags] public enum LayerOverrideMask : int` の **int 32 bit**。32 bit で preview 〜 release 早期まで足りる。将来不足したら `LayerOverrideMaskV2 : ulong` に schema bump して移行する。
  - **却下**: ulong / 構造体は overengineering。
  - JSON 永続化は **bit 値ではなく layer 名の配列**として保存する（Req 3.5 ゼロフラグ validation と相性が良い）。bit ↔ name の往復は Adapters layer で完結する。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A. Extend Existing | Expression / LayerSlot に `animationClip` 等の field を追加し既存 field と並置 | 既存テスト書き換え少 | Req 1.5 が既存 field を全廃せよと言っているため要件違反 | **却下** |
| B. New Components | Expression / LayerSlot を別名 (`ExpressionV2`) で新設、旧型は `[Obsolete]` | 旧新が物理分離 | 旧型を参照する 30+ ファイルが二系統並走、preview 破壊宣言と整合せず | **却下** |
| C. Hybrid（推奨） | Domain Expression / LayerSlot は file 名維持で中身置換、ExpressionSnapshot / LayerOverrideMask 等は新規追加 | preview 破壊宣言と整合、6 phase で漸進可能、外部参照の breakage が局所化 | Phase 1〜3 の bridge 期間でテスト書き換え集中 | **採用** |

## Design Decisions

### Decision 1: AnimationClip メタデータ運搬は AnimationEvent 採用

- **Context**: Req 2.4–2.6（TransitionDuration / Curve を AnimationClip から派生）
- **Alternatives Considered**:
  1. AnimationEvent (`functionName="FacialControlMeta_Set"`) ← **採用**
  2. AnimationClip 名命名規則（"Smile_dur0.5_easeIn.anim"）
  3. Companion ScriptableObject で AnimationClip と pair link
- **Selected Approach**: 予約 functionName を持つ AnimationEvent を AnimationClip 内に埋める。`stringParameter` でキー（"transitionDuration" / "transitionCurvePreset"）、`floatParameter` で値、`time` 位置は 0 固定。
- **Rationale**: AnimationClip 単独で完結、Editor の Animation Events インスペクタで人間が編集可能、Runtime はそもそも AnimationClip を再生しないため未解決 functionName 警告が発生しない。
- **Trade-offs**:
  - +: AnimationClip 移動・コピーで欠落しない
  - +: Editor で可視化・編集できる
  - −: 「AnimationEvent をコールバックとして使う」一般慣習と意図がずれるため、ユーザーに混乱の余地（移行ガイドで明示する）
- **Follow-up**: 実装時に `AnimationUtility.SetAnimationEvents` 経由で書き戻す経路の安定性を確認。Animation Window で event を手動削除されたケースの fallback（Req 2.5）を確実に動作させる。

### Decision 2: 中間 JSON schema v2.0 = snapshot table

- **Context**: Req 9.1–9.3, 11.1–11.4
- **Alternatives Considered**:
  1. snapshot table（時刻 0 の値のみ） ← **採用**
  2. curve-preserving（AnimationCurve.keys を全保存）
- **Selected Approach**: Editor サンプラが `AnimationUtility.GetCurveBindings + AnimationCurve.Evaluate(0f)` で時刻 0 の値だけを抽出し、フラットな JSON 配列としてシリアライズする。
- **Rationale**: Runtime は配列直読みで 0-alloc、AnimationCurve 復元 API は Editor only で Runtime asmdef からは不可視、preview 段階で curve-preserving の複雑性を抱える価値がない。
- **Trade-offs**:
  - +: 0-alloc 契約と完全整合
  - +: schema が小さく可読性高い
  - −: 時刻 0 以外の補間情報を Runtime で再現できない（本仕様では不要）
- **Follow-up**: schema は `version: "2.0"` で明示。`schemaVersion: "1.0"` の旧 JSON は preview 段階のため明示的にロード拒否（Req 10.1）。

### Decision 3: TransitionCurve は preset 4 種に縮退

- **Context**: Req 2.6, 6.1
- **Alternatives Considered**:
  1. preset 4 種（Linear / EaseIn / EaseOut / EaseInOut） ← **採用**
  2. AnimationClip 内 curve から完全復元
- **Selected Approach**: AnimationEvent の `floatParameter` に preset enum 整数（0=Linear, 1=EaseIn, 2=EaseOut, 3=EaseInOut）のみを格納。Domain `TransitionCurve` も preset 4 種に縮退（既存の Linear / Ease / Custom から、Custom を撤去）。
- **Rationale**: InputProcessor で AnimationCurve を扱えない制約と整合、ExpressionCreatorWindow の編集 UI が単純化、TransitionCalculator の評価ロジックを 4 つの hard-coded 関数で済ませられる。
- **Trade-offs**:
  - +: 0-alloc 維持が容易、UI 単純
  - −: 既存 Custom curve 利用ユーザー（preview ユーザーのみ）は移行が必要 → 移行ガイドで preset 選択を案内
- **Follow-up**: 1.0 release 後の Feedback で Custom 必要性が高いと判明したら schema 2.1 で `customKeyframes` field を追加する。

### Decision 4: AnalogBindingEntry の Domain ↔ Adapters 二層分離

- **Context**: Req 6.2, 6.3, 13.1, 13.3, 13.4
- **Alternatives Considered**:
  1. Domain も `InputActionReference` を持たせる ← Req 13.4 違反で却下
  2. Domain は SourceId 文字列のみ、Adapters Serializable に InputActionReference ← **採用**
  3. Domain `AnalogBindingEntry` を撤去し Adapters のみで完結 ← OSC 経路でも同型を再利用したいので却下
- **Selected Approach**: Topic 4 で詳述。
- **Rationale**: Domain の Unity 非依存契約を維持しつつ、Adapters Serializable は Inspector で `InputActionReference` を直接編集できる。Variant 解析（gamepad/keyboard）は Adapters 側のみで完結。
- **Follow-up**: OSC アダプタが `AnalogBindingEntry` Domain 値を OSC アドレスに変換する経路を別 spec で確認。

### Decision 5: KeyboardExpressionInputSource / ControllerExpressionInputSource 撤去タイミング

- **Context**: Req 8.1, 8.2
- **Alternatives Considered**:
  1. `[Obsolete]` で 1 release 残す
  2. 即時撤去 ← **採用**
- **Selected Approach**: Phase 4 で `ExpressionInputSourceAdapter` 完成と同時に物理削除。Migration Guide で Component 差し替え手順を明示。
- **Rationale**: preview 段階のため schema/API 互換は破壊宣言済み（Req 10）。`[Obsolete]` 中継は維持コストのみで読者を混乱させる。

### Decision 6: Inspector validation の error 表示形式

- **Context**: Req 1.6, 1.7, 3.5
- **Alternatives Considered**:
  1. UI Toolkit `HelpBox` ← **採用**
  2. ラベル赤文字
  3. UI Toolkit `Notification`
- **Selected Approach**: 既存 Inspector で `HelpBox` を多用しているため踏襲。
- **Rationale**: 既存 UX との一貫性。`HelpBox` は icon + 文字列 + severity (Info / Warning / Error) を 1 行で出せる。
- **Follow-up**: validation エラー時は SO の `Save` ボタンを disabled にする（Req 1.6 の「prevent saving」を満たす）。

### Decision 7: `AnalogMappingEvaluator` (Domain) の処遇

- **Context**: Req 6 で InputProcessor 化 → Domain 側評価器が不要に
- **Alternatives Considered**:
  1. Domain から削除 ← **採用**
  2. OSC 互換のため Domain に残す
- **Selected Approach**: Domain `AnalogMappingFunction` / `AnalogMappingEvaluator` を完全撤去。OSC アダプタは（OSC が来たら）独自に Adapters 層内で対応する。
- **Rationale**: 二重実装の温床になるため、Domain には残さない。OSC は本仕様のスコープ外。
- **Trade-offs**:
  - −: 将来 OSC アダプタが処理を再実装する必要 → 別 spec で対処

### Decision 8: Expression / LayerSlot を struct のまま中身置換

- **Context**: Domain Expression が外部から多くの test / adapters で参照されており、新型に名前変更すると影響範囲が大きい
- **Alternatives Considered**:
  1. struct 中身置換、file 名維持 ← **採用**
  2. 別名（ExpressionV2）に rename
- **Selected Approach**: `Expression.cs` のファイル名は維持、内部 field を破壊的に書き換える。
- **Rationale**: preview 破壊宣言済みで、外部参照の compile error は意図的。新名にすると test ファイル名の rename 連鎖が発生して PR が肥大化する。
- **Follow-up**: phase 3 のリファクタで全 test を一斉に書き換える。CI を一時的に red 状態で進める。

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| AnimationEvent メタデータ運搬がユーザーに難解 | 中 | Migration Guide + ExpressionCreatorWindow の編集 UI で「TransitionDuration スライダー」「Curve preset ドロップダウン」を提供し、ユーザーが直接 AnimationEvent を編集する必要を排除 |
| InputProcessor stateless 制約による表現力不足 | 中 | preset 4 種で対応、Custom が必要なら user は複数 processor を直列化する（Unity 標準パターン） |
| 30+ test ファイル一斉書き換えで CI が long red | 中 | Phase 3 / 5 で集中的に書き換え、phase ごとに CI green に戻す。Phase 4 は別 PR で並行可 |
| Sample アセット二重管理（Samples~ + Assets/Samples）の同期忘れ | 中 | Phase 6 の Definition of Done に「両 location が同名 hash で一致」を含める |
| Layer 並び替えで bit 配置がずれる | 低 | LayerOverrideMask の JSON シリアライズは layer 名配列を採用、bit 値ではなく |
| AnimationClip サンプリング 200ms 超で Editor フリーズ | 低 | DisplayProgressBar 表示。50+ Expression ある SO でのみ問題化（preview ユーザーは 10 程度） |
| BonePose 自動移行 menu 不在 | 低 | Migration Guide に手動手順を詳述。preview ユーザーは数名のため許容 |
| `OnWillSaveAssets` 内例外で ProgressBar 残存 | 低 | try/finally で ClearProgressBar 保証 |

## References

- [Unity InputSystem Processors documentation (1.11)](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/Processors.html) — InputProcessor stateless 必須要件と組み込み processor 一覧
- [Unity InputSystem Processors documentation (1.12)](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.12/api/UnityEngine.InputSystem.InputProcessor.html) — InputProcessor API リファレンス（最新）
- [Unity Discussions: Using AnimationCurve in an Input Processor](https://discussions.unity.com/t/using-animationcurve-in-an-input-processor/820926) — AnimationCurve serialization 制約の primary source
- [Unity Discussions: Custom Input Processor cannot serialize String value](https://discussions.unity.com/t/custom-input-processor-cannot-serialize-string-value/907703) — primitive 限定の serialization 制約
- [Unity Discussions: Custom processor fails to register in builds](https://discussions.unity.com/t/custom-processor-fails-to-register-in-builds/851657) — RuntimeInitializeOnLoadMethod 必須パターン
- [Unity ScriptReference: AnimationUtility.GetCurveBindings](https://docs.unity3d.com/ScriptReference/AnimationUtility.GetCurveBindings.html) — AnimationClip 内 curve binding 列挙
- [Unity ScriptReference: AnimationUtility.GetEditorCurve](https://docs.unity3d.com/ScriptReference/AnimationUtility.GetEditorCurve.html) — 個別 binding の AnimationCurve 取得
- [Unity ScriptReference: EditorCurveBinding](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorCurveBinding.html) — path / propertyName / type 三組構造
- [Unity ScriptReference: AnimationClip](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/AnimationClip.html) — Unity 6 系 AnimationClip API
- [Unity ScriptReference: AnimationEvent](https://docs.unity3d.com/ScriptReference/AnimationEvent.html) — clip events 構造
- [Unity ScriptReference: AnimationEvent.floatParameter](https://docs.unity3d.com/ScriptReference/AnimationEvent-floatParameter.html) — float メタデータ運搬手段
- [Unity Manual: Use Animation Events](https://docs.unity3d.com/Manual/script-AnimationWindowEvent.html) — AnimationEvent 編集 UI
- [UnityCsReference AnimationUtility.bindings.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Animation/AnimationUtility.bindings.cs/) — Editor 専用実装の正典
