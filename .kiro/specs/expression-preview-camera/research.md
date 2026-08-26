# Research Notes: expression-preview-camera

## テスト設計トレードオフ

### 課題

`PreviewRenderWrapper.Setup()` は `UnityEngine.Object.Instantiate()` を内部で呼ぶため、EditMode テストから直接呼び出すと `GameObject` の生成が必要になる。テスト対象の `HandleInput(Rect, PreviewInputFrame)` は `IsInitialized` チェックを持つため、`Setup()` なしでは Handler が呼ばれない。

### 検討した選択肢

**案 A: `internal` 初期化コンストラクタ**

`PreviewRenderWrapper` に `internal` コンストラクタを追加し、`CameraState` を直接注入する。テスト asmdef と本体 asmdef に `InternalsVisibleTo` を設定する。

- メリット: プロダクションコードの public API を汚染しない
- デメリット: `InternalsVisibleTo` の asmdef 管理が複雑になる

**案 B: IsInitialized チェックの分割**

`HandleInput(Rect, PreviewInputFrame)` の `IsInitialized` チェックを `_previewRenderUtility != null` のみに限定し、`_state` はデフォルト値（`new CameraState()`）でも Handler を呼び出せるようにする。テストは `new PreviewRenderWrapper()` のデフォルトコンストラクタで直接インスタンス生成して検証する。

- メリット: シンプル。`InternalsVisibleTo` 不要
- デメリット: `_previewInstance == null` 状態で Handler が動作する（Preview が表示されない状態で Handler が呼ばれる可能性）。ただし `Render()` は `_previewInstance == null` で早期リターンするため副作用なし

**案 C: テスト専用ファクトリメソッド（`CreateForTest`）**

`public static PreviewRenderWrapper CreateForTest(CameraState initialState)` を設け、プロダクション用コンストラクタとは別パスで初期化する。

- メリット: 意図が明確
- デメリット: テスト専用コードがプロダクションに混入する

### 採用方針

**案 B を採用**。

`HandleInput(Rect, PreviewInputFrame)` のガードを `_previewRenderUtility != null` チェックのみとし、`_previewInstance == null` であっても Handler 呼び出しと `_state` 更新は行う。`Render()` 側は従来通り `_previewRenderUtility == null || _previewInstance == null` で早期リターンするため、未初期化 Render の副作用は発生しない。

テストは `new PreviewRenderWrapper()` → `HandleInput(rect, frame)` → `CameraState` の変化を検証、という最小限のパスで Handler 結線を確認できる。ただし初期 `CameraState` は `new CameraState()` のデフォルト値（全フィールド 0 / Quaternion.identity）になるため、`ResetCamera` テストは以下のフローで行う：

1. `new PreviewRenderWrapper()` を生成
2. `HandleInput(rect, orbitFrame)` で `_state` を変化させる
3. `ResetCamera()` を呼び出す
4. `_state` が再び初期値（デフォルト `CameraState`）に戻ることを確認

---

## パッケージ API 確認

### `com.hidano.scene-view-style-camera-controller@1.0.0`

調査元: `/dig` セッション（事前調査済み）

**利用 API**

| クラス / 構造体 | メソッド / フィールド | シグネチャ |
|--------------|-------------------|----------|
| `CameraState` (struct) | フィールド | `Vector3 position`, `Quaternion rotation`, `Vector3 pivotPoint`, `float pivotDistance` |
| `Handlers.OrbitHandler` | `Apply` (static) | `CameraState Apply(CameraState state, Vector2 delta, float sensitivity, float minPivotDistance)` |
| `Handlers.PanHandler` | `Apply` (static) | `CameraState Apply(CameraState state, Vector2 delta, float sensitivity, float minPivotDistance)` |
| `Handlers.DollyHandler` | `Apply` (static) | `CameraState Apply(CameraState state, float delta, float sensitivity, float minPivotDistance)` |

**感度パラメータ（ハードコード値）**

| Handler | パラメータ名 | 値 |
|--------|------------|---|
| OrbitHandler | sensitivity | 0.3f |
| PanHandler | sensitivity | 0.001f |
| DollyHandler (Scroll) | sensitivity | 1.0f |
| DollyHandler (Drag) | sensitivity | 0.05f |
| 全 Handler | minPivotDistance | 0.1f |

**asmdef 情報**

- Runtime-only（Editor asmdef なし）
- `autoReferenced: true`
- 参照: `Unity.InputSystem` のみ

**InputSystem バージョン互換性**

- パッケージ要求: `Unity.InputSystem`（バージョン制約は `1.0.0` 以上の想定）
- 既存宣言: `com.unity.inputsystem@1.17.0`
- 競合: なし

---

## `Rotation` / `Zoom` プロパティ外部参照調査

`grep` による調査結果（`ExpressionCreatorWindow.cs` + 他 Editor ファイル）：

- `_previewWrapper.Rotation` の参照: 0 件
- `_previewWrapper.Zoom` の参照: 0 件
- `RotationSensitivity` の外部参照: 0 件（`PreviewRenderWrapper.cs` 内定義のみ）
- `ZoomSensitivity` の外部参照: 0 件（同上）

結論: `Rotation`・`Zoom` プロパティおよび関連定数の削除はリグレッションリスクなし。

---

## IMGUI イベントと Handler のマッピング根拠

Scene ビューの標準操作規約に基づくマッピング：

| 操作 | IMGUI 条件 | Handler |
|-----|-----------|---------|
| Orbit | `MouseDrag`, `button==0`, `alt==true` | `OrbitHandler` |
| Pan | `MouseDrag`, `button==2` | `PanHandler` |
| Dolly (Scroll) | `ScrollWheel` | `DollyHandler`（`evt.delta.y` を渡す） |
| Dolly (Drag) | `MouseDrag`, `button==1`, `alt==true` | `DollyHandler`（`evt.delta.magnitude` or `evt.delta.y` を渡す） |

`EventType.MouseDown` / `EventType.MouseUp` は Handler 呼び出しに不要。従来の `_isDragging` フラグは `DollyHandler` / `OrbitHandler` が内部で処理するため不要。

Alt+右ドラッグの Dolly では `delta` の Y 成分（縦方向ドラッグ）を `float delta` として渡す。スクロールと同じ `DollyHandler` を使うため統一性がある。
