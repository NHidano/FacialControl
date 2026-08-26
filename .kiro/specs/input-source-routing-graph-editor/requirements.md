# Requirements Document

## Project Description (Input)
入力源ルーティング・グラフエディタ（Input Source Routing Graph Editor）。

## 背景・解決する課題
FacialControl のプロファイル（FacialCharacterProfileSO）では、各レイヤーの入力源を `inputSources[].id` という文字列 slug（例 `lipsync-overlay:a`）で保持し、現状 Inspector では生の PropertyField で人間が手打ちしている（FacialCharacterProfileSOInspector.cs:1738-1743）。この id は本来自由入力ではなく、プロファイルに載った Adapter Binding が GetDefaultLayerInputSources()/registry slug/overlay slot 宣言から算出できる確定値である。手打ち運用のため binding slug 形（ulipsync:a）と overlay 登録 prefix 形（lipsync-overlay:a）の取り違えが起き、InputSourceRegistry.TryResolve が無言で外れて音素が欠落する事故が実際に発生した（HANDOVER 参照）。本機能はこの「人間が id 文字列を直書きする運用」を廃し、グラフィカルな配線 UI に置き換える。

## 中心思想
id 文字列をソースノードの出力ポートが内部保持し、画面にはラベル（「あ」等）のみ表示。ユーザーはノード間に線（エッジ）を引くだけ。エッジが canonical id を運ぶので slug 不一致が物理的に発生不能になる。

## UI 形態（確定事項）
- パッチベイ/ノードグラフ形式。左=入力源ノード → 中=レイヤーノード → 右=合成出力ノード、の左→右フロー。
- 配置: 専用エディタウィンドウ（FacialCharacterProfileSO を選んで「ルーティングを編集」ボタンから開く独立ウィンドウ）。既存 Inspector は従来通り残す。
- 技術基盤: UnityEditor.Experimental.GraphView（Shader Graph 等で使われる公式 GraphView。ノード/エッジ/ズーム/ミニマップが標準提供）。experimental 名前空間である点はリスクとして design で扱う。

## ノード3種とデータ対応
- ソースノード: `_adapterBindings[]` が公開する id（GetDefaultLayerInputSources ＋ registry slug ＋ overlay slot 宣言）から生成。読み取り専用。各出力ポートに canonical id を紐づけ、ラベルのみ表示・ホバーで実 id を tooltip。uLipSync なら a/i/u/e/o の5ポート。
- レイヤーノード: `_layers[]`（name/priority/exclusionMode/overrideMask）。レイヤー属性を直接編集可能。
- エッジ（source→layer）: `_layers[i].inputSources[]` の各 {id, weight} に対応。配線＝Declaration 追加、切断＝削除、エッジ上の weight ノブ（ドラッグ/数値）＝weight 編集。
- 出力ノード: priority + overrideMask の合成順序を俯瞰（読み取り専用）。

このグラフは FacialCharacterProfileSO の _layers/_slots/_defaultOverlays の別ビューであり、保存先は既存 SO のまま。JSON エクスポート経路は無改修で通すこと。

## 自動配線ボタン（スコープ内）
binding 追加時にソースノードが自動出現。ノードヘッダの「overlay レイヤーへ一括配線」ボタンで、binding.GetDefaultLayerInputSources を全レイヤー名で評価し該当レイヤーへ weight 1.0 のエッジを一括生成。slot 未宣言なら _slots に a/i/u/e/o を同時宣言（既存「Phoneme slots を初期化」と統合）。常用ケースの手入力をゼロにする。

## 無効配線の可視化（スコープ内・限定）
既存プロファイルを開いた時、どのソースポートにも一致しない id（旧 ulipsync:a や typo）を「赤い破線の宙ぶらりんエッジ」として描画し、ユーザーが事故に気づけるようにする。※ワンクリック自動修復ボタンは今回スコープ外（横断機能は自動配線のみ採用）。

## スコープ外（Non-Goals）
- 無効 id のワンクリック自動修復ボタン（別途検討）。
- ランタイム UI（Editor 専用。CLAUDE.md 方針通りランタイム UI は提供しない）。

## 技術・規約上の制約
- Editor 拡張は UI Toolkit ベース（既存 FacialControlCommon.uss と整合）。GraphView も UI Toolkit 系。
- C# 名前空間 Hidano.FacialControl、4スペース、明示的 public/private、日本語コメント。
- TDD（Red-Green-Refactor）。Editor ロジック（id 列挙・配線↔SO 変換・検証）は EditMode テスト可能な純粋ロジックに切り出す。GraphView 描画自体は薄く保つ。
- 関連既存型: InputSourceId, FacialProfile(LayerInputSources/Slots), InputSourceDeclaration, FacialCharacterProfileSO(_layers/_slots/_defaultOverlays/_adapterBindings), IInputSourceRegistry/InputSourceRegistry, PhonemeOverlaySlots, LipSyncPhonemeOverlayInputSource, ULipSyncAdapterBinding(GetDefaultLayerInputSources/ApplyInitialDefaults)。
- パッケージ: com.hidano.facialcontrol の Editor アセンブリに配置（lipsync binding 固有に依存しない汎用設計。uLipSync は一例として扱う）。

## Introduction
本機能は、FacialCharacterProfileSO のレイヤー入力源配線（`_layers[].inputSources[].id`）を、人間が文字列 slug を直書きする運用から、ノードグラフ（パッチベイ）による配線 UI へ置き換える Editor 拡張である。

中心となる設計思想は「canonical id をソースノードの出力ポートが内部的に保持し、エッジが id を運ぶ」ことにある。ユーザーは画面上のラベル（「あ」等）を見ながらノード間に線を引くだけであり、id 文字列を一度も入力しない。これにより、binding slug 形（`ulipsync:a`）と overlay 登録 prefix 形（`lipsync-overlay:a`）の取り違えに代表される slug 不一致を、UI 操作のレベルで物理的に発生不能にする。グラフは FacialCharacterProfileSO（`_layers` / `_slots` / `_defaultOverlays` / `_adapterBindings`）の別ビューであり、保存先は既存 SO のままで、JSON エクスポート経路は無改修で通る。

対象ユーザーは FacialControl を組み込む Unity エンジニアであり、本機能は `com.hidano.facialcontrol` の Editor アセンブリに配置する汎用機能とする（uLipSync は一例にすぎない）。

## Boundary Context
- **In scope**:
  - FacialCharacterProfileSO から開く専用ノードグラフエディタウィンドウ（UnityEditor.Experimental.GraphView ベース）。
  - ソースノード / レイヤーノード / 出力ノードの 3 種ノードと、source→layer エッジによる配線編集。
  - canonical id をポートが内部保持し、画面ラベルのみ表示する配線モデル。
  - グラフ操作の FacialCharacterProfileSO への保存（`_layers` / `_slots` / `_defaultOverlays`）。
  - overlay レイヤーへの一括自動配線ボタン（slot 同時宣言を含む）。
  - 既存プロファイルのロード時、どのソースポートにも一致しない無効 id を赤い破線の宙ぶらりんエッジとして可視化。
- **Out of scope**:
  - 無効 id のワンクリック自動修復ボタン（別途検討）。
  - ランタイム UI（Editor 専用。ランタイム UI は提供しない方針）。
  - 既存 Inspector（FacialCharacterProfileSOInspector）の置き換えや撤去（従来通り併存させる）。
  - JSON エクスポート/インポート経路自体の改修。
- **Adjacent expectations**:
  - 既存型 InputSourceId / FacialProfile / InputSourceDeclaration / FacialCharacterProfileSO / IInputSourceRegistry / PhonemeOverlaySlots / ULipSyncAdapterBinding の契約を変更せずに利用する。
  - Editor は UI Toolkit ベースで実装し、既存 FacialControlCommon.uss と整合させる。
  - 配線↔SO 変換・id 列挙・検証の純粋ロジックは EditMode テスト可能な形で切り出す（GraphView 描画は薄く保つ）。

## Requirements

### Requirement 1: 専用ルーティングエディタウィンドウの起動
**Objective:** Unity エンジニアとして、FacialCharacterProfileSO の入力源配線を専用ウィンドウで編集したい。これにより、既存 Inspector を壊さずにグラフィカルな配線作業に集中できる。

#### Acceptance Criteria
1. When ユーザーが FacialCharacterProfileSO を選択し「ルーティングを編集」ボタンを押す, the ルーティングエディタウィンドウ shall 対象 FacialCharacterProfileSO を読み込んだ独立ウィンドウとして開く。
2. The ルーティングエディタウィンドウ shall 編集対象として開いている FacialCharacterProfileSO への参照を保持する。
3. While ルーティングエディタウィンドウが開いている, the 既存 FacialCharacterProfileSOInspector shall 従来通り利用可能な状態を維持する。
4. If 「ルーティングを編集」ボタンの押下時に対象 FacialCharacterProfileSO が null または破棄済みである, then the ルーティングエディタウィンドウ shall ウィンドウを開かず Unity 標準ログで警告を出力する。
5. When 同一の FacialCharacterProfileSO に対するルーティングエディタウィンドウが既に開いている状態で再度起動が要求される, the ルーティングエディタウィンドウ shall 新規ウィンドウを重複生成せず既存ウィンドウを前面に表示する。

### Requirement 2: 左→右フローのノードグラフ表示
**Objective:** Unity エンジニアとして、入力源・レイヤー・合成出力を左→右のフローで俯瞰したい。これにより、配線の全体構造を直感的に把握できる。

#### Acceptance Criteria
1. When ルーティングエディタウィンドウが FacialCharacterProfileSO を読み込む, the ルーティングエディタウィンドウ shall ソースノード群を左、レイヤーノード群を中央、合成出力ノードを右に配置したグラフを描画する。
2. The ルーティングエディタウィンドウ shall UnityEditor.Experimental.GraphView を基盤としてノード・エッジ・ズーム・ミニマップを提供する。
3. The ルーティングエディタウィンドウ shall UI Toolkit ベースで実装し、既存 FacialControlCommon.uss と整合するスタイルを適用する。
4. When ユーザーがグラフのズームまたはパン操作を行う, the ルーティングエディタウィンドウ shall ノード配置を保ったままビューポートを拡大縮小・移動する。

### Requirement 3: ソースノードの生成と canonical id の内部保持
**Objective:** Unity エンジニアとして、入力源を id 文字列ではなくラベル付きのポートとして扱いたい。これにより、id を一度も手入力せずに配線できる。

#### Acceptance Criteria
1. When ルーティングエディタウィンドウが FacialCharacterProfileSO を読み込む, the ルーティングエディタウィンドウ shall `_adapterBindings[]` が公開する id（GetDefaultLayerInputSources、registry slug、overlay slot 宣言由来）からソースノードを生成する。
2. The ソースノード shall 各出力ポートに canonical id を内部的に紐づけ、画面にはラベル（「あ」等）のみを表示する。
3. When ユーザーがソースノードの出力ポートにホバーする, the ソースノード shall 当該ポートが保持する実 id を tooltip として表示する。
4. The ソースノード shall 読み取り専用とし、ポートのラベルや id をグラフ上で直接編集させない。
5. Where 対象 binding が uLipSync である, the ソースノード shall a / i / u / e / o の 5 ポートを公開する。
6. When binding が新たに FacialCharacterProfileSO へ追加される, the ルーティングエディタウィンドウ shall 対応するソースノードを自動的に出現させる。

### Requirement 4: レイヤーノードによるレイヤー属性編集
**Objective:** Unity エンジニアとして、レイヤーの属性をグラフ上で直接編集したい。これにより、配線とレイヤー設定を同一画面で完結できる。

#### Acceptance Criteria
1. When ルーティングエディタウィンドウが FacialCharacterProfileSO を読み込む, the ルーティングエディタウィンドウ shall `_layers[]` の各要素に対応するレイヤーノードを生成する。
2. The レイヤーノード shall name / priority / exclusionMode / overrideMask を表示し、ユーザーが直接編集できるようにする。
3. When ユーザーがレイヤーノード上でレイヤー属性を変更する, the ルーティングエディタウィンドウ shall 対応する `_layers[]` の値を更新する。

### Requirement 5: エッジによる配線（Declaration）の編集
**Objective:** Unity エンジニアとして、ソースポートとレイヤーを線でつなぐだけで入力源を宣言したい。これにより、slug の取り違えなく weight 付き配線を作成・編集できる。

#### Acceptance Criteria
1. The エッジ（source→layer） shall `_layers[i].inputSources[]` の各 {id, weight} に対応する。
2. When ユーザーがソースノードの出力ポートからレイヤーノードへエッジを引く, the ルーティングエディタウィンドウ shall 当該ポートが保持する canonical id を用いて対応するレイヤーへ InputSourceDeclaration を追加する。
3. When ユーザーが既存エッジを切断する, the ルーティングエディタウィンドウ shall 対応する `_layers[i].inputSources[]` の要素を削除する。
4. The エッジ shall weight ノブを備え、ユーザーがドラッグまたは数値入力で weight を編集できるようにする。
5. When ユーザーがエッジの weight ノブを操作する, the ルーティングエディタウィンドウ shall 対応する InputSourceDeclaration の weight を更新する。
6. The ルーティングエディタウィンドウ shall 配線時に id 文字列をユーザーへ入力させず、ポートが保持する canonical id のみをエッジに運ばせる。

### Requirement 6: 合成出力ノードによる合成順序の俯瞰
**Objective:** Unity エンジニアとして、レイヤーの優先度と override マスクによる合成順序を一目で確認したい。これにより、配線結果が最終出力にどう影響するかを把握できる。

#### Acceptance Criteria
1. When ルーティングエディタウィンドウが FacialCharacterProfileSO を読み込む, the ルーティングエディタウィンドウ shall priority と overrideMask による合成順序を俯瞰する合成出力ノードを右端に描画する。
2. The 合成出力ノード shall 読み取り専用とし、合成順序をグラフ上で直接編集させない。
3. When レイヤーの priority または overrideMask が変更される, the 合成出力ノード shall 表示する合成順序を更新後の値で再描画する。

### Requirement 7: グラフ操作の FacialCharacterProfileSO への保存
**Objective:** Unity エンジニアとして、グラフ上の編集結果が既存の SO とそのままの永続化経路で保存されてほしい。これにより、JSON エクスポートを含む既存ワークフローを改修せずに使い続けられる。

#### Acceptance Criteria
1. When ユーザーがグラフ上で配線・レイヤー属性・weight を変更する, the ルーティングエディタウィンドウ shall 変更を編集対象 FacialCharacterProfileSO の `_layers` / `_slots` / `_defaultOverlays` へ反映する。
2. The ルーティングエディタウィンドウ shall FacialCharacterProfileSO 以外の新たな保存先を導入しない。
3. The ルーティングエディタウィンドウ shall 既存の JSON エクスポート経路を改修せずに通す（グラフ編集の結果がそのまま既存経路でエクスポートできる）。
4. When FacialCharacterProfileSO の値がグラフ操作で変更される, the ルーティングエディタウィンドウ shall Unity の Undo 履歴に登録し、編集対象アセットを dirty としてマークする。

### Requirement 8: overlay レイヤーへの一括自動配線
**Objective:** Unity エンジニアとして、常用ケースの配線をワンボタンで生成したい。これにより、overlay 配線の手入力をゼロにできる。

#### Acceptance Criteria
1. The ソースノードヘッダ shall 「overlay レイヤーへ一括配線」ボタンを備える。
2. When ユーザーが「overlay レイヤーへ一括配線」ボタンを押す, the ルーティングエディタウィンドウ shall 当該 binding の GetDefaultLayerInputSources を全レイヤー名で評価し、該当するレイヤーへ weight 1.0 のエッジを一括生成する。
3. When 一括自動配線の実行時に対象の overlay slot が `_slots` に未宣言である, the ルーティングエディタウィンドウ shall a / i / u / e / o の slot を `_slots` へ同時宣言する。
4. The ルーティングエディタウィンドウ shall 一括自動配線機能を既存の「Phoneme slots を初期化」操作と統合し、単一のボタン操作で配線と slot 宣言を完了させる。

### Requirement 9: 無効配線の赤い破線可視化
**Objective:** Unity エンジニアとして、既存プロファイルに残った無効な id に気づきたい。これにより、slug 不一致による音素欠落の事故を発見できる。

#### Acceptance Criteria
1. When ルーティングエディタウィンドウが既存 FacialCharacterProfileSO を読み込む, the ルーティングエディタウィンドウ shall `_layers[].inputSources[].id` のうちどのソースポートの canonical id にも一致しないものを無効 id として検出する。
2. If 無効 id が検出される, then the ルーティングエディタウィンドウ shall 当該 Declaration を赤い破線の宙ぶらりんエッジ（接続元ポートを持たないエッジ）として描画する。
3. The ルーティングエディタウィンドウ shall 無効 id の宙ぶらりんエッジに対するワンクリック自動修復ボタンを提供しない。
4. The ルーティングエディタウィンドウ shall 無効 id を検出しても対応する `_layers[].inputSources[]` の要素を自動的に削除・改変しない（ユーザーの明示操作まで温存する）。

### Requirement 10: 配線ロジックのテスト可能な分離と汎用性
**Objective:** FacialControl 開発者として、配線↔SO 変換・id 列挙・検証ロジックを GraphView 描画から分離したい。これにより、TDD で振る舞いを検証でき、特定 binding に依存しない汎用機能として維持できる。

#### Acceptance Criteria
1. The ルーティングエディタ機能 shall id 列挙・配線↔SO 変換・無効 id 検証のロジックを GraphView 描画から分離した純粋ロジックとして実装する。
2. The ルーティングエディタ機能 shall 当該純粋ロジックを EditMode テストで検証可能にする。
3. The ルーティングエディタ機能 shall 特定の uLipSync binding 実装に依存せず、binding が公開する id を一般的に扱う汎用設計とする。
4. The ルーティングエディタ機能 shall `com.hidano.facialcontrol` の Editor アセンブリ（Hidano.FacialControl.Editor 名前空間）に配置する。
5. The ルーティングエディタ機能 shall 既存型 InputSourceId / FacialProfile / InputSourceDeclaration / FacialCharacterProfileSO / IInputSourceRegistry / PhonemeOverlaySlots の契約を変更せずに利用する。
