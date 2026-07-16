# Implementation Plan

> 開発方針: TDD 厳守（Red-Green-Refactor）。各タスクはテストを先に書いてから実装する。
> テスト実行は Unity batchmode 同期実行（`timeout: 600000`、Editor を閉じた状態で実行）。

- [ ] 1. Foundation: パッケージ雛形と前提 spike
- [x] 1.1 新規パッケージ com.hidano.facialcontrol.timeline の雛形を構築する
  - package.json（core / rec / com.unity.timeline 1.8.9 の依存宣言。Timeline 依存を本パッケージに局所化し core / rec へ波及させない）、README / CHANGELOG / LICENSE / Documentation~ を既存拡張パッケージ（osc / inputsystem）と同一パターンで作成する
  - Runtime asmdef（参照: core の Domain / Application / Adapters + Unity.Timeline。rec は参照しない）と Editor asmdef（参照: timeline Runtime + rec + Unity.Timeline / TimelineEditor）、Tests（EditMode / PlayMode / Shared）の asmdef を配置する
  - 完了条件: 空実装のままプロジェクトがコンパイルされ、Runtime asmdef から rec の型が物理的に参照不能であること
  - _Requirements: 9.4_

- [x] 1.2 空親 Track の mixer 非コンパイル挙動を Timeline 1.8.9 実機で spike 確認する
  - 最小の TrackAsset 構成で「クリップを持たない親 Track（子レーンのみ）が graph にコンパイルされず mixer が生成されない」ことを実機確認する
  - 挙動が設計前提と異なる場合は「レーン 0 = 親 Track 自身」規約と EmptyParentTrack 検証の要否を再判断し、design へフィードバックする
  - 完了条件: 確認結果が research.md に記録され、レーン規約の前提（採用 / 再判断）が確定していること
  - _Requirements: 3.3_

- [x] 2. core 観測フック: Aggregator のソース単位値観測点を追加する（本 spec 唯一の core 改修）
  - テストファースト: observer 登録時に Aggregate 呼出しごと (layer 昇順, source 昇順) で TryWriteValues 直後の pre-weight 値（isValid=false 時は全ゼロ）が同期到達すること、null 設定で解除できることの EditMode テストを先に書く
  - 観測契約インターフェースと設定 API を core Domain に追加し、AggregateInternal へ null チェック付き通知 1 箇所のみの加算的変更を行う（Editor オフラインベイク専用の想定を XML ドキュメントに明記）
  - 完了条件: observer 未登録で既存 Aggregator テストが全緑（挙動・性能不変の回帰確認）かつ新規フックテストが緑であること
  - _Requirements: 4.2, 9.3_

- [ ] 3. Track / クリップ定義と Domain 純ロジック
- [x] 3.1 (P) 表情 / 連続値の Track・クリップアセットを定義する
  - 表情 Track（binding 対象 = Receiver、対象レイヤー名プロパティ、子レーン Track 管理）と表情クリップ（保持フィールドは expressionId のみ、ClipCaps.None でTimeline 側ブレンド禁止）を定義する。遷移時間・カーブはクリップに持たせない（プロファイル read-only 参照）
  - 連続値 Track（チャネル sub-id + Analog / Gaze 種別）と連続値クリップ（軸ごとの AnimationCurve、gaze は 2 軸・値域 -1..1 のまま、クリップローカル時間、ClipCaps.None）を定義する
  - 完了条件: Timeline ウィンドウで Track 追加・クリップ配置・移動・長さ変更・削除・expressionId 差し替え・Keyframe 直接編集が Unity 標準操作で行えること
  - _Requirements: 1.1, 3.1, 7.1_
  - _Boundary: FacialExpressionTrack/Clip, FacialValueTrack/Clip_

- [x] 3.2 (P) 状態イベント列からの任意時刻状態確定ロジックを実装する
  - テストファースト: 線形前進とジャンプで active 集合・スタック順が全経路一致すること（同時刻イベントの安定順序、LIFO 順序復元、差分は TriggerOff 群 → TriggerOn 群を最終 on 時刻昇順で発火）の EditMode テストを先に書く
  - 状態イベントモデルと再構築サービス（イベント列設定 / 線形前進 / ジャンプ、sink 抽象で Fake 駆動可能）を実装する。走査 index・スクラッチ集合は事前確保し、両操作ともヒープ確保なし。遷移値の計算は行わない
  - 完了条件: ジャンプ後の active 集合が「時刻 t までのイベント順次適用」と集合・スタック順ともに一致するテストが緑であること
  - _Requirements: 5.3_
  - _Boundary: TimelineEventStateReconstructor_

- [x] 3.3 正本の正規形ハッシュ計算を実装する
  - テストファースト: 同一正本 → 同一値 / クリップ移動・Keyframe 編集・プロファイル遷移時間変更・Track 並べ替えのそれぞれで不一致 / Track リネームでは不変（非意味変更）の EditMode テストを先に書く
  - design の正規形列挙順（本パッケージ Track を出現順 → 子レーン順、クリップ時刻・expressionId・全 Keyframe、プロファイル（id 昇順）、sampleRate。Track 名は含めない）で FNV-1a 64bit を計算する
  - 完了条件: 上記テストが全緑で、Editor / Runtime 共用（Runtime asmdef 配置・ビット表現ベースでプラットフォーム非依存）であること
  - _Requirements: 6.1_
  - _Depends: 3.1_

- [ ] 4. 入力パイプライン参加（sink 群・ベイク成果物・Receiver・Binding）
- [x] 4.1 (P) 状態のみ供給する trigger sink を実装する
  - テストファースト: TriggerOn/Off で ActiveExpressionIds の LIFO 意味論（再トリガー位置更新・深度制限）が base のまま維持され、値の書込み寄与が構造的にゼロ（blendShapeCount=0）であることのテストを先に書く
  - core の trigger 入力源基底を blendShapeCount: 0 で構築し、maxStackDepth・ExclusionMode をライブ側設定と一致させる（追加 API なし）
  - 完了条件: レイヤー割当時に Layer2ActiveExpressionProvider の解決対象となり、値出力ゼロを検証するテストが緑であること
  - _Requirements: 5.2, 5.4_
  - _Boundary: TimelineExpressionStateSink_

- [x] 4.2 (P) ベイク値・アナログ・gaze の ValueProvider sink 群を実装する
  - テストファースト: 有効時のみ値供給・Invalidate 中は他入力源の寄与を妨げないこと、書込み経路がヒープ確保なしであることのテストを先に書く
  - ベイク値 sink: 事前確保バッファ + 有効フラグ、書込み / 無効化 API、ContributeMask をベイク対象 BlendShape 名から構築（触らない BlendShape へ干渉しない）
  - アナログ sink（可変 N 軸）と gaze sink（Publish(x, y)、値域 -1..1、clamp なし、BlendShapeCount=0）を実装。Timeline 非再生時は invalid。gaze sink への IInjectedInputSource マーカー付与は core 注入面確定後の結合タスク（8.2）で行う
  - 完了条件: 有効 / 無効の切替と値供給のテストが全緑であること
  - _Requirements: 1.4, 5.5, 7.1_
  - _Boundary: TimelineBakedValueSink, TimelineGazeInputSource, TimelineAnalogInputSource_

- [x] 4.3 (P) ベイク成果物アセットのスキーマを定義する
  - ハッシュ hex・sampleRate・表情ソースベイク（レイヤー別・BlendShape 名キーの sparse カーブ。リグ非依存・2 バイト文字対応）・連続値ベイク（絶対時間カーブ、gaze は正規化 Vector2 のままボーン回転を持たない）を保持する ScriptableObject を定義する
  - 状態イベント列は成果物に持たせない（mixer が正本クリップ列から導出する設計を崩さない）
  - 完了条件: TimelineAsset の sub-asset として保存・参照でき、シリアライズ往復で内容が保たれること
  - _Requirements: 4.1, 6.1, 7.3_
  - _Boundary: FacialTimelineBakeAsset_

- [x] 4.4 Receiver（mixer と sink の唯一の橋渡し + ベイク検査）を実装する
  - テストファースト: ベイク欠落 → Warning + 値供給なし・状態駆動継続 / ハッシュ不一致 → Warning + 再生継続 / Track の対象レイヤー名不在 → Warning + 当該 Track のみ無効化、の縮退テストを先に書く
  - sink 解決 API（レイヤー名 / sub → sink。辞書は初期化時構築・以後参照のみ）、再生セッション開始処理（ベイク有無確認 + ハッシュ照合。照合結果は Editor 側から読める形で公開）、全解除処理（state sink 全 TriggerOff + value / gaze sink invalidate）を冪等に実装する
  - ベイクカーブの BlendShape 名 → sink バッファ index 解決は初期化時 1 回のみ（以後 GC ゼロ）
  - gaze ソース差し替え（装着・占有ガード・復元）は core 注入面の Fake を境界に先行実装し、実注入面との結合はタスク 8.2 で行う
  - 完了条件: ベイク欠落 / 不一致 / レイヤー不在の各縮退テストが全緑で、いずれも再生を停止させないこと
  - _Requirements: 1.2, 3.3, 6.3, 6.4, 7.2_
  - _Depends: 4.1, 4.2, 4.3_

- [x] 4.5 AdapterBinding 正道の接続点を実装する
  - テストファースト: OnStart で sink 群 + Receiver が登録され、Timeline 未再生時は他入力へ影響しない（invalid / 空スタック・gaze 差し替え未実施）ことのテストを先に書く
  - AdapterBindingBase 継承 + [FacialAdapterBinding] で、レイヤー名列とチャネル定義（sub / AxisCount / IsGaze / TakeoverSourceId）に従い sink 群を構築し registry へ登録（`timeline:gaze-{n}` / `timeline:{channel}`）、HostGameObject へ Receiver を生成・注入する
  - Dispose は gaze 差し替え復元（最終防衛線・参照同一性ガード付き）→ Receiver 破棄 → sink 解除の順。デッド PlayableGraph 経路（OscReceiverPlayable 等）への依存を持たない
  - 完了条件: core 無改修（観測フック以外）のまま binding 追加だけで sink 群が入力パイプラインに参加するテストが緑であること
  - _Requirements: 1.2, 9.1, 9.2, 9.3_

- [ ] 5. mixer（Timeline 評価 → sink 駆動）
- [x] 5.1 表情 mixer の状態イベント導出と線形 / ジャンプ駆動を実装する
  - graph 構築時に Track（子レーン含む）のクリップ列から状態イベント列（開始 = On / 終了 = Off、時刻昇順・安定ソート）を導出して再構築サービスに設定する（ベイク成果物に依存しない。ベイク欠落時も状態駆動は継続）
  - 時刻は playable.GetTime() のみ使用（新規の絶対時刻源なし）。線形前進は区間イベントの順次発火、後退・1 評価超の前進はジャンプとして差分駆動。値は Receiver 経由でベイクカーブをサンプルし value sink へ書込み、ベイク欠落時は値供給をスキップ
  - graph 停止 / 破棄で Receiver の全解除を呼ぶ。毎評価の定常処理はヒープ確保なし（イベント走査・カーブ Evaluate・差分配列は事前確保）
  - 完了条件: 線形再生とジャンプの双方で state sink の active 集合と value sink の値が期待どおり確定するテストが緑であること
  - _Requirements: 1.2, 5.1, 5.3, 8.1, 8.2_
  - _Depends: 3.2, 4.4_

- [x] 5.2 (P) 連続値 mixer のカーブサンプル駆動を実装する
  - 毎評価、ベイク成果物の絶対時間カーブをサンプルし gaze は Publish / アナログは書込みで sink を駆動する。クリップの存在しない区間とベイク欠落時は invalidate
  - 完了条件: gaze / アナログの sink 値が Timeline 時刻に追従し、空白区間で invalid になるテストが緑であること
  - _Requirements: 5.1, 7.2_
  - _Boundary: FacialValueMixerBehaviour_
  - _Depends: 4.4_

- [x] 5.3 Editor スクラブプレビューを実装する
  - 非 Play 評価では sink を駆動せず、ベイクカーブのサンプル値を SkinnedMeshRenderer の BlendShape weight（+ gaze 解決結果の目ボーン localRotation）へ直接適用する（プレビュー専用経路。gaze のライブソース差し替えは行わない）
  - IPropertyPreview（GatherProperties）で driven-property 登録し、preview 解除時に Timeline が自動復元する。ベイク欠落時はプレビュー不可（Scene 変化なし + Console 通知）
  - 完了条件: Timeline ウィンドウのスクラブ位置に対応する表情が Scene に表示され、preview 解除で元の状態へ戻ること
  - _Requirements: 5.6_

- [ ] 6. Editor ベイク（再シミュレーション + 陳腐化検知）
- [x] 6.1 再シミュレーションハーネスとイベント時刻分割ステップを実装する
  - テストファースト: 60Hz グリッド外のクリップ境界時刻に必ずキーが存在すること、線形遷移のベイクカーブが任意時刻サンプルでライブ遷移計算と厳密一致すること（イベント時刻分割ステップの検証）の EditMode テストを先に書く
  - 表情 Track ごとに trigger source（blendShapeCount = プロファイル内全 Expression の BlendShape 名和集合数）+ registry + Aggregator + 観測フックのオフラインパイプラインを構築する（遷移計算は core の実コードが実行 = ライブと同一コードパス）
  - 「次のクリップ境界まで正確に前進 → 境界サンプル → イベント発火（同時刻は記録順）→ 直後再サンプル → 残り時間を 1/sampleRate 上限の刻みで前進」のステップ制御を実装する。Editor asmdef 配置でヒープ確保許容
  - 完了条件: 線形遷移が区分線形カーブとして厳密表現される忠実度テストが緑であること
  - _Requirements: 1.3, 1.5, 4.1, 4.2, 4.3, 8.3_
  - _Depends: 2, 3.1_

- [ ] 6.2 ベイクサービス（カーブ化・保存・決定性）を実装する
  - テストファースト: 同一クリップ列 + プロファイルから 2 回ベイクして成果物がバイト等価であること（決定性）、キー削減が決定的でイベント時刻キー（遷移の折れ点）を削減しないことのテストを先に書く
  - 観測値の決定的キー削減 + BlendShape 名キーのカーブ化、連続値（gaze / アナログ）はクリップカーブを絶対時間へオフセット正規化して複製（再シミュレーションなし・gaze はボーン回転を焼かない）、正本ハッシュの記録、sub-asset 保存・置換（失敗時は既存ベイクを残す）を実装する
  - 削除済み expressionId のクリップは core の実挙動どおり焼き per-clip 1 回 Warning。本パッケージ Track 0 本は no-op + ログ。陳腐化判定 API（IsStale）を提供する
  - 完了条件: ベイク直後は IsStale が false、正本（クリップ列またはプロファイル）編集後は true になり、成果物が決定的に再現されるテストが全緑であること
  - _Requirements: 3.2, 4.4, 4.5, 6.1, 7.3_
  - _Depends: 3.3, 4.3_

- [ ] 6.3 陳腐化検知と自動再ベイク・修復ダイアログを実装する
  - アセット保存時（TimelineAsset / プロファイル SO の検出）と Play Mode 遷移時に陳腐化検査を行い、不一致なら自動再ベイクを実行する（Timeline ウィンドウ編集中の逐次検査は行わない）
  - Play 中に Receiver が不一致警告を出したセッションの終了（Editor）で自動再ベイクを試行し、成否と失敗理由をダイアログで報告する
  - 完了条件: クリップ列 / プロファイル編集 → 保存で自動再ベイクが走り、警告済みセッション終了で結果ダイアログが表示されること
  - _Requirements: 6.2, 6.5_

- [ ] 7. Editor 書き出し + 検証
- [ ] 7.1 REC → クリップ列変換ロジックを実装する（rec 論理形の Fake で先行）
  - テストファースト: on/off 対 → クリップ / off 欠落 → 記録終端まで / 重なり → 決定的貪欲レーン割当（レーン 0 = 親 Track 自身に最初のレーンが置かれること）/ 未知 expressionId → クリップ生成 + Warning（LogAssert）/ 多軸アナログ（AxisCount > 2）の欠落なし変換 / gaze 自動推論の決定性（GazeBindingConfig 一致 + 2 軸 → Gaze、軸数不一致 → Warning + Analog フォールバック）の EditMode テストを先に書く
  - rec 論理イベント形の契約（IRecordedEventSequence / RecordedEvent。本パッケージ内定義）と Fake 実装を用意し、rec 実 API の確定を待たずに変換ロジックを完成させる
  - AnalogValue イベントは SourceId 単位で連続値 Track / クリップへ変換する（記録イベント時刻をそのまま Keyframe とし、リサンプルしない）
  - 完了条件: 同一入力 → 同一クリップ列（決定的）を含む変換テストが全緑であること
  - _Requirements: 2.1, 2.2, 2.3, 2.5_

- [ ] 7.2 書き出しウィンドウと保存フローを実装する
  - UI Toolkit ウィンドウで REC 記録・対象プロファイル・出力先を明示指定する（既定は新規 TimelineAsset）。SourceId ごとの Analog / Gaze 種別上書き UI を提供する
  - 既存アセット / 既存 Track を指定した場合は確認ダイアログ必須（無警告上書きなし）。REC 読込失敗は Error ログ + 書き出し中止（アセット変更なし）
  - 書き出し後にベイクを自動実行して Receiver へベイク成果物を割当て、TimelineAsset として保存する
  - 完了条件: 書き出した TimelineAsset が Unity 標準 Timeline ウィンドウで編集可能で、ベイク sub-asset が自動生成・割当済みであること
  - _Requirements: 2.4, 3.4_
  - _Depends: 6.2, 7.1_

- [ ] 7.3 (P) 事前検証とクリップ UI 表示を実装する
  - テストファースト: EmptyParentTrack（親 Track 空 + 子レーンのみ = mixer 非コンパイルで無警告沈黙する状態）をエラー検出し正常なレーン構成では検出しないこと、MissingExpressionId / GazeOutOfRange / EmptyClip を検出することの EditMode テストを先に書く
  - 検証レポート（EmptyParentTrack は修復手段を Message で提示。gaze 値域外は Warning のみで値は変更しない）と、ClipEditor による MissingExpressionId クリップのエラー表示、TrackEditor のレーン編集補助を実装する
  - 完了条件: 検証テストが全緑で、無効 expressionId のクリップが Timeline ウィンドウ上でエラー表示されること
  - _Requirements: 2.5, 3.3_
  - _Boundary: FacialTimelineValidator, FacialExpressionClipEditor, FacialExpressionTrackEditor_
  - _Depends: 3.1_

- [ ] 8. 先行 spec 依存の結合（依存ゲート付き）
- [ ] 8.1 (P) rec 実フォーマット読込アダプターを結合する
  - 依存ゲート: rec-recording-playback の読込 API 確定・実装後にのみ着手可能（それまでは 7.1 の Fake で先行済み。契約が変わった場合は Revalidation Trigger に従い再照合）
  - rec 実モデル（RecTimeline / RecEvent、AnalogSample 可変軸。IdDefine / Footer は adapter 内で解決・消費）→ rec 論理形への 1:1 機械変換のみを行う（kind 推論・gaze 判定は行わない）。rec 依存はこの 1 ファイル + Editor asmdef に封じ込める
  - 完了条件: 実 REC 記録から書き出し → Timeline 編集可能な TimelineAsset + ベイク生成まで通しで成功すること
  - _Requirements: 9.5_
  - _Boundary: RecEventSequenceAdapter_
  - _Depends: 7.1_

- [ ] 8.2 (P) gaze ライブソース一時差し替えを core 注入面と結合する
  - 依存ゲート: core 注入面（rec spec 所掌の Replace 再バインド伝搬 + IInjectedInputSource マーカー）の実装後にのみ着手可能（それまでは 4.4 / 4.5 で注入面の Fake を境界に先行実装済み）
  - gaze sink へ IInjectedInputSource マーカーを付与し、再生セッション開始時の装着（TakeoverSourceId 解決 → 元ソース退避 → Replace で消費側 EyeBinding へ再バインド伝搬）、開始時ガード（現占有ソースが IInjectedInputSource なら Warning + 当該チャネル無効化）、復元時ガード（現占有者が自分の装着 sink と同一参照の場合のみ復元。不一致は Warning + no-op）を実結合する
  - 三重の復元保証（全解除 / Receiver の OnDisable・OnDestroy / Binding の Dispose。各段とも冪等・ガード付き）。TakeoverSourceId が解決不能な場合は Warning + 当該チャネルのみ無効化
  - 完了条件: 装着で gaze 消費側が timeline sink へ再バインドされ、停止で元のライブソースへ復元されるテストが緑であること
  - _Requirements: 7.2, 7.4_
  - _Boundary: FacialTimelineReceiver, TimelineGazeInputSource, TimelineAdapterBinding_
  - _Depends: 4.4, 4.5_

- [ ] 9. PlayMode 統合・性能検証
- [ ] 9.1 ライブ等価の統合テストを実装する
  - 同一イベント列を (a) ライブ trigger 駆動、(b) 書き出し → ベイク → Timeline 線形再生の 2 経路で流し、post-blend 出力を比較する（線形遷移は厳密一致、カーブ遷移は epsilon 許容）
  - 完了条件: 同一プロファイル・同一レイヤー設定での等価テストが PlayMode で緑であること
  - _Requirements: 1.3, 1.5_

- [ ] 9.2 (P) スクラブ / ジャンプと override / suppress の統合テストを実装する
  - 任意時刻へのジャンプ直後の値（ベイクサンプル）と active 状態（音素 override / suppress の発火）が線形到達時と一致することを検証する
  - 完了条件: ジャンプ整合テストが PlayMode で緑であること
  - _Requirements: 5.3, 5.4_
  - _Boundary: PlayMode Integration Tests_

- [ ] 9.3 (P) レイヤー共存・停止時解除・ベイク欠落 degradation の統合テストを実装する
  - Timeline 再生中に Fake リップシンク入力を並走させ、既存合成パイプラインで共存することを検証する
  - graph 停止で state sink 全解除 + value sink invalidate となり他入力源へ影響しないこと、ベイク成果物未割当の再生で値供給なし・状態駆動（override / suppress）継続 + ログ通知が出ることを検証する
  - 完了条件: 3 シナリオの PlayMode テストが全緑であること
  - _Requirements: 5.5, 6.4_
  - _Boundary: PlayMode Integration Tests_

- [ ] 9.4 gaze 差し替え経路・復元保証・多重占有ガードの統合テストを実装する
  - ライブ gaze ソース稼働中の Timeline 再生開始で消費側が timeline sink へ切替わり、gaze カーブ → Publish → GazeBonePoseProvider で目ボーンが動き、停止でライブソースへ復元されること。プロファイル差し替えで gaze 再生結果が追従すること
  - 三重防衛線（graph 破棄 / Receiver 破棄 / Binding Dispose）の各経路で差し替えが必ず復元されること。同一 TakeoverSourceId への A 差し替え → B 差し替え → A 先行停止で A の復元が no-op + Warning となり B の占有が保たれること。開始時に他 sink 占有済みなら Warning + チャネル無効化で縮退すること
  - 完了条件: gaze 系 PlayMode テストが全緑であること
  - _Requirements: 7.2, 7.4_
  - _Depends: 8.2_

- [ ] 9.5 (P) GC ゼロ・性能ゲートを検証する
  - Timeline 再生中の定常フレーム GC ゼロと、ジャンプ（キュー点移動）評価フレームの GC ゼロ（再構築スクラッチが事前確保であること）を FacialControllerGcZeroGateTests の ProfilerRecorder パターン踏襲で検証する
  - 観測フック未登録時の Aggregator アロケーション非退行を検証する
  - 完了条件: 性能ゲートテストが PlayMode で緑であること
  - _Requirements: 8.1, 9.3_
  - _Boundary: PlayMode Performance Tests_

- [ ] 10. 最終回帰: 全体テスト実行と統合制約の確認
  - EditMode / PlayMode の全テストを batchmode 同期実行（`timeout: 600000`、Editor を閉じた状態）で実行し全緑を確認する
  - 既知の pre-existing 赤（SampleAssetsAreInSyncTests 4 件 / OSC heartbeat 系 / TenIndependentBindings_OneSwap）は本 spec の FAIL 判定に含めない
  - デッド PlayableGraph 経路への依存がないこと（blendshape-output-refactor による撤去の影響なし）と、core の既存入力パイプライン・遷移計算・レイヤー合成のコードパスが観測フック以外無変更であることを確認する
  - 完了条件: 本 spec 追加テストと既存回帰が pre-existing 赤を除き全緑であること
  - _Requirements: 9.2, 9.3_
