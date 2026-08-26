# Implementation Plan

> Phase ordering follows design.md "Migration Strategy" (Phase 1 Domain → Phase 2 JSON DTO → Phase 3 Parser/Converter → Phase 4 SO Serializable → Phase 5 OverlayInputSource + Application tests → Phase 6 Inspector UI → Phase 7 Sample 移行 → Phase 8 Documentation)。各 sub-task は TDD (Red → Green) の順で並べ、test-first を徹底する。

## Phase 1: Domain 層（OverlaySlotBinding 3 状態モデル + Slots 真実化）

- [x] 1. Domain 層を新 overlay モデルへ刷新する

- [x] 1.1 InvalidSlotReference 値型を Domain に追加する
  - `InvalidLayerReference` 兄弟として `(Slot, Reason)` を保持する readonly struct を新設する
  - `Reason` は `"Duplicate"` / `"Undeclared"` の 2 種類のみ受け付け、`IEquatable` を実装する
  - 観測可能な完了条件: `Packages/com.hidano.facialcontrol/Runtime/Domain/Models/InvalidSlotReference.cs` がコンパイル成功し、空 EditMode テストでインスタンス化できること
  - _Requirements: 1.3, 1.4_
  - _Boundary: Domain.Models_

- [x] 1.2 OverlaySlotBindingTests を 3 状態モデルで Red 化する
  - 既存テスト `Tests/EditMode/Domain/OverlaySlotBindingTests.cs` を破棄し、(i) default fallback / (ii) suppress / (iii) snapshot override の 3 状態構築、(iv) `suppress=true && snapshot!=null` の `ArgumentException`、(v) `slot` 空文字での `ArgumentException`、(vi) Equals / GetHashCode の同値性を観測するテストへ書き直す
  - 旧 `(slot, expressionId)` ctor 参照と `ExpressionId` プロパティ参照を完全に除去する
  - 観測可能な完了条件: テストがコンパイルエラーで Red になり、新 API シグネチャを参照していること
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 8.2_
  - _Boundary: Tests/EditMode/Domain_

- [x] 1.3 OverlaySlotBinding を 3 状態 readonly struct に全置換する
  - `Packages/com.hidano.facialcontrol/Runtime/Domain/Models/OverlaySlotBinding.cs:1-74` を `(string Slot, bool Suppress, ExpressionSnapshot? Snapshot)` フィールド構造へ全置換する
  - ctor で `slot` 空文字と `Suppress=true && Snapshot.HasValue` を `ArgumentException` で拒否し、`IsDefaultFallback => !Suppress && !Snapshot.HasValue` を計算プロパティで提供する
  - 旧 `ExpressionId` プロパティと旧 ctor を型から完全削除する（preview.2 破壊的変更）
  - 観測可能な完了条件: 1.2 で書いた `OverlaySlotBindingTests` が全件 Green になること
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 8.2_
  - _Boundary: Domain.Models_

- [x] 1.4 FacialProfileSlotsTests を新規追加して Red 化する
  - 新規 `Tests/EditMode/Domain/FacialProfileSlotsTests.cs` を追加し、(i) `Slots` が空で初期化される、(ii) `Slots` 重複が `InvalidSlotReference("Duplicate")` として検出される、(iii) `Expression.Overlays[i].Slot` が `Slots` 未宣言なら `InvalidSlotReference("Undeclared")` を返す、(iv) `DefaultOverlays[i].Slot` が `Slots` 未宣言なら同じく `Undeclared` を返す、(v) `Slots` の防御的コピーが行われていることを観測する
  - `FacialProfile` ctor は named arg (`slots:`) で呼び出し、positional 呼出が破壊変更で fail しないようにする
  - 観測可能な完了条件: テストがコンパイルエラー or アサーション失敗で Red になり、新 ctor + `Slots` プロパティ + `ValidateSlotReferences` を参照していること
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_
  - _Boundary: Tests/EditMode/Domain_

- [x] 1.5 FacialProfile に Slots プロパティと ValidateSlotReferences を実装する
  - `Packages/com.hidano.facialcontrol/Runtime/Domain/Models/FacialProfile.cs:42-46, 131-140, 149-167, 173-200` を拡張し、ctor に `string[] slots` 引数を追加する（既存呼び出しは named arg 強制で更新）
  - `Slots: ReadOnlyMemory<string>` プロパティを公開し、防御的コピー (`Array.Empty<string>()` 正規化を含む) を行う
  - `ValidateSlotReferences()` メソッドで Slots 重複と Overlays/DefaultOverlays の未宣言 slot を検出して `IReadOnlyList<InvalidSlotReference>` を返す
  - 観測可能な完了条件: 1.4 の `FacialProfileSlotsTests` が全件 Green になること
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_
  - _Boundary: Domain.Models_

- [x] 1.6 Expression / FacialProfile の XML doc を新セマンティクスに更新する
  - `Runtime/Domain/Models/Expression.cs:69-77, 196-214` の `Overlays` / `TryGetOverlay` の XML doc を「`expressionId` が空 / 非空」表現から「`Suppress` / `Snapshot` 状態」表現へ更新する
  - `Runtime/Domain/Models/FacialProfile.cs` の `DefaultOverlays` / `TryGetDefaultOverlay` の XML doc も同様に更新する
  - API シグネチャは無変更とする（doc のみ変更）
  - 観測可能な完了条件: doc 更新後も全 Domain テストが Green を維持し、`grep "expressionId"` が Domain 配下で 0 件になること
  - _Requirements: 2.7, 8.2_
  - _Boundary: Domain.Models_

## Phase 2: JSON DTO 層（slots フィールド + 新 OverlaySlotBindingDto）

- [x] 2. JSON DTO を新スキーマへ刷新する

- [x] 2.1 (P) OverlaySlotBindingDto を新 3 フィールド構造に全置換する
  - `Packages/com.hidano.facialcontrol/Runtime/Adapters/Json/Dto/OverlaySlotBindingDto.cs:1-21` から `expressionId: string` を完全削除し、`suppress: bool` と `snapshot: ExpressionSnapshotDto` を追加する
  - `[Serializable]` クラスを維持し、`JsonUtility` で読み書き可能な形に保つ
  - 観測可能な完了条件: 新フィールドのみで `JsonUtility.ToJson` が `{"slot":"blink","suppress":false,"snapshot":{...}}` 形式の文字列を返すこと
  - _Requirements: 4.2, 4.4, 8.1_
  - _Boundary: Adapters.Json.Dto_

- [x] 2.2 (P) ProfileSnapshotDto に slots フィールドを追加する
  - `Packages/com.hidano.facialcontrol/Runtime/Adapters/Json/Dto/ProfileSnapshotDto.cs:1-43` に `slots: List<string>` を追加する（既存 `defaultOverlays`, `expressions`, `gazeConfigs` と同階層）
  - 既存フィールドの型・名称は無変更とする
  - 観測可能な完了条件: `JsonUtility.FromJson<ProfileSnapshotDto>` が `slots` キーを含む JSON を読み取り、`slots: null` の場合に後続正規化で空 List 化できる足場が整うこと
  - _Requirements: 4.1_
  - _Boundary: Adapters.Json.Dto_

- [x] 2.3 (P) JsonSchemaDefinition の定数と SampleProfileJson を新スキーマへ更新する
  - `Runtime/Adapters/Json/JsonSchemaDefinition.cs:144-152, 22-46` から `Profile.OverlaySlot.ExpressionId` 定数を削除し、`Suppress = "suppress"` / `Snapshot = "snapshot"` を追加する
  - `Profile.Slots = "slots"` 定数を新規追加する
  - `SampleProfileJson` (lines 292-357) を新スキーマで全置換し、`smile` の overlays 配列に snapshot inline、`smile_closed_eye` の overlays に suppress=true、`defaultOverlays` を新 3 フィールド構造で記述する
  - 観測可能な完了条件: 定数参照箇所が新名称で解決し、`SampleProfileJson` が design.md "Data Contracts & Integration" の JSON サンプルと一致すること
  - _Requirements: 4.6_
  - _Boundary: Adapters.Json_

## Phase 3: Parser / Converter（旧フィールド拒否 + 双方向変換）

- [x] 3. SystemTextJsonParser と FacialCharacterProfileConverter を新スキーマへ刷新する

- [x] 3.1 SystemTextJsonParserOverlaysTests を新スキーマで Red 化する
  - 既存 `Tests/EditMode/Adapters/Json/SystemTextJsonParserOverlaysTests.cs` を破棄し、新スキーマ用テストへ書き直す
  - 必須テストケース: (i) 旧 `expressionId` フィールドを含む legacy JSON が `FormatException` で拒否される (`Parse_LegacyExpressionIdField_ThrowsFormatException`)、(ii) `gaze_configs[].expressionId` は維持され throw しない (`Parse_GazeConfigsExpressionId_DoesNotThrow`)、(iii) `suppress=true && snapshot!=null` 矛盾組み合わせが `FormatException` で拒否される、(iv) 3 状態 (default fallback / suppress / override) の round-trip 等価性、(v) `Slots` 欠落時に空配列として正規化される、(vi) `SampleProfileJson` がパース可能で round-trip 等価
  - 観測可能な完了条件: テストがコンパイルエラー or アサーション失敗で Red になり、新 Parser API を参照していること
  - _Requirements: 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 8.1_
  - _Boundary: Tests/EditMode/Adapters/Json_

- [x] 3.2 LegacyOverlayFieldDetector (RejectLegacyExpressionIdInOverlays) を実装する
  - design.md §D5 / "JSON パース時の旧フィールド拒否フロー" のアルゴリズムに基づき、文字列レベル状態機械で `defaultOverlays[N]` および `expressions[N].snapshot.overlays[N]` 直下の `"expressionId"` キーを検出する内部静的クラスを新設する
  - `gaze_configs[N]` / `_gazeConfigs[N]` / `expressions[N].snapshot.id` 等の他スコープは黒リスト化する
  - エラーメッセージは `"Legacy field 'expressionId' detected in OverlaySlotBinding scope (path={path})"` 形式で path を含める
  - escape 文字 (`\"`) を skip し、JSON 値内の偽陽性を防ぐ
  - 観測可能な完了条件: 3.1 の `Parse_LegacyExpressionIdField_ThrowsFormatException` と `Parse_GazeConfigsExpressionId_DoesNotThrow` が Green になること
  - _Requirements: 4.4, 8.1_
  - _Boundary: Adapters.Json_

- [x] 3.3 SystemTextJsonParser の Convert/Build メソッド群を新型化する
  - `Runtime/Adapters/Json/SystemTextJsonParser.cs:619-628, 630-644, 842-855` の `ConvertToProfile` / `ConvertOverlaySlotBindings` / `BuildOverlaySlotBindingDtoList` を新型 (`OverlaySlotBindingDto` の suppress + snapshot) へ書き換える
  - `NormalizeProfileSnapshotDto` (lines 103-128) で `dto.slots == null` を `new List<string>()` に正規化する
  - `Parse(string json)` のエントリで 3.2 の `RejectLegacyExpressionIdInOverlays(json)` を呼び出す
  - DTO → Domain 変換時に `suppress=true && snapshot!=null` を検出したら `FormatException` で fail する (Domain ctor 拒否前にメッセージを差別化するため)
  - `ConvertToProfile` で `dto.slots` を `FacialProfile` ctor の named arg (`slots:`) で渡す
  - 観測可能な完了条件: 3.1 のテストが全件 Green になり、`SampleProfileJson` の round-trip が等価 FacialProfile を返すこと
  - _Requirements: 4.3, 4.4, 4.5, 4.7, 4.8, 8.1_
  - _Boundary: Adapters.Json_

- [x] 3.4 FacialCharacterProfileConverter を新スキーマで双方向変換に対応させる
  - `Runtime/Adapters/ScriptableObject/Serializable/FacialCharacterProfileConverter.cs:17-42, 167-213, 202-213, 229-241` を更新し、`ToFacialProfile` シグネチャに `IReadOnlyList<string> slots` 引数を追加する（named arg 強制）
  - `ConvertOverlays` を新型化: `serializable.suppress=true` → `OverlaySlotBinding(slot, suppress: true, snapshot: null)`、`suppress=false && cachedSnapshot.IsEmpty()` → default fallback、`suppress=false && cachedSnapshot 有効` → `cachedSnapshot` を `ExpressionSnapshot` に再構築して inline
  - 逆方向 (Domain → DTO) は `OverlaySlotBindingDto` を出力時に `Snapshot` を Dto 化する既存ヘルパパターン (`ConvertSnapshotBlendShapes`) を流用する
  - 観測可能な完了条件: SO → Domain → DTO → JSON → DTO → Domain → SO の往復で 3 状態すべてが等価に保たれること（テストは Phase 4 / 5 で観測）
  - _Requirements: 4.7, 5.1, 5.2_
  - _Boundary: Adapters.ScriptableObject.Serializable_

## Phase 4: SO Serializable + Exporter

- [x] 4. SO Serializable と FacialCharacterProfileExporter を新スキーマへ刷新する

- [x] 4.1 (P) OverlaySlotBindingSerializable を新 4 フィールド構造に全置換する
  - `Runtime/Adapters/ScriptableObject/Serializable/OverlaySlotBindingSerializable.cs:1-19` から `expressionId` を完全削除し、`suppress: bool`, `animationClip: AnimationClip`, `cachedSnapshot: ExpressionSnapshotDto` を追加する
  - Tooltip を新セマンティクス (`"suppress=true で AnimationClip と cachedSnapshot は無視されます"`) に更新する
  - design.md "OverlaySlotBindingSerializable Implementation Notes" の `OverlaySlotBindingState` enum (`DefaultFallback` / `Suppress` / `Override`) と `OverlaySlotBindingSerializableExtensions.GetState()` 拡張メソッドを同じ asmdef 内に追加する（Inspector UI が再利用するため Adapter 層に置く）
  - 観測可能な完了条件: 旧 .asset を読み込んだとき `expressionId` キーが silent drop され、新フィールドが既定値で deserialize されること、`GetState()` が 3 状態を正しく返すこと
  - _Requirements: 5.1, 8.3_
  - _Boundary: Adapters.ScriptableObject.Serializable_

- [x] 4.2 (P) FacialCharacterProfileSO に _slots フィールドを追加する
  - `Runtime/Adapters/ScriptableObject/FacialCharacterProfileSO.cs:18-25, 56-60` に `[SerializeField] private List<string> _slots = new();` を追加し、`public IReadOnlyList<string> Slots => _slots;` プロパティを公開する
  - `BuildFallbackProfile()` で `_slots` を `FacialCharacterProfileConverter.ToFacialProfile` の named arg (`slots:`) で渡す
  - 観測可能な完了条件: `FacialCharacterProfileSO` の Inspector に `_slots` フィールドが表示され、`Slots` プロパティが空 List で初期化されること
  - _Requirements: 5.2, 1.5_
  - _Boundary: Adapters.ScriptableObject_

- [x] 4.3 FacialCharacterProfileExporter で overlay clip サンプリングと slots 出力を統合する
  - `Editor/AutoExport/FacialCharacterProfileExporter.cs:40-68, 151-237` の `SampleAnimationClipsIntoCachedSnapshots` に overlays 走査を追加する: 各 Expression の `overlays[]` と `_defaultOverlays[]` をループし、`suppress=true` なら `cachedSnapshot` を空相当でクリア、`animationClip != null && !suppress` なら `AnimationClipSampler.SampleSnapshot` で `cachedSnapshot` を更新、`animationClip == null && !suppress` なら default fallback として `cachedSnapshot` を空相当でクリア
  - `BuildProfileSnapshotDto` で `dto.slots = SO._slots` を出力し、`dto.defaultOverlays` を新スキーマで出力する（research.md (b) Req 5 で指摘された defaultOverlays 出力欠落 Bug も同時修正）
  - `_slots` 重複 / Expression.Overlays / DefaultOverlays から参照されていない slot を検出した場合は `Debug.LogWarning` を出力する（エクスポート自体は継続）
  - Exporter は Editor asmdef のみで完結し、Runtime asmdef には依存しないこと（既存契約維持）
  - 観測可能な完了条件: SO に AnimationClip 付き overlay を設定して Exporter を実行したとき `cachedSnapshot.blendShapes` が AnimationClip サンプリング結果で埋まり、出力 JSON に新スキーマの `slots` と `defaultOverlays` が含まれること
  - _Requirements: 5.3, 5.4, 5.5, 5.6, 5.7_
  - _Boundary: Editor.AutoExport_

## Phase 5: OverlayInputSource snapshot 引き + Application テスト

- [x] 5. OverlayInputSource を snapshot 引きへ書き換え、Application UseCase テストを Green 化する

- [x] 5.1 OverlayInputSourceTests を snapshot 引きで Red 化する
  - 既存 `Tests/EditMode/Adapters/InputSources/OverlayInputSourceTests.cs` を破棄し、(i) Expression が個別 snapshot を持つ場合に当該 snapshot が出力される (Req 3.1)、(ii) Expression が suppress を持つ場合に出力されない (Req 3.2)、(iii) Expression が default fallback で DefaultOverlays に snapshot がある場合に DefaultOverlays から解決される (Req 3.3)、(iv) DefaultOverlays も default / なしの場合に出力されない (Req 3.4)、(v) `slot` が `Profile.Slots` に未宣言の場合に ctor で `Debug.LogWarning` が 1 度だけ出る (Req 3.6) の 5 経路を観測する
  - `StubActiveProvider` を使って active expression を切り替え、`Span<float> output` への書き込み内容を assertion する
  - 観測可能な完了条件: テストがコンパイルエラー or アサーション失敗で Red になり、新 ctor シグネチャと snapshot 引き API を参照していること
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.6_
  - _Boundary: Tests/EditMode/Adapters/InputSources_

- [x] 5.2 OverlayInputSource を _resolvedBySlot + ResolvedSnapshot 構造へ全置換する
  - `Runtime/Adapters/InputSources/OverlayInputSource.cs:1-227` の `_resolvedById: Dictionary<string, ResolvedExpression>` を `_resolvedBySlot: Dictionary<SlotKey, ResolvedSnapshot>` 構造へ置換する
  - `internal readonly struct SlotKey : IEquatable<SlotKey>` を `(string ExpressionId, string Slot)` で定義し、`ExpressionId == null` を DefaultOverlays sentinel として再利用する。`GetHashCode` は `Slot` 主導でキャッシュし boxing 回避
  - `internal readonly struct ResolvedSnapshot` を `(bool Suppress, bool HasSnapshot, int[] Indices, float[] Values, BitArray Mask)` で定義し、Nullable<T> を Dictionary value にしない (boxing 回避、§D2)
  - ctor 時に Expression × slot と DefaultOverlays × slot の override / suppress エントリを事前展開し、毎フレームは Dictionary lookup + 配列直書きで完結させる
  - `slot` が `profile.Slots.Span` 線形検索で未ヒットなら `_logged` フラグ付きで `Debug.LogWarning` を 1 度だけ出力し、`_resolvedBySlot` を空のまま保持する
  - `TryWriteValues(Span<float> output)` 内で LINQ / boxing / `new` / `ToArray` を一切行わず、`BitArray.SetAll(false)` のみで mask を初期化する
  - `Profiler.BeginSample("OverlayInputSource.TryWriteValues")` を `TryWriteValues` のエントリに挟む
  - 観測可能な完了条件: 5.1 の `OverlayInputSourceTests` が全件 Green になり、4 解決経路と未宣言 slot の警告が観測されること
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 9.1, 9.2, 9.3_
  - _Boundary: Adapters.InputSources_

- [x] 5.3 OverlayInputSourcePerformanceTests を新規追加して GC ゼロを検証する
  - `Tests/PlayMode/Performance/OverlayInputSourcePerformanceTests.cs` を新規追加する
  - design.md "PlayMode Performance テスト方法論" に従い、`GC.GetTotalAllocatedBytes(true)` 差分計測を用いる: warm-up 10 フレーム → 1000 フレーム連続 `TryWriteValues` 後の差分が 0 byte であることを assertion する
  - `Unity.PerformanceTesting` は採用せず、.NET 標準 API のみで計測する (steering tech.md "JSON パースは JsonUtility ベース" 方針と整合し、新規パッケージ依存を増やさない)
  - 観測可能な完了条件: PlayMode テストランナー (`-batchmode -nographics -testPlatform PlayMode`) で `TryWriteValues_1000Frames_AllocatesZeroBytes` が Green になること
  - _Requirements: 9.1, 9.2, 9.3, 9.4_
  - _Boundary: Tests/PlayMode/Performance_

- [x] 5.4 LayerUseCaseOverlayLayerTests を新スキーマで書き換える
  - `Tests/EditMode/Application/LayerUseCaseOverlayLayerTests.cs` を新スキーマで書き換える
  - 旧 `OverlaySlotBinding(slot, expressionId)` 呼出と `blink_overlay` 中間 Expression 参照を撤去し、`smile` / `smile_closed_eye` の `overlays` に snapshot inline / suppress を持たせるシナリオに置換する
  - `BuildProfile` ヘルパは `slots:` named arg を追加して new ctor シグネチャに揃える
  - 観測可能な完了条件: テストが新スキーマで Green になり、blink overlay レイヤーが smile 系 Expression で出力されることが観測できること
  - _Requirements: 11.3_
  - _Boundary: Tests/EditMode/Application_

- [x] 5.5 (P) LayerUseCaseAnalogOverlayTests を新スキーマで書き換える
  - `Tests/EditMode/Application/LayerUseCaseAnalogOverlayTests.cs` を 5.4 と同じ移行手順で書き換える
  - アナログ入力経路で blink overlay が smile 系 Expression に乗ることを観測する
  - 観測可能な完了条件: テストが新スキーマで Green になること
  - _Requirements: 11.4_
  - _Boundary: Tests/EditMode/Application_
  - _Depends: 5.2_

- [x] 5.6 (P) LayerUseCaseAnalogExpressionAdditionTests を新スキーマで書き換える
  - `Tests/EditMode/Application/LayerUseCaseAnalogExpressionAdditionTests.cs` を 5.4 と同じ移行手順で書き換える
  - アナログ Expression 追加経路で overlay が正しく統合されることを観測する
  - 観測可能な完了条件: テストが新スキーマで Green になること
  - _Requirements: 11.5_
  - _Boundary: Tests/EditMode/Application_
  - _Depends: 5.2_

- [x] 5.7 (P) ExpressionUseCaseActiveProviderTests を新スキーマで書き換える
  - `Tests/EditMode/Application/ExpressionUseCaseActiveProviderTests.cs` の overlay 経路への参照のみ新型 (`OverlaySlotBinding(slot, suppress, snapshot)`) に置換する
  - 当該 UseCase の overlay 以外のロジックは無変更を維持する
  - 観測可能な完了条件: テストが新スキーマで Green になり、active provider 切り替え時の overlay 解決が期待通りに動くこと
  - _Requirements: 11.6_
  - _Boundary: Tests/EditMode/Application_
  - _Depends: 5.2_

- [x] 5.8 InputSystemAdapterBinding に Slots 検証を追加する
  - `Packages/com.hidano.facialcontrol.inputsystem/Runtime/Adapters/AdapterBindings/InputSystemAdapterBinding.cs:440-510` の `BuildOverlaySources` 内で `entry.overlaySlot` を `ctx.Profile.Slots.Span` で線形検索し、未ヒットなら `Debug.LogWarning` + 当該エントリを skip する
  - 既存ロジック (源 ID 構築、blendShapeCount 等) は無変更を維持する
  - 観測可能な完了条件: 既存の InputSystem 系テストがすべて Green を維持し、未宣言 slot 入力時に warning が観測されること
  - _Requirements: 10.1, 10.2, 10.4, 10.5, 1.5_
  - _Boundary: InputSystem.Adapters.AdapterBindings_

## Phase 6: Inspector UI 6 タブ再編 + Overlays 3 状態 UI

- [x] 6. Inspector UI を 6 タブ + Overlays 3 状態 UI へ再編する

- [x] 6.1 OverlaysTabUITests を新規追加して Red 化する
  - `Tests/EditMode/Editor/Inspector/OverlaysTabUITests.cs` を新規追加し、(i) Overlays セクションの 3 状態ラジオが `OverlaySlotBindingSerializable.GetState()` の値と一致する、(ii) "Override" 選択時のみ AnimationClipField が visible になる (Req 6.8 / 6.9)、(iii) ラジオ切替で `serializable.suppress` / `animationClip` / `cachedSnapshot` が期待通りに更新される、(iv) Slots 宣言から削除された slot を参照する row に警告 HelpBox が表示される (Req 6.10)、(v) `_slots` 編集後に Default Overlays セクションの DropdownField choices が再生成される (Req 6.11) を観測する
  - `ScriptableObject.CreateInstance<FacialCharacterProfileSO>()` で in-memory SO を作成し、`Editor.CreateEditor(so)` 経由で Inspector の `CreateInspectorGUI()` 戻り値の VisualElement を取得して `Q<RadioButtonGroup>(name)` / `Q<DropdownField>(name)` / `Q<HelpBox>` で要素検索する
  - 観測可能な完了条件: テストがコンパイルエラー or アサーション失敗で Red になり、新タブ ID 定数と新セクションビルダ名を参照していること
  - _Requirements: 6.7, 6.8, 6.9, 6.10, 6.11, 11.8_
  - _Boundary: Tests/EditMode/Editor/Inspector_

- [x] 6.2 FacialCharacterProfileSOInspector を 6 タブ構成に再編する
  - `Editor/Inspector/FacialCharacterProfileSOInspector.cs:47-50, 178-205` のタブ ID 定数を 6 個 (`TabExpressionLibraryName`, `TabLayersName`, `TabBaseExpressionName`, `TabGazeName`, `TabAdapterBindingsName`, `TabDebugName`) に再定義する
  - 既存の `BuildLayersSection` (line 973) を `TabLayersName` タブへ、`BuildBaseExpressionSection` (line 408) を `TabBaseExpressionName` タブへ移植する（中身の挙動は無変更、配置のみ変更）
  - `OnBuildPreLayersSections` hook (line 241) は維持し、派生 Inspector (InputSystem 用) への影響は revalidation trigger としてドキュメント化する（本 spec の Boundary 外）
  - 観測可能な完了条件: SO Inspector を Editor で開いたとき 6 タブが表示され、レイヤー / ベース表情 / 表情ライブラリが分離されていること (Req 6.1, 6.2)
  - _Requirements: 6.1, 6.2_
  - _Boundary: Editor.Inspector_

- [x] 6.3 表情ライブラリタブに Slots 宣言セクションを実装する
  - `BuildSlotsDeclarationSection(VisualElement root)` を新規実装し、`_layerNameChoices` パターン (`FacialCharacterProfileSOInspector.cs:142, 1020`) を踏襲して `_slotNameChoices` 動的候補リストを生成する
  - `BuildArrayListView<string>(_slotsProperty, allowAdd, allowRemove, onRename)` で Slot 識別子文字列の追加 / 削除 / リネーム UI を提供する
  - `_slotsProperty` を `TrackPropertyValue` で監視し、変更検知時に `RefreshSlotNameChoices()` で全 DropdownField の choices を再設定する
  - 観測可能な完了条件: SO Inspector で Slot 識別子を追加 / 削除 / リネームでき、Default Overlays セクションと Expression row Overlays セクションの slot プルダウンが即座に追従すること (Req 6.4)
  - _Requirements: 6.3, 6.4_
  - _Boundary: Editor.Inspector_

- [x] 6.4 表情ライブラリタブに Default Overlays セクションを実装する
  - `BuildDefaultOverlaysSection(VisualElement root)` を新規実装し、slot プルダウン × AnimationClip フィールドの組をリスト形式で編集できる UI を提供する
  - 各 row で `OverlaySlotBindingSerializableExtensions.GetState()` を使って 3 状態ラジオの初期値を判定する
  - 観測可能な完了条件: SO Inspector の表情ライブラリタブに Default Overlays セクションが Slots 宣言セクションの直下に表示され、追加 / 削除 / 編集が可能なこと (Req 6.5)
  - _Requirements: 6.3, 6.5_
  - _Boundary: Editor.Inspector_

- [x] 6.5 Expression row に Layer プルダウンと Overlays 3 状態 UI を実装する
  - `BuildExpressionRow(int exprIndex)` (line 1306) を拡張し、Layer プルダウン (`_layerNameChoices` 流用) と Overlays セクションを各 row に追加する
  - `BuildOverlaysSectionForExpression(SerializedProperty overlaysProp, int exprIndex)` を新規実装する
  - 各 declared slot の row に `RadioButtonGroup ["Default", "Suppress", "Override"]` と `ObjectField<AnimationClip>` を配置し、ラジオ選択値に応じて以下のハンドラを呼ぶ:
    - `Default` → `serializable.suppress = false; serializable.animationClip = null; cachedSnapshot.Clear();` AnimationClipField を `style.display = None`
    - `Suppress` → `serializable.suppress = true; serializable.animationClip = null; cachedSnapshot.Clear();` AnimationClipField を `style.display = None`
    - `Override` → `serializable.suppress = false;` AnimationClipField を `style.display = Flex`
  - 初期値判定は `OverlaySlotBindingSerializableExtensions.GetState()` で行う
  - Slots 宣言から削除された slot を参照している row には `HelpBox(MessageType.Warning)` を先頭に表示する (Req 6.10)
  - 観測可能な完了条件: 6.1 の `OverlaysTabUITests` が全件 Green になり、3 状態切替・AnimationClip 表示制御・警告表示が動作すること
  - _Requirements: 6.6, 6.7, 6.8, 6.9, 6.10_
  - _Boundary: Editor.Inspector_

- [x] 6.6 InputSystemAdapterBindingDrawer の overlaySlot を DropdownField 化する
  - `Packages/com.hidano.facialcontrol.inputsystem/Editor/AdapterBindings/InputSystemAdapterBindingDrawer.cs:288, 363-376` の `PropertyField` (TextField) を `DropdownField` に置換する
  - choices は `property.serializedObject.targetObject as FacialCharacterProfileSO` を辿って `so.Slots` から動的取得する
  - SO 取得失敗時は HelpBox `"FacialCharacterProfileSO の Slots を先に宣言してください"` を表示し、dropdown を disabled にする
  - `_slots` 変更検知は `schedule.Execute(() => RefreshChoices()).Every(intervalMs)` で polling する (§D4 Decision Log)
  - choices 配列は `_slots` 内容変更時のみ `Equals` 比較で再生成し、no-op skip で GC を抑制する
  - 観測可能な完了条件: Adapter Bindings タブで `overlaySlot` がドロップダウン表示され、SO の `_slots` を編集すると即時に選択肢が更新されること (Req 6.11)
  - _Requirements: 6.11, 10.5_
  - _Boundary: InputSystem.Editor.AdapterBindings_

## Phase 7: Sample データ移行

- [x] 7. MultiSourceBlendDemo Sample を新スキーマへ同期移行する

- [x] 7.1 SampleAssetsAreInSyncTests を新規追加して dev/Samples~ drift を検出する
  - `Tests/EditMode/Editor/Inspector/SampleAssetsAreInSyncTests.cs` を新規追加する
  - (i) `Assets/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json` と `Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json` のテキスト一致を assertion する
  - (ii) `Assets/Samples/.../MultiSourceBlendDemoCharacter.asset` に `blink_overlay` Expression が残っていないことを検出する (移行漏れ shortcut detection)
  - (iii) `Assets/Samples/...` の .asset と `Packages/.../Samples~/...` の .asset が存在する場合の YAML key set 一致を比較する
  - 観測可能な完了条件: テストが追加された時点で 7.2 / 7.3 / 7.4 完了前は Red、Phase 7 完了後に Green になること (R1 Mitigation)
  - _Requirements: 7.5_
  - _Boundary: Tests/EditMode/Editor/Inspector_

- [x] 7.2 dev 側 profile.json (StreamingAssets) を新スキーマへ移行する
  - `FacialControl/Assets/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json:140-348` を編集する
  - (i) ルートに `"slots": ["blink"]` を追加する
  - (ii) `expressions[]` から `blink_overlay` Expression エントリ (lines 145-225) を削除する
  - (iii) `defaultOverlays[]` (lines 343-348) を新スキーマ `[{slot: "blink", suppress: false, snapshot: {...}}]` へ書き換え、旧 `blink_overlay` の snapshot を inline する
  - (iv) `expressions[smile_closed_eye].snapshot.overlays` を `[{slot: "blink", suppress: true, snapshot: null}]` に書き換える
  - (v) `expressions[smile].snapshot.overlays` を空配列または default fallback `[{slot: "blink", suppress: false, snapshot: null}]` に書き換える (DefaultOverlays から解決させる場合)
  - 旧 `expressionId` 参照を JSON から完全に除去する
  - 観測可能な完了条件: SystemTextJsonParser でこの JSON を読み込んだとき例外なく FacialProfile が構築でき、`blink_overlay` Expression が存在しないこと (Req 7.7)
  - _Requirements: 7.2, 7.4, 7.7_
  - _Boundary: Sample.StreamingAssets_

- [x] 7.3 Samples~ 側 profile.json を dev 側と同期する
  - `Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json:1-139` を 7.2 と同一内容で書き換える
  - canonical 配布なので drift 厳禁 (CLAUDE.md "Samples の二重管理ルール" 遵守)
  - 観測可能な完了条件: 7.1 の `SampleAssetsAreInSyncTests` の profile.json テキスト一致テストが Green になること (Req 7.3, 7.5)
  - _Requirements: 7.3, 7.5, 7.7_
  - _Boundary: Sample.Packages.Samples~_

- [x] 7.4 MultiSourceBlendDemoCharacter.asset を新スキーマへ移行する
  - `FacialControl/Assets/Samples/FacialControl InputSystem/0.1.0-preview.2/Multi Source Blend Demo/MultiSourceBlendDemoCharacter.asset:38-228` を編集する
  - (i) `_slots: ["blink"]` を追加する
  - (ii) `blink_overlay` Expression を `_expressions[]` から削除する
  - (iii) `_defaultOverlays[0]` を `(slot: "blink", suppress: false, animationClip: <旧 blink_overlay の AnimationClip 参照>, cachedSnapshot: <Exporter 再生成>)` に書き換える
  - (iv) `_expressions[smile].overlays[0]` を `(slot: "blink", suppress: false, animationClip: null, cachedSnapshot: null)` (default fallback) または override に書き換える
  - (v) `_expressions[smile_closed_eye].overlays[0]` を `(slot: "blink", suppress: true, animationClip: null, cachedSnapshot: null)` に書き換える
  - (vi) Adapter Bindings の `overlaySlot: blink` (lines 270-274 の RightTrigger entry) は文字列値のまま維持する
  - 観測可能な完了条件: SO Inspector で表情ライブラリタブを開いたとき 6 タブ構成で `blink_overlay` が存在せず、smile / smile_closed_eye の Overlays セクションが期待通りの 3 状態を表示すること (Req 7.1, 7.7)
  - _Requirements: 7.1, 7.4, 7.7_
  - _Boundary: Sample.Assets_

- [x] 7.5 Sample 起動確認と Samples~ 側 .asset 同期
  - Unity Editor で `MultiSourceBlendDemo` Scene を開いて再生し、`smile` 表情選択時に blink overlay が乗る、`smile_closed_eye` 選択時に blink overlay が抑制されることを確認する (Req 7.6)
  - `Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/MultiSourceBlendDemoCharacter.asset` が存在する場合は dev 側 .asset と同期する
  - 7.1 の `SampleAssetsAreInSyncTests` を実行し、すべて Green であることを確認する
  - 観測可能な完了条件: Sample 再生で旧来と同等の overlay 挙動が再現され、`SampleAssetsAreInSyncTests` が Green になること
  - _Requirements: 7.5, 7.6_
  - _Boundary: Sample.Validation_

## Phase 8: Documentation

- [x] 8. preview.2 リリース向けドキュメントを更新する
  - `Packages/com.hidano.facialcontrol/CHANGELOG.md` の preview.2 セクションに「OverlaySlotBinding 3 状態モデルへの破壊的変更」「旧 `expressionId` フィールド廃止」「FacialProfile.Slots 追加」「Inspector 6 タブ再編」を明示する
  - `Packages/com.hidano.facialcontrol/README.md` (overlay 章) を新スキーマ (snapshot ベース) へ更新する
  - 全 EditMode テスト (`-batchmode -nographics -testPlatform EditMode`) と PlayMode テスト (`-testPlatform PlayMode`) を実行し、書き換え / 追加した全テストが pass することを確認する
  - 観測可能な完了条件: CHANGELOG / README が新スキーマを反映し、EditMode + PlayMode テスト結果がすべて Green になること (Req 8.5, 11.9, 11.10)
  - _Requirements: 8.4, 8.5, 11.9, 11.10_
  - _Boundary: Documentation, Test Validation_

## Phase 9: Inspector UI 表情ライブラリ昇格と Default Overlays 簡素化（spec 違反修正）

> 既存 Inspector 実装が Req 6.2 / 6.3 / 6.5 の Acceptance Criteria を満たしていないことが事後判明したため、追加で対応する。新規要件追加ではなく既存要件への適合作業（T44〜T48）。
>
> 違反内容:
> - Req 6.3 違反: 表情ライブラリタブが Slots 宣言 + Default Overlays の 2 セクション構成で、Expression リストが欠落している。Expression は依然として `BuildLayersSection` 内でレイヤーカードに埋め込まれたまま。
> - Req 6.2 違反: レイヤータブと表情ライブラリタブが Expression リストを完全に分離できていない。
> - Req 6.5 違反: Default Overlays セクションに 3 状態ラジオ (`Default / Suppress / Override`) が混入。spec は「slot プルダウン + AnimationClip フィールドのみ」を要求している。意味論的にも DefaultOverlays では「Default fallback」と「Suppress」が同じ runtime 結果（overlay 出力なし）になり redundant。

- [x] 9. Inspector UI を Req 6.2 / 6.3 / 6.5 に適合させる

- [x] 9.1 (T44) 表情ライブラリタブに Expression リストセクションを新設する
  - `Editor/Inspector/FacialCharacterProfileSOInspector.cs:204-211` の `CreateInspectorGUI` 内で、表情ライブラリタブの `BuildDefaultOverlaysSection` 呼び出しの直後に `BuildExpressionLibrarySection(expressionLibraryTab.contentContainer)` を追加する
  - `BuildExpressionLibrarySection(VisualElement root)` を新規実装し、全 Expression を所属レイヤーに依らずフラットに列挙する単一リストとして構築する
  - 各 row は既存の `BuildExpressionRow(int exprIndex)` を流用するが、Layer プルダウン (`ExpressionRowLayerDropdownName`) を必ず表示する（Req 6.6）。`_layerNameChoices` を choices として動的生成する
  - 新規 Expression 追加ボタンを Section 末尾に配置し、デフォルト所属レイヤーは `emotion`（未定義の場合は最初のレイヤー）にフォールバックする
  - `_layersProperty` を `TrackPropertyValue` で監視し、レイヤー追加/削除/リネーム時に全 row の Layer プルダウン choices を即時更新する
  - 観測可能な完了条件: SO Inspector の表情ライブラリタブを開いたとき、Slots 宣言 / Default Overlays / Expression リストの 3 セクションが順に表示され、Expression リストには全レイヤーの Expression が混在表示されること (Req 6.3)
  - _Requirements: 6.3, 6.6_
  - _Boundary: Editor.Inspector_

- [x] 9.2 (T45) レイヤータブから Expression リスト構築を撤去する
  - `Editor/Inspector/FacialCharacterProfileSOInspector.cs:1533-1535` 周辺の `BuildLayersSection` 内で、各レイヤーカードに対する `RebuildExpressionRowsForLayer(expressionsContainer, layerName)` 呼び出しを削除する
  - レイヤーカードはレイヤー定義 (name / priority / exclusionMode / inputSources) の編集 UI のみを保持する
  - `RebuildExpressionRowsForLayer` メソッド自体は 9.1 の `BuildExpressionLibrarySection` から間接的に利用される可能性がないなら削除、利用するなら refactor して shared helper 化する
  - 観測可能な完了条件: SO Inspector のレイヤータブを開いたとき、各レイヤーカード内に Expression row が一切表示されず、レイヤー定義のみが表示されること (Req 6.2)
  - _Requirements: 6.2_
  - _Boundary: Editor.Inspector_

- [x] 9.3 (T46) Default Overlays セクションから 3 状態ラジオを撤去する
  - `Editor/Inspector/FacialCharacterProfileSOInspector.cs:706-713` の `OverlayStateRadioButtonGroup` 生成と関連ハンドラ (`:739-746` の `radio.OnValueAssigned`) を削除する
  - AnimationClip フィールド (`DefaultOverlayAnimationClipFieldName`) を常時 visible にする（`style.display = DisplayStyle.None` 制御を撤去）
  - データモデル側のセマンティクスを以下に固定する:
    - `AnimationClip != null` → `serializable.suppress = false` を強制、Exporter が `cachedSnapshot` をサンプリング結果で埋める（Override 相当）
    - `AnimationClip == null` → `serializable.suppress = false` を強制、`cachedSnapshot` を空相当にクリア（Default fallback 相当 = overlay 出力なし）
  - `DefaultOverlayStateRadioName` 定数および `ApplyDefaultOverlayState` メソッドが Inspector 内の他箇所から参照されていないことを確認し、孤立したら削除する
  - 既存の `_defaultOverlays[i].suppress = true` を持つ .asset/JSON は Exporter 再実行で `suppress = false` に正規化されるが、preview.2 段階のため破壊的変更として許容する
  - 観測可能な完了条件: SO Inspector の Default Overlays セクションが slot プルダウン + AnimationClip フィールドの 2 要素のみで構成され、3 状態ラジオが UI に存在しないこと (Req 6.5)
  - _Requirements: 6.5_
  - _Boundary: Editor.Inspector_

- [x] 9.4 (T47) Inspector UI テストを 9.1 〜 9.3 の構造変更に合わせて更新する
  - `Tests/EditMode/Editor/Inspector/OverlaysTabUITests.cs`（6.1 で追加）を以下の観点で拡張する:
    - (i) 表情ライブラリタブ内に Expression リストセクションが存在し、全レイヤーの Expression が含まれること（Req 6.3）
    - (ii) Expression row の Layer プルダウンを操作すると `_expressions[i].layer` SerializedProperty が更新されること（Req 6.6）
    - (iii) レイヤータブを開いたとき、各レイヤーカード内に Expression row 相当の VisualElement (`ExpressionRowNameFieldName` 等) が存在しないこと（Req 6.2）
    - (iv) Default Overlays セクションの row に `RadioButtonGroup` が存在せず、`ObjectField<AnimationClip>` のみが存在すること（Req 6.5）
    - (v) Default Overlays の AnimationClip フィールドを null から非 null へ変更したとき `_defaultOverlays[i].suppress` が `false` 維持されること
  - 既存の Default Overlays 3 状態切替テスト（6.1 で書いた `DefaultOverlayStateRadio` 系）は 9.3 と矛盾するので削除する
  - 観測可能な完了条件: テストランナー (`-batchmode -nographics -testPlatform EditMode`) で `OverlaysTabUITests` が全件 Green になり、9.1 〜 9.3 の構造変更が回帰検出できること (Req 11.8)
  - _Requirements: 6.2, 6.3, 6.5, 6.6, 11.8_
  - _Boundary: Tests/EditMode/Editor/Inspector_

- [x] 9.5 (T48) Sample データの DefaultOverlays 正規化と Sample 起動回帰確認
  - `Assets/Samples/FacialControl InputSystem/0.1.0-preview.2/Multi Source Blend Demo/MultiSourceBlendDemoCharacter.asset` の `_defaultOverlays[]` を確認し、`suppress` フィールドが全エントリで `false` であることを確認する（9.3 のセマンティクス変更で `suppress = true` は意味を持たなくなる）
  - `Assets/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json` および `Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json` の `defaultOverlays[].suppress` も同様に `false` で統一されていることを確認する
  - 7.1 の `SampleAssetsAreInSyncTests` を再実行し、dev 側と Samples~ 側が drift していないことを確認する
  - Unity Editor で `MultiSourceBlendDemo` Scene を再生し、(i) smile 表示中に blink overlay が乗る、(ii) smile_closed_eye 表示中に blink overlay が抑制される、(iii) Inspector の表情ライブラリタブで全 Expression がフラットに表示される、(iv) Inspector のレイヤータブで Expression リストが表示されない、を目視確認する
  - 観測可能な完了条件: Sample 再生時の overlay 挙動が Phase 7 完了時点と同等で、`SampleAssetsAreInSyncTests` が Green、Inspector の表示が Req 6.2 / 6.3 / 6.5 に適合していること (Req 7.5, 7.6)
  - _Requirements: 7.5, 7.6_
  - _Boundary: Sample.Validation_

- [x] 10. テスト残債: JsonUtility / EditorJsonUtility 経路の overlay round-trip 失敗 7 件を緑化する
> **発見経緯 (2026-05-19)**: preview1-polish-pack 完了後の `/kiro:validate-impl` で uloop run-tests を実行し、EditMode で以下 7 件が恒常 fail することを再検出。`docs/backlog.md` M-17（2026-05-15 osc-output-binding 完了直後に検出済み）と同件で、当該 spec のスコープ外残債として本 Phase に吸収する。preview1-polish-pack の commit 群 (ad5b7b7〜7a77c1e) は本件箇所のファイルを一切変更していないため、preview1-polish-pack 起因ではない。
> - **共通根本原因 (系統 A: 6 件)**: `SystemTextJsonParser.ParseProfileSnapshotV2Internal` が `JsonUtility.FromJson<ProfileSnapshotDto>` を使っており、Unity の `JsonUtility` はクラスフィールドの `"snapshot": null` を保持できず空 `ExpressionSnapshotDto` インスタンスを生成する。一方 `ConvertOverlaySlotBindings`（line 652 付近）は `suppress=true && snapshot != null` を `FormatException` 扱いするため、JsonUtility の挙動と衝突して `OverlaySlotBinding slot='X' has invalid state: suppress=true and snapshot is not null.` で fail する。
> - **共通根本原因 (系統 B: 1 件)**: `EditorJsonUtility.FromJsonOverwrite` で legacy `expressionId` キーを含む JSON を SO 上書きすると、`OverlaySlotBindingSerializable.slot` が null になる挙動（`Expected: "blink" But was: null`）。
> - **対象テスト (系統 A)**:
>   - `Hidano.FacialControl.Tests.EditMode.Adapters.Json.SystemTextJsonParserOverlaysTests.Parse_SampleProfileJson_RoundTripsEquivalentOverlaySchema`
>   - `Hidano.FacialControl.Tests.EditMode.Adapters.Json.SystemTextJsonParserOverlaysTests.RoundTrip_ThreeOverlayStates_PreservesEquivalentProfile`
>   - `Hidano.FacialControl.Tests.EditMode.Adapters.Json.SystemTextJsonParserRoundTripTests.ParseProfile_PopulatesLayerInputSourcesAlignedWithLayers`
>   - `Hidano.FacialControl.Tests.EditMode.Adapters.Json.SystemTextJsonParserRoundTripTests.SerializeParseSerialize_SampleJson_ProducesIdenticalString`
>   - `Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.FacialCharacterProfileConverterTests.ToProfileSnapshotDto_DomainOverlayStates_EmitsNewOverlayDtoSchema`
>   - `Hidano.FacialControl.Tests.EditMode.Editor.AutoExport.FacialCharacterProfileExporter_OverlayClipTests.ExportProfileJson_OverlaySlotsAndBindings_WritesNewSchema`
> - **対象テスト (系統 B)**:
>   - `Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.Serializable.OverlaySlotBindingSerializableTests.UnitySerialization_LegacyExpressionIdKey_IsIgnoredAndNewFieldsKeepDefaults`
> - **PlayMode 同系残債**: M-17 にあわせて以下 2 件も同 Phase で扱う（実機 PlayMode 実行で再確認要）。
>   - `Hidano.FacialControl.Tests.PlayMode.Integration.LayerLifecycleZeroFadeTests.ActivateThenDeactivate_DrivenByMonoBehaviourTick_FadesToZero`
>   - `Hidano.FacialControl.Tests.PlayMode.Performance.OverlayInputSourcePerformanceTests.TryWriteValues_1000Frames_AllocatesZeroBytes`

- [x] 10.1 `ConvertOverlaySlotBindings` で JsonUtility 由来の "実質的に空" な `ExpressionSnapshotDto` を null として扱う（系統 A: 6 件 Green）
  - `Packages/com.hidano.facialcontrol/Runtime/Adapters/Json/SystemTextJsonParser.cs:640-672` の `ConvertOverlaySlotBindings` 内で、`d.snapshot` の判定を「実質的に空（`transitionDuration == 0f && transitionCurvePreset∈{null,"","Linear"} && blendShapes/bones/rendererPaths/overlays すべて null か Count==0`）なら null として扱う」ロジックに変更する
  - 既存の `Parse_SuppressWithSnapshot_ThrowsFormatException`（`SystemTextJsonParserOverlaysTests`）が依然 Green であること（非空 snapshot が来た場合は従来通り `FormatException` を投げる挙動を維持）を確認する
  - 観測可能な完了条件: 上記 6 件の EditMode テストが Green、`Parse_SuppressWithSnapshot_ThrowsFormatException` が Green、`uloop run-tests --test-mode EditMode` 全件 Pass
  - _Requirements: 2.6, 3.1, 3.3_
  - _Boundary: SystemTextJsonParser_

- [x] 10.2 `OverlaySlotBindingSerializable` の Unity Serialization 経路で legacy `expressionId` キー混在時に新フィールド `slot` を保持する（系統 B: 1 件 Green）
  - `Packages/com.hidano.facialcontrol/Tests/EditMode/Adapters/ScriptableObject/Serializable/OverlaySlotBindingSerializableTests.cs:21-40` を起点に、`EditorJsonUtility.FromJsonOverwrite` の挙動を再現する小さな再現テストを書き直してから（必要なら Red にして）、`OverlaySlotBindingSerializable` 側のフィールド配置 / `[FormerlySerializedAs]` 付与 / `ISerializationCallbackReceiver` 採用などで `"slot":"blink","expressionId":"legacy_overlay"` 混在 JSON でも `slot = "blink"` が保持される実装に修正する
  - `TypeDefinition_DoesNotExposeLegacyExpressionIdField`（同ファイル）は引き続き Green を維持すること（`expressionId` フィールド自体は型に存在しないことを保証）
  - 観測可能な完了条件: `UnitySerialization_LegacyExpressionIdKey_IsIgnoredAndNewFieldsKeepDefaults` を含む `OverlaySlotBindingSerializableTests` 全 5 件が Green
  - _Requirements: 4.1, 4.4_
  - _Boundary: OverlaySlotBindingSerializable_

- [x] 10.3 PlayMode 同系の overlay 由来 2 件を緑化する
  - `Hidano.FacialControl.Tests.PlayMode.Integration.LayerLifecycleZeroFadeTests.ActivateThenDeactivate_DrivenByMonoBehaviourTick_FadesToZero` と `Hidano.FacialControl.Tests.PlayMode.Performance.OverlayInputSourcePerformanceTests.TryWriteValues_1000Frames_AllocatesZeroBytes` を `uloop run-tests --test-mode PlayMode` で再走し、10.1 / 10.2 完了後に failure が残るかを確認する
  - 残る場合は別系統の overlay 動的駆動経路の問題として原因切り分けし、修正 or Skip 判断を残す
  - 観測可能な完了条件: PlayMode 上で当該 2 件が Green、もしくは現実的に直せない場合は理由を本タスクのコメントに残して `[Ignore]` 化（preview 段階のため許容）
  - _Requirements: 2.6, 5.3_
  - _Boundary: PlayMode.Integration / PlayMode.Performance_
