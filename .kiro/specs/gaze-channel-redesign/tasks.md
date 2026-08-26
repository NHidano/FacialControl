# Implementation Plan

> **実行前提条件（ゲート）**: 本 spec の実装は **Spec 1（osc-gaze-auto-mapping）の実装完了後にのみ着手する**。Spec 1 成果のシンボル（`GazeAdvertisementResolver` / `GazeBindingConfigResolver.ComposeSourceId` / `OscReceiverAdapterBinding.Configure` / `GazeInputReader`）がコードベースに存在しない場合は着手せず停止する（タスク 1.1 で確認）。Spec 1 実装が design 記載と異なるシンボル名で完了していた場合は design の Revalidation Triggers に従い追従レビューしてから着手する。
>
> TDD 厳守 (Red-Green-Refactor)。各実装サブタスクは「失敗するテストを先に書く → 最小実装で緑 → リファクタ」の順で進める。
> テスト配置基準: mock/Fake のみ・同期実行は EditMode、MonoBehaviour ライフサイクル・実 UDP・フレーム同期が必要なものは PlayMode (CLAUDE.md「テスト配置基準」準拠)。
> テスト実行: `D:/UnityEditors/6000.3.19f1/Editor/Unity.exe` の batchmode（`-runTests -testPlatform EditMode|PlayMode`、`timeout: 600000` の同期実行、他バージョンでの実行禁止）。実行前に同一プロジェクトを開いた Editor が無いことを確認する。
>
> **pre-existing 赤（本 spec の変更起因 FAIL として扱わない）**:
> - `SampleAssetsAreInSyncTests` 4 件（M-28、MultiSourceBlendDemo サンプル同期ずれ + blink overlay snapshot 欠落）— タスク 7.1 のサンプル更新と交差するが **M-28 は取り込まない**（Req 12.6。3 コピー同期再生成で一部解消しうるが blink snapshot 分は gaze 無関係）
> - `TenIndependentBindings_OneSwap` フレーキー 1 件（PlayMode seed 依存）
> - S-21 系 4 件（OSC heartbeat/auto-mapping）— **Spec 1 完了により緑化済みの想定**。タスク 1.1 のベースラインで赤の場合は Spec 1 の分岐手順（ベースライン採取 → 原因系統分類）に従い切り分けてから着手する

## Foundation: Spec 1 前提ゲートと Domain の id 規約・宣言契約

- [ ] 1. Foundation: 前提確認・id 規約・binding 契約の Domain 新設

- [ ] 1.1 Spec 1 実装完了ゲートとテストベースラインを確認する
  - Spec 1 成果のシンボル（広告解決・id 合成 helper・受信側 GazeConfig 注入 Configure・gaze 読取共通実装）がコードベースに存在することを確認する。欠けている場合は実装着手せず停止する
  - 一切の変更を加える前のベースで EditMode / PlayMode を batchmode 実行し、S-21 系 4 件が Spec 1 完了により緑であることを確認する（赤の場合は Spec 1 の分岐手順で切り分けてから着手）
  - 冒頭一覧の pre-existing 赤（M-28 4 件 / フレーキー 1 件）の現況を記録し、以降のタスクの FAIL 判定除外基準とする
  - 前提シンボルの存在確認とベースライン結果が後続タスクから参照できる形で記録されている (観測可能な完了条件)
  - _Requirements: 12.6_

- [ ] 1.2 gaze source id の合成・パース規約を Domain に新設する
  - 規約定数 `"gaze"`（既定チャネル id）の単一定義、合成 3 形（shared / left / right + sub 部のみ版）、形状分解（例外を送出せず false 返却。「gaze かどうか」の分類は呼び出し側のチャネル id 集合照合とする責務分離を doc 明記）、チャネル id validation（InputSourceId 文字集合 + `:` 禁止 + `.left`/`.right` 終端禁止）を単一の Unity 非依存 static 実装に集約する
  - `.left` / `.right` の文字列定数は本実装内にのみ存在させる（他ファイルへの直書きはレビュー違反とする）
  - Spec 1 が局所化した Adapters 側の合成 helper と side enum を本規約へ統合する方針とし、呼び出し点の置換は後続タスク（4.2 / 5.2）で実施、置換完了（6.4）まで旧 helper は温存する
  - パースはアロケーションなし、既定チャネル左 sub の合成結果が iFacialMocap 現行ハードコードと文字列一致（`"gaze.left"`）
  - EditMode テストで「Compose→TryParse ラウンドトリップ（3 形 × 既定/追加チャネル）」「非準拠入力（空 / `:` 過多 / suffix のみ）の false」「チャネル id validation 境界（`.left` 終端 / `:` 混入 / 64 文字 / 空）」「`"gaze.left"` 互換固定」が緑になる (観測可能な完了条件)
  - _Requirements: 3.1, 3.2, 3.4, 3.5, 3.6, 3.7, 1.3_
  - _Boundary: GazeSourceIdConvention, GazeSide_

- [ ] 1.3 (P) gaze source 宣言契約とチャネル注入契約を Domain に新設する
  - binding が提供可能な gaze source の静的宣言（対象チャネル id + 左右ペア有無。id null/空 = 広告駆動等のワイルドカード）と宣言 interface、チャネル id 列の型付き注入 interface を、既存の宣言 interface 前例と同格の Domain 配置・純 C# で定義する
  - 注入契約（rebuild ごと・OnStart 前・先頭が既定チャネルの不変条件済みリスト・null なし）と、provider 追加のみで将来の procedural gaze ソースが選択肢に載る拡張点であることを XML doc に明記する
  - Domain asmdef 単体でコンパイル可能で、宣言 struct の値保持が EditMode テストで緑になる (観測可能な完了条件)
  - _Requirements: 4.1, 4.8, 8.1_
  - _Boundary: IGazeSourceProvider, IGazeChannelConsumer, GazeSourceDeclaration_

## Core データモデル: Gaze セクションと旧構造の廃止

- [ ] 2. Core データモデル: チャネル定義・SO 置換・legacy 検出

- [ ] 2.1 死んだ BlendShape gaze 資産を参照ゼロ検証のうえ削除する
  - look*Clip ×4 / look*Samples ×4 の 8 フィールドと sample entry 型・Editor の clip sampler について、ランタイム消費者ゼロ（M-29 未配線）を grep で検証してから型・フィールド・関連テストごと削除する（D-2 / research Decision 8）
  - 削除後に Editor asmdef を含む全 asmdef がコンパイル可能で、既存テストが緑のまま維持される (観測可能な完了条件)
  - _Requirements: 9.1_

- [ ] 2.2 チャネル定義型を新設し SO を Gaze セクションへ置換する
  - チャネル定義（id / providerSlug 空 = 自動 / distinct 左右 id / 左右目ボーン path・初期回転・軸 / 可動角 4 値。look* 系は持たない）を Serializable として新設する
  - SO の旧 GazeConfig ルートリストをチャネルリスト 1 本へ置換し、公開アクセサで既定チャネル不変条件を自己修復する（リスト null/空 → 既定チャネル 1 件生成、先頭 id が `"gaze"` 以外 → 矯正。Ordinal 比較・確保最小）。profile interface の gaze アクセサもチャネルリストへ置換する
  - ExpressionSerializable の isGaze フィールドを削除する（Timeline パッケージの isGaze は別概念として無改修）
  - 旧 GazeBindingConfig 型はこの段階では温存し（依存側の置換完了 6.4 まで）、core 内の旧参照は動作変更なしの最小暫定追従でコンパイル可能な状態を維持する
  - EditMode テストで「空リスト自己修復」「先頭 id 改変 YAML の矯正」「isGaze 不在のシリアライズ確認」が緑になる (観測可能な完了条件)
  - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2_

- [ ] 2.3 SO 経路の旧スキーマ検出（legacy フィールド）を実装する
  - 検出専用マーカー型の非公開リストを `FormerlySerializedAs` で旧キーに対応付け、旧 YAML の gaze config 群を初回ロードで件数・id のみ受け取り、検出有無・件数を Inspector / FacialController 向けに内部公開する
  - 再保存で旧キー行がアセットから消えること（新キーの空リスト行が残るのは仕様）を確認する
  - EditMode テストで「旧 `_gazeConfigs` 入り YAML 読込 → 検出 true + 件数/id 取得」「新規アセットで検出 false」が緑になる (観測可能な完了条件)
  - _Requirements: 2.7_

## シリアライズ: 新スキーマ JSON 入出力と旧キー警告

- [ ] 3. JSON: Gaze セクションの parse・変換・出力・旧スキーマ警告

- [ ] 3.1 新スキーマ DTO と parse・旧キー検出警告を実装する
  - ルートキー `gaze`（オブジェクト + channels 配列、フィールドは camelCase、schemaVersion "1.0" 維持）の DTO を追加し、旧 gaze config DTO フィールドと snake/camel preprocessing（Pre/Postprocess）を削除する
  - parse 経路の入口で raw JSON の旧キー（`"gaze_configs"` + 後続 `:` のキー形）を検出した場合、移行ガイド誘導を含む警告を 1 回だけ出し、以降は通常 parse（DTO フィールド不在による自然読み捨て）で gaze 以外を通常どおり読み込む
  - `gaze` キー欠落は警告なしで受理する（既定チャネル補完は Converter 側）
  - EditMode テストで「新スキーマ parse」「旧キー警告 1 回 + gaze 以外の通常読込」「`gaze` キー欠落の無警告受理」「look* 系フィールドがスキーマに存在しない」が緑になる (観測可能な完了条件)
  - _Requirements: 2.3, 2.6, 2.7, 9.3_

- [ ] 3.2 Converter / Exporter の Gaze セクション対応とラウンドトリップを実装する
  - Converter: DTO → チャネルリスト変換時に不変条件を正規化する（既定チャネル欠落は補完 + 警告なしの最小形許容、id validation 違反・重複 id チャネルは警告 + 読み捨て）
  - Exporter: SO の Gaze セクションを新スキーマのみで出力する（旧キー postprocess の削除確認）
  - Exporter 出力 → Converter 読み戻しで Gaze セクションが値等価となるラウンドトリップを固定する
  - EditMode テストで「ラウンドトリップ値等価（既定 + 追加チャネル + 上級設定）」「不正チャネル読み捨て警告」「既定チャネル補完」が緑になる (観測可能な完了条件)
  - _Requirements: 2.4, 2.5, 1.3, 9.3_

## Core ランタイム: チャネル解決・型付き注入・目ボーン接続

- [ ] 4. Core ランタイム: 解決後継・FacialController 置換・ボーン fallback・テスト追従

- [ ] 4.1 チャネル起点の入力源解決（旧 resolver 後継）を新設する
  - 実績ある解決アルゴリズム（distinct → side-pair → shared の 3 段フォールバック、複数 slug 競合は Ordinal 辞書順最小採用 + 警告 1 回）をチャネル入力へ引き継ぎ、id 合成を規約 helper のみに置換する（D-4 / D-9: preferredSlug は導入しない）
  - providerSlug 明示時は当該 slug の合成 id（side-pair → shared）のみ探索し、未解決は false を返す（呼び出し側の不足警告用）
  - 旧 resolver のテスト群をチャネル入力へ移植して緑維持し、providerSlug 制限（該当 slug のみ / 未解決 false）の新テストが緑になる (観測可能な完了条件)
  - _Requirements: 4.4, 4.5, 3.3_
  - _Boundary: GazeChannelResolver_

- [ ] 4.2 FacialController を型付き注入とチャネル起点の gaze 配線へ置換する
  - リフレクション注入一式（Configure メソッド探索・引数読み戻し・PascalCase 変換・gaze config リスト型判定）を削除し、`is` キャストによる型付き注入（チャネル id 列。注入 → child scope build → OnStart の順序維持）へ置換する
  - GazeSnapshot の id フィールドを ExpressionId から ChannelId へリネームし（Breaking。関連 XML doc 追従）、チャネル単位の snapshot 生成へ置換する（バッファ運用維持 = 毎フレーム GC ゼロ）
  - 候補 Subscribe を「binding slug 一覧 × チャネル × 3 形」の規約 helper 全合成へ置換する（distinct チャネルは明示 id を直接 Subscribe、providerSlug 明示チャネルは当該 slug のみ合成）。Spec 1 の Adapters 側合成 helper 呼び出しを規約 helper へ置換する
  - 目ボーン provider の構築（bone path を持つチャネルのみ binding 構築）と provider / binding 型をチャネル追従させる（回転適用・読取委譲は挙動互換で無改修）
  - legacy 検出時の rebuild 警告 1 回（移行ガイド誘導）と、providerSlug 明示チャネルが未解決の場合の不足警告 1 回を追加する（無警告沈黙の禁止）
  - EditMode テストで「Fake binding への注入呼び出し検証」「候補 Subscribe 集合（slug × channel × 3 形 / providerSlug 制限 / distinct 直接）」「legacy 警告 1 回性」「snapshot がチャネル id を運ぶこと」が緑になる (観測可能な完了条件)
  - _Requirements: 8.1, 8.3, 8.4, 4.9, 2.7, 1.4, 3.4_
  - _Depends: 1.3, 2.2, 2.3, 4.1_

- [ ] 4.3 (P) ボーン path 解決の単純名フォールバックを追加する
  - 相対 path 解決失敗時に末尾セグメントの単純名解決へフォールバックし、目線タブの再解決操作へ誘導する警告を 1 回出す（dedupe は既存機構流用）。完全失敗時は既存の警告 + null を維持する
  - フォールバック時の同名ボーン複数ヒットは既存の複数ヒット警告がそのまま機能することを確認する
  - EditMode テストで「path 一致の従来解決」「path 不一致 → 単純名フォールバック + 警告 1 回」「完全失敗の既存挙動維持」が緑になる (観測可能な完了条件)
  - _Requirements: 7.3_
  - _Boundary: BoneTransformResolver_

- [ ] 4.4 旧 GazeConfigs 前提の core テスト群を新データモデルへ書き換える
  - FacialController / 解決 / 目ボーン provider / parser・converter 系の旧 `_gazeConfigs` / GazeBindingConfig 前提テスト（15+ ファイル規模）をチャネル前提へ書き換える（design Risks の独立タスク化指示）
  - 挙動互換部分（3 段解決・bone 適用・GC ゼロ・Subscribe ハンドラの provider 再構築のみ）のテスト意図を変えず、入力データ構築のみ差し替える
  - core パッケージの gaze 関連 EditMode / PlayMode テストが新モデルで全緑になる (観測可能な完了条件)
  - _Requirements: 1.4, 2.2, 4.4_

## 拡張 binding: OSC / InputSystem / iFacialMocap / Timeline・rec の追従

- [ ] 5. 拡張 binding: 宣言・注入契約の実装と id 合成の規約集約

- [ ] 5.1 (P) OSC 送信 binding をチャネル注入へ一本化し旧 id 供給 API を廃止する
  - チャネル注入契約を実装し、広告ペア構築（Spec 1 の広告機構は無変更）と GazeSnapshot 送出フィルタの id 源を注入チャネル id 列に一本化する
  - serialized の明示 gaze id リスト・その公開 API・options JSON の gaze id キー・SO 直読みフォールバックを削除する（Breaking。CHANGELOG / 移行ガイドはタスク 7.3）
  - 未注入の単体使用時はチャネル集合を空として gaze 送出・広告なし + 警告 1 回とする（無警告沈黙の禁止）
  - EditMode テストで「注入チャネル id → 広告ペア構築」「既定構成で広告 id が `"gaze"`」「旧 API・旧 options キーの不在」「未注入警告 1 回」が緑になる (観測可能な完了条件)
  - _Requirements: 11.2, 11.6, 8.4_
  - _Boundary: OscSenderAdapterBinding_
  - _Depends: 1.3, 4.2_

- [ ] 5.2 (P) OSC 受信 binding の注入置換と id 合成の規約集約を実装する
  - Spec 1 の GazeConfig リスト Configure をチャネル注入契約へ置換する。突合警告は「広告 id が注入チャネル id 集合に無い」場合の 1 回警告へ読み替え（既定構成では恒常一致で非発火）、未注入時はスキップする（全広告 id への誤警告防止。送信側の未注入警告とは役割が異なる非対称として実装コメントに明記）
  - gaze source 登録（手動 entry / 広告駆動とも）と広告解決内の id 合成を規約 helper へ置換する（`.left`/`.right` 連結の根絶。広告 accumulate / dirty / rebuild / immutable-swap / staleness の機構は無改修）
  - 宣言契約を実装する（手動 gaze entry ごとの宣言 + 広告駆動のワイルドカード宣言 1 件）
  - テストで「規約 helper 経由でも登録 id が現行互換」「突合警告の読み替え（既定構成で非発火 / 不一致 1 回 / 未注入スキップ）」「宣言列挙」が緑になり、Spec 1 の広告駆動テスト群が緑のまま維持される (観測可能な完了条件)
  - _Requirements: 3.3, 3.4, 4.2, 8.2, 11.3, 11.4_
  - _Boundary: OscReceiverAdapterBinding, GazeAdvertisementResolver_
  - _Depends: 1.2, 1.3, 4.2_

- [ ] 5.3 (P) InputSystem binding の一貫登録と Drawer のチャネル id 列挙化を実装する
  - Configure の末尾 gaze 引数と注入済み config 保持を削除し、チャネル注入契約へ置換する（gaze entry のチャネル id が注入集合に無い場合は警告 1 回）
  - Gaze 分岐の「実体 actionName 登録 + 規約 id エイリアス後付け」の二重登録を、規約 helper 合成 id での直接登録に一貫化する（side suffix の文字列補間を削除）
  - Gaze entry の expressionId をチャネル id 参照として再解釈し（フィールド名維持・Tooltip 更新。serialized 資産の意味変更 = Breaking として 7.3 で移行ガイド記載）、宣言契約を実装、declared inputs へ gaze 規約 id を追加する
  - Drawer: bindingMode=Gaze のとき expression ドロップダウンを Profile のチャネル id 列挙へ切り替える（Expression 名変換をやめ id 直接表示。Drawer テスト追従）
  - EditMode テストで「規約 id の一貫登録（エイリアス不在）」「宣言」「チャネル id 不一致警告 1 回」「Drawer のチャネル id 列挙」が緑になる (観測可能な完了条件)
  - _Requirements: 3.3, 4.2, 4.7, 8.2_
  - _Boundary: InputSystemAdapterBinding, InputSystemAdapterBindingDrawer_
  - _Depends: 1.2, 1.3, 4.2_

- [ ] 5.4 (P) iFacialMocap binding のハードコード吸収と宣言実装を行う
  - 左右 sub のハードコード定数を規約 helper 合成へ置換する（合成結果は現行と同一文字列 = 追加設定なしで既定チャネルに接続される使用感を維持）
  - 宣言契約を実装する（既定チャネル・左右ペアの 1 件宣言）。yaw/pitch 反転等の設定は binding 側に存続させる
  - テストで「登録 id の現行互換（`ifm:gaze.left` / `ifm:gaze.right`）」「宣言内容」が緑になり、既存 iFM テストが緑のまま維持される (観測可能な完了条件)
  - _Requirements: 3.3, 4.2, 4.6_
  - _Boundary: IFacialMocapReceiverAdapterBinding_
  - _Depends: 1.2, 1.3_

- [ ] 5.5 (P) Timeline / rec の gaze 判定を新規約へ追従させる
  - takeover 対象 source id の合成・検証を規約 helper 経由にする（Drawer / validation で非準拠 id を警告。Timeline 独自の isGaze フラグは存続）
  - isGaze チャネル config について宣言契約を実装する（sub をチャネル id として宣言、チャネル id validation 非準拠は宣言から除外 + Editor validation 警告 — 診断連番 id の規約整合）
  - rec 書き出しの gaze 分類を「profile のチャネル id 集合 + distinct 明示値 × 規約パース」ベースへ置換する（規約合成 id を分類できない現行の穴を解消）
  - テストで「規約合成 id / distinct 明示 / 非 gaze sub の 3 系分類」「新規約 gaze チャネルを含む rec 記録の正分類・書き出し」「takeover 検証と非準拠警告」が緑になる (観測可能な完了条件)
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 4.2_
  - _Boundary: TimelineAdapterBinding, RecToTimelineExporter_
  - _Depends: 1.2, 1.3_

## Inspector: 目線タブ再設計と旧導線の全撤去

- [ ] 6. Inspector: 目線タブ 1 か所完結・自動解決・旧構造一掃（本 spec 最大の変更面積。段階削除で進める）

- [ ] 6.1 provider 列挙 helper と入力ソースドロップダウンを実装する
  - 割当済み binding（SerializeReference 走査）から宣言 provider を列挙する Editor helper を、既存のソースポート列挙前例と同型で EditMode テスト可能な形に切り出す
  - チャネルごとの入力ソースドロップダウン: 「自動」既定 + 宣言 provider の列挙（表示は binding displayName + slug、選択値は providerSlug へ保存、ワイルドカード宣言 provider は全チャネルの選択肢に表示）
  - EditMode テストで「provider 列挙（宣言あり / なし / ワイルドカード）」「選択値の providerSlug 保存」が緑になる (観測可能な完了条件)
  - _Requirements: 4.3, 4.8_

- [ ] 6.2 目線タブ本体（チャネルリスト・既定保護・上級 foldout・legacy HelpBox）を実装する
  - 目線タブを「legacy 検出 HelpBox（移行ガイド誘導 + 旧データクリアボタン）/ 参照モデル / チャネルリスト / チャネル本体（入力ソース・目ボーン・可動角）/ 上級 foldout（distinct 左右 id・初期回転・軸）」で構成し、gaze の全設定を 1 か所で編集可能にする
  - チャネルリスト: 先頭 = 既定チャネル（id 非編集・削除ボタン非表示のラベル表示）、追加チャネルは id validation（規約 helper）+ リスト内重複禁止 + 削除可、「チャネルを追加」は上級者向け折りたたみ内に配置
  - 既定構成（規約チャネル 1 件 + 「自動」）では上級設定を前面に出さず、目ボーンと入力ソース選択が主要操作となる表示にする
  - すべての編集を既存 SerializedObject / Undo パイプライン経由とし、OnEnable で既定チャネル不変条件を SerializedProperty 上で修復する
  - EditMode テストで「既定チャネル保護（削除・id 編集不可）validation」「追加 id validation / 重複禁止」「legacy HelpBox 表示条件とクリアボタンの stale 行除去」が緑になる（UIToolkit の panel 未接続制約に留意しロジックは helper へ抽出。既知の disposed SerializedObject ガードを維持） (観測可能な完了条件)
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 1.3, 1.5, 2.7_

- [ ] 6.3 目ボーン自動解決の自動実行と Animator 起点フルパス保存を実装する
  - 参照モデルの変更（null → 非 null / 別モデル）を検知して自動解決を自動実行する（現行の `*` マーク表示のみの置換）。自動実行は空の bone path フィールドのみ補完し、手動編集済み非空 path は上書きしない
  - path 生成は参照モデル内 Animator の Transform 起点の階層パス（`/` 区切り）で保存する（旧 backlog S-1 の単純名保存問題の解消。Animator 不在時は参照モデル root 起点 + 注意ログ）。Humanoid → 名前ヒューリスティックの 2 段解決は現行維持
  - 明示的な再解決操作（チャネル別 / 一括、上書き確認あり）を提供し、解決不能時は HelpBox で明示 + 手動設定の手掛かり（Humanoid マッピング / 命名規則 / path 直接入力）を案内する
  - EditMode テストで「自動実行の発火条件」「非空 path の非上書き」「Animator 起点の path 生成」「解決不能時の案内表示条件」が緑になる (観測可能な完了条件)
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

- [ ] 6.4 旧導線・孤児削除・isGaze validation を撤去し旧型を一掃する
  - 孤児 GazeConfig 削除の 3 トリガ一式 / 隠し dropdown デッドパス / isGaze Toggle 行 UI と連動 validation / GazeConfig 生成 3 導線 / gaze 候補列挙 / look* の UI・validation を撤去し、表情ライブラリタブから gaze 関連 UI を完全に除去する
  - 依存側の置換完了を受けて、旧型（GazeBindingConfig / 旧 resolver / 旧 gaze config DTO）と Spec 1 の Adapters 側合成 helper・side enum を削除する（解決結果型は後継 resolver へ移設済みであること）
  - 旧構造（isGaze / SO ルート gaze config リスト / 生成 3 導線）を参照するコードがプロジェクト全体でゼロになり、Editor asmdef 含む全 asmdef がコンパイル可能で Editor テストが緑になる (観測可能な完了条件)
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 9.2_
  - _Depends: 4.2, 5.1, 5.2, 5.3, 5.5_

## サンプル・ドキュメント: 新スキーマ追従と移行ガイド

- [ ] 7. サンプル・ドキュメント: 4+ 系統のアセット更新と破壊的変更の文書化

- [ ] 7.1 (P) OSC 2 サンプルと MultiSourceBlendDemo を新スキーマへ更新する
  - MultiSourceBlendDemo / OscOutputDemo / OscReceiverDemo の Profile アセットと対応 profile.json を新スキーマ（Gaze セクション、isGaze / gaze_configs なし）へ更新する。gaze 構成は既定チャネル `"gaze"` + 入力ソース選択のみとし上級設定を使用しない
  - OscOutputDemo の options JSON から廃止済み gaze id キーを除去する
  - 各サンプル README を新しい gaze 設定手順（Gaze セクション + 入力ソース選択）へ更新する
  - **M-28 交差の注記**: MultiSourceBlendDemo 更新は pre-existing 赤 `SampleAssetsAreInSyncTests` 4 件と交差するが M-28 は取り込まず、当該 4 件は FAIL 判定から除外する（Req 12.6）
  - 3 サンプルの Profile / JSON が新スキーマで読み込まれ、旧スキーマ警告が出ない構成になっている（起動動作確認はタスク 8.4） (観測可能な完了条件)
  - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.6, 11.6_
  - _Boundary: MultiSourceBlendDemo, OscOutputDemo, OscReceiverDemo_

- [ ] 7.2 (P) iFacialMocap demo と lipsync 2 サンプルの stale アセットを更新する
  - iFM demo README の旧手順（GazeBindingConfig での目ボーン結線）を新手順（追加設定なしで既定チャネルに接続 + 目ボーン設定のみ）へ置換する
  - iFM demo / MicLipSyncDemo / AnimationClipLipSyncDemo の Profile アセットを再保存し stale な旧キー行を除去する（新キーの空リスト行が残るのは仕様）
  - アセット diff で旧キー `_gazeConfigs` 行が 3 アセットから消えている (観測可能な完了条件)
  - _Requirements: 12.5_
  - _Boundary: IFacialMocapReceiverDemo, MicLipSyncDemo, AnimationClipLipSyncDemo_

- [ ] 7.3 (P) 移行ガイドと CHANGELOG を破壊的変更へ追従させる
  - migration-guide の陳腐化節（存在しない型 `InputSystemGazeBinding` / `_gazeInputBindings` を参照する L232-273 相当）を除去し、新スキーマへの移行手順に置き換える: SO / JSON 両経路の再設定手順、isGaze ダミー Expression の削除手順、InputSystem gaze entry の expressionId → チャネル id 読み替え、options JSON キー削除、distinct 上級構成で actionName 由来 id が解決不能になる注意、.fcrec 記録済みデータの旧 id 残存の既知事項、第三者 binding の interface 実装手順
  - Fork 実機（`D:\Unvgi\Repositries\UnvgiFacialVerification`）の Profile が同手順の対象であることを明示する（手動再設定方針 = D-3。自動マイグレータなし）
  - core + 変更拡張（osc / inputsystem / ifacialmocap / timeline）の CHANGELOG に Breaking changes を記録する（isGaze 廃止 / Gaze セクション統合 / JSON スキーマ変更 / GazeSnapshot の id リネーム / 送信側旧 API・options キー廃止 / InputSystem 登録流儀変更）
  - 移行ガイドから旧型参照が消え、旧データ保有者が再設定手順を辿れる状態になっている (観測可能な完了条件)
  - _Requirements: 13.1, 13.2, 13.3_
  - _Boundary: migration-guide, CHANGELOG_

- [ ] 7.4 (P) mental-model / technical-spec / requirements の gaze 記述を更新する
  - mental-model と quickstart 系ドキュメントへ新しい gaze 設定手順（Gaze セクション・入力ソース選択・目ボーン自動解決）を反映する
  - 「目レイヤー = まばたき等の BlendShape 表情（レイヤー合成参加）」「Gaze = ボーン駆動の独立チャネル（レイヤー合成不参加）」の用語分離を明文化し、docs/requirements.md の「目レイヤー = まばたき・目線」記述と整合させる（design Decision 2）
  - docs/technical-spec.md §12/§17 の gaze_follow / gaze_camera「Expression テンプレート」構想を、procedural gaze 入力ソース切替（S2-3 再解釈）に基づく記述へ更新する
  - Timeline の gaze チャネル sub は既定チャネル id を推奨とする旨をサンプル / ドキュメントに明記する
  - 各ドキュメントから新 identity モデル（規約 id・入力ソース選択・レイヤー外チャネル）が一貫して読み取れる (観測可能な完了条件)
  - _Requirements: 13.4, 13.5, 13.6_
  - _Boundary: docs/mental-model.md, docs/technical-spec.md, docs/requirements.md_

## Validation: ゼロ設定 E2E・性能・全体スイープ

- [ ] 8. Validation: OSC ゼロ設定 E2E・統合退行・GC・最終スイープ

- [ ] 8.1 OSC 送受信ゼロ id 設定 E2E を検証する
  - Spec 1 の決定論方式（受信 handler 直接呼び出し + UDP loopback）を踏襲する
  - 既定チャネル: 送信側既定 Gaze セクション → 広告 id `"gaze"` → 受信側ゼロ id 設定（既定チャネル + 目ボーン path のみ）→ 目ボーン localRotation 反映まで assert する（Spec 1 D-1 案 (c) の完結 = 残る手動設定が受信側目ボーンのみであることの実証）
  - 追加チャネル: ユーザー命名 id が広告に載り受信側同名チャネルで解決されることを検証する
  - 既定構成で突合警告が発火しないこと、広告プロトコル（アドレス・flat pairs payload・chunk 分割規約）が無変更であることをテストで固定する
  - 上記 PlayMode テスト群が緑になる (観測可能な完了条件)
  - _Requirements: 11.1, 11.2, 11.3, 11.5_
  - _Depends: 5.1, 5.2_

- [ ] 8.2 iFM / InputSystem / Timeline の統合と既存退行禁止を検証する
  - iFM: 既定チャネルでの追加設定なし接続（現行互換の登録 id → 目ボーン provider 到達）を PlayMode で検証する
  - InputSystem: gaze entry（チャネル id 参照）→ 規約 id 登録 → 目ボーン反映、distinct 構成の左右独立を検証する
  - 既存退行禁止: Spec 1 の広告駆動 E2E 群 / Timeline gaze takeover / rec 書き出しの既存テストが新データモデルで緑のまま維持されることを確認する
  - 上記 PlayMode シナリオ群が全て緑になる (観測可能な完了条件)
  - _Requirements: 4.6, 4.7, 10.3, 11.4_
  - _Depends: 5.3, 5.4, 5.5_

- [ ] 8.3 (P) gaze 経路の GC ゼロと自己修復の非再入を検証する
  - チャネル構成済み状態で snapshot 構築 + 目ボーン適用を 100 フレーム実行し gaze 経路由来の GC allocation が 0 byte であることを検証する（既存 GC テストの新モデル追従）
  - Gaze セクションアクセサの連続呼び出しで修復済みリストの再確保が発生しないことを検証する
  - 上記 PlayMode 性能テストが緑になる (観測可能な完了条件)
  - _Requirements: 1.1, 1.4_
  - _Boundary: 性能テスト（core PlayMode/Performance）_
  - _Depends: 4.2_

- [ ] 8.4 全体スイープとスコープ遵守の最終確認を実施する
  - EditMode / PlayMode の全テストを batchmode で実行し、本 spec の変更起因の赤がゼロであることを確認する（冒頭一覧の pre-existing 赤は 1.1 のベースラインと突合して除外判定。M-28 は Req 12.6 どおり不取り込み）
  - 3 サンプル Scene の起動でスキーマ関連の警告・エラーが出ず gaze を含めて動作することを確認する
  - スコープ外事項（M-13 合成戦略 / M-5 procedural 実装 / Vector3・VRM / 広告プロトコル変更 / M-29 BlendShape gaze 配線）が成果物に混入していないことを確認し、実装中に必要が生じた項目は backlog 記録または新 spec 起票で決着していることを確認する
  - 全体スイープ結果とスコープ遵守確認が記録され、Fork 実機 Profile の再設定が spec 外フォローアップとして引き継がれている (観測可能な完了条件)
  - _Requirements: 12.2, 12.6, 14.1, 14.2, 14.3, 14.4, 14.5, 14.6_
  - _Depends: 7.1, 7.2, 8.1, 8.2_
