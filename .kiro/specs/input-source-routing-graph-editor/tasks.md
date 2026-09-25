# 実装計画

> 中心思想: canonical id をソースノードのポートが内部保持し、エッジが id を運ぶことで slug 不一致を UI 操作レベルで物理的に発生不能にする。純粋ロジック層（GraphView 非依存・EditMode テスト対象）を先行実装し、GraphView 薄描画層は後付けする（research.md 実装アプローチ A + C）。

## 1. 基盤整備: テスト土台と既存挙動の固定

- [x] 1.1 ルーティング機能の Editor 配置とテスト土台を準備する
  - 既存 `Hidano.FacialControl.Editor` アセンブリ配下にルーティング機能用のディレクトリ構成（純粋ロジック層と GraphView 薄描画層を分離）を用意する
  - 純粋ロジックを検証する EditMode テストアセンブリ参照が整い、空のテストが Editor アセンブリ参照込みでビルド・実行できる状態にする
  - GraphView module が using のみで参照可能であることを最小コンパイルで確認する（asmdef references 変更が不要であることの確認を含む）
  - 観測可能な完了条件: 新規ディレクトリ配下の空クラスと EditMode テストが既存テストランナーで収集・実行され、ビルドが通る
  - _Requirements: 10.1, 10.2, 10.4_

- [x] 1.2 既存 id 列挙ロジックの現挙動を固定する回帰テストを先行作成する
  - 抽出対象である既存 Inspector の default input source 解決ロジックについて、抽出前の出力結果を固定する EditMode 回帰テストを作成する
  - uLipSync を含む代表 binding と複数レイヤー名の組み合わせで、現状の解決結果（canonical id 集合）を期待値としてキャプチャする
  - 観測可能な完了条件: 抽出前の現行コードに対して回帰テストが緑で通り、後続の抽出リファクタの安全網として機能する
  - _Requirements: 10.1, 10.3_
  - _Boundary: AdapterBindingsListView_

- [x] 1.3 既存 slot 初期化ロジックの現挙動を固定する回帰テストを先行作成する
  - 抽出対象である既存 Inspector の a/i/u/e/o slot 初期化ロジックについて、抽出前の出力結果を固定する EditMode 回帰テストを作成する
  - slot 未宣言・一部宣言済み・全宣言済みの各状態で初期化後の slot 構成を期待値としてキャプチャする
  - 観測可能な完了条件: 抽出前の現行コードに対して回帰テストが緑で通り、後続の抽出リファクタの安全網として機能する
  - _Requirements: 8.4_
  - _Boundary: FacialCharacterProfileSOInspector_

## 2. コア純粋ロジック: id 列挙と検証

- [x] 2.1 ソースポート列挙ロジックを実装する
  - binding 群と全レイヤー名から、各 binding が公開する canonical id 群を静的・決定的に列挙する純粋ロジックを TDD で実装する
  - canonical id の 3 供給源（全レイヤー名で評価した既定入力源、legacy 単一既定入力源、overlay slot 由来 id）を distinct 集約する
  - registry を一切参照せず（Editor では空）、同一入力に対し同一出力を返す決定性を保証する
  - 表示ラベルを id 末尾の slot/phoneme から導出し、導出失敗時は id をそのままラベルとする（特定 binding 命名に依存しない）
  - 観測可能な完了条件: uLipSync binding と overlay レイヤー名から a/i/u/e/o の 5 ポートが canonical id 付きで返り、レイヤー名非依存で全ポートが得られる EditMode テストが緑になる
  - _Requirements: 3.1, 3.5, 8.2, 10.1, 10.2, 10.3, 10.5_
  - _Boundary: SourcePortEnumerator_

- [x] 2.2 既存 Inspector の id 列挙を抽出済みロジックへ統合する
  - 既存 Inspector の default input source 解決ロジックを 2.1 の純粋ロジックへ移設し、Inspector 側を薄いラッパへ置換して id 列挙の単一真実源化を完了する
  - 観測可能な完了条件: 1.2 の回帰テストが抽出後も緑のまま維持され、Inspector が新ヘルパ経由で同一結果を返す
  - _Requirements: 10.1, 10.3, 10.5_
  - _Depends: 1.2, 2.1_
  - _Boundary: AdapterBindingsListView, SourcePortEnumerator_

- [x] 2.3 (P) 無効 id 検証ロジックを実装する
  - 配線済み id 群とソースポート canonical id 集合を入力に、どのポートにも一致しない id を無効 id（レイヤー位置 + 宣言位置 + id）として判定する純粋ロジックを TDD で実装する
  - 検出のみを行い、SO の削除・改変・修復を一切行わない不変条件を満たす
  - 観測可能な完了条件: legacy slug 形（例 `ulipsync:a`）が無効 id として検出され、対象 SO が不変であることを確認する EditMode テストが緑になる
  - _Requirements: 9.1, 9.4, 10.1, 10.2_
  - _Depends: 2.1_
  - _Boundary: InvalidIdValidator_

- [x] 2.4 (P) phoneme slot 初期化ロジックを実装し既存 Inspector へ統合する
  - a/i/u/e/o の予約 slot のうち未宣言分を SO へ追加する純粋ロジックを TDD で実装する
  - 既存 Inspector の slot 初期化ロジックを本ロジックへ移設し、Inspector 側を薄いラッパへ置換する
  - 観測可能な完了条件: 1.3 の回帰テストが抽出後も緑のまま維持され、未宣言 slot のみが追加され既存宣言が温存される EditMode テストが緑になる
  - _Requirements: 7.1, 8.3, 8.4, 10.1, 10.2_
  - _Depends: 1.3_
  - _Boundary: PhonemeSlotInitializer, FacialCharacterProfileSOInspector_

## 3. コア純粋ロジック: SO 編集と一括配線

- [x] 3.1 配線・属性・weight の SO 編集ロジックを実装する
  - 配線追加・配線切断・weight 更新・レイヤー属性更新（name/priority/exclusionMode/layerOverrideMask）を編集対象 SO へ反映する純粋ロジックを TDD で実装する
  - すべて SerializedObject/SerializedProperty 経由で行い、コミット時に Undo 登録と dirty マークを行う
  - 配線は対象レイヤーの入力源配列への canonical id + weight 要素の追加、切断は一致要素の削除、weight 更新は一致要素の weight 更新として実装する
  - 新規保存先を導入せず、無効 id 要素と optionsJson を改変しない不変条件を満たす
  - weight 連続ドラッグを開始・逐次反映・確定の 3 段に分離し、1 ドラッグ = 1 Undo に collapse する API を備える
  - 観測可能な完了条件: 配線追加で入力源配列に id/weight が追加され Undo・dirty が立ち、切断で一致要素が削除される EditMode テストが緑になる
  - _Requirements: 4.3, 5.2, 5.3, 5.5, 7.1, 7.2, 7.4, 10.1, 10.2_
  - _Boundary: WiringSerializedMapper_

- [x] 3.2 一括自動配線ロジックを実装する
  - 対象 binding の既定入力源を全レイヤー名で評価し、該当レイヤーへ weight 1.0 のエッジを一括追加する純粋ロジックを TDD で実装する
  - overlay slot 未宣言時は a/i/u/e/o の slot 宣言を同時に行い、配線と slot 宣言を単一 Undo グループへまとめる（既存 slot 初期化操作と統合）
  - 特定 binding 実装に依存せず既定入力源インターフェース経由で汎用に扱い、未実装 binding では no-op とする
  - 既存配線と重複しない範囲で配線を追加し、既存配線・無効 id を破壊しない
  - 観測可能な完了条件: overlay レイヤーへの一括配線で weight 1.0 配線と a/i/u/e/o slot 宣言が単一 Undo で完了する EditMode テストが緑になる
  - _Requirements: 8.2, 8.3, 8.4, 10.1, 10.2, 10.3_
  - _Depends: 2.1, 2.4, 3.1_
  - _Boundary: AutoWireService, SourcePortEnumerator, PhonemeSlotInitializer, WiringSerializedMapper_

## 4. コア純粋ロジック: グラフモデル構築

- [x] 4.1 SO からグラフモデルを冪等構築するロジックを実装する
  - 編集対象 SO から GraphView 非依存のグラフモデル（ソースノード群・レイヤーノード群・出力ノード・通常エッジ・無効エッジ）を構築する純粋ロジックを TDD で実装する
  - ソースノードはポート列挙ロジック由来、レイヤーノードは name/priority/exclusionMode/layerOverrideMask と配線リスト、出力ノードは priority と layerOverrideMask による合成順序を保持する
  - 無効 id 判定を検証ロジックへ委譲し、無効 id を無効エッジとしてモデルに含めるのみで SO を改変しない
  - 全入力源宣言が通常エッジか無効エッジのいずれかに取りこぼしなく分類される不変条件を満たす
  - 同一 SO 状態から同一モデルを返す冪等性を保証する（rebuild の基礎）
  - 観測可能な完了条件: 同一 SO から構築したモデルが等価であり、全宣言が通常/無効エッジに分類されることを確認する EditMode テストが緑になる
  - _Requirements: 3.1, 4.1, 6.1, 9.1, 9.4, 10.1, 10.2_
  - _Depends: 2.1, 2.3_
  - _Boundary: RoutingGraphModelBuilder, SourcePortEnumerator, InvalidIdValidator_

## 5. GraphView 薄描画層: ノードとエッジ

- [x] 5.1 (P) ソースノード描画と一括配線ボタンを実装する
  - グラフモデルのソースノード記述からソースノードを描画し、各出力ポートに canonical id を内部保持しつつ画面にはラベルのみを表示する
  - ポートのホバーで実 id を tooltip 表示し、ノードを読み取り専用化して id・ラベルをグラフ上で直接編集させない
  - ノードヘッダに「overlay レイヤーへ一括配線」ボタンを設け、押下で一括自動配線ロジックを呼ぶ
  - 観測可能な完了条件: ソースノードがラベル表示・id 非表示・読み取り専用で描画され、ヘッダの一括配線ボタンから配線と slot 宣言が反映される
  - _Requirements: 3.2, 3.3, 3.4, 5.6, 8.1_
  - _Depends: 4.1, 3.2_
  - _Boundary: SourceNodeView_

- [x] 5.2 (P) レイヤーノード描画と属性編集を実装する
  - グラフモデルのレイヤーノードデータから name/priority/exclusionMode/layerOverrideMask を編集可能なフィールドとして描画する
  - 属性変更を SO 編集ロジック経由で対応レイヤーへ反映する
  - 観測可能な完了条件: レイヤーノード上での属性変更が対応レイヤーへ反映され Undo・dirty が立つ
  - _Requirements: 4.1, 4.2, 4.3_
  - _Depends: 4.1, 3.1_
  - _Boundary: LayerNodeView, WiringSerializedMapper_

- [x] 5.3 (P) 合成出力ノード描画を実装する
  - グラフモデルの出力ノードデータから priority と layerOverrideMask による合成順序を俯瞰する読み取り専用ノードを右端に描画する
  - 合成順序をグラフ上で直接編集させない読み取り専用化を行う
  - 観測可能な完了条件: 出力ノードが合成順序を読み取り専用で描画し、priority/mask 変更後の rebuild で更新後の値に再描画される
  - _Requirements: 6.1, 6.2, 6.3_
  - _Depends: 4.1_
  - _Boundary: OutputNodeView_

- [x] 5.4 (P) weight ノブ付き通常エッジを実装する
  - source→layer の通常配線エッジを描画し、中点に weight ノブ（ドラッグ Manipulator + 数値入力）を子要素として配置する
  - weight 操作を SO 編集ロジックの連続更新 API（開始・逐次・確定）へ流し、1 ドラッグ = 1 Undo に collapse する
  - 観測可能な完了条件: エッジ上の weight ノブをドラッグ/数値入力すると対応宣言の weight が更新され、1 ドラッグが Undo 1 段に collapse される
  - _Requirements: 5.1, 5.4, 5.5_
  - _Depends: 3.1, 4.1_
  - _Boundary: RoutingEdge, WiringSerializedMapper_

- [x] 5.5 (P) 無効 id 用の赤破線宙ぶらりんエッジを実装する
  - 無効 id を接続元ポートを持たない赤い破線エッジとして読み取り専用で描画する
  - ワンクリック自動修復ボタンを提供せず、無効 id を自動削除・改変しない
  - 観測可能な完了条件: 無効 id 宣言が赤破線の宙ぶらりんエッジとして描画され、修復ボタンが存在せず対応宣言が温存される
  - _Requirements: 9.2, 9.3, 9.4_
  - _Depends: 4.1_
  - _Boundary: DanglingEdge_

## 6. GraphView 統合: ビューとウィンドウ

- [x] 6.1 ルーティンググラフビューを実装しノード・エッジを結線する
  - グラフモデルからソースノード（左）・レイヤーノード（中央）・出力ノード（右）を左→右フローで配置し、ノード/エッジ/ズーム/ミニマップを提供する GraphView を実装する
  - 接続/切断コールバックを SO 編集ロジックへ委譲し、接続可否をソースポート→レイヤー入力ポート方向のみに制限する
  - 配線時に id 文字列をユーザーへ入力させず、ポートが保持する canonical id のみをエッジに運ばせる
  - 既存 uss と整合する UI Toolkit スタイルを適用し、ズーム/パンでノード配置を保ったままビューポートを拡大縮小・移動する
  - 観測可能な完了条件: グラフが左右フローで描画され、ソースポートからレイヤーへエッジを引くと canonical id を用いた配線が追加され、切断で対応要素が削除される
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 5.2, 5.3, 5.6, 9.2_
  - _Depends: 5.1, 5.2, 5.3, 5.4, 5.5_
  - _Boundary: RoutingGraphView_

- [x] 6.2 ルーティングエディタウィンドウの起動制御と外部変更検知を実装する
  - 編集対象 SO 参照と SerializedObject を保持する EditorWindow を実装し、SO が null/破棄の場合は Unity 標準ログで警告しウィンドウを開かない
  - 同一 SO の既存ウィンドウがあれば新規生成せず前面化し、Inspector を従来通り併存可能に保つ
  - Undo/Redo・Inspector 編集・binding 追加・priority/mask 変更などの外部変更を検知してグラフを冪等 rebuild し、binding 追加で対応ソースノードを自動出現させる
  - 観測可能な完了条件: null プロファイル起動で警告ログが出てウィンドウが開かず、同一 SO の二重起動で既存ウィンドウが前面化し、外部変更後にグラフが再構築される最小ウィンドウテストが緑になる
  - _Requirements: 1.2, 1.3, 1.4, 1.5, 3.6, 6.3, 7.3_
  - _Depends: 4.1, 6.1_
  - _Boundary: RoutingEditorWindow_

- [x] 6.3 Inspector に「ルーティングを編集」起動ボタンを追加する
  - 既存 Inspector に「ルーティングを編集」ボタンを追加し、対象 SO を読み込んだルーティングエディタウィンドウを開く
  - 観測可能な完了条件: Inspector のボタン押下で対象 SO を読み込んだ独立ウィンドウが開き、既存 Inspector が従来通り利用可能なまま維持される
  - _Requirements: 1.1, 1.3_
  - _Depends: 6.2_
  - _Boundary: FacialCharacterProfileSOInspector_

## 7. 検証: 取りこぼしなし分類とウィンドウ挙動の確認

- [x] 7.1 グラフモデル構築の取りこぼしなし分類を検証する
  - 通常配線・無効 id・空配線が混在する SO に対し、全入力源宣言が通常エッジか無効エッジのいずれかに取りこぼしなく分類され、無効 id 検出後も SO が改変されないことを EditMode テストで検証する
  - 観測可能な完了条件: 混在ケースで分類が網羅的かつ SO 不変であることを確認する EditMode テストが緑になる
  - _Requirements: 9.1, 9.4_
  - _Depends: 4.1_
  - _Boundary: RoutingGraphModelBuilder, InvalidIdValidator_

- [x] 7.2 一括自動配線と JSON エクスポート無改修の整合を検証する
  - 一括自動配線後の SO が既存 JSON エクスポート経路を改修せずにそのまま通り、配線・slot 宣言が反映された状態でエクスポートできることを EditMode テストで検証する
  - 観測可能な完了条件: 一括配線後の SO が既存エクスポート経路で正しく出力されることを確認する EditMode テストが緑になる
  - _Requirements: 7.2, 7.3, 8.4_
  - _Depends: 3.2_
  - _Boundary: AutoWireService, WiringSerializedMapper_

- [x] 7.3* ウィンドウ起動の受け入れ基準テストを補強する
  - null/破棄プロファイル起動時の警告ログと非起動、同一 SO 二重起動時の前面化を確認する最小ウィンドウテストを補強する
  - 観測可能な完了条件: 起動制御の受け入れ基準（1.4 / 1.5）を確認するテストが緑になる
  - _Requirements: 1.4, 1.5_
  - _Depends: 6.2_
  - _Boundary: RoutingEditorWindow_
