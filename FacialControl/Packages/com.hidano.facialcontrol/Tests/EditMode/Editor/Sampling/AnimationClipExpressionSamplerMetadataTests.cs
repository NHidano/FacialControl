using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Sampling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Sampling
{
    /// <summary>
    /// <see cref="AnimationClipExpressionSampler"/> returns transition metadata defaults.
    /// AnimationEvent metadata may remain on clips baked by earlier previews, but the sampler
    /// no longer treats those events as the transition source of truth.
    /// </summary>
    [TestFixture]
    public class AnimationClipExpressionSamplerMetadataTests
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
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Smile", 0.5f);
            return clip;
        }

        [Test]
        public void SampleSnapshot_NoMetadata_ReturnsDefaultTransitionMetadata()
        {
            var clip = CreateTrackedClip();
            var sampler = new AnimationClipExpressionSampler();

            var snapshot = sampler.SampleSnapshot("expr-meta-default", clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
        }

        [Test]
        public void SampleSummary_NoMetadata_ReturnsDefaultTransitionDuration()
        {
            var clip = CreateTrackedClip();
            var sampler = new AnimationClipExpressionSampler();

            var summary = sampler.SampleSummary(clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, summary.TransitionDuration);
        }

        [Test]
        public void SampleSummary_NoMetadata_ReturnsLinearCurvePreset()
        {
            var clip = CreateTrackedClip();
            var sampler = new AnimationClipExpressionSampler();

            var summary = sampler.SampleSummary(clip);

            Assert.AreEqual(TransitionCurvePreset.Linear, summary.TransitionCurve);
        }

        [Test]
        public void SampleSnapshot_LegacyDurationEvent_ReturnsDefaultTransitionDuration()
        {
            var clip = CreateTrackedClip();
            SetMetaEvents(clip, new[]
            {
                CreateMetaEvent("transitionDuration", 0.5f),
            });

            var sampler = new AnimationClipExpressionSampler();

            var snapshot = sampler.SampleSnapshot("expr-meta-duration", clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
        }

        [Test]
        public void SampleSnapshot_LegacyCurvePresetEvent_ReturnsLinearCurvePreset()
        {
            var clip = CreateTrackedClip();
            SetMetaEvents(clip, new[]
            {
                CreateMetaEvent("transitionCurvePreset", (float)(int)TransitionCurvePreset.EaseInOut),
            });

            var sampler = new AnimationClipExpressionSampler();

            var snapshot = sampler.SampleSnapshot("expr-meta-curve", clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
        }

        [Test]
        public void SampleSummary_LegacyAnimationEventMeta_ReturnsDefaultsWithoutError()
        {
            var clip = CreateTrackedClip();
            SetMetaEvents(clip, new[]
            {
                CreateMetaEvent("transitionDuration", 0.4f),
                CreateMetaEvent("transitionCurvePreset", (float)(int)TransitionCurvePreset.EaseInOut),
            });

            var sampler = new AnimationClipExpressionSampler();

            var summary = sampler.SampleSummary(clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, summary.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, summary.TransitionCurve);
        }

        [Test]
        public void SampleSnapshot_LegacyDuplicateMetaEvents_ReturnsDefaultsWithoutWarning()
        {
            var clip = CreateTrackedClip();
            SetMetaEvents(clip, new[]
            {
                CreateMetaEvent("transitionDuration", 0.4f),
                CreateMetaEvent("transitionDuration", 0.7f),
            });

            var sampler = new AnimationClipExpressionSampler();

            var snapshot = sampler.SampleSnapshot("expr-meta-dup", clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MetaSetFunctionName_IsExposedAsConstant()
        {
            Assert.AreEqual("FacialControlMeta_Set", AnimationClipExpressionSampler.MetaSetFunctionName);
        }

        private static AnimationEvent CreateMetaEvent(string key, float value)
        {
            return new AnimationEvent
            {
                time = 0f,
                functionName = AnimationClipExpressionSampler.MetaSetFunctionName,
                stringParameter = key,
                floatParameter = value,
            };
        }

        private static void SetMetaEvents(AnimationClip clip, AnimationEvent[] events)
        {
            AnimationUtility.SetAnimationEvents(clip, events);
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
