# Requirements Document

## Project Description (Input)
feature_name: blendshape-output-refactor（旧 burst-blendshape-output。2026-06-26 にリネーム。Burst フル作り直しから①GCゼロ+③デッドコード一掃/差し替え抽象へ縮小。経緯は下記「スコープ改訂メモ」を参照）

## スコープ改訂メモ（2026-06-25）
当初の本スペックは「BlendShape 出力経路を `PlayableGraph` + `AnimationScriptPlayable` + `IAnimationJob`（Burst）へ全面作り直しし、同時 10 体の CPU スループットを最大化する」フルスコープ（工数 XL）であった。validate-gap 後の判断により、本スペックの目的を以下の 2 点に縮小する。

- **① 毎フレーム GC アロケーションゼロ**: managed の集約／ブレンド経路に残る毎フレームの `List`／`Dictionary` 確保（`ExpressionUseCase.GetActiveExpressions` の `List`、`LayerUseCase.GroupByLayer` の `Dictionary`）を撲滅する。
- **③ デッドコード一掃 ＋ 差し替え可能な出力抽象**: デッド状態の `PlayableGraph` 資産（`FacialControlMixer` / `LayerPlayable` / 現行 `PlayableGraphBuilder` / 未使用 `PropertyStreamHandleCache`）を撤去し、BlendShape 出力を将来 Job/Burst 実装へ差し替え可能なインターフェースの背後に整理する。

**② 同時 10 体並列の CPU スループット最大化（`IAnimationJob`/Burst フル作り直し）は本スペックから外し、将来送りとする**（現状性能の実測で 10 体が性能要件を割ったときに別スペックで再評価）。本スペックでは BlendShape 出力の実装は現行の **managed 直書き（`SkinnedMeshRenderer.SetBlendShapeWeight`）を維持**し、それを差し替え可能な抽象の背後に置く。`IAnimationJob` / `AnimationStream` / `AnimationScriptPlayable` による新出力経路と aggregate/blend の Burst 化は本スペックでは導入しない。

## 目的
3D キャラクターの BlendShape 出力経路から **デッド状態の `PlayableGraph` 資産を撤去**し、**managed の集約／ブレンド経路に残る毎フレーム GC アロケーションを撲滅**し、**BlendShape 出力を差し替え可能なインターフェースの背後に整理**する。これにより、コードベースのデッドコードを排除しつつ、配信・ゲーム実行中の GC スパイクを解消し、将来の Job/Burst 化に向けた差し替え点を用意する。出力実装そのものは現行の managed 直書きを維持し、既存機能を一切回帰させない。

## 現状（作り直しの出発点）
- ランタイムのブレンド計算は `LayerUseCase`（Application）→ `LayerInputSourceAggregator` / `LayerBlender` / `LayerExpressionSource`（Domain, 通常 C#）で行い、`FacialController.LateUpdate` が `BlendedOutputSpan` を読んで `SkinnedMeshRenderer.SetBlendShapeWeight` に直書きしている。**これが生きた出力実体である。**
- `PlayableGraph`（`PlayableGraphBuilder` / `FacialControlMixer` / `LayerPlayable` / `AnimationPlayableOutput`）は構築・Play されるが、`FacialControlMixer` は `PlayableBehaviour`（ScriptPlayable）で `AnimationStream` に一切書かず、出力 `NativeArray` は誰にも読まれない＝**事実上デッド**。コミット 296ef96（2026-04-22, Multi Source Blend 導入時）に「PlayableGraph の出力はバイパスする（preview.2 以降で撤去検討）」とコメント。`PropertyStreamHandleCache` も実装済だがどこからも生成されず未使用。
- 毎フレームの GC 発生源は `ExpressionUseCase.GetActiveExpressions` の `List` 確保と `LayerUseCase.GroupByLayer` の `Dictionary` 確保に特定済み（dig）。

## 今回のスコープ（採用方針）
- **デッド `PlayableGraph` 資産を撤去する**。`FacialControlMixer` / `LayerPlayable` / 現行 `PlayableGraphBuilder` / 未使用 `PropertyStreamHandleCache` と、それらに依存する検証資産を撤去する。
- **managed の集約／ブレンド経路の毎フレーム GC をゼロにする**。`GetActiveExpressions` の `List` と `GroupByLayer` の `Dictionary` を事前確保・再利用バッファへ置き換える。集約／ブレンドのアルゴリズム自体（通常 C#）は維持する。
- **BlendShape 出力を差し替え可能なインターフェースの背後に整理する**。現行の `SkinnedMeshRenderer.SetBlendShapeWeight` 直書きを、出力ライターの抽象（例: `IBlendShapeOutputWriter` 相当）の一実装として保持し、将来 Job/Burst 実装を同抽象に差し込める構造にする。
- 既存の `LayerInputSourceWeightBuffer`（任意スレッド書込み・SwapIfDirty）/ per-layer 複数入力ソース集約 / `layerOverrideMask` 抑制 / overlay suppress / lipsync phoneme overlay / ARKit-PerfectSync 自動生成 / `TransitionCurve` 評価 などの既存挙動を**一切回帰させない**こと。

## スコープ外（今回やらない）
- **`IAnimationJob` / `AnimationStream` / `AnimationScriptPlayable` による新 BlendShape 出力経路の導入（旧スコープ②、将来送り）**。本スペックでは出力は managed 直書きを維持する。
- **aggregate / blend / 遷移計算の Burst 化（旧スコープ②、将来送り）**。Burst / `Unity.Burst` / Job 内 struct 移植・`TransitionCurve` の Burst 内評価（LUT 等）は本スペックでは行わない。
- BoneWriter のボーン直書きの `IAnimationJob` / `OnAnimatorIK` 化（bone-control 側の別仕様。backlog M-8）。
- Timeline 統合本体（将来対応）。
- 既知の「active 取得 2 系統分断」（系1 `ExpressionUseCase` / 系2 `ExpressionTriggerInputSource`）の是正そのもの。本スペックは抑制・overlay の**非回帰**のみを保証し、当該バグの修正はスコープ外（別途扱い）。

## 主要な論点（dig で詰めた／将来送り）
- 出力ライター抽象の境界（どこまでをインターフェースに切り出し、どこから managed 実装に残すか）。
- デッド `PlayableGraph` 撤去後、ベース AnimationClip のサンプリング経路（`AnimationClipCache` / sampler）が `PlayableGraph` に依存していないことの確認と、破棄時リソース解放の整理。
- 毎フレ GC 撲滅のための事前確保バッファのライフサイクル設計。
- （将来送り）Burst 化スコープ・カーネル配置・main/Job 境界・`TransitionCurve` の Burst 内評価。旧 D-2/D-3/D-4 を参照。

## Introduction
本仕様は、FacialControl コアパッケージ（`com.hidano.facialcontrol`）の BlendShape 出力経路から、**デッド状態の `PlayableGraph` 資産を撤去**し、**managed の集約／ブレンド経路に残る毎フレーム GC アロケーションを撲滅**し、**BlendShape 出力を差し替え可能なインターフェースの背後に整理**することを目的とする。BlendShape 出力の実装は現行の managed 直書き（`SkinnedMeshRenderer.SetBlendShapeWeight`）を維持し、`IAnimationJob` / `AnimationStream` による新出力経路と aggregate/blend の Burst 化は本スペックでは導入しない（将来送り）。最終目標は、毎フレームの GC アロケーションゼロを満たしつつ、既存機能（マルチソース／レイヤー別ブレンド、`layerOverrideMask` 抑制、overlay suppress、リップシンク音素 overlay、ARKit/PerfectSync 自動生成、`LayerInputSourceWeightBuffer` 経由のランタイム重み書込み、`TransitionCurve` 評価）を一切回帰させないことである。

本フェーズでは実装方式（出力ライター抽象の正確な境界、撤去に伴うテスト資産の差し替え方など）は確定させず、満たすべき振る舞い・制約・性能 NFR・非回帰要件のみを定義する。具体的な設計判断は design フェーズで行う。

## Boundary Context
- **In scope（本仕様の対象）**:
  - デッド `PlayableGraph` 資産（`FacialControlMixer` / `LayerPlayable` / 現行 `PlayableGraphBuilder` / 未使用 `PropertyStreamHandleCache`）の撤去
  - managed 集約／ブレンド経路の毎フレーム GC アロケーション撲滅（脱 per-frame `List` / `Dictionary`）
  - BlendShape 出力の差し替え可能なインターフェース化（managed 直書きを一実装として保持）
  - 既存挙動（マルチソース集約・レイヤー別ブレンド・各種抑制・overlay・自動生成・ランタイム重み書込み・遷移カーブ評価）の非回帰維持
  - 性能 NFR（毎フレーム GC ゼロ）の達成、同時多体動作の既存どおりの維持
- **Out of scope（本仕様で扱わない）**:
  - `IAnimationJob` / `AnimationStream` / `AnimationScriptPlayable` による新出力経路の導入（将来送り）
  - aggregate / blend / 遷移計算の Burst 化（将来送り）
  - BoneWriter のボーン直書きの `IAnimationJob` / `OnAnimatorIK` 化（backlog M-8）
  - Timeline 統合本体（将来対応）
  - 「active 取得 2 系統分断」バグの是正（非回帰のみ保証）
- **Adjacent expectations（隣接システム／仕様への期待）**:
  - BoneWriter 経路は本仕様の変更後も従来どおり動作し続けること
  - 出力ライター抽象は、将来 `IAnimationJob`/Burst 実装を同抽象へ差し込める構造を保つこと（backlog M-8「インターフェース設計で Jobs/Burst 差替え可能」を満たす）

## Open Questions and Decisions (Dig)

本セクションは dig 面談および 2026-06-25 のスコープ縮小判断で確定した方向を記録する。各決定は design フェーズの制約となる。

| ID | トピック | 決定 | 根拠 | リスク | 関連要件 |
|----|---------|------|------|--------|---------|
| D-1 | 既存 PlayableGraph 資産の扱い | デッドな `FacialControlMixer` / `LayerPlayable` / 現行 `PlayableGraphBuilder` / 未使用 `PropertyStreamHandleCache` を**撤去**する。新規 `PlayableGraph`／`AnimationScriptPlayable` は本スペックでは新設しない | デッドコードを残さず最小構成にする。出力は managed 直書きを維持するため新グラフは不要 | 中: 既存テスト/参照の撤去作業。撤去後にベース Clip サンプリングが graph 非依存であることの確認が必要 | Req 1 |
| D-5 | スコープ縮小（旧 D-2 を撤回） | Burst/Job フル作り直し（旧スコープ②＝同時 10 体の CPU スループット最大化）を**本スペックから外し将来送り**とする。本スペックは ① GC ゼロ ＋ ③ デッドコード一掃／差し替え抽象に縮小。出力実装は managed 直書きを維持 | validate-gap の結果、XL コストの大半は②のためであり、①は managed 経路の脱 `List`/`Dictionary` で、③は撤去＋抽象で達成可能。②は欲しい物に対しオーバースペックと判断（2026-06-25） | 低: managed 維持のため未知技術（IAnimationJob/Burst）リスクを回避。将来②着手時に本スペックの差し替え抽象が踏み台になる | Req 1, Req 2, Req 6, Req 7 |
| D-3（将来送り） | Burst 数値カーネルの配置 | 旧決定（数値カーネルを Domain、`[BurstCompile]` を Adapters）は **②着手時に再評価**。本スペックでは適用しない | Burst を導入しないため本スペックでは保留 | — | （将来）|
| D-4（将来送り） | main / Job 境界 | 旧決定（深い境界・Job 内完結）は **②着手時に再評価**。本スペックでは Job を導入しないため適用しない | 同上 | — | （将来）|

### 設計フェーズへ送る論点
- 出力ライター抽象（`IBlendShapeOutputWriter` 相当）の境界設計：どこまでを抽象に切り出し、将来 Job/Burst 実装をどう差し込むか。
- デッド `PlayableGraph` 撤去後の破棄時リソース解放の整理（graph 解放処理の除去・残る native バッファの解放）。
- 毎フレ GC 撲滅の具体手段：`GetActiveExpressions` / `GroupByLayer` の事前確保バッファ化のデータ構造（容量・再利用ライフサイクル）。

## Dig Summary

- **実施ラウンド**: 3 ラウンド（① 既存 Graph 資産 + Burst 必要性の再確認、② Burst/Job 概念の共有、③ カーネル配置 + Job 境界の確定）＋ validate-gap 後のスコープ縮小判断（2026-06-25）。
- **確定した決定**: D-1（PlayableGraph 撤去）、D-5（スコープ縮小・旧 D-2 撤回）。D-3 / D-4 は②着手時へ将来送り。
- **主要な発見**:
  1. **Burst は本スペックの目的（デッドコード一掃・GC ゼロ・差し替え抽象）には不要**。これらは managed 経路の最適化と抽象化、デッド資産撤去で達成できる。同時 10 体の CPU スループット最大化（②）のみが Burst/Job を正当化するが、現状性能は未実測であり、ユーザー判断で②は将来送りとした。
  2. **現行の毎フレーム GC 発生源を特定**（`ExpressionUseCase.GetActiveExpressions` の `List`、`LayerUseCase.GroupByLayer` の `Dictionary`）。これらの事前確保化が①の核心。
  3. デッド `PlayableGraph` は出力に寄与しておらず、撤去しても出力（managed 直書き）に影響しない。ベース Clip サンプリングが graph 非依存であることの確認のみ design で要する。
- **残存リスク（design フェーズへ）**: 「設計フェーズへ送る論点」節を参照。いずれも HOW のみ未確定で、要件 WHAT は確定。

## Requirements

### Requirement 1: デッド PlayableGraph の撤去と差し替え可能な出力抽象
**Objective:** Unity エンジニア（パッケージ利用者・メンテナ）として、出力に寄与していないデッドな `PlayableGraph` 資産を撤去し、BlendShape 出力を差し替え可能なインターフェースの背後に整理したい。これにより、コードベースからデッドコードを排除し、将来の Job/Burst 実装への差し替え点を用意したいからである。

#### Acceptance Criteria
1. The BlendShape Output System shall デッド状態の `FacialControlMixer`（`AnimationStream` へ書かない `ScriptPlayable`）・`LayerPlayable`・現行 `PlayableGraphBuilder`・未使用 `PropertyStreamHandleCache` を撤去する。
2. The BlendShape Output System shall BlendShape 出力の最終書込みを、差し替え可能な出力ライター抽象（例: `IBlendShapeOutputWriter` 相当）を介して行う。
3. The BlendShape Output System shall 当該出力ライター抽象の既定実装として、現行の `SkinnedMeshRenderer.SetBlendShapeWeight` 直書き（managed）を提供する。
4. The BlendShape Output System shall 出力ライター抽象を、将来 `IAnimationJob`/Burst ベースの実装へ差し替え可能な形（backlog M-8 の方針）に保つ。
5. While 出力経路が動作している間、the BlendShape Output System shall BoneWriter によるボーン制御経路を従来どおり機能させ続ける。
6. When デッド `PlayableGraph` を撤去するとき、the BlendShape Output System shall ベース AnimationClip のサンプリングおよび最終 BlendShape 出力結果を撤去前と同一に保つ。

### Requirement 2: 毎フレーム GC アロケーションゼロ（managed 集約経路の脱 List/Dictionary）
**Objective:** Unity エンジニアとして、定常運転時の BlendShape 出力処理で毎フレームの GC アロケーションをゼロにしたい。これにより、配信・ゲーム実行中の GC スパイクとフレーム落ちを回避したいからである。

#### Acceptance Criteria
1. While 定常運転（毎フレームの出力処理）が継続している間、the BlendShape Output System shall マネージドヒープのアロケーションを発生させない（毎フレーム GC アロケーションゼロ）。
2. The BlendShape Output System shall `ExpressionUseCase.GetActiveExpressions` 相当のアクティブ表情収集を、毎フレームの `List` 新規確保なしで行う（事前確保・再利用バッファ等）。
3. The BlendShape Output System shall `LayerUseCase.GroupByLayer` 相当のレイヤー別グルーピングを、毎フレームの `Dictionary` 新規確保なしで行う。
4. The BlendShape Output System shall 集約・ブレンドに用いる `NativeArray` 等のネイティブ確保を一度行い、毎フレームの再確保を行わない。
5. When キャラクターまたは出力経路が破棄されるとき、the BlendShape Output System shall 確保済みのネイティブメモリ等のリソースを解放する。
6. The BlendShape Output System shall 同時 10 体以上のキャラクターの BlendShape 出力を、既存どおり成立させる（CPU スループットの最大化は本スペックの対象外）。

### Requirement 3: ハイブリッド入力モデルとランタイム重み書込みの非回帰
**Objective:** Unity エンジニアとして、ハイブリッド入力モデル（ExpressionTrigger + ValueProvider）と任意スレッドからのランタイム重み書込みを、本スペックの変更後も従来どおり使い続けたい。これにより、既存の入力連携コードを変更せずに済ませたいからである。

#### Acceptance Criteria
1. The BlendShape Output System shall per-layer の複数入力ソース集約（weighted-sum → clamp01）を既存仕様どおりに維持する。
2. When 任意スレッドから `LayerInputSourceWeightBuffer` に重みが書き込まれるとき、the BlendShape Output System shall その書込みを既存の SwapIfDirty セマンティクスで安全に取り込む。
3. When `LayerInputSourceWeightBuffer` の重みが更新された次の出力フレームになるとき、the BlendShape Output System shall 更新後の重みを集約・ブレンド結果に反映する。
4. The BlendShape Output System shall ExpressionTrigger（バイナリのスタックベース）と ValueProvider（直接値書込み）の双方を入力ソースとして従来どおり受け付ける。

### Requirement 4: 抑制・オーバーレイ機能の非回帰
**Objective:** Unity エンジニアとして、レイヤー抑制とオーバーレイ系機能（`layerOverrideMask` 抑制・overlay suppress・リップシンク音素 overlay）を本スペックの変更後も同一の結果で得たい。これにより、既存プロファイルや配信セットアップが破綻しないようにしたいからである。

#### Acceptance Criteria
1. While `layerOverrideMask` による抑制が有効な間、the BlendShape Output System shall 対象レイヤーの寄与を既存仕様どおりに抑制する。
2. While overlay suppress が有効な間、the BlendShape Output System shall 抑制対象の表情寄与を既存仕様どおりに抑制する。
3. When リップシンク音素 overlay が適用されるとき、the BlendShape Output System shall 該当 BlendShape へ既存仕様どおりに音素 overlay の重みを反映する。
4. The BlendShape Output System shall 抑制・overlay の適用結果が、本スペックの変更前と同一の最終 BlendShape 重みになることを保証する。

### Requirement 5: ARKit / PerfectSync およびプロファイル仕様の非回帰
**Objective:** Unity エンジニア（VTuber 配信向け利用者）として、ARKit 52 / PerfectSync の自動検出・プロファイル自動生成と BlendShape 命名規則の自由度を、本スペックの変更後も完全に維持したい。これにより、フェイシャルキャプチャ連動が引き続き正しく動作するようにしたいからである。

#### Acceptance Criteria
1. The BlendShape Output System shall ARKit 52 / PerfectSync の自動検出およびプロファイル自動生成の既存挙動を維持する。
2. If モデルに存在しない（未対応の）BlendShape パラメータが与えられるとき、then the BlendShape Output System shall それを警告なしでスキップする。
3. The BlendShape Output System shall BlendShape 命名規則を固定せず、2 バイト文字・特殊記号を含む BlendShape 名を正しく扱う。
4. The BlendShape Output System shall プロファイル（基本 AnimationClip + カテゴリ + リップシンク用 Clip + 遷移時間）に基づく表情合成の既存仕様を維持する。

### Requirement 6: 遷移補間と TransitionCurve の非回帰
**Objective:** Unity エンジニアとして、表情遷移の線形補間・イージング・カスタムカーブによる上書きを、本スペックの変更後も従来どおりの結果で評価したい。これにより、表情の動きの質感が変化しないようにしたいからである。

#### Acceptance Criteria
1. The BlendShape Output System shall 遷移補間を既定の線形補間で行い、`TransitionCurve`（イージング／カスタムカーブ）が指定された場合はそれで上書きする。
2. The BlendShape Output System shall 遷移時間 0〜1 秒（既定 0.25 秒）の範囲を既存仕様どおりに扱う。
3. When 遷移中に新しい表情がトリガーされるとき、the BlendShape Output System shall 現在の補間値から即座に新しい遷移を開始する。
4. The BlendShape Output System shall 遷移補間の出力が、本スペックの変更前と等価な結果になることを保証する。

### Requirement 7: クリーンアーキテクチャ整合と差替え可能性
**Objective:** Unity エンジニアとして、本スペックの変更後も出力経路実装を差替え可能な抽象として保ち、クリーンアーキテクチャの依存方向契約を維持したい。これにより、将来の Job/Burst 化や代替実装への置き換えと、パッケージのアーキテクチャ品質を両立させたいからである。

#### Acceptance Criteria
1. The BlendShape Output System shall backlog M-8 の方針に沿った差替え可能なインターフェース抽象（将来 Job/Burst 実装を差し込める出力ライター抽象）を提供する。
2. The BlendShape Output System shall Engine 機能（`SkinnedMeshRenderer` 等）への依存を Adapters 層に封じ込め、asmdef の依存方向契約（Adapters → Application → Domain）を破らない。
3. The BlendShape Output System shall レンダーパイプライン非依存・シェーダー非依存・物理演算非依存の契約を維持する。
4. The BlendShape Output System shall エラーハンドリングを Unity 標準ログ（`Debug.Log/Warning/Error`）の範囲で行い、標準例外を超えるカスタム例外型を新設しない。

### Requirement 8: テスト整合と回帰検証
**Objective:** Unity エンジニアとして、既存の EditMode / PlayMode テスト資産と整合した形で本スペックの回帰検証を行いたい。これにより、TDD（Red-Green-Refactor）を維持しつつ非回帰を客観的に保証したいからである。

#### Acceptance Criteria
1. The BlendShape Output System shall 既存の集約・ブレンド・遷移に関するドメインロジックを、EditMode（モック／Fake のみ・同期実行）で検証可能な形に保つ。
2. When デッド `PlayableGraph` 資産を撤去するとき、the BlendShape Output System shall 当該資産に依存していた検証資産（`PlayableGraphBuilderTests` / `LayerPlayableTests` / `FacialControlMixerBaseExpressionTests` 等）を整理・差し替える。
3. When 本スペックの変更後の出力結果を検証するとき、the BlendShape Output System shall 既存挙動（マルチソース集約・抑制・overlay・遷移補間）に対する非回帰を確認できるテストを伴う。
4. The BlendShape Output System shall 毎フレーム GC アロケーションゼロを検証可能な計測手段（例: アロケーション計測テスト）を伴う。
