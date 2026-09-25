# Implementation Plan

## タスク一覧

- [x] 1. Foundation: パッケージ依存と asmdef の設定
- [x] 1.1 manifest.json へのパッケージ依存追加
  - `FacialControl/Packages/manifest.json` の `dependencies` に `"com.hidano.scene-view-style-camera-controller": "1.0.0"` を追加する
  - `scopedRegistries` に `com.hidano` スコープエントリが既に存在することを確認し、重複追加しない
  - Unity Editor 起動時に `com.hidano.scene-view-style-camera-controller@1.0.0` がエラーなく解決されることを確認できる状態になっていること
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 1.2 Editor asmdef への SceneViewStyleCameraController 参照追加
  - `Hidano.FacialControl.Editor.asmdef` の `references` に `"SceneViewStyleCameraController"` を追加する
  - `Hidano.FacialControl.Tests.EditMode.asmdef` の `references` にも `"SceneViewStyleCameraController"` を明示追加する（推移的参照非サポート対応）
  - 両 asmdef がコンパイルエラーなく `CameraState` / Handler 型を参照できる状態になっていること
  - _Requirements: 1.1, 6.6_

- [x] 2. PreviewInputFrame 構造体の新設
  - `Editor/Common/PreviewInputFrame.cs` に `readonly struct PreviewInputFrame` を定義する
  - フィールドは `EventType`・`Button`・`MousePosition`・`Delta`・`ScrollDelta`・`Alt` の 6 つで、すべて `public readonly`
  - コンストラクタで全フィールドを初期化し、`UnityEngine.Event` への参照を持たない純粋な値型として実装する
  - EditMode テストから `new PreviewInputFrame(...)` で直接インスタンス化できる状態になっていること
  - _Requirements: 6.1_

- [x] 3. PreviewRenderWrapper の CameraState ベース実装
- [x] 3.1 自前カメラ状態フィールドの廃止と CameraState 一元管理
  - `_rotation`（`Vector2`）・`_zoom`（`float`）・`_isDragging`（`bool`）・`_lastMousePos`（`Vector2`）のフィールドを削除する
  - `_state: CameraState` と `_initialState: CameraState` の 2 フィールドを追加し、カメラ状態を一元管理する
  - `RotationSensitivity`・`ZoomSensitivity`・`ZoomMin`・`ZoomMax`・`PitchLimit` の public 定数を廃止し、Handler に渡す private 感度定数（`OrbitSensitivity`・`PanSensitivity`・`DollyScrollSensitivity`・`DollyDragSensitivity`・`MinPivotDistance`）で代替する
  - コンパイルエラーなく `_rotation`・`_zoom` 関連コードが消去された状態になっていること
  - _Requirements: 2.1, 2.2, 2.6_

- [x] 3.2 Setup() での初期 CameraState 算出
  - `Setup(GameObject)` 呼び出し時に `CalculateBounds()` を使って `pivotPoint`・`pivotDistance`・`rotation`・`position` を算出し `_initialState` に格納する
  - `_state` にも同じ値を設定して初期表示状態を確立する
  - `Rotation` プロパティ・`Zoom` プロパティが存在しない形で、`IsInitialized`・`PreviewInstance`・`Setup()`・`Cleanup()`・`Render()`・`HandleInput()`・`Dispose()`・`CalculateBounds()` の public シグネチャが維持されていること
  - _Requirements: 2.3, 2.5_

- [x] 3.3 Render() での CameraState → camera.transform 書き戻し
  - `Render(Rect)` 内で `_state.position` と `_state.rotation` を `PreviewRenderUtility.camera.transform` に設定してからレンダリングする
  - `_previewRenderUtility == null` または `_previewInstance == null` の場合は早期リターンする
  - `Render()` 呼び出し後に `CameraState` が反映されたカメラ映像が `GUI.DrawTexture` で描画された状態になっていること
  - _Requirements: 2.4, 5.6_

- [x] 3.4 HandleInput() の Handler 委譲実装
  - `HandleInput(Rect rect)` を `Event.current` から `PreviewInputFrame` を生成して `HandleInput(Rect, PreviewInputFrame)` に委譲する形で実装する
  - `HandleInput(Rect rect, PreviewInputFrame frame)` オーバーロードを実装し、以下のルーティングを行う：
    - Alt+左ドラッグ（`MouseDrag`, button=0, alt=true）→ `OrbitHandler.Apply` を呼び出して `_state` を更新
    - 中ドラッグ（`MouseDrag`, button=2）→ `PanHandler.Apply` を呼び出して `_state` を更新
    - ScrollWheel → `DollyHandler.Apply` を呼び出して `_state` を更新
    - Alt+右ドラッグ（`MouseDrag`, button=1, alt=true）→ `DollyHandler.Apply` を呼び出して `_state` を更新
  - `rect.Contains(mousePosition)` が false の場合は Handler を呼び出さず `false` を返す
  - `_state` が変化した場合のみ `Event.current.Use()` を呼び出して `true` を返し、変化なし・rect 外の場合は `false` を返す
  - `MonoBehaviour` および InputSystem インスタンスを参照しない実装になっていること
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_
  - _Depends: 2_

- [x] 3.5 ResetCamera() の実装
  - `public void ResetCamera()` メソッドを追加し、`_state = _initialState` で初期 CameraState を復元する
  - `ResetCamera()` 呼び出し後の `_state` が `Setup()` 直後の `_initialState` と等値である不変条件を満たす実装にする
  - `ResetCamera()` が呼ばれた後に `HandleInput()` が `true` を返さず、次の `Render()` で初期視点が描画される動作が可能な状態になっていること
  - _Requirements: 4.2, 4.4_

- [x] 4. ExpressionCreatorWindow へのカメラリセットボタン追加
  - `ExpressionCreatorWindow.cs` の左パネルに `_previewContainer`（`IMGUIContainer`）の直後、既存「全スライダーリセット」ボタンの前にカメラリセットボタン（ラベル: 「カメラリセット」）を追加する
  - `FacialControlStyles.ActionButton` CSS クラスを適用し、既存リセットボタンと視覚的に区別可能なラベルで配置する
  - ボタンクリック時に `_previewWrapper.ResetCamera()` を呼び出し、続けて `_previewContainer.MarkDirtyRepaint()` を実行する `OnCameraReset()` ハンドラを追加する
  - 既存の「全スライダーリセット」ボタン（`OnResetBlendShapes()`）に変更を加えず、カメラリセットボタンと同一ボタンに統合しないこと
  - カメラリセットボタンのクリック後にプレビューが初期視点で再描画される状態になっていること
  - _Requirements: 4.1, 4.3, 4.5, 5.1, 5.2, 5.3, 5.4, 5.5_
  - _Depends: 3.5_

- [x] 5. EditMode テストの実装
- [x] 5.1 Handler 結線テスト（Orbit / Pan / Dolly）
  - `FacialControl/Packages/com.hidano.facialcontrol/Tests/EditMode/Editor/PreviewRenderWrapperTests.cs` に `PreviewRenderWrapperTests` クラスを作成する
  - `new PreviewRenderWrapper()` でデフォルトコンストラクタ経由でインスタンスを生成し、`HandleInput(Rect, PreviewInputFrame)` に各フレームデータを直接渡して Handler 結線を検証する（`Setup()` 呼び出し不要）
  - 以下の 5 テストメソッドを実装する：
    - `HandleInput_AltLeftDrag_OrbitApplied`：Alt+左ドラッグ相当の `PreviewInputFrame` を渡すと `CameraState` が変化すること
    - `HandleInput_MiddleDrag_PanApplied`：中ドラッグ相当の `PreviewInputFrame` を渡すと `CameraState` が変化すること
    - `HandleInput_ScrollWheel_DollyApplied`：ScrollWheel 相当の `PreviewInputFrame` を渡すと `CameraState` が変化すること
    - `HandleInput_AltRightDrag_DollyApplied`：Alt+右ドラッグ相当の `PreviewInputFrame` を渡すと `CameraState` が変化すること
    - `HandleInput_OutsideRect_ReturnsFalse`：rect 外の `mousePosition` を持つ `PreviewInputFrame` を渡すと `false` を返すこと
  - 4 つの Handler 結線テストがすべて Green になっていること
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.6_
  - _Depends: 2, 3.4_

- [x] 5.2 ResetCamera テストおよび rect 内変化テスト
  - `HandleInput_InsideRect_Changed_ReturnsTrue`：Handler が `CameraState` を変化させた場合に `true` を返すことを検証するテストを追加する
  - `ResetCamera_RestoresInitialState`：2 回の `HandleInput` で状態変化させた後 `ResetCamera()` を呼び出すと `CameraState` が元の状態に戻ることを検証するテストを追加する（内部フィールド直接比較ではなく、「状態変化→リセット→再確認」フローで検証）
  - 2 テストがすべて Green になっていること
  - _Requirements: 3.5, 3.6, 6.5, 6.6_
  - _Depends: 5.1_
