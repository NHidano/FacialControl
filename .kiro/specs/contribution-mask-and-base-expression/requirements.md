# Requirements Document

## Project Description (Input)
BlendShape 単位の contribution mask による layer 合成と、 SO root に持つベース表情 (Base Expression) を導入する。これにより emotion (controller 由来 Digital expression) と LipSync の合成が「contribute する index は上書き、 contribute しない index は下位 layer / ベース表情の値が残る」 で自然に動作するようにする。

### 背景

現状の `LayerBlender.Blend` は per-blendshape index で `output[i] = lerp(output[i], values[i], weight)` を実行するため、 高優先度 layer (例: lipsync) の weight=1.0 で blend すると、 当該 layer が contribute していない index (例: 目) も lerp の 0 補間で消えてしまう。 これにより emotion + lipsync を素直に合成できない。

`layerOverrideMask` は Inspector で設定可能だが Phase 3.4 で導入予定の `ExpressionResolver` 経由で解釈される設計で、 現状は無視されている。

ユーザー (FacialControl ライブラリ開発者本人) の意向: 加算 + cap 1.0 ではなく「上書き合成 + 自分が値を持つ index しか上書きしない」 方針を採用。 さらに常に怒った顔のキャラや衣装で特定 BlendShape をロックしたいユースケースに対応するため、 ベース表情の概念を SO root に導入する。

### 実装方針 (案 D + ベース表情)

#### R1. BlendShape contribution mask
- `IInputSource` に「自分が contribute する BlendShape index 集合」を取得する API を追加 (BitArray か ReadOnlySpan<int>)
- `ExpressionTriggerInputSource` / `LipSyncInputSource` (`ULipSyncProvider`) / `AnalogBlendShapeInputSource` 各実装で contribute index を提供
- `LayerInputSourceAggregator` で source 群の mask を OR 集約して layer 全体の contribute mask を構築
- `LayerPlayable` / `LayerBehaviour` の出力に mask を併走させる
- `LayerBlender.Blend` を「mask 立っている index のみ lerp 上書き、 立ってない index は下位 layer の値を保持」に変更

#### R2. ベース表情 (Base Expression)
- `FacialCharacterProfileSO` (root) に `_baseExpression` を追加。 1 個の AnimationClip 参照 + bake 後の per-blendshape weight 配列
- `FacialControlMixer.ComputeOutput` で layer 合成前に出力バッファをベース表情の値で初期化 (現状は Array.Clear で 0 初期化)
- ベース表情 AnimationClip が null の場合は全 BlendShape 0 で初期化 (= 現状互換)
- ベース表情用の AnimationClip サンプリングは既存 `AnimationClipExpressionSampler` を流用

#### R3. AnimationClip からの contribute mask 抽出と bake (Editor)
- `AnimationClipExpressionSampler` に「curve binding に含まれる BlendShape 名集合」を返す API を追加
- Editor 自動保存時に Expression / Base Expression の AnimationClip から bake
- bake 結果は `ExpressionSnapshot` / `BaseExpressionSnapshot` 等のメタデータに保存

#### R4. Inspector UI
- `FacialCharacterProfileSOInspector` の最上位に「ベース表情」セクションを追加
- AnimationClip ObjectField + bake された BlendShape weight プレビュー (任意)
- ベース表情未設定時の説明 HelpBox

#### R5. JSON schema bump
- profile.json に `baseExpression` フィールドを追加
- 既存 profile が `baseExpression` 欠如時は全 0 base として読み込む (forward compat)
- migration ツールは preview 段階につき不要 (ユーザー報告: 既存ユーザーゼロ)

#### R6. テスト
- `LayerBlenderTests`: contribute mask 解釈の挙動 (mask 全立て = 旧 lerp 互換、 mask 部分立て = base / 下位 layer 保持)
- `LayerInputSourceAggregatorTests`: source mask の OR 集約
- `FacialControlMixerTests`: ベース表情からの初期化
- `AnimationClipExpressionSamplerTests`: curve binding からの contribute index 抽出
- emotion + lipsync 合成シナリオの統合テスト

### 非スコープ (別 spec / backlog)

- アナログ入力 Weight 値による Expression Lerp 駆動 (S-7)
- AdapterBinding Slug ドロップダウン UI (S-8)
- Phase 3.4 `ExpressionResolver` + `layerOverrideMask` 解釈 (引き続き別 spec で並行設計)
- 衣装毎に複数 BaseExpressionVariant を持つ拡張 (preview.2 以降)
- 「強制ピン (上位 layer から上書きされない BlendShape)」 概念 (将来検討)

### 制約 / 設計原則

- preview 段階のため破壊的変更を許容
- 既存 lerp 挙動との互換性は「mask 全立て + base 全 0」 で担保
- ヒープ確保ゼロ目標 (per-frame) を維持。 mask 用の bool[] / BitArray は preallocated
- Domain 層は UnityEngine 非依存を維持
- `IInputSource` に追加するメンバーは breaking change だが preview 許容

### 期待されるユーザー体験

1. ユーザーが Inspector で Layer A (emotion, priority=0) と Layer B (lipsync, priority=1) を作る
2. Layer A の Expression は AnimationClip で目・眉・口を駆動、 Layer B の LipSync は phoneme entry で口関連 BlendShape のみ駆動
3. 実行: emotion 表情を発火しつつ lipsync を再生
4. 結果: 目・眉は emotion の値、 口は lipsync で上書き、 contribute されない index は base (デフォルト全 0) のまま
5. 怒り顔キャラを作る場合: SO の baseExpression に「怒り眉・怒り目」 を含む AnimationClip をアサインすれば、 Expression 発火していない index は怒り顔のまま残る

## Introduction

本仕様は FacialControl ライブラリの表情レイヤー合成パイプラインを拡張し、(1) `IInputSource` ごとに「自分が値を持つ BlendShape index」を contribution mask として宣言可能にし、(2) `FacialCharacterProfileSO` の root にベース表情 (Base Expression) を導入する。これにより、現在 `LayerBlender.Blend` の per-index lerp で発生している「高優先度 layer が contribute しない index まで 0 補間で消す」問題を解消し、emotion + LipSync の自然な合成と、衣装・キャラ固有の固定表情ベースを実現する。

## Boundary Context

- **In scope**:
  - `IInputSource` への contribution mask API 追加と既存 5 実装 (`ExpressionTriggerInputSource` / `ULipSyncProvider` / `AnalogBlendShapeInputSource` / `OscFloatAnalogSource` / `ArKitOscAnalogSource`) の対応 (see D-9)
  - `LayerInputSourceAggregator` における mask の OR 集約
  - `LayerPlayable` / `LayerBehaviour` 経由の mask 伝搬 (`LayerBlender.LayerInput` 構造体に `BitArray ContributeMask` 追加。 see D-5)
  - `LayerBlender.Blend` の mask 解釈ロジック変更
  - `FacialCharacterProfileSO` への `_baseExpression` 追加と内部 snapshot 透過生成 (see D-2, D-4)
  - `FacialControlMixer.ComputeOutput` のベース表情初期化
  - `AnimationClipExpressionSampler` への BlendShape 名集合取得 API 追加
  - `FacialCharacterProfileSOInspector` への「ベース表情」UI 追加 (GazeConfigs の隣に配置。 see D-8)
  - profile.json schema への `baseExpression` フィールド追加 (forward compat、 通常 Expression と同形式。 see D-3)
  - 上記すべてに対する EditMode/PlayMode テスト
- **Out of scope**:
  - アナログ入力 Weight による Expression Lerp 駆動 (S-7)
  - AdapterBinding Slug ドロップダウン UI (S-8)
  - Phase 3.4 `ExpressionResolver` + `layerOverrideMask` 解釈
  - 衣装毎の複数 `BaseExpressionVariant` (preview.2 以降)
  - 強制ピン (上位 layer から上書きされない BlendShape) 概念
  - 既存 profile.json の migration ツール (既存ユーザーゼロのため不要)
- **Adjacent expectations**:
  - Phase 3.4 `ExpressionResolver` 仕様は本 spec の contribution mask 設計と整合する形で別 spec として並行設計される
  - 既存 `LayerBlender` 旧挙動 (= 全 index lerp) は「mask 全立て + base 全 0」のときに完全に再現されること

## Open Questions and Decisions (Dig)

dig インタビューで確定した設計判断を記録する。各決定は対応する Acceptance Criteria から `(see D-N)` で参照する。

| ID | Topic | Decision | Rationale | Risk |
|----|-------|----------|-----------|------|
| D-1 | Mask データ構造 | `System.Collections.BitArray` を採用 (preallocated インスタンスを source / aggregator / layer 各層で保持) | .NET 標準で Domain 層 UnityEngine 非依存制約を満たす。dense bitmap で BlendShape 数 100 規模の OR 集約・bit 検査が安定動作。ulong[] ビットパックよりも実装コストが低い | 低 (実装シンプル、 alloc は preallocated で 0) |
| D-2 | Bake トリガー / ユーザー体験 | bake という工程をユーザーに意識させない。Gaze 同様、SO root に「ベース表情」専用オプション設定枠を設け、AnimationClip がアサインされたら**内部で透過的に** per-blendshape weight 配列と contribute mask を生成。アサインなしのときは全 0 + 空 mask を内部で生成して合成パイプラインに供給する | UX 上「bake してください」のような明示操作はノイズ。Inspector 自動保存パイプライン (`OnSerializedObjectChanged` → `delayCall FlushAutoSave`) に乗せれば透過的に処理可能 | 中 (Inspector 自動保存と内部 snapshot 生成の経路が密結合になる。失敗時のフォールバック明示が必要) |
| D-3 | profile.json `baseExpression` 表現 | 通常の Expression と同じ JSON 形式 (= bake 後の `{blendShapeName, weight}` 配列を `blendShapeValues` フィールドに保存。AnimationClip 参照は SO 内のみで JSON には載せない) | JSON ファースト方針 (CLAUDE.md) と整合。runtime で AnimationClip 再 sample 不要 → Domain 層 Unity 非依存維持。ビルド後の差し替えも JSON 編集で完結 | 低 (既存 Expression の JSON serializer / deserializer を流用可能) |
| D-4 | ベース表情の SO 上保持位置 | `FacialCharacterProfileSO` の root に専用 field `_baseExpression` を持つ (Gaze と同じ root field パターン)。layer / kind / transitionDuration といった Expression 固有メタデータは持たず、AnimationClip 参照 + bake 済み weight 配列 + contribute mask のみ | Inspector で独立セクションとして示しやすい。 Expression list の中に「特殊な 1 個」を混ぜないので Expression API を肥大化させない | 低 |
| D-5 | Mask 伝搬経路 | `LayerBlender.LayerInput` 構造体に `BitArray ContributeMask` フィールドを追加。 `LayerInputSourceAggregator` が当該 layer の mask を構築して LayerInput に詰めて渡す。 BitArray は ref 型だが preallocated インスタンスを毎フレーム使い回し alloc 0 を維持 | 既存の LayerInput shape 拡張で済み、 `LayerBlender.Blend` シグネチャ変更が最小。 layerIndex / mask 不一致リスクなし | 低 |
| D-6 | Mask の動的性 | binding 初期化時に静的確定、 runtime 中は mask 値を再計算しない。 各 Expression / phoneme entry / analog binding は AnimationClip / blendshape 名から bake 時に mask を生成し、 runtime ではその参照を切り替えて使う (`ExpressionTriggerInputSource` は active Expression に対応する preallocated mask への参照を切替、 BitArray の中身書き換えは行わない) | per-frame の bit 操作・GC ビジー回避。 LipSync / Analog は binding が固定なので静的で十分。 Trigger も「個々の Expression に bake 済みの mask を持たせ、 source は参照を切替」 という形で alloc 0 を担保 | 中 (Expression 切替時の参照切替ロジックが必要) |
| D-7 | Transition 中の mask 挙動 | Expression A → B の遷移中は **A ∪ B (union)** を contribute mask とする。 遷移完了後に B 単独に切替。 union 用 BitArray は preallocated でその場で OR 計算 (`mask = A.Or(B)`) | 遷移中に「A 側 contribute index が一瞬 base に戻る」「B 側が遷移完了直前まで base のまま」 のような不連続を回避。 補間中の値は実際 lerp(A_value, B_value, t) で連続に変化するため、mask も連続的に union が自然 | 中 (union 用バッファを 1 つ余分に preallocated。 BitArray.Or は O(N/32)) |
| D-8 | Inspector ベース表情セクションの位置 | `FacialCharacterProfileSOInspector` で **GazeConfigs の隣**に配置。具体的順序: Reference Model → Save Status → Layers → **Base Expression** → GazeConfigs → AdapterBindings → Debug | 「付随的設定」 として Gaze と並ぶ位置にまとめると、 ユーザーがオプション設定を 1 グループとして認識しやすい。 Layers より下に置くことで「Layer の動作を補強する設定」 という意味合いも明確 | 低 |
| D-9 | OSC / ARKit Input Source のスコープ | `OscFloatAnalogSource` / `ArKitOscAnalogSource` も `IInputSource` 実装として contribute mask API 対応を本 spec に含める。 mask は binding 初期化時にバインドされた BlendShape index 集合で静的確定 (D-6 と同じ方針) | OSC / ARKit も runtime 中の binding 集合は変わらない (受信値の有無は valid 判定で扱い、 mask とは独立) ため D-6 と整合。 5 実装すべて mask 対応で完結 | 中 (実装ファイル増、 EditMode/PlayMode テストもこの 2 source 分追加) |

## Requirements

### Requirement 1: BlendShape Contribution Mask API
**Objective:** As a FacialControl ライブラリの実装者, I want 各 `IInputSource` が「自分が値を上書きする BlendShape index 集合」を宣言できる API を持つ, so that LayerBlender が「contribute する index のみ上書き、それ以外は下位 layer の値を保持」する mask 駆動の合成を実現できる.

#### Acceptance Criteria
1. The IInputSource interface shall expose 当該 source が contribute する BlendShape index 集合を `System.Collections.BitArray` で取得する API を提供する (see D-1)。
2. When `ExpressionTriggerInputSource` がスタック先頭の Expression を解決した時, the ExpressionTriggerInputSource shall 当該 Expression の AnimationClip に含まれる BlendShape index に対応する preallocated BitArray への参照を mask として返す (see D-6)。
3. When `ExpressionTriggerInputSource` が Expression A から B への遷移中である時, the ExpressionTriggerInputSource shall 遷移期間中 A ∪ B の union mask を返し、 遷移完了後に B 単独 mask へ切り替える (see D-7)。
4. When `ULipSyncProvider` が phoneme entry を出力する時, the ULipSyncProvider shall binding 初期化時に確定した phoneme entry の BlendShape index 集合を mask として返す (see D-6)。
5. When `AnalogBlendShapeInputSource` が値を出力する時, the AnalogBlendShapeInputSource shall binding 初期化時に確定した BlendShape index 集合を mask として返す (see D-6)。
6. When `OscFloatAnalogSource` または `ArKitOscAnalogSource` が値を出力する時, the source shall binding 初期化時に確定した BlendShape index 集合を mask として返す (see D-6, D-9)。
7. The IInputSource implementations shall mask 用 BitArray を preallocated とし、毎フレームのヒープ確保をゼロに保つ (see D-1)。
8. The Domain layer shall mask API の定義に `UnityEngine` 型を持ち込まない (Domain 層 Unity 非依存契約を維持する)。

### Requirement 2: Layer 単位の Contribution Mask 集約
**Objective:** As a FacialControl ライブラリの実装者, I want 1 つの layer に属する複数 `IInputSource` の mask を OR 集約して layer 全体の contribute mask を構築する, so that LayerBlender が layer ごとに「この layer が値を持つ index 集合」を一意に判定できる.

#### Acceptance Criteria
1. When `LayerInputSourceAggregator` が layer に属する source 群を集約する時, the LayerInputSourceAggregator shall 各 source の contribute mask を index 単位で論理 OR 集約した layer mask を出力する。
2. When 同一 layer 内の複数 source が同じ index に contribute する時, the LayerInputSourceAggregator shall その index について layer mask に true を立てる (D-1 ハイブリッド入力モデルの weighted-sum + clamp01 結果に対する mask は OR 集約とする)。
3. When layer 内の全 source が当該 index に contribute しない時, the LayerInputSourceAggregator shall その index について layer mask を false のままにする。
4. The LayerInputSourceAggregator shall mask 集約用バッファを preallocated とし、毎フレームのヒープ確保をゼロに保つ。
5. While layer に source が 1 つも紐づいていない, the LayerInputSourceAggregator shall layer mask を全 index false で出力する。

### Requirement 3: Mask 駆動の LayerBlender 合成
**Objective:** As a FacialControl ライブラリの実装者, I want `LayerBlender.Blend` を「contribute mask が立つ index のみ lerp 上書き、立たない index は下位 layer / ベース表情の値を保持」する挙動に変更する, so that 高優先度 layer が contribute しない index を意図せず 0 へ補間してしまう現状の問題を解消する.

#### Acceptance Criteria
1. The LayerBlender.LayerInput shall `BitArray ContributeMask` フィールドを保持する (see D-5)。
2. When `LayerBlender.Blend` が ある index `i` について layer の `ContributeMask` が true である時, the LayerBlender shall `output[i] = lerp(output[i], values[i], weight)` を実行する。
3. When `LayerBlender.Blend` が ある index `i` について layer の `ContributeMask` が false である時, the LayerBlender shall `output[i]` を変更せず下位 layer / ベース表情の値を保持する。
4. When ある layer のすべての mask bit が true かつベース表情が全 0 である時, the LayerBlender shall 旧仕様 (全 index lerp) と同一の出力を生成する (後方互換性の担保)。
5. The LayerBlender shall mask 解釈ロジックを Domain 層に閉じ、`UnityEngine` 型に依存しない。
6. The LayerBlender shall 毎フレーム呼び出しでのヒープ確保をゼロに保つ。

### Requirement 4: ベース表情 (Base Expression) のプロファイル定義
**Objective:** As a FacialControl ライブラリの利用者 (Unity エンジニア), I want `FacialCharacterProfileSO` の root に AnimationClip 参照ベースのベース表情を定義できる, so that 怒り顔キャラ・衣装固定 BlendShape など「Expression が contribute しない index に固定値を残す」用途を Inspector 上で簡潔に表現できる.

#### Acceptance Criteria
1. The FacialCharacterProfileSO shall root レベルに `_baseExpression` フィールドを保持し、1 個の AnimationClip 参照と bake 後の per-blendshape weight 配列を格納する。
2. When `FacialCharacterProfileSO` の `_baseExpression` に AnimationClip がアサインされている時, the FacialCharacterProfileSO shall 当該 AnimationClip からサンプリングした per-blendshape weight 配列を保持する。
3. Where `_baseExpression` の AnimationClip 参照が null である, the FacialCharacterProfileSO shall ベース表情の per-blendshape weight 配列を全 0 として扱う (現状互換)。
4. The FacialCharacterProfileSO shall ベース表情の bake 結果 (`BaseExpressionSnapshot` 等) を ScriptableObject に永続化する。
5. The FacialCharacterProfileSO shall `_baseExpression` 用の AnimationClip サンプリングを既存 `AnimationClipExpressionSampler` 経由で行う。

### Requirement 5: ベース表情による出力バッファ初期化
**Objective:** As a FacialControl ライブラリの実装者, I want `FacialControlMixer.ComputeOutput` が layer 合成前に出力バッファをベース表情の値で初期化する, so that すべての layer が contribute しない BlendShape index にベース表情の値が残り、固定表情キャラの実装が可能になる.

#### Acceptance Criteria
1. When `FacialControlMixer.ComputeOutput` が呼ばれた時, the FacialControlMixer shall layer 合成 (`LayerBlender.Blend`) を実行する前に出力バッファを `FacialCharacterProfileSO._baseExpression` の per-blendshape weight 配列で初期化する。
2. Where `FacialCharacterProfileSO._baseExpression` の AnimationClip 参照が null である, the FacialControlMixer shall 出力バッファを全 0 で初期化する (現状の `Array.Clear` 互換)。
3. While ある index `i` についていずれの layer も contribute しない (= すべての layer mask が false), the FacialControlMixer shall 当該 index の出力をベース表情の値のまま保持する。
4. The FacialControlMixer shall ベース表情からの初期化処理を毎フレームのヒープ確保ゼロで実行する。

### Requirement 6: AnimationClip からの Contribute Index 抽出 API
**Objective:** As a FacialControl ライブラリの実装者, I want `AnimationClipExpressionSampler` に「AnimationClip の curve binding に含まれる BlendShape 名集合」を返す API を追加する, so that Editor で Expression / Base Expression の bake 時に contribute mask を AnimationClip から自動抽出できる.

#### Acceptance Criteria
1. The AnimationClipExpressionSampler shall AnimationClip の curve binding を走査し、BlendShape 名集合 (および対応する index 集合) を返す API を提供する。
2. When AnimationClip に複数 BlendShape の curve が含まれる時, the AnimationClipExpressionSampler shall すべての BlendShape 名を漏れなく集合に含める。
3. When AnimationClip に BlendShape 以外の curve binding (例: Transform、Material) のみ含まれる時, the AnimationClipExpressionSampler shall BlendShape 名集合を空として返す。
4. The AnimationClipExpressionSampler shall BlendShape 命名規則を固定せず、2 バイト文字・特殊記号を含む BlendShape 名を正しく扱う。
5. The AnimationClipExpressionSampler の本 API は Editor 専用機能として提供される (`Editor/Sampling/` 配下)。

### Requirement 7: Editor 自動保存時の Snapshot 透過生成
**Objective:** As a FacialControl ライブラリの利用者, I want Editor 上で Expression / Base Expression の AnimationClip がアサイン・変更されたタイミングで自動的に内部 snapshot (per-blendshape weight 配列 + contribute index 集合) が生成・永続化される, so that bake という工程を意識せずとも AnimationClip を差し替えるだけで合成結果に即時反映される.

#### Acceptance Criteria
1. When Editor が `FacialCharacterProfileSO` 上の Expression または Base Expression の AnimationClip を変更・保存する時, the Editor shall 既存の自動保存パイプライン (`OnSerializedObjectChanged` → `delayCall FlushAutoSave`) の中で当該 AnimationClip から per-blendshape weight 配列と contribute index 集合を内部生成する (see D-2)。
2. The Editor shall Expression の snapshot を `ExpressionSnapshot` (または同等のメタデータ) に格納する。
3. The Editor shall Base Expression の snapshot を `BaseExpressionSnapshot` (または同等のメタデータ) に格納する。
4. When AnimationClip が null へ変更された時, the Editor shall 対応する snapshot を空 (全 0 weight / 空 mask) として透過的に再生成する (see D-2)。
5. The Editor shall snapshot 生成処理を `AnimationClipExpressionSampler` 経由で実行する。
6. The Editor shall ユーザーに「bake」「rebake」といった明示操作ボタンを露出させない (see D-2)。

### Requirement 8: FacialCharacterProfileSO Inspector の Base Expression UI
**Objective:** As a FacialControl ライブラリの利用者 (Unity エンジニア), I want `FacialCharacterProfileSOInspector` に Gaze と並ぶ「ベース表情」専用セクション (オプション設定枠) が表示される, so that AnimationClip のアサインと用途ガイドだけでベース表情を簡潔に管理できる.

#### Acceptance Criteria
1. The FacialCharacterProfileSOInspector shall Layers セクションと GazeConfigs セクションの間に「ベース表情」専用セクションを表示する (see D-2, D-8)。
2. The FacialCharacterProfileSOInspector shall ベース表情セクション内に AnimationClip ObjectField を配置する。
3. Where ベース表情の AnimationClip が未設定, the FacialCharacterProfileSOInspector shall ベース表情の用途 (常時怒り顔キャラ / 衣装で BlendShape をロックするケース等) を説明する HelpBox を表示する。
4. The FacialCharacterProfileSOInspector shall ベース表情セクションに「bake」「rebake」等のボタンを露出させない (see D-2)。
5. The FacialCharacterProfileSOInspector shall UI Toolkit で実装され、IMGUI を新規 UI に使わない。

### Requirement 9: profile.json Schema への baseExpression フィールド追加
**Objective:** As a FacialControl ライブラリの利用者, I want profile.json に `baseExpression` フィールドが追加され、欠如時は全 0 ベースとして読み込まれる, so that JSON ファースト永続化方針を維持しつつベース表情をビルド後も差し替え可能にする.

#### Acceptance Criteria
1. The profile.json schema shall root レベルに `baseExpression` フィールドを保持し、その内部スキーマは通常の Expression と同形式 (= bake 後の `{blendShapeName, weight}` 配列を持つ) とする (see D-3)。
2. When profile.json の読み込み時に `baseExpression` フィールドが存在する時, the FacialControl shall 当該フィールドを `FacialCharacterProfileSO._baseExpression` に反映する。
3. If profile.json に `baseExpression` フィールドが存在しない, then the FacialControl shall ベース表情を空 (全 0 / 空 mask) として読み込む (forward compatibility)。
4. The FacialControl shall profile.json の `baseExpression` パースを `JsonUtility` ベースで実装し、 通常の Expression と同じ serializer/deserializer を流用する (see D-3)。
5. The FacialControl shall AnimationClip の asset path を `baseExpression` の JSON に含めない (= runtime 側は bake 済み weight 配列のみで完結する。 see D-3)。
6. The FacialControl shall preview 段階の方針に従い、本 schema 変更に対する migration ツールを提供しない。

### Requirement 10: 自動テストによる挙動保証
**Objective:** As a FacialControl ライブラリの実装者, I want contribution mask とベース表情の各層に対する EditMode/PlayMode テストが整備される, so that TDD (Red-Green-Refactor) による品質基準と、emotion + lipsync 合成シナリオの後方互換性を確実に担保できる.

#### Acceptance Criteria
1. The LayerBlenderTests shall mask 全 index 立て + base 全 0 のとき出力が旧 lerp 挙動と一致することを検証する。
2. The LayerBlenderTests shall mask 部分立てのとき contribute しない index がベース表情 / 下位 layer の値を保持することを検証する。
3. The LayerInputSourceAggregatorTests shall 複数 source の contribute mask が index 単位で論理 OR 集約されることを検証する。
4. The FacialControlMixerTests shall ベース表情 AnimationClip がアサインされた状態で出力バッファがベース表情値で初期化されることを検証する。
5. The FacialControlMixerTests shall ベース表情 AnimationClip が null のとき出力バッファが全 0 で初期化されることを検証する (現状互換)。
6. The AnimationClipExpressionSamplerTests shall AnimationClip の curve binding から BlendShape 名集合 / index 集合が正しく抽出されることを検証する。
7. When emotion layer (priority=0) と lipsync layer (priority=1) が同時に動作する統合テストを実行する時, the FacialControl shall 「目・眉は emotion の値、口は lipsync で上書き、contribute されない index はベース表情のまま」という出力を生成することを検証する。
8. The Tests shall 配置基準として「モック/Fake のみで同期実行可能なテストは EditMode、MonoBehaviour ライフサイクル / Playable / 実時間補間が必要なテストは PlayMode」に従う。
9. The LayerBlenderTests shall Expression A → B 遷移中に mask が A ∪ B (union) になることを検証する (see D-7)。
10. The Tests shall `OscFloatAnalogSource` および `ArKitOscAnalogSource` の mask API を EditMode テストでカバーする (see D-9)。

## Dig Summary

### Rounds Completed
- Rounds: 3
- Questions asked: 9
- Decisions made: 9 (D-1 〜 D-9)

### Key Discoveries
1. **ユーザーは bake をユーザー操作として意識させたくない** (D-2) — Gaze 同様の「オプション設定枠」として扱い、AnimationClip の差し替えだけで内部 snapshot を透過的に再生成する設計に変わった。 これにより Inspector に bake/rebake ボタンを置かない、 自動保存パイプライン (`OnSerializedObjectChanged → delayCall FlushAutoSave`) に乗せる、 という運用が確定。
2. **Mask の動的性は「参照切替」で表現** (D-6, D-7) — BitArray の中身を毎フレーム書き換えるのではなく、 各 Expression / phoneme entry / analog binding が bake 時に自分の mask BitArray を持ち、 source は active な対象に応じて preallocated 参照を切り替える方針。 transition 中は A ∪ B union 用 BitArray を 1 個追加で preallocated して、 切替期間だけ OR 演算した参照を返す。
3. **OSC / ARKit も 5 実装すべて mask 対応** (D-9) — 当初は Trigger / LipSync / Analog の 3 実装に絞る案も提示されたが、 ユーザーは「全 IInputSource 実装で対応する」 を選択。 OSC は受信値の有無を valid 判定で扱い、 mask は binding 初期化時にバインドされた BlendShape index 集合で静的確定するという D-6 と同じ方針で対応する。

### Decisions Table

| ID | Topic | Decision | Risk |
|----|-------|----------|------|
| D-1 | Mask データ構造 | `System.Collections.BitArray` (preallocated) | 低 |
| D-2 | Bake トリガー / UX | bake をユーザー操作として露出させず、Gaze 同様の専用オプション枠で透過的に処理 | 中 |
| D-3 | profile.json 表現 | 通常 Expression と同じ形式 (bake 後 `{blendShapeName, weight}` のみ、AnimationClip path は載せない) | 低 |
| D-4 | SO 上の保持位置 | root に専用 field `_baseExpression` (Gaze と同じパターン) | 低 |
| D-5 | Mask 伝搬 | `LayerBlender.LayerInput` 構造体に `BitArray ContributeMask` フィールド追加 | 低 |
| D-6 | Mask の動的性 | binding 初期化時に静的確定、 runtime は参照切替のみ | 中 |
| D-7 | Transition 中 mask | A ∪ B (union)、 完了後に B 単独へ切替 | 中 |
| D-8 | Inspector 配置 | Layers セクションと GazeConfigs セクションの間 | 低 |
| D-9 | スコープ | OSC / ARKit 含む 5 実装全てを対象 | 中 (実装/テストファイル増) |

### Remaining Risks (Design Phase で詰める)
- `BaseExpressionSnapshot` の具体的フォーマット (`ExpressionSnapshot` を流用するか専用型を作るか) は design で確定する
- `LayerBehaviour` (Adapters 層 PlayableBehaviour) から `LayerBlender.LayerInput.ContributeMask` への詰込み経路 (PlayableGraph 経由でどう mask を運ぶか) は design で確認する
- 既存 `LayerBlenderTests` / `LayerInputSourceAggregatorTests` の更新スコープ (削除 vs 後方互換テストとして残置) は tasks phase で個別判断する
- BitArray の preallocated インスタンスのライフサイクル (誰が確保 / 解放するか、 BlendShape 数変更時の再確保) は design で明確化する
