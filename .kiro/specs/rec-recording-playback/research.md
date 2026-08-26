# Research & Design Decisions

## Summary
- **Feature**: `rec-recording-playback`
- **Discovery Scope**: Extension（既存システムへの統合中心。gap 分析 + 実コード検証で完了。新規外部依存なしのため Web 調査は不要と判断）
- **Key Findings**:
  - core の観測面には既製の設計前例がある: `FacialOutputBus`（`HasObservers` ガード / publish 中 Subscribe 遅延適用 / 例外隔離）をそのまま入力観測バスの契約テンプレートにできる
  - `InputSourceRegistry.Replace` は既に `Subscribe` ハンドラを発火する（`ReplaceInternal` → `NotifySubscribers`）。再バインド伝搬は「全宣言 id を常時 Subscribe する」だけで既存機構の上に載る
  - アナログ/gaze は pull 型（`IAnalogInputSource.TryRead*`）であり、push 実装（`Publish`）は拡張パッケージ側に分散。拡張無改修の制約下では「core 側の pull 消費点サンプリング」が唯一成立する観測方式
  - `TriggerOn` に削除済み expressionId を渡すと「安全に無視」ではなく **ターゲット全ゼロの遷移が開始される**（`FindExpressionById` null → release 相当）。再生側での事前フィルタが必須（Req 9.1 の実装上の根拠）

## Research Log

### トリガー観測点の実在確認
- **Context**: Req 6.2 の観測フックをどこに置くか
- **Sources Consulted**: `Runtime/Domain/Services/ExpressionTriggerInputSourceBase.cs`（TriggerOn L167 / TriggerOff L196）
- **Findings**:
  - `TriggerOn` / `TriggerOff` は全トリガー入力（inputsystem / osc / lipsync / ifacialmocap / 手動 API）が通る唯一の共通点
  - `ActiveExpressionIds` が public で読める（初期状態捕捉 Req 1.6 に利用可能。末尾が LIFO top なのでスタック順の再現が可能）
  - `FacialController.TryGetExpressionTriggerSourceById`（L910, public）で id → インスタンス解決が既に可能
- **Implications**: 基底クラスに per-instance observer フック（null 既定・`?.Invoke`）を足すだけで全入力元を網羅。拡張パッケージは無改修

### アナログ/gaze の観測方式（pull 消費点タップ vs 統一 push 面）
- **Context**: Research Needed #3。push 実装（`GazeVector2InputSource.Publish` / `AnalogAxesInputSource.Publish` / inputsystem wrapper）は拡張側に分散し、無改修制約下で core は Publish を観測できない
- **Sources Consulted**: `IAnalogInputSource.cs`、`FacialController.BuildGazeSnapshotSpan`、`AnalogBlendShapeInputSource.cs`、ifacialmocap `IFacialMocapReceiverAdapterBinding.cs`
- **Findings**:
  - パイプラインは毎フレームの pull（`TryRead*`）でのみ値を消費する。OSC の複数回/フレーム受信も `OscDoubleBuffer` 等の最新値スロットで畳まれ、ブレンドに影響するのは「そのフレームに消費された値」だけ
  - `TryRead*` は読み取り専用・冪等・alloc なしで、第三者（core のサンプラー）が呼んでも副作用がない
  - registry の `RegisteredIds` + `TryResolve` + `is IAnalogInputSource` で登録済みアナログソースを全列挙できる
- **Implications**: **フレーム消費粒度の pull サンプリング（変化検出付き）が「ブレンド完全再現」の意味論に厳密に一致する**。Publish 単位の観測より情報が落ちるのではなく、ブレンドが見た値そのものを記録できる。Req 1.2/1.3 の「更新されたとき」はフレーム消費粒度で解釈する（design.md に明記）

### Replace 再バインド伝搬の到達範囲
- **Context**: Research Needed #4。消費側の構築時キャッシュ調査
- **Sources Consulted**: `InputSourceRegistry.cs`、`FacialController.cs`（InitializeInternal L200 / ResolveLayerInputSourcesFromRegistry L572 / SetupGazeBoneProvider L695 / TryBuildGazeSnapshot L518）、`LayerUseCase.BindLateInputSource` L283、`GazeBonePoseProvider.cs`（EyeBinding.Source readonly）
- **Findings**:
  - `Replace` は `NotifySubscribers` を同期発火する（Register も同様）。現状 FacialController は「解決失敗した id」しか Subscribe しない
  - `BindLateInputSource` は同 id 既存時の remove→add スワップを既に実装済み。weight 焼き込み・`_layerHasAdditionalSources` フラグも面倒を見る → レイヤー再バインドはこのメソッドの再利用で成立
  - `GazeBonePoseProvider` は `EyeBinding.Source` を readonly キャッシュ → Replace 時は provider 再構築（`SetupGazeBoneProvider` 再実行）が必要
  - gaze snapshot（OSC 送信）経路は毎フレーム `GazeBindingConfigResolver.TryResolve` で再解決しており無改修で追従する
  - **到達不能な消費者**: 拡張 binding が自前インスタンスを直接参照する内部配線（例: ifacialmocap binding が `_headSource` へ直接 Publish し、拡張内部で構築した consumer に直接渡すケース、inputsystem の `FacialCharacterInputExtension` が構築する `AnalogBonePoseProvider`）。これらは registry を介さないため Replace では原理的に届かない（拡張無改修の制約下ではどの方式でも届かない）
- **Implications**: 再バインド伝搬の正式スコープを「registry 経由で解決される消費者（レイヤー入力源・GazeBonePoseProvider・毎フレーム解決経路）」と定義し、拡張内部の直接参照消費者は既知の制限として design.md に明記する。ブレンド（BlendShape 出力）と gaze ボーンはすべて registry 経由なので Req 3.3 の「ブレンドの完全再現」は満たせる

### 削除済み expressionId の TriggerOn 挙動
- **Context**: Req 9.1 の「安全に扱う」の実装根拠
- **Sources Consulted**: `ExpressionTriggerInputSourceBase.StartTransition`
- **Findings**: 未知 id を push すると `FindExpressionById` が null → `_targetValues` 全ゼロ + 既定 release 遷移が開始される。LastWins では未知 id が top になり **表情がゼロへ落ちる**
- **Implications**: 「FindExpressionById が静かに無視するから安全」ではない。再生側（rec）で発火前にプロファイル照合し、該当イベントをスキップ + 警告する事前フィルタが必須

### ストリーミング書き出しの参照パターン
- **Context**: Req 8.3/8.4 の writer thread 設計
- **Sources Consulted**: ifacialmocap `IFacialMocapReceiverHost.ReceiveLoop`（Thread + IsBackground + lock 手渡し + Stopwatch + throttled error log）、`FileProfileRepository.Save`（同期 WriteAllText のみ = Runtime のファイル書込前例はこれだけ）、`Tests/PlayMode/Performance/FacialControllerGcZeroGateTests.cs`（ProfilerRecorder による GC ゲート）
- **Findings**: 時系列イベントを別スレッドへ手渡すバッファ部品は存在しない（`OscDoubleBuffer` / `LayerInputSourceWeightBuffer` は最新値スロット型）。SPSC キューは新規 Domain 部品として Unity 非依存で書ける
- **Implications**: チャンク連結型 SPSC キュー（固定長セグメント事前確保 + 飽和時のみ新セグメント alloc = Req 8.5 の許容条件に一致）を rec Domain の新規部品とし、EditMode で TDD する

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| 観測: per-instance observer + per-FC バス | トリガー基底に per-instance フック、child scope 単位の観測バス | 10 体同時制御で記録スコープが FacialController 単位に自然に閉じる。static 状態なし（ドメインリロード安全） | ソース差し替え時に再配線が必要（再バインド伝搬とセットで解決） | `FacialOutputBus` と対称の設計 |
| 観測: static event | `ExpressionTriggerInputSourceBase` に static event | 配線不要 | 全キャラ混線。記録スコープ分離に送信元判定が必要。ドメインリロード残留リスク | 不採用 |
| 注入: Replace + 再バインド伝搬 | registry Replace を全宣言 id 購読で消費側へ伝搬 | 既存 `Subscribe` / `BindLateInputSource` の再利用で core 改修が小さい。後続 spec も再利用可 | 拡張内部の直接参照消費者には届かない（既知の制限） | ユーザー確定済み（案2） |
| 注入: 全消費点のハンドル間接化 | 消費者が具象参照でなくハンドルを保持 | 全消費者に届く | core 全域の侵襲的改修。既存挙動不変の制約（6.1）に抵触リスク大 | 不採用 |

## Design Decisions

### Decision: sidecar フォーマット = 追記型バイナリコンテナ（単一 `.fcrec`、id インライン定義レコード方式）
- **Context**: Open Question 1。Req 5.6（順次追記必須）+ Req 8（writer 側含む GC ゼロ）+ JsonUtility の巨大配列制約
- **Alternatives Considered**:
  1. JSONL（1 行 1 イベント）— 可読・追記容易だが、イベント毎の文字列生成が writer thread 側でも alloc を積む。`JsonUtility.ToJson` は per-event の string alloc が不可避
  2. JSON ヘッダ + バイナリ本体 — ヘッダのみ可読になるが 2 セクション管理でファイナライズ・破損復旧が複雑化
  3. 追記型バイナリ（固定 magic/version ヘッダ + 可変長レコード列 + 任意フッタ）
- **Selected Approach**: 3。文字列 id は初出時のみ `IdDefine` レコードとしてインライン定義し、以降は u16 インデックス参照（append-only を維持したまま辞書を持てる）。停止時にフッタ（イベント数・総時間）を書いてファイナライズ。フッタ欠落時（クラッシュ等）は先頭からのスキャンで復旧読込
- **Rationale**: 事前確保 byte バッファへの手書きシリアライズで記録経路の alloc をゼロにでき（8.1/8.3）、追記のみでストリーミング書き出し（5.6）と途中破損耐性を両立する。JsonUtility の制約（5.6）に一切触れない
- **Trade-offs**: 人間可読性を失う（読込・検証 API とログで補う）。バージョニングはヘッダ version u16 で管理（preview 段階は破壊的変更許容の既存方針に整合）
- **Follow-up**: 実装時にレコード書込の endianness（little 固定）と truncated tail の復旧テストを EditMode で先行させる

### Decision: 基準状態レコード + 再生開始時の決定的確立（重畳方式の廃止 / validate-design 反映）
- **Context**: validate-design の Critical Issue。当初案は記録開始時の状態を「t=0 の TriggerOn 群」に畳み込み、再生時にライブ状態へ**重畳**する方式だった。レビューで (a) ライブの残存トリガーとの重畳（MaxStackDepth の自動 drop でスタック順まで変化）、(b) 保持済み表情が 0→1 の新遷移として立ち上がり遷移時間ぶんの収束窓でブレンドが不一致、の 2 欠陥が指摘された。ユーザー判断:「ニュートラル状態は事前に取得または生成しておいて、それを基準に最初のフレームを記録すればよい」
- **Alternatives Considered**:
  1. 既知の制限として文書化（重畳方式を維持）— 不採用（ユーザー決定）
  2. t=0 TriggerOn 畳み込みを維持しつつ再生前に全解除 — 収束窓の問題が残る
  3. 記録 = 「基準状態 + イベント列」構造とし、再生開始時に基準状態を決定的に確立
- **Selected Approach**: 3。`.fcrec` に `BaselineTrigger` / `BaselineAnalog` レコード種別（時刻なし、最初の時刻付きイベントより前に出現する不変条件）を追加し、再生側が「基準確立」と「通常イベント」を構造的に区別できるようにする。再生開始時は core 新 API `ExpressionTriggerInputSourceBase.ResetToExpressionStack`（スタック置換 + 遷移を経ない定常値確定 + observer 非通知）で**全トリガーソース**を基準へリセット（基準の無いソースは空スタック = ライブ残存トリガーの解除）、アナログは注入ソースへ基準値をシードする
- **Rationale**: フレーム 0 から収録時と同一のブレンドが同一構成で無条件に成立し（Req 3.3 / 新設 3.8）、重畳の非決定性と収束窓が構造的に消える
- **Trade-offs**: core 注入面に API が 1 つ増える（案2 の枠内として正式化）。再生開始が「ライブ状態を上書きする」操作になる（意図された仕様。requirements.md に AC 3.8 として追加済み）
- **Follow-up**: ResetToExpressionStack の定常確定（直後 TryWriteValues が最終合成値・Tick 非進行）と LastWins/Blend 両モードでのマスク整合を EditMode で先行検証

### Decision: 観測スコープ = FacialController（child scope）単位の `IFacialInputObservationBus`
- **Context**: Research Needed #2。10 体同時制御時の記録スコープ
- **Alternatives Considered**: static event（グローバル）/ per-instance バス
- **Selected Approach**: `FacialOutputBus` と対称に child LifetimeScope へ登録し、`FacialController` から公開する per-FC バス。トリガーは基底クラスの per-instance フック（`ITriggerEventObserver`、バスが実装）を core が解決済みソースへ配線する
- **Rationale**: 記録対象キャラクターの選択が「どの FacialController の バスを購読するか」に還元される。既存パターンの踏襲で review コストも低い
- **Trade-offs**: `SetProfile` 再初期化で child scope ごとバスが作り直される → rec 側でバスインスタンス同一性を毎フレーム参照比較（GC ゼロ）して再購読する
- **Follow-up**: 再初期化を跨ぐ記録セッションの挙動（継続 + 警告ログ）を統合テストで確認

### Decision: アナログ/gaze 観測 = core 側フレーム消費粒度 pull サンプリング（変化検出付き）
- **Context**: Research Needed #3（上記 Research Log 参照）
- **Selected Approach**: `FacialController.LateUpdate` 冒頭（`UpdateWeights` 前）で、バスに観測者がいる場合のみ registry 登録済み `IAnalogInputSource` を毎フレーム `TryResolve` + `TryReadAxes` し、前回値と異なるソースのみバスへ publish する。毎フレーム `TryResolve` により Replace 直後も自動追従する
- **Rationale**: ブレンドが消費した値そのものを記録でき Req 3.3 と意味論が一致。拡張無改修（6.7）・観測者ゼロ時の無コスト（6.5）を同時に満たす
- **Trade-offs**: フレーム内の中間値（同一フレーム複数 Publish）は畳まれるが、それはライブのブレンドも同じものしか見ていない
- **Follow-up**: 変化検出の比較は完全一致（float ビット比較）とし、epsilon は導入しない（記録漏れ防止優先）

### Decision: Replace 再バインド伝搬 = 全宣言 id の常時 Subscribe + 既存スワップ経路再利用
- **Context**: Research Needed #4（ユーザー確定済み案2 の具体化）
- **Selected Approach**: FacialController が (a) レイヤー入力源の全宣言 id を（解決成否に関わらず）Subscribe し、通知時に `BindLateInputSource`（既存スワップ対応）+ Layer2Provider 再投入 + トリガー観測フック再配線、(b) GazeConfigs 由来の gaze id を Subscribe し、通知時に `SetupGazeBoneProvider` を再実行する
- **Rationale**: 新規機構をほぼ作らず、既存の遅延バインド経路を Replace にも汎化するだけで済む
- **Trade-offs**: Subscribe に Unsubscribe が無いが、registry は child scope 再構築ごとに新インスタンスになるためリークは scope 寿命に閉じる
- **Follow-up**: 発火順（Replace → 同期ハンドラ → 次フレーム Aggregate 反映）の統合テスト
- **改訂（validate-design 反映）**:
  - **再入ガードの実行時化**: 「Subscribe ハンドラ内から registry を変更しない」制約は XML doc のみでは検出不能とのレビュー指摘（`NotifySubscribers` L144-155 はライブ list を index 走査）を受け、`InputSourceRegistry` に notify 中フラグ + `Debug.LogError` + no-op の軽量ガード（数行・alloc なし）を追加する
  - **Unregister 通知の追加（原本不在注入の正式サポート）**: `UnregisterInternal`（L191-206）は従来 NotifySubscribers を発火せず、原本が存在しない id へ注入したソースを消費側から外す経路が無かった。ユーザー判断で「記録時と再生時でシーン構成が異なる利用（別プロジェクト/別バインディング構成への記録持ち込み）は preview スコープ内」と確定したため、原本不在 id への注入は `Register` で装着し、`EndInjection` は `Unregister` で除去する。Unregister は `NotifySubscribers(key, null)` を発火し、null 通知 = 「ソース消滅 → 消費側は未解決時挙動へ回帰」（レイヤー: `UnbindLateInputSource` で除去、gaze: provider 再構築で該当 binding スキップ）を正式契約とする。既存の遅延バインドハンドラ（FacialController L613）は `!= null` ガード済みのため null 通知で誤動作しない

### Decision: 注入面の多重占有規則（rec-timeline-baking レビュー反映）
- **Context**: 後続 spec `rec-timeline-baking` の validate-design で「同一ソース id を複数の所有者が Replace で差し替える系（rec リアルタイム再生と Timeline 再生の併用等）において、A 差し替え → B 差し替え → A 復元、の順序で B の占有が破壊される」が指摘された。注入面の契約オーナーは本 spec
- **Alternatives Considered**:
  1. core に占有管理テーブルを持たせる — core が注入者概念を持つことになり「core は rec を知らない」（6.6）と整合しにくい
  2. マーカー interface（`IInjectedInputSource`）+ 注入者側が遵守する規則の契約化
- **Selected Approach**: 2。注入ソースは core Domain の `IInjectedInputSource`（`ReplacedSource` = 退避原本、新規 Register 時は null）を実装する。規則: (1) 装着時、現エントリが `IInjectedInputSource` なら他者占有とみなし当該 id をスキップ + Warning（処理全体は継続、多重占有スタックは作らない）、(2) 復元時、現エントリが自分の装着インスタンスと参照同一の場合のみ Replace（原本復元）/ Unregister（原本なし装着）を行い、異なれば Warning + no-op
- **Rationale**: A→B→A 系で B の占有が破壊されない（B は装着時スキップ or A の復元が no-op）。core は規則とマーカーのみ提供し状態を持たない
- **Trade-offs**: 併用時に一部 id が再生に乗らない可能性（Warning で可視化）。占有の「強制解除」は提供しない（必要になれば将来 spec で検討）
- **Follow-up**: `rec-timeline-baking` の設計がこの規則へ準拠しているかの相互確認。A→B→A 系の統合テスト

### Decision: writer ファイナライズの所有権（validate-design 反映）
- **Context**: `Join(2000ms)` タイムアウト後の FileStream 所有権が未定義で、Windows のファイルロック残留 →「次の記録が開始できない」実害の懸念が指摘された
- **Selected Approach**: FileStream の所有権を writer thread に固定する。writer thread はループ脱出時に `finally` で必ず close する（フッタ書込の成否と独立）。Join タイムアウト時、メインスレッドは stream に触れずエラーログのみ（遅れて終了する writer が `finally` で close する）。停止系 API（`StopSession` / `Close` / `StopPlayback` / MonoBehaviour の `OnDisable`+`OnDestroy`）はすべて冪等とし、二重呼び出しで警告・例外を出さない。次の記録は常に新パス（連番）で開始するためロック競合しない
- **Rationale**: 所有スレッドの一意化により close の競合・二重 close・ロック残留を構造的に排除する
- **Follow-up**: 人工的に writer を遅延させた Join タイムアウト経路のテスト（close が最終的に行われること）

### Decision: 再生停止 → ライブ引き継ぎの値ジャンプは既存パイプライン挙動に委ねる
- **Context**: Research Needed #6（Req 3.5「常に保持・自動解除なし」確定済みの残論点）
- **Selected Approach**: トリガー: スタックが sink（実インスタンス）に残るため無操作で保持され、次のライブ操作は通常の遷移計算（現在補間値からの新遷移）で滑らかに引き継がれる。アナログ/gaze: 停止時に原本ソースを Replace で復元し、次フレームからライブ値が pull される。値差はジャンプするが、これはライブの OSC/アナログ入力自体が平滑化を持たない既存仕様と同一
- **Rationale**: 既存コードパスの挙動不変（6.1）に忠実。rec 側で独自平滑化を挟むと「ライブと同一コードパス」（3.2）が崩れる
- **Trade-offs**: アナログ値の急変はユーザー体験としてありうる → README/Documentation~ で明記

### Decision: rec の Runtime asmdef は 3 分割（Domain / Application / Adapters）
- **Context**: Research Needed #7。osc は単一 asmdef 前例、core は 3 分割
- **Selected Approach**: `Hidano.FacialControl.Rec.Domain` / `.Rec.Application` / `.Rec.Adapters` の 3 asmdef + Editor + Tests
- **Rationale**: Req 7.2 が「クリーンアーキテクチャの依存方向（asmdef 強制）」と字義どおり要求している。rec は SPSC キュー・バイナリフォーマット・再生スケジューラという実質的な Unity 非依存ロジックを持ち、物理的な依存方向強制と EditMode TDD の恩恵が大きい
- **Trade-offs**: osc の単一 asmdef 前例からの逸脱（design.md に明記）。asmdef/meta ファイル数の増加

### Decision: 削除済み expressionId の検証 UX（Open Question 2）
- **Selected Approach**: (a) 読込 API が `RecLoadResult`（タイムライン + 欠落 expressionId の distinct リスト）を返し事前検証可能にする（9.2）、(b) 再生開始時に欠落 id を distinct 単位で 1 回ずつ `Debug.LogWarning`（イベント毎ではなくログスパム回避）、(c) 再生中は該当イベントを発火前フィルタでスキップし再生全体は継続する（9.1）
- **Rationale**: TriggerOn へ未知 id を渡すと表情ゼロ落ち遷移が始まるため（Research Log 参照）、スキップは「安全」のための必須動作。カスタム例外は作らない（9.3）

## Risks & Mitigations
- 拡張内部の直接参照消費者（例: inputsystem の AnalogBonePoseProvider 経路）へ Replace 注入が届かない — 到達範囲を design.md に明文化し、ブレンド + gaze ボーン（registry 経由）で Req 3.3 を保証。残りは将来の拡張側対応（backlog 候補）
- 記録セッション中の `SetProfile` 再初期化でバス・ソースが作り直される — rec が参照同一性チェックで再購読し、切替時に警告ログ。完全性は保証しない（既知の制限として記載）
- writer thread の flush 取りこぼし（Play 停止・ドメインリロード） — `OnDisable`/`OnDestroy` で停止 → リング drain → フッタ書込 → `Join(timeout)`。FileStream は writer thread が `finally` で必ず close（所有権固定、ロック残留防止）。バックストップとしてフッタ欠落ファイルのスキャン復旧
- 再生開始の基準確立がライブ状態を上書きする — 意図された仕様（Req 3.8）だが、配信中の誤操作リスクとして README/Documentation~ に明記
- 複数注入者の併用（rec + Timeline 等）で占有スキップが発生しうる — Warning で可視化。占有規則により状態破壊は起きない
- Editor での StreamingAssets 書込直後にファイルが見えない — 記録停止時のみ `#if UNITY_EDITOR` で `AssetDatabase.Refresh()`（毎フレームは呼ばない）
- 再生注入イベントが観測バスへ還流（再生中に記録すると注入イベントも記録される） — 仕様として容認（それも実操作イベントである）。design.md に明記

## References
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/FacialOutputBus.cs` — 観測バスの契約テンプレート（publish 中 Subscribe 遅延適用 / HasObservers / 例外隔離）
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/InputSources/InputSourceRegistry.cs` — Replace が Subscribe を発火する実証
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Application/UseCases/LayerUseCase.cs` L283 — 同 id スワップ対応の後付けバインド
- `FacialControl/Packages/com.hidano.facialcontrol.ifacialmocap/Runtime/Adapters/Hosts/IFacialMocapReceiverHost.cs` — writer thread の参照パターン
- `FacialControl/Packages/com.hidano.facialcontrol/Tests/PlayMode/Performance/FacialControllerGcZeroGateTests.cs` — GC ゼロゲートの ProfilerRecorder パターン
- `.kiro/specs/rec-timeline-baking/` — 後続 spec（本 spec の観測・注入面を再利用予定）
