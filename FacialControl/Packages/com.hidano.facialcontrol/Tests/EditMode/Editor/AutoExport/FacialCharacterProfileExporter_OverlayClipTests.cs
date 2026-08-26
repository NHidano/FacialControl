using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Editor.Sampling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.AutoExport
{
    [TestFixture]
    public sealed class FacialCharacterProfileExporter_OverlayClipTests
    {
        [Test]
        public void SampleAnimationClipsIntoCachedSnapshots_OverlayClips_BakesAndClearsByState()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            var blinkClip = new AnimationClip { name = "BlinkOverlay" };
            var sparkleClip = new AnimationClip { name = "SparkleOverlay" };
            var blushClip = new AnimationClip { name = "SuppressedBlushOverlay" };
            var sampler = new RecordingSampler();

            try
            {
                so.DefaultOverlays.Add(new OverlaySlotBindingSerializable
                {
                    slot = "blink",
                    animationClip = blinkClip,
                    cachedSnapshot = CreateSnapshotDto("Face", "StaleBlink", 99f),
                });
                so.DefaultOverlays.Add(new OverlaySlotBindingSerializable
                {
                    slot = "mouth",
                    cachedSnapshot = CreateSnapshotDto("Face", "StaleMouth", 99f),
                });
                so.Expressions.Add(new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    overlays = new List<OverlaySlotBindingSerializable>
                    {
                        new OverlaySlotBindingSerializable
                        {
                            slot = "sparkle",
                            animationClip = sparkleClip,
                            cachedSnapshot = CreateSnapshotDto("Face", "StaleSparkle", 99f),
                        },
                        new OverlaySlotBindingSerializable
                        {
                            slot = "blush",
                            suppress = true,
                            animationClip = blushClip,
                            cachedSnapshot = CreateSnapshotDto("Face", "StaleBlush", 99f),
                        },
                    },
                });

                FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, sampler);

                Assert.That(sampler.SampleSnapshotCallCount, Is.EqualTo(2));
                Assert.That(sampler.SampledIds, Is.EquivalentTo(new[] { "blink", "sparkle" }));
                AssertSnapshot(so.DefaultOverlays[0].cachedSnapshot, "blink_Sampled", 10f);
                AssertEmptySnapshot(so.DefaultOverlays[1].cachedSnapshot);
                AssertSnapshot(so.Expressions[0].overlays[0].cachedSnapshot, "sparkle_Sampled", 20f);
                AssertEmptySnapshot(so.Expressions[0].overlays[1].cachedSnapshot);
            }
            finally
            {
                Object.DestroyImmediate(blinkClip);
                Object.DestroyImmediate(sparkleClip);
                Object.DestroyImmediate(blushClip);
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void ExportProfileJson_OverlaySlotsAndBindings_WritesNewSchema()
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            var blinkClip = new AnimationClip { name = "BlinkOverlayExport" };
            var sparkleClip = new AnimationClip { name = "SparkleOverlayExport" };
            var sampler = new RecordingSampler();
            string assetName = "ExporterOverlayClips_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                so.name = assetName;
                SetSlots(so, "blink", "mouth", "sparkle", "blush");
                so.DefaultOverlays.Add(new OverlaySlotBindingSerializable
                {
                    slot = "blink",
                    animationClip = blinkClip,
                });
                so.DefaultOverlays.Add(new OverlaySlotBindingSerializable
                {
                    slot = "mouth",
                });
                so.Expressions.Add(new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.4f,
                    overlays = new List<OverlaySlotBindingSerializable>
                    {
                        new OverlaySlotBindingSerializable
                        {
                            slot = "sparkle",
                            animationClip = sparkleClip,
                        },
                        new OverlaySlotBindingSerializable
                        {
                            slot = "blush",
                            suppress = true,
                        },
                    },
                });

                FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots(so, sampler);

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(so);

                Assert.That(exported, Is.True);
                Assert.That(File.Exists(profilePath), Is.True);

                string json = File.ReadAllText(profilePath);
                StringAssert.Contains("\"slots\"", json);
                StringAssert.Contains("\"defaultOverlays\"", json);

                var dto = new SystemTextJsonParser().ParseProfileSnapshotV2(json);
                Assert.That(dto.slots, Is.EqualTo(new List<string> { "blink", "mouth", "sparkle", "blush" }));
                Assert.That(dto.defaultOverlays, Has.Count.EqualTo(2));
                Assert.That(dto.defaultOverlays[0].slot, Is.EqualTo("blink"));
                Assert.That(dto.defaultOverlays[0].suppress, Is.False);
                AssertSnapshot(dto.defaultOverlays[0].snapshot, "blink_Sampled", 10f);
                Assert.That(dto.defaultOverlays[1].slot, Is.EqualTo("mouth"));
                Assert.That(dto.defaultOverlays[1].snapshot, Is.Null);

                var overlays = dto.expressions[0].snapshot.overlays;
                Assert.That(overlays, Has.Count.EqualTo(2));
                Assert.That(overlays[0].slot, Is.EqualTo("sparkle"));
                Assert.That(overlays[0].suppress, Is.False);
                AssertSnapshot(overlays[0].snapshot, "sparkle_Sampled", 20f);
                Assert.That(overlays[1].slot, Is.EqualTo("blush"));
                Assert.That(overlays[1].suppress, Is.True);
                Assert.That(overlays[1].snapshot, Is.Null);
            }
            finally
            {
                if (!string.IsNullOrEmpty(profileDirectory) && Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }

                Object.DestroyImmediate(blinkClip);
                Object.DestroyImmediate(sparkleClip);
                Object.DestroyImmediate(so);
            }
        }

        private static void SetSlots(FacialCharacterProfileSO so, params string[] slots)
        {
            var serializedObject = new SerializedObject(so);
            var slotsProperty = serializedObject.FindProperty("_slots");
            slotsProperty.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                slotsProperty.GetArrayElementAtIndex(i).stringValue = slots[i];
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static OverlaySnapshotDto CreateSnapshotDto(
            string rendererPath,
            string blendShapeName,
            float value)
        {
            return new OverlaySnapshotDto
            {
                rendererPaths = new List<string> { rendererPath },
                blendShapes = new List<BlendShapeSnapshotDto>
                {
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = rendererPath,
                        name = blendShapeName,
                        value = value,
                    },
                },
                bones = new List<BoneSnapshotDto>(),
            };
        }

        private static void AssertSnapshot(OverlaySnapshotDto snapshot, string blendShapeName, float value)
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.blendShapes, Has.Count.EqualTo(1));
            Assert.That(snapshot.blendShapes[0].name, Is.EqualTo(blendShapeName));
            Assert.That(snapshot.blendShapes[0].value, Is.EqualTo(value).Within(1e-6f));
        }

        private static void AssertEmptySnapshot(OverlaySnapshotDto snapshot)
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.blendShapes, Is.Not.Null);
            Assert.That(snapshot.blendShapes, Is.Empty);
        }

        private sealed class RecordingSampler : IExpressionAnimationClipSampler
        {
            private readonly List<string> _sampledIds = new List<string>();

            public IReadOnlyList<string> SampledIds => _sampledIds;
            public int SampleSnapshotCallCount { get; private set; }

            public ExpressionSnapshot SampleSnapshot(string snapshotId, AnimationClip clip)
            {
                SampleSnapshotCallCount++;
                _sampledIds.Add(snapshotId);
                float value = string.Equals(snapshotId, "sparkle", StringComparison.Ordinal) ? 20f : 10f;
                return new ExpressionSnapshot(
                    snapshotId,
                    Expression.DefaultTransitionDuration,
                    TransitionCurvePreset.Linear,
                    new[]
                    {
                        new BlendShapeSnapshot("Face", snapshotId + "_Sampled", value),
                    },
                    Array.Empty<BoneSnapshot>(),
                    new[] { "Face" });
            }

            public ClipSummary SampleSummary(AnimationClip clip)
            {
                return new ClipSummary(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Expression.DefaultTransitionDuration,
                    TransitionCurvePreset.Linear);
            }
        }
    }
}
