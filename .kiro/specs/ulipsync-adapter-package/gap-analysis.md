# Gap Analysis: `com.hidano.facialcontrol.lipsync` (uLipSync 連携アダプタパッケージ)

言語: 日本語 / 日付: 2026-05-07 / 対象 spec: `.kiro/specs/ulipsync-adapter-package/`

## 概要 (Analysis Summary)

- 本パッケージは新規 UPM パッケージだが、コア側は既に必要な契約 (`ILipSyncProvider` / `LipSyncInputSource` / `AdapterBindingBase` / `AdapterBuildContext` / `IInputSourceRegistry` / `LayerUseCase.additionalInputSources`) を **完備しており、コア改変は一切不要**。
- 主要リスクは AnimationClip 形式エントリの time-0 スナップショット抽出（既存 `AnimationClipCache` は逆方向のため流用不可）と Editor 側 `[SerializeReference]` 多態リスト Drawer の新規作成（既存にテンプレートなし）。
- Zero-frame settle は当初想定より単純: `LayerInputSourceAggregator` は per-layer バッファを毎フレーム `Array.Clear` で初期化するため、`LipSyncInputSource.TryWriteValues == false` だけで既にレイヤー値はゼロに収束する（追加の打ち消し機構不要）。
- 推定工数: **L (1〜2 週間)**、リスク: **Medium**（uLipSync 内部構造への依存と PropertyDrawer 新規実装が主因）。
- 推奨アプローチ: **Option B（新規パッケージ追加）** — 既存 OSC / InputSystem パッケージと同じ姉妹パッケージ構造を踏襲し、コアは触らない。

---

## 1. 要件 × 既存資産マッピング

| Req | 区分 | 既存資産 / 必要なラッパー |
|---|---|---|
| 1. パッケージ構成と配布 | **完全新規** | 新パッケージ `com.hidano.facialcontrol.lipsync` を `FacialControl/Packages/` 配下に新設。既存 `com.hidano.facialcontrol.osc` / `com.hidano.facialcontrol.inputsystem` の package.json / asmdef レイアウトを雛形流用可。 |
| 2. Prefab-Clean Contract | **部分流用** | `com.hidano.facialcontrol.inputsystem` の `InputSystemAdapterBinding` が `ctx.HostGameObject.AddComponent<ExpressionInputSourceAdapter>()` → `Dispose` で `Object.Destroy` する手順を完全踏襲可能。`com.hidano.facialcontrol.osc` の `OscReceiverAdapterBinding` も `OscReceiverHost` で同パターン (line 140 / 170)。 |
| 3. Inline serialization (No-New-SO) | **部分流用** | `OscReceiverAdapterBinding` の `[SerializeField]` フィールド + `FacialCharacterProfileSO._adapterBindings` の inline `[SerializeReference]` パターンを踏襲。Analyzer Profile フォールバックは `Resources.Load` を新規実装。 |
| 4. 多態的音素エントリ (BlendShape / AnimationClip) | **完全新規** | `[SerializeReference]` ベースの polymorphic list は `FacialCharacterProfileSO._adapterBindings` で前例があるが、**1 つのバインディング内で 2 種類の派生型を Inspector で混在編集する Drawer 例は未実装**。AnimationClip → time-0 スナップショット抽出は §3 参照。 |
| 5. ULipSyncProvider と ILipSyncProvider 実装 | **完全新規** | コア側 `ILipSyncProvider` (`Domain/Interfaces/ILipSyncProvider.cs`) は契約のみ定義済み。`uLipSync.LipSyncInfo.phonemeRatios` (Dictionary<string,float>) を内部固定長バッファへ橋渡しする実装を新規作成する。`uLipSync.uLipSync.onLipSyncUpdate` イベント (LipSyncUpdateEvent : UnityEvent<LipSyncInfo>) を購読する。 |
| 6. ULipSyncAdapterBinding ライフサイクル | **部分流用** | `OscReceiverAdapterBinding`（OnStart 雛形）と `InputSystemAdapterBinding`（複数 IInputSource 登録 + AddComponent + Dispose 取り外し）の両方をテンプレート化して使用可能。`AdapterSlug.TryParse` / `[FacialAdapterBinding(displayName: ...)]` 属性も既存。 |
| 7. 入力デバイス自動判定 (Mic vs ASIO) | **完全新規** | `uLipSyncAsioInput.GetAsioDriverNames()` (instance method、§4 参照) と `UnityEngine.Microphone.devices` の組合せロジックは新規。disambiguatorIndex は新規概念で類似実装なし。 |
| 8. ランタイムデバイス hot-swap | **完全新規** | コア側に類例なし。新規 public API として `ULipSyncAdapterBinding.SwapDevice(string deviceName, int disambiguatorIndex)` 等を追加。 |
| 9. デバイス断 / 未接続時の挙動 | **部分流用** | `LipSyncInputSource` の SilenceThreshold (1e-4f) `false` 経路が「無音時 output 非変更 → Aggregator が Array.Clear で 0 化」を担保するため、provider に zero-output モードを実装するだけで成立する（§5 参照）。 |
| 10. マルチキャラクター・複数バインディング | **完全流用** | `IInputSourceRegistry` の `AdapterSlug` キー名前空間化、`FacialCharacterProfileSO._adapterBindings` の per-instance 保持は既に分離設計。binding 内で静的状態を持たない原則を守るだけで成立。 |
| 11. GC フリー / 性能契約 | **部分流用** | `LipSyncInputSource` 内部 `_scratch` 1 度だけ確保パターンを provider 側でも踏襲。`LayerInputSourceAggregator` の per-frame 0-alloc 設計に準拠。テスト基盤は §7 参照。**懸念**: `uLipSync.uLipSync` 本体は内部 `Dictionary<string, float> _ratios` を毎フレーム `Clear()` してから値を入れ直す（uLipSync.cs:237-247）。これは uLipSync 側の責務で本パッケージのホットパスとは独立だが、provider が `LipSyncInfo.phonemeRatios` を辞書 lookup する経路で string→float Dictionary 参照が発生するため、**インデックス化キャッシュ**を介する必要あり。 |
| 12. Editor PropertyDrawer | **部分流用** | `OscReceiverAdapterBindingDrawer` (UI Toolkit, `CreatePropertyGUI` ベース、`PropertyField` 並べ) を style ベースで踏襲可。ただし `[SerializeReference]` 多態リスト＋型セレクタ＋追加/削除/並替の Drawer は既存になく新規実装（§6 参照）。 |
| 13. Samples~ パッケージング | **完全流用** | 既存 `com.hidano.facialcontrol.inputsystem` の `MultiSourceBlendDemo` と `Assets/Samples/` 二重管理ルール（CLAUDE.md / structure.md 既定）をそのまま適用。 |
| 14. テスト網羅 (TDD) と ドキュメント / 規約 | **部分流用** | テスト命名規約 / EditMode・PlayMode 配置・Tests/Shared フォルダ構成は project-wide 既定。`Tests/PlayMode/Performance/GCAllocationTests.cs`、`SetWeightZeroAllocationTests.cs` 等の `GC.GetTotalMemory` 差分パターンが既存（§7 参照）。命名規約 / 4 スペース / 日本語コメント は既定通り。 |

---

## 2. AdapterBinding 統合点の確認

### `AdapterBuildContext` の場所と公開フィールド

- 場所: `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Adapters/AdapterBuildContext.cs`
- `readonly struct`、`in` 修飾で boxing-free に渡される。
- 公開フィールド:
  - `FacialProfile Profile`
  - `IReadOnlyList<string> BlendShapeNames`（Profile 同期固定長）
  - `IInputSourceRegistry InputSourceRegistry`（slug-keyed lookup）
  - `ITimeProvider TimeProvider`（unscaled time）
  - `GameObject HostGameObject`（**`AddComponent` 先**、Prefab-Clean Contract の dynamic attach に必須）
  - `ILipSyncProvider LipSyncProvider`（**任意（null 可）**）
- 既存 `LipSyncProvider` field の使い方注意: 本パッケージは binding 自身が provider を構築し `IInputSourceRegistry` に `LipSyncInputSource` を登録するため、`ctx.LipSyncProvider` 経由ではなく自前で provider を抱える経路を取る（spec の Adjacent expectations と整合）。`ctx.LipSyncProvider` を上書きする経路はコンストラクタで完結しているため不可—これは binding 自身が provider を保持して `LipSyncInputSource` を構築すれば問題ない。

### `OscReceiverAdapterBinding.OnStart` パターンの再現可能性

`OscReceiverAdapterBinding.OnStart` (lines 111-147) は以下手順で構成され、`ULipSyncAdapterBinding` でも同一形式で再現可能（コア改変なし）:
1. `_started` ガード → 二重 OnStart 防止
2. `ctx.HostGameObject == null` ガード → LogError + 早期 return
3. mappings 未設定ガード → LogWarning + 早期 return
4. `AdapterSlug.TryParse(Slug, out var slug)` → 失敗時 LogError
5. `ctx.HostGameObject.AddComponent<HelperHost>()` → Configure
6. `IInputSource` を構築 → `ctx.InputSourceRegistry.Register(slug, source)`
7. `_started = true`

`Dispose` (lines 166-183) も `Object.Destroy(_helperHost)` → null 化 → `_started = false` の標準形。`InputSystemAdapterBinding` は同じパターンを複数 helper / 複数 IInputSource 登録に拡張した先行例（lines 152, 297）。

### `[FacialAdapterBinding]` 属性と `AdapterSlug` 規約

- 属性: `Hidano.FacialControl.Domain.Adapters.FacialAdapterBindingAttribute`
  - 場所: `Runtime/Domain/Adapters/FacialAdapterBindingAttribute.cs`
  - `AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)`
  - コンストラクタ: `FacialAdapterBindingAttribute(string displayName)` — Inspector の Add ドロップダウン discovery に使用
  - 用法: `[FacialAdapterBinding(displayName: "uLipSync")]` を `sealed class ULipSyncAdapterBinding : AdapterBindingBase` に付与
- `AdapterSlug` 規約 (`Runtime/Domain/Models/AdapterSlug.cs`):
  - パターン: `^[a-zA-Z0-9_.-]{1,64}$`（ASCII 限定、kebab-case 想定）
  - `TryParse` / `Parse` / `FromDisplayName`（kebab 自動正規化）/ `TryParseComposite`（`<slug>:<sub>`）
  - **本パッケージで採用すべき slug 例**: `ulipsync` / displayName 由来 `u-lip-sync`（`FromDisplayName("uLipSync") = "u-lip-sync"`）。一意性は inline serialized binding 内の `Slug` field で保証。
- `AdapterBindingBase` の `public string Slug` field（NOT `[SerializeField]`、Domain 純度のため Unity 自動 serialize ルールに乗せる）を使用（base.Slug を継承）。

---

## 3. AnimationClip → time-0 BlendShape スナップショット抽出

### `AnimationClipCache` の API 方向

- ファイル: `Runtime/Adapters/Playable/AnimationClipCache.cs`
- API: `GetOrCreate(string expressionId, BlendShapeMapping[] blendShapeValues)` — **BlendShapeMapping[] から AnimationClip を生成する forward 方向**（line 131-147 の `CreateAnimationClip`）。
- 結論: **本パッケージで必要な reverse 方向（AnimationClip → BlendShape weight 配列）の機能は提供されていない**。spec の「`AnimationClipCache` の time-0 サンプリングパターンを再利用」という文言は誤解を招く—実際には **AnimationClip 内のキーフレームから値を読み出す別経路** が必要。

### 候補手段の比較

| 候補 | 概要 | Pros | Cons |
|---|---|---|---|
| **A. `Animator.SampleAnimation(target GameObject, clip, 0f)` + `SkinnedMeshRenderer.GetBlendShapeWeight(i)` 読戻し** | 対象 GameObject に SMR を持たせ、clip を時刻 0 で評価し SMR の現在 weight を読み戻す。 | Runtime 経路で動作（PlayMode 必須ではない）。Animator コンポーネント不要（メソッドは static 様）。 | SMR 実体が必要 = `ctx.HostGameObject` 配下の SMR を見つける必要。OnStart で副作用（実 SMR の weight が一時上書き）→ 元値の保存・復元が必要。各 clip ごとに 1 度だけなので構築時 1 回で OK。 |
| **B. `AnimationMode.SampleAnimationClip` (UnityEditor.AnimationMode)** | Editor 専用 API。Animation ウィンドウのプレビュー用。 | A と同じく Editor 上で精度よくサンプル可能。 | **Editor 専用** = Runtime asmdef からアクセス不可。Req 1.4「Editor 専用機能を Runtime asmdef に含めない」に違反するため不可。 |
| **C. 手動 curve scan: `AnimationUtility.GetCurveBindings(clip)` + `Evaluate(0f)`** | clip 内の各 EditorCurveBinding を列挙し `propertyName` が `blendShape.{name}` のものだけ抽出して `AnimationCurve.Evaluate(0f)` で値取得。 | **`UnityEditor` 依存**（`AnimationUtility`）— Runtime 不可。 | B と同じく Editor 専用のため Runtime asmdef 不可。 |
| **D. `clip.events` / `clip.SampleAnimation(GameObject, 0f)` (legacy)** | 旧 `AnimationClip.SampleAnimation` static method (`AnimationClip.SampleAnimation(GameObject go, float time)`) は Runtime API。 | Runtime API。SMR を持つ GameObject を渡せば即座に時刻 0 状態を反映可能。 | A と同じ「対象 GameObject の SMR を一時上書き」副作用あり。元値の保存・復元が必須。 |

### 推奨案

**A or D（実態は同等経路）+ 副作用ロールバック**:

1. `OnStart` で対象 SMR を `ctx.HostGameObject` 配下から `GetComponentInChildren<SkinnedMeshRenderer>()` で取得（Profile に紐づく SMR 解決ロジックは要確認・[要追加調査]）。
2. SMR 全 BlendShape の現在 weight を一時退避（`float[] _savedWeights = new float[smr.sharedMesh.blendShapeCount]`）。
3. AnimationClip 形式エントリごとに:
   - `AnimationClip.SampleAnimation(host, clip, 0f)`（または `Animator.SampleAnimation(host, clip, 0f)`）
   - `for (int i = 0; i < blendShapeCount; i++) snapshot[i] = smr.GetBlendShapeWeight(i) / 100f * maxWeight`
4. 全エントリ処理後に SMR weight を退避値で復元。

これは `OnStart` 1 度きりの初期化コストで GC アロケーション許容（Req 11.5 と整合）。

[要追加調査] 対象 SMR の取得方法は `FacialCharacter` / `FacialCharacterProfileSO` 側に既存の解決機構があるか design 段階で確認（恐らく `SkinnedMeshRenderer` ターゲットは既に Profile から解決されているはず）。

---

## 4. ASIO ドライバ列挙の到達経路

### `uLipSyncAsioInput.GetAsioDriverNames()` の呼び出し可否

- 場所: `Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/Windows/uLipSyncAsioInput.cs:47-57`
- シグネチャ: `public string[] GetAsioDriverNames()` — **インスタンスメソッド**（static ではない）。
- 内部実装: `return AsioOut.GetDriverNames();`（NAudio.Wave.Asio）を try/catch で安全化（例外時は空配列）。
- 問題点: **インスタンスメソッドなので呼び出すには `uLipSyncAsioInput` コンポーネントの実体が必要**。binding は OnStart で初めて AddComponent するため、デバイス判定は AddComponent **後**に行う必要がある（または `AsioOut.GetDriverNames()` を直接 NAudio 経由で呼ぶ）。
  - **推奨**: 本パッケージは NAudio.Wave への直接参照を持つ asmdef を許容するか（`com.hidano.ulipsync-asio` 既に NAudio を抱えるためその間接参照のみで可能か）、または「先に空 GameObject に Mic / Asio コンポーネントを暫定 AddComponent → 列挙 → 不要なら Destroy」のいずれかを取る。
  - **より単純な経路**: `NAudio.Wave.Asio.AsioOut.GetDriverNames()` を直接呼ぶ（uLipSyncAsioInput のインスタンス化を介さない）。これには本パッケージから NAudio へのアクセスが必要。`com.hidano.ulipsync-asio` の asmdef が NAudio を `precompiled references` として抱えていれば、本パッケージはそれに依存することで間接的に NAudio 型へアクセス可能。[要追加調査] `com.hidano.ulipsync-asio` の asmdef 内容を design 段階で確認。

### Windows 専用 `#if` ガード

- `uLipSyncAsioInput.cs` 全体が `#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN` でガードされ、それ以外のプラットフォームでは空 stub クラス（`isRecording=false`、`StartRecord/StopRecord` 空）が定義されている。
- `IAudioInputSource` インターフェース型は両ブランチで実装。
- 本パッケージも同じ `#if` を使うべき。Req 1.6「Windows-only」明記と整合。

### WebGL stub の存在確認

- `uLipSyncMicrophone.cs:9` に `#if !UNITY_WEBGL || UNITY_EDITOR` で本体定義、`#else` で空 stub（line 220-224）。
- `uLipSync.cs` は WebGL でも本体動作する（`UNITY_WEBGL && !UNITY_EDITOR` 専用パスを内蔵）。
- 本パッケージは Windows-only スコープ（Req 1.6）のため WebGL stub は不要だが、誤コンパイルを防ぐため Runtime コードの非 Windows 経路は no-op stub にしておく（OnStart で LogError + 早期 return）。

### Mic 側の `Microphone.devices` 列挙

- `Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/Core/MicUtil.cs:17-35` に `MicUtil.GetDeviceList()` が `Microphone.devices` を列挙して `MicDevice { name, index, minFreq, maxFreq }` のリストを返す（GC 許容、Editor 列挙用）。
- 本パッケージのデバイス記述子解決は OnStart で 1 度のみ行うので `MicUtil.GetDeviceList()` を直接呼んで GC 許容で問題なし。あるいは `UnityEngine.Microphone.devices` を直接呼んでも同等。
- `disambiguatorIndex` は `deviceName` が同名複数登場した場合の N 番目選択用 — `Microphone.devices.Where(n => n == deviceName)` の N 番目を取る。Linq 不可（Req 11.6）なので手動 for ループで実装。

---

## 5. Zero-frame settle (Req 8.2(a) / Req 9) の実現可能性

### 重要な再評価: 「直前値 layer 残存」は発生しない

`LayerInputSourceAggregator.AggregateInternal` (lines 287-390) の挙動を読んだ結果:

```csharp
for (int l = 0; l < layerCount; l++)
{
    var layerOutput = _perLayerOutput[l];
    Array.Clear(layerOutput, 0, layerOutput.Length);   // ★ 毎フレーム per-layer ゼロクリア
    // ...
    for (int s = 0; s < sourceCount; s++)
    {
        // ...
        scratchSpan.Clear();
        bool sourceIsValid = source.TryWriteValues(scratchSpan);
        if (sourceIsValid) {
            // ... layerOutput[k] += scratchSpan[k] * w;
        }
        // sourceIsValid==false の場合は加算しない（continue 相当）
    }
    // [0,1] クランプ → outputPerLayer[l] = new LayerInput(...)
}
```

`LipSyncInputSource.TryWriteValues` (line 74) の `if (sum < SilenceThreshold) return false;` は**厳密 less-than**で、scratch sum が完全に 0 でも 1e-4 未満なら false を返し、provider 出力は output に書き込まれない。Aggregator はそれを「寄与なし」として加算ループをスキップ → layerOutput はゼロのまま。

つまり、**provider が次フレームに「全 0」を出力すれば、`TryWriteValues` が false を返し、Aggregator が layerOutput を Array.Clear した直後に何も加算されず、当該レイヤーの最終出力は 0 になる**。LayerBlender の優先度ブレンド側は priority + weight に従って他レイヤーと混ぜる挙動だが、当該レイヤーの寄与は 0 として正しく扱われる。

### 各案の評価

| 案 | 内容 | コア不変契約への影響 | 備考 |
|---|---|---|---|
| **a. provider に「次フレーム強制 0 出力」フラグ** | `ULipSyncProvider` に `RequestZeroOutput()` API を持たせ、次回の `GetLipSyncValues(output)` で `output.Clear()` のみ実行（uLipSync イベントを無視）。 | **コア不変・最小副作用**。`LipSyncInputSource` は SilenceThreshold (1e-4f) `<` 厳密判定なので、output 全 0 → sum=0 < 1e-4 で false → 値書込なし → Aggregator 側で当該レイヤーゼロ化。**整合する**。 | 推奨案。1 フレーム経過後にフラグを自動解除（または明示解除 API）すれば hot-swap シーケンスに自然に組み込める。 |
| b. provider をバイパスして binding 側から InputSource を一時切替 | `IInputSourceRegistry` から登録解除 / 再登録。 | コア不変だが副作用多い（IInputSource の slug 衝突管理が必要）、不要に複雑。 | 不採用。 |
| c. 別の打ち消し input source を transient registration | 別 IInputSource を一時的に登録して 0 を出力。 | コア不変だがコード量・設計コスト過大。 | 不採用。 |

### 推奨案: **案 a**

`ULipSyncProvider` に `bool _zeroOutputRequested` を持たせ、`RequestZeroOutputForNextFrame()` を public API として公開。`GetLipSyncValues(output)` で:

```csharp
if (_zeroOutputRequested)
{
    output.Clear();          // 全 0 → sum=0 → LipSyncInputSource が false 返却
    _zeroOutputRequested = false;
    return;
}
// 通常経路
```

これにより:
- **Req 8.2(a)** (hot-swap 時の zero-frame settle): swap 開始時に `RequestZeroOutputForNextFrame()` → 次の Aggregate で当該レイヤー 0 化 → 旧コンポーネント Destroy → 新コンポーネント AddComponent。
- **Req 9.3** (デバイス断時の silence モード): 同 API を呼ぶ + デバイス再解決を行わず維持 → silence モード成立。

コア（`LipSyncInputSource`、`LayerInputSourceAggregator`）の改変は 0 行。

---

## 6. PropertyDrawer / Editor 統合

### 既存 OSC binding Drawer

- 場所: `FacialControl/Packages/com.hidano.facialcontrol.osc/Editor/AdapterBindings/OscReceiverAdapterBindingDrawer.cs`
- 形式: **UI Toolkit**（`CreatePropertyGUI` override、`PropertyField` を `VisualElement` root へ Add）。`OnGUI` は使わない（Req 12.2 と整合）。
- 構造: `[CustomPropertyDrawer(typeof(OscReceiverAdapterBinding))]` 属性、フィールド名 `Slug` / `_endpoint` / `_port` / `_stalenessSeconds` を `FindPropertyRelative` で取得し `PropertyField` でラップ。
- 同等パターンを `ULipSyncAdapterBindingDrawer` で踏襲可能（**フラットフィールド部分は完全に流用形**）。

その他参考:
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Editor/AdapterBindings/ArKitOscAdapterBindingDrawer.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.inputsystem/Editor/AdapterBindings/InputSystemAdapterBindingDrawer.cs`

### UI Toolkit / IMGUI

すべて UI Toolkit。Req 12.2、tech.md「Editor は UI Toolkit、IMGUI を新規 UI に使わない」と一致。

### `[SerializeReference]` polymorphic list の Drawer パターン

- 既存使用箇所: `FacialControl/Packages/com.hidano.facialcontrol/Editor/Inspector/AdapterBindings/AdapterBindingsListView.cs` が `FacialCharacterProfileSO._adapterBindings` の `[SerializeReference] List<AdapterBindingBase>` を扱う ListView を実装。Add ドロップダウンは `[FacialAdapterBinding]` 属性を持つ型を `TypeCache.GetTypesWithAttribute` で discover してメニュー生成する。
- **しかし、これは「`AdapterBindingBase` 派生の polymorphic list」専用の実装**。本パッケージで必要なのは「**1 つの binding 内部の `List<PhonemeEntryBase>` を Drawer 内に埋め込んだ多態リストとして編集**」という別レイヤーの問題。
- 既存のパッケージ内に「Drawer 内部に多態リスト + 型セレクタ + 追加/削除/並び替え」のテンプレートは見当たらず、**新規実装が必要**。
- 推奨実装: `ListView` (UI Toolkit) を `CreatePropertyGUI` の root に埋め、`makeItem` / `bindItem` で各エントリの `managedReferenceFullTypename` に応じて BlendShape 形式 / AnimationClip 形式の専用 row を切替。Add ボタンは Generic Menu を出して 2 種類の派生型のいずれかを `managedReferenceValue` に代入。これは新規開発。

[要追加調査] `AdapterBindingsListView.cs` の Add ドロップダウン実装の細部は同パッケージの design 段階で読み込み、reuse 可能なヘルパー関数があるかを確認。

---

## 7. Test 共有資産

### Tests/Shared/ 配下

- 既存ファイル: `FacialControl/Packages/com.hidano.facialcontrol/Tests/Shared/ManualTimeProvider.cs` のみ。
- BlendShape mock や GC-free assertion ヘルパは **未整備**。本パッケージでは独自 Fake を実装する必要あり（`Tests/Shared/FakeULipSyncSource.cs`、`Tests/Shared/FakeAsioDriverEnumerator.cs` 等）。

### PerformanceTesting 系の使用例

- `FacialControl/Packages/com.hidano.facialcontrol/Tests/PlayMode/Performance/GCAllocationTests.cs`
- `FacialControl/Packages/com.hidano.facialcontrol/Tests/PlayMode/Performance/SetWeightZeroAllocationTests.cs`
- `FacialControl/Packages/com.hidano.facialcontrol/Tests/PlayMode/Performance/AdapterBindingHostAllocationTests.cs`
- `FacialControl/Packages/com.hidano.facialcontrol/Tests/PlayMode/Performance/MultiCharacterPerformanceTests.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.inputsystem/Tests/PlayMode/Performance/AnalogProcessorAllocationTests.cs`

これらは `GC.GetTotalMemory(false)` の差分計測パターン（[要追加調査] 詳細パターン確認は design 段階で 1 ファイル読めば十分）。`Unity.PerformanceTesting` の `Measure.Method().MeasureGcAlloc()` は使用されていない可能性が高い（grep 0 hit）。本パッケージでも `GC.GetTotalMemory(false)` 差分パターンを踏襲する。

---

## 8. リスク・ブロッカー

### High リスク（要 design 段階で解決）

1. **AnimationClip → time-0 BlendShape 抽出の対象 SMR 解決経路** — `ctx.HostGameObject` 配下の SMR を一意に取得する既定経路があるか未確認。多メッシュ char では曖昧性あり。[要追加調査]
2. **NAudio への asmdef 参照経路** — `com.hidano.ulipsync-asio` の asmdef が NAudio を `precompiled references` として公開しているか、本パッケージから NAudio.Wave.Asio.AsioOut を直接参照可能かを確認。不可なら uLipSyncAsioInput 経由でしかドライバ列挙できず、AddComponent → 列挙 → 不要なら Destroy という回り道が必要。[要追加調査]

### Medium リスク

3. **`uLipSync.uLipSync.profile` の inject タイミング** — `uLipSync.uLipSync` は Awake / OnEnable で profile 依存のバッファ確保 (`AllocateBuffers`) を行う。binding が AddComponent → profile セット → Enable のシーケンスで適切に動作するか実機検証が必要（特に `AllocateBuffers` が `profile.mfccs.Count` に依存する点）。[要追加調査]
4. **PropertyDrawer の `[SerializeReference]` 多態リスト Drawer 新規実装** — UI Toolkit ListView ベースで型セレクタ + makeItem 切替を実装する必要があり、既存テンプレートが乏しい。1〜2 日のスパイクが必要。
5. **`uLipSync.uLipSync` の onLipSyncUpdate イベント (UnityEvent)** — UnityEvent.Invoke は内部で List<UnityAction> 列挙が走るため GC ヒット可能性あり。provider 側のホットパスは `phonemeRatios` 受信→自前バッファ書込みのみ（GC 0）に保つ必要あり。`LipSyncInfo.phonemeRatios` (Dictionary<string,float>) の foreach 列挙はキャッシュ経由（音素 string→snapshot index の事前構築マップ）で 0-alloc 化する必要あり。

### Low リスク

6. **`AdapterSlug.FromDisplayName("uLipSync")` の生成結果** — 期待値 `"u-lip-sync"`。ユーザーが手動オーバーライド可能なため致命的ではない。
7. **Sample 二重管理** — `Samples~/MicLipSyncDemo/` と `Assets/Samples/` の同期は CLAUDE.md に既定があり既存パッケージで運用済み。

---

## 実装アプローチ評価

### 推奨: **Option B (新規パッケージ追加)**

- 既存 `com.hidano.facialcontrol.osc` / `com.hidano.facialcontrol.inputsystem` と並列の姉妹パッケージとして配置。
- コア (`com.hidano.facialcontrol`) の改変は **0 行**。
- スコープ境界: `ULipSyncProvider` (新) + `ULipSyncAdapterBinding` (新) + 多態的 `PhonemeEntry` (新) + Editor PropertyDrawer (新) + Sample + ドキュメント。

### 工数・リスク評価

- **Effort: L (1〜2 週間)** — 新規パッケージ + 14 要件 + PropertyDrawer 多態リスト新規 + テスト網羅。
- **Risk: Medium** — uLipSync 内部構造への依存（Profile inject、UnityEvent ホットパス）と AnimationClip 抽出の対象 SMR 解決が不確定要素。

### Design 段階に持ち込む Research items

- 対象 SMR の解決経路（`FacialCharacter` / Profile 側の既存解決機構）。
- `com.hidano.ulipsync-asio` asmdef の NAudio 参照公開状況。
- `uLipSync.uLipSync.profile` の Awake/OnEnable シーケンスと AddComponent 順序の整合。
- `LipSyncUpdateEvent` (UnityEvent<LipSyncInfo>) の Invoke パスの GC 影響と、`phonemeRatios` Dictionary を 0-alloc で扱うキャッシュ戦略。
- `[SerializeReference]` 多態リスト UI Toolkit Drawer の既存パッケージ内テンプレート再利用可能性。
