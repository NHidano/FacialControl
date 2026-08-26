using System;
using System.Collections.Generic;
using Hidano.FacialControl.Editor.Sampling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Sampling
{
    /// <summary>
    /// <see cref="GazeClipBlendShapeSampler"/> の EditMode 契約テスト。
    /// AnimationClip 内の BlendShape curve を time=0 でサンプルし、
    /// (BlendShape 名, weight) 列が抽出されることを検証する。
    /// </summary>
    [TestFixture]
    public class GazeClipBlendShapeSamplerTests
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

        private static void SetBlendShapeConstantCurve(AnimationClip clip, string path, string blendShapeName, float weight)
        {
            var binding = new EditorCurveBinding
            {
                path = path,
                type = typeof(SkinnedMeshRenderer),
                propertyName = "blendShape." + blendShapeName,
            };
            var curve = AnimationCurve.Constant(0f, 1f, weight);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        [Test]
        public void Sample_NullClip_ReturnsEmpty()
        {
            var result = GazeClipBlendShapeSampler.Sample(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Sample_EmptyClip_ReturnsEmpty()
        {
            var clip = CreateTrackedClip();
            var result = GazeClipBlendShapeSampler.Sample(clip);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Sample_BlendShapeCurves_ReturnsValuesAtTimeZero()
        {
            var clip = CreateTrackedClip();
            SetBlendShapeConstantCurve(clip, "Body/Face", "EyeLookOutRight", 100f);
            SetBlendShapeConstantCurve(clip, "Body/Face", "EyeLookInLeft", 80f);

            var result = GazeClipBlendShapeSampler.Sample(clip);

            Assert.AreEqual(2, result.Count);

            var byName = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < result.Count; i++)
            {
                byName[result[i].BlendShapeName] = result[i].Weight;
            }
            Assert.IsTrue(byName.ContainsKey("EyeLookOutRight"));
            Assert.IsTrue(byName.ContainsKey("EyeLookInLeft"));
            Assert.AreEqual(100f, byName["EyeLookOutRight"], 1e-5f);
            Assert.AreEqual(80f, byName["EyeLookInLeft"], 1e-5f);
        }

        [Test]
        public void Sample_NonBlendShapeCurves_AreIgnored()
        {
            var clip = CreateTrackedClip();
            SetBlendShapeConstantCurve(clip, "Body/Face", "EyeLookOutRight", 100f);

            // Transform curve (non-blendShape) を追加 → 結果に含まれないこと。
            var transformBinding = new EditorCurveBinding
            {
                path = "Armature/Head",
                type = typeof(Transform),
                propertyName = "m_LocalPosition.x",
            };
            AnimationUtility.SetEditorCurve(clip, transformBinding, AnimationCurve.Constant(0f, 1f, 0.5f));

            var result = GazeClipBlendShapeSampler.Sample(clip);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("EyeLookOutRight", result[0].BlendShapeName);
        }

        [Test]
        public void Sample_DuplicateBlendShapeNamesAcrossRenderers_KeepsFirst()
        {
            var clip = CreateTrackedClip();
            SetBlendShapeConstantCurve(clip, "Body/Face", "EyeBlink", 50f);
            SetBlendShapeConstantCurve(clip, "Body/Head", "EyeBlink", 90f);

            var result = GazeClipBlendShapeSampler.Sample(clip);

            // 同名 BS は最初に出会った 1 件のみ採用 (renderer path は無視する仕様)。
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("EyeBlink", result[0].BlendShapeName);
        }
    }
}
