using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.Playable;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Playable
{
    [TestFixture]
    public class SkinnedMeshRendererBlendShapeWriterTests
    {
        private readonly List<Object> _trackedObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _trackedObjects.Count - 1; i >= 0; i--)
            {
                if (_trackedObjects[i] != null)
                {
                    Object.DestroyImmediate(_trackedObjects[i]);
                }
            }

            _trackedObjects.Clear();
        }

        [Test]
        public void Write_LegacyCollectedBlendShapeNames_BuildsEquivalentRendererMapping()
        {
            var face = CreateRenderer("Face", CreateMeshWithShapes("Smile", "Blink", "JawOpen"));
            var teeth = CreateRenderer("Teeth", CreateMeshWithShapes("JawOpen", "Smile"));
            var legacyNames = CollectLegacyBlendShapeNames(face, teeth);

            using var writer = new SkinnedMeshRendererBlendShapeWriter(new[] { face, teeth }, legacyNames);

            writer.Write(new[] { 0.15f, 0.35f, 0.55f });

            CollectionAssert.AreEqual(new[] { "Smile", "Blink", "JawOpen" }, legacyNames);
            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(15f).Within(0.0001f));
            Assert.That(face.GetBlendShapeWeight(1), Is.EqualTo(35f).Within(0.0001f));
            Assert.That(face.GetBlendShapeWeight(2), Is.EqualTo(55f).Within(0.0001f));
            Assert.That(teeth.GetBlendShapeWeight(0), Is.EqualTo(55f).Within(0.0001f));
            Assert.That(teeth.GetBlendShapeWeight(1), Is.EqualTo(15f).Within(0.0001f));
        }

        [Test]
        public void Write_NormalizedWeights_ScalesToBlendShapeWeightPercentage()
        {
            var face = CreateRenderer("Face", CreateMeshWithShapes("Smile"));

            using var writer = new SkinnedMeshRendererBlendShapeWriter(new[] { face }, new[] { "Smile" });

            writer.Write(new[] { 0.42f });

            Assert.That(face.GetBlendShapeWeight(0), Is.EqualTo(42f).Within(0.0001f));
        }

        private string[] CollectLegacyBlendShapeNames(params SkinnedMeshRenderer[] renderers)
        {
            var host = Track(new GameObject("FacialControllerHost"));
            host.AddComponent<Animator>();
            var controller = host.AddComponent<FacialController>();
            var method = typeof(FacialController).GetMethod(
                "CollectBlendShapeNames",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, "FacialController.CollectBlendShapeNames が見つかりません。");

            return (string[])method.Invoke(controller, new object[] { renderers });
        }

        private SkinnedMeshRenderer CreateRenderer(string name, Mesh mesh)
        {
            var go = Track(new GameObject(name));
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private Mesh CreateMeshWithShapes(params string[] shapeNames)
        {
            var mesh = Track(new Mesh());
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };

            var zeroDeltas = new Vector3[3];
            for (int i = 0; i < shapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(shapeNames[i], 100f, zeroDeltas, null, null);
            }

            return mesh;
        }

        private T Track<T>(T obj)
            where T : Object
        {
            _trackedObjects.Add(obj);
            return obj;
        }
    }
}
