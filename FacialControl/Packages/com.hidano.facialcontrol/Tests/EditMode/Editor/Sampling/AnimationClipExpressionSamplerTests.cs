using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Sampling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Sampling
{
    /// <summary>
    /// <see cref="AnimationClipExpressionSampler"/> のテスト。
    /// AnimationUtility 経由で時刻 0 の BlendShape / Transform 値を取得し、
    /// 不明 binding を warning + skip することを検証する。
    /// </summary>
    [TestFixture]
    public class AnimationClipExpressionSamplerTests
    {
        private readonly List<UnityEngine.Object> _trackedObjects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _trackedObjects.Count; i++)
            {
                if (_trackedObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_trackedObjects[i]);
                }
            }
            _trackedObjects.Clear();
        }

        private AnimationClip CreateTrackedClip()
        {
            var clip = new AnimationClip();
            _trackedObjects.Add(clip);
            return clip;
        }

        [Test]
        public void SampleSnapshot_BlendShapeCurves_ReturnsValuesAtTimeZero()
        {
            var clip = CreateTrackedClip();
            // blendShape カーブは Unity 標準 0..100 スケール。snapshot へは正規化 0..1 で格納される。
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Smile", 50f);
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Anger", 25f);
            SetFloatCurve(clip, "Body/Head", typeof(SkinnedMeshRenderer), "blendShape.Surprise", 100f);

            var sampler = new AnimationClipExpressionSampler();
            var snapshot = sampler.SampleSnapshot("expr-001", clip);

            Assert.AreEqual("expr-001", snapshot.Id);
            Assert.AreEqual(3, snapshot.BlendShapes.Length);

            var byKey = new Dictionary<string, BlendShapeSnapshot>();
            for (int i = 0; i < snapshot.BlendShapes.Length; i++)
            {
                var bs = snapshot.BlendShapes.Span[i];
                byKey[$"{bs.RendererPath}|{bs.Name}"] = bs;
            }
            Assert.AreEqual(0.5f, byKey["Body/Face|Smile"].Value, 1e-5f);
            Assert.AreEqual(0.25f, byKey["Body/Face|Anger"].Value, 1e-5f);
            Assert.AreEqual(1.0f, byKey["Body/Head|Surprise"].Value, 1e-5f);

            // Renderer paths は重複排除されている
            Assert.AreEqual(2, snapshot.RendererPaths.Length);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < snapshot.RendererPaths.Length; i++)
            {
                paths.Add(snapshot.RendererPaths.Span[i]);
            }
            Assert.IsTrue(paths.Contains("Body/Face"));
            Assert.IsTrue(paths.Contains("Body/Head"));

            // metadata 抽出を行わないため fallback default
            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
        }

        [Test]
        public void SampleSnapshot_BlendShapePartialWeights_AreNormalizedNotSaturated()
        {
            // 回帰: 手書き / インポートした標準 Unity clip は blendShape を 0..100 で記録する。
            // キーフレーム 30 / 40 が runtime で ×100 され全て 100 に飽和していた不具合の防止。
            // snapshot は正規化 0..1 (0.3 / 0.4) を返し、ドメイン → runtime ×100 で 30 / 40 に戻る。
            var clip = CreateTrackedClip();
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.MouthOpen", 30f);
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.BrowDown", 40f);

            var sampler = new AnimationClipExpressionSampler();
            var snapshot = sampler.SampleSnapshot("expr-partial", clip);

            var byKey = new Dictionary<string, float>();
            for (int i = 0; i < snapshot.BlendShapes.Length; i++)
            {
                var bs = snapshot.BlendShapes.Span[i];
                byKey[bs.Name] = bs.Value;
            }

            Assert.AreEqual(0.3f, byKey["MouthOpen"], 1e-5f);
            Assert.AreEqual(0.4f, byKey["BrowDown"], 1e-5f);
        }

        [Test]
        public void SampleSnapshot_TransformCurves_ReturnsBoneSnapshot()
        {
            var clip = CreateTrackedClip();
            // Position
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalPosition.x", 0.1f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalPosition.y", 0.2f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalPosition.z", 0.3f);
            // Euler (raw 形式)
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "localEulerAnglesRaw.x", 10f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "localEulerAnglesRaw.y", 20f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "localEulerAnglesRaw.z", 30f);
            // Scale
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalScale.x", 1.1f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalScale.y", 1.2f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalScale.z", 1.3f);

            var sampler = new AnimationClipExpressionSampler();
            var snapshot = sampler.SampleSnapshot("expr-002", clip);

            Assert.AreEqual(1, snapshot.Bones.Length);
            var bone = snapshot.Bones.Span[0];
            Assert.AreEqual("Armature/Head", bone.BonePath);
            Assert.AreEqual(0.1f, bone.PositionX, 1e-5f);
            Assert.AreEqual(0.2f, bone.PositionY, 1e-5f);
            Assert.AreEqual(0.3f, bone.PositionZ, 1e-5f);
            Assert.AreEqual(10f, bone.EulerX, 1e-5f);
            Assert.AreEqual(20f, bone.EulerY, 1e-5f);
            Assert.AreEqual(30f, bone.EulerZ, 1e-5f);
            Assert.AreEqual(1.1f, bone.ScaleX, 1e-5f);
            Assert.AreEqual(1.2f, bone.ScaleY, 1e-5f);
            Assert.AreEqual(1.3f, bone.ScaleZ, 1e-5f);
        }

        [Test]
        public void SampleSnapshot_UnsupportedBinding_LogsWarningAndSkips()
        {
            var clip = CreateTrackedClip();
            // 既知の正常 binding（BlendShape）
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Smile", 0.5f);
            // 未知の binding（Renderer.m_Color.r 等は本サンプラのスコープ外）
            SetFloatCurve(clip, "Body/Face", typeof(Renderer), "material._Color.r", 0.7f);

            var sampler = new AnimationClipExpressionSampler();

            LogAssert.Expect(LogType.Warning, new Regex("Unsupported binding"));
            var snapshot = sampler.SampleSnapshot("expr-003", clip);

            // 未対応 binding は skip され、BlendShape のみ採用される
            Assert.AreEqual(1, snapshot.BlendShapes.Length);
            Assert.AreEqual("Smile", snapshot.BlendShapes.Span[0].Name);
            Assert.AreEqual(0, snapshot.Bones.Length);
        }

        [Test]
        public void SampleSummary_ReturnsRendererPathsAndBlendShapeNames()
        {
            var clip = CreateTrackedClip();
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Smile", 0.5f);
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Anger", 0.25f);
            SetFloatCurve(clip, "Body/Head", typeof(SkinnedMeshRenderer), "blendShape.Surprise", 1.0f);

            var sampler = new AnimationClipExpressionSampler();
            var summary = sampler.SampleSummary(clip);

            Assert.IsNotNull(summary.RendererPaths);
            Assert.IsNotNull(summary.BlendShapeNames);

            var rendererSet = new HashSet<string>(summary.RendererPaths);
            Assert.AreEqual(2, rendererSet.Count);
            Assert.IsTrue(rendererSet.Contains("Body/Face"));
            Assert.IsTrue(rendererSet.Contains("Body/Head"));

            var blendShapeSet = new HashSet<string>(summary.BlendShapeNames);
            Assert.AreEqual(3, blendShapeSet.Count);
            Assert.IsTrue(blendShapeSet.Contains("Smile"));
            Assert.IsTrue(blendShapeSet.Contains("Anger"));
            Assert.IsTrue(blendShapeSet.Contains("Surprise"));

            Assert.AreEqual(Expression.DefaultTransitionDuration, summary.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, summary.TransitionCurve);
        }

        [Test]
        public void SampleSnapshot_NullClip_Throws()
        {
            var sampler = new AnimationClipExpressionSampler();

            Assert.Throws<ArgumentNullException>(() =>
                sampler.SampleSnapshot("expr-null", null));
        }

        private static void SetFloatCurve(AnimationClip clip, string path, Type type, string propertyName, float value)
        {
            var binding = new EditorCurveBinding
            {
                path = path,
                type = type,
                propertyName = propertyName,
            };
            var curve = AnimationCurve.Constant(0f, 1f, value);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
    }
}
