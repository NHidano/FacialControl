using System;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Common
{
    /// <summary>
    /// PreviewRenderUtility のラッパー。
    /// Editor ウィンドウ向けにカメラ・ライティング・RenderTexture の管理を提供する。
    /// IMGUIContainer 内での描画とマウス操作（回転・ズーム）をサポートする。
    /// </summary>
    public class PreviewRenderWrapper : IDisposable
    {
        /// <summary>デフォルトのカメラ FOV</summary>
        public const float DefaultFov = 30f;

        /// <summary>デフォルトのニアクリップ</summary>
        public const float DefaultNearClip = 0.01f;

        /// <summary>デフォルトのファークリップ</summary>
        public const float DefaultFarClip = 100f;

        /// <summary>デフォルトのライト強度</summary>
        public const float DefaultLightIntensity = 1.2f;

        /// <summary>デフォルトのライト回転</summary>
        public static readonly Quaternion DefaultLightRotation = Quaternion.Euler(30f, -30f, 0f);

        /// <summary>デフォルトの背景色</summary>
        public static readonly Color DefaultBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

        /// <summary>マウスドラッグの回転感度（度/ピクセル）</summary>
        public const float RotationSensitivity = 0.5f;

        /// <summary>スクロールのズーム感度</summary>
        public const float ZoomSensitivity = 0.05f;

        /// <summary>ズーム最小値</summary>
        public const float ZoomMin = 0.5f;

        /// <summary>ズーム最大値</summary>
        public const float ZoomMax = 5f;

        /// <summary>垂直回転の最大角度（度）</summary>
        public const float PitchLimit = 89f;

        private PreviewRenderUtility _previewRenderUtility;
        private GameObject _previewInstance;
        private bool _disposed;

        // カメラ操作状態
        private Vector2 _rotation;
        private float _zoom = 1.5f;
        private Vector2 _lastMousePos;
        private bool _isDragging;

        /// <summary>
        /// PreviewRenderUtility が初期化済みかどうかを取得する
        /// </summary>
        public bool IsInitialized => _previewRenderUtility != null && _previewInstance != null;

        /// <summary>
        /// プレビュー内の GameObject インスタンスを取得する
        /// </summary>
        public GameObject PreviewInstance => _previewInstance;

        /// <summary>
        /// カメラの水平・垂直回転角度（度）を取得・設定する。
        /// X が水平（ヨー）、Y が垂直（ピッチ）。
        /// </summary>
        public Vector2 Rotation
        {
            get => _rotation;
            set => _rotation = new Vector2(value.x, Mathf.Clamp(value.y, -PitchLimit, PitchLimit));
        }

        /// <summary>
        /// ズーム倍率を取得・設定する
        /// </summary>
        public float Zoom
        {
            get => _zoom;
            set => _zoom = Mathf.Clamp(value, ZoomMin, ZoomMax);
        }

        /// <summary>
        /// 対象 GameObject でプレビューを初期化する。
        /// 既存のプレビューがある場合は先にクリーンアップされる。
        /// </summary>
        /// <param name="sourceObject">プレビュー対象の GameObject</param>
        public void Setup(GameObject sourceObject)
        {
            Cleanup();

            if (sourceObject == null)
                return;

            _previewRenderUtility = new PreviewRenderUtility();

            // カメラ設定
            _previewRenderUtility.camera.fieldOfView = DefaultFov;
            _previewRenderUtility.camera.nearClipPlane = DefaultNearClip;
            _previewRenderUtility.camera.farClipPlane = DefaultFarClip;
            _previewRenderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            _previewRenderUtility.camera.backgroundColor = DefaultBackgroundColor;

            // ライティング設定
            _previewRenderUtility.lights[0].intensity = DefaultLightIntensity;
            _previewRenderUtility.lights[0].transform.rotation = DefaultLightRotation;

            // プレビューインスタンス生成
            _previewInstance = UnityEngine.Object.Instantiate(sourceObject);
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;
            _previewInstance.transform.position = Vector3.zero;
            _previewInstance.transform.rotation = Quaternion.identity;

            _previewRenderUtility.AddSingleGO(_previewInstance);
        }

        /// <summary>
        /// プレビューリソースを解放する
        /// </summary>
        public void Cleanup()
        {
            if (_previewInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }

            if (_previewRenderUtility != null)
            {
                _previewRenderUtility.Cleanup();
                _previewRenderUtility = null;
            }
        }

        /// <summary>
        /// IMGUI 内でプレビューを描画する。
        /// IMGUIContainer のコールバックから呼び出す。
        /// </summary>
        /// <param name="rect">描画先の矩形</param>
        public void Render(Rect rect)
        {
            if (_previewRenderUtility == null || _previewInstance == null)
                return;

            // カメラ位置の計算
            var bounds = CalculateBounds(_previewInstance);
            float distance = bounds.extents.magnitude * _zoom;
            var center = bounds.center;

            var rotation = Quaternion.Euler(_rotation.y, _rotation.x, 0f);
            var camPos = center + rotation * new Vector3(0f, 0f, -distance);

            _previewRenderUtility.camera.transform.position = camPos;
            _previewRenderUtility.camera.transform.LookAt(center);

            _previewRenderUtility.BeginPreview(rect, GUIStyle.none);
            _previewRenderUtility.Render(true, true);
            var texture = _previewRenderUtility.EndPreview();

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        /// <summary>
        /// マウス入力を処理して回転・ズームを更新する。
        /// IMGUIContainer のコールバック内で Render の前に呼び出す。
        /// </summary>
        /// <param name="rect">入力受付の矩形</param>
        /// <returns>入力によって状態が変化した場合 true</returns>
        public bool HandleInput(Rect rect)
        {
            var evt = Event.current;

            if (!rect.Contains(evt.mousePosition))
                return false;

            bool changed = false;

            switch (evt.type)
            {
                case EventType.MouseDown when evt.button == 0:
                    _isDragging = true;
                    _lastMousePos = evt.mousePosition;
                    evt.Use();
                    break;

                case EventType.MouseUp when evt.button == 0:
                    _isDragging = false;
                    evt.Use();
                    break;

                case EventType.MouseDrag when _isDragging:
                    var delta = evt.mousePosition - _lastMousePos;
                    _rotation.x += delta.x * RotationSensitivity;
                    _rotation.y -= delta.y * RotationSensitivity;
                    _rotation.y = Mathf.Clamp(_rotation.y, -PitchLimit, PitchLimit);
                    _lastMousePos = evt.mousePosition;
                    evt.Use();
                    changed = true;
                    break;

                case EventType.ScrollWheel:
                    _zoom += evt.delta.y * ZoomSensitivity;
                    _zoom = Mathf.Clamp(_zoom, ZoomMin, ZoomMax);
                    evt.Use();
                    changed = true;
                    break;
            }

            return changed;
        }

        /// <summary>
        /// GameObject の全 Renderer を含むバウンディングボックスを計算する
        /// </summary>
        public static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        /// <summary>
        /// IDisposable 実装。Cleanup を呼び出す。
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                Cleanup();
                _disposed = true;
            }
        }
    }
}
