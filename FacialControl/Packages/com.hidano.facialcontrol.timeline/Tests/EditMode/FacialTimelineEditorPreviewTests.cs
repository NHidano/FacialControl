using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.EditorPreview;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.TestTools;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;
using FacialController = Hidano.FacialControl.Adapters.Playable.FacialController;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    public sealed class FacialTimelineEditorPreviewTests
    {
        [Test]
        public void GatherProperties_RegistersBlendShapesAndEyeRotations()
        {
            var root = new GameObject("Root");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var director = root.AddComponent<PlayableDirector>();
            var receiver = root.AddComponent<FacialTimelineReceiver>();
            var controller = root.AddComponent<FacialController>();

            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(root.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateBlendShapeMesh("Smile");

            var leftEye = new GameObject("LeftEye");
            leftEye.transform.SetParent(root.transform, false);
            var rightEye = new GameObject("RightEye");
            rightEye.transform.SetParent(root.transform, false);

            var profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            profile.MutableGazeConfigs.Add(new GazeBindingConfig
            {
                leftEyeBonePath = "LeftEye",
                rightEyeBonePath = "RightEye",
            });

            controller.CharacterSO = profile;
            controller.SkinnedMeshRenderers = new[] { renderer };

            var track = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
            director.SetGenericBinding(track, receiver);

            var collector = new RecordingPropertyCollector();

            try
            {
                Assert.That(FacialTimelineEditorPreviewBridge.GatherProperties, Is.Not.Null);

                FacialTimelineEditorPreviewBridge.GatherProperties(director, track, collector);

                Assert.That(collector.Properties, Does.Contain("FaceMesh|blendShape.Smile"));
                Assert.That(collector.Properties, Does.Contain("LeftEye|m_LocalRotation.x"));
                Assert.That(collector.Properties, Does.Contain("LeftEye|m_LocalRotation.y"));
                Assert.That(collector.Properties, Does.Contain("LeftEye|m_LocalRotation.z"));
                Assert.That(collector.Properties, Does.Contain("LeftEye|m_LocalRotation.w"));
                Assert.That(collector.Properties, Does.Contain("RightEye|m_LocalRotation.x"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(timeline);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ApplyPreview_SamplesBakeIntoBlendShapesAndGazeBones()
        {
            var root = new GameObject("Root");
            var receiver = root.AddComponent<FacialTimelineReceiver>();
            var controller = root.AddComponent<FacialController>();

            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(root.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateBlendShapeMesh("Smile");

            var leftEye = new GameObject("LeftEye");
            leftEye.transform.SetParent(root.transform, false);
            var rightEye = new GameObject("RightEye");
            rightEye.transform.SetParent(root.transform, false);

            var profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            profile.MutableGazeConfigs.Add(new GazeBindingConfig
            {
                leftEyeBonePath = "LeftEye",
                rightEyeBonePath = "RightEye",
                leftEyeYawAxisLocal = Vector3.up,
                leftEyePitchAxisLocal = Vector3.right,
                rightEyeYawAxisLocal = Vector3.up,
                rightEyePitchAxisLocal = Vector3.right,
                outerYawAngle = 20f,
                innerYawAngle = 10f,
                lookUpAngle = 15f,
                lookDownAngle = 5f,
            });

            controller.CharacterSO = profile;
            controller.SkinnedMeshRenderers = new[] { renderer };

            var bakeAsset = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bakeAsset.ExpressionBakes = new[]
            {
                new ExpressionSourceBake
                {
                    LayerName = "Expressions",
                    Curves = new[]
                    {
                        new BlendShapeCurve
                        {
                            BlendShapeName = "Smile",
                            Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                        },
                    },
                },
            };
            bakeAsset.ValueBakes = new[]
            {
                new ValueChannelBake
                {
                    Sub = "gaze-main",
                    IsGaze = true,
                    Axes = new[]
                    {
                        AnimationCurve.Constant(0f, 1f, 0.5f),
                        AnimationCurve.Constant(0f, 1f, 0.25f),
                    },
                },
            };

            receiver.BakeAsset = bakeAsset;

            try
            {
                Assert.That(FacialTimelineEditorPreviewBridge.ApplyPreview, Is.Not.Null);

                FacialTimelineEditorPreviewBridge.ApplyPreview(receiver, null, 0.5d);

                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(50f).Within(0.01f));
                Assert.That(leftEye.transform.localRotation, Is.Not.EqualTo(Quaternion.identity));
                Assert.That(rightEye.transform.localRotation, Is.Not.EqualTo(Quaternion.identity));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bakeAsset);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ApplyPreview_WithoutBakeAsset_LeavesSceneUntouchedAndWarns()
        {
            var root = new GameObject("Root");
            var receiver = root.AddComponent<FacialTimelineReceiver>();
            var controller = root.AddComponent<FacialController>();

            var meshObject = new GameObject("FaceMesh");
            meshObject.transform.SetParent(root.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateBlendShapeMesh("Smile");
            renderer.SetBlendShapeWeight(0, 12f);

            var leftEye = new GameObject("LeftEye");
            leftEye.transform.SetParent(root.transform, false);
            leftEye.transform.localRotation = Quaternion.Euler(1f, 2f, 3f);

            var profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            profile.MutableGazeConfigs.Add(new GazeBindingConfig
            {
                leftEyeBonePath = "LeftEye",
            });

            controller.CharacterSO = profile;
            controller.SkinnedMeshRenderers = new[] { renderer };

            Quaternion beforeRotation = leftEye.transform.localRotation;
            LogAssert.Expect(LogType.Warning, "[FacialTimelineEditorPreview] BakeAsset is missing. Scrub preview is disabled.");

            try
            {
                FacialTimelineEditorPreviewBridge.ApplyPreview(receiver, null, 0.25d);

                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(12f).Within(0.01f));
                Assert.That(leftEye.transform.localRotation, Is.EqualTo(beforeRotation));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Mesh CreateBlendShapeMesh(string blendShapeName)
        {
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.tangents = new[] { Vector4.zero, Vector4.zero, Vector4.zero };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2 };

            Vector3[] deltaVertices =
            {
                new Vector3(0.1f, 0f, 0f),
                new Vector3(0f, 0.1f, 0f),
                new Vector3(0f, 0f, 0.1f),
            };
            mesh.AddBlendShapeFrame(blendShapeName, 100f, deltaVertices, new Vector3[3], new Vector3[3]);
            return mesh;
        }

        private sealed class TestFacialCharacterProfileSO : FacialCharacterProfileSO
        {
            public List<GazeBindingConfig> MutableGazeConfigs => _gazeConfigs;
        }

        private sealed class RecordingPropertyCollector : IPropertyCollector
        {
            public List<string> Properties { get; } = new List<string>();

            public void PushActiveGameObject(GameObject gameObject)
            {
            }

            public void PopActiveGameObject()
            {
            }

            public void AddFromClip(AnimationClip clip)
            {
            }

            public void AddFromClips(IEnumerable<AnimationClip> clips)
            {
            }

            public void AddFromName<T>(string name) where T : Component
            {
                Properties.Add(typeof(T).Name + "|" + name);
            }

            public void AddFromName(string name)
            {
                Properties.Add(name);
            }

            public void AddFromClip(GameObject obj, AnimationClip clip)
            {
            }

            public void AddFromClips(GameObject obj, IEnumerable<AnimationClip> clips)
            {
            }

            public void AddFromName<T>(GameObject obj, string name) where T : Component
            {
                Properties.Add(obj.name + "|" + name);
            }

            public void AddFromName(GameObject obj, string name)
            {
                Properties.Add(obj.name + "|" + name);
            }

            public void AddFromName(Component component, string name)
            {
                Properties.Add(component.gameObject.name + "|" + name);
            }

            public void AddFromComponent(GameObject obj, Component component)
            {
            }

            public void AddObjectProperties(UnityEngine.Object obj, AnimationClip clip)
            {
            }
        }
    }
}
