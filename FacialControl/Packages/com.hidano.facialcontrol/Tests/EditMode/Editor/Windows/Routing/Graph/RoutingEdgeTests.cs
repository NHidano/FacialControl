using System.Collections.Generic;
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
    public class RoutingEdgeTests
    {
        private TestFacialCharacterProfileSO _profile;
        private SerializedObject _serializedObject;
        private NoOpWiringSerializedMapper _mapper;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
                priority = 1,
                exclusionMode = ExclusionMode.Blend,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable
                    {
                        id = "lipsync-overlay:a",
                        weight = 0.4f,
                    },
                },
            });

            _serializedObject = new SerializedObject(_profile);
            _mapper = new NoOpWiringSerializedMapper();
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
        public void SetWiringEdges_BuildsRoutingEdge_ConnectingAdapterOutputToLayerInput()
        {
            RoutingGraphView graphView = CreateGraphView();

            graphView.SetWiringEdges(
                new[] { new WiringEdgeData(0, "lipsync-overlay:a", 0.4f) },
                _serializedObject,
                _mapper);

            Assert.That(graphView.RoutingEdges, Has.Count.EqualTo(1));
            RoutingEdge edge = graphView.RoutingEdges[0];
            Assert.That(edge.output, Is.SameAs(graphView.AdapterNodeViews[0].GetOutputPort("lipsync-overlay:a")));
            Assert.That(edge.input, Is.SameAs(graphView.LayerNodeViews[0].InputPort));
            Assert.That(edge.EdgeData.Weight, Is.EqualTo(0.4f));
        }

        [Test]
        public void RoutingEdge_IsSelectableAndDeletable()
        {
            RoutingEdge edge = CreateSingleEdge();

            Assert.That(edge.IsSelectable(), Is.True);
            Assert.That((edge.capabilities & Capabilities.Deletable), Is.EqualTo(Capabilities.Deletable));
        }

        [Test]
        public void SetWiringEdges_RebuildsExistingEdges()
        {
            RoutingGraphView graphView = CreateGraphView();

            graphView.SetWiringEdges(
                new[] { new WiringEdgeData(0, "lipsync-overlay:a", 0.4f) },
                _serializedObject,
                _mapper);
            graphView.SetWiringEdges(
                new[] { new WiringEdgeData(0, "lipsync-overlay:a", 0.9f) },
                _serializedObject,
                _mapper);

            Assert.That(graphView.RoutingEdges, Has.Count.EqualTo(1));
            Assert.That(graphView.RoutingEdges[0].EdgeData.Weight, Is.EqualTo(0.9f));
        }

        [Test]
        public void SetInvalidInputs_BuildsMovableOrphanNodeConnectedToLayer()
        {
            RoutingGraphView graphView = CreateGraphView();

            graphView.SetInvalidInputs(
                new[] { new DanglingEdgeData(0, 2, "ulipsync:a") });

            Assert.That(graphView.OrphanInputNodes, Has.Count.EqualTo(1));
            OrphanInputNodeView orphanNode = graphView.OrphanInputNodes[0];
            Assert.That(orphanNode.Data.Id, Is.EqualTo("ulipsync:a"));
            Assert.That(orphanNode.OutputPort, Is.Not.Null);
            Assert.That(orphanNode.OutputPort.direction, Is.EqualTo(Direction.Output));
            Assert.That((orphanNode.capabilities & Capabilities.Movable), Is.EqualTo(Capabilities.Movable));
            Assert.That((orphanNode.capabilities & Capabilities.Deletable), Is.EqualTo((Capabilities)0));

            Assert.That(graphView.OrphanInputEdges, Has.Count.EqualTo(1));
            Edge edge = graphView.OrphanInputEdges[0];
            Assert.That(edge.output, Is.SameAs(orphanNode.OutputPort));
            Assert.That(edge.input, Is.SameAs(graphView.LayerNodeViews[0].InputPort));
        }

        [Test]
        public void SetInvalidInputs_RebuildsExistingOrphanNodes()
        {
            RoutingGraphView graphView = CreateGraphView();

            graphView.SetInvalidInputs(
                new[] { new DanglingEdgeData(0, 1, "legacy:a") });
            graphView.SetInvalidInputs(
                new[] { new DanglingEdgeData(0, 3, "legacy:b") });

            Assert.That(graphView.OrphanInputNodes, Has.Count.EqualTo(1));
            Assert.That(graphView.OrphanInputNodes[0].Data.Id, Is.EqualTo("legacy:b"));
        }

        [Test]
        public void SetCompositionEdges_ConnectsEachLayerToCompositeOutput()
        {
            RoutingGraphView graphView = CreateGraphView();
            graphView.SetOutputNode(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 1, new string[0]),
                }),
                _serializedObject,
                _mapper);

            graphView.SetCompositionEdges();

            Assert.That(graphView.CompositionEdges, Has.Count.EqualTo(1));
            Edge edge = graphView.CompositionEdges[0];
            Assert.That(edge.output, Is.SameAs(graphView.LayerNodeViews[0].OutputPort));
            Assert.That(edge.input, Is.SameAs(graphView.OutputNodeView.GetLayerInputPort(0)));
        }

        [Test]
        public void SetCompositionEdges_BuildsSelectableNonDeletableEdge()
        {
            RoutingGraphView graphView = CreateGraphView();
            graphView.SetOutputNode(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 1, new string[0]),
                }),
                _serializedObject,
                _mapper);

            graphView.SetCompositionEdges();

            Edge edge = graphView.CompositionEdges[0];
            // 選択・端点ドラッグでの繋ぎ替えは許可するが、削除は不可。
            Assert.That(edge.IsSelectable(), Is.True);
            Assert.That((edge.capabilities & Capabilities.Deletable), Is.EqualTo((Capabilities)0));
            Assert.That(edge.pickingMode, Is.Not.EqualTo(PickingMode.Ignore));
        }

        private RoutingGraphView CreateGraphView()
        {
            var graphView = new RoutingGraphView();
            graphView.SetAdapterNodes(
                new[]
                {
                    new AdapterNodeData(
                        "ulipsync",
                        "ulipsync",
                        supportsAutoWire: true,
                        new[] { new AdapterOutputData("lipsync-overlay:a", "a") }),
                });
            graphView.SetLayerNodes(
                new[] { new LayerNodeData(0, "overlay", 1, ExclusionMode.Blend, new string[0]) },
                _serializedObject,
                _mapper);
            return graphView;
        }

        private RoutingEdge CreateSingleEdge()
        {
            RoutingGraphView graphView = CreateGraphView();
            graphView.SetWiringEdges(
                new[] { new WiringEdgeData(0, "lipsync-overlay:a", 0.4f) },
                _serializedObject,
                _mapper);
            return graphView.RoutingEdges[0];
        }

        private sealed class NoOpWiringSerializedMapper : IWiringSerializedMapper
        {
            public void AddDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
            {
            }

            public void RemoveDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId)
            {
            }

            public void SetWeight(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
            {
            }

            public void SetLayerProperties(
                SerializedObject serializedObject,
                int layerIndex,
                string layerName,
                int priority,
                ExclusionMode exclusionMode,
                IReadOnlyList<string> overrideMask)
            {
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
