# Implementation Plan

- [x] 1. rec パッケージの骨格を新設する
  - 新規 UPM パッケージ `com.hidano.facialcontrol.rec`（0.1.0-preview.1）を標準構成（Runtime の Domain / Application / Adapters + Editor + Tests(EditMode / PlayMode / Shared) + Samples~ + Documentation~）で作成する
  - 4 つの asmdef で依存方向（Adapters → Application → Domain、Editor は Editor 専用）を物理的に強制し、依存は core のみ（OSC / InputSystem / lipsync / ifacialmocap への参照を持たない）とする
  - `.meta` の GUID はランダム生成の 32 桁 hex を使用する（連番・ローテーション系列は禁止）
  - 完了条件: 空実装のままプロジェクト全体がコンパイルされ、rec の各 asmdef から逆方向参照・禁止パッケージ参照ができないこと
  - _Requirements: 7.1, 7.2, 7.3_

- [ ] 2. core: トリガー観測フックと基準状態確立 API
- [x] 2.1 トリガー on/off の per-instance 観測フックを追加する
  - 観測契約（ITriggerEventObserver）を core Domain に定義し、トリガー型入力源の on/off がスタック操作成立後に観測者へ通知されるようにする（off は除去成功時のみ通知。既存の「不在 id は静かに無視」と整合）
  - 観測者は 1 インスタンスに高々 1 つ。未設定時は null チェック 1 回のみで alloc・仮想呼び出しゼロ、既存挙動・性能は不変
  - TDD: 通知タイミング・expressionId の受け渡し・未設定時の挙動不変を検証する EditMode テストを先に書いてから（Red）実装で緑にする
  - 完了条件: 観測者登録時に on/off が sourceId + expressionId 付きで通知され、未登録時は既存テストが全て緑のまま
  - _Requirements: 1.1, 6.2, 6.5_

- [x] 2.2 遷移を経ない基準状態確立 API を追加する
  - トリガー入力源の内部スタックを指定列（古い→新しい）で置換し、遷移を経ずに合成結果を定常値として確定する（空列 = 全解除、深度超過は最古から切り詰め）
  - 観測フックへは通知しない（基準確立は操作イベントではない）
  - TDD: 直後の出力が最終合成値であること・遷移が進行しないこと・空列での全解除・observer 非通知・深度切り詰めの EditMode テストを先に書く
  - 完了条件: API 呼出直後のフレームから収束窓なしに定常ブレンド値が得られる
  - 注: 2.1 と同一クラス（ExpressionTriggerInputSourceBase）への変更のため並列不可
  - _Requirements: 3.8_

- [ ] 3. core: 入力観測バスとアナログ/gaze サンプラー
- [x] 3.1 入力観測バスを実装する
  - トリガーイベントとアナログ/gaze サンプルを per-FC で集約し複数観測者へ配信する読取専用契約（パイプラインへ何も書き戻さない）を core Domain に追加する
  - FacialOutputBus と対称の契約を踏襲: HasObservers ガード、publish 中 Subscribe/Unsubscribe の遅延適用、観測者例外の隔離（Debug.LogException で他観測者へ継続配信）
  - axes はコールバック中のみ有効（保持禁止）の規約を XML doc に明記。core は rec を知らない
  - TDD: FacialOutputBusTests と同型の EditMode テスト（遅延適用・例外隔離・HasObservers）を先に書く
  - 完了条件: 観測者ゼロ時に全 publish が早期 return し alloc・列挙なし
  - _Requirements: 1.1, 1.2, 1.3, 2.5, 6.3, 6.5, 6.6_

- [x] 3.2 アナログ/gaze の消費点サンプラーを実装する
  - registry 登録済みの全アナログソースをフレーム消費粒度で pull し、前回消費値と float ビット完全一致しない場合のみバスへ流す（epsilon なし・clamp / 正規化なし、gaze は -1..1 の 2 軸を無変換で通す）
  - 毎フレーム id → 再解決するため Replace 直後の差し替えに自動追従。id リスト再走査は登録数変化時のみ（alloc は再走査時のみ許容）
  - 無効・読取失敗のソースはそのフレームのサンプル対象外（last-valid policy と整合）。バスの観測者ゼロなら全処理をスキップ
  - TDD: 変化検出・無変換通過・2 軸欠落なし・観測者ゼロ時スキップの EditMode テスト（Fake registry）を先に書く
  - 完了条件: 変化のあったソースだけが 1 回ずつ観測され、定常フレームで alloc ゼロ
  - _Requirements: 1.2, 1.3, 4.1, 4.2, 6.3, 6.5, 6.7_

- [ ] 4. core: 注入面（registry 契約強化 + 再バインド伝搬）
- [x] 4.1 (P) registry の通知契約を強化する
  - Unregister 時に購読ハンドラへ null を通知する契約を追加し、通知中の再入（Register/Replace/Unregister/Subscribe）を LogError + no-op とする実行時ガード（notify 中フラグ、数行・alloc なし）を実装する
  - Replace 系 XML doc の文字化けを修繕し、Subscribe 契約（Register/Replace = 新ソース、Unregister = null、通知中再入は契約違反）を明文化する
  - TDD: null 通知・再入ガードの EditMode テストを先に書く
  - 完了条件: Unregister で購読ハンドラへ null が届き、通知中の再入呼び出しが LogError + 無視される
  - _Requirements: 6.4_
  - _Boundary: InputSourceRegistry, IInputSourceRegistry_

- [x] 4.2 (P) 注入ソースのマーカー契約と占有規則を定義する
  - 注入で装着された代替入力ソースのマーカー契約（IInjectedInputSource、退避原本の保持）を core Domain に追加する
  - 多重注入の占有規則（他者占有 id への装着スキップ + Warning / 参照同一性による復元ガード / 「A 装着→B 装着→A 復元」で B 非破壊）を契約として XML doc に文書化する。core は占有の集中管理テーブルを持たない
  - 完了条件: rec および後続 spec（rec-timeline-baking）の注入実装が同一規則を参照できる契約が core に存在する
  - _Requirements: 6.4_
  - _Boundary: IInjectedInputSource_

- [x] 4.3 (P) レイヤーからの遅延アンバインドを追加する
  - Unregister 伝搬時に、指定 id の入力ソースをレイヤー合成から除去し未解決時挙動（初期解決失敗時と同じ状態）へ回帰させる操作を LayerUseCase に追加する
  - TDD: 除去後の合成結果が初期解決失敗時と同一になる EditMode テストを先に書く
  - 完了条件: 指定 id のソースがレイヤー合成から外れる
  - _Requirements: 6.4_
  - _Boundary: LayerUseCase_

- [x] 4.4 FacialController へ観測・注入面を配線し再バインド伝搬を実装する（統合）
  - child scope でのバス登録・取得、解決済み全トリガーソースへの観測フック配線、LateUpdate 冒頭（UpdateWeights 前）でのサンプラー駆動を組み込む
  - 解決成否に関わらず全宣言 id（gaze 解決 id 含む）を Subscribe し、非 null 通知でレイヤースワップ + gaze provider 再構築 + 新ソースがトリガー型ならフック再配線、null 通知でレイヤー除去 + gaze provider 再構築（未解決時挙動へ回帰）を行う
  - gaze snapshot（OSC 送信）経路は無変更（毎フレーム再解決のまま）。既存の遅延バインドハンドラの null 安全を再確認する
  - rec が購読・注入・列挙・参照同一性チェックに使う公開面（観測バス・registry への参照）を公開する
  - PlayMode テストを先に書く: Replace 後 1 フレーム以内にレイヤー出力・gaze ボーンへ新ソース値が反映 / Unregister で合成から外れ未解決時挙動へ回帰 / Replace 後のトリガーイベントがバスへ届く
  - 完了条件: 上記 PlayMode テストが緑で、観測者ゼロ・差し替え未実施時の追加毎フレームコストがない（初期化時の Subscribe 登録のみ）
  - _Requirements: 6.1, 6.3, 6.4, 6.5, 6.7_
  - _Depends: 2.1, 3.1, 3.2, 4.1, 4.3_

- [x] 4.5 core 改修の回帰ゲートを通す
  - 既存 EditMode / PlayMode の全スイートを batchmode 同期実行（timeout 600000）し、観測者ゼロ・注入なし時の既存挙動が不変であることを確認する
  - 既知の pre-existing 赤（SampleAssetsAreInSyncTests 4 件 / OSC heartbeat 系 / TenIndependentBindings_OneSwap）は FAIL 判定に含めない
  - 完了条件: 上記除外を除く全テストが緑（core 改修の受け入れ条件）
  - _Requirements: 6.1, 6.5, 6.7_

- [ ] 5. rec Domain: 記録データモデルとコア部品
- [x] 5.1 (P) 記録データモデルと契約を定義する
  - 操作イベント（種別・相対秒・id 参照・軸値参照）、基準状態（トリガーソース別スタック + アナログソース別値）、タイムライン（基準状態 + 時刻昇順イベント列 + id テーブル + 総時間）、読込結果（タイムライン + 欠落 expressionId リスト）の Domain モデルを定義する
  - 記録用単調クロック契約とその Stopwatch 実装（記録開始起点の相対秒・alloc なし）、イベント書込先契約（writer 抽象）、トリガー/アナログの注入ポート契約を定義する
  - 正本は「基準状態 + 操作イベント」のみ（合成後 BlendShape 値・フレーム番号は保持しない）という不変条件をモデルに反映する
  - 完了条件: 後続の Domain / Application 部品がこのモデル・契約のみを参照して EditMode TDD を開始できる
  - core 改修（タスク 2〜4）とは独立に着手可能
  - _Requirements: 1.4, 1.5, 8.6_
  - _Boundary: RecEvent, RecBaselineState, RecTimeline, RecLoadResult, IRecClock, IRecEventSink, Injection Ports_
  - _Depends: 1_

- [x] 5.2 (P) SPSC チャンクキューを実装する
  - 固定長セグメントを初期 N 個事前確保し、飽和時のみセグメント追加 alloc で欠落なしに継続する単一 producer / 単一 consumer キュー（Unity 非依存）
  - Enqueue は I/O 完了を待たない。セグメント受け渡しは Volatile/Interlocked の軽量同期。飽和拡張回数の診断カウンタを持つ
  - TDD: FIFO 順序・飽和時の無欠落拡張・producer/consumer 別スレッドでの整合を検証する EditMode テストを先に書く
  - 完了条件: 定常時（非飽和）の Enqueue/TryDequeue が alloc ゼロで、イベントが失われない
  - _Requirements: 8.1, 8.3, 8.4, 8.5_
  - _Boundary: RecEventChunkQueue_
  - _Depends: 5.1_

- [x] 5.3 (P) .fcrec バイナリフォーマットを実装する
  - little-endian・追記型・自己記述長レコードのコンテナ: ヘッダ（magic/version/flags/開始時刻）、id 初出時インライン定義、時刻付きイベント、基準状態レコード、フッタ（総時間・イベント数）
  - 基準状態レコードは最初の時刻付きレコードより前に出現しなければならない（違反は読込エラー）。未知 version はエラー。フッタ欠落（クラッシュ）はスキャン復旧し truncated tail を警告付き破棄
  - 事前確保 byte バッファへの手書きシリアライズで writer 側 alloc ゼロ。JsonUtility は使用しない
  - TDD: 全レコード種別（基準レコード含む）の roundtrip・出現順不変条件違反の検出・復旧スキャン・truncated tail 破棄・未知 version エラーの EditMode テストを先に書く
  - 完了条件: 全種別の serialize→deserialize roundtrip とフッタ欠落ファイルの復旧読込が緑（.fcrec 読込 API の確定 = 後続 spec の依存面）
  - _Requirements: 1.4, 1.5, 5.5, 5.6_
  - _Boundary: RecBinaryFormat, RecIdTable_
  - _Depends: 5.1_

- [x] 5.4 (P) 再生スケジューラを実装する
  - deltaTime を double で累積し、タイムスタンプ到達イベントを記録順に visitor へ発火する。1 回の Tick に複数イベントが到達しても順序を維持して全て発火し、終端到達を検知可能にする
  - TDD: 大小さまざまな deltaTime での順序・時刻維持、終端検知、長時間セッションの累積精度を検証する EditMode テストを先に書く
  - 完了条件: 収録時と再生時のフレームレートが異なってもイベントの順序と時刻が維持され、Tick が alloc ゼロ
  - _Requirements: 3.4, 3.6, 8.2, 8.6_
  - _Boundary: RecPlaybackScheduler_
  - _Depends: 5.1_

- [x] 5.5 (P) 記録とプロファイルの整合性検証を実装する
  - 記録が参照する全 expressionId のうち現在のプロファイルに存在しないものを distinct で返す検証機能（例外を投げない・カスタム例外なし）
  - TDD: 欠落検出・空リスト = 整合の EditMode テストを先に書く
  - 完了条件: 読込済み記録から欠落 expressionId を事前検知できる
  - _Requirements: 9.2, 9.3_
  - _Boundary: RecValidation_
  - _Depends: 5.1_

- [ ] 6. rec Application: 記録・再生ユースケース
- [ ] 6.1 (P) 記録セッションのユースケースを実装する
  - 観測イベントを受領しクロックの相対秒を刻んで書込先へ渡す正規化。開始時はクロックのゼロリセット → 基準状態捕捉（トリガースタック順 + アナログ現在値。列挙は Adapters から注入されるスナップショット提供経由）→ 書込先オープン → バス購読の順で行う
  - 二重開始は拒否 + Warning で既存セッション継続。停止中は非購読のため記録されない。停止は冪等（未開始・停止済みは静かに no-op）
  - 記録は観測のみでライブの表情出力へ一切影響しない
  - TDD: Fake クロック / Fake 書込先 / Fake バスで、開始・停止・二重開始拒否・基準捕捉・停止中非記録・冪等停止の EditMode テストを先に書く
  - 完了条件: セッション状態（Idle⇄Recording）が正しく遷移し、基準状態 + イベント列が正しい順序で書込先に渡る
  - _Requirements: 1.1, 1.2, 1.3, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5_
  - _Boundary: RecordingUseCase_
  - _Depends: 3.1, 5.1_

- [ ] 6.2 (P) 再生のユースケースを実装する
  - 読込時に整合性検証を実行し欠落 expressionId を保持（読込失敗時はエラーログ + 再生を開始しない）。再生開始のフェーズ 1 で基準状態を確立（欠落 id は基準スタックからも除外）し、注入開始 + 基準アナログ値適用を指示する
  - Tick でスケジューラを駆動し、欠落 id を参照するトリガーイベントは発火前にスキップ（distinct 単位 1 回の Warning、ログスパム回避）
  - 二重再生は拒否 + Warning。終端到達で Completed 状態 + 完了イベントを 1 回発火。停止は状態保持のまま注入終了を指示するのみ（自動解除なし）で冪等
  - TDD: Fake 注入ポートで基準確立の順序・欠落スキップ・二重再生拒否・終端検知・冪等停止の EditMode テストを先に書く
  - 完了条件: 状態遷移（Idle→Playing→Completed）とイベント発火列が仕様どおりで、Tick が alloc ゼロ
  - _Requirements: 3.1, 3.5, 3.6, 3.7, 3.8, 9.1, 9.2_
  - _Boundary: PlaybackUseCase_
  - _Depends: 5.4, 5.5_

- [ ] 7. rec Adapters: sidecar 永続化
- [ ] 7.1 (P) sidecar パス規約を実装する
  - `StreamingAssets/FacialControl/{assetName}/recordings/` 規約（既存の sidecar 規約定数を再利用）で保存先パスを一元的に組み立てる。recordings サブフォルダで profile.json / ARKit config.json と物理分離する
  - assetName の無効文字置換とディレクトリトラバーサル（`..` 等）の拒否。Editor / ビルド後の両対応（Windows PC 前提）
  - TDD: パス組み立て・無効名・トラバーサル拒否の EditMode テストを先に書く
  - 完了条件: 任意の assetName / recordingName から安全な保存先パスが得られる
  - _Requirements: 5.1, 5.2, 5.7_
  - _Boundary: RecSidecarPath_
  - _Depends: 1_

- [ ] 7.2 (P) 記録ファイルの読込を実装する
  - .fcrec を読み込みタイムラインへ復元する。フッタ欠落ファイルは Warning + スキャン復旧読込
  - ファイル不在・ヘッダ不正・未知 version は Error ログを出し読込失敗として検知可能にする（再生を開始しない材料になる）
  - TDD: 正常読込・復旧読込・不正ファイル失敗の EditMode テストを先に書く
  - 完了条件: 正常ファイルとクラッシュファイルからタイムラインが復元され、解釈できないファイルは失敗として検知できる
  - _Requirements: 5.4, 5.5, 9.3_
  - _Boundary: RecFileReader_
  - _Depends: 5.3_

- [ ] 7.3 (P) writer thread によるストリーミング書き出しを実装する
  - background スレッド + 事前確保バッファ + ファイル追記でキューを順次消費する（既存の受信ループパターンを踏襲、throttled error log 含む）
  - ファイルストリームの所有権は writer thread に固定し、ループ脱出時に finally で必ず close する。ファイナライズは producer 停止 → drain → フッタ書込 → close → Join(timeout) の順。Join タイムアウト時メインスレッドはストリームに触れない（ロック残留防止）
  - 停止 API は冪等（二重呼び出し・未オープンは静かに no-op）。同名ファイルは開始時に連番リネームで回避。Editor では記録停止時のみ AssetDatabase.Refresh
  - I/O 例外は thread 内で捕捉し Error ログ、以後もキュー消費を継続（捕捉側を飽和させない）
  - 完了条件: 記録停止直後に読込可能な .fcrec が生成され、異常終了時もファイルロックが残留しない
  - _Requirements: 5.1, 5.3, 5.7, 8.3, 8.4_
  - _Boundary: RecStreamWriter_
  - _Depends: 5.2, 5.3_

- [ ] 8. rec Adapters: 注入・再生入力ソース
- [ ] 8.1 (P) 再生用アナログ/gaze 入力ソースを実装する
  - アナログ入力ソース契約に準拠し、注入マーカー契約（退避原本の保持）を実装する再生用ソース。再生側から値を設定され、パイプラインから pull で読める
  - 装着時に基準アナログ値でシードする。gaze の 2 軸を -1..1 のまま欠落なく扱う
  - TDD: 値の設定 / 読取・2 軸の欠落なし・マーカー実装の EditMode テストを先に書く
  - 完了条件: パイプラインからライブソースと同様に読める再生ソースが成立する
  - _Requirements: 3.2, 4.3, 4.4_
  - _Boundary: RecPlaybackAnalogSource_
  - _Depends: 4.2_

- [ ] 8.2 (P) トリガー注入を実装する
  - sourceId から原本のトリガーソースを解決して on/off を直接駆動する（差し替えない = ライブと同一コードパス）。解決失敗はイベント単位で distinct 1 回の Warning + スキップ
  - 基準確立では配下の全トリガーソースへ基準スタックを適用する（基準に無いソースは空スタック = ライブ残存トリガーの解除）。遷移を経ない
  - 完了条件: 注入イベントがライブと同一の遷移計算・レイヤー合成に乗り、再生停止時も on のままのトリガーが解除されず保持される
  - _Requirements: 3.1, 3.2, 3.5, 3.8_
  - _Boundary: RecTriggerInjector_
  - _Depends: 2.2_

- [ ] 8.3 アナログ注入（装着・復元）を実装する
  - 記録に登場する各アナログ id へ: 原本あり = 退避して差し替え / 原本なし（別構成への記録持ち込み）= 新規装着 + Info ログ / 他者占有検出 = 当該 id スキップ + Warning（占有規則 1）
  - 注入終了は参照同一性ガード（占有規則 2）で原本復元または除去（原本なし装着時）を行い、現エントリが自分の装着インスタンスでなければ Warning + no-op。冪等
  - 再生イベント到達で再生ソースへ値を反映する
  - 完了条件: 注入開始→再生→終了で自ソースがパイプラインから完全に外れ、再バインド伝搬でライブへシームレスに引き継がれる
  - _Requirements: 3.1, 3.5, 3.8, 4.3, 6.4_
  - _Depends: 4.4, 8.1_

- [ ] 9. 統合: ユーザー向けファサードと Editor UI
- [ ] 9.1 記録・再生の MonoBehaviour ファサードを実装する（統合）
  - FacialController の自動解決（Inspector 未設定時、追加の必須設定なし）と、記録開始/停止・読込・再生開始/停止・状態・完了イベントの公開 API を提供する
  - 毎フレーム、再生中は再生 Tick を駆動し、記録中はバスの参照同一性を比較して SetProfile 再初期化時に再購読 + Warning（切替瞬間の欠落は既知の制限）
  - OnDisable / OnDestroy で記録・再生を安全停止（writer ファイナライズ含む）。全停止経路が冪等で二重呼び出しでも警告・例外を出さない。記録名省略時は開始時刻ベースで自動命名
  - 記録はパス規約 + writer、読込は reader + 整合性検証、再生は注入ポートへの結線として全部品を接続する
  - 完了条件: コンポーネントを 1 つ追加するだけで、Play 中に記録→停止→読込→再生が一通り動作する
  - _Requirements: 2.1, 2.2, 3.6, 5.1, 7.4_
  - _Depends: 6.1, 6.2, 7.1, 7.2, 7.3, 8.2, 8.3_

- [ ] 9.2 記録/再生操作の最小 Inspector を実装する
  - UI Toolkit で Play 中の記録/再生の開始・停止ボタンと状態（記録中 / 再生状態 / 経過秒 / 保存先パス）を表示する
  - 新しい境界は導入しない（ファサードの公開 API を呼ぶだけ）。ランタイム UI ではない
  - 完了条件: コードを書かずに記録・再生の手動確認ができる
  - _Requirements: 2.1, 2.2_

- [ ] 10. PlayMode 統合・性能検証
  - Unity テストランナーは batchmode 同期実行（timeout 600000）。既知の pre-existing 赤（SampleAssetsAreInSyncTests 4 件 / OSC heartbeat 系 / TenIndependentBindings_OneSwap）は FAIL 判定に含めない
- [ ] 10.1 記録→再生のブレンド完全再現テスト
  - トリガー・アナログ・gaze を含む操作列で記録→停止→読込→再生し、ブレンド出力がフレーム 0 から収録時と一致することを検証する（同一プロファイル・同一レイヤー設定で無条件成立。保持中トリガーがある状態からの収録・再生を含む）
  - 基準スタックが収束窓なしに定常値で立ち上がり、記録に無いライブの残存トリガーが再生開始で解除されることを検証する
  - 完了条件: 上記 PlayMode 統合テストが緑
  - _Requirements: 3.2, 3.3, 3.8, 4.3_

- [ ] 10.2 停止時状態保持と注入占有規則のテスト
  - 再生停止で on のままのトリガーが解除されず、ライブの off 操作で通常遷移すること。アナログの原本復元後にライブ値へ戻り、原本不在で装着した id は除去後に未解決時挙動へ回帰すること
  - 他者占有 id への装着スキップ + Warning、「A 装着→B 装着→A 復元」で B の占有が破壊されないこと
  - 完了条件: 上記 PlayMode テストが緑
  - _Requirements: 3.5, 6.4_

- [ ] 10.3 ファイナライズと I/O 耐性のテスト
  - 記録停止直後にファイルが読込可能であること、OnDestroy 経由でも取りこぼしがないこと、二重停止で安全なこと
  - 人工 I/O 遅延（テスト用 slow sink 差し替え）下でメインスレッドの捕捉が停止・欠落しないこと（拡張カウンタで飽和拡張の発生を確認）
  - 完了条件: 上記 PlayMode / Performance テストが緑
  - _Requirements: 5.3, 8.4, 8.5_

- [ ] 10.4 GC ゼロゲート
  - 記録中・再生中の毎フレーム GC アロケーションゼロ（既存 GC ゼロゲートの ProfilerRecorder パターン踏襲）
  - 観測者ゼロ時の core 追加コストゼロ（alloc ゲート + 既存 GC ゲートの継続緑）
  - 完了条件: 3 種の alloc ゲートテストが緑
  - _Requirements: 6.5, 8.1, 8.2, 8.6_

- [ ] 10.5 全体回帰と受け入れ確認
  - EditMode / PlayMode の全スイートを batchmode 同期実行し、pre-existing 赤の除外を除き全緑であることを確認する
  - rec 未使用シーンでの既存挙動不変（観測者未登録時の回帰ゲート）を最終確認する
  - 完了条件: 除外以外の全テストが緑で、core 挙動不変 + rec 全機能の受け入れ条件を満たす
  - _Requirements: 6.1, 6.7_
