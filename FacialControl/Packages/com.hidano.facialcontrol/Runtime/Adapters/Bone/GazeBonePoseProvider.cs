using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// <see cref="GazeBindingConfig"/> を毎フレーム評価し、左右目ボーンに直接 localRotation を書込む
    /// 目線ボーン専用 provider。アナログ入力 (Vector2) を yaw / pitch 角度に変換し、
    /// 設定された外側/内側/上下の角度制限と各ボーンの参照モデル時取得 local 軸を用いて
    /// <c>Quaternion.AngleAxis</c> 合成で姿勢を計算する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// AnalogBonePoseProvider と異なり <see cref="IBonePoseProvider"/> 経由で snapshot を流す
    /// のではなく、bone の <see cref="Transform.localRotation"/> を直接書換える。これは目線回転が
    /// Euler 加算では正しく表現できず、各ボーン固有の rest pose と parent frame 軸を用いた
    /// quaternion 合成が必要なためである。
    /// </para>
    /// <para>
    /// 適用順は <c>localRotation = AngleAxis(yaw, yawAxisLocal) * AngleAxis(pitch, pitchAxisLocal) * Euler(rest)</c>。
    /// yaw を最も外側 (最後に適用) にすることで、視線左右が常に「水平」に動くように見える。
    /// </para>
    /// <para>
    /// 左右非対称制限: input.x が外側方向のとき outerYawAngle、内側方向のとき innerYawAngle で線形駆動する。
    /// 「外側」はキャラの鼻から見て当該の眼が遠ざかる側、すなわち左目では input.x &lt; 0、右目では input.x &gt; 0 の方向。
    /// </para>
    /// <para>
    /// <see cref="Dispose"/> 時には書込み開始前のオリジナル localRotation に各ボーンを復元する。
    /// </para>
    /// <para>
    /// 入力源 (<see cref="IAnalogInputSource"/>) と <see cref="GazeBindingConfig"/> のペアは
    /// <see cref="GazeBoneBinding"/> として呼出側で解決済みの状態で渡す。これにより本クラスは
    /// Unity InputSystem・OSC・ARKit などの具体的な入力方式に依存しない。
    /// </para>
    /// </remarks>
    public sealed class GazeBonePoseProvider : IDisposable
    {
        private readonly BoneTransformResolver _resolver;
        private readonly EyeBinding[] _bindings;
        private bool _disposed;

        /// <summary>
        /// <see cref="GazeBonePoseProvider"/> を構築する。
        /// </summary>
        /// <param name="resolver">ボーン名から Transform を解決するリゾルバー (FacialController と同じものを共有)。</param>
        /// <param name="bindings"><see cref="GazeBindingConfig"/> と入力源のペア配列。</param>
        public GazeBonePoseProvider(
            BoneTransformResolver resolver,
            IReadOnlyList<GazeBoneBinding> bindings)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));

            var list = new List<EyeBinding>(bindings.Count * 2);
            for (int i = 0; i < bindings.Count; i++)
            {
                var cfg = bindings[i].Config;
                var leftSource = bindings[i].LeftSource ?? bindings[i].Source;
                var rightSource = bindings[i].RightSource ?? bindings[i].Source;
                if (cfg == null || (leftSource == null && rightSource == null)) continue;

                if (!string.IsNullOrWhiteSpace(cfg.leftEyeBonePath) && leftSource != null)
                {
                    list.Add(new EyeBinding(
                        leftSource,
                        cfg.leftEyeBonePath,
                        Quaternion.Euler(cfg.leftEyeInitialRotation),
                        SafeNormalize(cfg.leftEyeYawAxisLocal, Vector3.up),
                        SafeNormalize(cfg.leftEyePitchAxisLocal, Vector3.right),
                        isLeftEye: true,
                        cfg.outerYawAngle,
                        cfg.innerYawAngle,
                        cfg.lookUpAngle,
                        cfg.lookDownAngle));
                }
                if (!string.IsNullOrWhiteSpace(cfg.rightEyeBonePath) && rightSource != null)
                {
                    list.Add(new EyeBinding(
                        rightSource,
                        cfg.rightEyeBonePath,
                        Quaternion.Euler(cfg.rightEyeInitialRotation),
                        SafeNormalize(cfg.rightEyeYawAxisLocal, Vector3.up),
                        SafeNormalize(cfg.rightEyePitchAxisLocal, Vector3.right),
                        isLeftEye: false,
                        cfg.outerYawAngle,
                        cfg.innerYawAngle,
                        cfg.lookUpAngle,
                        cfg.lookDownAngle));
                }
            }

            _bindings = list.Count == 0 ? Array.Empty<EyeBinding>() : list.ToArray();
        }

        /// <summary>
        /// per-frame に呼出され、各 GazeBindingConfig の入力を読んで両目の <see cref="Transform.localRotation"/> を計算/書込みする。
        /// </summary>
        public void Apply()
        {
            if (_disposed)
            {
                return;
            }

            for (int i = 0; i < _bindings.Length; i++)
            {
                ref var b = ref _bindings[i];
                var target = ResolveTarget(ref b);
                if (target == null)
                {
                    continue;
                }

                if (!b.HasInitialSnapshot)
                {
                    b.InitialLocalRotation = target.localRotation;
                    b.HasInitialSnapshot = true;
                }

                if (!GazeInputReader.TryReadXY(b.Source, out float ix, out float iy))
                {
                    target.localRotation = b.RestRotation;
                    continue;
                }

                float yawDeg;
                if (b.IsLeftEye)
                {
                    // 左目: 後段の `yawDeg = -yawDeg` 反転と Unity 既定 (+Y/+X) 軸配置の組合せを踏まえ、
                    // input.x > 0 のとき左目は外側、input.x < 0 のとき内側方向に振れる。
                    yawDeg = ix >= 0f
                        ? ix * b.OuterYawAngle
                        : ix * b.InnerYawAngle;
                }
                else
                {
                    // 右目: 同様に input.x > 0 のとき内側、input.x < 0 のとき外側に振れる。
                    yawDeg = ix >= 0f
                        ? ix * b.InnerYawAngle
                        : ix * b.OuterYawAngle;
                }

                float pitchDeg = iy >= 0f
                    ? iy * b.LookUpAngle
                    : iy * b.LookDownAngle;

                // Unity の Quaternion.AngleAxis は左手系で「軸方向を見て時計回り = 正」。
                // 参照モデルから自動取得した yawAxisLocal (+Y) / pitchAxisLocal (+X) と組み合わせると、
                // input.x > 0 で視線が左、input.y > 0 で視線が下に振れる (上下左右とも反転)。
                // InputActionAsset 側 Invert で毎回吸収するのは手間なのでコード側で符号反転する。
                yawDeg = -yawDeg;
                pitchDeg = -pitchDeg;

                var yawRot = Quaternion.AngleAxis(yawDeg, b.YawAxisLocal);
                var pitchRot = Quaternion.AngleAxis(pitchDeg, b.PitchAxisLocal);

                target.localRotation = yawRot * pitchRot * b.RestRotation;
            }
        }

        /// <summary>
        /// 書込中だった bone の <see cref="Transform.localRotation"/> を最初の書込み直前の値に戻す。
        /// </summary>
        public void RestoreInitialRotations()
        {
            for (int i = 0; i < _bindings.Length; i++)
            {
                ref var b = ref _bindings[i];
                if (!b.HasInitialSnapshot)
                {
                    continue;
                }
                var target = ResolveTarget(ref b);
                if (target == null)
                {
                    continue;
                }
                target.localRotation = b.InitialLocalRotation;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            RestoreInitialRotations();
            _disposed = true;
        }

        private Transform ResolveTarget(ref EyeBinding b)
        {
            if (b.CachedTarget != null)
            {
                return b.CachedTarget;
            }
            b.CachedTarget = _resolver.Resolve(b.BonePath);
            return b.CachedTarget;
        }

        private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            if (v.sqrMagnitude < 1e-8f)
            {
                return fallback;
            }
            return v.normalized;
        }

        private struct EyeBinding
        {
            public readonly IAnalogInputSource Source;
            public readonly string BonePath;
            public readonly Quaternion RestRotation;
            public readonly Vector3 YawAxisLocal;
            public readonly Vector3 PitchAxisLocal;
            public readonly bool IsLeftEye;
            public readonly float OuterYawAngle;
            public readonly float InnerYawAngle;
            public readonly float LookUpAngle;
            public readonly float LookDownAngle;

            public Transform CachedTarget;
            public Quaternion InitialLocalRotation;
            public bool HasInitialSnapshot;

            public EyeBinding(
                IAnalogInputSource source,
                string bonePath,
                Quaternion restRotation,
                Vector3 yawAxisLocal,
                Vector3 pitchAxisLocal,
                bool isLeftEye,
                float outerYawAngle,
                float innerYawAngle,
                float lookUpAngle,
                float lookDownAngle)
            {
                Source = source;
                BonePath = bonePath;
                RestRotation = restRotation;
                YawAxisLocal = yawAxisLocal;
                PitchAxisLocal = pitchAxisLocal;
                IsLeftEye = isLeftEye;
                OuterYawAngle = Mathf.Max(0f, outerYawAngle);
                InnerYawAngle = Mathf.Max(0f, innerYawAngle);
                LookUpAngle = Mathf.Max(0f, lookUpAngle);
                LookDownAngle = Mathf.Max(0f, lookDownAngle);

                CachedTarget = null;
                InitialLocalRotation = Quaternion.identity;
                HasInitialSnapshot = false;
            }
        }
    }
}
