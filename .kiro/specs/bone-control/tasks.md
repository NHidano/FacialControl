# Implementation Plan

## スコープ外メモ（実装中に再オープンしない）

- **MINOR-2 (Bone 名衝突警告) は preview.2 以降に延期**: preview.1 は Humanoid-first（VTuber/VRM 中心）。非 Humanoid の手入力で同名ボーンが複数ヒットする場合は「最初の発見を採用、警告なし」で確定する。実装中に「同名衝突を警告すべきでは？」と揺り戻しが発生したらこのメモを参照すること。
- **`bonePoses` ブロックに対する必須化バリデーションは絶対に追加しない**: `inputSources` の D-5 必須化ポリシーは BonePose には踏襲しない。Req 1.5 / 7.3 / 10.2 の後方互換を破壊するため。
- **Expression への BonePose 埋込み禁止 (Req 1.4)**: preview.1 では `Expression.bonePoseRef` を導入しない。BonePose は `FacialProfile.BonePoses` 単独配列のみ。
- **新規 asmdef は作らない**: `Hidano.FacialControl.Adapters.asmdef` を再利用し、配下に `Adapters/Bone/` ディレクトリを追加する形で統合する。

## 設計レビュー繰越事項（タスクに反映済み）

1. **MAJOR-1 RestoreInitialRotations は遅延スナップショット方式を採用**: BoneWriter は `Apply` 内で各エントリの対象 Transform に「初回書込み直前」に `_initialSnapshot[boneName] = transform.localRotation` を記録し、`Dispose` / `OnDisable` でスナップショットを巡回して復元する。`Initialize` を空 BonePose で呼んでも（preview.1 の典型ケース、analog-input-binding が後から `SetActiveBonePose` で埋める）正しく復元できる。Task 6.4 / 9.3 で実装と検証を行う。
2. **MINOR-1 Apply シグネチャから basisBoneName を除去**: `BoneWriter.Initialize(in BonePose initialPose, string basisBoneName)` 段階で basis Transform を解決してキャッシュし、`Apply()` は引数なしで毎フレーム `_basisBone.localRotation` を 1 回だけ参照する。毎フレームの辞書ルックアップ削減と凝集向上のため。Task 6.2 で API 形状を確定する。
3. **MINOR-2 → preview.2 延期**: 上記スコープ外メモのとおり。

---

- [x] 1. Foundation: Domain 値型と契約の確立
- [x] 1.1 BonePoseEntry の失敗テストを書く（Red）
  - 単一ボーンの (boneName, EulerX, EulerY, EulerZ) を保持する readonly struct の振る舞いを検証
  - boneName が null / 空白文字列 / 全空白の場合に `ArgumentException` が投げられることをアサート
  - Euler degrees の round-trip（コンストラクタに渡した値が getter から同値で返る）をアサート
  - Equals / GetHashCode の同名・同値による等価性をアサート
  - 観測可能な完了条件: `Tests/EditMode/Domain/BonePoseEntryTests.cs` がコンパイルされ、未実装の本体に対して全 Assert が失敗（Red）して保留される
  - _Requirements: 1.2, 1.6, 4.1_
  - _Boundary: Domain.Models.BonePoseEntry_

- [x] 1.2 BonePoseEntry を実装（Green）
  - readonly struct として `BoneName` / `EulerX` / `EulerY` / `EulerZ` を公開
  - コンストラクタで boneName の null/whitespace バリデーション → `ArgumentException`
  - Domain 層配置のため `UnityEngine.*` を一切参照しない
  - 観測可能な完了条件: 1.1 のテストが全件 Green になる
  - _Requirements: 1.2, 1.3, 1.6, 4.1_
  - _Boundary: Domain.Models.BonePoseEntry_

- [x] 1.3 BonePose の失敗テストを書く（Red）
  - 0 個以上のエントリを `ReadOnlyMemory<BonePoseEntry>` として保持することをアサート
  - 同一 boneName の重複エントリで構築すると `ArgumentException` が投げられることをアサート（Req 1.7）
  - 防御的コピー（外部配列を後から書き換えても BonePose 内部値が変わらない）をアサート
  - 空エントリ（0 件）でも構築可能であることをアサート
  - `Id` フィールドが string で round-trip 可能であることをアサート（preview.1 では参照キー未使用、空文字許容）
  - 観測可能な完了条件: `Tests/EditMode/Domain/BonePoseTests.cs` が Red 状態で保留される
  - _Requirements: 1.1, 1.2, 1.7_
  - _Boundary: Domain.Models.BonePose_

- [x] 1.4 BonePose を実装（Green）
  - readonly struct + `ReadOnlyMemory<BonePoseEntry> Entries`、`string Id`
  - コンストラクタで防御的コピーと boneName 重複チェック
  - Domain 層配置（`UnityEngine.*` 不参照）
  - 観測可能な完了条件: 1.3 のテストが全件 Green、Domain asmdef が Engine 参照ゼロでビルド成功
  - _Requirements: 1.1, 1.2, 1.3, 1.7_
  - _Boundary: Domain.Models.BonePose_

- [x] 2. Domain Service: 顔相対 Euler 合成の数学
- [x] 2.1 BonePoseComposer の Unity 突合テストを書く（Red、PlayMode）
  - PlayMode 配置の理由: Unity の `Quaternion.Euler(x, y, z)` と数値突合するため Engine 参照が必要
  - X/Y/Z 各軸 -180°〜180° の grid（10° 刻み）で `EulerToQuaternion(x, y, z)` の出力 (qx, qy, qz, qw) が `UnityEngine.Quaternion.Euler(x, y, z)` と誤差 ≤ 1e-5 で一致することをアサート
  - `Compose(basisQ, eulerXYZ)` が `basisQ * Quaternion.Euler(eulerXYZ)` と数値等価であることをアサート（Hamilton 積）
  - basis を非単位 quaternion（例: 体が傾いた状態）にしても結果が正しく合成されることをアサート（Req 4.5: body tilt が gaze に漏れない）
  - 観測可能な完了条件: `Tests/PlayMode/Domain/BonePoseComposerTests.cs` が Red 状態で保留される
  - _Requirements: 4.2, 4.3, 4.4, 4.5_
  - _Boundary: Domain.Services.BonePoseComposer_

- [x] 2.2 BonePoseComposer を実装（Green）
  - `EulerToQuaternion(x, y, z, out qx, out qy, out qz, out qw)`: Unity 互換の **Z-X-Y Tait-Bryan 順** で half-angle quaternion を合成（intrinsic, Z-first）
  - `Compose(basisX..W, eulerX..Z, out outX..W)`: basis × EulerToQuaternion の Hamilton 積
  - Domain 層配置のため float 4 タプル (qx, qy, qz, qw) のみで完結し `UnityEngine.Quaternion` 不参照
  - pure function、副作用なし、ヒープ確保なし（hot path 仕様）
  - 観測可能な完了条件: 2.1 の grid 突合テストが全件 Green になる
  - _Requirements: 1.3, 4.2, 4.3, 4.4, 4.5_
  - _Boundary: Domain.Services.BonePoseComposer_

- [x] 3. FacialProfile への BonePoses optional 追加
- [x] 3.1 (P) FacialProfile 後方互換テストを書く（Red）
  - 既存コンストラクタ呼出（BonePoses 引数なし）が warning なく通り、`BonePoses` プロパティが空配列を返すことをアサート
  - BonePoses を渡したコンストラクタで防御的コピーが行われることをアサート
  - 既存 `FacialProfileTests` が無修正で全件パスし続けることをアサート（Req 10.1 / 10.3 の構造的保証）
  - 観測可能な完了条件: 後方互換テストファイルが Red、既存テストは無変更
  - _Requirements: 1.4, 1.5, 10.1, 10.3_
  - _Boundary: Domain.Models.FacialProfile_
  - _Depends: 1.4_

- [x] 3.2 (P) FacialProfile に BonePoses プロパティを追加（Green、最小破壊）
  - `ReadOnlyMemory<BonePose> BonePoses { get; }` を追加（default 空）
  - 既存コンストラクタを温存しつつ BonePoses を受け取るオーバーロードを追加
  - `Expression` には一切手を入れない（Req 1.4）
  - 観測可能な完了条件: 3.1 のテストが Green、既存 FacialProfile 利用箇所がコンパイル可能
  - _Requirements: 1.4, 1.5, 10.1, 10.3_
  - _Boundary: Domain.Models.FacialProfile_
  - _Depends: 1.4_

- [x] 4. Adapters/Json: BonePose の JSON DTO とパーサ拡張
- [x] 4.1 (P) BonePoseDto / BonePoseEntryDto の round-trip テストを書く（Red、EditMode）
  - サンプル JSON（`bonePoses[].id`, `bonePoses[].entries[].boneName`, `bonePoses[].entries[].eulerXYZ.{x,y,z}`）→ DTO → Domain BonePose → DTO の名前/値が同値であることをアサート
  - `eulerXYZ` の degree 値が float round-trip で保持されることをアサート（Req 8.5）
  - 多バイト boneName（例: 「頭」「左目_あ」）が壊れずに round-trip することをアサート（Req 2.2）
  - 観測可能な完了条件: `Tests/EditMode/Adapters/Json/BonePoseDtoRoundTripTests.cs` が Red
  - _Requirements: 2.2, 7.1, 7.2, 8.5_
  - _Boundary: Adapters.Json.Dto.BonePoseDto, BonePoseEntryDto_
  - _Depends: 1.4_

- [x] 4.2 (P) BonePoseDto / BonePoseEntryDto を実装（Green）
  - `JsonUtility` 互換の `[Serializable]` クラスとして DTO を定義
  - `BonePoseDto`: `string id` + `List<BonePoseEntryDto> entries`
  - `BonePoseEntryDto`: `string boneName` + `Vector3 eulerXYZ`（degrees）
  - DTO ↔ Domain 変換ヘルパ（DTO → BonePose 構築時に Domain ctor のバリデーションを通す）
  - 観測可能な完了条件: 4.1 のテストが Green
  - _Requirements: 7.1, 7.2, 7.5, 8.5_
  - _Boundary: Adapters.Json.Dto.BonePoseDto, BonePoseEntryDto_
  - _Depends: 1.4_

- [x] 4.3 SystemTextJsonParser に bonePoses ブロック処理を追加するテストを書く（Red、EditMode）
  - `bonePoses` ブロック付き JSON が `FacialProfile.BonePoses` に正しく Domain として乗ることをアサート（Req 7.1, 7.2）
  - `boneName` 欠落 / null / 空のエントリが Warning + skip され、続行することをアサート（Req 7.4）
  - `eulerXYZ` 不在 / 欠損のエントリが Warning + skip されることをアサート（Req 7.4）
  - `JsonSchemaDefinition.Profile.BonePoses` 定数が追加されていることをアサート
  - 観測可能な完了条件: `Tests/EditMode/Adapters/Json/SystemTextJsonParserBonePoseTests.cs` が Red
  - _Requirements: 7.1, 7.2, 7.4, 7.5_
  - _Boundary: Adapters.Json.SystemTextJsonParser, JsonSchemaDefinition_
  - _Depends: 3.2, 4.2_

- [x] 4.4 SystemTextJsonParser を拡張（Green）
  - `ProfileDto` に `public List<BonePoseDto> bonePoses;` を追加
  - `ConvertToProfile` で BonePoseDto[] → BonePose[] 変換（Domain ctor のバリデーション通過）
  - 不正エントリは `Debug.LogWarning` + skip + 続行（**例外を throw しない**）
  - `JsonSchemaDefinition` に `Profile.BonePoses = "bonePoses"` 定数とサブクラス定義を追加
  - 観測可能な完了条件: 4.3 のテストが Green
  - _Requirements: 7.1, 7.2, 7.4, 7.5_
  - _Boundary: Adapters.Json.SystemTextJsonParser, JsonSchemaDefinition_
  - _Depends: 3.2, 4.2_

- [x] 4.5 後方互換テスト: bonePoses 欠落 JSON の読込（Red → Green、EditMode）
  - **重要**: このタスクは `inputSources` の D-5 必須化ポリシーを bonePoses に**踏襲しない**ことを保証する
  - bone-control 導入前の既存 JSON（`bonePoses` フィールドが完全に欠落）を読込み、`FacialProfile.BonePoses` が空配列となり、他のフィールド（layers, expressions, inputSources）が破壊なく読込まれることをアサート（Req 7.3, 10.2）
  - `bonePoses: null` / `bonePoses: []` の両方が空配列扱いとなることをアサート
  - JSON Loader が「bonePoses 必須」エラーを投げないことをアサート（**意図的に必須化バリデーションを書かない**）
  - 既存 `JsonSchemaDefinition.SampleProfileJson` を読ませて従来通りの BlendShape プロファイルが構築されることをアサート（既存 `SampleJsonParseTests` の現行アサーションが無修正で通ること）
  - 観測可能な完了条件: `Tests/EditMode/Adapters/Json/SystemTextJsonParserBonePoseBackwardCompatTests.cs` が Green、既存 JSON parse テストが無修正で通る
  - _Requirements: 1.5, 7.3, 7.5, 10.2, 10.4_
  - _Boundary: Adapters.Json.SystemTextJsonParser_
  - _Depends: 4.4_

- [x] 5. Adapters/ScriptableObject: SO 内包と Mapper 双方向変換
- [x] 5.1 FacialProfileSO の `_bonePoses` Serializable フィールドを追加するテストを書く（Red、EditMode）
  - `BonePoseSerializable` / `BonePoseEntrySerializable`（id, entries[], boneName, Vector3 eulerXYZ）が `[Serializable]` で定義されていることをアサート
  - `FacialProfileSO` の `_bonePoses` フィールドが SerializedProperty として読み取れることをアサート
  - 既存 SO アセット（`_bonePoses` 未設定）が空配列で初期化されることをアサート（Req 8.4 / 10.1）
  - 観測可能な完了条件: `Tests/EditMode/Adapters/ScriptableObject/FacialProfileSOBonePoseTests.cs` が Red
  - _Requirements: 8.1, 8.4, 10.1_
  - _Boundary: Adapters.ScriptableObject.FacialProfileSO_

- [x] 5.2 FacialProfileSO に `_bonePoses` を追加（Green、extend 既存 SO）
  - **既存 `FacialProfileSO.cs` を rewrite せず extend する**: 既存フィールドと CreateAssetMenu 属性を保持
  - `[Serializable] public sealed class BonePoseSerializable { public string id; public BonePoseEntrySerializable[] entries; }` を追加
  - `[Serializable] public sealed class BonePoseEntrySerializable { public string boneName; public Vector3 eulerXYZ; }` を追加
  - `[SerializeField] private BonePoseSerializable[] _bonePoses` と対応 getter を追加
  - 観測可能な完了条件: 5.1 のテストが Green、既存 `FacialProfileSO` 参照箇所が無変更で通る
  - _Requirements: 8.1, 8.4, 10.1_
  - _Boundary: Adapters.ScriptableObject.FacialProfileSO_

- [x] 5.3 FacialProfileMapper の round-trip テストを書く（Red、EditMode）
  - Domain BonePose → BonePoseSerializable → Domain BonePose の名前 / Euler 値が同値であることをアサート（Req 8.1, 8.2, 8.5）
  - SO → JSON 経由 → Domain → SO の経路でも値が保たれることをアサート
  - `_bonePoses` 空配列の SO が Domain で空 BonePoses に変換されることをアサート（Req 10.1）
  - 観測可能な完了条件: `Tests/EditMode/Adapters/ScriptableObject/FacialProfileMapperBonePoseTests.cs` が Red
  - _Requirements: 8.1, 8.2, 8.5, 10.1_
  - _Boundary: Adapters.ScriptableObject.FacialProfileMapper_
  - _Depends: 4.4, 5.2_

- [x] 5.4 FacialProfileMapper を拡張（Green、extend 既存 Mapper）
  - **既存 `FacialProfileMapper.cs` を rewrite せず extend する**: 既存メソッドと変換経路を保持
  - SO → Domain: `_bonePoses` を BonePose[] に変換し、`FacialProfile` ctor へ渡す
  - Domain → SO: BonePose[] を `BonePoseSerializable[]` へ変換し SO へ書込む
  - ランタイムから JSON → SO → Domain が呼べる（Editor 専用 API 不要、Req 8.3）
  - 観測可能な完了条件: 5.3 のテストが Green
  - _Requirements: 8.1, 8.2, 8.3, 8.5, 10.1_
  - _Boundary: Adapters.ScriptableObject.FacialProfileMapper_
  - _Depends: 4.4, 5.2_

- [x] 6. Adapters/Bone: Resolver / AutoAssigner / BoneWriter
- [x] 6.1 Adapters/Bone ディレクトリ構築（Foundation、新規 asmdef なし）
  - `Runtime/Adapters/Bone/` ディレクトリを作成し、`.meta` ペアリングルールに従って配置
  - **新規 asmdef は作らない**: 既存 `Hidano.FacialControl.Adapters.asmdef` の参照配下に新ディレクトリを置く
  - 空のスケルトン（`IBonePoseSource`, `IBonePoseProvider`, `BoneTransformResolver`, `HumanoidBoneAutoAssigner`, `BoneWriter`, `BonePoseSnapshot`）の C# ファイルとそれぞれの `.meta` を配置
  - 観測可能な完了条件: Unity エディタが `Adapters/Bone/` 配下を認識し、Domain / Adapters の asmdef ビルドが通る
  - _Requirements: 5.1_
  - _Boundary: Adapters.Bone (directory scaffolding)_

- [x] 6.2 (P) IBonePoseSource / IBonePoseProvider 契約定義
  - `IBonePoseSource.GetActiveBonePose()` を定義（BoneWriter 自身が実装、内部経路）
  - `IBonePoseProvider.SetActiveBonePose(in BonePose pose)` を定義（外部 = analog-input-binding が消費）
  - `in` 渡しで struct コピーを避け、hot path で alloc しない契約を明文化（Req 11.5）
  - **MINOR-1 反映**: `BoneWriter.Initialize(in BonePose initialPose, string basisBoneName)` で basis をキャッシュ、`Apply()` は引数なしという API 形状をインターフェース層で確定
  - 観測可能な完了条件: 両インターフェースがコンパイルされ、後続実装 / テストから using 可能
  - _Requirements: 5.6, 11.1, 11.3, 11.4, 11.5_
  - _Boundary: Adapters.Bone.IBonePoseSource, IBonePoseProvider_
  - _Depends: 1.4, 6.1_

- [x] 6.3 (P) BoneTransformResolver のテストを書く（Red、PlayMode）
  - PlayMode 配置の理由: 実 Transform 階層を持つ GameObject ツリーが必要
  - 名前から Transform を解決でき、未解決時に `Debug.LogWarning` + null 返却（throw しない）であることをアサート（Req 2.4）
  - 多バイト boneName（「頭」「左目」等）が解決できることをアサート（Req 2.2）
  - 命名規則を強制しない（任意の prefix/suffix で OK）ことをアサート（Req 2.5）
  - `Prime(boneNames)` 一括解決後、ホットパスでは辞書ルックアップのみ（GC alloc ゼロ）であることをアサート
  - 同名 Transform が複数存在する場合は「最初の発見を採用、警告なし」であることをアサート（preview.1 確定挙動、上記スコープ外メモ参照）
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/Bone/BoneTransformResolverTests.cs` が Red
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 5.1_
  - _Boundary: Adapters.Bone.BoneTransformResolver_
  - _Depends: 6.1_

- [x] 6.4 BoneTransformResolver を実装（Green）
  - ルート Transform から再帰探索で名前一致の最初の Transform を返す
  - `Dictionary<string, Transform>` でキャッシュ、`Initialize` 時に `Prime(boneNames)` で一括解決
  - 解決失敗時は `Debug.LogWarning`（同名連続警告は dedupe）+ null 返却
  - 観測可能な完了条件: 6.3 のテストが Green
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 5.1_
  - _Boundary: Adapters.Bone.BoneTransformResolver_
  - _Depends: 6.1_

- [x] 6.5 (P) HumanoidBoneAutoAssigner のテストを書く（Red、PlayMode）
  - PlayMode 配置の理由: 実 Animator + Humanoid Avatar が必要
  - Humanoid Animator から `HumanBodyBones.LeftEye / RightEye` の bone 名を取得できることをアサート（Req 3.1）
  - `HumanBodyBones.Head` を優先し、Head 不在時に `useNeckFallback=true` で Neck を返すことをアサート（Req 3.2）
  - 非 Humanoid Animator または該当スロット未マップの場合に empty 返却 + `Debug.LogWarning` であり throw しないことをアサート（Req 3.4）
  - オプトイン（明示呼出のみ、自動実行されない）であることをアサート（Req 3.3）
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/Bone/HumanoidBoneAutoAssignerTests.cs` が Red
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_
  - _Boundary: Adapters.Bone.HumanoidBoneAutoAssigner_
  - _Depends: 6.1_

- [x] 6.6 HumanoidBoneAutoAssigner を実装（Green）
  - `ResolveEyeBoneNames(Animator)` → `EyeBoneNames(LeftEye, RightEye)` を返す
  - `ResolveBasisBoneName(Animator, useNeckFallback=true)` → Head 名（不在時 Neck）を返す
  - 非 Humanoid / 未マップは empty + Warning（throw しない）
  - Adapters/Bone 配下に配置（Domain 不可、Req 3.5）
  - 観測可能な完了条件: 6.5 のテストが Green
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_
  - _Boundary: Adapters.Bone.HumanoidBoneAutoAssigner_
  - _Depends: 6.1_

- [x] 6.7 BonePoseSnapshot のテストを書く（Red、PlayMode）
  - 解決済み Transform 配列と中間 quaternion (qx, qy, qz, qw) 配列を事前確保し、BonePose 切替時に容量不足のみ拡張、縮小しないことをアサート（Req 6.2, 6.3）
  - 同一 BonePose 継続中は内部配列を再確保しないことをアサート（Req 6.3）
  - `BoneWriter` の `Apply` ホットパスで参照される配列が `null` でないことをアサート
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/Bone/BonePoseSnapshotTests.cs` が Red
  - _Requirements: 6.2, 6.3_
  - _Boundary: Adapters.Bone.BonePoseSnapshot_
  - _Depends: 6.1_

- [x] 6.8 BonePoseSnapshot を実装（Green）
  - 内部 `Transform[]` と `float[4 * capacity]`（または `(float qx, qy, qz, qw)[]`）を保持
  - `EnsureCapacity(int)` で拡張のみ（縮小なし）
  - 観測可能な完了条件: 6.7 のテストが Green
  - _Requirements: 6.1, 6.2, 6.3_
  - _Boundary: Adapters.Bone.BonePoseSnapshot_
  - _Depends: 6.1_

- [x] 7. BoneWriter 本体（適用パイプラインの中核）
- [x] 7.1 BoneWriter の Apply 順序・basis 採取テスト（Red、PlayMode）
  - PlayMode 配置の理由: 実 Animator + Transform 階層 + LateUpdate 順序が必要
  - `Apply()` 開始時に basis bone の `localRotation` を 1 回だけ採取し、entries ループ前にキャッシュすることをアサート（Req 5.5、決定的順序）
  - active BonePose が null / Entries が空のとき、いかなる Transform にも触れないことをアサート（Req 5.4, 10.1）
  - 解決失敗 boneName のエントリは `Debug.LogWarning` + skip し、他のエントリ適用に影響しないことをアサート（Req 2.4）
  - basis bone 解決失敗時は Warning + そのフレームの bone 適用を全 skip（world 軸フォールバックしない、Req 4.6）
  - Animator が Update で bone を書いた後の値（body tilt 込み）に対して BoneWriter が乗り、結果が basis 相対であることをアサート（Req 4.2, 4.5, 5.2, 5.3）
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/Bone/BoneWriterApplyTests.cs` が Red
  - _Requirements: 2.4, 4.2, 4.5, 4.6, 5.2, 5.3, 5.4, 5.5, 10.1_
  - _Boundary: Adapters.Bone.BoneWriter_
  - _Depends: 2.2, 6.2, 6.4, 6.6, 6.8_

- [x] 7.2 BoneWriter の Initialize / Apply / Dispose を実装（Green）
  - `BoneWriter(BoneTransformResolver resolver, Animator animator)` で構築
  - `Initialize(in BonePose initialPose, string basisBoneName)`: basis Transform を解決して `_basisBone` にキャッシュ（**MINOR-1 反映**）、Resolver に entries の boneName を Prime
  - `Apply()`: 引数なし。`_basisBone.localRotation` を 1 回読み、Composer で各エントリの最終 localRotation を計算、対象 Transform に書込む
  - active BonePose null/空 → no-op、basis 未解決 → Warning + skip、bone 名未解決 → Warning + skip + 続行
  - 観測可能な完了条件: 7.1 のテストが Green
  - _Requirements: 2.4, 4.2, 4.5, 4.6, 5.2, 5.3, 5.4, 5.5, 10.1_
  - _Boundary: Adapters.Bone.BoneWriter_
  - _Depends: 2.2, 6.2, 6.4, 6.6, 6.8_

- [x] 7.3 SetActiveBonePose / GetActiveBonePose の next-frame セマンティクステスト（Red、PlayMode）
  - PlayMode 配置の理由: フレーム同期が必要
  - `SetActiveBonePose(in newPose)` 呼出後、**同一フレーム内の Apply** には反映されず、**次フレームの Apply** から反映されることをアサート（Req 11.2）
  - `GetActiveBonePose()` が現在 active な pose を返すことをアサート（Req 5.6, 11.1）
  - 入力源を仮定しない（任意の MonoBehaviour / 任意のスクリプトから呼出可）ことをアサート（Req 11.4）
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/Bone/BoneWriterProviderTests.cs` が Red
  - _Requirements: 5.6, 11.1, 11.2, 11.3, 11.4_
  - _Boundary: Adapters.Bone.BoneWriter (IBonePoseProvider)_
  - _Depends: 7.2_

- [x] 7.4 SetActiveBonePose / GetActiveBonePose を実装（Green）
  - `_pendingPose` / `_activePose` の簡易 swap を内部で持ち、`SetActiveBonePose` は `_pendingPose` に格納
  - `Apply` 開始時に `_pendingPose != null` なら `_activePose` へ swap してから処理
  - `GetActiveBonePose()` は `_activePose` を返す
  - メインスレッド限定契約（preview.1）。スレッド安全保証を持たない
  - 観測可能な完了条件: 7.3 のテストが Green
  - _Requirements: 5.6, 11.1, 11.2, 11.3, 11.4, 11.5_
  - _Boundary: Adapters.Bone.BoneWriter (IBonePoseProvider)_
  - _Depends: 7.2_

- [x] 7.5 RestoreInitialRotations 遅延スナップショットテスト（Red、PlayMode）
  - PlayMode 配置の理由: 実 Transform への書込みと復元の検証が必要
  - **MAJOR-1 反映**: `Initialize` を空 BonePose で呼び、その後 `SetActiveBonePose` で BonePose を流して `Apply` した場合、各エントリの対象 Transform に「初回書込み直前」の `localRotation` が `_initialSnapshot[boneName]` に記録されることをアサート
  - `Dispose` または BoneWriter を制御する側の `OnDisable` 経路で `RestoreInitialRotations()` を呼ぶと、スナップショットを巡回して全対象 Transform が初期姿勢に戻ることをアサート
  - 一度も書込みされなかった Transform は `_initialSnapshot` に登録されず、復元時にも触らないことをアサート
  - 同一 boneName への複数回適用後でも、最初の書込み直前の値で復元されることをアサート
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/Bone/BoneWriterRestoreTests.cs` が Red
  - _Requirements: 5.4, 10.1, 10.3_
  - _Boundary: Adapters.Bone.BoneWriter (RestoreInitialRotations)_
  - _Depends: 7.2_

- [x] 7.6 RestoreInitialRotations を実装（Green、遅延スナップショット）
  - 内部に `Dictionary<string, Quaternion> _initialSnapshot` を保持
  - `Apply()` 内で各エントリの対象 Transform に書込む直前、`_initialSnapshot.ContainsKey(boneName) == false` なら `_initialSnapshot[boneName] = transform.localRotation` を記録（hot path だが key 既存後はチェックのみ、alloc しない）
  - `RestoreInitialRotations()`: スナップショットを巡回し、各 boneName の Transform を `_initialSnapshot[boneName]` に戻す
  - `Dispose()` で内部状態をクリア
  - 観測可能な完了条件: 7.5 のテストが Green
  - _Requirements: 5.4, 10.1, 10.3_
  - _Boundary: Adapters.Bone.BoneWriter (RestoreInitialRotations)_
  - _Depends: 7.2_

- [x] 7.7 BoneWriter の GC ゼロアロケーション検証テスト（Red → Green、PlayMode、Performance）
  - **必須要件**: Req 6.1 / 6.4 / 11.5 の構造的保証として `GCAllocationTests` ファミリに追加
  - 同一 BonePose を複数フレーム継続して `Apply()` した場合に warmup 後の per-frame ヒープ確保が **0 バイト** であることをアサート（`NUnit.Framework.Assert.That(() => writer.Apply(), Is.Not.AllocatingGCMemory())` 相当 / Unity Performance Testing API の `Measure.Method().GC()` 相当）
  - `SetActiveBonePose(in pose)` の呼出経路でも 0 バイトであることをアサート（Req 11.5）
  - 複数エントリ（5 bone）含む BonePose で 0 バイトであることをアサート（Req 6.1）
  - 観測可能な完了条件: `Tests/PlayMode/Performance/BoneWriterGCAllocationTests.cs` が新規追加され Green
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 11.5_
  - _Boundary: Adapters.Bone.BoneWriter (performance)_
  - _Depends: 7.2, 7.4_

- [ ] 7.8* (P) BoneWriter のパフォーマンステスト（同時 10 体 × 5 bone、PlayMode）
  - **このタスクのみ optional**: コア機能の保証は 7.7 で完結し、本タスクは予算スケール検証のため preview.1 では nice-to-have
  - 同時 10 GameObject に BoneWriter を載せ、各 BonePose に 5 エントリ（左右目、頭、首 +α）を流して `Apply()` するシナリオで budget 内に収まることをアサート（Acceptance: Req 6 全体の steering「同時 10 体以上」想定の検証）
  - `Tests/PlayMode/Performance/BoneWriterPerformanceTests.cs` を追加
  - _Requirements: 6.1, 6.2, 6.3_
  - _Boundary: Adapters.Bone.BoneWriter (performance)_
  - _Depends: 7.7_

- [x] 8. Adapters/Playable: FacialController 統合
- [x] 8.1 FacialController に BoneWriter 統合する order テスト（Red、PlayMode）
  - PlayMode 配置の理由: 実 Animator + LateUpdate 同期 + 実フレーム必要
  - 同一フレーム内で **Animator → BlendShape 書込 → BoneWriter.Apply** の順で実行されることをアサート（Req 5.3, 10.3）
  - 既存 `FacialControlMixer` 出力（BlendShape weight、layer slots）が BoneWriter 統合後も無変更であることをアサート（Req 10.3）
  - `OnDisable` で BoneWriter の Restore + Dispose が呼ばれ、書込中だった Transform が初期姿勢に戻ることをアサート
  - `IBonePoseProvider` / `IBonePoseSource` を `FacialController` 経由で外部から呼出可能であることをアサート（Req 11.1, 11.3）
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/Playable/FacialControllerBoneIntegrationTests.cs` が Red
  - _Requirements: 5.3, 10.3, 11.1, 11.2, 11.3_
  - _Boundary: Adapters.Playable.FacialController (extension only)_
  - _Depends: 5.4, 7.4, 7.6_

- [x] 8.2 FacialController に BoneWriter を統合（Green、extend 既存 Controller）
  - **既存 `FacialController.cs` を rewrite せず extend する**: 既存 fields / Initialize / LateUpdate を保持
  - `Initialize` 末尾で `_boneWriter = new BoneWriter(resolver, _animator); _boneWriter.Initialize(profile.BonePoses[0] or empty, basisBoneName)` を呼ぶ
  - basisBoneName は SO 設定または `HumanoidBoneAutoAssigner.ResolveBasisBoneName` から決定（未設定時 `"Head"` をデフォルト）
  - `LateUpdate` の **末尾**（既存 BlendShape 書込の後）で `_boneWriter?.Apply()` を呼ぶ（独立 MonoBehaviour にしない、`[DefaultExecutionOrder]` に依存しない）
  - 公開 API: `SetActiveBonePose(in BonePose pose)` / `BonePose GetActiveBonePose()` を追加（`IBonePoseProvider` / `IBonePoseSource` の delegating wrapper）
  - `OnDisable` で `_boneWriter?.RestoreInitialRotations(); _boneWriter?.Dispose();`
  - 観測可能な完了条件: 8.1 のテストが Green、既存 `FacialController` の Lifecycle テストが無修正で通る
  - _Requirements: 5.3, 5.6, 10.3, 11.1, 11.2, 11.3, 11.4_
  - _Boundary: Adapters.Playable.FacialController (extension only)_
  - _Depends: 5.4, 7.4, 7.6_

- [x] 9. Editor: FacialProfileSO_BonePoseView
- [x] 9.1 BoneNameProvider（候補列挙ヘルパ）を追加
  - 参照モデル配下の Animator / SkinnedMeshRenderer から `Transform.name` を再帰列挙し、ソート + 重複除去した string 配列を返す
  - 既存 `Editor/Common/BlendShapeNameProvider.cs` のパターンを踏襲
  - 参照モデル未設定時は空配列を返す
  - 観測可能な完了条件: `Editor/Common/BoneNameProvider.cs` が追加され、Editor アセンブリでビルド成功
  - _Requirements: 9.2, 9.3_
  - _Boundary: Editor.Common.BoneNameProvider_

- [x] 9.2 FacialProfileSO_BonePoseView の UI Toolkit 表示テスト（Red、EditMode）
  - `Foldout` + `ListView` で BonePose 一覧が表示されることをアサート（Req 9.1）
  - 各エントリ行に boneName 入力（typeahead 候補表示）+ Vector3Field（X, Y, Z degrees）+ 削除ボタンが配置されることをアサート（Req 9.2, 9.4）
  - エントリ追加/削除/編集後に `EditorUtility.SetDirty(target)` が呼ばれ、SO アセットがダーティになることをアサート（Req 9.4）
  - ランタイム asmdef からは参照不可（Editor asmdef 内に閉じる）であることをアサート（Req 9.6）
  - 観測可能な完了条件: `Tests/EditMode/Editor/Inspector/FacialProfileSO_BonePoseViewTests.cs` が Red
  - _Requirements: 9.1, 9.2, 9.4, 9.6_
  - _Boundary: Editor.Inspector.FacialProfileSO_BonePoseView_
  - _Depends: 5.2, 9.1_

- [x] 9.3 FacialProfileSO_BonePoseView を実装（Green）
  - UI Toolkit (`UnityEngine.UIElements` / `UnityEditor.UIElements`) で `Foldout` + `ListView` を構築（既存 `FacialProfileSO_InputSourcesView` と同型）
  - 各エントリ行: boneName 用 `DropdownField` + `BoneNameProvider` の候補列挙（Req 9.2、research §7）。候補がない場合は `TextField` フォールバック
  - Vector3Field で X/Y/Z degrees を編集
  - 削除ボタンで `_bonePoses` から該当エントリを除去し `EditorUtility.SetDirty` + Undo 連携
  - 観測可能な完了条件: 9.2 のテストが Green、Inspector で実 SO を編集できる
  - _Requirements: 9.1, 9.2, 9.4, 9.6_
  - _Boundary: Editor.Inspector.FacialProfileSO_BonePoseView_
  - _Depends: 5.2, 9.1_

- [x] 9.4 (P) Humanoid 自動アサインボタンを追加
  - 「Humanoid 自動アサイン」ボタンを BonePoseView に配置（Req 9.3）
  - クリック時に `HumanoidBoneAutoAssigner.ResolveEyeBoneNames(animator)` で eye bone 名を取得し、LeftEye / RightEye の 2 エントリを既定 Euler (0, 0, 0) で `_bonePoses` に追加
  - 非 Humanoid のときはボタンが Disabled（または押下時に Warning ログ + no-op）
  - 観測可能な完了条件: 既存 SO に対し 1 クリックで eye 2 エントリが追加され Inspector に表示される
  - _Requirements: 3.3, 9.3_
  - _Boundary: Editor.Inspector.FacialProfileSO_BonePoseView (auto-assign button)_
  - _Depends: 6.6, 9.3_

- [x] 9.5 (P) JSON Import / Export ボタンを追加
  - **判定**: 既存 FacialProfileSO Inspector に JSON I/E のパターンがあるため、同じパターンの転写であり Req 9.5 で必須化されている → optional ではなく必須実装とする
  - 「JSON Import」ボタン: `EditorUtility.OpenFilePanel` で JSON 選択 → `SystemTextJsonParser.ParseProfile` → `FacialProfileMapper` で SO の `_bonePoses` を上書き
  - 「JSON Export」ボタン: 現在の `_bonePoses` を Domain BonePose[] に変換 → JSON 文字列化 → `EditorUtility.SaveFilePanel` で保存
  - 観測可能な完了条件: 既存 SO の bonePoses を JSON 経由で round-trip でき、Inspector の表示が変わる
  - _Requirements: 9.5_
  - _Boundary: Editor.Inspector.FacialProfileSO_BonePoseView (JSON I/E)_
  - _Depends: 4.4, 5.4, 9.3_

- [x] 9.6 FacialProfileSOEditor に BonePoseView を組込
  - **既存 `FacialProfileSOEditor.cs` を rewrite せず extend する**: `_bonePoseView` フィールドと `CreateInspectorGUI` での `root.Add(_bonePoseView.RootElement)` 1 行追加のみ
  - 既存 InputSourcesView 直下に配置（UX 一貫性）
  - 観測可能な完了条件: FacialProfileSO アセットを Inspector で開くと既存 View に加えて BonePose セクションが表示される
  - _Requirements: 9.1, 9.6_
  - _Boundary: Editor.Inspector.FacialProfileSOEditor (extension only)_
  - _Depends: 9.3_

- [x] 10. End-to-End 統合検証
- [x] 10.1 JSON → SO → Domain → BoneWriter → Transform の E2E テスト（PlayMode）
  - JSON ファイル（`bonePoses` ブロック付き）から `FacialProfileSO` をロード → `FacialController.Initialize` → `LateUpdate` で `BoneWriter.Apply` → 対象 Transform に最終 localRotation が書込まれているという全経路をアサート
  - basis bone 採取 → Composer 合成 → Transform 書込みまでのデータフローが値破壊なしであることをアサート（Req 8.5 の round-trip と整合）
  - body tilt 込みのモーション再生中でも、BonePose の Euler 値が basis 相対で正しく適用され、tilt が gaze に漏れないことをアサート（Req 4.5、design §System Flows の主要不変条件）
  - 観測可能な完了条件: `Tests/PlayMode/Integration/BonePoseEndToEndTests.cs` が Green
  - _Requirements: 1.1, 4.2, 4.5, 5.2, 5.3, 7.2, 8.1, 8.2, 8.5, 10.3_
  - _Boundary: cross-layer integration_
  - _Depends: 8.2, 9.5_

- [x] 10.2 (P) IBonePoseProvider 経由の analog-input-binding 互換性検証（PlayMode）
  - **このタスクは analog-input-binding spec が消費する API の安定性を保証する**
  - 任意の MonoBehaviour（テスト用 Fake Provider）から `FacialController.SetActiveBonePose(in pose)` を呼び、次フレームから `Apply()` 結果が変わることをアサート（Req 11.2, 11.3, 11.4）
  - 入力源を仮定しない汎用 API であることを Fake Provider のシグネチャレビューで確認（Req 11.4）
  - hot path（Set/Apply）で GC alloc 0 バイトであることをアサート（Req 11.5、7.7 と重複だが API 経路で再確認）
  - 観測可能な完了条件: `Tests/PlayMode/Integration/BonePoseProviderApiTests.cs` が Green
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_
  - _Boundary: Adapters.Bone.IBonePoseProvider (API surface)_
  - _Depends: 8.2_

- [x] 10.3 (P) 既存 BlendShape パイプラインの非破壊検証（PlayMode）
  - 既存 `FacialControlMixer` / `LayerInputSourceAggregator` の出力（BlendShape weight, layer slots）が BoneWriter 統合後も bit-exact で同一であることをアサート（Req 10.3）
  - 既存 `FacialControllerLifecycleTests` / `SampleJsonParseTests` が **無修正で全件パス**することをアサート
  - 既存 GC alloc テスト（BlendShape 経路）が BoneWriter 追加後も予算内であることをアサート（Req 10.3）
  - 観測可能な完了条件: 既存テストスイート（BlendShape 関連）が無修正で全件 Green、新規 `Tests/PlayMode/Integration/BlendShapeNonRegressionTests.cs` が Green
  - _Requirements: 10.1, 10.3_
  - _Boundary: cross-layer non-regression_
  - _Depends: 8.2_

- [ ] 10.4* (P) BoneWriter verbose 診断ログ（optional、preview.2 候補）
  - **optional 判定理由**: 既存 `LayerInputSourceAggregator` に verbose mode が存在し、対称性として BoneWriter にも欲しいが、Req 1-11 では要求されていない nice-to-have
  - verbose flag を `BoneWriter` に追加し、有効時は basis 採取値、各 entry の解決状態、最終 localRotation 書込値を `Debug.Log` で出力
  - 通常モード（既定 false）では出力されないことを確認
  - _Requirements: 5.5_
  - _Boundary: Adapters.Bone.BoneWriter (diagnostics)_
  - _Depends: 7.2_
