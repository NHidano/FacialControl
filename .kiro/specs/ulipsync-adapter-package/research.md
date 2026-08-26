# Research & Design Decisions — `com.hidano.facialcontrol.lipsync`

言語: 日本語 / 日付: 2026-05-07 / 対象 spec: `.kiro/specs/ulipsync-adapter-package/`

## Summary

- **Feature**: `ulipsync-adapter-package`
- **Discovery Scope**: New Feature (新規 UPM 姉妹パッケージ追加)
- **Key Findings**:
  1. コア (`com.hidano.facialcontrol`) は `ILipSyncProvider` / `LipSyncInputSource` / `lipsync` レイヤー / `LayerUseCase.additionalInputSources` / `LayerInputSourceAggregator` をすべて公開済み。**コア改変 0 行**で本パッケージ実装可能。
  2. `uLipSync.Runtime.Windows` asmdef が `precompiledReferences` で `NAudio.Asio.dll` を公開しているため、本パッケージから `AsioOut.GetDriverNames()` を直接呼べる。
  3. `LayerInputSourceAggregator` は per-layer バッファを毎フレーム `Array.Clear` するため、provider が「次フレーム強制 0 出力」フラグを立てるだけで zero-frame settle が成立する（追加 mechanism 不要）。

## Research Log

### Topic A: NAudio (`AsioOut.GetDriverNames()`) への到達経路

- **Context**: 要件 7.2 は ASIO ドライバ名一覧を OnStart で列挙する。`uLipSyncAsioInput.GetAsioDriverNames()` (Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/Windows/uLipSyncAsioInput.cs:47-57) は **インスタンスメソッド** のため、列挙のために空 GameObject に AddComponent してから呼ぶ回り道が必要に見える。
- **Sources Consulted**:
  - `FacialControl/Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/Windows/uLipSync.Runtime.Windows.asmdef` (`precompiledReferences: [NAudio.Core.dll, NAudio.Asio.dll]`、`overrideReferences: true`、`includePlatforms: [Editor, WindowsStandalone64]`)
  - `FacialControl/Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/uLipSync.Runtime.asmdef` (`autoReferenced: true`)
- **Findings**:
  - `uLipSync.Runtime.Windows` asmdef は `autoReferenced: true` かつ `precompiledReferences` で NAudio.Asio.dll を公開している。
  - 本パッケージの Runtime asmdef は `references` に `uLipSync.Runtime.Windows` を追加するだけで `NAudio.Wave.Asio.AsioOut` に直接アクセスできる（precompiled DLL が transitive に解決される）。
  - 結果として `uLipSyncAsioInput` を一時 AddComponent する回り道は不要。
- **Implications**:
  - DD-A: `DefaultAsioDriverEnumerator` を `AsioOut.GetDriverNames()` の直接ラッパとして実装する。
  - 本パッケージ Runtime asmdef の `references` に `uLipSync.Runtime.Windows` を追加し、`includePlatforms: [Editor, WindowsStandalone64]` で Windows 限定にする（Req 1.6 と整合）。

### Topic B: 複数 SkinnedMeshRenderer 環境での対象 SMR 解決

- **Context**: AnimationClip 形式エントリの time-0 サンプリングは `AnimationClip.SampleAnimation(host, 0f)` または `Animator.SampleAnimation(host, clip, 0f)` を使うため、対象 SMR の取得が必要。多メッシュキャラクター（衣装メッシュ + 体メッシュ等）で曖昧性がある。
- **Sources Consulted**:
  - gap-analysis §3「対象 SMR の取得方法は `FacialCharacter` / `FacialCharacterProfileSO` 側に既存の解決機構があるか design 段階で確認」
  - 既存コア `FacialCharacterProfileSO` の AdapterBuildContext は `BlendShapeNames`（固定長配列）を持つが、SMR 参照を直接渡さない。
- **Findings**:
  - `AdapterBuildContext.BlendShapeNames` は SMR の BlendShape 名を保持するが、SMR 参照は提供されない（design 時点での確定情報、Req 4.4 の SMR 解決は本パッケージ側の責務）。
  - 既存パッケージで複数 SMR 環境を扱う前例なし（既存サンプル `MultiSourceBlendDemo` も単一 SMR 構成）。
  - depth-first で最初に出現する SMR を選ぶのが Unity 標準動作（`GetComponentInChildren<T>()`）と整合。
- **Implications**:
  - DD-B: デフォルトは `host.GetComponentInChildren<SkinnedMeshRenderer>()` の結果（depth-first 最初）。
  - `[SerializeField] string _targetMeshHint` を任意の override として用意（相対パス、空時 default）。多メッシュキャラクターのユーザーが明示的に SMR を指定できるようにする。
  - 解決失敗（hint があるが見つからない）時は LogWarning + first SMR フォールバック。

### Topic C: hot-swap シーケンスのタイミング（同期 vs 遅延）

- **Context**: Req 8.2(a) は swap 時に「provider に 1 フレーム分のゼロ値を明示通知し、レイヤー値を中立に収束させてから旧コンポーネントを Destroy」と規定。同期 swap だと 1 frame の古いデータが残るリスクあり。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/LayerInputSourceAggregator.cs:287-390` (per-layer `Array.Clear` 動作)
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/InputSources/LipSyncInputSource.cs:74` (`if (sum < SilenceThreshold) return false;`)
  - gap-analysis §5
- **Findings**:
  - 同期 swap（API 呼出と同時に Destroy + AddComponent）の場合、当該フレームで Aggregator が既に旧 source を呼んだ後ならば古い値が layer に残るリスクがある。
  - Deferred（API 呼出時はフラグだけ立て、次の `OnFixedTick` で実 swap）にすると、1 サイクル目で provider が 0 出力 → layer が `Array.Clear` 済みのままで終わり、2 サイクル目で実 swap → 3 サイクル目以降で新デバイスの値が反映されるシーケンスが安定。
  - `OnFixedTick` ではなく `OnTick` でも可だが、`OscReceiverAdapterBinding.OnFixedTick` (line 150) パターンとの整合のため `OnFixedTick` を採用。
- **Implications**:
  - DD-C: `SwapDevice` は **deferred**。`RequestZeroOutputForNextFrame` + `_swapPending = true` だけ実施し、次の `OnFixedTick` で実 swap を行う。
  - 配信用途のフレームレート（60-90 fps）で 1 サイクル分の遅延（〜16 ms）は感覚的に許容範囲。

### Topic D: AdapterSlug の既定値選定

- **Context**: `AdapterSlug` は `^[a-zA-Z0-9_.-]{1,64}$` 規約。displayName から自動正規化する `FromDisplayName("uLipSync") = "u-lip-sync"` か、手動指定の `"ulipsync"` か。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Models/AdapterSlug.cs` (gap-analysis §2)
  - 既存 OscReceiverAdapterBinding の slug 既定 = `"osc"`（lowercase, no separator）
- **Findings**:
  - 既存 `osc` は単純 lowercase。`u-lip-sync` の方が長く、ユーザーが手で書く頻度（ScriptableObject 内の slug 重複検知ログ等）を考えると短い方が扱いやすい。
  - displayName を `"uLipSync"` のまま保ち、Slug 既定値は `"ulipsync"` とする方針が他パッケージと整合する（OSC が `"OSC"` displayName / `"osc"` slug の前例）。
- **Implications**:
  - DD-D: 既定 Slug = `"ulipsync"`。ユーザーは Inspector で必要に応じて変更可能（10.2 の per-character 衝突回避用途）。

### Topic E: `uLipSync.uLipSync.profile` 注入タイミングと Awake/OnEnable シーケンス

- **Context**: gap-analysis Medium リスク #3。`uLipSync.uLipSync` は `Awake` / `OnEnable` で `AllocateBuffers` を呼び、`profile.mfccs.Count` に依存するバッファサイズを確保する。binding が AddComponent → profile 注入 → 動作 のシーケンスで `profile == null` の状態を経由するため、AllocateBuffers が誤ったサイズで確保される懸念。
- **Sources Consulted**:
  - `FacialControl/Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/uLipSync.cs:78` Awake
  - 同 :88 OnEnable, :118 AllocateBuffers, :131 `int phonemeCount = profile ? profile.mfccs.Count : 1;`
  - 同 :188-197 `Update()` 内の自己整合性チェック（`profile.mfccs.Count * mfccNum != _phonemes.Length` で再 `AllocateBuffers`）
- **Findings**:
  - AddComponent 直後の `Awake` / `OnEnable` 時点では `profile == null` のため `phonemeCount = 1` でバッファ確保される。
  - 次の `Update()` で profile が設定されていれば自己整合性チェックがヒットし `AllocateBuffers` が再実行され、正しいサイズに再確保される。
  - 結論として **AddComponent → set profile** の順で安全に動作する（最初の 1 フレームは phoneme 検出が機能しないが、配信開始の数 ms スパンで許容）。
- **Implications**:
  - DD-E: AddComponent 直後に `_analyzer.profile = analyzerProfile` を設定する。`gameObject.SetActive(false)` → AddComponent → set profile → `SetActive(true)` の代替案は user の character GameObject を一瞬非アクティブ化する副作用があるため不採用。

### Topic F: `Dictionary<string,float>` の 0-alloc 列挙

- **Context**: Req 11.1 は provider のホットパスで GC 0 byte を要求。`uLipSync.LipSyncInfo.phonemeRatios` は `Dictionary<string,float>`。
- **Sources Consulted**:
  - .NET 公式: `Dictionary<TKey,TValue>.Enumerator` は struct（alloc-free for direct foreach on concrete type）
  - gap-analysis Medium リスク #5
- **Findings**:
  - 直接 `foreach (var kv in dict)` は struct enumerator のため alloc-free。
  - ただし `IReadOnlyDictionary` や `IEnumerable<KeyValuePair>` 経由では boxing alloc が発生する。
  - `info.phonemeRatios` の公開型が将来 `IReadOnlyDictionary` に変わると壊れる。
  - 安全策: 構築時に `_phonemeKeys: string[]` をキャッシュし、`for` ループ内で `TryGetValue` する。これは公開型変更に強い。
- **Implications**:
  - DD-PhonemeLookup: `for (int i = 0; i < _phonemeKeys.Length; i++) { if (info.phonemeRatios.TryGetValue(_phonemeKeys[i], out var ratio)) { ... } }` のループ。`Dictionary.TryGetValue` は struct lookup でも alloc-free。

### Topic G: AnimationClip の time-0 サンプリング手段

- **Context**: Req 4.4 は AnimationClip 形式エントリの time-0 BlendShape weight 抽出を要求。gap-analysis §3 が候補手段を比較済み。
- **Sources Consulted**:
  - gap-analysis §3 候補比較表
  - Unity `AnimationClip.SampleAnimation(GameObject, float)` Runtime API（legacy だが Unity 6 で動作確認、autoReferenced）
- **Findings**:
  - `AnimationMode.SampleAnimationClip` と `AnimationUtility.GetCurveBindings` は **Editor 専用**（Req 1.4 違反）のため不採用。
  - `AnimationClip.SampleAnimation(host, 0f)` は Runtime API。SMR の現在 weight を一時上書きするため、構築時に `_savedWeights` で保存・復元する。
  - 1 binding あたり N 個の AnimationClip エントリでも N 回 sample すれば良い。OnStart 1 度きりの構築コスト（Req 11.5）。
- **Implications**:
  - `BuildSnapshots` で：`_savedWeights[]` 確保 → 全 SMR weight 保存 → 各 AnimationClip エントリで `clip.SampleAnimation(host, 0f)` → SMR から `GetBlendShapeWeight(i)` でスナップショット読出 → 全エントリ完了後 SMR weight を `_savedWeights[]` で復元。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| **A. 新規パッケージ追加（採用）** | `com.hidano.facialcontrol.lipsync` を `Packages/` に新設。コア改変なし | 既存 OSC / InputSystem の姉妹パッケージ構成と整合。リップシンク不要なプロジェクトには影響なし | パッケージ追加で UPM dependency 解決が増える | gap-analysis 推奨 (Option B) |
| B. コアに統合 | `Hidano.FacialControl.LipSync.*` 名前空間でコアに直接実装 | 1 パッケージで完結 | コアが uLipSync 依存を直接持つ。リップシンク不要なプロジェクトに余計な依存。コア改変必要 | 不採用（パッケージ独立性が崩れる） |
| C. ハイブリッド（コアに interface 追加 + 実装は別パッケージ） | 既に `ILipSyncProvider` がコアに存在するため不要 | — | — | 既に達成済み（コアが契約を保持、実装が別パッケージ） |

## Design Decisions

### Decision: 新規パッケージ `com.hidano.facialcontrol.lipsync` で姉妹パッケージとして配置

- **Context**: コアは既に `ILipSyncProvider` / `LipSyncInputSource` / `lipsync` レイヤー / `LayerUseCase.additionalInputSources` / `LayerInputSourceAggregator` を公開済み。本パッケージはそれを実装・利用する。
- **Alternatives Considered**:
  1. コアに統合 — 不採用（依存方向違反）
  2. 新規パッケージ追加 — **採用**
- **Selected Approach**: `Packages/com.hidano.facialcontrol.lipsync/` に独立 UPM パッケージを新設。Runtime asmdef は `Hidano.FacialControl.Domain` / `Hidano.FacialControl.Application` / `Hidano.FacialControl.Adapters` / `uLipSync.Runtime` / `uLipSync.Runtime.Windows` を `references` に持つ。
- **Rationale**: 既存 `com.hidano.facialcontrol.osc` / `com.hidano.facialcontrol.inputsystem` と同じパターン。コア改変 0 行。リップシンク不要プロジェクトでは導入不要。
- **Trade-offs**: パッケージ数が増えるが、責務分離と配布柔軟性のメリットが上回る。
- **Follow-up**: パッケージ間の version 整合性は CHANGELOG で告知（preview 期は破壊変更許容）。

### Decision: NAudio への直接アクセスを `uLipSync.Runtime.Windows` asmdef 経由で取得

- **Context**: `AsioOut.GetDriverNames()` を呼ぶ必要がある。
- **Alternatives Considered**:
  1. `uLipSync.Runtime.Windows` asmdef を `references` に追加し、`AsioOut` 型を直接使う — **採用**
  2. `uLipSyncAsioInput` を空 GameObject に一時 AddComponent して `GetAsioDriverNames()` を呼び、不要なら Destroy
- **Selected Approach**: 1。`uLipSync.Runtime.Windows.asmdef` の `precompiledReferences` (`NAudio.Asio.dll`) が transitive に解決され、`NAudio.Wave.Asio.AsioOut.GetDriverNames()` を直接呼べる。
- **Rationale**: GC アロケーション少なく、副作用なし。
- **Trade-offs**: 本パッケージが NAudio 型に直接依存する形になるため、将来 uLipSync が NAudio を置き換えた場合に追従が必要。`IAsioDriverEnumerator` 抽象でこの依存を 1 ファイルに局所化する。
- **Follow-up**: 実装時に `DefaultAsioDriverEnumerator.cs` の `#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN` ガードと non-Windows stub を必ず配置する。

### Decision: hot-swap は deferred (next OnFixedTick) で実行

- **Context**: Req 8.2(a) の zero-frame settle を確実にする。
- **Alternatives Considered**:
  1. 同期 swap（`SwapDevice` 呼出と同時に Destroy + AddComponent）
  2. Deferred swap（フラグを立て、次の `OnFixedTick` で実 swap） — **採用**
  3. coroutine ベース（`yield return null` で 1 フレーム待ち）
- **Selected Approach**: 2。`SwapDevice` で `RequestZeroOutputForNextFrame()` + `_swapPending = true` + 新 descriptor を `_pendingDescriptor` に保存。次の `OnFixedTick` で実 swap を行う。
- **Rationale**: 1 サイクル分の遅延（〜16 ms）は配信用途で許容。`LipSyncInputSource.TryWriteValues == false` + Aggregator の per-layer `Array.Clear` で zero-frame settle が自動成立する。Coroutine は `AdapterBindingBase` が MonoBehaviour ではないため不可。
- **Trade-offs**: 同期版より少し遅延するが、レイヤー値の確実なゼロ収束が得られる。
- **Follow-up**: PlayMode テスト `DeviceHotSwapTests` で「swap 開始フレームの layer output が 0、swap 完了後に新デバイス値が反映」を検証。

### Decision: 多態 phoneme entry を `[SerializeReference]` で表現

- **Context**: Req 4.1 が BlendShape 形式 / AnimationClip 形式の混在を要求。
- **Alternatives Considered**:
  1. enum + 共通 `PhonemeEntry` struct で型を分岐 — シリアライズしにくい AnimationClip 参照と混在しにくい
  2. `[SerializeReference] List<PhonemeEntryBase>` で polymorphic — **採用**
  3. 2 つの別 List（`List<BlendShapePhonemeEntry>` と `List<AnimationClipPhonemeEntry>`）— UI で混在編集が難しい
- **Selected Approach**: 2。コア側 `FacialCharacterProfileSO._adapterBindings` と同じ Unity 公式パターン。
- **Rationale**: Inspector で 1 つのリストとして混在編集可能。将来の派生型追加（OSC 形式 / Bone 形式等）も後方互換。
- **Trade-offs**: PropertyDrawer の実装コストが高い（既存コードに 1 binding 内部 polymorphic list の前例なし）。Risk-2 として明示。
- **Follow-up**: Drawer は `PhonemeEntryListView` で `ListView` + `bindItem` 内 `managedReferenceFullTypename` 判定で row 切替。

### Decision: zero-frame settle は provider 側フラグ + 既存 `LipSyncInputSource` + `Array.Clear` の合成で成立

- **Context**: Req 8.2(a), 9.3 の中立収束。
- **Alternatives Considered**:
  1. provider に「次フレーム強制 0 出力」フラグ — **採用**
  2. `IInputSourceRegistry` から登録解除 / 再登録
  3. 別 IInputSource を一時的に登録して 0 を出力
- **Selected Approach**: 1。`ULipSyncProvider.RequestZeroOutputForNextFrame()` を呼ぶと次の `GetLipSyncValues` が `output.Clear()` のみを実行 → `LipSyncInputSource.TryWriteValues` が `sum < SilenceThreshold` で `false` を返す → Aggregator が当該 source の値を加算しない → per-layer `Array.Clear` でゼロのまま当該レイヤーが終わる。
- **Rationale**: コア改変 0 行。`LipSyncInputSource` の SilenceThreshold (1e-4f) `<` 厳密判定（line 74）と Aggregator の per-layer `Array.Clear`（lines 287-390）が既に zero settle を保証している。
- **Trade-offs**: フラグの解除タイミングが「次回 `GetLipSyncValues` 呼出後」のため、複数フレームの 0 出力が必要なら都度 `RequestZeroOutputForNextFrame()` を呼ぶ。Hot-swap シーケンスでは deferred swap と組合わせて 1 回呼べば十分。
- **Follow-up**: EditMode テスト `ULipSyncProviderTests.GetLipSyncValues_AfterRequestZeroOutput_ProducesZeroSpan` で検証。

## Risks & Mitigations

- **Risk-1 (UnityEvent micro-alloc)**: `uLipSync.LipSyncUpdateEvent (UnityEvent<LipSyncInfo>)` の Invoke が内部で `List<UnityAction>` 列挙を行うため、購読者数次第で micro-alloc 発生の可能性。
  - **Mitigation**: 1 binding = 1 listener 構成のため実質影響なし。実機計測で 0 byte 確認。
- **Risk-2 (Drawer 工数)**: `[SerializeReference]` 多態リスト Drawer の新規実装は既存パッケージにテンプレートなし、1〜2 日のスパイク必要。
  - **Mitigation**: Editor タスクを独立 task 化し、Runtime + Tests の TDD サイクルを Drawer 完成前から進められるよう `Configure(...)` プログラム API を併設（既存 `OscReceiverAdapterBinding.Configure` パターンと同じ）。
- **Risk-3 (uLipSync API 変更)**: `phonemeRatios` 公開型が `IReadOnlyDictionary` 等に変わると enumerator が boxing 化する懸念。
  - **Mitigation**: `_phonemeKeys[]` + `TryGetValue` ループパターンで型変更耐性を確保。
- **Risk-4 (uLipSync.profile 注入)**: AddComponent 直後の Awake/OnEnable で profile が null のまま AllocateBuffers が走る。
  - **Mitigation**: `Update()` の自己整合性チェックが次フレームで救済（DD-E）。最初の 1 フレームの phoneme 検出無効は許容。
- **Risk-5 (複数 SMR 解決)**: 多メッシュキャラクターで対象 SMR が曖昧。
  - **Mitigation**: `_targetMeshHint` で override 可能。default は first SMR depth-first。

## References

### Project files (cited in design.md)

- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Interfaces/ILipSyncProvider.cs` — コア契約
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/InputSources/LipSyncInputSource.cs` — `SilenceThreshold = 1e-4f`、`TryWriteValues` 経路
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Adapters/AdapterBuildContext.cs` — `HostGameObject` / `InputSourceRegistry` / `LipSyncProvider` field
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Adapters/AdapterBindingBase.cs` — lifecycle hook 契約
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscReceiverAdapterBinding.cs` — `OnStart` (lines 111-147), `Dispose` (lines 166-183) パターン
- `FacialControl/Packages/com.hidano.facialcontrol.inputsystem/Runtime/Adapters/AdapterBindings/InputSystemAdapterBinding.cs` — 複数 helper / 複数 IInputSource 登録の先行例
- `FacialControl/Packages/com.hidano.facialcontrol/Editor/Inspector/AdapterBindings/AdapterBindingsListView.cs` — `[FacialAdapterBinding]` discovery + Add ドロップダウンパターン
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Hidano.FacialControl.Osc.asmdef` — 姉妹パッケージ asmdef 雛形
- `FacialControl/Packages/com.hidano.facialcontrol.osc/package.json` — `dependencies` 雛形

### uLipSync (`com.hidano.ulipsync-asio@e3514c204ca1`)

- `Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/uLipSync.Runtime.asmdef` — 自動参照の Runtime asmdef
- `Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/Windows/uLipSync.Runtime.Windows.asmdef` — `precompiledReferences: [NAudio.Core.dll, NAudio.Asio.dll]`
- `Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/uLipSync.cs` — `profile` (line 12), `Awake` (78), `OnEnable` (88), `AllocateBuffers` (118-149), `Update` self-check (188-197), `onLipSyncUpdate.Invoke` (269)
- `Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/Windows/uLipSyncAsioInput.cs:47-57` — `GetAsioDriverNames` (`AsioOut.GetDriverNames()` ラッパ)
- `Library/PackageCache/com.hidano.ulipsync-asio@e3514c204ca1/Runtime/Core/MicUtil.cs:17-35` — `GetDeviceList()` 例

### Spec & gap-analysis

- `.kiro/specs/ulipsync-adapter-package/requirements.md` — 14 要件、Boundary Context
- `.kiro/specs/ulipsync-adapter-package/gap-analysis.md` — §1 要件 × 既存資産マッピング、§2 AdapterBinding 統合点、§3 AnimationClip サンプリング候補、§4 ASIO 列挙経路、§5 zero-frame settle、§6 Drawer、§7 Test 共有資産、§8 リスク

### Steering

- `.kiro/steering/product.md` — Core capabilities、Release plan
- `.kiro/steering/tech.md` — Architectural contracts、Performance standards
- `.kiro/steering/structure.md` — Package layout、二重管理ルール、Naming conventions
