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
    public class RoutingGraphViewTests
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
                inputSources = new List<InputSourceDeclarationSerializable>(),
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
        public void Construct_InitializesGraphViewManipulatorsAndMiniMap()
        {
            var graphView = new RoutingGraphView();

            Assert.That(graphView.MiniMap, Is.Not.Null);
            Assert.That(graphView.Q<MiniMap>(), Is.SameAs(graphView.MiniMap));
            Assert.That(graphView.Q<GridBackground>(), Is.Not.Null);
            Assert.That(graphView.contentViewContainer.transform.scale.x, Is.GreaterThan(0f));
        }

        [Test]
        public void GetCompatiblePorts_AllowsOnlySourceToLayerConnections()
        {
            RoutingGraphView graphView = CreateGraphView();

            List<Port> compatiblePorts = graphView.GetCompatiblePorts(
                graphView.AdapterNodeViews[0].GetOutputPort("lipsync-overlay:a"),
                null);

            Assert.That(compatiblePorts, Has.Count.EqualTo(1));
            Assert.That(compatiblePorts[0], Is.SameAs(graphView.LayerNodeViews[0].InputPort));
        }

        [Test]
        public void GetCompatiblePorts_AllowsLayerOutputToCompositeSlotReconnection()
        {
            RoutingGraphView graphView = CreateGraphView();
            graphView.SetOutputNode(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 1, ExclusionMode.Blend, new string[0]),
                }),
                _serializedObject,
                _mapper);

            List<Port> compatiblePorts = graphView.GetCompatiblePorts(
                graphView.LayerNodeViews[0].OutputPort,
                null);

            Assert.That(compatiblePorts, Has.Count.EqualTo(1));
            Assert.That(compatiblePorts[0], Is.SameAs(graphView.OutputNodeView.GetLayerInputPort(0)));
        }

        [Test]
        public void GraphViewChanged_CreateEdge_UsesCanonicalIdAndReplacesWithRoutingEdge()
        {
            RoutingGraphView graphView = CreateGraphView();
            var edge = new Edge
            {
                output = graphView.AdapterNodeViews[0].GetOutputPort("lipsync-overlay:a"),
                input = graphView.LayerNodeViews[0].InputPort,
            };

            GraphViewChange change = graphView.graphViewChanged(new GraphViewChange
            {
                edgesToCreate = new List<Edge> { edge },
            });

            Assert.That(_mapper.AddDeclarationCalls, Is.EqualTo(1));
            Assert.That(_mapper.LastLayerIndex, Is.EqualTo(0));
            Assert.That(_mapper.LastCanonicalId, Is.EqualTo("lipsync-overlay:a"));
            Assert.That(_mapper.LastWeight, Is.EqualTo(1f));
            Assert.That(change.edgesToCreate, Has.Count.EqualTo(1));
            Assert.That(change.edgesToCreate[0], Is.InstanceOf<RoutingEdge>());
        }

        [Test]
        public void GraphViewChanged_AllPortEdge_ExpandsToEveryAdapterOutput()
        {
            var graphView = new RoutingGraphView();
            graphView.SetAdapterNodes(new[]
            {
                new AdapterNodeData(
                    "ulipsync",
                    "ulipsync",
                    supportsAutoWire: true,
                    new[]
                    {
                        new AdapterOutputData("lipsync-overlay:a", "a"),
                        new AdapterOutputData("lipsync-overlay:i", "i"),
                    }),
            });
            graphView.SetLayerNodes(
                new[] { new LayerNodeData(0, "overlay", 0, ExclusionMode.Blend, new string[0]) },
                _serializedObject,
                _mapper);

            var edge = new Edge
            {
                output = graphView.AdapterNodeViews[0].AllPort,
                input = graphView.LayerNodeViews[0].InputPort,
            };

            GraphViewChange change = graphView.graphViewChanged(new GraphViewChange
            {
                edgesToCreate = new List<Edge> { edge },
            });

            Assert.That(_mapper.AddDeclarationCalls, Is.EqualTo(2));
            CollectionAssert.AreEqual(
                new[] { "Add:0:lipsync-overlay:a:1", "Add:0:lipsync-overlay:i:1" },
                _mapper.Invocations);
            // All ポートの一時エッジは残さず、rebuild / 合成エッジの貼り直しに委ねる。
            Assert.That(change.edgesToCreate, Is.Empty);
        }

        [Test]
        public void GraphViewChanged_RemoveEdge_RemovesMatchingDeclaration()
        {
            RoutingGraphView graphView = CreateGraphView();
            graphView.SetWiringEdges(
                new[] { new WiringEdgeData(0, "lipsync-overlay:a", 0.4f) },
                _serializedObject,
                _mapper);

            graphView.graphViewChanged(new GraphViewChange
            {
                elementsToRemove = new List<GraphElement> { graphView.RoutingEdges[0] },
            });

            Assert.That(_mapper.RemoveDeclarationCalls, Is.EqualTo(1));
            Assert.That(_mapper.LastLayerIndex, Is.EqualTo(0));
            Assert.That(_mapper.LastCanonicalId, Is.EqualTo("lipsync-overlay:a"));
        }

        [Test]
        public void GraphViewChanged_RewireEdge_RemovesOldThenAddsNewWithCarriedWeight()
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
                new[]
                {
                    new LayerNodeData(0, "overlay", 1, ExclusionMode.Blend, new string[0]),
                    new LayerNodeData(1, "emotion", 0, ExclusionMode.LastWins, new string[0]),
                },
                _serializedObject,
                _mapper);
            graphView.SetWiringEdges(
                new[] { new WiringEdgeData(0, "lipsync-overlay:a", 0.4f) },
                _serializedObject,
                _mapper);

            RoutingEdge oldEdge = graphView.RoutingEdges[0];
            var newEdge = new Edge
            {
                output = graphView.AdapterNodeViews[0].GetOutputPort("lipsync-overlay:a"),
                input = graphView.LayerNodeViews[1].InputPort,
            };

            graphView.graphViewChanged(new GraphViewChange
            {
                elementsToRemove = new List<GraphElement> { oldEdge },
                edgesToCreate = new List<Edge> { newEdge },
            });

            Assert.That(_mapper.RemoveDeclarationCalls, Is.EqualTo(1));
            Assert.That(_mapper.AddDeclarationCalls, Is.EqualTo(1));
            Assert.That(_mapper.LastLayerIndex, Is.EqualTo(1));
            Assert.That(_mapper.LastCanonicalId, Is.EqualTo("lipsync-overlay:a"));
            Assert.That(_mapper.LastWeight, Is.EqualTo(0.4f));
            CollectionAssert.AreEqual(
                new[] { "Remove:0:lipsync-overlay:a", "Add:1:lipsync-overlay:a:0.4" },
                _mapper.Invocations);
        }

        [Test]
        public void GraphViewChanged_CompositionReconnect_SwapsLayerPriorities()
        {
            RoutingGraphView graphView = CreateReorderableGraphView();

            // レイヤー 0（priority 0）の Blend 出力を、レイヤー 1（priority 1）のスロットへ繋ぎ直す。
            var edge = new Edge
            {
                output = graphView.LayerNodeViews[0].OutputPort,
                input = graphView.OutputNodeView.GetLayerInputPort(1),
            };

            graphView.graphViewChanged(new GraphViewChange
            {
                edgesToCreate = new List<Edge> { edge },
            });

            Assert.That(_mapper.SetLayerPropertiesCalls, Is.EqualTo(2));
            CollectionAssert.AreEqual(
                new[] { "SetLayer:0:1", "SetLayer:1:0" },
                _mapper.LayerPriorityInvocations);
        }

        [Test]
        public void CompositionEdge_IsNotDeletable_AndRemovalKeepsLayerDeclarations()
        {
            var graphView = new RoutingGraphView();
            graphView.SetAdapterNodes(new[]
            {
                new AdapterNodeData(
                    "ulipsync",
                    "ulipsync",
                    supportsAutoWire: true,
                    new[]
                    {
                        new AdapterOutputData("lipsync-overlay:a", "a"),
                        new AdapterOutputData("lipsync-overlay:i", "i"),
                    }),
            });
            graphView.SetLayerNodes(
                new[]
                {
                    new LayerNodeData(
                        0,
                        "overlay",
                        0,
                        ExclusionMode.Blend,
                        new string[0],
                        new[]
                        {
                            new LayerInputData("lipsync-overlay:a", "a", 1f),
                            new LayerInputData("lipsync-overlay:i", "i", 0.5f),
                        }),
                },
                _serializedObject,
                _mapper);
            graphView.SetOutputNode(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 0, ExclusionMode.Blend, new string[0]),
                }),
                _serializedObject,
                _mapper);
            graphView.SetCompositionEdges();

            Assert.That(graphView.CompositionEdges, Has.Count.EqualTo(1));
            Edge compositionEdge = graphView.CompositionEdges[0];

            // 合成エッジは削除不可（Layer→Composite Output は構造的に常に存在する）。
            Assert.That(compositionEdge.capabilities & Capabilities.Deletable, Is.EqualTo((Capabilities)0));

            // 仮に elementsToRemove に渡されても、そのレイヤーの入力配線は消さない。
            graphView.graphViewChanged(new GraphViewChange
            {
                elementsToRemove = new List<GraphElement> { compositionEdge },
            });

            Assert.That(_mapper.RemoveDeclarationCalls, Is.EqualTo(0));
        }

        [Test]
        public void PruneUntrackedEdges_RemovesGhostEdge_KeepsTrackedEdges()
        {
            RoutingGraphView graphView = CreateGraphView();
            graphView.SetWiringEdges(
                new[] { new WiringEdgeData(0, "lipsync-overlay:a", 1f) },
                _serializedObject,
                _mapper);

            // All ポート展開で消費される一時エッジ（ゴースト）を模し、追跡されないエッジを直接追加する。
            var ghost = new Edge
            {
                output = graphView.AdapterNodeViews[0].AllPort,
                input = graphView.LayerNodeViews[0].InputPort,
            };
            graphView.AddElement(ghost);
            CollectionAssert.Contains(graphView.edges.ToList(), ghost);

            graphView.PruneUntrackedEdges();

            List<Edge> remaining = graphView.edges.ToList();
            CollectionAssert.DoesNotContain(remaining, ghost);
            CollectionAssert.Contains(remaining, graphView.RoutingEdges[0]);
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

        private RoutingGraphView CreateReorderableGraphView()
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
                new[]
                {
                    new LayerNodeData(0, "overlay", 0, ExclusionMode.Blend, new string[0]),
                    new LayerNodeData(1, "emotion", 1, ExclusionMode.LastWins, new string[0]),
                },
                _serializedObject,
                _mapper);
            graphView.SetOutputNode(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 0, ExclusionMode.Blend, new string[0]),
                    new OutputLayerData(1, "emotion", 1, ExclusionMode.LastWins, new string[0]),
                }),
                _serializedObject,
                _mapper);
            return graphView;
        }

        private sealed class RecordingWiringSerializedMapper : IWiringSerializedMapper
        {
            public List<string> Invocations { get; } = new List<string>();

            public List<string> LayerPriorityInvocations { get; } = new List<string>();

            public int AddDeclarationCalls { get; private set; }

            public int RemoveDeclarationCalls { get; private set; }

            public int SetLayerPropertiesCalls { get; private set; }

            public int LastLayerIndex { get; private set; }

            public string LastCanonicalId { get; private set; } = string.Empty;

            public float LastWeight { get; private set; }

            public void AddDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
            {
                AddDeclarationCalls++;
                LastLayerIndex = layerIndex;
                LastCanonicalId = canonicalId;
                LastWeight = weight;
                Invocations.Add($"Add:{layerIndex}:{canonicalId}:{weight}");
            }

            public void RemoveDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId)
            {
                RemoveDeclarationCalls++;
                LastLayerIndex = layerIndex;
                LastCanonicalId = canonicalId;
                Invocations.Add($"Remove:{layerIndex}:{canonicalId}");
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
                SetLayerPropertiesCalls++;
                LayerPriorityInvocations.Add($"SetLayer:{layerIndex}:{priority}");
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
