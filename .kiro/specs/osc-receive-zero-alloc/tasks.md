# Implementation Plan

> 実装順序は design.md の Migration Strategy（Phase 1〜7）に従う。各タスクは TDD（Red-Green-Refactor）で進め、テストを先に書いてから実装する。受信スレッド上では Unity API を呼ばず、`Debug.Log*` は致命的エラー時のみ許容する（Req 10.7）。

- [ ] 1. 基盤: 受信経路の共通値型・定数・診断カウンタとテストアクセスの整備
  - 型タグの byte 定数と「payload の有無 / 既知か」の判定、解析エラー種別、制御アドレス（sender_id / blendshape_names / preset / gaze）の文字列と UTF-8 バイト列定数を定義する
  - 受信リング設定（スロットサイズ・スロット数・ソケット受信バッファ）の既定値と範囲検証、受信診断カウンタ（received / applied / dropped / oversized / truncated / malformed / stale / heartbeat 到着 / 受信スレッド確保バイト）を `Interlocked` ベースで定義し、「0 → 非 0」遷移を一度だけ検出する仕組みを持たせる
  - アドレス解決結果、分類レコード（値型のみ・48 byte 以下目標）、解決種別フラグ、binding のメインスレッド受け口インターフェースを定義する
  - OSC パッケージのテストアセンブリ（EditMode / PlayMode）から internal 型を参照できるようにする
  - 完了条件: 上記の型が Adapters/OSC 名前空間内でコンパイルでき、設定の範囲外指定で標準例外が出ることと一度きり検出の EditMode テストが緑
  - _Requirements: 1.5, 5.1, 10.1, 10.6_

- [ ] 2. Span ベースの OSC パケットリーダー（Unity 非依存）
- [ ] 2.1 単一 message の走査と引数の逐次読み出し
  - 手組みバイト列で「address / 型タグ / 引数領域を確保なしで切り出せる」失敗テストを先に書く
  - address（終端 NUL を含まない）・型タグ（先頭 `,` を含まない）・引数領域をスライスとして公開し、4 byte アライメント（address / 型タグ / string / blob の末尾パディング）を正しく扱う
  - `f` はビッグエンディアン読み出しで boxing なしに float、`i` は int、`s` / `b` はバイトスライス、`T` / `F` は payload なしとして識別する。先頭引数の float 取得は `f` そのまま・`i` は変換・それ以外は失敗という既存の意味論と一致させる
  - bare（非 bundle）message のタイムスタンプキーは immediate（`0x1`）とする
  - 完了条件: 2 バイト文字 address、`,bs`（blob + string）、`,ss`、`,T` の各パケットで期待どおりのスライスと値が返り、走査中のヒープ確保が `GC.GetAllocatedBytesForCurrentThread` 差分で 0
  - _Requirements: 2.1, 2.3, 2.4, 2.5, 2.6, 2.9, 8.8_

- [ ] 2.2 bundle の走査とタイムスタンプ伝播
  - `#bundle` 識別子 + 8 byte タイムスタンプを読み取り、内包する message / ネスト bundle を深さ優先・到着順に列挙する（ネストは固定スタックで深さ 8 まで）
  - bundle 内の各 message にはその bundle のタイムスタンプを、ネスト bundle には内側のタイムスタンプを付与し、既存の bundle タイムスタンプ判定（0 と 1 は bare / immediate 扱い）と意味論を一致させる
  - 送信側 bundle ビルダーの出力（定常フレーム bundle、1472 byte 分割後の 2 パケット）を入力にした往復テストで、uOSC が返すのと同じ順序・同じ値が得られることを確認する
  - 完了条件: ネスト bundle・分割パケット・空 bundle の EditMode テストが緑で、走査中の確保が 0
  - _Requirements: 2.2, 2.7, 8.8_

- [ ] 2.3 不正パケットのスキップと継続
  - 長さ不足・4 byte 非整列・空 address・`,` で始まらない型タグ・型タグ数と引数長の不一致・未知の型タグ・ネスト深さ超過の各ケースで「例外を投げずに当該要素をスキップし、後続の有効要素を返す」失敗テストを先に書く
  - スキップ件数と最後のエラー種別を診断用に公開し、走査終了後も安定した値を返す
  - 未知の型タグは message 単位でスキップする（uOSC の「後続がずれる」挙動より堅牢化する方向のみの差分であることをテストで明示する）
  - 完了条件: 不正要素と有効要素が混在するパケットで有効要素だけが順序どおり列挙され、スキップ件数が一致する
  - _Requirements: 2.8, 8.8, 10.6_

- [ ] 3. UTF-8 バイト列キーによるアドレス解決テーブルと分類器
- [ ] 3.1 (P) UTF-8 キーの事前生成とテーブル構築規則
  - 送信側アドレスフォーマッタに「受信側プールから同一アドレスの UTF-8 を共有取得する」入口を加算で追加する（既存の UTF-8 生成規則と同一）
  - テーブルビルダー（メインスレッド専用）が runtime mapping・gaze アドレス列・analog listener アドレス列・制御アドレスを入力に、既存 string 実装と同一の解決結果になるようキーを展開する: 完全一致キーを後勝ちで登録 → BlendShape 名から VRChat / ARKit prefix のフォールバックキーを後勝ちで登録（同一バイト列の完全一致キーが存在すれば追加しない）→ gaze / listener / 制御は同一バイト列エントリにフラグを合成
  - テーブルは構築後不変で単調増加の version を持つ
  - 完了条件: 同一アドレスの UTF-8 `byte[]` が重複せず共有され、キー展開規則の各ケース（完全一致優先・後勝ち・prefix 両対応・フラグ合成）の EditMode テストが緑
  - タスク 2 とは境界が重ならないため並行実行可能（パケットリーダーに依存しない）
  - _Requirements: 3.1, 3.2, 3.6, 3.7_
  - _Boundary: OscAddressKeyTable.Builder, OscAddressFormatter_

- [ ] 3.2 確保なしのアドレス照合
  - 「登録済みアドレスのバイトスライスで mapping index が返り、未登録なら確保なしで未マッピングになる」失敗テストを先に書く
  - 長さ + FNV-1a 32bit ハッシュで開番地バケットを引き、候補とバイト列完全一致で照合する。衝突時も確保しない
  - 2 バイト文字・特殊記号を含む BlendShape 名のアドレス、gaze アドレス（VRChat `{pattern}X` / `{pattern}Y`、ARKit eyeLook 系）を Substring なしの byte 比較で解決する
  - 既存の string ベース解決（BlendShape 名抽出 + 辞書引き）とのランダム mapping 集合に対する同値性プロパティテストを含める
  - 完了条件: 照合の EditMode テストが緑で、一致・非一致いずれの照合でも `GC.GetAllocatedBytesForCurrentThread` 差分が 0
  - _Requirements: 3.3, 3.4, 3.5, 3.6, 3.7, 6.3_

- [ ] 3.3 message view から分類レコードへの縮約
  - view とテーブルから「BlendShape / gaze / listener / 制御 / 未マッピング」を判定し、値型レコード（種別・float 有無と値・各 index・タイムスタンプキー・テーブル version・スロット内要素位置）へ縮約する。未マッピングかつ listener なしはレコードを作らない
  - float 有無は `f` / `i` のみ真とし、`,T` などは既知アドレスなら「float なしレコード」として届ける（既存の staleness 更新のみの挙動を維持）
  - sender_id は受信スレッド上で string 化せず値型に解決する: uuid は blob 16 byte（既存の `Guid(byte[])` と同じバイト順）または文字列、startedAt は文字列または int。既存の識別子パース関数と同じ受理集合とし、blob 表現と文字列表現で同一 sender が同一識別子になることを固定バイト列で検証する
  - データグラム全体を「走査 → 分類 → レコード配列へ書き込み」する純関数を用意し、レコード容量超過時は残りを捨てて truncated カウンタを増やす。この純関数が将来 Jobs / Burst へ差し替える境界となる
  - 完了条件: 分類の EditMode テストが緑で、分類中の確保が 0
  - _Depends: 2.3, 3.2_
  - _Requirements: 1.3, 4.2, 10.5_

- [ ] 4. データグラムリングと自前 UDP 受信ループ
- [ ] 4.1 (P) 固定スロットリングの状態機械とドレイン
  - スロット状態（Free / Reserved / Committed）と 3 つの単調増加索引（head / committedTail / reservedTail）に基づく予約・コミット・中止・ドレイン・クリアを実装する。バイト領域・レコード配列・スロットヘッダはコンストラクタで一度だけ確保する
  - 満杯時は最古の Committed スロットを破棄して dropped カウンタを増やし、最新を優先する。Reserved スロットは常に高々 1 つでドレイン対象に含めない。wrap-around 時のコピーは 2 回の memcpy に分ける
  - メインスレッド所有のドレインバッファへ `[head, committedTail)` をロック下でコピーし、直後にスロットを解放する
  - EditMode テスト: 最古破棄で最新が残る、ドレイン後に空、中止後の再予約、wrap-around 境界（スロット数の倍数 ±1）、Reserved 中のドレインが Reserved に触れない、producer / consumer 2 スレッド × 10,000 データグラムで順序保存とドロップ数の整合
  - 完了条件: 上記 EditMode テストが緑で、定常の予約〜ドレインで確保が 0
  - タスク 2・3 とは境界が重ならないため並行実行可能（レコード型はタスク 1 で定義済み）
  - _Requirements: 1.1, 1.2, 1.4, 1.5_
  - _Boundary: OscDatagramRing, OscDrainBuffer_

- [ ] 4.2 リングと解析・分類の接続
  - 予約スロット上でデータグラムを解析・分類しレコード配列へ書く経路と、facade 用に「外部バイト列をコピー → 解析・分類 → コミット」する入口（メインスレッドから呼ばれ、ロックで受信スレッドと共存）を実装する
  - ドレインバッファがレコードの要素位置から message view を再構成して返せるようにする（view は handler 呼び出し中のみ有効という契約）
  - 完了条件: 定常フレーム bundle をコミット → ドレイン → view 再構成で address / 引数が元のバイト列と一致する EditMode テストが緑、確保 0
  - _Depends: 3.3, 4.1_
  - _Requirements: 1.3, 5.1, 10.5_

- [ ] 4.3 UDP 受信スレッドの起動とスロットへの直接受信
  - IPv6 dual-mode + ReuseAddress で bind した UDP ソケットから、background スレッドがブロッキング `Receive`（送信元不要のため `ReceiveFrom` は使わない）で予約スロットへ直接書き込み、解析・分類してコミットする。受信スレッドはソケット・自スレッド所有スロット・不変テーブル・診断カウンタ以外に触れない
  - スレッド生涯フック（開始時 / 停止時 / データグラムコミット時）を `Start` 前に設定できるようにし、ホットパスではフックを呼ばない。受信スレッド確保バイトの記録オプションを持たせる
  - `MessageSize`（スロット超過）と `ConnectionReset`（ICMP 到達不能）は継続し対応カウンタを増やす
  - PlayMode テスト: bind → 実 UDP 送信（MTU 分割 2 パケットを含む）→ ドレインでレコードが到着順に得られる、1 フレーム内の複数データグラム受信、二重 Start が no-op
  - 完了条件: 上記 PlayMode テストが緑で、受信スレッド確保バイトが起動後 0
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 10.7_

- [ ] 4.4 停止・破棄・障害時の安全な終了
  - 停止は「停止要求フラグ → ソケット Close でブロッキング受信を解除 → Join(500 ms)」。Join 成功で Stopped としてリングをクリア、タイムアウト時は Stopping のままリングを再利用・破棄せず、再 Start を標準ログのエラーで拒否する。受信スレッドが最終的に抜けた時点で自らリングをクリアする
  - bind 失敗はエラーログを出してスレッドを起動せず Faulted、停止要求外の受信例外は受信スレッドから例外ログを出して Faulted で終了し、メインスレッドを止めない。Faulted 後の再 Start を許容する
  - PlayMode テスト: Close により 500 ms 以内に Join 成功し以後コールバックなし、二重 Stop、占有済みポートへの直接 Start で Faulted、スロット長 + 1 byte のデータグラムで例外か切り詰めかを確認して oversized 判定方法をテストで固定する
  - 完了条件: 上記 PlayMode テストが緑で、停止後にレコードが適用されない
  - _Requirements: 1.6, 1.8, 10.6, 10.7_

- [ ] 5. OscReceiver / OscReceiverHost の新経路統合と uOSC 互換 facade
- [x] 5.1 受信器による所有・テーブル再構築・ドレイン適用
  - 受信器が受信ループ・リング・ドレインバッファ・テーブルを所有し、初期化・analog listener 登録解除・gaze アドレス設定のたびにテーブルを version +1 で再構築して `Volatile.Write` で公開する。既存の string 辞書による解決は撤去する
  - マッピング再構築はバッファ・accumulator・mapping・テーブルを同一メソッド内で連続して差し替える単一コミット点とし、version 不一致の古いレコードは stale カウンタを増やして破棄する
  - 「ドレイン → 各レコード適用」のポンプをメインスレッド専用で用意する。適用は前段フィルタ（resolved handler が設定されていれば handler、なければ旧 uOSC.Message フィルタ、両方あれば handler 優先で filter は呼ばない）を必ず 1 回だけ実行し、float なしなら終了、mapping 一致でダブルバッファ書込（AtomicSwap 時は bundle 記録）、listener slot 一致で通知（例外は既存どおり握る）。受信時刻はメインスレッドで既存と同じ時刻源から取る
  - ドレイン時に診断カウンタの「0 → 非 0」遷移で一度きり警告（port と件数を含む）を出す。ポート解決は既存のポート自動解決とログ文言を維持する
  - 完了条件: PlayMode で実 UDP 送信 → ポンプ → ダブルバッファに値が入り listener が既存と同じ引数で 1 回呼ばれる。溢れ・不正要素の警告が `LogAssert` で 1 回だけ観測される
  - _Requirements: 1.5, 1.7, 5.5, 5.6, 10.7_

- [x] 5.2 (P) uOSC.Message からワイヤ形式への一方向シリアライザ
  - float / int / string / byte[] / bool を既存 uOSC.Message の受理集合どおり型タグ付きで書き出し、タイムスタンプが bundle 値なら `#bundle` で包む。宛先不足やサポート外型は false を返す。確保なし（固定宛先へ直接エンコード）で internal に留める
  - EditMode テスト: 各型 → bytes → パケットリーダーで同値、bundle タイムスタンプが透過する、bare は immediate になる
  - 完了条件: 往復テストが緑
  - 5.1 とは境界が重ならないため並行実行可能
  - _Depends: 2.3_
  - _Requirements: 5.3, 5.4, 10.2_
  - _Boundary: OscMessageSerializer_

- [ ] 5.3 互換 facade の同一経路化
  - 既存の `HandleOscMessage(uOSC.Message)` を残し、内部でワイヤ化 → リングへ外部コミット → 同期ポンプで適用する。facade 用スクラッチは初回に一度だけ確保する。null / 空 address / 未初期化は既存どおり無視する
  - 前段フィルタは facade 経路でも適用時に 1 回だけ呼ばれ、通常メッセージが二重処理されないことをテストで固定する
  - 完了条件: 既存の facade 直接呼び出し EditMode テスト（analog listener の同期呼び出し、統合テスト群）が変更なしで緑。facade 経由と UDP 経由で同一 message を流した結果（バッファ値・listener 引数・accumulator 状態）が一致する
  - _Depends: 5.1, 5.2_
  - _Requirements: 5.3, 5.4, 5.7, 10.2, 10.4_

- [ ] 5.4 ホストからの uOSC サーバー撤去とライフサイクル配線
  - ホストから uOSC サーバーコンポーネントの取得・破棄を撤去し、`Update` で受信器のポンプを呼ぶ（従来の uOSC `Update` と同じフェーズ）。`Tick`（FlushDue / Swap）は不変。既存の Configure に加えて受信リング設定を受ける overload を追加し、破棄時は受信停止 → 受信器破棄の順にする
  - 完了条件: 既存 PlayMode の送受信・bundle 原子性・マルチエンドポイントテストが新経路で緑、10 体の独立 binding が各自の受信スレッドを独立に停止できる
  - _Requirements: 1.6, 5.7, 7.3, 7.7_

- [ ] 6. binding の struct view 受け口と制御メッセージの byte fast path
- [ ] 6.1 struct view ハンドラと sender / gaze / staleness の分岐
  - binding が resolved handler として自身を受信器に登録し、旧 uOSC.Message フィルタの使用をやめる。gaze route をアドレス列ごとの route set 配列として持ち、route 再構築のたびに受信器へ gaze アドレス列を登録する
  - 分岐順序を既存と同一にする: sender_id（値型から識別・解釈不能なら既存文言で警告）→ ZombieEviction 判定と bundle / bare 単位の sender 判定キャッシュ → 非受理 sender は終了 → 制御メッセージ分岐（6.2 / 6.3 で実装、ここでは受け皿のみ）→ gaze route（float ありなら各 route へ記録、AtomicSwap は gaze bundle）→ gaze または既知 BlendShape なら float の有無に依らず staleness 更新 → true
  - 回帰テスト: `,T` メッセージで既知アドレスの staleness が更新される、sender 切替時の ZombieEviction 判定タイミングが既存と一致する
  - 完了条件: 既存の zombie eviction / gaze E2E / fail-safe テストが facade 経由・UDP 経由の両方で緑
  - _Requirements: 3.6, 4.1, 4.2, 5.1, 5.2, 5.7, 7.2, 7.4, 7.5_

- [x] 6.2 heartbeat のバイト列累積と unchanged ゲート
  - heartbeat チャンクを同一 bundle タイムスタンプで固定 byte scratch（32 KB / 1024 名）に追記し、タイムスタンプが変われば reset する。scratch 超過は切り詰めて一度だけ警告する。heartbeat 到着カウンタを加算する
  - FixedTick での処理は byte ハッシュを前回と比較し、同一かつ処理済みなら確保ゼロで終了。変化時のみ string 化して既存の一貫性チェック → string ハッシュ → mapping マージ → 公開 → 再構築の経路へ渡す（公開ハッシュの算出元は従来どおり string 列）
  - 完了条件: 既存の heartbeat 自動マッピング / 一貫性テストが緑（マッピング数・順序・未対応スキップ・チャンク欠落挙動が一致）、既存の「同一 heartbeat 連続到着で 100 フレーム 0 byte」テストが facade 経由で緑
  - _Requirements: 4.1, 4.3, 4.6, 4.7, 7.1_

- [ ] 6.3 preset / gaze 広告のバイト列比較と string 化境界
  - preset は引数を現在値の UTF-8（固定 256 byte）と比較し、差分があるときのみ string 化して preset 名 / カスタムプレフィックスを更新する
  - gaze 広告は heartbeat と同様に固定 byte scratch（8 KB / 256 ペア）へ累積し、byte ハッシュで gate してから既存の広告解析・route 再構築へ渡し、再構築後に受信器へ gaze アドレス列を再登録する
  - 完了条件: 既存の preset / gaze 広告テストが緑、既存の「gaze 広告が毎 tick 到着しても内容不変なら 0 byte」テストが facade 経由で緑
  - _Requirements: 4.1, 4.4, 4.5, 4.6, 4.7_

- [ ] 7. FacialControl 側の残存確保の排除
- [ ] 7.1 (P) bundle accumulator のフレームリストプール化
  - 「bundle 完了ごとに新規リストを作らず、プールから再利用したインスタンスが再登場する」「返却前にクリアされ前 bundle の値が混入しない」失敗テストを先に書く
  - プール（初期 4 本）から借りて bundle 完了時に使い、フレーム適用後にクリアして返却する。全消去時も全フレームをプールへ戻す。uOSC.Message を受ける既存の記録 API は互換で残す
  - 完了条件: 上記 EditMode テストが緑で、既存の accumulator テストも緑
  - タスク 6 とは境界が重ならないため並行実行可能
  - _Requirements: 6.1, 6.2_
  - _Boundary: OscBundleAccumulator_

- [ ] 7.2 gaze フレームリストのプール化と残存経路の確保確認
  - binding の gaze bundle 用フレームリストを accumulator と同じプール方式にし、混入なしをテストで固定する
  - IndividualMessage モードの書込経路、staleness 判定、FailSafe の処理経路にフレームごとの確保がないことを確認し、見つかった確保源を除去する（判定はタスク 8 の GC ゲートで最終確認）
  - 完了条件: gaze プールの EditMode テストが緑、既存の fail-safe / staleness テストが緑
  - _Depends: 6.1_
  - _Requirements: 6.2, 6.3, 6.4, 6.5_

- [ ] 8. GC アロケーションテストの昇格（実 UDP + 全スレッド）
- [ ] 8.1 計測ワークロード送信器と計測器の自己検証
  - テスト側送信器: 送信側 bundle ビルダーで (a) 定常フレーム（sender_id + VRChat preset アドレスの ARKit 52 本 + gaze X/Y、1472 byte 分割で 2 パケット）と (b) heartbeat フレーム（(a) + blendshape_names チャンク + preset + gaze 広告）を事前生成し、接続済み UDP ソケットの `Send` で確保ゼロで送る。送信側本体コードは変更しない
  - 計測器: M1 = ProfilerRecorder の `GC.Alloc`（`CollectOnlyOnCurrentThread` なし・フレーム合計）、M2 = 受信スレッドの `GetAllocatedBytesForCurrentThread` 窓前後差分、M3 = リフレクションで取れれば全体確保の記録のみ
  - positive control テスト: データグラムコミット時フックで受信スレッドに意図的確保を注入し、M1 が受信スレッドを観測できたかを記録する。M2 も増えない場合は計測器故障として Inconclusive にする
  - 完了条件: positive control テストが「M1 観測可否」をテスト出力に記録して完了し、M2 が注入分を検出する
  - _Depends: 5.4, 6.3, 7.2_
  - _Requirements: 8.1, 8.2, 8.4, 10.3_

- [ ] 8.2 100 フレーム 0 byte ゲートと heartbeat 除外
  - 手順: binding 起動 → ウォームアップ 30 フレーム（heartbeat を含む）→ ヒープ安定化 → 「送信済み == 適用済みデータグラム数」まで待機（最大 1 秒）→ 計測 100 フレーム（`WaitForFixedUpdate` → `null` で 1 フレームを構成し、ドレインと FixedTick が各 1 回走ることをカウンタで確認）。heartbeat は 25 フレームごとに送る
  - 除外判定は送った番号ではなく適用側の heartbeat 到着カウンタの増分で行い、除外フレーム数と番号をテスト出力に記録する
  - ゲートを独立に assert する: G1 = 受信スレッド M2 差分が窓全体で 0（除外なし）、G2 = メインスレッド M1 が heartbeat 除外後の全フレームで 0。失敗メッセージに `frame / gcAllocBytes / heartbeatFrame` を列挙する
  - 既存 baseline 記録テスト 2 件を UDP 経由の 0 byte assert へ置換し、facade 経由の既存シナリオ 3 件はメインスレッド計測のまま維持する。テストは PlayMode Performance 配下に置く
  - 完了条件: G1 / G2 が緑（受信側 PlayMode GC テストが実 UDP 経由で 100 フレーム 0 byte）
  - _Requirements: 4.6, 6.4, 6.5, 8.1, 8.2, 8.3, 8.5, 8.6, 8.7, 9.1_

- [ ] 9. 既存機能回帰と受入確認
- [ ] 9.1 OSC 関連テスト全件と境界制約の確認
  - OSC パッケージの EditMode / PlayMode テストを全件実行し、pre-existing 赤（SampleAssetsAreInSync ×4、PlayMode OSC heartbeat / auto-mapping ×4、TenIndependentBindings_OneSwap フレーキー）を除いて緑であることを確認する
  - 送信側コード・uOSC パッケージ・uOSC 互換 facade が無変更であること、uOSC 型の参照が facade・旧フィルタ・既存記録 API・シリアライザに限定されること、Domain / Application 層に Socket や uOSC 型が入っていないこと、ifacialmocap 等の依存パッケージが無改修でコンパイル・動作することを確認する
  - 完了条件: テスト結果 XML で対象外を除く失敗が 0 件、VRChat 形式アドレスの受信・10 体構成の既存テストが緑
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 9.2, 10.1, 10.2, 10.3, 10.4_

- [ ] 9.2 実機確認と検証記録
  - 検証プロジェクトの OscSend シーンから受信した状態で Profiler（Memory の GC Used Memory、CPU の GC.Alloc 全スレッド）を 60 秒観測し、約 7 秒周期ののこぎり歯が消失していることを確認する（手動観測が必要）
  - Profiler スクリーンショットまたは計測値、受信診断カウンタ値、positive control の M1 観測可否を spec の検証記録（validation.md）に残す
  - 完了条件: 検証記録にのこぎり歯消失の根拠と計測値が記載されている
  - _Requirements: 9.3, 9.4_
