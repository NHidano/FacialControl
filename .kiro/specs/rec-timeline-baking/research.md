# Research & Design Decisions — rec-timeline-baking

## Summary
- **Feature**: `rec-timeline-baking`
- **Discovery Scope**: Complex Integration（既存入力パイプラインへの Timeline 統合 + Editor ベイク基盤）
- **Key Findings**:
  - 系2 active 表情解決（`Layer2ActiveExpressionProvider`）は `ExpressionTriggerInputSourceBase.ActiveExpressionIds` を直接読むため、`blendShapeCount = 0` で構築した派生 sink は「値出力ゼロ・active 状態のみ供給」を**構造的に**実現できる（Req 5.2 / 5.4 の核）
  - `LayerInputSourceAggregator.AggregateInternal` には per-source 値（scratch、pre-weight）の観測点となるコード位置が存在するが、観測フックは未実装。追加は 1 フィールド + null チェック 1 箇所の加算的変更で済む（Req 4.2）
  - Unity Timeline のカスタム Track は `TrackAsset.CreateTrackMixer` + `PlayableBehaviour.ProcessFrame` + `IPropertyPreview.GatherProperties`（Edit Mode プレビュー）が公式パターン。Timeline の PlayableGraph は PlayableDirector 所有であり、FacialController のデッド PlayableGraph 出力経路とは別物
  - gaze 消費側（`GazeBonePoseProvider` の `EyeBinding.Source`）は構築時キャッシュ（readonly）のため `registry.Replace` 単体では差し替わらない（rec spec の gap 分析で実コード確認済み）。ユーザー決定（案 2）で core に「Replace 再バインド伝搬」注入面が rec spec Req 6.4 として追加予定であり、gaze のライブ⇄Timeline 切替はこれを利用する

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

### 空親 Track の mixer コンパイル挙動 spike（2026-07-16, ローカル実機解決版 Timeline 1.8.12）
- **Context**: design の「Timeline はクリップを持たない親 Track を graph にコンパイルしない」という前提と、`EmptyParentTrack` 検証および「レーン 0 = 親 Track 自身」規約の要否を、実機で先に確定したい。spec / package 依存には `1.8.9` が書かれているが、2026-07-16 時点のこの Unity project の実解決版は `manifest.json` / `packages-lock.json` / package cache 実体ともに `com.unity.timeline 1.8.12` だったため、spike は **実機解決版 1.8.12** で実施した
- **Verification Method**:
  - EditMode テスト `TimelineEmptyParentTrackSpikeTests` を追加
  - `TimelineAsset` 上に最小の親子 Track 構成を構築し、`PlayableDirector.RebuildGraph()` + `Evaluate()` で実際の graph を生成
  - 親 Track (`ProbeTrack`) と子レーン Track (`ProbeLaneTrack`) の `CreateTrackMixer` 呼び出しを静的ログで観測
  - 比較用に「親 Track 自身にも 1 クリップあるケース」も同じ方法で観測
- **Findings**:
  - 親 Track が **0 クリップ**、子レーンのみが 1 クリップを持つ構成では、`CreateTrackMixer` は **子レーン Track のみ**で呼ばれ、親 Track の mixer は生成されなかった
  - 親 Track 自身に 1 クリップ追加した比較ケースでは、`CreateTrackMixer` は **親 Track のみ**で呼ばれ、子レーン Track の mixer は生成されなかった
  - したがって、2026-07-16 時点のローカル実機解決版 Timeline 1.8.12 では「空親 Track は parent mixer 不在」「親にクリップがあると parent mixer が責務を持つ」という前提が成立する
- **Re-judgment**:
  - 「レーン 0 = 親 Track 自身」規約は **維持**でよい。親 Track に runtime 上の一元管理責務を持たせる設計では、親が空になるだけで parent mixer ベースの処理が沈黙しうる
  - `EmptyParentTrack` 検証前提も **維持**でよい。人手編集で親 Track を空にしたケースは、Editor 側で明示的に弾く価値がある
  - 比較ケースでは child lane 側 mixer が作られず、親 mixer に責務が集中した。したがって design の「子レーン mixer は no-op / parent mixer が一元管理」という方向性とも整合する
- **Notes**:
  - Timeline package source の `TrackAsset.CanCreateTrackMixer()` コメントには「child track が mixer を要求すると parent が mixer を生成しうる」と読める記述があるが、今回の最小構成の実測では少なくとも空親 Track の `CreateTrackMixer` は呼ばれなかった。設計判断は source コメントではなく実測結果を優先する

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
- **Context**: REC 記録データの物理フォーマットが未確定（先行 spec は requirements フェーズ → gap 分析・design 生成中）
- **Findings**: 論理形は「操作イベント時系列（トリガー on/off + expressionId + アナログ軸値 + gaze(-1..1 Vector2)、秒ベース相対タイムスタンプ）」で確定済み。sidecar 物理フォーマット・読込 API は未確定
- **Implications**: 本 spec は論理形のみに依存する `IRecordedEventSequence` を自パッケージ内に定義し、rec の実フォーマットへの変換を Editor 書き出し境界の adapter 1 ファイルに封じ込める。rec の design 確定時に adapter のみ再照合すればよい（→ 下記「rec design 確定モデルとの契約照合」で実施済み）

### rec design 確定モデルとの契約照合（2026-07-16・レビュー修正）
- **Context**: rec spec の design が確定したため、本 spec の `IRecordedEventSequence` / `RecordedEvent` 契約を rec の実モデルと突き合わせた
- **Sources Consulted**: `.kiro/specs/rec-recording-playback/design.md`（確定版）
- **Findings**:
  - rec の記録イベント実モデルは `RecTimeline` / `RecEvent`。`RecEventKind` は IdDefine / TriggerOn / TriggerOff / AnalogSample / Footer のみで、**gaze 専用 kind は存在しない**（gaze はアナログ 2 軸サンプルに統一）
  - AnalogSample レコードは `u8 axisCount + f32[axisCount]` の**可変軸数（最大 255 軸）**。多軸アナログソースが実在する（ifacialmocap の `AnalogAxesInputSource` 等）
  - id は slug:sub 形式のソース id 文字列で識別
  - 当初契約との不整合 2 件: (1) `RecordedEvent` が `float X / float Y` の 2 軸固定で AxisCount > 2 のイベントが変換で欠落する（timeline 側の `FacialValueClip.Axes` / `TimelineValueChannelConfig.AxisCount` は N 軸対応済みで、間の契約だけがボトルネックだった）、(2) `RecordedEventKind.GazeValue` に対応する kind が rec 側になく、adapter の振り分け規則が未定義だった
- **Implications**: `RecordedEvent` を `float[] Axes`（可変軸）へ変更し、kind を `AnalogValue` に一本化（下記 Decision）。IdDefine / Footer は adapter 内で解決・消費し契約には現さない。アーキテクチャパターン選定への影響なし（データ形のみの変更）

### gaze 消費側の構築時キャッシュと core 注入面（先行 spec 決定・設計後に確定）
- **Context**: 当初設計は「profile の `GazeBindingConfig` に `timeline:gaze-{n}` を静的配線する」方式だったが、rec spec の gap 分析結果と突き合わせた結果、方式の見直しが必要になった
- **Sources Consulted**: rec spec gap 分析（`FacialController.InitializeInternal` のレイヤー入力源 1 回解決 / `GazeBonePoseProvider.EyeBinding.Source` の readonly 実コード確認）、コーディネーター経由のユーザー決定（案 2）
- **Findings**:
  - gaze 消費側は入力ソース参照を構築時にキャッシュするため、`GazeBindingConfig` の静的配線は「ライブ gaze か Timeline gaze のどちらか一方に固定」となり、「ライブ本番中の Timeline 再生」という本 spec の前提と衝突する
  - `registry.Replace` 単体では消費側参照は差し替わらない。ユーザー決定（案 2）により「Replace 時に消費側（レイヤー入力・gaze 解決）へ再バインドを伝搬する注入面」が rec spec Req 6.4 として core に正式追加される予定（記録時点では rec spec design 生成中・契約形状未確定 → 後日 2026-07-16 に確定・照合済み。確定内容は下記 gaze Decision の多重占有ガード参照）
- **Implications**: 本 spec の gaze 供給は「再生セッション中のみ既存ライブ gaze ソースを timeline sink へ Replace（伝搬付き）し、停止時に復元する」一時差し替え方式へ変更。注入面の実装・契約定義は rec spec 所掌で、本 spec は利用側（Revalidation Trigger に契約照合を追加）

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

### Decision: ベイク忠実度 — イベント時刻分割ステップ + 60 Hz 上限刻み（validate-design Issue 1 で改訂）
- **Context**: Req 1.5「同一のブレンド結果を再現」とカーブ近似の関係。当初案「t=0 から 60Hz 固定グリッドで前進しクリップ境界で発火」は、Exporter が記録イベント時刻をリサンプルしない（境界が 60Hz グリッド外に落ちる）ため、トリガー時刻が最大 1/60 秒量子化され線形遷移の折れ点がキーに乗らない（遷移 0.25 秒なら折れ点近傍で最大約 6.7% の値誤差）。「線形遷移は厳密一致」という当初の検証前提が設計時点で成立していなかった
- **Selected Approach**: ハーネスのステップを**イベント時刻で分割**する — 次のクリップ境界時刻まで正確に前進（`Aggregate(境界までの deltaTime)`）→ 境界時刻でサンプル → 発火 → 発火直後を再サンプル → 残り時間を 1/60 秒上限の刻みで前進。全イベント時刻に必ずキーを打ち、キー削減はイベント時刻キーを対象外とする。これにより**線形遷移は区分線形カーブとして厳密に表現され「線形 = 厳密一致」が構造的に真になる**。イージング/カスタムカーブはサンプル点間の線形補間近似のままで、等価性検証テストは許容誤差（epsilon）比較。sampleRate（刻み上限）はベイク成果物に記録しハッシュに含める
- **Trade-offs**: 曲線遷移で最大サンプル間隔相当の微小誤差（60 Hz で知覚不可レベル）。厳密一致が必要になった場合は接線付きキーまたはレート引き上げで対処可能。ステップ制御がわずかに複雑化するが決定性は維持（イベント時刻列 + 固定刻み上限から一意に定まる）
- **Follow-up**: PlayMode 等価性テストの epsilon（カーブ遷移用）を実測で確定する。EditMode に「境界時刻キーの存在」「線形厳密一致」の検証を追加する

### Decision: gaze のライブ⇄Timeline 切替は Replace 再バインド伝搬による一時差し替え（レビュー修正 1）
- **Context**: 当初設計の「`GazeBindingConfig` に `timeline:gaze-{n}` を静的配線」は、gaze 消費側（`EyeBinding.Source`）が構築時固定であるためライブ gaze と Timeline gaze が共存できず（プロファイル設定でどちらか一方に固定）、「ライブ本番中の Timeline 再生」という前提と衝突した
- **Alternatives Considered**:
  1. 静的配線（当初案） — 実装は単純だが構築時固定により共存不可。棄却
  2. **一時差し替え**: 再生セッション開始時に `Replace(既存ライブ gaze ソース id, timeline sink)` を実行し、core 注入面（rec spec が追加する Replace 再バインド伝搬）が消費側を再バインド。停止時に元ソースへ復元
  3. gaze 解決側に複数ソースの優先度合成を新設 — 共存の表現力は最大だが core の gaze 解決コードパスの変更（Req 9.3 違反）であり、本 spec / rec spec のいずれの所掌でもない
- **Selected Approach**: 2（一時差し替え）。差し替えの実行と復元は `FacialTimelineReceiver` が所有し、差し替え対象は binding 設定の `TakeoverSourceId`（`GazeBindingConfig` が参照している既存ライブソース id）で指定する。復元は Receiver.ReleaseAll → Receiver.OnDisable/OnDestroy → TimelineAdapterBinding.Dispose の三重防衛線で保証（すべて冪等）
- **多重占有ガード**（validate-design Issue 2 で追加、2026-07-16 の rec spec 契約確定で用語を最終化）: 同一 `TakeoverSourceId` を複数所有者が差し替える系（同一キャラに複数 PlayableDirector / rec リアルタイム再生との併用 — rec も同じ注入面を使う）では、「A 差し替え → B 差し替え（B の退避元は A の sink）→ A 先行停止で元ソース復元」の順序で B の占有が破壊され、三重防衛線では検出できない。確定した注入面契約に従い (a) **復元時ガード = 参照同一性ガード**: 現占有者が自分の装着した sink と同一参照の場合のみ復元し、不一致は Warning + no-op（三重防衛線すべての冪等条件に含める）、(b) **開始時ガード = `IInjectedInputSource` 占有判定**: 現占有ソースが `IInjectedInputSource`（core Domain のマーカーインターフェース）なら他者占有としてスキップし Warning + 当該チャネル無効化（TakeoverSourceId 解決不能時と同じ縮退）。`TimelineGazeInputSource` は占有検出が機能する前提条件として `IInjectedInputSource` を実装する
- **Rationale**: ユーザーの `GazeBindingConfig` は既存ライブ配線のまま変更不要になり、非再生中はライブ gaze が従来どおり機能する。core への追加は rec spec 所掌の注入面のみで、本 spec は利用側に留まる
- **Trade-offs**: 再生中は当該 gaze チャネルを Timeline が占有する（ライブ gaze と同時合成はしない — 案 3 のスコープ）。多重占有時は後着が縮退する（先着優先）
- **Follow-up**: 注入面の契約形状は 2026-07-16 に rec spec design で確定・照合済み（`IInjectedInputSource` 占有判定 + 参照同一性ガード。原本不在 id への Register/Unregister + null 通知、`ResetToExpressionStack` は rec 専用で timeline は不使用）。契約が再度変わった場合のみ再照合（Revalidation Trigger 更新済み）

### Decision: 状態イベント列は正本クリップ列から graph 構築時に導出（レビュー修正 3）
- **Context**: 当初設計は状態イベント列（`StateEvents`）を `FacialTimelineBakeAsset` に格納していたが、「ベイク欠落時は状態駆動のみ継続」という 6.4 の挙動記述と矛盾していた（イベント列自体がベイク成果物内にあるため欠落時は状態駆動も不能）
- **Alternatives Considered**:
  1. (a) ベイク欠落時は当該 Track を完全無効化（状態駆動もなし）とし記述側を修正 — BakeAsset 中心の単純な構成を維持できるが、ベイク欠落で override/suppress まで全滅し degradation が粗い。また状態イベントが「正本の複製」としてベイクに二重化され、陳腐化時に状態まで古くなる
  2. (b) mixer が graph 構築時（アロケーション許容）に Track のクリップ列から状態イベントを直接導出 — 状態は常に正本と一致（ベイク陳腐化・欠落の影響を受けない）。導出コストは構築時 1 回のみ。BakeAsset は「シミュレーションを要する値カーブ + ハッシュ」に純化される
- **Selected Approach**: 2（クリップ列から導出）。`FacialTimelineBakeAsset` から `StateEvents` を削除し、`TimelineStateEvent` は非シリアライズのランタイムモデルとする
- **Rationale**: 「正本はクリップ列」（Req 3.2）の原則に照らすと、正本から O(クリップ数) で導出できる状態イベントをベイク（派生物）に複製する必然性がなく、複製を持たない方が整合性の破れ口が減る。6.4 の graceful degradation（値欠落でも状態駆動継続）が構造的に成立する
- **Trade-offs**: mixer の graph 構築時処理がやや増える（レーン統合 + 安定ソート。構築時のためGC 制約外）。ベイク欠落時に状態だけ動く状態は「表情が出ないのに override/suppress は効く」という中途半端な見え方になり得る（ログ通知で原因提示）

### Decision: RecordedEvent は可変軸 + kind は AnalogValue に一本化、gaze 振り分けは Exporter の責務（レビュー修正 4）
- **Context**: rec design 確定モデルとの照合で、`RecordedEvent` の 2 軸固定（`float X/Y`）と `GazeValue` kind の 2 点が rec の実モデル（AnalogSample 可変軸・gaze 専用 kind なし）と不整合だった
- **Alternatives Considered**:
  1. **kind 一本化**: `RecordedEventKind` を TriggerOn / TriggerOff / AnalogValue の 3 値にし、`Axes: float[]`（1..255 軸）で全アナログを表現。gaze への割当は Exporter の振り分け規則（profile の `GazeBindingConfig` 参照 id との一致 + 軸数 2 で自動推論、書き出しウィンドウで上書き可、不一致は Warning + Analog フォールバック）
  2. GazeValue kind を契約に残す: adapter が AnalogSample を Gaze/Analog に振り分ける推論規則を持つ
- **Selected Approach**: 1（kind 一本化）
- **Rationale**: 案 2 は adapter に profile 知識（GazeBindingConfig との照合）が必要になり、「rec 実モデルの 1:1 機械変換に限定した薄い adapter」という依存封じ込め方針が崩れる。また同じ振り分け規則が adapter と Exporter UI（ユーザー上書き）の 2 箇所に分裂する。案 1 は契約が rec 実モデルと同形になり、振り分けという編集判断を Exporter（Editor UI を持つ層）1 箇所に集約できる。Timeline 側の gaze 識別は元から Track 種別 / `TimelineValueChannelConfig.IsGaze` が担っており、契約に gaze kind がなくても表現力は失われない
- **Trade-offs**: `IRecordedEventSequence` 単体からは gaze チャネルを識別できない（利用側が profile 文脈を持つ必要がある — 現状の利用側は Exporter のみで、Exporter は profile を必ず受け取るため実害なし）。`float[] Axes` はイベント毎のヒープ確保を伴うが、Editor 書き出し専用の Batch 契約のため許容（Req 8.3 と同枠）
- **Follow-up**: EditMode テストに「多軸（AxisCount > 2）アナログの欠落なし変換」「gaze 自動推論の決定性」を追加する

### Decision: rec 依存の宣言形 — package.json 必須依存 + asmdef 参照は Editor のみ（レビュー修正 2）
- **Context**: Runtime asmdef のコメントに rec 参照が残っており、「rec 依存は Editor 書き出し時のみ」という Boundary 記述と矛盾していた
- **Alternatives Considered**:
  1. package.json で rec を必須依存として宣言し、asmdef 参照は Editor asmdef のみに限定
  2. rec を package.json から外し `versionDefines` で rec 存在時のみ Exporter をコンパイル（optional 化）
- **Selected Approach**: 1。UPM には optional 依存の表現がなく、案 2 は rec 未導入時に主要ユースケース（REC 書き出し、Req 9.5）が無言で消える。本パッケージは rec の後続 spec であり rec 前提は自然。ランタイム再生コードが rec の型に触れないことは Runtime asmdef の参照リストで物理的に強制する
- **Trade-offs**: Timeline 編集・再生だけを使いたい利用者にも rec の導入を要求する（rec は core のみに依存する軽量パッケージのため許容）

### Decision: ベイク欠落時のランタイム挙動
- **Selected Approach**: ベイク成果物が無い場合は値供給なし（表情ソース値・連続値とも）+ 状態駆動（クリップ列由来イベント）は継続 + Unity 標準ログ通知（Req 6.4）。ライブ駆動モードへのフォールバックは持たない
- **Rationale**: フォールバックは第 2 の再生モード（スクラブ非対応）を生み、Req 5.2 の「値の供給元はベイクのみ」と矛盾する。状態イベントは正本クリップ列から導出するため欠落の影響を受けない（上記 Decision 参照）。Editor では自動再ベイク（6.2）が先に走るため通常発生しない
- **Trade-offs**: ビルド後ランタイムでベイク欠落だと表情値が出ない（ログで原因提示）

### Decision: Timeline 停止時は全解除
- **Selected Approach**: PlayableGraph の停止/破棄時（`OnPlayableDestroy` / graph stop）に state sink の active 表情を全 TriggerOff し、値 sink / gaze sink を invalidate する。解除遷移は core の既定リリース遷移で自然に走る
- **Rationale**: Timeline は「区間演出」であり、rec のリアルタイム再生（停止時状態保持）と役割が異なる。クリップ終端 = TriggerOff の意味論と一貫
- **Trade-offs**: Timeline 停止からライブ操作への状態引き継ぎはしない（引き継ぎたい場合は rec のリアルタイム再生を使う）

## Risks & Mitigations
- **rec spec の記録モデルが再度変わる**（2026-07-16 確定版とは照合済み） — `IRecordedEventSequence` + adapter 1 ファイルに依存を封じ込め、Revalidation Trigger として明記
- **core 注入面（Replace 再バインド伝搬）の契約が再度変わる**（2026-07-16 確定版 — `IInjectedInputSource` 占有判定 + 参照同一性ガード — とは照合済み） — gaze 差し替えを Receiver の `BeginPlaybackSession`/`ReleaseAll` に局所化し、注入面は Fake 境界で先行実装。再変更時のみ再照合（Revalidation Trigger 更新済み）
- **gaze 差し替えの復元漏れ・多重占有の破壊**（差し替えたままライブ gaze が死ぬ / 他所有者の占有を上書きする） — 三重防衛線（ReleaseAll / Receiver OnDisable/OnDestroy / Binding Dispose、すべて冪等）+ 開始時/復元時ガード（現占有者照合）+ 各経路・A/B 多重占有シナリオの PlayMode テストで担保
- **空親 Track の mixer 非コンパイルによる無警告沈黙** — 「レーン 0 = 親 Track 自身」規約 + `FacialTimelineValidator` の `EmptyParentTrack` エラー検出。実装初期に Timeline 1.8.9 実機でコンパイル挙動を spike 確認（挙動が想定と異なれば規約を再判断）
- **Aggregator 観測フックの perf 退行** — observer 未登録時は null チェック 1 回/ソース/フレームのみ。既存 GC ゼロゲートテストで担保
- **Edit Mode プレビューの driven-property 復元漏れ**（過去に reflection 注入で実害事例あり） — `GatherProperties` 経由の標準 preview 機構のみを使い、独自の直接書込プレビューを作らない
- **同一フレーム内複数イベントの順序** — レーン分割後もイベント統合はレイヤー親 Track の mixer が一元管理し、記録時刻 + 安定ソートで順序決定性を保証
- **ハッシュの正規化漏れ**（同一内容で不一致 / 異内容で一致） — ハッシュ入力の正規形（フィールド列挙順・浮動小数のビット表現）を design に明文化し、EditMode テストで往復検証

## References
- [Extending Timeline: A practical guide](https://unity.com/blog/engine-platform/extending-timeline-practical-guide) — カスタム Track / mixer / ClipEditor の公式ガイド
- [TrackAsset API](https://docs.unity3d.com/Packages/com.unity.timeline@1.6/api/UnityEngine.Timeline.TrackAsset.html) — CreateTrackMixer / GatherProperties
- [IPropertyPreview API](https://docs.unity3d.com/Packages/com.unity.timeline@1.4/api/UnityEngine.Timeline.IPropertyPreview.html) — Edit Mode プレビューの driven-property 登録
- `.kiro/specs/rec-recording-playback/requirements.md` — 先行 spec の論理イベント形の定義元
- `.kiro/specs/rec-recording-playback/design.md` — 記録イベント実モデル（RecTimeline / RecEvent / AnalogSample 可変軸）と core 注入面（Replace 再バインド伝搬）の定義元（2026-07-16 照合）
