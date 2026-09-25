using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows.Routing.Graph;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Graph
{
    [TestFixture]
    public class OutputNodeViewTests
    {
        private TestFacialCharacterProfileSO _profile;
        private SerializedObject _serializedObject;
        private RecordingWiringSerializedMapper _mapper;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
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
        public void Construct_RendersEditableCompositionFieldsPerLayer()
        {
            var view = new OutputNodeView(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(1, "emotion", 0, ExclusionMode.LastWins, new string[0]),
                    new OutputLayerData(0, "overlay", 2, ExclusionMode.Blend, new[] { "emotion", "fx" }),
                }),
                _serializedObject,
                _mapper);

            Assert.That(view.title, Is.EqualTo(OutputNodeView.NodeTitle));

            IntegerField[] priorityFields = view
                .Query<IntegerField>(className: OutputNodeView.PriorityFieldClassName)
                .ToList()
                .ToArray();
            Assert.That(priorityFields, Has.Length.EqualTo(2));
            Assert.That(priorityFields[0].value, Is.EqualTo(0));
            Assert.That(priorityFields[1].value, Is.EqualTo(2));

            EnumField[] exclusionFields = view
                .Query<EnumField>(className: OutputNodeView.ExclusionFieldClassName)
                .ToList()
                .ToArray();
            Assert.That(exclusionFields, Has.Length.EqualTo(2));
            Assert.That((ExclusionMode)exclusionFields[1].value, Is.EqualTo(ExclusionMode.Blend));

            TextField[] maskFields = view
                .Query<TextField>(className: OutputNodeView.MaskFieldClassName)
                .ToList()
                .ToArray();
            Assert.That(maskFields, Has.Length.EqualTo(2));
            Assert.That(maskFields[1].value, Is.EqualTo("emotion, fx"));

            Assert.That(view.GetLayerInputPort(0), Is.Not.Null);
            Assert.That(view.GetLayerInputPort(1), Is.Not.Null);
        }

        [Test]
        public void ApplyLayer_WritesLayerPropertiesPreservingName()
        {
            var view = new OutputNodeView(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 2, ExclusionMode.Blend, new[] { "emotion" }),
                }),
                _serializedObject,
                _mapper);

            InvokeApplyLayer(view, 0, "overlay", 5, ExclusionMode.LastWins, new[] { "emotion", "fx" });

            Assert.That(_mapper.SetLayerPropertiesCalls, Is.EqualTo(1));
            Assert.That(_mapper.LastLayerIndex, Is.EqualTo(0));
            Assert.That(_mapper.LastLayerName, Is.EqualTo("overlay"));
            Assert.That(_mapper.LastPriority, Is.EqualTo(5));
            Assert.That(_mapper.LastExclusionMode, Is.EqualTo(ExclusionMode.LastWins));
            CollectionAssert.AreEqual(new[] { "emotion", "fx" }, _mapper.LastOverrideMask);
        }

        [Test]
        public void SetOutputNode_RebuildsGraphWithUpdatedRows()
        {
            var graphView = new RoutingGraphView();

            graphView.SetOutputNode(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 5, ExclusionMode.Blend, new[] { "emotion" }),
                }),
                _serializedObject,
                _mapper);

            Assert.That(graphView.OutputNodeView, Is.Not.Null);
            Assert.That(
                graphView.OutputNodeView
                    .Query<IntegerField>(className: OutputNodeView.PriorityFieldClassName)
                    .ToList(),
                Has.Count.EqualTo(1));

            graphView.SetOutputNode(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(1, "emotion", 1, ExclusionMode.LastWins, new string[0]),
                    new OutputLayerData(0, "overlay", 3, ExclusionMode.Blend, new[] { "emotion", "fx" }),
                }),
                _serializedObject,
                _mapper);

            Assert.That(
                graphView.OutputNodeView
                    .Query<IntegerField>(className: OutputNodeView.PriorityFieldClassName)
                    .ToList(),
                Has.Count.EqualTo(2));
        }

        [Test]
        public void SwapLayerOrderByIndex_SwapsPrioritiesOfTwoLayers()
        {
            var view = new OutputNodeView(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 0, ExclusionMode.Blend, new string[0]),
                    new OutputLayerData(1, "emotion", 5, ExclusionMode.LastWins, new string[0]),
                }),
                _serializedObject,
                _mapper);

            view.SwapLayerOrderByIndex(0, 1);

            Assert.That(_mapper.SetLayerPropertiesCalls, Is.EqualTo(2));
            CollectionAssert.AreEqual(
                new[] { "0:5", "1:0" },
                _mapper.LayerPriorityInvocations);
        }

        [Test]
        public void ReorderButtons_AreDisabledAtListEnds()
        {
            var view = new OutputNodeView(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 0, ExclusionMode.Blend, new string[0]),
                    new OutputLayerData(1, "emotion", 5, ExclusionMode.LastWins, new string[0]),
                }),
                _serializedObject,
                _mapper);

            List<Button> reorderButtons = view
                .Query<Button>(className: OutputNodeView.ReorderButtonClassName)
                .ToList();
            List<Button> upButtons = reorderButtons
                .Where(b => b.text == OutputNodeView.ReorderUpButtonText)
                .ToList();
            List<Button> downButtons = reorderButtons
                .Where(b => b.text == OutputNodeView.ReorderDownButtonText)
                .ToList();

            Assert.That(upButtons, Has.Count.EqualTo(2));
            Assert.That(downButtons, Has.Count.EqualTo(2));
            // 先頭行の ▲ と末尾行の ▼ は無効。
            Assert.That(upButtons[0].enabledSelf, Is.False);
            Assert.That(downButtons[1].enabledSelf, Is.False);
            Assert.That(upButtons[1].enabledSelf, Is.True);
            Assert.That(downButtons[0].enabledSelf, Is.True);
        }

        [Test]
        public void ClickMoveUpButton_SwapsPriorityWithLayerAbove()
        {
            var view = new OutputNodeView(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 0, ExclusionMode.Blend, new string[0]),
                    new OutputLayerData(1, "emotion", 5, ExclusionMode.LastWins, new string[0]),
                }),
                _serializedObject,
                _mapper);

            Button secondRowUp = view
                .Query<Button>(className: OutputNodeView.ReorderButtonClassName)
                .ToList()
                .Where(b => b.text == OutputNodeView.ReorderUpButtonText)
                .ToList()[1];

            InvokeButton(secondRowUp);

            Assert.That(_mapper.SetLayerPropertiesCalls, Is.EqualTo(2));
            // emotion(idx1) は overlay の priority 0 へ、overlay(idx0) は emotion の priority 5 へ。
            CollectionAssert.AreEqual(
                new[] { "1:0", "0:5" },
                _mapper.LayerPriorityInvocations);
        }

        private static void InvokeApplyLayer(
            OutputNodeView view,
            int layerIndex,
            string layerName,
            int priority,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> overrideMask)
        {
            MethodInfo method = typeof(OutputNodeView).GetMethod(
                "ApplyLayer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(view, new object[] { layerIndex, layerName, priority, exclusionMode, overrideMask });
        }

        private static void InvokeButton(Button button)
        {
            Assert.That(button, Is.Not.Null);
            MethodInfo invoke = button.clickable.GetType().GetMethod(
                "Invoke",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(EventBase) },
                modifiers: null);
            Assert.That(invoke, Is.Not.Null);
            invoke.Invoke(button.clickable, new object[] { null });
        }

        private sealed class RecordingWiringSerializedMapper : IWiringSerializedMapper
        {
            public List<string> LayerPriorityInvocations { get; } = new List<string>();

            public int SetLayerPropertiesCalls { get; private set; }

            public int LastLayerIndex { get; private set; }

            public string LastLayerName { get; private set; } = string.Empty;

            public int LastPriority { get; private set; }

            public ExclusionMode LastExclusionMode { get; private set; }

            public IReadOnlyList<string> LastOverrideMask { get; private set; } = new string[0];

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
                SetLayerPropertiesCalls++;
                LastLayerIndex = layerIndex;
                LastLayerName = layerName;
                LastPriority = priority;
                LastExclusionMode = exclusionMode;
                LastOverrideMask = overrideMask;
                LayerPriorityInvocations.Add($"{layerIndex}:{priority}");
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
