#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows.Routing;
using Hidano.FacialControl.Editor.Windows.Routing.Graph;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing
{
    [TestFixture]
    public class RoutingEditorScaffoldingTests
    {
        [TearDown]
        public void TearDown()
        {
            RoutingEditorWindow[] windows = Resources.FindObjectsOfTypeAll<RoutingEditorWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                Object.DestroyImmediate(windows[i]);
            }
        }

        [Test]
        public void CreateInstance_RoutingEditorWindow_CanBeConstructed()
        {
            var window = ScriptableObject.CreateInstance<RoutingEditorWindow>();

            Assert.That(window, Is.Not.Null);

            Object.DestroyImmediate(window);
        }

        [Test]
        public void Construct_GraphView_SetsStableElementName()
        {
            var graphView = new RoutingGraphView();

            Assert.That(graphView, Is.InstanceOf<VisualElement>());
            Assert.That(graphView.name, Is.EqualTo("routing-graph-view"));
        }

        [Test]
        public void Open_NullProfile_LogsWarningAndDoesNotOpen()
        {
            LogAssert.Expect(
                LogType.Warning,
                "[RoutingEditorWindow] FacialCharacterProfileSO is null or invalid. Window will not open.");

            RoutingEditorWindow window = RoutingEditorWindow.Open(null);

            Assert.That(window, Is.Null);
            Assert.That(CountOpenWindows(), Is.EqualTo(0));
        }

        [Test]
        public void Open_InvalidProfileType_LogsWarningAndDoesNotOpen()
        {
            var unrelatedProfile = ScriptableObject.CreateInstance<UnrelatedScriptableObject>();

            LogAssert.Expect(
                LogType.Warning,
                "[RoutingEditorWindow] FacialCharacterProfileSO is null or invalid. Window will not open.");

            RoutingEditorWindow window = RoutingEditorWindow.Open(unrelatedProfile);

            Assert.That(window, Is.Null);
            Assert.That(CountOpenWindows(), Is.EqualTo(0));

            Object.DestroyImmediate(unrelatedProfile);
        }

        [Test]
        public void Open_SameProfileTwice_FocusesExistingWindowWithoutCreatingDuplicate()
        {
            TestFacialCharacterProfileSO profile = CreateProfile("overlay");

            RoutingEditorWindow first = RoutingEditorWindow.Open(profile);
            first.CreateGUI();
            RoutingEditorWindow second = RoutingEditorWindow.Open(profile);

            Assert.That(second, Is.SameAs(first));
            Assert.That(CountOpenWindows(), Is.EqualTo(1));

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void EditorUpdate_ExternalLayerChange_RebuildsGraph()
        {
            TestFacialCharacterProfileSO profile = CreateProfile("overlay");
            RoutingEditorWindow window = RoutingEditorWindow.Open(profile);
            window.CreateGUI();

            RoutingGraphView graphView = window.rootVisualElement.Q<RoutingGraphView>();
            Assert.That(graphView, Is.Not.Null);
            Assert.That(graphView.LayerNodeViews.Count, Is.EqualTo(1));

            profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
                inputSources = new List<InputSourceDeclarationSerializable>(),
            });
            EditorUtility.SetDirty(profile);

            InvokeEditorUpdate(window);

            graphView = window.rootVisualElement.Q<RoutingGraphView>();
            Assert.That(graphView.LayerNodeViews.Count, Is.EqualTo(2));
            Assert.That(graphView.OutputNodeView.OutputNodeData.OrderedLayers[0].Name, Is.EqualTo("emotion"));

            Object.DestroyImmediate(profile);
        }

        private static TestFacialCharacterProfileSO CreateProfile(string layerName)
        {
            var profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            profile.WritableAdapterBindings.Add(new ULipSyncAdapterBinding());
            profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = layerName,
                priority = 1,
                exclusionMode = ExclusionMode.Blend,
                inputSources = new List<InputSourceDeclarationSerializable>(),
            });
            return profile;
        }

        private static void InvokeEditorUpdate(RoutingEditorWindow window)
        {
            MethodInfo method = typeof(RoutingEditorWindow).GetMethod(
                "HandleEditorUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(window, null);
        }

        private static int CountOpenWindows()
        {
            return Resources.FindObjectsOfTypeAll<RoutingEditorWindow>().Length;
        }

        private sealed class UnrelatedScriptableObject : ScriptableObject
        {
        }
    }
}
#endif
