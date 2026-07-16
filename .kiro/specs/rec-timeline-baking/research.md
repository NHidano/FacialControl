# Research & Design Decisions — rec-timeline-baking

## Summary
- **Feature**: `rec-timeline-baking`
- **Discovery Scope**: Complex Integration（既存入力パイプラインへの Timeline 統合 + Editor ベイク基盤）
- **Key Findings**:
  - 系2 active 表情解決（`Layer2ActiveExpressionProvider`）は `ExpressionTriggerInputSourceBase.ActiveExpressionIds` を直接読むため、`blendShapeCount = 0` で構築した派生 sink は「値出力ゼロ・active 状態のみ供給」を**構造的に**実現できる（Req 5.2 / 5.4 の核）
  - `LayerInputSourceAggregator.AggregateInternal` には per-source 値（scratch、pre-weight）の観測点となるコード位置が存在するが、観測フックは未実装。追加は 1 フィールド + null チェック 1 箇所の加算的変更で済む（Req 4.2）
  - Unity Timeline のカスタム Track は `TrackAsset.CreateTrackMixer` + `PlayableBehaviour.ProcessFrame` + `IPropertyPreview.GatherProperties`（Edit Mode プレビュー）が公式パターン。Timeline の PlayableGraph は PlayableDirector 所有であり、FacialController のデッド PlayableGraph 出力経路とは別物

## Research Log

### Unity Timeline カスタム Track API（1.8.9）
- **Context**: 独自 Track / mixer / Edit Mode スクラブプレビューの実現手段の確認
- **Sources Consulted**:
  - [Extending Timeline: A practical guide](https://unity.com/blog/engine-platform/extending-timeline-practical-guide)
  - [Class TrackAsset | Timeline](https://docs.unity3d.com/Packages/com.unity.timeline@1.6/api/UnityEngine.Timeline.TrackAsset.html)
  - [Interface IPropertyPreview | Timeline](https://docs.unity3d.com/Packages/com.unity.timeline@1.4/api/UnityEngine.Timeline.IPropertyPreview.html)
- **Findings**:
  - カスタム Track は `TrackAsset` 派生 + `[TrackClipType]` + `[TrackBindingType]`、mixer は `CreateTrackMixer` が返す `ScriptPlayable<T>`（`PlayableBehaviour` 派生）
  - mixer の `ProcessFrame` は再生・スクラブ両方で評価され、`playable.GetTime()` で Track ローカル時刻を取得できる（独自の絶対時刻取得は不要 → Req 8.2 と整合）
  - Edit Mode プレビューは `TrackAsset` が `IPropertyPreview` を実装済みで、`GatherProperties` で driven-property 登録した対象は preview 解除時に自動復元される
  - `TimelineAsset` / Track / Clip はランタイムでも読取可能（ランタイムのハッシュ照合 Req 6.3 に利用可能）
  - `ClipCaps.None` の Track では同一 Track 上のクリップ重なりは編集不可（ブレンド領域が作れない）→ 重なる表情（スタック）はレーン分割が必要
- **Implications**: Track/mixer/プレビューはすべて Timeline 標準機構で成立。クリップ重なり制約から「レイヤー親 Track + 子レーン Track」構成を導入する

### 既存コードベース統合点分析
- **Context**: 「もう一つの入力アダプター」としての接続点と、ソース単位ベイクの観測点の特定
- **Sources Consulted**: `ExpressionTriggerInputSourceBase.cs` / `LayerInputSourceAggregator.cs` / `ValueProviderInputSourceBase.cs` / `AdapterBindingBase.cs` / `AdapterBuildContext.cs` / `OscReceiverAdapterBinding.cs` / `GazeVector2InputSource.cs` / `Layer2ActiveExpressionProvider.cs` / `FacialController.cs` / `IInputSourceRegistry.cs` / `IAnalogInputSource.cs`
- **Findings**:
  - アダプター拡張の正道: `AdapterBindingBase` 継承 + `[Serializable]` + `[FacialAdapterBinding]`。`OnStart(in AdapterBuildContext)` で helper MonoBehaviour を `ctx.HostGameObject` に AddComponent し、`ctx.InputSourceRegistry.Register(slug, source)` で入力源登録（OscReceiverAdapterBinding が参考型）
  - `ExpressionTriggerInputSourceBase.TriggerOn/TriggerOff` が遷移状態機械の入口。「遷移中の再トリガーは現在の補間値から開始」は `StartTransition` の `_currentValues` スナップショットで実現。`TryWriteValues` は非 virtual のため値出力の抑止は override では不可 → `blendShapeCount = 0` 構築で書込みバイト数ゼロにするのが唯一の非改修手段
  - `TriggerOn` で未知の expressionId を渡した場合、`FindExpressionById` が null を返し「目標ゼロ + 既定遷移時間」で安全に処理される（例外なし）→ 削除済み expressionId のクリップは core 挙動そのままで破綻しない（Req 3.3）
  - `AggregateInternal` は各 (layer, source) について `Tick → scratch.Clear → TryWriteValues → weight 加算` を回す。scratch の内容が「遷移補間済み・レイヤー合成前・pre-weight」のソース単位値そのもの（Req 4.1 のベイク対象）
  - `Layer2ActiveExpressionProvider.SetSources` は FacialController がレイヤー割当済みの `ExpressionTriggerInputSourceBase` 群を収集して流し込む。レイヤーに割当てられた state sink は自動的に overlay/suppress の active 解決対象になる
  - gaze は `GazeVector2InputSource`（osc パッケージ内、`IInputSource` + `IAnalogInputSource` 両実装、push 型 `Publish(x, y)`、-1..1、clamp なし）を registry に登録し、`GazeBindingConfigResolver` → `GazeBonePoseProvider` が解決・適用する。timeline パッケージは osc に依存できないため同型の sink を自前実装する
  - GC ゼロ検証は `FacialControllerGcZeroGateTests`（PlayMode / ProfilerRecorder）パターンが確立済み
  - `versionDefines` は既存 asmdef 24 ファイルで使用実績あり
- **Implications**: core 改修は Aggregator への観測フック追加のみに限定できる。それ以外はすべて既存拡張契約の範囲内で実現可能

### 先行 spec `rec-recording-playback` との境界
- **Context**: REC 記録データの物理フォーマットが未確定（先行 spec は requirements フェーズ）
- **Findings**: 論理形は「操作イベント時系列（トリガー on/off + expressionId + アナログ軸値 + gaze(-1..1 Vector2)、秒ベース相対タイムスタンプ）」で確定済み。sidecar 物理フォーマット・読込 API は未確定
- **Implications**: 本 spec は論理形のみに依存する `IRecordedEventSequence` を自パッケージ内に定義し、rec の実フォーマットへの変換を Editor 書き出し境界の adapter 1 ファイルに封じ込める。rec の design 確定時に adapter のみ再照合すればよい

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: mixer がライブ遷移計算を毎回駆動 | 再生中も trigger sink（実値あり）を駆動し値もライブ計算 | ベイク不要、1.5 が厳密に成立 | スクラブ/ジャンプで遷移状態が時刻に対して非決定（要件矛盾）。ランダムアクセス不可 | Req 5.3 を満たせず不採用 |
| B: post-blend 値をベイク | 合成後 BlendShape 値を焼いて直接出力 | 実装単純 | ライブのリップシンク等と共存不可（レイヤー合成を通らない）。Req 1.4 / 4.1 違反 | 要件で明示的に排除済み |
| C: ソース単位ベイク + 状態イベント並行駆動 | 値はベイクカーブ（ValueProvider として合成参加）、active 状態はイベント列から state-only sink へ | スクラブ決定的・レイヤー合成共存・override/suppress 動作 | ベイク鮮度管理が必要（ハッシュ検知で対処）。カーブ近似誤差 | 要件が指定する構成。採用 |

## Design Decisions

### Decision: OQ1 — 遷移時間の所有権はプロファイル read-only 参照
- **Context**: クリップ単位で遷移時間を上書き可能にするか
- **Alternatives Considered**:
  1. プロファイル値の read-only 参照
  2. per-clip 上書き（クリップに遷移時間フィールドを持たせる）
- **Selected Approach**: 1（read-only 参照）
- **Rationale**: 遷移時間・カーブは `StartTransition` が `profile.FindExpressionById` から取得する構造で、per-clip 上書きは core API の変更（Req 9.3 違反）または表情ごとの合成プロファイル生成を要する。制約からの導出であり選好ではない
- **Trade-offs**: クリップ単位の演出調整はできない。表情差し替え（expressionId 変更）とプロファイル側の遷移時間編集で代替
- **Follow-up**: per-clip 上書き要望が出た場合は backlog 化し、core への遷移時間注入 API を別 spec で検討

### Decision: OQ2 — 削除済み expressionId の検証 UX
- **Selected Approach**: (a) 書き出し時: クリップは生成し Warning（差し替え修復を可能にするため除外しない）、(b) ベイク時: core と同一挙動（目標ゼロ + 既定遷移）で焼き per-clip 1 回 Warning、(c) Editor 事前検証 API `FacialTimelineValidator` + ClipEditor でのエラー表示、(d) ランタイム: Warning + 継続
- **Rationale**: core の `TriggerOn` が未知 ID を安全に処理する実挙動（調査済み）に整合させ、追加の防御分岐を作らない
- **Trade-offs**: 無効クリップが「無表情区間」として焼かれる（見た目で気付ける + 検証 API で事前検知可能）

### Decision: OQ3 — 連続値クリップの Keyframe 直接編集は全面許容
- **Selected Approach**: アナログ/gaze クリップの `AnimationCurve` は正本の一部として Inspector / Curve Editor で自由編集可。gaze の -1..1 値域は Validator が範囲外を Warning するが値は変更しない（clamp しない）
- **Rationale**: ライブ経路（`GazeVector2InputSource.Publish`）も clamp しないため、clamp を挟むと「ライブと同一コードパス」原則が崩れる
- **Trade-offs**: 範囲外値がそのまま gaze 解決に流れる（ライブと同等のリスク。Validator で検知可能）

### Decision: OQ4 — REC 原本からの再書き出し運用
- **Selected Approach**: 書き出し先は Editor UI で常に明示指定。既定は新規 TimelineAsset 生成。既存アセット / 既存 Track を対象にした場合は確認ダイアログ必須（無警告上書きなし）。「編集済みか否か」の dirty 判定は行わない（ハッシュはベイク陳腐化専用で編集済み判定に流用しない）
- **Rationale**: Req 3.4 は「無警告での上書きをしない」であり、編集検知までは要求していない。判定機構を持たない方が状態管理が単純
- **Trade-offs**: 未編集アセットへの上書きでもダイアログが出る（安全側）

### Decision: OQ5 — パッケージ配置は新規 `com.hidano.facialcontrol.timeline`
- **Context**: Req 9.4（Timeline 依存を導入者のみに課す）の実現形態
- **Alternatives Considered**:
  1. 新規パッケージ `com.hidano.facialcontrol.timeline`（core + rec + com.unity.timeline に依存）
  2. `com.hidano.facialcontrol.rec` 内に asmdef を分割し `defineConstraints` + `versionDefines`（`com.unity.timeline` 存在時のみコンパイル）
- **Selected Approach**: 1（新規パッケージ）
- **Rationale**: steering「配布単位の独立性」（コア/OSC/InputSystem/lipsync/ifacialmocap の既存分割パターンと同型）に整合し、package.json で `com.unity.timeline: 1.8.9` のハード依存を宣言でき UPM が自動解決する。案 2 は単一パッケージで済むが、optional 依存を package.json で表現できず利用者が Timeline を手動導入する必要があり、rec パッケージの Samples / ドキュメントに Timeline 前提が混入する
- **Trade-offs**: パッケージ数が 1 増える（publish 運用コスト増）
- **Follow-up**: rec パッケージの publish 後に version 依存を確定する

### Decision: ベイク忠実度 — 60 Hz サンプリング + 線形キー補間
- **Context**: Req 1.5「同一のブレンド結果を再現」とカーブ近似の関係
- **Selected Approach**: 表情ソース値は 60 Hz 固定サンプリング（決定的キー削減つき）で焼く。線形遷移は厳密一致、イージング/カスタムカーブはサンプル点間の線形補間近似となるため、1.5 の等価性検証テストは許容誤差（epsilon）で比較する。sampleRate はベイク成果物に記録しハッシュに含める
- **Trade-offs**: 曲線遷移で最大サンプル間隔相当の微小誤差（60 Hz で知覚不可レベル）。厳密一致が必要になった場合は接線付きキーまたはレート引き上げで対処可能
- **Follow-up**: PlayMode 等価性テストの epsilon を実測で確定する

### Decision: ベイク欠落時のランタイム挙動
- **Selected Approach**: ベイク成果物が無い場合は値供給なし + 状態イベント駆動のみ + Unity 標準ログ通知（Req 6.4）。ライブ駆動モードへのフォールバックは持たない
- **Rationale**: フォールバックは第 2 の再生モード（スクラブ非対応）を生み、Req 5.2 の「値の供給元はベイクのみ」と矛盾する。Editor では自動再ベイク（6.2）が先に走るため通常発生しない
- **Trade-offs**: ビルド後ランタイムでベイク欠落だと表情値が出ない（ログで原因提示）

### Decision: Timeline 停止時は全解除
- **Selected Approach**: PlayableGraph の停止/破棄時（`OnPlayableDestroy` / graph stop）に state sink の active 表情を全 TriggerOff し、値 sink / gaze sink を invalidate する。解除遷移は core の既定リリース遷移で自然に走る
- **Rationale**: Timeline は「区間演出」であり、rec のリアルタイム再生（停止時状態保持）と役割が異なる。クリップ終端 = TriggerOff の意味論と一貫
- **Trade-offs**: Timeline 停止からライブ操作への状態引き継ぎはしない（引き継ぎたい場合は rec のリアルタイム再生を使う）

## Risks & Mitigations
- **rec spec の設計変更で論理イベント形が変わる** — `IRecordedEventSequence` + adapter 1 ファイルに依存を封じ込め、Revalidation Trigger として明記
- **Aggregator 観測フックの perf 退行** — observer 未登録時は null チェック 1 回/ソース/フレームのみ。既存 GC ゼロゲートテストで担保
- **Edit Mode プレビューの driven-property 復元漏れ**（過去に reflection 注入で実害事例あり） — `GatherProperties` 経由の標準 preview 機構のみを使い、独自の直接書込プレビューを作らない
- **同一フレーム内複数イベントの順序** — レーン分割後もイベント統合はレイヤー親 Track の mixer が一元管理し、記録時刻 + 安定ソートで順序決定性を保証
- **ハッシュの正規化漏れ**（同一内容で不一致 / 異内容で一致） — ハッシュ入力の正規形（フィールド列挙順・浮動小数のビット表現）を design に明文化し、EditMode テストで往復検証

## References
- [Extending Timeline: A practical guide](https://unity.com/blog/engine-platform/extending-timeline-practical-guide) — カスタム Track / mixer / ClipEditor の公式ガイド
- [TrackAsset API](https://docs.unity3d.com/Packages/com.unity.timeline@1.6/api/UnityEngine.Timeline.TrackAsset.html) — CreateTrackMixer / GatherProperties
- [IPropertyPreview API](https://docs.unity3d.com/Packages/com.unity.timeline@1.4/api/UnityEngine.Timeline.IPropertyPreview.html) — Edit Mode プレビューの driven-property 登録
- `.kiro/specs/rec-recording-playback/requirements.md` — 先行 spec の論理イベント形の定義元
