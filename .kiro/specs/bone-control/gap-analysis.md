# Gap Analysis: bone-control

## サマリ

- **本 spec の 95% は新規追加機能**: Runtime 層には `Transform` / `Animator` / `HumanBodyBones` / `Quaternion` / `localRotation` / "bone" 系参照が事実上ゼロ（`FacialController.cs` の `RequireComponent(typeof(Animator))` と `Animator` 保持参照のみ、bone 駆動には未使用）。greenfield within brownfield。
- **再利用すべきは「持続化フォーマット規約」「Domain 層 readonly struct パターン」「Inspector 編集 UI フレームワーク」「LateUpdate per-frame ホットパス規約」の 4 つ**。これらを真似るが、BlendShape Aggregator パイプライン (`LayerInputSourceAggregator` / `FacialControlMixer`) には **そもそも組込まない** のが正解。出力チャンネルが `float[BlendShapeCount]` ではなく `Quaternion[boneCount]` で根本的に異質なため、別パイプラインを並走させる方が clean。
- **最大の技術リスクは Animator update order**: Animator は `Update` 直後にボーン Transform を上書きするため、bone writer は **必ず `LateUpdate` でかつ `FacialController.LateUpdate` より後**（または同関数内末尾）で動かす必要がある。BlendShape は `SkinnedMeshRenderer` の独立フィールドなので Animator と競合しないが、bone は競合する。これは Req 4・5 を成立させる前提条件。
- **JSON 永続化の伏兵**: 実装は **Unity `JsonUtility` ベース** で、自由形式 object を扱えず `optionsJson` への文字列退避で pre/post-process している。BonePose の DTO 設計でも同じ制約に沿う必要がある（BonePose は固定スキーマなので素直に書けるが、optional 化の工夫が必要）。
- **後方互換は Req 1.5 / 7.3 / 10.2 で既に明示的に保証されている**ため、追加スキーマフィールドを必須化しないだけで足りる。実装側は `inputSources` で行ったような必須化バリデーションを **絶対に bonePoses に対して掛けない** こと。

---

## 1. Requirement → Asset Map

| Req | 要件 | 既存資産で再利用 | 新規作成 | ギャップ種別 |
|-----|------|------------------|----------|--------------|
| 1 | BonePose ドメインモデル（Expression から分離） | `Domain/Models/Expression.cs` の readonly struct + `ReadOnlyMemory<T>` + 防御的コピーパターン | `Domain/Models/BonePose.cs`、`Domain/Models/BonePoseEntry.cs`（boneName + Vector3-equivalent Euler）、`BonePoseId`（identity 参照、value object） | Missing |
| 2 | 名前ベースのボーン参照 | `BlendShapeMapping.Name` の string 名一次手段の前例 | `Adapters/Bone/BoneTransformResolver.cs`（Transform ツリーから名前で再帰探索 + キャッシュ） | Missing |
| 3 | Humanoid 自動アサイン補助 | なし（Animator 参照は `FacialController` の `GetComponent` のみ、HumanBodyBones は完全未使用） | `Adapters/Bone/HumanoidBoneAutoAssigner.cs`（`Animator.GetBoneTransform(HumanBodyBones)` ラッパ） | Missing |
| 4 | 顔相対 Euler 軸の解釈 | TransitionCalculator / ExclusionResolver の数学ヘルパースタイル | `Domain/Services/BonePoseComposer.cs`（`basis.localRotation * Quaternion.Euler(xyz)` 相当を Domain 値で実装） | **Constraint**: Domain 層は UnityEngine 非参照のため、`Quaternion.Euler` ではなく Domain 内で同等関数を実装 |
| 5 | Adapters/Bone 書戻しアダプタ | `FacialController.LateUpdate` の per-frame タイミング規約、`IFacialControllerExtension` の拡張点 | `Adapters/Bone/BoneWriter.cs`、`Adapters/Bone/IBonePoseSource.cs` | Missing, **Risk**: Animator 上書き対策の更新タイミング |
| 6 | 毎フレームゼロヒープ | `LayerInputSourceAggregator` の事前確保 scratch / span / double-buffer パターン、`LayerInputSourceWeightBuffer` のダブルバッファ | `Adapters/Bone/BonePoseSnapshot.cs`、PlayMode `GCAllocationTests` 拡張 | 既存パターンの直適用で済む |
| 7 | JSON 永続化スキーマ拡張 | `SystemTextJsonParser.cs`、`JsonSchemaDefinition.cs`、`InputSourceDto.cs`、`InputSourceDeclaration.cs`（round-trip 担体パターン） | `BonePoseDto.cs`、`BonePoseEntryDto.cs`、`SystemTextJsonParser` への bonePoses ブロック処理追加、`JsonSchemaDefinition.Profile.BonePoses` 定数 | **Constraint**: 実装は `JsonUtility` 由来。BonePose は固定スキーマなので素直に書けるが、optional にする工夫が必要 |
| 8 | ScriptableObject 変換 | `FacialProfileSO.cs`、`FacialProfileMapper.cs` | 案 A: `FacialProfileSO` に BonePose リストを sibling フィールド追加 / 案 B: `BonePoseSO.cs` を独立 SO として作る | **Decision needed**: SO 統合 vs 独立 |
| 9 | Editor BonePose 編集 UI | `FacialProfileSOEditor.cs`、`FacialProfileSO_InputSourcesView.cs`（複合 View をサブコンポーネント化する設計）、UI Toolkit `Foldout`/`ListView` | `Editor/Inspector/FacialProfileSO_BonePoseView.cs` | 既存 Foldout/ListView パターンをそのまま展開可能 |
| 10 | 既存パイプラインとの後方互換 | `SystemTextJsonParser` のスキーマバージョンチェック、Expression / FacialProfile が optional ReadOnlyMemory フィールドを許す方針 | preview 段階では `1.0` のまま additive にして良い（D-5: inputSources のような必須化を bonePoses にはしない） | Constraint |
| 11 | analog-input-binding 向け API | `FacialController.SetInputSourceWeight` / `GetInputSourceWeightsSnapshot` / `TryGetExpressionTriggerSourceById` のルックアップ規約、`IFacialControllerExtension` の subpackage 統合点 | `BoneWriter.SetActiveBonePose(in BonePose)` / `GetActiveBonePose()` / `OverrideBoneEntry(string boneName, eulerXYZ)` | 既存に倣える |

---

## 2. 既存パイプラインへの統合点 (per-frame flow)

現状の `FacialController.LateUpdate`:

```
LateUpdate {
  1. _layerUseCase.UpdateWeights(deltaTime)            // Aggregator → BlendedOutputSpan
  2. for each blend shape index:
       Renderer.SetBlendShapeWeight(idx, output[i] * 100) // SkinnedMeshRenderer 書込
}
```

bone-control 組込み後:

```
LateUpdate {
  1. (既存) BlendShape Aggregator + Renderer 書込
  2. (新規) bonePoseSource.GetCurrent(out BonePose currentPose)
  3. (新規) basisBone.localRotation を採取（顔相対基準）
  4. (新規) for each entry in currentPose:
       targetBone.localRotation = basisRotation * Domain.RotationFromEuler(entry.relativeEulerXYZ)
}
```

**重要決定**: `FacialController.LateUpdate` 末尾に直接挿入するか、別 MonoBehaviour (`BoneWriter`) として `[DefaultExecutionOrder]` で順序保証するかは設計フェーズで決める。前者は依存関係シンプルだが SRP 劣化、後者は分離度高いが execution order に依存して脆い。

---

## 3. クリティカルリスク: Animator update order

### 問題

- Unity の `Animator` はデフォルト execution order で `Update`（または `FixedUpdate`、Animator の Update Mode 設定による）の直後にボーン Transform を上書きする。`LateUpdate` より前に確定する。
- 現状の BlendShape 書込みは `SkinnedMeshRenderer.SetBlendShapeWeight` が独立フィールドなので Animator と競合せず `LateUpdate` で問題ない。
- bone Transform への直接書込みは Animator が Humanoid muscle / Generic Animation を流していると **その出力が一旦適用された後の値** に対しての追加適用となる。この順序こそ Req 4.5（"upstream body tilt leak しない"）と Req 4.2（"basis bone の現在 localRotation を起点"）の前提を成立させる。

### 帰結

- **bone writer は `LateUpdate`（または LateLateUpdate 相当の遅延ステージ）でなければならない**。Req 5.2 / 5.3 は Mixer 同様のステージ実行を要求しているが、これは "BlendShape と同じフレーム" であって "BlendShape Mixer の中に組み込む" ではない。設計フェーズで明確に分離させるべき。
- **execution order risk**: 同じ GameObject 上に他の MonoBehaviour が `LateUpdate` で同じボーンを書く場合（例: `LookAt` constraint, MagicaCloth2 揺れもの）、競合する。回避策として `[DefaultExecutionOrder(後寄り)]` を BoneWriter に付ける、あるいは `Animator.OnAnimatorIK` / `IAnimationJob` を採用する選択肢も研究対象。**Research Needed**。
- **Avatar Mask / PlayableGraph の影響**: 現状 `AnimationPlayableOutput` を Animator に接続しているが、bone への影響有無を確認する必要がある。**Research Needed**。

---

## 4. 後方互換性検証パス

Req 1.5、7.3、10.2 は「BonePose 未参照の既存 FacialProfile / 既存 JSON は変更不要に動く」を要求。

検証経路:

1. **Domain**: `FacialProfile` の追加フィールドを optional `ReadOnlyMemory<BonePoseRef>` にし、既存コンストラクタ / 呼び出しが warning なく通ることを既存 `FacialProfileTests.cs` で担保。
2. **Parser**: `SystemTextJsonParser.ParseProfile` で bonePoses ブロックが存在しない既存 JSON が schemaVersion チェックを通り、`bonePoses` フィールドが空配列扱いになることを `EditMode/Adapters/Json/SystemTextJsonParserBonePoseBackwardCompatTests.cs`（新規）で検証。**重要**: `inputSources` の D-5 必須化を絶対に bonePoses に対して行わない。
3. **Runtime**: `FacialControllerLifecycleTests` 既存ケースが新パイプライン追加でも GC alloc 増えないことを `GCAllocationTests` 拡張で検証。
4. **Mixer**: 既存 `FacialControlMixerTests` の `ComputeOutput` 結果が bone writer 有無で変わらないこと（独立パイプラインなので影響しないはず、Req 10.3 の保証）。
5. **JSON サンプル**: `JsonSchemaDefinition.SampleProfileJson` をそのまま読ませて従来通りの BlendShape プロファイルが構築されることを既存 `SampleJsonParseTests` の現行アサーションのまま通す。

---

## 5. 実装アプローチオプション

### Option A: FacialController に直結（最小新規）

- **What**: `FacialController.LateUpdate` 末尾に BonePose 適用ロジックを直接追加。`FacialProfileSO` に BonePose 配列フィールドを追加。
- **Trade-offs**:
  - ✅ 単一の MonoBehaviour で per-frame 順序が自明
  - ✅ 既存テストインフラへの追加が最小
  - ❌ FacialController の責務が "BlendShape ミキサ" から "BlendShape + Bone コントローラ" へ拡張し SRP が劣化
  - ❌ analog-input-binding が将来サブパッケージ化された場合の拡張点が `IFacialControllerExtension` と非対称になる

### Option B: BoneWriter を独立 MonoBehaviour として分離（最大分離）

- **What**: `Adapters/Bone/BoneWriter.cs` を `[RequireComponent(typeof(FacialController))]` の sibling MonoBehaviour として作成。`[DefaultExecutionOrder]` で FacialController の後に走らせる。`BonePoseSO` を独立 SO に。
- **Trade-offs**:
  - ✅ クリーンアーキテクチャ整合（SRP / 出力チャンネル分離）
  - ✅ analog-input-binding が `IBonePoseSource` インターフェース経由で接続でき、サブパッケージ化しやすい
  - ❌ ユーザー設置時の component 二重設置（または FacialController 側で auto-add）が必要
  - ❌ execution order に依存するため Unity の実装詳細にロックインされる

### Option C: Hybrid（推奨）

- **What**:
  - **Domain & Adapters/Bone は Option B 同様に独立**（`BonePose` Domain 値、`BoneWriter` クラス、`IBonePoseSource` インターフェース）。
  - **Runtime での起動は FacialController に内包**: FacialController が内部で `BoneWriter` を保持し、`LateUpdate` 末尾で `_boneWriter.Apply()` を呼ぶ。execution order 問題を Unity の枠組みに頼らず内部呼出順で確定。
  - **SO は `FacialProfileSO` に BonePose フィールドを追加**（既存ユーザの既存 SO アセットは bonePoses が空配列で読み込まれ無害）。Req 8 の独立 SO 化は preview.2 以降で別 spec へ送る。
  - **拡張点は `IFacialControllerExtension` ではなく新設 `IBonePoseProvider`**: analog-input-binding spec はこれを実装する MonoBehaviour として接続される。
- **Trade-offs**:
  - ✅ クリーン分離（Domain / Adapters / API 境界）と単純な per-frame 順序を両立
  - ✅ 既存ユーザに SO を作り直させない（追加フィールドは optional）
  - ✅ Animator 上書き問題を `LateUpdate` 内シーケンス制御で確定的に回避
  - ❌ FacialController が BoneWriter のライフタイム管理も担うことで内部複雑度が増す
  - ❌ "BonePose を独立 SO 化したい" ユースケース（複数 FacialProfile で同じ BonePose を共有等）は別 spec で対応

---

## 6. 具体的なファイル一覧（推奨 Option C）

### 新規作成

```
Runtime/Domain/Models/
  BonePose.cs                      # readonly struct, ReadOnlyMemory<BonePoseEntry>
  BonePoseEntry.cs                 # readonly struct (boneName, eulerX, Y, Z)
  BonePoseId.cs                    # （Req 1.4 の identity 参照のため任意、value object）

Runtime/Domain/Services/
  BonePoseComposer.cs              # basisRotation * EulerToQuat(...) の数学合成（Domain 内自前実装）

Runtime/Adapters/Bone/
  Hidano.FacialControl.Adapters.Bone.asmdef  # 新 asmdef または既存 Adapters に統合
  IBonePoseSource.cs               # 現在のアクティブ BonePose を提供するインターフェース
  IBonePoseProvider.cs             # （Req 11）外部から override 値を流し込むインターフェース
  BoneTransformResolver.cs         # name → Transform 解決 + キャッシュ
  HumanoidBoneAutoAssigner.cs      # HumanBodyBones.LeftEye 等の解決（Adapters 層）
  BoneWriter.cs                    # 毎フレーム BonePose を Transform.localRotation に書き戻す
  BonePoseSnapshot.cs              # zero-alloc バッファ

Runtime/Adapters/Json/Dto/
  BonePoseDto.cs                   # JsonUtility 互換 DTO（List<BonePoseEntryDto>）
  BonePoseEntryDto.cs              # boneName + Vector3 風 (x,y,z) float

Editor/Inspector/
  FacialProfileSO_BonePoseView.cs  # InputSourcesView と並ぶサブビュー

Tests/EditMode/Domain/
  BonePoseTests.cs
  BonePoseComposerTests.cs
Tests/EditMode/Adapters/Json/
  SystemTextJsonParserBonePoseTests.cs
  SystemTextJsonParserBonePoseBackwardCompatTests.cs
Tests/PlayMode/Adapters/Bone/
  BoneWriterTests.cs               # 実 Transform 書込 + Animator 順序検証
Tests/PlayMode/Performance/
  BoneWriterGCAllocationTests.cs
```

### 拡張（既存ファイル変更）

```
Runtime/Domain/Models/FacialProfile.cs
  → ReadOnlyMemory<BonePose> BonePoses { get; } を追加（optional, default 空）

Runtime/Adapters/Json/SystemTextJsonParser.cs
  → ProfileDto に bonePoses フィールド追加、ParseProfile / SerializeProfile で双方向

Runtime/Adapters/Json/JsonSchemaDefinition.cs
  → Profile クラスに BonePoses 定数 + サブクラス（BoneName, RelativeEulerXYZ）

Runtime/Adapters/ScriptableObject/FacialProfileSO.cs
  → BonePoseEntry[] フィールドまたは BonePose 用 SerializableData 追加

Runtime/Adapters/ScriptableObject/FacialProfileMapper.cs
  → SO ⟷ Domain 変換に BonePose を含める

Runtime/Adapters/Playable/FacialController.cs
  → BoneWriter インスタンスを内部保持、LateUpdate 末尾で Apply、API メソッド公開
  → ResolveBoneTransforms ステップを Initialize に追加

Editor/Inspector/FacialProfileSOEditor.cs
  → BonePoseView の組込み（InputSourcesView と同型）
```

### 完全に触らない（重要）

```
Runtime/Domain/Services/LayerInputSourceAggregator.cs       # BlendShape 専用
Runtime/Domain/Services/LayerBlender.cs                     # BlendShape 専用
Runtime/Domain/Services/ExpressionTriggerInputSourceBase.cs # BlendShape 専用
Runtime/Domain/Services/ValueProviderInputSourceBase.cs     # BlendShape 専用
Runtime/Domain/Models/Expression.cs                         # Req 1.4 で embed しないため変更不要
Runtime/Adapters/Playable/FacialControlMixer.cs             # BlendShape Mixer は無関係
```

---

## 7. Effort & Risk

- **Effort: L (1–2 weeks)** — Domain `BonePose` モデル、Adapters の `BoneWriter`、JSON DTO、SO 拡張、Editor UI、各層テスト（zero-alloc 含む）と多層に渡る。各層は既存パターンの素直な転写であり未知技術ゼロ。
- **Risk: Medium**
  - **High**: Animator update order と PlayableGraph 出力の bone への影響、execution order の sibling component 競合
  - **Medium**: JsonUtility ベース parser での optional ブロック実装
  - **Low**: Domain 層 Quaternion 数学の自前実装、UI Toolkit Inspector の追加（前例あり）

---

## 8. Research Needed (設計フェーズへ持ち越し)

1. **Animator + LateUpdate 順序の確定方法**: `[DefaultExecutionOrder]` での順序固定 vs `Animator.OnAnimatorIK` 利用 vs `IAnimationJob` の利用比較。preview.1 では C# レベルで十分か Burst 化を見越すか。
2. **PlayableGraph 出力の bone 副作用**: 現状 BlendShape のみを Animator に流しているが、Avatar が Humanoid のとき AnimationPlayableOutput が bone レイアウトに与える影響をプロトタイプで確認。
3. **BonePose Identity 参照の実装**: Req 1.4「Expression が BonePose を reference する」のスキーマ表現。`expressions[].bonePoseRef: "id"` か、別マッピング table か。analog-input-binding が「Expression と無関係に BonePose を直接駆動する」要件と整合する形を選ぶ。
4. **Domain 内 Quaternion 数学**: Unity の `Quaternion.Euler(x,y,z)` は Z-X-Y 順（Tait-Bryan）。Domain 層で同じ順序を再現する必要があるか、独自順序を採るか。Unity 互換の方が運用上安全と推測されるが、Req 4 の「mathematically equivalent composition」をどこまで許容するか確認。
5. **BonePose と FacialProfileSO の関係性**: Req 8 は ScriptableObject 化を求めるが、独立 `BonePoseSO` か `FacialProfileSO` 内包かは設計判断。Option C は内包推奨だが、複数プロファイル間共有のユースケースが preview.2 以降で見込まれる場合は最初から独立 SO にしておく方がよい。
6. **Runtime API の thread-safety**: Req 11.5 で hot path で alloc しない、analog-input-binding が任意スレッドから書込む可能性。`LayerInputSourceWeightBuffer` のダブルバッファ + `SwapIfDirty` パターンを bone にも適用するか、メインスレッド限定とするか。
7. **non-Humanoid + 自動アサインなしの典型ワークフロー**: Req 2 / 3 を満たすが、ユーザが string 名を手入力するしかない場合の UI ガイダンス（既存 `BlendShapeNameProvider` 相当のヘルパが必要か）。

---

## 9. 推奨

- **アプローチ**: **Option C (Hybrid)**
- **主要設計判断**:
  - BonePose は Expression に embed しない（Req 1.4 厳守）。`FacialProfile.BonePoses` ReadOnlyMemory として追加。
  - 適用パイプラインは FacialControlMixer / LayerInputSourceAggregator とは独立。`BoneWriter` を `FacialController.LateUpdate` 末尾から呼ぶ。
  - Domain 層は UnityEngine 非依存。`BonePoseComposer` で Euler→Quaternion 合成を自前実装。
  - JSON は JsonUtility ベースで素直な固定スキーマ。`bonePoses` ブロックは optional（**inputSources の D-5 必須化は適用しない**）。
  - Editor UI は `FacialProfileSO_BonePoseView` を新設し、既存 `FacialProfileSOEditor` に組み込む。
- **次ステップ**: ユーザー確認後、`/kiro:spec-design bone-control` を実行。

---

## ステアリングディレクトリの状態

`.kiro/steering/` 配下に `product.md` / `tech.md` / `structure.md` を含むファイルが**一つも存在しない**（templates のみ）。グローバル steering 文書は未整備のため、本分析は `CLAUDE.md`（プロジェクトルート）と既存隣接 spec（`input-binding-persistence`、`layer-input-source-blending` 等）から推定したコンテキストで実施。後続 design phase 前に `/kiro:steering` で最低限の steering を生成しておくと、analog-input-binding spec との整合検証が容易になる。
