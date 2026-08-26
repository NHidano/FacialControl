# 実装計画

## タスク一覧

- [ ] 1. Foundation: InputSystemAdapter のリファクタリング（MonoBehaviour → IDisposable）
- [ ] 1.1 既存テスト（InputSystemAdapterTests）を新しいコンストラクタ受け取り API に合わせて修正し、Red → Green サイクルを確立する
  - `InputSystemAdapter` が `new InputSystemAdapter(facialController)` で生成される前提で既存テストのセットアップを書き換える
  - `OnDisable()` の代わりに `Dispose()` を呼び出す形に修正する
  - この時点ではテストがコンパイルエラーまたは Red になることを確認する（TDD の Red 段階）
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

- [ ] 1.2 `InputSystemAdapter` を MonoBehaviour から純粋 C# クラスへ変換し、テストを Green に戻す
  - `MonoBehaviour` 継承を除去し `[AddComponentMenu]` 属性を削除する
  - `[SerializeField]` を除去し、`FacialController` の参照をコンストラクタ引数で受け取るよう変更する
  - `OnDisable()` を `IDisposable.Dispose()` に置き換え、内部で `UnbindAll()` を呼び出す
  - 既存の `BindExpression`、`UnbindExpression`、`UnbindAll`、`FacialController` プロパティのシグネチャを変更しない
  - `FacialController` が未初期化のときに InputAction がトリガーされても例外をスローせず安全に無視することを確認する
  - 1.1 で修正したテストが全 Green になることを確認する
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 3.7_

---

- [ ] 2. Core: InputBinding ドメインモデルの定義
- [ ] 2.1 `InputBinding` ドメインモデルのテストを先に作成する（Red）
  - `ActionName` と `ExpressionId` の正常値コンストラクタが成功することを検証するテストケースを書く
  - null または空文字を渡した場合に `ArgumentException` がスローされることを検証するテストケースを書く
  - 同一値のインスタンス同士の等価性判定が正しく動作することを検証するテストケースを書く
  - `UnityEngine` 名前空間への依存がないことをアセンブリ参照で確認できる構成にする
  - この時点でテストが Red（コンパイルエラーまたは未実装エラー）になることを確認する
  - _Requirements: 1.1, 1.2, 1.3, 1.4_
  - _Boundary: InputBinding（Domain）_

- [ ] 2.2 `InputBinding` struct を実装してテストを Green にする
  - `ActionName`（string）と `ExpressionId`（string）の読み取り専用フィールドを持つ `readonly struct` として定義する
  - コンストラクタで null/空文字を検証し、違反時は `ArgumentException` をスローする
  - `IEquatable<InputBinding>` を実装し、値ベースの等価性を提供する
  - `UnityEngine` 名前空間の参照を一切含まない Domain 層（`Hidano.FacialControl.Domain.Models`）に配置する
  - 2.1 の全テストが Green になることを確認する
  - _Requirements: 1.1, 1.2, 1.3, 1.4_
  - _Boundary: InputBinding（Domain）_

---

- [ ] 3. Core: InputBindingProfileSO の ScriptableObject 新設
- [ ] 3.1 `InputBindingProfileSO` のテストを先に作成する（Red）
  - `_actionAsset` が null のとき空リストが返されることを検証するテストケースを書く
  - シリアライズフィールドから `IReadOnlyList<InputBinding>` への変換が正しく行われることを検証するテストケースを書く
  - バインディングペアリストが ActionName / ExpressionId として正しく取り出せることを検証するテストケースを書く
  - この時点でテストが Red になることを確認する
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_
  - _Boundary: InputBindingProfileSO（Adapters/ScriptableObject）_

- [ ] 3.2 `InputBindingProfileSO` ScriptableObject を実装してテストを Green にする
  - `[CreateAssetMenu(menuName = "FacialControl/Input Binding Profile")]` を付与した ScriptableObject として定義する
  - `[SerializeField] InputActionAsset _actionAsset` フィールドを追加する
  - `[SerializeField] string _actionMapName` フィールドをデフォルト値 `"Expression"` で追加する
  - `[SerializeField] List<InputBindingEntry> _bindings` フィールドを追加する（`InputBindingEntry` は `[Serializable]` な内部クラス）
  - `GetBindings()` で `IReadOnlyList<InputBinding>` に変換して返し、`_actionAsset` が null の場合は空リストを返す
  - 3.1 の全テストが Green になることを確認する
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_
  - _Boundary: InputBindingProfileSO（Adapters/ScriptableObject）_

---

- [ ] 4. Core: FacialInputBinder MonoBehaviour の新設
- [ ] 4.1 `FacialInputBinder` の PlayMode テストを先に作成する（Red）
  - `OnEnable` 時に `InputBindingProfileSO` からバインディングが登録され、InputAction を `Press()` すると `FacialController` の Expression がアクティブ化されることを検証するテストケースを書く
  - `_bindingProfile` が null のとき警告ログが出力され例外がスローされないことを検証するテストケースを書く
  - `ExpressionId` が現在のプロファイルに存在しない場合に警告ログが出力され、該当行をスキップして残りのバインディングは登録されることを検証するテストケースを書く
  - `OnDisable` 時に全バインディングが解除され ActionMap が無効化されることを検証するテストケースを書く
  - この時点でテストが Red になることを確認する
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_
  - _Boundary: FacialInputBinder（Adapters/Input）_

- [ ] 4.2 `FacialInputBinder` MonoBehaviour を実装してテストを Green にする
  - `[AddComponentMenu("FacialControl/Facial Input Binder")]` を付与した MonoBehaviour として定義する
  - `[SerializeField] FacialController _facialController` と `[SerializeField] InputBindingProfileSO _bindingProfile` フィールドを Inspector に公開する
  - `OnEnable` で `_bindingProfile._actionAsset` をインスタンス化し対象 ActionMap を有効化、`new InputSystemAdapter(controller)` を生成し全バインディングを `BindExpression` で登録する
  - バインディング登録前に `ExpressionId` の存在確認を行い、存在しない場合は警告ログを出力してそのバインディングのみスキップする
  - `_bindingProfile` が null の場合は警告ログを出力してバインディング登録を行わない
  - `OnDisable` で `UnbindAll()`、`Dispose()`、ActionMap の無効化を行う
  - 4.1 の全テストが Green になることを確認する
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_
  - _Boundary: FacialInputBinder（Adapters/Input）_

---

- [ ] 5. Core: InputBindingProfileSOEditor の Inspector GUI 実装
- [ ] 5.1 `InputBindingProfileSOEditor` を UI Toolkit で実装する
  - `InputBindingProfileSO` を対象とした `CustomEditor` として定義し、UI Toolkit（`VisualElement`）ベースで Inspector をオーバーライドする
  - `InputActionAsset` 参照を設定する ObjectField を表示する
  - Editor 専用の参照用 `FacialProfileSO` ObjectField を表示する（SO 本体には保存しない）
  - ActionMap ドロップダウンを配置し、`InputActionAsset` が設定されたとき Asset 内の ActionMap 名を自動列挙する
  - バインディング一覧を ListView または ScrollView で表示し、各行に Action ドロップダウンと Expression ドロップダウンを配置する
  - ActionMap が選択されたとき、その ActionMap 内の Action 名を各行の Action ドロップダウンに自動列挙する
  - `FacialProfileSO` が設定されたとき `JsonFilePath` から JSON を 1 度パースして Expression 名リストをキャッシュし、各行の Expression ドロップダウンに列挙する（保存時は ExpressionId で記録する）
  - バインディング追加ボタンと各行の削除ボタンを提供する
  - `InputActionAsset` または ActionMap が変更されたとき一覧表示を自動再構築する
  - JSON が外部編集された場合のための手動リフレッシュボタンを設ける
  - Inspector を開いた状態でバインディングを追加・削除・変更でき、変更が `InputBindingProfileSO` にシリアライズされることを確認する
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8_
  - _Boundary: InputBindingProfileSOEditor（Editor/Inspector）_

---

- [ ] 6. Integration: サンプルアセット一式の整備とサンプルシーン更新
- [ ] 6.1 サンプル専用の最小 FacialProfile JSON とアセットを新設する
  - `FacialControl/Assets/StreamingAssets/FacialControl/sample_profile.json` を新規作成し、Donut-Chan モデルの「まばたき」BlendShape を使うまばたき Expression（固定 GUID）を含める
  - `FacialControl/Assets/Samples/SampleFacialProfileSO.asset`（`FacialProfileSO`）を新設し、`sample_profile.json` を参照するよう設定する
  - 既存の `NewFacialProfile.asset` は参照せず、サンプルが独立した状態を保つことを確認する
  - _Requirements: 5.2, 5.3, 5.7_
  - _Boundary: Assets/Samples, Assets/StreamingAssets_

- [ ] 6.2 `SampleInputBinding.asset` を新設し、まばたき Expression にバインドする
  - `FacialControl/Assets/Samples/SampleInputBinding.asset`（`InputBindingProfileSO`）を新設する
  - `FacialControlDefaultActions.inputactions` を `_actionAsset` に設定し、`Trigger1` が 6.1 で作成したまばたき Expression の ID にバインドされた状態にする
  - Inspector で Asset を開いたとき Action / Expression ドロップダウンが正しく表示されることを確認する
  - _Requirements: 5.4_
  - _Boundary: Assets/Samples_
  - _Depends: 6.1_

- [ ] 6.3 サンプルシーンを `FacialInputBinder` ベースに移行し、`TestExpressionToggle.cs` を削除する
  - `Assets/Samples/TestExpressionToggle.cs` と対応する `.meta` ファイルを削除する
  - サンプルシーンの対象 GameObject から `TestExpressionToggle` コンポーネントを削除する
  - `FacialController` の Profile に `SampleFacialProfileSO` を設定し、`FacialInputBinder` コンポーネントを追加して `SampleInputBinding.asset` を割り当てる
  - サンプルシーンを実行したとき、キーボード `1` キー押下でまばたき表情がトグル動作することを確認する
  - _Requirements: 5.1, 5.5, 5.6, 5.7_
  - _Boundary: Assets/Samples, SampleScene_
  - _Depends: 6.2_

---

- [ ] 7. Validation: 統合確認とリグレッションテスト
- [ ] 7.1 全 EditMode テストを実行してリグレッションがないことを確認する
  - `InputBindingTests`（EditMode）が全て Green であることを確認する
  - `InputBindingProfileSOTests`（EditMode）が全て Green であることを確認する
  - 既存 EditMode テストに新たな失敗がないことを確認する
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

- [ ] 7.2 全 PlayMode テストを実行してリグレッションがないことを確認する
  - `FacialInputBinderTests`（PlayMode）が全て Green であることを確認する
  - `InputSystemAdapterTests`（PlayMode）が引き続き Green であることを確認する（リファクタリング後のリグレッション確認）
  - 既存 PlayMode テストに新たな失敗がないことを確認する
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 7.1, 7.2, 7.3, 7.4, 7.5_

- [ ]* 7.3 `InputBindingProfileSOEditor` の受け入れ基準に沿った追加テストカバレッジを確認する
  - Action / Expression ドロップダウンの自動列挙が要件 4.4〜4.6 を満たすことを手動確認またはテストで検証する
  - バインディング追加・削除ボタンが要件 4.7 を満たすことを確認する
  - `InputActionAsset` / ActionMap 変更時の一覧再構築が要件 4.8 を満たすことを確認する
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8_

---

- [ ] 8. Documentation: クイックスタート・README・CHANGELOG の更新
- [ ] 8.1 `quickstart.md` にキーコンフィグの設定セクションを追加する
  - `Documentation~/quickstart.md` に「6. キーコンフィグの設定」セクションを追加する（既存セクションの番号は適宜繰り下げる）
  - `InputBindingProfileSO` の作成手順（Project ウィンドウ → Create → FacialControl → Input Binding Profile）を記載する
  - `FacialInputBinder` の配置と割り当て手順を記載する（FacialController と InputBindingProfileSO のアサイン）
  - `FacialControlDefaultActions.inputactions` の `Trigger1〜Trigger12` 汎用スロットの説明を含める
  - InputActions Asset を複製してキーバインドをカスタマイズする手順を記載する
  - _Requirements: 6.1_
  - _Boundary: Documentation_

- [ ] 8.2 `README.md` の主要機能一覧に「キーコンフィグの永続化」を追記する
  - パッケージ `README.md` の主要機能セクションに「キーコンフィグの永続化（`InputBindingProfileSO` + `FacialInputBinder`）」を追加する
  - 簡潔な 1〜2 行の説明とともに、詳細は `quickstart.md` を参照させる
  - _Requirements: 6.2_
  - _Boundary: Documentation_

- [ ] 8.3 `CHANGELOG.md` の preview.1 エントリに本機能の変更内容を記載する
  - `CHANGELOG.md` の `[0.1.0-preview.1]` セクションに `### Added` として `InputBinding`、`InputBindingProfileSO`、`FacialInputBinder`、`InputBindingProfileSOEditor`、サンプルアセット一式の追加を記載する
  - `### Breaking Changes` セクションを設け、`InputSystemAdapter` が MonoBehaviour から純粋 C# クラスへ変更されたことと、移行方法（`new InputSystemAdapter(facialController)` の使用）を明記する
  - `### Removed` として `TestExpressionToggle.cs`（サンプル）の削除を記載する
  - _Requirements: 6.3, 7.6_
  - _Boundary: Documentation_
