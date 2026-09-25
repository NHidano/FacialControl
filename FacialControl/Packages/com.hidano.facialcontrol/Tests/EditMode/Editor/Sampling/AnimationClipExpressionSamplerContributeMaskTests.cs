using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Editor.Sampling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Sampling
{
    [TestFixture]
    public class AnimationClipExpressionSamplerContributeMaskTests
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

        [Test]
        public void TryResolveContributeIndices_MultipleBlendShapeCurves_SetsAllMatchingIndices()
        {
            var clip = CreateTrackedClip();
            SetBlendShapeConstantCurve(clip, "Body/Face", "Smile", 0.5f);
            SetBlendShapeConstantCurve(clip, "Body/Face", "MouthOpen", 1.0f);
            SetBlendShapeConstantCurve(clip, "Body/Head", "BlinkLeft", 0.25f);

            var blendShapeNames = new[] { "Smile", "Anger", "BlinkLeft", "MouthOpen" };
            var mask = new BitArray(blendShapeNames.Length);

            bool resolved = InvokeTryResolveContributeIndices(clip, blendShapeNames, mask);

            Assert.IsTrue(resolved);
            AssertMask(mask, 0, 2, 3);
        }

        [Test]
        public void TryResolveContributeIndices_NonBlendShapeCurvesOnly_ReturnsFalseAndLeavesEmptyMask()
        {
            var clip = CreateTrackedClip();
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalPosition.x", 0.5f);
            SetFloatCurve(clip, "Body/Face", typeof(Renderer), "material._Color.r", 0.8f);

            var blendShapeNames = new[] { "Smile", "BlinkLeft", "MouthOpen" };
            var mask = new BitArray(blendShapeNames.Length);

            bool resolved = InvokeTryResolveContributeIndices(clip, blendShapeNames, mask);

            Assert.IsFalse(resolved);
            AssertMask(mask);
        }

        [Test]
        public void TryResolveContributeIndices_MultibyteAndSymbolBlendShapeNames_UsesExactNames()
        {
            var clip = CreateTrackedClip();
            SetBlendShapeConstantCurve(clip, "Body/Face", "怒り眉★左", 0.75f);
            SetBlendShapeConstantCurve(clip, "Body/Face", "口_A+B(右)", 0.4f);

            var blendShapeNames = new[] { "怒り眉★左", "口_A+B(右)", "怒り眉★右" };
            var mask = new BitArray(blendShapeNames.Length);

            bool resolved = InvokeTryResolveContributeIndices(clip, blendShapeNames, mask);

            Assert.IsTrue(resolved);
            AssertMask(mask, 0, 1);
        }

        private AnimationClip CreateTrackedClip()
        {
            var clip = new AnimationClip();
            _trackedObjects.Add(clip);
            return clip;
        }

        private static bool InvokeTryResolveContributeIndices(
            AnimationClip clip,
            IReadOnlyList<string> blendShapeNames,
            BitArray output)
        {
            var sampler = new AnimationClipExpressionSampler();
            var method = typeof(AnimationClipExpressionSampler).GetMethod(
                "TryResolveContributeIndices",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(AnimationClip), typeof(IReadOnlyList<string>), typeof(BitArray) },
                null);

            Assert.IsNotNull(
                method,
                "AnimationClipExpressionSampler must expose TryResolveContributeIndices(AnimationClip, IReadOnlyList<string>, BitArray).");

            return (bool)method.Invoke(sampler, new object[] { clip, blendShapeNames, output });
        }

        private static void AssertMask(BitArray mask, params int[] expectedTrueIndices)
        {
            var expected = new HashSet<int>(expectedTrueIndices);
            for (int i = 0; i < mask.Length; i++)
            {
                Assert.AreEqual(expected.Contains(i), mask[i], $"mask[{i}]");
            }
        }

        private static void SetBlendShapeConstantCurve(
            AnimationClip clip,
            string path,
            string blendShapeName,
            float value)
        {
            SetFloatCurve(clip, path, typeof(SkinnedMeshRenderer), "blendShape." + blendShapeName, value);
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
