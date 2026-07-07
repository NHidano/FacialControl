using System;
using SceneViewStyleCameraController;
using SceneViewStyleCameraController.Handlers;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Common
{
    public class PreviewRenderWrapper : IDisposable
    {
        public const float DefaultFov = 30f;

        /// <summary>
        /// トラッキング対象（顔ジョイント）が指定されたときの FoV。
        /// カメラ位置は全身 bounds ベースのまま動かさず、FoV を下げることで顔のアップを実現する。
        /// </summary>
        public const float FaceTrackFov = 12f;

        public const float DefaultNearClip = 0.01f;
        public const float DefaultFarClip = 100f;
        public const float DefaultLightIntensity = 1.2f;
        public static readonly Quaternion DefaultLightRotation = Quaternion.Euler(30f, -30f, 0f);
        public static readonly Color DefaultBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

        private const float OrbitSensitivity = 0.5f;
        private const float PanSensitivity = 0.002f;
        private const float DollyScrollSensitivity = 0.1f;
        private const float DollyDragSensitivity = 0.02f;
        private const float MinPivotDistance = 0.1f;

        private PreviewRenderUtility _previewRenderUtility;
        private GameObject _previewInstance;
        private bool _disposed;

        private CameraState _state;
        private CameraState _initialState;

        private int _dragButton = -1;
        private bool _dragAlt;

        public bool IsInitialized => _previewRenderUtility != null && _previewInstance != null;

        public GameObject PreviewInstance => _previewInstance;

        /// <summary>
        /// 現在のプレビューカメラ FoV。未初期化時は <see cref="DefaultFov"/> を返す。
        /// </summary>
        public float CameraFieldOfView
            => _previewRenderUtility != null ? _previewRenderUtility.camera.fieldOfView : DefaultFov;

        public void Setup(GameObject sourceObject)
        {
            Setup(sourceObject, null);
        }

        /// <summary>
        /// プレビューをセットアップする。
        /// <paramref name="trackTargetPath"/> にソースルートからの相対 Transform パスを渡すと、
        /// その位置をカメラの注視点にし FoV を <see cref="FaceTrackFov"/> へ下げる（顔アップ用途）。
        /// null / 解決不能パスの場合は従来どおり bounds / Humanoid Head ベースの注視点と
        /// <see cref="DefaultFov"/> を用いる。
        /// </summary>
        public void Setup(GameObject sourceObject, string trackTargetPath)
        {
            Cleanup();

            if (sourceObject == null)
                return;

            _previewRenderUtility = new PreviewRenderUtility();

            _previewRenderUtility.camera.fieldOfView = DefaultFov;
            _previewRenderUtility.camera.nearClipPlane = DefaultNearClip;
            _previewRenderUtility.camera.farClipPlane = DefaultFarClip;
            _previewRenderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            _previewRenderUtility.camera.backgroundColor = DefaultBackgroundColor;

            _previewRenderUtility.lights[0].intensity = DefaultLightIntensity;
            _previewRenderUtility.lights[0].transform.rotation = DefaultLightRotation;

            _previewInstance = UnityEngine.Object.Instantiate(sourceObject);
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;
            _previewInstance.transform.position = Vector3.zero;
            _previewInstance.transform.rotation = Quaternion.identity;

            _previewRenderUtility.AddSingleGO(_previewInstance);

            var bounds = CalculateBounds(_previewInstance);
            var trackTarget = ResolveTrackTarget(_previewInstance.transform, trackTargetPath);
            Vector3 pivotPoint;
            if (trackTarget != null)
            {
                pivotPoint = trackTarget.position;
                _previewRenderUtility.camera.fieldOfView = FaceTrackFov;
            }
            else
            {
                pivotPoint = CalculatePivotPoint(_previewInstance, bounds);
            }

            var pivotDistance = bounds.extents.magnitude * 2f;
            var rotation = Quaternion.Euler(0f, 180f, 0f);
            var position = pivotPoint - rotation * Vector3.forward * pivotDistance;

            _initialState = new CameraState(position, rotation, pivotPoint, pivotDistance);
            _state = _initialState;
        }

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

        public void Render(Rect rect)
        {
            if (_previewRenderUtility == null || _previewInstance == null)
                return;

            _previewRenderUtility.camera.transform.position = _state.position;
            _previewRenderUtility.camera.transform.rotation = _state.rotation;

            _previewRenderUtility.BeginPreview(rect, GUIStyle.none);
            _previewRenderUtility.Render(true, true);
            var texture = _previewRenderUtility.EndPreview();

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        public Texture2D CapturePreviewTexture(int width, int height)
        {
            if (_previewRenderUtility == null || _previewInstance == null)
                return null;

            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");

            _previewRenderUtility.camera.transform.position = _state.position;
            _previewRenderUtility.camera.transform.rotation = _state.rotation;

            var rect = new Rect(0f, 0f, width, height);
            var previousActive = RenderTexture.active;

            _previewRenderUtility.BeginPreview(rect, GUIStyle.none);
            try
            {
                _previewRenderUtility.Render(true, true);
                var renderTexture = _previewRenderUtility.EndPreview() as RenderTexture;
                if (renderTexture == null)
                    return null;

                RenderTexture.active = renderTexture;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(rect, 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        public bool HandleInput(Rect rect)
        {
            var evt = Event.current;
            var scrollDelta = evt.type == EventType.ScrollWheel ? evt.delta : Vector2.zero;
            var frame = new PreviewInputFrame(
                evt.type,
                evt.button,
                evt.mousePosition,
                evt.delta,
                scrollDelta,
                evt.alt);

            var changed = HandleInput(rect, frame);
            if (changed)
                evt.Use();
            return changed;
        }

        public bool HandleInput(Rect rect, PreviewInputFrame frame)
        {
            if (frame.EventType == EventType.MouseDown)
            {
                if (rect.Contains(frame.MousePosition))
                {
                    _dragButton = frame.Button;
                    _dragAlt = frame.Alt;
                }
                return false;
            }

            if (frame.EventType == EventType.MouseUp)
            {
                if (_dragButton == frame.Button)
                    _dragButton = -1;
                return false;
            }

            bool isCapturedDrag = frame.EventType == EventType.MouseDrag && _dragButton >= 0;
            int button = isCapturedDrag ? _dragButton : frame.Button;
            bool alt = isCapturedDrag ? _dragAlt : frame.Alt;

            if (!isCapturedDrag && !rect.Contains(frame.MousePosition))
                return false;

            var previous = _state;

            var verticalFlippedDelta = new Vector2(frame.Delta.x, -frame.Delta.y);

            switch (frame.EventType)
            {
                case EventType.MouseDrag when button == 0 && alt:
                    _state = OrbitHandler.Apply(_state, verticalFlippedDelta, OrbitSensitivity, MinPivotDistance);
                    break;
                case EventType.MouseDrag when button == 2:
                    _state = PanHandler.Apply(_state, verticalFlippedDelta, PanSensitivity, MinPivotDistance);
                    break;
                case EventType.ScrollWheel:
                    // Unity の ScrollWheel delta.y は「奥にホイール (≒ ズームイン期待)」で負、
                    // 「手前にホイール (≒ ズームアウト期待)」で正になる。DollyHandler は
                    // 正の dollyAmount で「カメラを前方に進める = ズームイン」になるため、
                    // 直感どおりの方向にするには符号を反転して渡す必要がある。
                    _state = DollyHandler.Apply(_state, -frame.ScrollDelta.y, DollyScrollSensitivity, MinPivotDistance);
                    break;
                case EventType.MouseDrag when button == 1 && alt:
                    _state = DollyHandler.Apply(_state, -frame.Delta.y, DollyDragSensitivity, MinPivotDistance);
                    break;
                default:
                    return false;
            }

            return _state.position != previous.position
                || _state.rotation != previous.rotation
                || _state.pivotPoint != previous.pivotPoint
                || _state.pivotDistance != previous.pivotDistance;
        }

        public void ResetCamera()
        {
            _state = _initialState;
        }

        /// <summary>
        /// トラッキング対象パスをプレビューインスタンス内の Transform に解決する。
        /// 空文字はルート自身、null / 不一致は null を返す。
        /// </summary>
        private static Transform ResolveTrackTarget(Transform instanceRoot, string trackTargetPath)
        {
            if (trackTargetPath == null)
                return null;

            if (trackTargetPath.Length == 0)
                return instanceRoot;

            return instanceRoot.Find(trackTargetPath);
        }

        public static Vector3 CalculatePivotPoint(GameObject go, Bounds fallbackBounds)
        {
            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                if (headBone != null)
                {
                    var headHeight = headBone.position.y;
                    return new Vector3(fallbackBounds.center.x, headHeight, fallbackBounds.center.z);
                }
            }

            return fallbackBounds.center;
        }

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
