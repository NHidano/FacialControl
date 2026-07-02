# Changelog

すべての変更は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従います。

## 初回リリース

本パッケージはこれが初回リリースです。

### Breaking Changes

- **`AnimationEvent` 由来の遷移メタデータを撤去**: Expression の `transitionDuration` / `transitionCurvePreset` は `FacialCharacterProfileSO` Inspector の Expression 行だけで編集する方針に統一しました。AnimationClip 上の `AnimationEvent` (`FacialControlMeta_Set` など) は遷移メタデータとして扱いません。
- **自動マイグレーション無し**: 過去の AnimationClip に保存されていた遷移時間・遷移カーブは自動マイグレーションされません。必要な値はユーザーが `FacialCharacterProfileSO` / profile JSON 側へ手動で設定し直してください。
- 根拠: spec `preview1-polish-pack` Req 2.6 / task 8.2。

### Fixed

- Overlay slot（blink 等）の解決結果（default / 表情別 override / suppress）が active 表情の切替時に 1 フレームで瞬時に切替わり、目パチ（パチパチした見た目のポップ）や一瞬のリセットに見える不具合を修正。`OverlayInputSource` が解決結果の切替を検出した際、旧出力値から新出力値へ表情側と同期したクロスフェード（切替先 active 表情の `transitionDuration` / `transitionCurve`、active 解除時は既定リリース `Expression.DefaultTransitionDuration` + Linear）で補間するようにした。遷移中の ContributeMask は from ∪ target の union を維持し、suppress への切替もフェードアウト完了後に無効ソース化する。フォニーム予約 slot（a/i/u/e/o）はリップシンク応答性の「1 フレーム切替」仕様を維持するためクロスフェード対象外。典型例: RT（overlay weight）押下中に override 付き表情を ON/OFF しても、専用閉じ目 ⇄ 既定閉じ目が滑らかに遷移する。
- AnimationClip で登録した Expression の BlendShape weight が個別値を反映せず全て最大 (100) に飽和する不具合を修正。`AnimationClipExpressionSampler` が `blendShape.*` カーブ（Unity 標準 0..100 スケール）の値を正規化せず snapshot へ格納していたため、ドメイン / runtime apply 側の正規化 0..1 規約（`FacialController` の `×100`）と二重スケールになり、キーフレーム 30/40 が `×100` で 3000/4000 → 100 にクランプされていた。サンプラはカーブ値を `/100` して正規化 0..1 で格納し、`ExpressionClipBakery` は正規化 0..1 を `×100` して Unity 標準スケールでカーブへ書き込むよう統一した。これに伴い同梱 `MultiSourceBlendDemo` の `profile.json`（dev / Samples~ 両コピー）と SO `.asset` に残っていた 0..100 スケールの BlendShape 値を正規化 0..1 へ移行した（.anim カーブは元から 0..100 のため変更なし）。
- `FacialCharacterProfileSO` Inspector の Expression List / Default Overlays で Overlay の Suppress / Override 切替および override clip 割当が確実に保存されない不具合を修正。これらのハンドラは `SerializedProperty` を経由せず managed モデルを直接書き換えて `serializedObject.Update()` のみで終えていたため、`TrackSerializedObjectValue` による自動保存監視が発火せず、`EditorUtility.SetDirty` 任せの「次回の手動保存時にたまたま保存される」挙動になっていた。各ハンドラから自動保存予約 `ScheduleAutoSave()` を明示的に呼び、profile.json エクスポートとアセット保存を確実に走らせるようにした。

### Added

- **目線の目ボーン適用を `FacialController` に集約**: `FacialController` が profile ルートの `GazeBindingConfig` 群を `GazeBindingConfigResolver` で `InputSourceRegistry` の gaze 入力源（`{slug}:{expressionId}` / `.left` / `.right`）に解決し、単一の `GazeBonePoseProvider` を構築して `LateUpdate` 末尾（BoneWriter 適用後）で目ボーンへ localRotation を書き込むようにした。各入力 binding（OSC / InputSystem / iFacialMocap）は gaze 入力源の registry 登録のみを担い、目ボーンは回さない。この経路は入力方式非依存のため、**OSC 受信した gaze も設定のみで目ボーンに反映される**（従来は目ボーン適用 provider を持つ binding が InputSystem / iFacialMocap に限られ、OSC 受信 gaze はローカルモデルに反映されなかった）。bone path を持たない `GazeBindingConfig`（BlendShape 経路のみ）は構築対象外。`Cleanup` 時は provider の `Dispose` が目ボーンを初期回転へ復元する。
- Play モード突入時（`EditorApplication.playModeStateChanged` の `ExitingEditMode`）およびビルド開始時（`IPreprocessBuildWithReport.OnPreprocessBuild`）に、プロジェクト内の全 `FacialCharacterProfileSO`（派生型含む）を再サンプリングして `StreamingAssets/FacialControl/{SO 名}/profile.json` を自動エクスポートする `FacialCharacterProfileAutoExporter` を追加。これまで profile.json の更新は Inspector 編集の `TrackSerializedObjectValue` 起点のみだったため、クリップだけ差し替えてエクスポートを忘れた場合や、`AnimationClipExpressionSampler` の ÷100 スケール修正前に生成された旧 profile.json（0..100 スケール）が残っている場合に、古い JSON のまま Play / ビルドに進み全 BlendShape が 100% に飽和し得た。本フックにより、ランタイムが読む JSON が常に最新の正規化 0..1 値になる。エクスポートは冪等（内容が最新なら同一バイトを書くだけ）で、SO の `cachedSnapshot` はインメモリ再サンプリングのみ行いアセットを dirty にしない。

### Breaking changes

- Overlay は旧 `(slot, expressionId)` 参照モデルを廃止し、`(slot, suppress, snapshot)` の 3 状態モデルへ破壊的に変更しました。`defaultOverlays[]` と `expressions[].snapshot.overlays[]` は `slot` / `suppress` / `snapshot` を保持し、`suppress=false && snapshot=null` は `FacialProfile.DefaultOverlays` への fallback、`suppress=true` は明示抑制、`snapshot` 指定時は個別 overlay override を表します。
- `OverlaySlotBinding.ExpressionId` および ScriptableObject 側の `expressionId` フィールドは廃止しました。本リリースでは旧 JSON からの自動マイグレーションを提供せず、`defaultOverlays[]` または `expressions[].snapshot.overlays[]` に旧 `expressionId` が残っている場合は `SystemTextJsonParser` が field 名と path を含む `FormatException` で読み込みを拒否します。
- `FacialProfile.Slots` / JSON ルート `slots[]` / `FacialCharacterProfileSO._slots` を overlay slot 識別子の唯一の宣言元として追加しました。`Expression.Overlays`、`FacialProfile.DefaultOverlays`、Adapter Bindings の `overlaySlot` は、この slots 宣言に存在しない値を不正参照として扱います。
- GazeConfig は `InputSystemAdapterBinding._gazeConfigs` から `FacialCharacterProfileSO` ルート直下の `_gazeConfigs` へ昇格しました。InputSystem 側は入力結線のみを保持する構造に変わるため、既存 SO YAML は binding 内部の gaze configs を SO ルートへ移植する必要があります。
- `profile.json` の `schemaVersion` はバージョンを別管理しないポリシーに従い `"1.0"` に統一しました。旧開発過程で出現した `"2.0"` / `"2.1"` の JSON は migration なしで `"1.0"` への hand-edit が必要です。
- `InputSourceId` の正規表現を `^[a-zA-Z0-9_.-]{1,64}$` から `^[a-zA-Z0-9_.\-:]{1,64}$` に拡張し、`slug:sub` 合成キー（例: `input:analog-expression`）を JSON 経路でも正しく受理するよう修正しました。`AdapterSlug` 自身の regex は `:` 不許可のまま維持。これにより `SystemTextJsonParser` が profile.json 内の `input:analog-expression` を弾いていた警告が解消されます。
- `ExpressionSerializable.kind` (`ExpressionKind` enum: Digital/Analog) を撤廃し、`bool isGaze`（目線操作フラグ）に置換しました。Inspector の種別 dropdown は「目線操作」チェックボックスに統一され、目線操作以外の表情は「名前 / AnimationClip / 遷移時間」のみで設定可能になります。Layer 側の種別属性は元々存在しないため変更なし。自動マイグレーションは提供しません。サンプル `.asset` の YAML 上の `kind: 0|1` を `isGaze: 0|1` に置換しています（`_schemaVersion` は `"1.0"` のまま据え置き）。

### ⚠ BREAKING CHANGES — Adapter Binding アーキテクチャ移行 (spec `adapter-binding-architecture`)

> 本リリースは spec `adapter-binding-architecture` に基づく Adapter 結線モデルの全面刷新を含む。**旧バージョンの `FacialCharacterSO` / `*FacialControllerExtension` MonoBehaviour 経路 / reserved id (`controller-expr` / `keyboard-expr` / `osc` / `lipsync` / `input` 等) を前提とする scene / asset / JSON はロード・動作できない**。自動マイグレーションは提供しない（Req 8.1, 8.3）。移行手順は [`Documentation~/migration-guide.md`](Documentation~/migration-guide.md) を参照（Req 8.2, 8.5）。
>
> Phase 1 並走期（`_adapterBindings` と旧 `IFacialControllerExtension` の同時利用が runtime warning 付きで許容されていた期間）は本リリースで **終了** した。両経路の同時利用を検出する `Debug.LogWarning` および empty-list ゲートは撤去され、`FacialController.Initialize` は無条件で per-FC `LifetimeScope` を build する。

#### 削除型一覧（compile error 必至）

- `Hidano.FacialControl.Adapters.Playable.IFacialControllerExtension` — MonoBehaviour Extension 経路の I/F。`AdapterBindingBase` 派生 + `[FacialAdapterBinding]` 属性 + per-FC VContainer LifetimeScope に置換（Req 6.8）
- `Hidano.FacialControl.Adapters.InputSources.InputSourceFactory` — `(id, options)` ディスパッチ + JSON deserialize + reserved id チェック。slug-keyed の `Hidano.FacialControl.Adapters.InputSources.InputSourceRegistry` に置換し、責務を `Register(slug, source)` / `TryResolve("<slug>" or "<slug>:<sub>")` に縮小（Req 6.10）
- `Hidano.FacialControl.Domain.Models.InputSourceId.ReservedIds` / `IsReservedId` / `IsReserved` — reserved id 体系。`AdapterSlug` 値オブジェクト + `[FacialAdapterBinding(displayName: ...)]` 由来の slug 命名（kebab-case）に置換（Req 12.5, 12.6, D-13）
- `com.hidano.facialcontrol.osc` の `OscFacialControllerExtension` / `OscRegistration` — `OscReceiverAdapterBinding` / `ArKitOscAdapterBinding` に置換（Req 6.9）
- `com.hidano.facialcontrol.inputsystem` の `FacialCharacterSO` (派生 SO) / `FacialCharacterInputExtension` / `InputFacialControllerExtension` / `InputRegistration` / `FacialCharacterSOInspector` / `FacialCharacterSOAutoExporter` — `FacialCharacterProfileSO` の `[SerializeReference] List<AdapterBindingBase>` + `InputSystemAdapterBinding` + `InputSystemAdapterBindingDrawer` に置換（Req 6.4, 6.8）

#### 追加型一覧

- `Hidano.FacialControl.Domain.Adapters.AdapterBindingBase`（abstract `[Serializable]`、`Slug: public string` field、`OnStart(in AdapterBuildContext)` / `OnTick` / `OnLateTick` / `OnFixedTick` / `Dispose` の virtual no-op）
- `Hidano.FacialControl.Domain.Adapters.FacialAdapterBindingAttribute`（`AttributeTargets.Class`、`DisplayName` プロパティ）
- `Hidano.FacialControl.Domain.Models.AdapterSlug`（`readonly struct`、`TryParse` / `Parse` / `FromDisplayName` / `TryParseComposite`、`^[a-zA-Z0-9_.-]{1,64}$`）
- `Hidano.FacialControl.Adapters.DependencyInjection.AdapterBuildContext`（`readonly struct`、`Profile` / `BlendShapeNames` / `InputSourceRegistry` / `TimeProvider` / `HostGameObject` / `LipSyncProvider`）
- `Hidano.FacialControl.Adapters.DependencyInjection.AdapterBindingHost`（VContainer の `IStartable` / `ITickable` / `ILateTickable` / `IFixedTickable` / `IDisposable` を実装し binding lifecycle に委譲、例外時 `_skipped = true` で以後 no-op）
- `Hidano.FacialControl.Adapters.DependencyInjection.FacialControlAppLifetimeScope` / `FacialControllerLifetimeScope`（VContainer 1.17.x ベース。`SubsystemRegistration` で auto-spawn する singleton + per-FC child scope）
- `Hidano.FacialControl.Adapters.InputSources.IInputSourceRegistry` / `InputSourceRegistry`（slug-keyed の `Dictionary<string, IInputSource>`、`<slug>` / `<slug>:<sub>` 形式）
- `Hidano.FacialControl.Editor.Inspector.AdapterBindings.AdapterBindingDiscovery`（`TypeCache.GetTypesWithAttribute<FacialAdapterBindingAttribute>()` ベースの auto-discovery、displayName 重複時 FQTN suffix）
- `Hidano.FacialControl.Editor.Inspector.AdapterBindings.AdapterBindingsListView` / `AdapterBindingAddDropdown` / `MissingAdapterPlaceholderElement`（UI Toolkit `ListView`、Add / Remove / Reorder、null 要素 placeholder、PropertyDrawer 例外 fallback）
- `Hidano.FacialControl.Editor.Inspector.AdapterBindings.FacialCharacterProfileAssetGuard`（`AssetModificationProcessor.OnWillSaveAssets` で slug 重複 save block）
- core 同梱 `Samples~/MultiSourceBlendBasicSample/`（HUD なし、Mock binding 2 種 + JSON プロファイル + Runner、`Tools > FacialControl > Run MultiSourceBlend Basic Sample` から 1 click 実行）

#### `FacialCharacterProfileSO` の field 追加

- `_adapterBindings: List<AdapterBindingBase>` を `[SerializeReference]` で追加。空 list 許容、同型 binding 複数可（Req 2.1, 2.2, 2.4）
- `abstract` 修飾を解除し `[CreateAssetMenu(menuName = "FacialControl/Facial Character Profile")]` を付与（Req 2.1, 6.6）
- 公開 API: `IReadOnlyList<AdapterBindingBase> AdapterBindings`

#### `FacialController` の lifecycle 改修

- `Initialize` で per-FC `LifetimeScope` を build し、`AdapterBindings` 各要素を `Lifetime.Scoped` で `AdapterBindingHost` に wrap して register する
- `LateUpdate` 内の `ApplyExtensions` / `BuildAdditionalInputSources` 経路は撤去。binding lifecycle は VContainer の `IStartable` / `ITickable` / `ILateTickable` / `IFixedTickable` / `IDisposable` 経由で駆動される
- `Cleanup` は child scope を `Dispose()` してから既存処理を行う

#### `layer.inputSources[].id` の slug 化（Req 12.7、D-13）

- 旧 reserved id（`controller-expr` / `keyboard-expr` / `osc` / `lipsync` / `input` 等）は **すべて廃止**
- 新形式は `<slug>` または `<slug>:<sub>` の 2 種類
  - `<slug>`: binding 1 個が登録する primary `IInputSource`（slug は当該 binding の `Slug` field）
  - `<slug>:<sub>`: binding が複数 `IInputSource` を register する場合の sub-id（例: `OscReceiverAdapterBinding` の `osc:secondary`）
- slug は Inspector で binding を Add した瞬間に `displayName.ToLowerInvariant()` の kebab-case 自動採番（例: `"OSC"` → `"osc"`、`"Input System"` → `"input-system"`、`"ARKit / PerfectSync"` → `"arkit-perfectsync"`）。手動編集も可能
- 同一 SO 内の slug 重複時は Inspector が error indicator + summary banner を表示し、`AssetModificationProcessor` が save をブロックする
- 第三者拡張は `x-` プレフィックス推奨（既存 `[a-zA-Z0-9_.-]{1,64}` ルール継続）

#### 新規アダプタパッケージ追加時の core への影響

- core パッケージへの compile-time 参照を増やさない（Req 1.5, 1.6, 4）
- 各アダプタは「`AdapterBindingBase` 派生 1 クラス + `[FacialAdapterBinding]` 属性 + 任意 `[CustomPropertyDrawer]`」の 3 点を当該パッケージで提供すれば、core Inspector の Add ドロップダウンに自動列挙される

### Added

- Overlay slot 宣言 (`FacialProfile.Slots` / `ProfileSnapshotDto.slots` / `FacialCharacterProfileSO._slots`) を追加。slot 重複と未宣言 slot 参照を検出する `ValidateSlotReferences()` と `InvalidSlotReference` も追加した。
- `OverlaySlotBinding` / `OverlaySlotBindingDto` / `OverlaySlotBindingSerializable` を 3 状態 overlay モデルへ更新し、default fallback / suppress / snapshot override を Domain / JSON / SO の全経路で同じ意味として扱うようにした。
- `FacialCharacterProfileSOInspector` を「表情ライブラリ / レイヤー / ベース表情 / 目線 / Adapter Bindings / Debug」の 6 タブ構成へ再編し、表情ライブラリタブに Slots 宣言、Default Overlays、Expression 行ごとの Overlays UI を追加。Adapter Bindings の `overlaySlot` は Slots 宣言から dropdown 生成される。
- `Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig` — Vector2 アナログ入力で両目を同時駆動するアナログ表情の汎用 `[Serializable]` 基底クラス。両目ボーン path / 初期回転 / yaw・pitch local 軸 / 可動範囲 (上下＋左右内外) / Look 4 系統 AnimationClip / 焼き付け sample 配列を保持。InputSystem 連携の `GazeExpressionConfig` はこのクラスを継承し `InputActionReference` だけ追加する形に再構成された (inputsystem 側参照)。
- `Hidano.FacialControl.Adapters.ScriptableObject.GazeBlendShapeSampleEntry` (旧 inputsystem 側から移管) — Look clip の time=0 サンプル結果 1 件 (`blendShapeName` / `weight`)。
- `Hidano.FacialControl.Adapters.Bone.GazeBonePoseProvider` (旧 inputsystem 側から移管) — `GazeBindingConfig` を毎フレーム評価して左右目ボーンに `localRotation` を直接書込む目線ボーン専用 provider。入力方式に依存しない。
- `Hidano.FacialControl.Adapters.Bone.GazeBoneBinding` (新設) — `GazeBindingConfig` と `IAnalogInputSource` のペアを保持する readonly struct。`GazeBonePoseProvider` のコンストラクタが受け取る形にし、入力源解決の責務を呼出側に閉じ込めた。
- `Hidano.FacialControl.Editor.Sampling.GazeClipBlendShapeSampler` (旧 inputsystem 側から移管) — 4 系統 Look clip の time=0 における BlendShape weight を抽出する `AnimationUtility` ベースの Editor ヘルパ。
- `Hidano.FacialControl.Editor.AutoExport.FacialCharacterProfileExporter` — profile.json 出力 + AnimationClip の time=0 サンプリング → `cachedSnapshot` 反映を担う汎用 Editor exporter。InputSystem 連携の `FacialCharacterSOAutoExporter` は本クラスへ delegate する形に再構成された (inputsystem 側参照)。
- `Hidano.FacialControl.Editor.Inspector.FacialCharacterProfileSOInspector` — `[CustomEditor(typeof(FacialCharacterProfileSO), editorForChildClasses: true)]` の汎用 UI Toolkit 基底 inspector。Layers / Expressions / Gaze (bone+clip) / Reference Model / Debug / Validation / 自動保存 (profile.json) を提供。派生クラス用 virtual hook (`OnResolveDerivedSerializedProperties` / `OnBuildPreLayersSections` / `OnBuildAnalogExpressionInputSourceFields` / `FindGazeConfigsProperty` / `ResolveAnalogSourceIdChoices` / `FlushAutoExport` / `ValidateAnalogExpression`) を提供し、入力方式固有 UI（InputActionAsset 選択、ExpressionBindings、`InputActionReference` フィールド、analog_bindings.json 出力）の重ね合わせを許容する。
- `Hidano.FacialControl.Domain.Models.TriggerMode` enum (`Hold=0 / Toggle=1`) — ボタン入力で表情をトリガーする際の動作モード。`Hold` (押下中のみ ON) を新規バインディングの既定値とする。
- `Hidano.FacialControl.Domain.Models.InputBinding` に `TriggerMode` フィールドを追加。既存の 2-arg コンストラクタは `TriggerMode.Hold` を初期値として呼び出す 3-arg コンストラクタに委譲する後方互換ラッパー。
- `Hidano.FacialControl.Domain.Models.AnalogBindingDirection` enum (`Bipolar=0 / Positive=1 / Negative=2`) — gaze 4 系統 (LookLeft / LookRight / LookUp / LookDown) のように 1 軸入力を符号で振り分けて複数 BlendShape clip に流すための input filter。
- `AnalogBindingEntry.Scale: float` (default `1f`) — clip 由来 binding で keyframe weight を保持して runtime で `raw * Scale` を加算するためのフィールド。
- `AnalogBindingEntry.Direction: AnalogBindingDirection` (default `Bipolar`) — 上記 input filter。
- `AnalogBindingEntry` に Scale / Direction を明示する 7 引数 ctor を追加（既存 5 引数 ctor は default 値で互換維持）。
- `AnalogBlendShapeInputSource` で direction filter と scale 倍率を適用（Bipolar 既定で従来挙動と完全一致）。

### Changed

- `OverlayInputSource` は expressionId 参照ではなく、現在 Expression 内の slot binding と `FacialProfile.DefaultOverlays` から overlay snapshot を解決する方式に変更。通常フレームの追加 GC を発生させない事前解決構造を維持する。
- `FacialCharacterProfileExporter` は default overlays と Expression overlays の `AnimationClip` を time=0 でサンプリングし、`cachedSnapshot` / JSON の `snapshot` として出力する。`suppress=true` または default fallback の場合は overlay snapshot を空扱いにする。
- MultiSourceBlendDemo の sample JSON / SO asset を新 overlay schema へ移行し、旧 `blink_overlay` expressionId 参照は `smile` / `smile_closed_eye` の slot overlay snapshot または suppress として inline 化した。
- `Adapters.Json.Dto.AnalogBindingEntryDto` に `scale: float` / `direction: string` を追加。旧スキーマ JSON は欠落フィールドを default 値 (`scale=1`, `direction="bipolar"`) で fallback する。
- `Adapters.Json.AnalogInputBindingJsonLoader` で scale / direction の parse / serialize を追加。不正 direction 文字列は warning + Bipolar 扱い。
- 表情遷移時間のデフォルト値を `0.25 秒` から `1/15 秒 (≒0.0667 秒)` に変更。`Domain.Models.Expression.DefaultTransitionDuration` を新設し、`Expression` / `ExpressionSnapshot` / `ExpressionSerializable` / `ExpressionSnapshotDto` / `ExpressionTriggerInputSourceBase.DefaultReleaseTransitionDuration` / `AnimationClipExpressionSampler.DefaultTransitionDuration` / `ARKitDetector` 既定生成 / `SystemTextJsonParser` snapshot 欠落フォールバック / `Templates/default_profile.json` がすべて参照する。後方互換は持たない。
- ExpressionTrigger 系 IInputSource の予約 ID を `controller-expr` / `keyboard-expr` の二系統から単一の `input` に統合。`InputSystem.Action` 名で device を抽象化する設計のため、コア側で device 種別ごとに ID を分ける必要がなくなった。`Domain.Models.InputSourceId.ReservedIds` から `controller-expr` / `keyboard-expr` を削除し、`input` を新規追加。InputSystem 連携の `ExpressionTriggerInputSource` も二系統 (Controller/Keyboard) のクラス分離を撤廃し、単一 `InputReservedId = "input"` に統一。後方互換は持たない。既存プロファイル JSON / SO の `inputSources[].id` が `controller-expr` / `keyboard-expr` のままだと parse 時に warning + skip されるため、`input` (1 件) に書き換える必要がある。


### ⚠ BREAKING CHANGES

> 本リリースは spec `inspector-and-data-model-redesign` に基づく Domain モデル / 中間 JSON schema / InputSystem 連携の全面改修を含みます。**旧バージョンの `FacialCharacterSO` / JSON / Scene / Prefab はロードできません**。アップグレード前に必ず [`Migration Guide v0.x → v1.0`](../com.hidano.facialcontrol.inputsystem/Documentation~/MIGRATION-v0.x-to-v1.0.md) の手順で資産を変換してください（Req 10.1, 10.6）。

#### 中間 JSON schema 破壊

- `schemaVersion` を `"1.0"` 固定としました（バージョンを別管理しないため）。`SystemTextJsonParser` は `schemaVersion != "1.0"` を `Debug.LogError` + `NotSupportedException` で拒否します
- `expressions[]` を `id / name / layer / layerOverrideMask: List<string> / snapshot: ExpressionSnapshotDto` の snapshot table 形式に再構成。旧 `transitionDuration / transitionCurve / blendShapeValues / layerSlots` field を撤去
- top-level `rendererPaths[]` を新設し、各 Expression snapshot の `rendererPaths[]` がそのサブセットであることを保証

#### Domain モデルの撤去

- `Hidano.FacialControl.Domain.Models.LayerSlot` — `LayerOverrideMask` (`[Flags] enum : int`、32 bit) に置換
- `Hidano.FacialControl.Domain.Models.BonePose` / `BonePoseEntry` — `BoneSnapshot` (`ReadOnlyMemory<BoneSnapshot>`) に統合
- `Hidano.FacialControl.Domain.Models.FacialProfile.BonePoses` プロパティ — Expression snapshot 経路に一元化
- `Hidano.FacialControl.Domain.Models.AnalogMappingFunction` — InputAction Asset の processors チェーンに置換
- `Hidano.FacialControl.Domain.Services.AnalogMappingEvaluator` — 上記に伴い物理削除
- `Hidano.FacialControl.Domain.Models.Expression` の独立 field（`TransitionDuration / TransitionCurve / BlendShapeValues / LayerSlots`） — `SnapshotId` 参照に集約
- `TransitionCurve` enum — `TransitionCurvePreset` enum (`Linear=0 / EaseIn=1 / EaseOut=2 / EaseInOut=3`) に置換

#### InputSystem 連携の撤去

- `KeyboardExpressionInputSource` / `ControllerExpressionInputSource` MonoBehaviour — `ExpressionInputSourceAdapter` 1 個に統合（`com.hidano.facialcontrol.inputsystem` パッケージで管理）
- `ExpressionBindingEntry.Category` field — `InputDeviceCategorizer.Categorize(bindingPath)` で自動分類
- `InputSourceCategory` enum — 参照ゼロ確認のうえ撤去（OSC 別 spec で再導入の余地）
- `FacialCharacterSO.GetExpressionBindings(InputSourceCategory category)` — 引数なし版に縮退
- `AnalogBindingEntry.Mapping` field — `*.inputactions` 内の processors 文字列に移行

### Added

#### Domain 層

- `LayerOverrideMask`（`[Flags] enum : int`、32 bit）— レイヤーオーバーライド bit マスク
- `BlendShapeSnapshot`（`readonly struct`、`RendererPath / Name / Value`）
- `BoneSnapshot`（`readonly struct`、`BonePath / Position(X,Y,Z) / Euler(X,Y,Z) / Scale(X,Y,Z)`）
- `ExpressionSnapshot`（`Id / TransitionDuration / TransitionCurvePreset / BlendShapes / Bones / RendererPaths`、防御コピー + `ReadOnlyMemory<T>` 公開）
- `TransitionCurvePreset` enum（`Linear=0 / EaseIn=1 / EaseOut=2 / EaseInOut=3`）
- `Domain.Services.ExpressionResolver` — `TryResolve(snapshotId, Span<float> blendShapeOutput, Span<BoneSnapshot> boneOutput)` の 0-alloc preallocated 解決経路

#### Adapters / Editor 層

- `Editor/Sampling/IExpressionAnimationClipSampler` interface と `AnimationClipExpressionSampler` 実装 — `AnimationUtility.GetCurveBindings` / `GetEditorCurve(...).Evaluate(0f)` で AnimationClip → `ExpressionSnapshot` をサンプリング
- AnimationEvent 経由のメタデータ運搬規約（予約 functionName `FacialControlMeta_Set`、key `transitionDuration` / `transitionCurvePreset`）
- 中間 JSON schema v2.0 DTO（`ExpressionSnapshotDto` / `BlendShapeSnapshotDto` / `BoneSnapshotDto`）
- `FacialCharacterSOAutoExporter` の `OnWillSaveAssets` 経路 — 200ms 超で `EditorUtility.DisplayProgressBar` 発火、サンプリング失敗時は当該 SO の save abort + `Debug.LogError`
- `FacialCharacterSOInspector` UI Toolkit 全面改修 — AnimationClip ObjectField / Layer DropdownField / LayerOverrideMask MaskField / read-only RendererPath summary、validation エラー時の HelpBox + Save 無効化、Guid 自動採番、AnimationClip 名からの Name 派生

### Changed

- `Domain.Models.AnalogBindingEntry` を `SourceId / SourceAxis / TargetKind / TargetIdentifier / TargetAxis` の 5 field に縮退（Mapping は Adapters 側 `InputActionReference` の processors に移管）
- `BonePoseComposer` の入力型を `BoneSnapshot` 経路に統一
- `ExpressionCreatorWindow` を AnimationClip ベイク経路に改修（`AnimationUtility.SetEditorCurve` + `SetAnimationEvents`）

### Removed

- `Domain.Models.LayerSlot`、`Domain.Models.BonePose`、`Domain.Models.BonePoseEntry`、`Domain.Models.AnalogMappingFunction`、`Domain.Services.AnalogMappingEvaluator`
- `FacialProfile.BonePoses` プロパティ
- 旧 `BonePoseDto` / `BonePoseEntryDto` / `LayerSlotDto` / `AnalogMappingFunctionSerializable`

初回リリースで提供する 3 パッケージ構成（コア + `com.hidano.facialcontrol.osc` + `com.hidano.facialcontrol.inputsystem`）。

### サブパッケージ構成

- 新パッケージ **`com.hidano.facialcontrol.osc`** — OSC 関連実装（`OscReceiver` / `OscSender` / `OscDoubleBuffer` / `OscMappingTable` / `OscReceiverPlayable` / `OscInputSource` / `OscOptionsDto`）+ `OscRegistration` ヘルパー + `OscFacialControllerExtension` MonoBehaviour
- 新パッケージ **`com.hidano.facialcontrol.inputsystem`** — InputSystem 関連実装（`InputSystemAdapter` / `FacialInputBinder` / `ControllerExpressionInputSource` / `KeyboardExpressionInputSource` / `InputBindingProfileSO` / `ExpressionTriggerOptionsDto` / `InputBinding`）+ `InputRegistration` ヘルパー + `InputFacialControllerExtension` MonoBehaviour
- コア `Hidano.FacialControl.Adapters` は `Unity.InputSystem` / `uOSC.Runtime` 非参照。表情切替を API から呼ぶだけのユーザーは uOSC・InputSystem インストール不要
- `IFacialControllerExtension` (`Hidano.FacialControl.Adapters.Playable`) — `FacialController` 初期化時に同 GameObject の拡張から `InputSourceFactory` に追加登録するための I/F
- `InputSourceFactory.RegisterReserved<TOptions>(...)` — 予約 id を含む任意 id の登録 API（公式サブパッケージ向け）



### Added

#### Domain 層
- `FacialProfile`、`Expression`、`BlendShapeMapping`、`LayerDefinition`、`LayerSlot` ドメインモデル
- `ExclusionMode`（LastWins / Blend）、`TransitionCurveType` enum
- `TransitionCurve` 構造体（Linear / EaseIn / EaseOut / EaseInOut / Custom）
- `TransitionCalculator` — 遷移カーブ評価サービス
- `ExclusionResolver` — LastWins クロスフェード / Blend 加算排他ロジック
- `LayerBlender` — レイヤー優先度ベースのウェイトブレンドと layerSlots オーバーライド
- `ARKitDetector` — ARKit 52 / PerfectSync の完全一致検出とレイヤーグルーピング
- `IJsonParser`、`IProfileRepository`、`ILipSyncProvider`、`IBlinkTrigger` インターフェース
- `FacialControlConfig`、`FacialState`、`FacialOutputData` 構造体
- `FacialProfile.RendererPaths` — Renderer パスの保持（JSON / SO 双方で同期）
- `InputBinding`（readonly struct）— ActionName と ExpressionId を値ベースで保持する Domain 層の入力バインディングモデル

#### Application 層
- `ProfileUseCase` — プロファイル読み込み・再読み込み・Expression 取得
- `ExpressionUseCase` — Expression のアクティブ化・非アクティブ化
- `LayerUseCase` — レイヤーウェイト更新とブレンド出力計算
- `ARKitUseCase` — ARKit / PerfectSync 検出と Expression・OSC マッピング自動生成

#### Adapters 層
- `SystemTextJsonParser` — System.Text.Json ベースの JSON パース / シリアライズ
- `FileProfileRepository` — ファイルシステムからのプロファイル読み書き
- `NativeArrayPool` — GC フリーの NativeArray プール管理
- `AnimationClipCache` — LRU 方式の AnimationClip キャッシュ
- `PropertyStreamHandleCache` — BlendShape → PropertyStreamHandle キャッシュ
- `LayerPlayable`（ScriptPlayable）— NativeArray ベースの補間計算と排他モード処理
- `FacialControlMixer`（ScriptPlayable）— レイヤーウェイトブレンドと最終出力統合
- `PlayableGraphBuilder` — FacialProfile からの PlayableGraph 構築
- `OscDoubleBuffer` — ロックフリーのダブルバッファリング
- `OscReceiver` / `OscSender` — uOsc ベースの OSC 送受信
- `OscReceiverPlayable` — PlayableGraph への OSC 受信統合
- `OscMappingTable` — OSC アドレスと BlendShape のマッピング管理
- `FacialProfileSO`（ScriptableObject）— JSON への参照ポインター（RendererPaths・使用モデル参照を含む）
- `FacialProfileMapper` — FacialProfile ⟷ FacialProfileSO 変換（RendererPaths 同期対応）
- `FacialController`（MonoBehaviour）— メインコンポーネント（Activate / Deactivate / LoadProfile / ReloadProfile）
- `InputSystemAdapter` — InputAction Asset との連携（Button / Value 両対応）
- `InputBindingProfileSO`（ScriptableObject）— InputActionAsset・ActionMap 名・バインディングペア（Action ⟷ ExpressionId）を永続化するアセット
- `FacialInputBinder`（MonoBehaviour）— `InputBindingProfileSO` を読み込み、`InputSystemAdapter` 経由で Action と Expression をバインドするシーン配置用コンポーネント
- `Runtime/Adapters/Input/FacialControlDefaultActions.inputactions` — デフォルト InputAction Asset（Xbox コントローラの LT/RT バインディング含む）

#### Editor 拡張
- `FacialControllerEditor` — FacialController の Inspector カスタマイズ
- `FacialProfileSOEditor` — FacialProfileSO の Inspector カスタマイズ（プロファイル管理を Inspector に統合）
  - Expression の一覧表示・検索フィルタ・追加/削除（Unity 標準 List UI）
  - BlendShape の Weight 値編集・追加/削除・検索付きドロップダウン
  - レイヤー一覧表示・インライン編集・ドラッグ順序による優先度設定
  - JSON インポート / エクスポート・JSON 上書き保存
  - 使用モデル指定と RendererPaths 自動検出
- UI Toolkit スタイル共通定義
- `ExpressionCreatorWindow` — BlendShape スライダーでリアルタイムプレビューしながら Expression 作成
- `PreviewRenderUtility` ラッパー（カメラ / ライティング / RenderTexture 管理）
- `ARKitDetectorWindow` — ARKit / PerfectSync 自動検出 Editor UI
- `InputBindingProfileSOEditor` — UI Toolkit ベースの Inspector。ActionMap / Action / Expression ドロップダウンの自動列挙とバインディング行の追加・削除をサポート
- MenuItem `FacialControl/新規プロファイル作成` — `ProfileCreationDialog` を開き GUI だけで `FacialProfileSO` + JSON を生成（JSON 手書き不要の GUI ファースト導線）
- `ProfileCreationData.NamingConvention`（VRM / ARKit / None）と `BuildSampleExpressions()` — 選択した命名規則に合わせて `smile` / `angry` / `blink` の雛形 Expression を自動生成
- `Editor/Common/BlendShapeNameProvider.cs` — 参照モデル（`GameObject` / `FacialProfileSO`）から BlendShape 名を収集する Editor 共通ユーティリティ
- `ARKitEditorService.MergeIntoExistingProfile()` — ARKit / PerfectSync 検出結果を既存 `FacialProfileSO` にマージ（`ARKitDetectorWindow` の UI から呼び出し可能）

#### サンプル
- `Samples~/MultiSourceBlendDemo` — 同一レイヤーに `controller-expr` + `keyboard-expr` を並置し、ウェイトブレンドの挙動を OnGUI HUD で目視確認する PlayMode サンプル。Scene (`MultiSourceBlendDemo.unity`) / FacialProfileSO / InputBindingProfileSO / JSON プロファイル / HUD スクリプト / README を同梱し、ユーザーは Scene を開いて Character の子にモデルを配置するだけで動作する（モデルはライセンスの都合で同梱しない）

#### テンプレート
- `Templates/default_profile.json` — デフォルト 3 レイヤー + 基本 Expression（default, blink, gaze_follow, gaze_camera）

#### ドキュメント
- 全公開 API の XML コメント
- クイックスタートガイド（`Documentation~/quickstart.md`）
- JSON スキーマリファレンス（`Documentation~/json-schema.md`）
- `README.md` に「既知の制限とロードマップ」節を新設（Addressables 対応方針 / 将来の `IProfileJsonLoader` 抽象化計画）

### Changed

#### Editor 拡張
- プロファイル管理機能を `ProfileManagerWindow` から `FacialProfileSOEditor`（Inspector）に統合
- JSON ファイルパスを読み取り専用表示に変更（インポート機能で代替）
- Expression の追加/削除を Unity 標準 List UI に置き換え
- BlendShape 選択ドロップダウンに検索入力ボックスを追加
- レイヤー優先度をドラッグ順序で設定し、数値はラベル表示のみに変更
- 「参照モデル」の表記を「使用モデル」に統一
- 使用モデルセクションを Inspector 最上部に移動
- JSON ファイルセクションを Inspector 最下部に移動
- 使用モデルに Hierarchy 上の GameObject をアタッチ可能に変更
- ARKit 検出ツールの JSON 保存後に `AssetDatabase.Refresh` を実行するよう修正
- JSON インポートで JsonFilePath 未設定時もファイル選択ダイアログを表示するよう改善
- `FacialProfileSOEditor` / `ExpressionCreatorWindow` の BlendShape 名入力を TextField から検索付きドロップダウン（`BlendShapeNameProvider` 連携）に変更してタイポ耐性を向上
- `ARKitDetectorWindow` に既存 `FacialProfileSO` へのマージ UI を追加（`MergeIntoExistingProfile()` 連携）

#### Domain 層
- `ARKitDetector` の検出仕様を見直し、完全一致判定を修正

#### ドキュメント
- `Documentation~/quickstart.md` を GUI ファースト手順に全面刷新（プロファイル作成ダイアログ → 使用モデル指定 → キーコンフィグ設定の順序）
- `README.md` のクイックスタート節を 4 ステップに刷新（GUI ファースト導線へ）

### Breaking Changes

> 本節の破壊的変更は破壊的変更ポリシー（`docs/requirements.md` の FR-001「表情プロファイル管理」内の「後方互換性: 開発段階では破壊的変更を許容」規定）に基づく。移行手順の詳細は [`docs/migration-guide.md`](../../../docs/migration-guide.md) を参照。

- `InputSystemAdapter` を `MonoBehaviour` から純粋 C# クラス（`IDisposable`）へ変更
  - 移行方法: GameObject へのアタッチを止め、`new InputSystemAdapter(facialController)` でインスタンスを生成する
  - 終了処理は `OnDisable()` の代わりに `Dispose()` を呼び出す
  - シーン内でキーコンフィグを扱う場合は新設の `FacialInputBinder` コンポーネントを使用する

- FacialProfile JSON の `layers[].inputSources` を**必須フィールド化**し、暗黙の `legacy` フォールバックを廃止
  - `inputSources` が欠落または空配列のレイヤーは `FormatException` でロードが失敗する
  - 予約 ID: `osc` / `lipsync` / `input` / `analog-blendshape` / `analog-bonepose`。サードパーティ拡張は `x-` プレフィックスを使用
  - 移行方法: 既存プロファイル JSON の各レイヤーに `"inputSources": [{ "id": "input", "weight": 1.0 }]` 等の配列を明示的に追記する（同梱サンプルの移行例は `docs/migration-guide.md` および `StreamingAssets/FacialControl/*_profile.json` を参照）
  - 根拠: `docs/requirements.md` FR-001（開発段階の破壊的変更許容）、および spec `layer-input-source-blending` の D-5 / R3.2 / R7.3 / R7.4

- 予約 ID `legacy` の廃止
  - 旧バージョンで `legacy` を用いて Expression パイプライン全体を 1 本の入力源として温存していた挙動は削除された
  - 移行方法: Expression 駆動のみを行うレイヤーは `inputSources: [{ "id": "input", "weight": 1.0 }]` へ置き換える
  - 根拠: spec `layer-input-source-blending` の D-1 / D-5 / D-6 / R1.7

- `InputBindingProfileSO` に `InputSourceCategory` フィールドを追加、既定値は `Controller`
  - 既存 Asset は本フィールドを持たないため、Unity のシリアライズ機構により初回ロード時に既定値 `Controller` が暗黙的に付与される
  - キーボード専用バインディングの Asset は `Keyboard` に明示変更しないと `ControllerExpressionInputSource` 側へトリガーが流れる挙動変更となるため、**全ての既存 Asset を Project ビューで一度レビューして Category を再設定する必要がある**
  - 手順の詳細は [`docs/migration-guide.md`](../../../docs/migration-guide.md) を参照
  - 根拠: spec `layer-input-source-blending` の R5.1 / R5.7 / R7.4

### Removed

- `ProfileManagerWindow`（Inspector 統合により不要）
- プロファイル情報のレイヤー数・Expression 数の冗長な表示
- JSON 読み込みボタン（Inspector 表示時の自動読み込みで代替）
- クローン作成ボタン（Unity 標準のアセット複製で代替）
- `Assets/Samples/TestExpressionToggle.cs`（`FacialInputBinder` + `com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/MultiSourceBlendDemoInputBinding.asset` への移行により不要）
