# Gap Analysis Report: overlay-clip-redesign

## Summary

- **Feature**: `overlay-clip-redesign`
- **Discovery Scope**: Complex Integration（preview.2 段階の破壊的スキーマ変更、11 Requirements、Domain → JSON → SO → Inspector → Sample → Tests を一貫改修）
- **Key Findings**:
  - **Domain `OverlaySlotBinding` は (slot, expressionId) の薄い値型** で、3 状態モデル化には型の作り直しが必要（コンストラクタ + `IsSuppress` ロジック撤去）。再利用できるのは `IEquatable` 実装パターンと防御的コピー方針のみ。
  - **`ExpressionSnapshot` は既に Domain 値型として独立**しており、Overlay の `snapshot override` フィールドにそのまま埋め込み可能（再利用 100%）。`BlendShapeSnapshot` / `BoneSnapshot` も同様。
  - **JSON は Unity `JsonUtility` ベース**（`SystemTextJsonParser` の名前に反して System.Text.Json は使用しない）。`OverlaySlotBindingDto` / `ExpressionSnapshotDto` の重なりが既に存在するため、構造的には新スキーマを乗せやすい。ただし旧 `expressionId` フィールド検出のために PreProcessor 段階で生 JSON を前処理する必要がある（`JsonUtility` は未知フィールドを silent drop するため通常パースで検知不能）。
  - **Inspector は現在 4 タブ構成（表情 / 目線 / Adapter Bindings / Debug）**。Req 6 の 6 タブ構成（表情ライブラリ / レイヤー / ベース表情 / 目線 / Adapter Bindings / Debug）への再編は `BuildLayersSection` / `BuildBaseExpressionSection` / Expression リスト構築をタブ単位に再分割する大きな改修。`FacialCharacterProfileSOInspector` は派生クラス hook 設計（`OnBuildPreLayersSections`）を持つため、派生（InputSystem 用）への影響は最小化可能。
  - **`OverlayInputSource` の per-frame GC ゼロ契約は既存実装で既に達成**されており、新スキーマでも `_resolvedById` の事前計算経路を残せば維持できる。snapshot 引きでも `_resolvedBySlot[slot] => ResolvedSnapshot` 形式に再構成可能（slot 数分の事前計算でカバー）。

---

## (a) 既存コードの再利用可能領域

### Domain 値型: 高再利用

| アセット | パス | 再利用可否 | 理由 |
|---|---|---|---|
| `ExpressionSnapshot` (struct) | `Packages/com.hidano.facialcontrol/Runtime/Domain/Models/ExpressionSnapshot.cs:20-136` | **そのまま再利用** | 既に `(Id, TransitionDuration, TransitionCurvePreset, BlendShapes[], Bones[], RendererPaths[])` を不変に保持する Domain 値型。`OverlaySlotBinding` の `snapshot` フィールド型に直接採用できる。`CreateDefault(string id)` ヘルパも fallback 構築で利用可。 |
| `BlendShapeSnapshot` / `BoneSnapshot` | `Runtime/Domain/Models/BlendShapeSnapshot.cs`, `BoneSnapshot.cs` | **そのまま再利用** | `ExpressionSnapshot` の構成要素として透過。 |
| `FacialProfile` 防御的コピー基盤 | `Runtime/Domain/Models/FacialProfile.cs:131-140` (DefaultOverlays コピー), `:74-105` | **再利用 + 拡張** | `Slots` フィールドを `ReadOnlyMemory<string>` で追加するだけで既存 ctor の防御的コピーパターンに乗る。`TryGetDefaultOverlay` (lines 149-167) と `FindLayerByName` (lines 275-288) の slot 線形検索パターンも `Slots` 検証ロジックでそのまま流用。 |
| `Expression.TryGetOverlay` の slot 線形検索 | `Runtime/Domain/Models/Expression.cs:196-214` | **シグネチャ再利用** | `out OverlaySlotBinding binding` のシグネチャは新 3 状態型のままで保存可能（型のフィールド構成が変わるだけ）。 |
| `InvalidLayerReference` ベース整合性検証 | `Runtime/Domain/Models/InvalidLayerReference.cs` + `FacialProfile.ValidateLayerReferences` (lines 173-200) | **パターン流用** | Slots 整合性検証 (`ValidateSlotReferences`) をこのパターンで新規追加。`InvalidSlotReference` 値型を兄弟として追加。 |

### JSON Adapter: 中再利用

| アセット | パス | 再利用可否 | 理由 |
|---|---|---|---|
| `SystemTextJsonParser` 全体構造 | `Runtime/Adapters/Json/SystemTextJsonParser.cs` | **メソッド単位で再利用** | `ParseProfileSnapshotV2Internal` (lines 62-97) の strict version check / `NormalizeProfileSnapshotDto` (lines 103-128) の null collection 正規化 / `ConvertToProfile` (lines 619-628) の DTO→Domain 変換骨格はそのまま。`ConvertOverlaySlotBindings` (lines 630-644) と `BuildOverlaySlotBindingDtoList` (lines 842-855) を新型へ置換。 |
| `ExpressionSnapshotDto` | `Runtime/Adapters/Json/Dto/ExpressionSnapshotDto.cs` | **そのまま再利用** | overlay の snapshot 部分の DTO 表現として既に存在し、新 `OverlaySlotBindingDto.snapshot` の型として埋め込める。 |
| `ProfileSnapshotDto` | `Runtime/Adapters/Json/Dto/ProfileSnapshotDto.cs` | **拡張** | `slots: List<string>` フィールド追加と `defaultOverlays: List<OverlaySlotBindingDto>` の型変更だけ。`gazeConfigs` などの既存フィールドは無変更。 |
| `JsonSchemaDefinition.OverlaySlot` 定数群 | `Runtime/Adapters/Json/JsonSchemaDefinition.cs:144-152` | **拡張 + 旧定数撤去** | `Slot` / `ExpressionId` のうち `ExpressionId` 撤去、`Suppress` / `Snapshot` 追加。`Profile.Slots` 定数を新規追加。 |
| 旧フィールド検出用 JSON 前処理パターン | `SystemTextJsonParser.PreprocessInputSourceOptions` (lines 309-369) | **パターン流用** | 既存の `optionsJson` 抽出が「JSON を文字列レベルで解析して特定キーの存在を検出」している。同じ手法で `expressionId` キーが `OverlaySlotBindingDto` 相当ブロック内に存在する場合に明示的に弾けばよい。`JsonUtility` は未知フィールドを silent drop するため、これが唯一の検出経路。 |

### SO Serializable: 中再利用

| アセット | パス | 再利用可否 | 理由 |
|---|---|---|---|
| `OverlaySlotBindingSerializable` | `Runtime/Adapters/ScriptableObject/Serializable/OverlaySlotBindingSerializable.cs:11-19` | **2 フィールド追加** | 既存 (`slot`, `expressionId`) → 新 (`slot`, `suppress`, `animationClip`, `cachedSnapshot`)。`expressionId` 撤去。 |
| `FacialCharacterProfileSO` | `Runtime/Adapters/ScriptableObject/FacialCharacterProfileSO.cs:24` | **フィールド追加** | `_slots: List<string>` を `_defaultOverlays` の隣に追加。既存 `_defaultOverlays` の List 型はそのまま、要素の型構造のみ変わる。`BuildFallbackProfile` (lines 56-60) は `_slots` を渡すよう拡張。 |
| `ExpressionSerializable.cachedSnapshot` ベイクパターン | `Runtime/Adapters/ScriptableObject/Serializable/ExpressionSerializable.cs:54-58` | **パターン流用** | overlays の各エントリにも同じ "AnimationClip → cachedSnapshot ベイク" パターンを適用。Exporter の `SampleAnimationClipsIntoCachedSnapshots` (Editor/AutoExport/FacialCharacterProfileExporter.cs:40-68) を overlays ループ追加で拡張。 |
| `FacialCharacterProfileConverter.ConvertOverlays` | `Runtime/Adapters/ScriptableObject/Serializable/FacialCharacterProfileConverter.cs:202-213` | **置換** | 旧 `(slot, expressionId)` → 新 `(slot, suppress, snapshot)`。`cachedSnapshot` から `ExpressionSnapshot` を再構築するヘルパが必要（`ConvertSnapshotBlendShapes` at lines 229-241 のパターン流用可）。 |

### Inspector: 高再利用 + 新セクション追加

| アセット | パス | 再利用可否 | 理由 |
|---|---|---|---|
| `FacialCharacterProfileSOInspector` 全体 | `Editor/Inspector/FacialCharacterProfileSOInspector.cs` (約 2300 行) | **タブ構成のみ再編、内部メソッドは大半再利用** | `BuildLayersSection` (line 973) / `BuildBaseExpressionSection` (line 408) / `BuildExpressionRow` (line 1306) / `BuildAdapterBindingsSection` (line ~196) / `BuildDebugSection` などの section ビルダー単位はそのまま使える。タブ配列の組み換え + 新セクション追加（Slots 宣言、Default Overlays、Overlays 3 状態 UI）。 |
| 派生クラス hook (`OnBuildPreLayersSections`) | `FacialCharacterProfileSOInspector.cs:241` | **再利用** | InputSystem パッケージの派生 Inspector への影響は本 hook で吸収できる。タブ ID 定数 (`TabExpressionsName` 等) のみ変更し、派生は新タブ ID を参照するよう更新。 |
| `BuildArrayListView` 共通ビルダ | `FacialCharacterProfileSOInspector.cs:2247` | **再利用** | Slots 宣言セクションのリスト UI / Default Overlays リスト UI に流用可能。 |
| `_layerNameChoices` 動的候補リストパターン | lines 142, 1020 (`RefreshLayerNameChoices`) | **パターン流用** | 同じ手法で `_slotNameChoices` を `_slots` から動的生成。Adapter Bindings の `overlaySlot` ドロップダウン化 (Req 6.11) で参照される。 |
| `InputSystemAdapterBindingDrawer.cs` の `overlaySlot` PropertyField | `Packages/com.hidano.facialcontrol.inputsystem/Editor/AdapterBindings/InputSystemAdapterBindingDrawer.cs:288, 363-376` | **置換** | 現状 `PropertyField` (TextField) で free-text。Slots 宣言から動的 choices を取得する `DropdownField` に差し替え（Req 6.11, 10.5）。 |

### Tests: 中再利用

既存テストは TDD 書き換えとなるが、シナリオ構造（`BuildProfile` ヘルパ、`StubActiveProvider` フェイク、`Span<float> output` 検証パターン）はそのまま再利用できる。書き換え対象は `OverlaySlotBinding(slot, expressionId)` コンストラクタ呼出と `ExpressionId` プロパティ参照を新型 API に置換するのみ。

---

## (b) 各 Requirement の影響範囲

### 凡例
- **NEW**: 新規ファイル
- **MOD**: 既存ファイル修正
- **DEL**: ファイル削除（未該当のため記載なし）
- **REPL**: 既存ファイル全置換相当の大改修

### Requirement 1: FacialProfile.Slots 宣言

| 種別 | パス | 内容 |
|---|---|---|
| MOD | `Runtime/Domain/Models/FacialProfile.cs` | `Slots` プロパティ追加、ctor 引数 `slots` 追加、防御的コピーロジック、`ValidateSlotReferences()` メソッド追加 |
| NEW | `Runtime/Domain/Models/InvalidSlotReference.cs` | `InvalidLayerReference` 兄弟。slot 識別子の整合性違反を表現 |
| MOD | `Tests/EditMode/Domain/FacialProfileSlotsTests.cs` (NEW) | Slots 初期化 / 重複検出 / overlays 整合性検証 |

### Requirement 2: OverlaySlotBinding 3 状態モデル

| 種別 | パス | 内容 |
|---|---|---|
| REPL | `Runtime/Domain/Models/OverlaySlotBinding.cs:1-74` | フィールドを `(string Slot, bool Suppress, ExpressionSnapshot? Snapshot)` に再定義。コンストラクタで suppress=true && snapshot!=null を `ArgumentException` で拒否。`IsSuppress` → `Suppress` プロパティ。`IsDefaultFallback` プロパティ追加 (`!Suppress && Snapshot == null`)。`ExpressionId` プロパティ完全撤去。 |
| MOD | `Runtime/Domain/Models/Expression.cs:69-77, 196-214` | `Overlays` フィールドの XML doc 更新（"ExpressionId が空" → "Suppress / Snapshot 状態"）。`TryGetOverlay` シグネチャはそのまま、内部 doc のみ更新。 |
| MOD | `Runtime/Domain/Models/FacialProfile.cs:42-46, 149-167` | `DefaultOverlays` の XML doc 更新。`TryGetDefaultOverlay` 内部参照更新。 |
| REPL | `Tests/EditMode/Domain/OverlaySlotBindingTests.cs` | 3 状態（default fallback / suppress / snapshot override）の構築・等価性・矛盾組合せ拒否テスト |

### Requirement 3: OverlayInputSource snapshot 引き

| 種別 | パス | 内容 |
|---|---|---|
| REPL | `Runtime/Adapters/InputSources/OverlayInputSource.cs:1-227` | `_resolvedById: Dictionary<string, ResolvedExpression>` (line 38) を廃止。代わりに「Expression が Overlays で個別 snapshot を持つケース + DefaultOverlays が個別 snapshot を持つケース」を slot ごとに事前展開する `_resolvedBySlot: Dictionary<(string ExprId, string Slot), ResolvedSnapshot>` 構造へ。`ResolveOverlayExpressionId` (lines 146-166) は `ResolveSnapshot(out ResolvedSnapshot)` へ。`ResolvedExpression.Build` (lines 199-223) は `Expression` 引数 → `ExpressionSnapshot` 引数 + slot key を受ける形に。slot が `_profile.Slots` 未宣言なら 1 回のみ `Debug.LogWarning` (Req 3.6)。 |
| REPL | `Tests/EditMode/Adapters/InputSources/OverlayInputSourceTests.cs` | snapshot 引きでの 4 経路（Expression 個別 / Expression suppress / DefaultOverlays 個別 / 未解決）を観測 |

### Requirement 4: JSON スキーマと Parser

| 種別 | パス | 内容 |
|---|---|---|
| REPL | `Runtime/Adapters/Json/Dto/OverlaySlotBindingDto.cs:1-21` | `expressionId: string` 撤去。`suppress: bool` + `snapshot: ExpressionSnapshotDto` 追加 |
| MOD | `Runtime/Adapters/Json/Dto/ProfileSnapshotDto.cs:1-43` | `slots: List<string>` 追加 |
| MOD | `Runtime/Adapters/Json/SystemTextJsonParser.cs` | (i) `NormalizeProfileSnapshotDto` (lines 103-128) で `dto.slots == null` を `new List<string>()` に正規化。(ii) `ConvertToProfile` (lines 619-628) で slots を Domain へ橋渡し。(iii) `ConvertOverlaySlotBindings` (lines 630-644) を新型へ。(iv) `BuildOverlaySlotBindingDtoList` (lines 842-855) を新型へ。(v) **重要**: `Preprocess*` 系統に `RejectLegacyExpressionIdInOverlays` を追加し、生 JSON 内に `OverlaySlotBindingDto` 相当ブロック中の `"expressionId"` キーを検出したら `FormatException` で fail。`JsonUtility` は未知フィールド silent drop なのでここでしか検出できない。(vi) `ConvertToExpressionDto` (lines 809-840) `BuildOverlaySlotBindingDtoList` 呼出を新型へ。 |
| MOD | `Runtime/Adapters/Json/JsonSchemaDefinition.cs:144-152, 22-46` | `Profile.OverlaySlot.ExpressionId` 撤去、`Suppress` / `Snapshot` 追加。`Profile.Slots` 定数追加。`SampleProfileJson` (lines 292-357) を新スキーマに更新。 |
| MOD | `Runtime/Adapters/ScriptableObject/Serializable/FacialCharacterProfileConverter.cs:17-42, 202-213` | `_slots` 引数を受けて FacialProfile に渡す。`ConvertOverlays` を新型へ。`cachedSnapshot` から `ExpressionSnapshot` を再構築するヘルパ追加。 |
| REPL | `Tests/EditMode/Adapters/Json/SystemTextJsonParserOverlaysTests.cs` | 旧 `expressionId` 拒否ケース追加、矛盾組合せ拒否ケース追加、3 状態 round-trip |

### Requirement 5: ScriptableObject Serializable

| 種別 | パス | 内容 |
|---|---|---|
| REPL | `Runtime/Adapters/ScriptableObject/Serializable/OverlaySlotBindingSerializable.cs:1-19` | `expressionId` 撤去。`suppress: bool`, `animationClip: AnimationClip`, `cachedSnapshot: ExpressionSnapshotDto` 追加。Tooltip 更新。 |
| MOD | `Runtime/Adapters/ScriptableObject/FacialCharacterProfileSO.cs:18-25, 56-60` | `_slots: List<string>` フィールド + `Slots` プロパティ追加。`BuildFallbackProfile` で `_slots` を渡す。 |
| MOD | `Editor/AutoExport/FacialCharacterProfileExporter.cs:40-68, 151-237` | (i) `SampleAnimationClipsIntoCachedSnapshots` で各 Expression の `overlays` リストを走査し、各 `OverlaySlotBindingSerializable.animationClip != null && !suppress` なら sampler.SampleSnapshot で `cachedSnapshot` 更新。(ii) suppress=true は AnimationClip サンプリングを skip し `cachedSnapshot = null` または空 dto。(iii) AnimationClip == null && suppress=false はデフォルト fallback として `cachedSnapshot = null`。(iv) `BuildProfileSnapshotDto` で `defaultOverlays` を新スキーマで出力（**現状 line 151 以降に defaultOverlays 出力ロジックが欠落しており、これは現行の Bug でもある**）。(v) `_slots` を `dto.slots` に出力。(vi) `_slots` 重複 / 未参照 slot を warning ログ出力。 |
| MOD | `Runtime/Adapters/ScriptableObject/Serializable/FacialCharacterProfileConverter.cs:17-42, 167-213` | 上述 Requirement 4 と統合。`ToFacialProfile` のシグネチャに `slots` 追加（オーバーロード保持で互換性緩和）。 |

### Requirement 6: Inspector UI 6 タブ再編

| 種別 | パス | 内容 |
|---|---|---|
| REPL | `Editor/Inspector/FacialCharacterProfileSOInspector.cs:178-205` (タブ構築部) | 4 タブ → 6 タブ。新タブ: 「表情ライブラリ」「レイヤー」「ベース表情」。タブ ID 定数 (`TabExpressionsName` 等、lines 47-50) を再定義し新タブ用定数を追加。 |
| NEW (内部メソッド) | 同ファイル内 `BuildExpressionLibraryTab(VisualElement root)` | Slots 宣言セクション + Default Overlays セクション + Expression リスト（新 row UI） |
| NEW (内部メソッド) | `BuildSlotsDeclarationSection(VisualElement root)` | 文字列リスト編集 UI（追加 / 削除 / リネーム）。`_layerNameChoices` パターンの `_slotNameChoices` 派生 |
| NEW (内部メソッド) | `BuildDefaultOverlaysSection(VisualElement root)` | slot プルダウン × AnimationClip フィールドのリスト UI |
| MOD | `BuildExpressionRow(int exprIndex)` (line 1306) | Layer プルダウンと Overlays セクションを各行に追加。Overlays セクションは Slots 宣言で定義された各 slot ごとに 3 状態ラジオ + AnimationClip フィールド |
| NEW (内部メソッド) | `BuildOverlaysSectionForExpression(SerializedProperty overlaysProp, int exprIndex)` | 3 状態ラジオの動的可視性切替（個別選択時のみ AnimationClip フィールド表示） |
| MOD | `BuildLayersSection(VisualElement root)` (line 973) | レイヤータブへ移動（中身はほぼ同じ） |
| MOD | `BuildBaseExpressionSection(VisualElement root)` (line 408) | ベース表情タブへ移動 |
| MOD | `Packages/com.hidano.facialcontrol.inputsystem/Editor/AdapterBindings/InputSystemAdapterBindingDrawer.cs:286-376` | `overlaySlot` の `PropertyField` (TextField) を `DropdownField` に置換。choices は SO の `_slots` から動的取得（PropertyDrawer から SO に到達するためには `serializedObject.targetObject as FacialCharacterProfileSO` を辿る or `IFacialCharacterProfile` 経由でアクセス）。 |
| NEW | `Tests/EditMode/Editor/Inspector/OverlaysTabUITests.cs` | 3 状態切替 / AnimationClip 設定 / Slots 宣言整合性の Edit Mode テスト |

### Requirement 7: Sample データ移行

| 種別 | パス | 内容 |
|---|---|---|
| MOD | `Assets/Samples/FacialControl InputSystem/0.1.0-preview.2/Multi Source Blend Demo/MultiSourceBlendDemoCharacter.asset:38-228` | (i) `_slots: ["blink"]` 追加。(ii) `blink_overlay` Expression を削除し、`smile` / `smile_closed_eye` の `overlays` に `blink_overlay` の AnimationClip サンプリング結果（cachedSnapshot）を直接焼き直す。(iii) `_defaultOverlays` の `expressionId: blink_overlay` を新型 `(slot: blink, suppress: false, animationClip: ..., cachedSnapshot: ...)` に変換。(iv) `smile_closed_eye.overlays[0]` を `(slot: blink, suppress: true, animationClip: null, cachedSnapshot: null)` に変換。 |
| MOD | `Assets/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json:140-348` | (i) `expressions[]` から `blink_overlay` Expression エントリ削除（lines 145-225）。(ii) ルートに `slots: ["blink"]` 追加。(iii) `defaultOverlays[]` (lines 343-348) を新スキーマ `[{slot, suppress, snapshot}]` へ。snapshot は元 `blink_overlay` の snapshot を inline。(iv) `smile_closed_eye.snapshot.overlays` (lines 309-314) を新スキーマ（suppress=true）へ。(v) `smile`（line 38- ）の `snapshot.overlays` に新スキーマで blink snapshot を inline。 |
| MOD | `Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/StreamingAssets/FacialControl/MultiSourceBlendDemoCharacter/profile.json:1-139` | dev 側と同期。Samples~ 配布の canonical なので drift 厳禁。 |
| MOD | `Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/MultiSourceBlendDemoCharacter.asset` (存在する場合) | dev 側 .asset と同期 |
| MOD | `Assets/Samples/FacialControl InputSystem/0.1.0-preview.2/Multi Source Blend Demo/MultiSourceBlendDemoCharacter.asset:270-274` (RightTrigger entry) | `overlaySlot: blink` はそのまま。Adapter Bindings 経路は `overlaySlot` 文字列リファレンスを保持するので Sample 内容は変わらない（ただし Inspector 上は dropdown に。バックエンドは asset 上の文字列のまま）。 |

### Requirement 8: 後方互換非対応と旧フィールド拒否

Requirement 4 と統合（Parser 側で対応）。追加で:

| 種別 | パス | 内容 |
|---|---|---|
| MOD | `Packages/com.hidano.facialcontrol/CHANGELOG.md` | preview.2 セクションに破壊的変更を明示 |
| MOD | `Packages/com.hidano.facialcontrol/README.md` (該当箇所) | overlay の旧 `expressionId` ベースから snapshot ベースへ変わった旨を記載 |

### Requirement 9: per-frame GC ゼロ

| 種別 | パス | 内容 |
|---|---|---|
| MOD | `Runtime/Adapters/InputSources/OverlayInputSource.cs` | Requirement 3 の REPL に統合。新解決経路で `Dictionary` 構築は ctor のみ、毎フレームは `TryGetValue` + `int[]` / `float[]` 配列直書きで GC ゼロ維持。 |
| NEW | `Tests/PlayMode/Performance/OverlayInputSourcePerformanceTests.cs` | `Unity.PerformanceTesting` または `GC.GetTotalAllocatedBytes(true)` で per-frame allocations を計測 |

### Requirement 10: ARKit / InputSystem / OSC 影響限定

| 種別 | パス | 内容 |
|---|---|---|
| MOD | `Packages/com.hidano.facialcontrol.inputsystem/Runtime/Adapters/AdapterBindings/InputSystemAdapterBinding.cs:440-510` | `BuildOverlaySources` 内で `entry.overlaySlot` が `ctx.Profile.Slots` に含まれているか検証。未宣言なら warning + skip。それ以外のロジックは無変更。 |
| MOD | `Packages/com.hidano.facialcontrol.inputsystem/Runtime/Adapters/ScriptableObject/ExpressionBindingEntry.cs:42` | `overlaySlot` フィールド型は `string` のままで OK（Tooltip だけ更新「FacialCharacterProfileSO.Slots 宣言から選択」） |
| (確認のみ) | OSC / ARKit 経路 | `Grep overlaySlot|OverlaySlot` で `com.hidano.facialcontrol.osc` には現状 hits なし、ARKit 経路にも overlay 参照なし。Req 10.1 / 10.3 は確認のみで No-op。 |

### Requirement 11: テストスイート TDD 書き換え

| 種別 | パス | 内容 |
|---|---|---|
| REPL | `Tests/EditMode/Domain/OverlaySlotBindingTests.cs` | 3 状態モデル |
| REPL | `Tests/EditMode/Adapters/InputSources/OverlayInputSourceTests.cs` | snapshot 引き |
| REPL | `Tests/EditMode/Application/LayerUseCaseOverlayLayerTests.cs` | 新スキーマで blink_overlay → smile/smile_closed_eye 個別 snapshot シナリオ |
| REPL | `Tests/EditMode/Application/LayerUseCaseAnalogOverlayTests.cs` | 同上 |
| REPL | `Tests/EditMode/Application/LayerUseCaseAnalogExpressionAdditionTests.cs` | 同上 |
| REPL | `Tests/EditMode/Application/ExpressionUseCaseActiveProviderTests.cs` | overlay 経路への参照のみ更新 |
| REPL | `Tests/EditMode/Adapters/Json/SystemTextJsonParserOverlaysTests.cs` | 上述 Req 4 |
| NEW | `Tests/EditMode/Editor/Inspector/OverlaysTabUITests.cs` | Inspector UI テスト（Req 6.7-6.9, 6.10） |

---

## (c) 破壊的変更を伴うシンボル列挙

### Domain (`Hidano.FacialControl.Domain.Models`)

| 種類 | シンボル | 変更 |
|---|---|---|
| 型 | `OverlaySlotBinding` (struct) | フィールド構造完全置換 |
| プロパティ | `OverlaySlotBinding.ExpressionId` | **削除** |
| プロパティ | `OverlaySlotBinding.IsSuppress` (computed) | **削除** または `=> Suppress` の単純 alias 化 |
| プロパティ (NEW) | `OverlaySlotBinding.Suppress: bool` | 新規 |
| プロパティ (NEW) | `OverlaySlotBinding.Snapshot: ExpressionSnapshot?` | 新規 |
| プロパティ (NEW) | `OverlaySlotBinding.IsDefaultFallback: bool` | 新規 (`!Suppress && !Snapshot.HasValue`) |
| ctor | `OverlaySlotBinding(string slot, string expressionId)` | **削除** |
| ctor (NEW) | `OverlaySlotBinding(string slot, bool suppress, ExpressionSnapshot? snapshot)` | 新規。矛盾組合せで `ArgumentException` |
| プロパティ (NEW) | `FacialProfile.Slots: ReadOnlyMemory<string>` | 新規 |
| ctor | `FacialProfile(string, LayerDefinition[], Expression[], string[], InputSourceDeclaration[][], OverlaySlotBinding[])` | **シグネチャ拡張** — `string[] slots` 引数を末尾または前段に追加（既存呼出箇所多数のため named arg 推奨）。ctor シグネチャは破壊的変更扱い。 |
| メソッド (NEW) | `FacialProfile.ValidateSlotReferences()` | 新規 |
| 型 (NEW) | `InvalidSlotReference` (struct) | 新規 |

### Adapter / JSON (`Hidano.FacialControl.Adapters.Json.Dto`)

| 種類 | シンボル | 変更 |
|---|---|---|
| フィールド | `OverlaySlotBindingDto.expressionId: string` | **削除** |
| フィールド (NEW) | `OverlaySlotBindingDto.suppress: bool` | 新規 |
| フィールド (NEW) | `OverlaySlotBindingDto.snapshot: ExpressionSnapshotDto` | 新規 |
| フィールド (NEW) | `ProfileSnapshotDto.slots: List<string>` | 新規 |
| 定数 | `JsonSchemaDefinition.Profile.OverlaySlot.ExpressionId` | **削除** |
| 定数 (NEW) | `JsonSchemaDefinition.Profile.OverlaySlot.Suppress` | 新規 (`"suppress"`) |
| 定数 (NEW) | `JsonSchemaDefinition.Profile.OverlaySlot.Snapshot` | 新規 (`"snapshot"`) |
| 定数 (NEW) | `JsonSchemaDefinition.Profile.Slots` | 新規 (`"slots"`) |
| 定数 | `JsonSchemaDefinition.SampleProfileJson` | **更新** |

### Adapter / SO (`Hidano.FacialControl.Adapters.ScriptableObject.Serializable`)

| 種類 | シンボル | 変更 |
|---|---|---|
| フィールド | `OverlaySlotBindingSerializable.expressionId: string` | **削除** |
| フィールド (NEW) | `OverlaySlotBindingSerializable.suppress: bool` | 新規 |
| フィールド (NEW) | `OverlaySlotBindingSerializable.animationClip: AnimationClip` | 新規 |
| フィールド (NEW) | `OverlaySlotBindingSerializable.cachedSnapshot: ExpressionSnapshotDto` | 新規 |
| フィールド (NEW) | `FacialCharacterProfileSO._slots: List<string>` | 新規 |
| プロパティ (NEW) | `FacialCharacterProfileSO.Slots: List<string>` | 新規 |
| メソッド | `FacialCharacterProfileSO.BuildFallbackProfile()` | 内部実装変更 (新引数を Converter に渡す) |
| メソッド | `FacialCharacterProfileConverter.ToFacialProfile(...)` | **シグネチャ拡張** — `IReadOnlyList<string> slots` 引数追加 |

### InputSystem 連携 (`Hidano.FacialControl.InputSystem.*`)

| 種類 | シンボル | 変更 |
|---|---|---|
| フィールド | `ExpressionBindingEntry.overlaySlot: string` | 型は無変更だが Inspector 経路で **動的 dropdown 化**（バックエンド互換） |
| 動作 | `InputSystemAdapterBinding.BuildOverlaySources` | `Profile.Slots` 検証ロジック追加（warning + skip） |

### 重要: ctor 破壊性の連鎖

`FacialProfile` ctor シグネチャ拡張は **約 30+ 箇所のテストコード（プロダクションコードでも 1-2 箇所）に波及**する。Grep で以下が確認できる:
- `Tests/EditMode/Domain/FacialProfileTests.cs` (推定)
- `Tests/EditMode/Application/*.cs` (LayerUseCase 系テスト群、上記 Req 11 対象)
- `Tests/EditMode/Adapters/Json/SystemTextJsonParserOverlaysTests.cs:42-44`
- `SystemTextJsonParser.ConvertToProfile` (lines 619-628)
- `FacialCharacterProfileConverter.ToFacialProfile` (lines 26-42)

named arg 経由で呼んでいる箇所は緩和されるが、positional 呼出箇所は全件更新必要。

---

## (d) 推奨実装順序（依存グラフ + コンパイル赤期間最小化）

依存方向: **Domain → JSON DTO → Parser → SO Serializable → Converter → Inspector → Sample → Tests**

コンパイル赤期間を最小化するため、以下の順序で実装する:

### Phase 1: Domain 層（1-2 日）
1. **Step 1.1**: `InvalidSlotReference.cs` 新規追加（既存型に依存しない、安全）
2. **Step 1.2**: `OverlaySlotBinding.cs` 全置換 — ここでコンパイル赤化が始まる
3. **Step 1.3**: `FacialProfile.cs` ctor 拡張 + `Slots` プロパティ + `ValidateSlotReferences` 追加
4. **Step 1.4**: `Expression.cs` の XML doc 更新（API は無変更）
5. **Step 1.5**: `Tests/EditMode/Domain/OverlaySlotBindingTests.cs` を Red → Green
6. **Step 1.6**: `Tests/EditMode/Domain/FacialProfileSlotsTests.cs` を Red → Green

**この時点で Adapter / Inspector / Tests/Application は赤のまま**。

### Phase 2: JSON DTO 層（1 日）
1. **Step 2.1**: `OverlaySlotBindingDto.cs` 新フィールド構造に置換
2. **Step 2.2**: `ProfileSnapshotDto.cs` に `slots` 追加
3. **Step 2.3**: `JsonSchemaDefinition.cs` 定数更新

**この時点で Parser がまだ赤**。

### Phase 3: Parser / Converter（2-3 日）
1. **Step 3.1**: `SystemTextJsonParser.cs` の `ConvertOverlaySlotBindings` / `BuildOverlaySlotBindingDtoList` を新型へ
2. **Step 3.2**: `NormalizeProfileSnapshotDto` で slots 正規化
3. **Step 3.3**: 旧 `expressionId` 検出 PreProcessor 実装 + 矛盾組合せ検出
4. **Step 3.4**: `JsonSchemaDefinition.SampleProfileJson` を新スキーマに更新（**重要**: `Tests/EditMode/Adapters/Json/SystemTextJsonParserOverlaysTests.cs:Parse_OverlaysFieldMissing_FallsBackToEmpty` がこれを参照）
5. **Step 3.5**: `SystemTextJsonParserOverlaysTests.cs` Red → Green
6. **Step 3.6**: `FacialCharacterProfileConverter.cs` の `ConvertOverlays` 新型化、`ToFacialProfile` シグネチャ拡張

### Phase 4: SO Serializable（1 日）
1. **Step 4.1**: `OverlaySlotBindingSerializable.cs` フィールド置換
2. **Step 4.2**: `FacialCharacterProfileSO.cs` `_slots` 追加
3. **Step 4.3**: `FacialCharacterProfileExporter.cs` で overlays サンプリング + defaultOverlays / slots 出力

### Phase 5: OverlayInputSource（2-3 日）
1. **Step 5.1**: `OverlayInputSource.cs` の `_resolvedById` を slot 別 snapshot 事前展開に置換
2. **Step 5.2**: `Tests/EditMode/Adapters/InputSources/OverlayInputSourceTests.cs` Red → Green
3. **Step 5.3**: `Tests/PlayMode/Performance/OverlayInputSourcePerformanceTests.cs` で GC ゼロ検証
4. **Step 5.4**: `LayerUseCaseOverlayLayerTests.cs` / `LayerUseCaseAnalogOverlayTests.cs` / `LayerUseCaseAnalogExpressionAdditionTests.cs` を Red → Green
5. **Step 5.5**: `ExpressionUseCaseActiveProviderTests.cs` 微修正

**この時点で全 Runtime テスト Green**。

### Phase 6: Inspector UI（3-5 日）
1. **Step 6.1**: 6 タブ構成への再編（既存 4 タブの分割）
2. **Step 6.2**: Slots 宣言セクション実装
3. **Step 6.3**: Default Overlays セクション実装
4. **Step 6.4**: Expression row の Overlays 3 状態 UI 実装
5. **Step 6.5**: `InputSystemAdapterBindingDrawer.cs` の overlaySlot dropdown 化
6. **Step 6.6**: Inspector UI テスト追加

### Phase 7: Sample 移行（1 日）
1. **Step 7.1**: `Assets/StreamingAssets/.../profile.json` 新スキーマ移行
2. **Step 7.2**: `Packages/.../Samples~/.../profile.json` 同期
3. **Step 7.3**: `Assets/Samples/.../MultiSourceBlendDemoCharacter.asset` 新スキーマ移行（`blink_overlay` Expression を smile/smile_closed_eye の overlays に snapshot として inline）
4. **Step 7.4**: Editor で起動確認 — `smile` で blink overlay が出る、`smile_closed_eye` で blink overlay が抑制される

### Phase 8: ドキュメント / 仕上げ（半日）
1. **Step 8.1**: `CHANGELOG.md` / `README.md` 更新
2. **Step 8.2**: `JsonSchemaDefinition.SampleProfileJson` 最終確認
3. **Step 8.3**: 全 EditMode + PlayMode テスト実行

**合計目安**: 12-17 営業日 (M-L サイズ、Risk Medium)

---

## (e) 移行リスクと緩和策

### Risk 1: Sample 二重管理同期（preview.2 の伝統的なリスク）

- **問題**: `Assets/Samples/.../MultiSourceBlendDemoCharacter.asset` (dev 用、scene 結線済み) と `Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/...` (UPM canonical、配布対象) が drift する。CLAUDE.md 「Samples の二重管理ルール」に明示的に書かれているリスク。
- **影響**: UPM 経由 import したユーザーと dev で挙動が乖離。Req 7.5 違反。
- **緩和策**:
  1. **チェックリスト化**: 移行 PR の自己レビュー項目に「Samples~ と Assets/Samples の両方を更新したか」を必須化
  2. **diff コマンド化**: PR 中に `diff -r Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/StreamingAssets Assets/Samples/FacialControl\ InputSystem/0.1.0-preview.2/Multi\ Source\ Blend\ Demo/StreamingAssets` で 0 差分を確認
  3. **テスト追加**: EditMode テスト `SampleAssetsAreInSyncTests` を追加し、`profile.json` のテキスト比較 / `.asset` の YAML diff（key set 一致）を CI で検証
  4. **Sample に Expression `blink_overlay` が残らないこと**を検証する Edit Mode テスト（移行漏れの shortcut detection）

### Risk 2: Inspector UI のテスト容易性

- **問題**: UI Toolkit の Inspector は `[CustomEditor]` 経由で生成され、Edit Mode テストから直接トリガーするのが面倒。`CreateInspectorGUI()` 戻り値の `VisualElement` を直接生成できるが、`SerializedObject` を build するために target asset が必要。
- **影響**: Req 6.7-6.10 / Req 11.8 の「3 状態切替 / AnimationClip 設定 / Slots 宣言整合性」テストが書けない or 表面的になる。
- **緩和策**:
  1. **既存パターン踏襲**: `inspector-and-data-model-redesign` spec で既に Inspector の Edit Mode テストが書かれている可能性を確認（`Tests/EditMode/Editor/...` を Glob 検索）
  2. **Inspector ロジックを純粋関数化**: 3 状態判定（`(suppress, snapshot) => UIState`）と Slots 整合性検証をピュア関数として切り出し、UI 構築とは別途テスト可能にする
  3. **VisualElement 構築のみのテスト**: テスト用に `ScriptableObject.CreateInstance<FacialCharacterProfileSO>()` で in-memory SO を作り、Inspector の `CreateInspectorGUI()` を呼んで返却された VisualElement に対し `Q<DropdownField>(name)` で要素検索 + `value` 検証
  4. **ScriptableSingleton or Helper**: 状態判定ロジックを `OverlaySlotBindingSerializable` / `FacialCharacterProfileSO` 上のメソッドに置く（Inspector 自体は薄いビュー）

### Risk 3: OverlayInputSource の GC ゼロ維持（Req 9）

- **問題**: 新解決経路で snapshot 引きにすると、`ExpressionSnapshot` が `readonly struct` であっても `_resolvedBySlot.TryGetValue` で `out ResolvedSnapshot` するときに boxing or copy オーバーヘッドが入る可能性。`Dictionary<TKey, TValue>.TryGetValue` は struct value の場合 copy するが boxing はしない。LINQ や `foreach` over `IEnumerable` は禁止。
- **影響**: Req 9.1-9.4 違反。VTuber 配信での GC スパイク。
- **緩和策**:
  1. **既存 `_resolvedById: Dictionary<string, ResolvedExpression>` パターンを踏襲**: ctor 時に slot ごとに事前展開し、毎フレームは hash 引き 1 回で完結させる
  2. **`ResolvedSnapshot` を class ではなく struct で保持** + Dictionary value 直書き（参照経路の boxing 回避）
  3. **`_activeMask` / `_emptyMask` の `BitArray` 既存パターン継続**: ctor で 1 度確保し、毎フレーム `SetAll(false)` + 個別 `[i] = true` で書き換え。Allocation ゼロ。
  4. **PlayMode Performance テスト**: `Unity.PerformanceTesting` または `GC.GetTotalAllocatedBytes(true)` で 1000 フレーム回しても allocation = 0 byte を検証
  5. **計測**: `Profiler.BeginSample` を `OverlayInputSource.TryWriteValues` に挟んで Profiler で確認可能にする
  6. **Roslyn analyzer or code review checklist**: LINQ 禁止 / boxing 禁止 / `ToArray()` `ToList()` 禁止のレビュー項目化

### Risk 4: 旧 `expressionId` フィールドの silent drop（Req 4.4 / Req 8.1）

- **問題**: `JsonUtility.FromJson<T>` は未知フィールドを silent drop する（warning も出さない）。新スキーマで `expressionId` を Dto から削除すると、旧 JSON を読んでもエラーが出ない。
- **影響**: Req 4.4 / Req 8.1 違反。preview.2 の意図的な break が伝わらず、ユーザーが silent な動作不一致でハマる。
- **緩和策**:
  1. **生 JSON 文字列レベルで `"expressionId"` を検出**: `SystemTextJsonParser` の Preprocessor 段階（`PreprocessInputSourceOptions` (lines 309-369) のパターン）で OverlaySlot 相当ブロック内の `"expressionId"` キーを検索 → ヒットしたら `FormatException` で fail
  2. **キー検出範囲を絞る**: `defaultOverlays` 配列内 / `expressions[].snapshot.overlays` 配列内のみで検出（gaze_configs 等の他箇所の `expressionId` は無視）
  3. **テストケース**: `SystemTextJsonParserOverlaysTests.Parse_LegacyExpressionIdField_ThrowsFormatException` を追加し、固定 legacy JSON 文字列で fail を観測

### Risk 5: `FacialProfile` ctor 破壊的シグネチャ拡張の波及

- **問題**: `Slots` を追加すると ctor 引数が 7 個目に。positional 呼出箇所が広範に存在し、コンパイル赤期間が長くなる。
- **緩和策**:
  1. **named argument 強制**: ctor 引数を全て optional にし、named arg 呼出を強制（既存も既に named arg のテストが多い、例: `OverlayInputSourceTests.cs:57-62`）
  2. **Phase 1 の最初に ctor 拡張**: 後段の Adapter/Test 実装より先に Domain ctor を拡張完了させ、コンパイル赤期間を Phase 1 内に閉じ込める
  3. **既存 ctor を残す方針も検討**: preview.2 段階のため互換性は捨ててよいが、移行作業中の中間状態を緩和したい場合は overload を一時的に残す → Phase 8 で削除

### Risk 6: Adapter Bindings overlaySlot dropdown の SO 到達経路

- **問題**: `InputSystemAdapterBindingDrawer.cs` は `PropertyDrawer` で、`SerializedProperty` から `targetObject` を辿って `FacialCharacterProfileSO._slots` にアクセスする必要がある。`AdapterBindingBase` は `SerializeReference` で SO の子オブジェクトとして配置されているため、`property.serializedObject.targetObject as FacialCharacterProfileSO` で到達可能。
- **緩和策**:
  1. **既存パターン確認**: `InputSystemAdapterBindingDrawer.cs` 内で既に SO target にアクセスしている箇所 (例: action choices 取得) を流用
  2. **`Slots` が未設定 / null の場合のフォールバック**: dropdown choices 空 + warning HelpBox 表示「FacialCharacterProfileSO の Slots を先に宣言してください」
  3. **動的更新**: SO の `_slots` 編集を Drawer に反映するため、`schedule.Execute(...)` or `TrackPropertyValue` で再描画

---

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| **A: Extend OverlaySlotBinding 既存型** | `ExpressionId` を残しつつ `Snapshot` フィールド追加 | 移行緩和、ctor 互換維持 | 3 状態が曖昧（ExpressionId と Snapshot の両方が non-null の case の意味が不明）、Req 2.7 (旧 expressionId 撤去) 違反 | **却下** |
| **B: 新型 + 旧型削除（推奨）** | OverlaySlotBinding を完全置換 | 3 状態が型レベルで明確、preview.2 の破壊的方針と整合、JSON / SO スキーマも揃う | コンパイル赤期間あり、テスト書き換え多数 | **採用候補**。Phase 順序で赤期間最小化 |
| **C: 別型 OverlaySlotSnapshotBinding 並立** | 旧型を deprecated にしつつ新型を別名で追加 | 段階的移行 | preview.2 で後方互換を捨てる方針と矛盾、型が 2 種になり混乱、Req 8 違反 | **却下** |

**Preferred**: Option B (新型完全置換)。preview.2 段階で破壊的変更を許容している点（CLAUDE.md / steering tech.md "Key Technical Decisions" 参照）と Req 2.7 / Req 8 の明示的な要求から、最も整合する。

## Implementation Complexity & Risk

- **Effort**: **L (12-17 営業日)** — 11 Requirements を Domain → JSON → SO → Inspector → Sample → Tests と全層横断する。Inspector UI の 6 タブ再編が単独で 3-5 日。
- **Risk**: **Medium** — 既存パターン踏襲がほぼ可能（防御的コピー / Dictionary 事前展開 / ReadOnlyMemory / Tab UI / `cachedSnapshot` ベイク）。新規パターンは「JSON Preprocessor での旧フィールド検出」と「Inspector の動的 dropdown」のみ。コンパイル赤期間は Phase 1-3 で集中するが、Phase 順序で局所化可能。

---

## Research Items to Carry Forward (design phase)

1. **`Slots` を ReadOnlyMemory<string> vs HashSet<string> どちらにすべきか**: 線形検索で十分か、`Slots` が多数のときに `HashSet` が必要か。preview 段階では数十程度を想定し `ReadOnlyMemory<string>` で十分だが、Slots × Expressions × DefaultOverlays の計算量上限を design.md で明記する。

2. **`ExpressionSnapshot?` (Nullable<T>) の値型 boxing リスク**: `OverlaySlotBinding.Snapshot` を `Nullable<ExpressionSnapshot>` にすると、`HasValue` / `Value` アクセスで boxing 発生しないが、Dictionary value 経路や `out ExpressionSnapshot?` シグネチャでパフォーマンス影響を要確認。代替案: sentinel value (`ExpressionSnapshot.Empty`) + `IsEmpty` プロパティ。

3. **Inspector の `OverlaySlotBindingSerializable.animationClip` サンプリングタイミング**: Expression 本体の AnimationClip サンプリングは `OnSerializedObjectChanged` → `FlushAutoExport` で行われる。Overlay 側 AnimationClip も同経路に乗せると、Sample 数 × Slot 数 のサンプリングが発生。サンプリング負荷を design phase で計測し、必要なら debounce 拡張。

4. **`_slots` 編集時の Adapter Bindings dropdown 連動**: SO 上で `_slots` を変更した直後に Adapter Bindings の dropdown 選択肢が古いまま残ると Inspector の整合性が崩れる。Inspector 全体の SerializedObject TrackChange で再描画させる仕組みが必要。

5. **JSON `expressionId` 検出スコープの正確な制限**: `defaultOverlays[].expressionId` と `expressions[].snapshot.overlays[].expressionId` は拒否、`gaze_configs[].expressionId` と `_gazeConfigs[].expressionId` は維持。文字列レベルマッチングで scope を間違えないよう、検出ロジックを design.md でアルゴリズム化する。

---

## Document Status

- **Approach**: Brownfield codebase に対する破壊的スキーマ変更（preview.2）の gap analysis。Domain → JSON → SO → Inspector → Sample → Tests の 6 層を横断調査済み。
- **Output Location**: `.kiro/specs/overlay-clip-redesign/research.md`
- **Language**: 日本語 (spec.json `language: "ja"` に従う)。

## Next Steps

1. 本 gap analysis をレビューし、Risk / 推奨実装順序が許容可能か判断
2. 必要に応じて requirements.md を refine（Slots 上限数 / Snapshot Nullable 戦略 / Inspector debounce など研究項目をスコープに追加 or 別 spec へ切出し）
3. `/kiro:spec-design overlay-clip-redesign` で design phase へ進む。design.md では特に以下を確定する:
   - `OverlaySlotBinding.Snapshot` の Nullable 表現戦略（`ExpressionSnapshot?` vs sentinel）
   - Parser の旧 `expressionId` 検出スコープ正規表現
   - Inspector の `_slots` 動的 dropdown 連動メカニズム
   - PlayMode 性能テストの allocation 計測方法

---

## Design Decisions Log (resolved during design phase, 2026-05-11)

design.md "Decision Log" セクションで Carry-Forward Item 1-5 を確定済。要約:

| # | 研究項目 | 結論 | design.md 該当箇所 |
|---|---------|------|--------------------|
| 1 | `Slots` を ReadOnlyMemory<string> vs HashSet<string> | `ReadOnlyMemory<string>` 採用。preview.2 想定 (S≤十数) では線形検索 O(N) で十分。S × E のボトルネック発生時に preview.3 で再検討。 | §D1 |
| 2 | `ExpressionSnapshot?` の boxing リスク | `Nullable<ExpressionSnapshot>` を public API で採用、`OverlayInputSource` 内部経路では `bool HasSnapshot + ExpressionSnapshot` の非 nullable 2 フィールドに展開して boxing 完全回避。 | §D2 |
| 3 | overlay AnimationClip サンプリングタイミング | 既存 `OnSerializedObjectChanged` → `FlushAutoExport` debounce 経路に統合。Sample 数 × Slot 数の負荷増加は preview.2 では許容範囲。 | §D3, FacialCharacterProfileExporter Implementation Notes |
| 4 | `_slots` 編集時の Adapter Bindings dropdown 連動 | Inspector 内は `TrackPropertyValue`、PropertyDrawer 側は `schedule.Execute` polling のハイブリッド。choices は内容変更時のみ再生成。 | §D4, InputSystemAdapterBindingDrawer Responsibilities |
| 5 | JSON `expressionId` 検出スコープ | 文字列レベル状態機械。`defaultOverlays[N]` / `expressions[N].snapshot.overlays[N]` のキー位置のみ検出、`gaze_configs` 等は黒リスト化。Regex 不採用 (誤マッチ回避)。 | §D5, LegacyOverlayFieldDetector |
| 6 | PlayMode Performance 計測手法 (新規確定) | `GC.GetTotalAllocatedBytes(true)` 差分計測。Warm-up 10 フレーム → 1000 フレームの差分 = 0 byte assertion。`Unity.PerformanceTesting` 非採用。 | §D6, OverlayInputSourcePerformanceTests |

design phase で追加発見した設計決定:

- **`OverlaySlotBindingSerializableExtensions.GetState()` 抽出**: 状態判定ロジック (`(suppress, animationClip) => UIState`) を Serializable 上の拡張メソッドに切り出すことで Inspector UI のテスト容易性を確保 (Risk 2 緩和)。design.md OverlaySlotBindingSerializable Implementation Notes 参照。
- **`SlotKey` struct 採用**: `OverlayInputSource._resolvedBySlot` の Dictionary key を `(string ExprId, string Slot)` の readonly struct + `IEquatable<SlotKey>` 実装で boxing 完全回避。`null ExpressionId` を DefaultOverlays sentinel として再利用。design.md OverlayInputSource Service Interface 参照。
- **`SampleAssetsAreInSyncTests` (NEW) の追加**: dev/Samples~ 二重管理 drift を CI で検出する EditMode テスト (Risk 1 緩和)。Req 7.5 をテスト化。design.md Testing Strategy 参照。
- **Inspector タブ ID 定数の派生 Inspector 影響**: `TabExpressionsName` → `TabExpressionLibraryName` / `TabLayersName` / `TabBaseExpressionName` の 3 分割は派生 Inspector (InputSystem パッケージ) に revalidation trigger として影響。本 spec の Boundary Commitments §Revalidation Triggers に明記し、派生側の追従は別 PR とする。
