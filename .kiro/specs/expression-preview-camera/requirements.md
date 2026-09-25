# Requirements Document

## Introduction

ExpressionCreatorWindow のプレビューカメラ制御を、`PreviewRenderWrapper` 内の自前 Orbit/Zoom 実装から、npm 公開パッケージ `com.hidano.scene-view-style-camera-controller@1.0.0` が提供する `CameraState` / Handler 群に完全置換する。

現状のプレビューはパン（平行移動）に非対応であり、UX として不十分である。本フィーチャーでは Orbit・Pan・Dolly の三操作を IMGUI `Event.current` 経由で Handler に橋渡しする薄いレイヤーを実装し、Preview 領域にカメラリセットボタンを追加する。既存の BlendShape スライダー・AnimationClip 保存等の機能にリグレッションを生じさせてはならない。

## Boundary Context

- **スコープ内**: `manifest.json` へのパッケージ依存追加、`PreviewRenderWrapper` の全面書き換え（`_rotation`/`_zoom` 廃止・`CameraState` への一本化）、IMGUI → Handler 橋渡し（Orbit/Pan/Dolly）、`ExpressionCreatorWindow` プレビュー領域へのカメラリセットボタン追加、EditMode テストによる Handler 結線検証
- **スコープ外**: LookAround（右ドラッグ見回し）・FlyThrough（WASDQE 移動）・FoV 操作の統合、ExpressionCreatorWindow の UI 再配置（リセットボタン追加以外）、Runtime 側への変更、サンプルシーン
- **隣接システム**: `ExpressionCreatorWindow` は `PreviewRenderWrapper` の公開 API（`HandleInput`・`Render`・`IsInitialized` 等）を介してプレビューを操作しており、本フィーチャー完了後も同 API シグネチャを維持する

## Requirements

### 1. パッケージ依存の追加

**Objective:** As a Unity エンジニア, I want `com.hidano.scene-view-style-camera-controller@1.0.0` をパッケージマネージャ経由で導入できる, so that 既存の ScopedRegistry 設定と整合しつつ手動インストール不要でパッケージが解決される。

#### Acceptance Criteria

1. The PreviewRenderWrapper shall require `com.hidano.scene-view-style-camera-controller` version `1.0.0` が `FacialControl/Packages/manifest.json` の `dependencies` に追加されていること。
2. The PreviewRenderWrapper shall require `manifest.json` の `scopedRegistries` に `name: "npmjs"`, `url: "https://registry.npmjs.org"`, `scopes: ["com.hidano"]` のエントリが存在すること（既存エントリが既に存在する場合は重複追加しない）。
3. When Unity Editor がプロジェクトを開く, the Unity Package Manager shall `com.hidano.scene-view-style-camera-controller@1.0.0` を npmjs レジストリから自動解決しエラーなくインポートすること。
4. If `com.hidano.scene-view-style-camera-controller` の依存する `com.unity.inputsystem` のバージョンが `manifest.json` に宣言済みのバージョン（1.17.0）と競合しない, the Unity Package Manager shall 競合エラーを発生させないこと。

---

### 2. PreviewRenderWrapper の CameraState ベース実装への全面置換

**Objective:** As a Unity エンジニア, I want `PreviewRenderWrapper` が自前の `_rotation`/`_zoom` フィールドを持たず `CameraState` で状態を一元管理する, so that パン操作を含む三軸のカメラ制御が正確かつ保守しやすい形で実現される。

#### Acceptance Criteria

1. The PreviewRenderWrapper shall `_rotation`（`Vector2`）・`_zoom`（`float`）・`_isDragging`（`bool`）・`_lastMousePos`（`Vector2`）のフィールドを持たないこと。
2. The PreviewRenderWrapper shall `SceneViewStyleCameraController.CameraState` 型のフィールドを内部状態として保持し、カメラ位置・姿勢の計算に使用すること。
3. The PreviewRenderWrapper shall 初期 `CameraState` を `pivotPoint = bounds.center`、`pivotDistance = bounds.extents.magnitude * 2f`、`rotation = Quaternion.identity`、`position = pivotPoint - rotation * Vector3.forward * pivotDistance` で算出し、`Setup()` 呼び出し時に初期化すること。
4. When `Render(rect)` が呼ばれる, the PreviewRenderWrapper shall `CameraState` から `PreviewRenderUtility.camera` の `transform.position` および `transform.rotation` を設定してレンダリングすること。
5. The PreviewRenderWrapper shall 既存の `public` プロパティ（`IsInitialized`・`PreviewInstance`）および `Setup()`・`Cleanup()`・`Render()`・`HandleInput()`・`Dispose()`・`CalculateBounds()` のシグネチャを維持すること（`Rotation` プロパティ・`Zoom` プロパティは削除してよい）。
6. The PreviewRenderWrapper shall `RotationSensitivity`・`ZoomSensitivity`・`ZoomMin`・`ZoomMax`・`PitchLimit` の定数を廃止し、Handler が提供する感度パラメータで代替すること。

---

### 3. IMGUI イベントから Handler への橋渡し（Orbit / Pan / Dolly）

**Objective:** As a Unity エンジニア, I want プレビュー領域内の IMGUI マウスイベントを `OrbitHandler`・`PanHandler`・`DollyHandler` の `Apply` メソッドに変換する薄いレイヤーが `PreviewRenderWrapper` 内に存在する, so that Scene ビュー準拠の操作感で Orbit・Pan・Dolly が機能する。

#### Acceptance Criteria

1. When Alt キーを押しながら左ボタンドラッグ (`EventType.MouseDrag`, `button == 0`, `alt == true`) が発生し、かつマウス座標が `rect` 内にある, the PreviewRenderWrapper shall `OrbitHandler.Apply` を呼び出して `CameraState` を更新すること。
2. When 中ボタンドラッグ (`EventType.MouseDrag`, `button == 2`) が発生し、かつマウス座標が `rect` 内にある, the PreviewRenderWrapper shall `PanHandler.Apply` を呼び出して `CameraState` を更新すること。
3. When スクロールホイール (`EventType.ScrollWheel`) イベントが発生し、かつマウス座標が `rect` 内にある, the PreviewRenderWrapper shall `DollyHandler.Apply` を呼び出して `CameraState` を更新すること。
4. When Alt キーを押しながら右ボタンドラッグ (`EventType.MouseDrag`, `button == 1`, `alt == true`) が発生し、かつマウス座標が `rect` 内にある, the PreviewRenderWrapper shall `DollyHandler.Apply` を呼び出して `CameraState` を更新すること。
5. When いずれかの Handler によって `CameraState` が変化した, the PreviewRenderWrapper shall `HandleInput()` が `true` を返すこと。
6. When マウス座標が `rect` 外にある, the PreviewRenderWrapper shall いずれの Handler も呼び出さず `HandleInput()` が `false` を返すこと。
7. When Handler が処理したイベントに対して, the PreviewRenderWrapper shall `Event.current.Use()` を呼び出し他の IMGUI 要素へのイベント伝播を防ぐこと。
8. The PreviewRenderWrapper shall `SceneViewStyleCameraController` の `MonoBehaviour`（`Update()` / InputSystem 依存）を参照・インスタンス化しないこと。使用できるのは `CameraState`（struct）と `Handlers.OrbitHandler`・`Handlers.PanHandler`・`Handlers.DollyHandler` の public static API のみとする。

---

### 4. カメラリセット機能

**Objective:** As a Unity エンジニア, I want プレビュー領域にカメラリセットボタンを配置する, so that 操作途中でもワンクリックで初期視点に戻ることができる。

#### Acceptance Criteria

1. The ExpressionCreatorWindow shall `ExpressionCreatorWindow` の左パネルにプレビュー領域（`IMGUIContainer`）と隣接する「カメラリセット」ボタンを配置すること。
2. When 「カメラリセット」ボタンが押される, the PreviewRenderWrapper shall `CameraState` を `Setup()` 時に設定した初期状態に復元すること。
3. When 「カメラリセット」ボタンが押される, the ExpressionCreatorWindow shall プレビューを再描画すること（`_previewContainer.MarkDirtyRepaint()` 相当）。
4. The PreviewRenderWrapper shall 初期 `CameraState` を返す `ResetCamera()` メソッド（または同等の公開 API）を提供すること。
5. The ExpressionCreatorWindow shall 既存の「全スライダーリセット」ボタンと「カメラリセット」ボタンを視覚的に区別できる形でレイアウトすること（同一ボタンに統合しない）。

---

### 5. ExpressionCreatorWindow 既存機能の非影響保証

**Objective:** As a Unity エンジニア, I want `PreviewRenderWrapper` の置き換えによって `ExpressionCreatorWindow` の他機能が影響を受けない, so that preview.1 に向けたリグレッションゼロを担保できる。

#### Acceptance Criteria

1. While プレビューが初期化済みである, the ExpressionCreatorWindow shall BlendShape スライダー操作に応じて `PreviewRenderUtility.camera` の映像が更新されること。
2. When 「Expression を保存」ボタンが押される, the ExpressionCreatorWindow shall カメラ状態の変更に関わらず JSON ファイルへの書き込みと `FacialProfileSO` の更新が正常に完了すること。
3. When モデル（GameObject）が変更される, the ExpressionCreatorWindow shall `PreviewRenderWrapper.Setup()` が再呼び出しされ、新モデルのプレビューが表示されること。
4. When ウィンドウが閉じられる (`OnDisable`), the ExpressionCreatorWindow shall `PreviewRenderWrapper.Dispose()` が呼ばれ、プレビューリソースが解放されること。
5. The ExpressionCreatorWindow shall `HandleInput()` および `Render()` の呼び出し順序（`HandleInput` を先、`Render` を後）を維持すること。
6. If `_targetObject` が null である, the PreviewRenderWrapper shall `HandleInput()` および `Render()` が呼ばれても例外を発生させないこと。

---

### 6. Handler 結線の EditMode テスト可能設計

**Objective:** As a Unity エンジニア, I want `PreviewRenderWrapper` の Handler 結線ロジックを EditMode テストで検証できる, so that IMGUI イベントと `CameraState` の遷移仕様を自動テストで保護できる。

#### Acceptance Criteria

1. The PreviewRenderWrapper shall IMGUI 入力を表す独自 `PreviewInputFrame` 構造体（マウスボタン状態・座標・デルタ・スクロール量・modifier キー状態を持つ値型）を定義し、`HandleInput(Rect rect)` は内部で `Event.current` を `PreviewInputFrame` に変換して `HandleInput(Rect rect, PreviewInputFrame frame)` オーバーロードを呼び出す形で実装すること。EditMode テストは `HandleInput(Rect rect, PreviewInputFrame frame)` に直接 `PreviewInputFrame` インスタンスを渡すことで IMGUI コンテキスト外から Handler 結線を検証できること。
2. When Alt+左ドラッグに相当する入力データが与えられる, the PreviewRenderWrapper shall `OrbitHandler.Apply` が呼ばれた後の `CameraState` がドラッグ前の状態と異なること（EditMode テストで検証可能）。
3. When 中ドラッグに相当する入力データが与えられる, the PreviewRenderWrapper shall `PanHandler.Apply` が呼ばれた後の `CameraState` がパン前の状態と異なること（EditMode テストで検証可能）。
4. When ホイールスクロールに相当する入力データが与えられる, the PreviewRenderWrapper shall `DollyHandler.Apply` が呼ばれた後の `CameraState` がドリー前の状態と異なること（EditMode テストで検証可能）。
5. The PreviewRenderWrapper shall `ResetCamera()` 呼び出し後の `CameraState` が `Setup()` 直後の初期値と等しいこと（EditMode テストで検証可能）。
6. The PreviewRenderWrapper shall EditMode テストが `FacialControl/Packages/com.hidano.facialcontrol/Tests/EditMode/Editor/` に配置されること（Common サブディレクトリは設けない）。
