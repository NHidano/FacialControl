using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows.Routing.Graph;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Graph
{
    [TestFixture]
    public class LayerNodeViewTests
    {
        private TestFacialCharacterProfileSO _profile;
        private SerializedObject _serializedObject;
        private RecordingWiringSerializedMapper _mapper;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
                priority = 1,
                exclusionMode = ExclusionMode.Blend,
                layerOverrideMask = new List<string> { "emotion" },
            });
            _serializedObject = new SerializedObject(_profile);
            _mapper = new RecordingWiringSerializedMapper();
        }

        [TearDown]
        public void TearDown()
        {
            if (_profile != null)
            {
                Object.DestroyImmediate(_profile);
                _profile = null;
            }
        }

        [Test]
        public void Construct_RendersNameAndInputWeightRows()
        {
            var view = CreateView();

            Assert.That(view.title, Is.EqualTo("overlay"));
            Assert.That(view.NameField.value, Is.EqualTo("overlay"));
            Assert.That(view.InputPort, Is.Not.Null);
            Assert.That(view.InputPort.direction, Is.EqualTo(Direction.Input));

            FloatField[] weightFields = view
                .Query<FloatField>(className: LayerNodeView.WeightFieldClassName)
                .ToList()
                .ToArray();
            Assert.That(weightFields, Has.Length.EqualTo(2));
            Assert.That(weightFields[0].value, Is.EqualTo(1f));
            Assert.That(weightFields[1].value, Is.EqualTo(0.5f));
        }

        [Test]
        public void EditingName_PreservesCompositionAndUpdatesTitle()
        {
            var view = CreateView();

            InvokeApplyLayerName(view, "overlay-updated");

            Assert.That(_mapper.SetLayerPropertiesCalls, Is.EqualTo(1));
            Assert.That(_mapper.LastLayerIndex, Is.EqualTo(0));
            Assert.That(_mapper.LastLayerName, Is.EqualTo("overlay-updated"));
            Assert.That(_mapper.LastPriority, Is.EqualTo(1));
            Assert.That(_mapper.LastExclusionMode, Is.EqualTo(ExclusionMode.Blend));
            CollectionAssert.AreEqual(new[] { "emotion" }, _mapper.LastOverrideMask);
            Assert.That(view.title, Is.EqualTo("overlay-updated"));
        }

        [Test]
        public void EditingInputWeight_CallsSetWeightWithCanonicalId()
        {
            var view = CreateView();

            InvokeApplyInputWeight(view, "lipsync-overlay:i", 0.75f);

            Assert.That(_mapper.SetWeightCalls, Is.EqualTo(1));
            Assert.That(_mapper.LastWeightLayerIndex, Is.EqualTo(0));
            Assert.That(_mapper.LastWeightCanonicalId, Is.EqualTo("lipsync-overlay:i"));
            Assert.That(_mapper.LastWeight, Is.EqualTo(0.75f));
        }

        [Test]
        public void SetLayerNodes_RebuildsGraphWithLayerNodeViews()
        {
            var graphView = new RoutingGraphView();
            var layerNodes = new[]
            {
                new LayerNodeData(0, "overlay", 1, ExclusionMode.Blend, new[] { "emotion" }),
                new LayerNodeData(1, "emotion", 2, ExclusionMode.LastWins, new string[0]),
            };

            graphView.SetLayerNodes(layerNodes, _serializedObject, _mapper);

            Assert.That(graphView.LayerNodeViews, Has.Count.EqualTo(2));
            Assert.That(graphView.LayerNodeViews[0].NameField.value, Is.EqualTo("overlay"));
            Assert.That(graphView.LayerNodeViews[1].NameField.value, Is.EqualTo("emotion"));
        }

        private LayerNodeView CreateView()
        {
            return new LayerNodeView(
                new LayerNodeData(
                    0,
                    "overlay",
                    1,
                    ExclusionMode.Blend,
                    new[] { "emotion" },
                    new[]
                    {
                        new LayerInputData("lipsync-overlay:a", "a", 1f),
                        new LayerInputData("lipsync-overlay:i", "i", 0.5f),
                    }),
                _serializedObject,
                _mapper);
        }

        private static void InvokeApplyLayerName(LayerNodeView view, string layerName)
        {
            MethodInfo method = typeof(LayerNodeView).GetMethod(
                "ApplyLayerName",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(view, new object[] { layerName });
        }

        private static void InvokeApplyInputWeight(LayerNodeView view, string canonicalId, float weight)
        {
            MethodInfo method = typeof(LayerNodeView).GetMethod(
                "ApplyInputWeight",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(view, new object[] { canonicalId, weight });
        }

        private sealed class RecordingWiringSerializedMapper : IWiringSerializedMapper
        {
            public int SetLayerPropertiesCalls { get; private set; }

            public int SetWeightCalls { get; private set; }

            public int LastLayerIndex { get; private set; }

            public string LastLayerName { get; private set; } = string.Empty;

            public int LastPriority { get; private set; }

            public ExclusionMode LastExclusionMode { get; private set; }

            public IReadOnlyList<string> LastOverrideMask { get; private set; } = new string[0];

            public int LastWeightLayerIndex { get; private set; }

            public string LastWeightCanonicalId { get; private set; } = string.Empty;

            public float LastWeight { get; private set; }

            public void AddDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
            {
            }

            public void RemoveDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId)
            {
            }

            public void SetWeight(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
            {
                SetWeightCalls++;
                LastWeightLayerIndex = layerIndex;
                LastWeightCanonicalId = canonicalId;
                LastWeight = weight;
            }

            public void SetLayerProperties(
                SerializedObject serializedObject,
                int layerIndex,
                string layerName,
                int priority,
                ExclusionMode exclusionMode,
                IReadOnlyList<string> overrideMask)
            {
                SetLayerPropertiesCalls++;
                LastLayerIndex = layerIndex;
                LastLayerName = layerName;
                LastPriority = priority;
                LastExclusionMode = exclusionMode;
                LastOverrideMask = overrideMask;
            }

            public void BeginContinuousWeight(SerializedObject serializedObject, int layerIndex, string canonicalId)
            {
            }

            public void SetWeightContinuous(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
            {
            }

            public void EndContinuousWeight()
            {
            }
        }
    }
}
