# Research & Design Decisions: bone-control

## Summary
- **Feature**: bone-control
- **Discovery Scope**: Extension（既存 FacialControl preview.1 に並走パイプラインを追加。greenfield within brownfield）
- **Key Findings**:
  1. `FacialController.LateUpdate` 末尾からの内部呼出で BoneWriter 順序は確定的に解決可能（独立 MonoBehaviour + `[DefaultExecutionOrder]` は不要）
  2. 現状の `PlayableGraphBuilder` の `AnimationPlayableOutput` は `FacialControlMixer` を source とし BlendShape ストリームのみ書込むため、bone Transform への副作用は発生しない（実装読込で確認済）
  3. preview.1 では `Expression.bonePoseRef` を導入せず、analog-input-binding は `IBonePoseProvider` 経由で直接駆動する（gap-analysis 推奨と整合）
  4. Domain 層は `Unity.Collections` のみ参照可で `UnityEngine.Quaternion` 不可。Unity 互換 Z-X-Y Tait-Bryan 順を float 4 タプルで自前実装する
  5. preview.1 はメインスレッド限定で十分。`LayerInputSourceWeightBuffer` のダブルバッファは現時点では BoneWriter には適用しない（複雑度コスト > 利得）

## Research Log

### Animator update order と LateUpdate のタイミング
- **Context**: gap-analysis §3 の Critical risk。Animator は `Update` 直後にボーン Transform を上書きするため、bone writer は `LateUpdate` でかつ **Animator の出力を踏んだ後**に走る必要がある
- **Sources Consulted**:
  - Unity Manual の `Animator` Update Mode / Execution Order 標準動作（Animator は `Update`、Update Mode が `AnimatePhysics` の場合 `FixedUpdate` フェーズ）
  - 既存 `FacialController.LateUpdate` の実装パターン（Aggregator → SkinnedMeshRenderer.SetBlendShapeWeight）
- **Findings**:
  - `LateUpdate` は同フレームの `Update` および `Animator` 出力**後**に実行される（Unity 標準ライフサイクル）
  - 同 GameObject 上の他 LateUpdate 呼出順は `[DefaultExecutionOrder]` で制御可能だが、それはユーザー依存・脆い
  - 同一クラス内の関数末尾呼出は execution order に依存せず確定的
- **Implications**:
  - BoneWriter は `FacialController.LateUpdate` 末尾**から内部呼出**する（gap-analysis Option C を採用、design §System Flows）
  - 独立 MonoBehaviour + `[DefaultExecutionOrder]` は採用しない
  - `OnAnimatorIK` / `IAnimationJob` も preview.1 では採用せず、preview.2 以降で Burst 化要件が出た際に再評価

### PlayableGraph 出力の bone 副作用
- **Context**: gap-analysis §8 Item 2。現在 `AnimationPlayableOutput` を Animator に接続しているが、これが bone Transform を書く可能性を検証する
- **Sources Consulted**:
  - `Runtime/Adapters/Playable/PlayableGraphBuilder.cs:14-62` の実装読取
  - `FacialControlMixer` の責務（BlendShape weight ストリーム書込のみ）
  - `PropertyStreamHandleCache` の用途（`SetBlendShapeWeight` 系のプロパティバインディング）
- **Findings**:
  - `AnimationPlayableOutput.SetSourcePlayable(mixer)` は `FacialControlMixer` を source としており、Mixer は BlendShape weight のみを `AnimationStream` 経由で書込む
  - bone 用 `TransformStreamHandle` 系の API は本パッケージ内で一切使われていない（Grep 結果ゼロ）
- **Implications**:
  - PlayableGraph 経路と BoneWriter 経路は独立。BoneWriter が直接 Transform.localRotation を書いても PlayableGraph と競合しない
  - preview.2 以降で PlayableGraph を撤去する場合（FacialController.LateUpdate コメントに撤去検討と記述あり）でも BoneWriter には影響なし
  - 設計判断: 「PlayableGraph と BoneWriter は同一フレーム内で独立、共有変数なし」を Req 10.3 の構造的保証として確定

### BonePose と Expression の参照関係（Identity 設計）
- **Context**: Req 1.4「Expression は BonePose を参照可、埋込不可」。preview.1 でこの参照をどう実装するかが設計判断
- **Sources Consulted**:
  - gap-analysis §8 Item 3 推奨：`expressions[].bonePoseRef: "id"` か独立駆動か
  - 後続 spec analog-input-binding が「Expression と無関係に BonePose を直接駆動する」要件
- **Findings**:
  - analog-input-binding はスティック入力 → BonePose の **直接駆動**（`IBonePoseProvider.SetActiveBonePose`）が想定される
  - Expression に `bonePoseRef` を導入すると、表情遷移と bone 駆動の同期セマンティクスが必要になり preview.1 のスコープを膨張させる
- **Implications**:
  - **Decided in this spec**: preview.1 では `Expression.bonePoseRef` は **導入しない**。Expression と BonePose は完全分離
  - `FacialProfile.BonePoses` は単独配列として保持し、analog-input-binding が `IBonePoseProvider` 経由で駆動する
  - `BonePose.Id` フィールドは preview.1 でも持つが（JSON `id` round-trip のため）、実行時の参照キーには使わない
  - preview.2 以降で「表情と連動する bone pose 切替」要件が来たら別 spec で再設計

### Domain 内 Quaternion 数学（Unity 互換性）
- **Context**: Req 4.4 が `basisBone.localRotation * RotationFromEuler(eulerXYZ)` の合成を要求。Domain 層は `UnityEngine.Quaternion` を参照できないため自前実装が必要
- **Sources Consulted**:
  - Unity の `Quaternion.Euler(x, y, z)` の回転順序: **Z 軸 → X 軸 → Y 軸**（Tait-Bryan、ローカル軸）
  - 一般 Tait-Bryan の half-angle quaternion 合成公式
- **Findings**:
  - Unity の `Quaternion.Euler(x, y, z) = Qy(y) * Qx(x) * Qz(z)`（intrinsic, Z-first）
  - Domain で同じ順序を再実装することで PlayMode テストでの突合が可能
- **Implications**:
  - **Decided in this spec**: Domain `BonePoseComposer.EulerToQuaternion` は **Unity の Z-X-Y 順と同等** の合成を行う
  - PlayMode テスト `BonePoseComposerTests.EulerToQuaternion_MatchesUnityQuaternionEuler` で Unity の `Quaternion.Euler(x, y, z)` との誤差 ≤ `1e-5` を確認
  - `Compose(basisQ, eulerXYZ)` は内部で `EulerToQuaternion` → quaternion 乗算（standard Hamilton product）の順
  - half-angle 化の数値安定性は Unity と同じ（pure float 演算、桁落ちは degree 単位で問題にならない）

### BonePose の SO 化（独立 SO vs FacialProfileSO 内包）
- **Context**: Req 8 が ScriptableObject 化を要求。独立 `BonePoseSO` か `FacialProfileSO` 内包かの設計判断
- **Sources Consulted**:
  - gap-analysis §9 推奨：preview.1 は内包、独立 SO は preview.2 以降
  - 既存パターン：`FacialProfileSO` は JSON ファイルパス + キャッシュフィールドのビュー
- **Findings**:
  - 独立 SO 化のメリット：複数 FacialProfile 間で BonePose を共有可能
  - 内包のメリット：JSON round-trip / Editor 編集 UI / Asset 管理が `FacialProfileSO` 内に閉じる
  - preview.1 のユースケース（VTuber キャプチャ、analog-input-binding）では BonePose 共有要件がない
- **Implications**:
  - **Decided in this spec**: preview.1 では `FacialProfileSO` 内包に固定（gap-analysis §9 と整合）
  - 独立 `BonePoseSO` は preview.2 以降で「複数プロファイル間共有」要件が出た際に別 spec で対応
  - 内包時のフィールド: `[SerializeField] private BonePoseSerializable[] _bonePoses;`（design §FacialProfileSO 参照）

### Runtime API の thread-safety
- **Context**: Req 11.5 が「hot path で alloc しない」を要求。analog-input-binding が任意スレッドから書込む可能性をどう扱うか
- **Sources Consulted**:
  - 既存 `LayerInputSourceWeightBuffer` のダブルバッファ + `SwapIfDirty` パターン（OSC が任意スレッドから書込むケース）
  - InputSystem のコールバックスレッド契約（メインスレッドが基本）
- **Findings**:
  - InputSystem の callback はメインスレッド配信（Unity 標準）
  - analog-input-binding の想定駆動経路は InputSystem → IBonePoseProvider のメインスレッド
  - OSC のような任意スレッド書込は preview.1 段階で BonePose 駆動には使わない
- **Implications**:
  - **Decided in this spec**: preview.1 BoneWriter は **メインスレッド限定**。`SetActiveBonePose` の呼出スレッド契約をメインに固定
  - `LayerInputSourceWeightBuffer` 風のダブルバッファは BonePose には適用しない（複雑度コスト > 利得）
  - 内部実装は `_pendingPose` / `_activePose` の簡易 swap で「書込は次フレーム反映」（Req 11.2）を担保
  - preview.2 以降で OSC 経由 / Job 経由の書込要件が出たらダブルバッファ化を再評価

### 非 Humanoid モデルの bone 名手入力 UX
- **Context**: Req 2 / 3 が VRM / FBX の非 Humanoid を排除しないことを要求。string 名手入力を要求するなら UI 補助が必要
- **Sources Consulted**:
  - 既存 `Editor/Common/BlendShapeNameProvider.cs`：参照モデル → SkinnedMeshRenderer 配下の BlendShape 名候補を列挙
  - UI Toolkit の `DropdownField` / `TextField` の typeahead 機能
- **Findings**:
  - BlendShape 名は `SkinnedMeshRenderer.sharedMesh.GetBlendShapeName(j)` で取得可
  - bone 名は `Animator.transform.GetComponentsInChildren<Transform>()` から `Transform.name` で取得可
  - 同名 Transform が複数存在するケース（一般的にはレアだが、左右対称命名で発生し得る）は preview.1 では「最初の発見を採用」で良い
- **Implications**:
  - **Decided in this spec**: Editor 側に **`BoneNameProvider`（新規ヘルパ）** を追加し、参照モデル配下の全 Transform.name を列挙する
  - `FacialProfileSO_BonePoseView` の bone 名入力欄は UI Toolkit の `DropdownField` ベースの typeahead で候補表示する
  - 候補がない（参照モデル未設定）場合はフリーテキスト `TextField` にフォールバック
  - これにより Req 2.1 の「string 名一次」と Req 9 の Editor 編集 UI が両立する

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: FacialController に直結 | `LateUpdate` 末尾に bone 適用ロジック直接追加、`FacialProfileSO` に bone 配列フィールド | 単一 MonoBehaviour で順序自明 / 既存テスト追加最小 | FacialController の SRP 劣化 / `IFacialControllerExtension` と非対称 | gap-analysis §5 |
| B: BoneWriter 独立 MonoBehaviour | sibling MonoBehaviour、`[DefaultExecutionOrder]` で順序固定、独立 `BonePoseSO` | クリーンアーキテクチャ整合 / サブパッケージ化容易 | execution order 依存で脆い / コンポーネント二重設置必要 | gap-analysis §5 |
| C: Hybrid（採用） | Domain & Adapters/Bone は独立、ランタイム起動は FacialController 内包、`IBonePoseProvider` 拡張点 | 分離と順序確定の両立 / 既存 SO 互換 / Animator 競合回避 | FacialController の内部複雑度増 / 独立 SO は preview.2 へ送り | gap-analysis §9 推奨 |

## Design Decisions

### Decision: BoneWriter は FacialController.LateUpdate 末尾から内部呼出する
- **Context**: Animator update order と LateUpdate ordering の確定方法（research §1）
- **Alternatives Considered**:
  1. 独立 MonoBehaviour + `[DefaultExecutionOrder]`
  2. `Animator.OnAnimatorIK` 利用
  3. `IAnimationJob` で Burst 化対応
- **Selected Approach**: `FacialController.LateUpdate` の末尾で `_boneWriter.Apply(basisBoneName)` を呼ぶ
- **Rationale**: 同一クラス内呼出のため execution order に依存せず確定的。Animator → BlendShape → Bone の順序が internal シーケンスのみで保証される
- **Trade-offs**: FacialController の責務が拡大するが、内部 BoneWriter インスタンスへの delegation のため見かけの複雑度は限定的
- **Follow-up**: 同 GameObject 上の他 LateUpdate コンポーネント（LookAt constraint, MagicaCloth2 揺れもの等）との競合はユーザー責任、ドキュメントに明記

### Decision: Domain 内 Quaternion 数学は Unity 互換 Z-X-Y 順を自前実装
- **Context**: Domain 層 `UnityEngine` 非参照契約と Req 4.4 の合成順序（research §4）
- **Alternatives Considered**:
  1. Domain 独自順序（X-Y-Z 等）
  2. Adapters 層に押し出して `UnityEngine.Quaternion.Euler` を直呼出
- **Selected Approach**: Domain 内に `BonePoseComposer.EulerToQuaternion(x, y, z)` を Unity の Z-X-Y Tait-Bryan 順で実装
- **Rationale**: 仕様の運用上 Unity の Inspector / `Quaternion.Euler` と同じ意味の度数値を扱うのが直感的。authoring 互換を維持
- **Trade-offs**: Adapters 層で Unity との突合 PlayMode テストが追加で必要
- **Follow-up**: PlayMode テスト `BonePoseComposerTests.EulerToQuaternion_MatchesUnityQuaternionEuler` で Unity の `Quaternion.Euler` との一致誤差を `1e-5` で担保

### Decision: BonePose は FacialProfileSO 内包（独立 BonePoseSO は preview.2 以降）
- **Context**: Req 8 SO 化の実装形態選択（research §5）
- **Alternatives Considered**:
  1. 独立 `BonePoseSO`（複数プロファイル間共有可）
  2. `FacialProfileSO` 内包（serialized field 追加）
- **Selected Approach**: `FacialProfileSO` に `[SerializeField] private BonePoseSerializable[] _bonePoses` を追加
- **Rationale**: preview.1 のユースケースでは共有要件がない / 既存ユーザの SO アセット互換 / JSON round-trip / Editor UI が同一 SO 内に閉じる
- **Trade-offs**: 将来「複数プロファイル間 BonePose 共有」要件が出たら独立 SO 化のリファクタが必要
- **Follow-up**: preview.2 以降で要件が顕在化したら別 spec を切る

### Decision: preview.1 の BoneWriter はメインスレッド限定
- **Context**: thread-safety と alloc-zero の両立（research §6）
- **Alternatives Considered**:
  1. `LayerInputSourceWeightBuffer` 風ダブルバッファで任意スレッド書込対応
  2. メインスレッド限定 + 簡易 swap（`_pendingPose` / `_activePose`）
- **Selected Approach**: メインスレッド限定。`SetActiveBonePose` で `_pendingPose` に格納、`Apply` 開始時に `_activePose` へ swap
- **Rationale**: analog-input-binding の駆動経路は InputSystem コールバック（メインスレッド）。OSC 経由のような任意スレッド書込は preview.1 段階の BonePose 駆動要件にない
- **Trade-offs**: 任意スレッド書込が必要になったらダブルバッファ化のリファクタが必要
- **Follow-up**: preview.2 以降で OSC や Job 経由の BonePose 駆動が必要になった時に再評価

### Decision: preview.1 では Expression.bonePoseRef を導入しない
- **Context**: Req 1.4 「Expression は参照可、埋込不可」の実装形態（research §3）
- **Alternatives Considered**:
  1. JSON: `expressions[].bonePoseRef: "id"` 導入
  2. Expression と BonePose は完全分離、analog-input-binding が直接 BonePose を駆動
- **Selected Approach**: 完全分離。`Expression` は変更しない。`FacialProfile.BonePoses` 単独配列のみ
- **Rationale**: analog-input-binding の駆動経路は IBonePoseProvider で完結する / 表情遷移と bone 切替の同期セマンティクスを preview.1 でデザインしない
- **Trade-offs**: 表情に紐づく bone pose（例: 笑顔のときだけ目線下げる）は preview.1 では実現できない
- **Follow-up**: preview.2 以降で「表情連動 bone」要件が出たら別 spec で `Expression.bonePoseRef` を導入

### Decision: Asmdef は既存 Hidano.FacialControl.Adapters に統合
- **Context**: Adapters/Bone 配下を独立 asmdef にすべきかの判断
- **Alternatives Considered**:
  1. 新規 `Hidano.FacialControl.Adapters.Bone.asmdef`
  2. 既存 `Hidano.FacialControl.Adapters.asmdef` 配下に Bone/ ディレクトリ
- **Selected Approach**: 既存 Adapters asmdef に統合
- **Rationale**: 既存の `Adapters/{Json, Playable, OSC, ScriptableObject, InputSources, FileSystem}` も全て同一 asmdef 配下。一貫性を保つ
- **Trade-offs**: Bone 機能だけを独立 dep boundary にすることはできない（preview.1 では不要）
- **Follow-up**: preview.2 以降で Burst 化 / 別パッケージ化が必要になった時に asmdef 分離を再評価

### Decision: BonePose JSON ブロックは optional（inputSources の D-5 必須化を踏襲しない）
- **Context**: Req 7.3 / 10.2 の後方互換要件 vs `inputSources` の D-5（preview 破壊的変更で必須化）
- **Alternatives Considered**:
  1. `bonePoses` 必須化（D-5 と同じく preview 中破壊的変更を許容）
  2. `bonePoses` optional（欠落・null・空配列を全て「BonePose なし」として扱う）
- **Selected Approach**: optional
- **Rationale**: gap-analysis §1 で明記された Req 1.5 / 7.3 の保証 / 既存ユーザはまだいないが、追加スキーマフィールドを必須化しないことで内部テストの修正範囲を最小化 / `inputSources` のように同じ JSON で複数機能を語る場合と異なり、bone は independent block
- **Trade-offs**: 「BonePose なしのプロファイル」と「BonePoses が空配列のプロファイル」が JSON 上で区別できないが、ランタイム挙動は同一
- **Follow-up**: 必須化が必要な状況は preview.1 では発生しない見込み

## Risks & Mitigations
- **Risk 1**: 同 GameObject 上の他 LateUpdate コンポーネント（LookAt constraint, MagicaCloth2 等）が同じ bone Transform を書く → bone 値がフレーム内で上書き合戦になる
  **Mitigation**: ドキュメントに「BoneWriter と同じ bone を書く他コンポーネントがある場合の execution order はユーザー責任」を明記。preview.2 以降で IK 統合（OnAnimatorIK / IAnimationJob）の選択肢を再評価
- **Risk 2**: Domain Quaternion 数学が Unity と微妙に異なる順序で実装され、同じ Euler 値が異なる回転を生む
  **Mitigation**: PlayMode テストで Unity の `Quaternion.Euler(x, y, z)` との誤差を `1e-5` で全角度パターン（X/Y/Z 各 -180°〜180° の grid）スキャン
- **Risk 3**: メインスレッド限定契約が analog-input-binding の実装段階で破綻する（OSC 統合等）
  **Mitigation**: design / research に契約を明記し、analog-input-binding spec の design phase で再確認。破綻時は preview.2 で BoneWriter をダブルバッファ化
- **Risk 4**: 大量 BonePoseEntry（>100）でホットパスが O(n) ループによりフレーム budget を超える
  **Mitigation**: preview.1 の典型ケース（5 bone × 10 体）では問題にならない試算。性能テスト `BoneWriterPerformanceTests.Apply_WithTenCharactersAndFiveBones_FitsBudget` で確認

## References
- [Unity Manual - Order of Execution](https://docs.unity3d.com/Manual/ExecutionOrder.html) — LateUpdate と Animator の順序
- [Unity Scripting API - Quaternion.Euler](https://docs.unity3d.com/ScriptReference/Quaternion.Euler.html) — Z-X-Y Tait-Bryan 順仕様
- [Unity Scripting API - Animator.GetBoneTransform](https://docs.unity3d.com/ScriptReference/Animator.GetBoneTransform.html) — HumanBodyBones 解決
- gap-analysis.md — 本 spec の根拠分析（特に §3 Critical Risk、§5 Option C、§8 Research Needed）
- `Runtime/Adapters/Playable/PlayableGraphBuilder.cs:14-62` — AnimationPlayableOutput の bone 副作用なしの実装根拠
- `Runtime/Domain/Services/LayerInputSourceWeightBuffer.cs` — 既存ダブルバッファ + SwapIfDirty パターン（preview.1 では BonePose に適用しない）
- `Editor/Common/BlendShapeNameProvider.cs` — bone 名候補プロバイダ（`BoneNameProvider`）の実装パターン参照元
