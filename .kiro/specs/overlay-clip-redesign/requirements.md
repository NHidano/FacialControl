# Requirements Document

## Project Description (Input)
overlay-clip-redesign

Overlay 機能をスキーマレベルで刷新する preview.2 段階の破壊的変更。

【ドメイン側】
- FacialProfile に Slots（List<string>）を追加。Adapter Bindings の overlaySlot / Expression.Overlays / FacialProfile.DefaultOverlays が参照する slot 識別子の単一の真実とする。
- OverlaySlotBinding を旧 (slot + expressionId) から新 (slot + suppress + ExpressionSnapshot) へ刷新。3 状態 (default fallback / 明示 suppress / 個別 snapshot override) を表現する。Expression と FacialProfile.DefaultOverlays の双方が新型を保持する。
- OverlayInputSource は「expressionId 引き」から「Expression 内部の slot snapshot 引き」へ書き換える。default fallback は profile.DefaultOverlays から解決する。

【JSON】
- ProfileSnapshotDto に slots フィールド追加。
- OverlaySlotBindingDto を {slot, suppress, snapshot: ExpressionSnapshotDto} へ刷新。
- SystemTextJsonParser / JsonSchemaDefinition / FacialCharacterProfileConverter を新スキーマで読み書き。

【SO Serializable】
- OverlaySlotBindingSerializable に AnimationClip + cachedSnapshot + suppress フラグを追加。
- FacialCharacterProfileSO に _slots（List<string>）を追加。
- FacialCharacterProfileExporter で overlay clip もサンプリング対象にし、cachedSnapshot を更新。

【Inspector UI】
- 「表情」タブを「表情ライブラリ」「レイヤー」「ベース表情」「目線」「Adapter Bindings」「Debug」に再編。レイヤー定義タブと表情ライブラリタブを完全に分離する。
- 表情ライブラリタブに Slots 宣言セクション、Default Overlays セクション、Expression リストを配置。
- 各 Expression row に Layer プルダウンと Overlays セクションを追加。Overlays セクションは slot ごとに 3 状態 (Default / Suppress / 個別 AnimationClip) のラジオ + 個別時の AnimationClip フィールドを表示。
- Default Overlays UI は slot プルダウン × AnimationClip フィールドのリスト形式。

【Sample データ移行】
- Assets/Samples/.../MultiSourceBlendDemoCharacter.asset、Assets/StreamingAssets/.../profile.json、Packages/.../Samples~/.../profile.json を新スキーマに同期更新。blink_overlay は smile / smile_closed_eye の overlays に snapshot として焼き直す。

【テスト】
- OverlaySlotBindingTests / OverlayInputSourceTests / LayerUseCaseOverlayLayerTests / LayerUseCaseAnalogOverlayTests / LayerUseCaseAnalogExpressionAdditionTests / ExpressionUseCaseActiveProviderTests / SystemTextJsonParserOverlaysTests を新スキーマで TDD 書き換え。Inspector UI 用の Edit Mode テストも追加 (3 状態切替、AnimationClip 設定、Slots 宣言の整合性検証)。

【非機能要件】
- preview.2 段階のため後方互換は不要。旧 expressionId フィールドを Parser が読んだ場合は明確なエラーで弾く。
- OverlayInputSource の per-frame GC アロケーションはゼロ目標を維持。
- ARKit / 既存 input system / OSC 経路への影響を最小化する (overlaySlot 参照箇所のみ更新)。

## Introduction
本仕様は preview.2 段階で実施する破壊的スキーマ変更である。Overlay 機能を「Expression が抱える Overlay 表現」から「FacialProfile.Slots を単一の真実とし、Expression と DefaultOverlays がそれぞれ slot ごとに 3 状態 (default fallback / suppress / 個別 ExpressionSnapshot override) を表明するモデル」へ刷新する。Domain / JSON DTO / Parser / Converter / SO Serializable / FacialCharacterProfileSO / FacialCharacterProfileExporter / Inspector UI / Sample データ / テスト群までを一貫して新スキーマへ移行し、旧 `expressionId` フィールドはエラーで明示的に拒否する。Inspector UI は「表情」タブを 6 タブ構成 (表情ライブラリ / レイヤー / ベース表情 / 目線 / Adapter Bindings / Debug) に再編し、Slots 宣言と Overlays 3 状態 UI を表情ライブラリタブに集約する。OverlayInputSource の per-frame GC ゼロ目標は維持し、ARKit / 既存 InputSystem / OSC 経路への影響は overlaySlot 参照箇所に限定する。

## Boundary Context
- **In scope**:
  - Domain: `FacialProfile.Slots`、`OverlaySlotBinding` の `(slot + suppress + ExpressionSnapshot)` 化、`Expression.Overlays` と `FacialProfile.DefaultOverlays` の新型保持、`OverlayInputSource` の snapshot 引きへの書き換え。
  - JSON: `ProfileSnapshotDto.slots` 追加、`OverlaySlotBindingDto` の新スキーマ化、`SystemTextJsonParser` / `JsonSchemaDefinition` / `FacialCharacterProfileConverter` の更新、旧 `expressionId` フィールドのエラー検出。
  - SO: `OverlaySlotBindingSerializable` への `AnimationClip` + `cachedSnapshot` + `suppress` 追加、`FacialCharacterProfileSO._slots` 追加、`FacialCharacterProfileExporter` の overlay clip サンプリング。
  - Inspector UI: 6 タブ再編、Slots 宣言セクション、Default Overlays セクション、Expression リスト (Layer プルダウン + Overlays 3 状態 UI)。
  - Sample: `MultiSourceBlendDemoCharacter.asset` / `Assets/StreamingAssets/.../profile.json` / `Packages/.../Samples~/.../profile.json` の新スキーマ同期、`blink_overlay` の snapshot 焼き直し。
  - テスト: 既存 7 系統のテスト書き換え + Inspector UI 用 Edit Mode テスト追加。
- **Out of scope**:
  - 旧 `expressionId` ベース overlay からの自動マイグレーション (preview.2 で明示的に非対応)。
  - ARKit / InputSystem / OSC のロジック変更 (overlaySlot 参照箇所の更新のみ許容)。
  - リップシンク・Timeline 統合・新規通信プロトコル対応。
  - ランタイム UI 提供 (Editor 拡張のみ)。
- **Adjacent expectations**:
  - `com.hidano.facialcontrol.inputsystem` Sample (`MultiSourceBlendDemo`) は新スキーマで起動しなければならない。
  - dev プロジェクト側 `Assets/Samples/...` と UPM 配布側 `Packages/.../Samples~/...` の二重管理ルールを維持し、両者を同期する。
  - Editor のみで使うサンプリング処理 (Exporter) はランタイム asmdef に混入させない。

## Requirements

### Requirement 1: FacialProfile への Slots 宣言の追加
**Objective:** Unity エンジニアとして、FacialProfile に Slots 宣言を追加したい。これにより Adapter Bindings の `overlaySlot` / `Expression.Overlays` / `FacialProfile.DefaultOverlays` が参照する slot 識別子を単一の真実として一元管理できる。

#### Acceptance Criteria
1. The FacialProfile shall expose a `Slots` collection of slot 識別子文字列を保持するプロパティとして。
2. When FacialProfile が新規構築された場合、the FacialProfile shall `Slots` を空コレクションで初期化する。
3. When `Expression.Overlays` または `FacialProfile.DefaultOverlays` に `Slots` で宣言されていない slot 識別子が含まれている場合、the FacialProfile shall 整合性検証で当該 slot 識別子を不正として検出する。
4. If 同一 slot 識別子が `Slots` に重複登録された場合、then the FacialProfile shall 重複を不正として検出するエラーを返す。
5. The FacialProfile shall Adapter Bindings 側の `overlaySlot` 参照解決で `Slots` を権威ソースとして使用する。

### Requirement 2: OverlaySlotBinding の 3 状態モデルへの刷新
**Objective:** Unity エンジニアとして、OverlaySlotBinding を `(slot + suppress + ExpressionSnapshot)` の 3 状態モデルに刷新したい。これにより default fallback / 明示 suppress / 個別 snapshot override の 3 状態を Expression と DefaultOverlays の双方で統一的に表現できる。

#### Acceptance Criteria
1. The OverlaySlotBinding shall `slot` (識別子)、`suppress` (bool)、`snapshot` (ExpressionSnapshot 参照、null 許容) の 3 フィールドを保持する。
2. When `suppress=false` かつ `snapshot=null` の状態で構築された場合、the OverlaySlotBinding shall 当該 slot を「default fallback (FacialProfile.DefaultOverlays から解決)」として扱う。
3. When `suppress=true` で構築された場合、the OverlaySlotBinding shall 当該 slot に対して overlay を適用しないことを表明する。
4. When `suppress=false` かつ `snapshot` が非 null で構築された場合、the OverlaySlotBinding shall 当該 slot に対して個別 snapshot override を適用することを表明する。
5. If `suppress=true` かつ `snapshot` が非 null の組み合わせで構築されようとした場合、then the OverlaySlotBinding shall 矛盾状態として構築を拒否しエラーを返す。
6. The OverlaySlotBinding shall `Expression.Overlays` および `FacialProfile.DefaultOverlays` の双方で同一型として保持される。
7. The OverlaySlotBinding shall 旧 `expressionId` フィールドを保持しない (型から完全に除去する)。

### Requirement 3: OverlayInputSource の snapshot 引きへの書き換え
**Objective:** Unity エンジニアとして、OverlayInputSource を `expressionId` 引きから「Expression 内部の slot snapshot 引き」へ書き換えたい。これにより新スキーマと整合し、per-frame GC ゼロを維持したまま 3 状態を解決できる。

#### Acceptance Criteria
1. When 現在の Expression が指定 slot に `suppress=false` かつ `snapshot` 非 null の OverlaySlotBinding を保持している場合、the OverlayInputSource shall 当該 snapshot を出力する。
2. When 現在の Expression が指定 slot に `suppress=true` の OverlaySlotBinding を保持している場合、the OverlayInputSource shall 当該 slot に対して overlay を出力しない。
3. When 現在の Expression が指定 slot に対して default fallback (suppress=false、snapshot=null) の OverlaySlotBinding を保持している、または当該 slot のエントリを保持していない場合、the OverlayInputSource shall `FacialProfile.DefaultOverlays` 側のエントリを参照して解決する。
4. When `FacialProfile.DefaultOverlays` 側でも default fallback (suppress=false、snapshot=null) または該当 slot エントリ無しの場合、the OverlayInputSource shall 当該 slot に対して overlay を出力しない。
5. While 通常運用中、the OverlayInputSource shall per-frame のヒープアロケーションをゼロに維持する。
6. If 解決対象の slot 識別子が `FacialProfile.Slots` に存在しない場合、then the OverlayInputSource shall Unity 標準ログ (`Debug.LogWarning` 以上) で警告した上で当該 slot を出力対象から除外する。

### Requirement 4: JSON スキーマと Parser の新スキーマ対応
**Objective:** Unity エンジニアとして、JSON DTO / Parser / Converter を新スキーマに揃えたい。これにより JSON ファースト方針を維持しつつ、旧 `expressionId` フィールドを明確なエラーで拒否できる。

#### Acceptance Criteria
1. The ProfileSnapshotDto shall `slots` フィールド (文字列配列) を保持する。
2. The OverlaySlotBindingDto shall `{slot, suppress, snapshot: ExpressionSnapshotDto}` の 3 フィールド構造を保持する。
3. When `SystemTextJsonParser` が新スキーマの JSON を読み込んだ場合、the SystemTextJsonParser shall `FacialProfile.Slots` / `Expression.Overlays` / `FacialProfile.DefaultOverlays` をすべて新型で構築する。
4. If 入力 JSON の `OverlaySlotBinding` 相当オブジェクトに旧 `expressionId` フィールドが含まれていた場合、then the SystemTextJsonParser shall 当該フィールド名を含む明示的なエラーで読み込みを失敗させる。
5. If 入力 JSON の `OverlaySlotBinding` で `suppress=true` かつ `snapshot` が非 null の組み合わせが指定された場合、then the SystemTextJsonParser shall 矛盾エラーで読み込みを失敗させる。
6. The JsonSchemaDefinition shall 新スキーマ (slots フィールド + 新 OverlaySlotBinding 構造) を定義しドキュメント化する。
7. When `FacialCharacterProfileConverter` が `FacialCharacterProfileSO` と JSON 間で変換する場合、the FacialCharacterProfileConverter shall `_slots` および新 `OverlaySlotBindingSerializable` を双方向で正しく往復させる。
8. The SystemTextJsonParser shall 新スキーマ JSON を往復 (parse → serialize → parse) しても等価な FacialProfile を生成する。

### Requirement 5: ScriptableObject Serializable の新スキーマ対応
**Objective:** Unity エンジニアとして、SO Serializable を新スキーマに対応させたい。これにより Unity Inspector 上で AnimationClip 参照と cached snapshot を保持しつつ、Exporter がランタイム用 snapshot を再生成できる。

#### Acceptance Criteria
1. The OverlaySlotBindingSerializable shall `slot`、`suppress`、`AnimationClip` 参照、`cachedSnapshot` (ExpressionSnapshot シリアライズ表現) の 4 要素を保持する。
2. The FacialCharacterProfileSO shall `_slots` (List<string>) フィールドを保持し、Inspector で編集可能にする。
3. When `FacialCharacterProfileExporter` が SO をエクスポートする場合、the FacialCharacterProfileExporter shall 各 OverlaySlotBindingSerializable の AnimationClip をサンプリングし `cachedSnapshot` を更新する。
4. When OverlaySlotBindingSerializable が `suppress=true` の状態でエクスポートされる場合、the FacialCharacterProfileExporter shall AnimationClip サンプリングをスキップし `cachedSnapshot` を空相当として扱う。
5. When OverlaySlotBindingSerializable が default fallback (suppress=false かつ AnimationClip=null) の場合、the FacialCharacterProfileExporter shall 当該エントリの `cachedSnapshot` を空相当として扱う。
6. The FacialCharacterProfileExporter shall ランタイム asmdef に依存せず Editor 専用 asmdef のみで完結する。
7. If `FacialCharacterProfileSO._slots` に重複または `Expression.Overlays` / DefaultOverlays から参照されていない slot がある場合、then the FacialCharacterProfileExporter shall 警告ログを出力する (エクスポート自体は継続)。

### Requirement 6: Inspector UI の 6 タブ再編と Overlays 3 状態 UI
**Objective:** Unity エンジニアとして、FacialProfileSO Inspector を 6 タブ構成 (表情ライブラリ / レイヤー / ベース表情 / 目線 / Adapter Bindings / Debug) に再編したい。これにより Slots 宣言・Default Overlays・Expression ごとの Overlays 3 状態を表情ライブラリタブで一元編集できる。

#### Acceptance Criteria
1. The FacialProfileSO Inspector shall 「表情ライブラリ」「レイヤー」「ベース表情」「目線」「Adapter Bindings」「Debug」の 6 タブを表示する。
2. The FacialProfileSO Inspector shall 「レイヤー」タブと「表情ライブラリ」タブを完全に分離し、レイヤー定義の編集と表情ライブラリの編集を別タブに置く。
3. The 表情ライブラリタブ shall Slots 宣言セクション、Default Overlays セクション、Expression リストの 3 セクションを順に表示する。
4. The Slots 宣言セクション shall Slot 識別子文字列の追加 / 削除 / リネームを編集できる UI を提供する。
5. The Default Overlays セクション shall slot プルダウン (Slots 宣言から派生) と AnimationClip フィールドの組をリスト形式で編集できる UI を提供する。
6. The Expression リストの各 row shall Layer プルダウンと Overlays セクションを表示する。
7. The Overlays セクション shall Slots 宣言で定義された slot ごとに「Default / Suppress / 個別 AnimationClip」の 3 状態をラジオで切り替える UI を提供する。
8. When ユーザーが Overlays セクションで「個別 AnimationClip」を選択した場合、the Overlays セクション shall AnimationClip フィールドを表示し編集を許可する。
9. When ユーザーが Overlays セクションで「Default」または「Suppress」を選択した場合、the Overlays セクション shall AnimationClip フィールドを非表示または無効化し誤入力を防止する。
10. If Slots 宣言から削除された slot を Default Overlays または Expression.Overlays が参照している場合、then the FacialProfileSO Inspector shall 当該行に警告アイコンと文言を表示する。
11. The FacialProfileSO Inspector shall Adapter Bindings タブの `overlaySlot` プルダウンを Slots 宣言から動的に生成する。

### Requirement 7: Sample (MultiSourceBlendDemo) データの新スキーマ同期移行
**Objective:** Unity エンジニアとして、MultiSourceBlendDemo Sample のデータを新スキーマへ同期移行したい。これにより dev プロジェクトと UPM 配布の両方で新スキーマでの動作確認ができる。

#### Acceptance Criteria
1. The MultiSourceBlendDemo Sample shall `MultiSourceBlendDemoCharacter.asset` を新 `OverlaySlotBindingSerializable` 構造で保存する。
2. The MultiSourceBlendDemo Sample shall `Assets/StreamingAssets/.../profile.json` を新 JSON スキーマで保存する。
3. The MultiSourceBlendDemo Sample shall `Packages/com.hidano.facialcontrol/Samples~/.../profile.json` を新 JSON スキーマで保存する。
4. When 旧 `blink_overlay` (expressionId ベース) を新スキーマへ移行する場合、the MultiSourceBlendDemo Sample shall `smile` および `smile_closed_eye` Expression の Overlays に snapshot として焼き直す。
5. The MultiSourceBlendDemo Sample shall dev 用 `Assets/Samples/...` と UPM 用 `Packages/.../Samples~/...` の同名サンプルを同一内容で同期する。
6. When 新スキーマ Sample を Unity Editor で再生した場合、the MultiSourceBlendDemo Sample shall 旧来と同等の overlay 挙動 (smile 系 Expression で blink overlay が乗る) を再現する。
7. The MultiSourceBlendDemo Sample shall 旧 `expressionId` 参照を JSON / SO の双方から完全に除去する。

### Requirement 8: 後方互換非対応と旧フィールドの拒否
**Objective:** Unity エンジニアとして、preview.2 段階では後方互換を持たず、旧 `expressionId` フィールドを明確なエラーで拒否したい。これにより破壊的変更の意図をユーザーに明示し、移行漏れを早期に検出できる。

#### Acceptance Criteria
1. If 入力 JSON に旧 `expressionId` フィールドを含む `OverlaySlotBinding` 相当オブジェクトが存在する場合、then the SystemTextJsonParser shall 当該フィールド名と該当 slot を含むエラーメッセージで失敗する。
2. The OverlaySlotBinding 型 shall 旧 `expressionId` プロパティを公開しない。
3. The OverlaySlotBindingSerializable 型 shall 旧 `expressionId` フィールドを保持しない。
4. The FacialControl パッケージ shall 旧スキーマからの自動マイグレーション処理を提供しない。
5. The FacialControl パッケージ shall preview.2 リリースノートで旧 `expressionId` 廃止を破壊的変更として明示する。

### Requirement 9: OverlayInputSource の per-frame GC ゼロ目標の維持
**Objective:** Unity エンジニアとして、OverlayInputSource の per-frame GC アロケーションをゼロに維持したい。これにより VTuber 配信や 10 体以上の同時制御シナリオでもフレーム毎の GC スパイクを発生させない。

#### Acceptance Criteria
1. While 通常運用中、the OverlayInputSource shall per-frame の解決処理でヒープアロケーションをゼロにする。
2. The OverlayInputSource shall 解決処理で LINQ・boxing・無名メソッドキャプチャによる per-frame アロケーションを生じさせない。
3. The OverlayInputSource shall 解決対象 slot を列挙する際にコレクションの再アロケーションを発生させない。
4. The FacialControl テストスイート shall PlayMode 計測テストで OverlayInputSource の per-frame アロケーションが 0 byte であることを検証する。

### Requirement 10: ARKit / InputSystem / OSC 経路への影響限定
**Objective:** Unity エンジニアとして、新スキーマ移行による ARKit / 既存 InputSystem / OSC 経路への影響を `overlaySlot` 参照箇所のみに限定したい。これにより既存の入力 / 通信ロジックを安定維持できる。

#### Acceptance Criteria
1. The ARKit Adapter shall 新スキーマ移行に伴うロジック変更を `overlaySlot` 参照箇所のみに限定する。
2. The InputSystem Adapter shall 新スキーマ移行に伴うロジック変更を `overlaySlot` 参照箇所のみに限定する。
3. The OSC Adapter shall 新スキーマ移行に伴うロジック変更を `overlaySlot` 参照箇所のみに限定する。
4. When 既存の ARKit / InputSystem / OSC テストを実行した場合、the FacialControl テストスイート shall overlay 参照に関係しないテストが旧来通りに pass することを保証する。
5. The Adapter Bindings shall `overlaySlot` の解決先を `FacialProfile.Slots` の宣言一覧に限定する。

### Requirement 11: テストスイートの新スキーマ TDD 書き換えと Inspector UI テスト追加
**Objective:** Unity エンジニアとして、既存テストを新スキーマで TDD 書き換えし、Inspector UI 用 Edit Mode テストを追加したい。これにより破壊的変更の意図を Red-Green-Refactor サイクルでロックインし、UI 仕様も自動検証できる。

#### Acceptance Criteria
1. The FacialControl テストスイート shall `OverlaySlotBindingTests` を新 3 状態モデルで書き換え、default fallback / suppress / 個別 snapshot の各状態を検証する。
2. The FacialControl テストスイート shall `OverlayInputSourceTests` を snapshot 引きで書き換え、Expression 側 / DefaultOverlays 側それぞれの解決経路を検証する。
3. The FacialControl テストスイート shall `LayerUseCaseOverlayLayerTests` を新スキーマで書き換える。
4. The FacialControl テストスイート shall `LayerUseCaseAnalogOverlayTests` を新スキーマで書き換える。
5. The FacialControl テストスイート shall `LayerUseCaseAnalogExpressionAdditionTests` を新スキーマで書き換える。
6. The FacialControl テストスイート shall `ExpressionUseCaseActiveProviderTests` を新スキーマで書き換える。
7. The FacialControl テストスイート shall `SystemTextJsonParserOverlaysTests` を新スキーマで書き換え、旧 `expressionId` 拒否ケースおよび矛盾組み合わせ拒否ケースを含める。
8. The FacialControl テストスイート shall Inspector UI 用 Edit Mode テストを新規追加し、Overlays 3 状態切替 / 個別 AnimationClip 設定 / Slots 宣言整合性検証の 3 観点を網羅する。
9. When テストランナーを `-batchmode -nographics -testPlatform EditMode` で実行した場合、the FacialControl テストスイート shall 上記すべての書き換え / 追加テストを pass する。
10. The FacialControl テストスイート shall TDD 原則 (テストファースト・小さなステップ・FIRST 原則) に従って書き換える。
