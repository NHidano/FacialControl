using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Rec.Adapters.Playable;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Editor.Inspector;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    public class RecCharacterBindingInspectorTests
    {
        private GameObject _host;
        private UnityEditor.Editor _editor;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("RecCharacterBindingInspectorTestsHost");
            _host.AddComponent<Animator>();
            _host.AddComponent<FacialController>();
            _host.AddComponent<RecCharacterBinding>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_editor != null)
            {
                Object.DestroyImmediate(_editor);
                _editor = null;
            }

            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void CreateInspectorGUI_BuildsControlsAndStateLabels()
        {
            VisualElement root = BuildInspectorRoot();

            Assert.That(root.Q<TextField>(RecCharacterBindingInspector.RecordingNameFieldName), Is.Not.Null);
            Assert.That(root.Q<Button>(RecCharacterBindingInspector.StartRecordingButtonName), Is.Not.Null);
            Assert.That(root.Q<Button>(RecCharacterBindingInspector.StopRecordingButtonName), Is.Not.Null);
            Assert.That(root.Q<Button>(RecCharacterBindingInspector.LoadRecordingButtonName), Is.Not.Null);
            Assert.That(root.Q<Button>(RecCharacterBindingInspector.StartPlaybackButtonName), Is.Not.Null);
            Assert.That(root.Q<Button>(RecCharacterBindingInspector.StopPlaybackButtonName), Is.Not.Null);
            Assert.That(root.Q<Label>(RecCharacterBindingInspector.RecordingStateLabelName), Is.Not.Null);
            Assert.That(root.Q<Label>(RecCharacterBindingInspector.PlaybackStateLabelName), Is.Not.Null);
            Assert.That(root.Q<Label>(RecCharacterBindingInspector.ElapsedSecondsLabelName), Is.Not.Null);
            Assert.That(root.Q<Label>(RecCharacterBindingInspector.PathLabelName), Is.Not.Null);
        }

        [Test]
        public void CreateInspectorGUI_WhenNotPlaying_ShowsEditModeHelpAndDisablesButtons()
        {
            VisualElement root = BuildInspectorRoot();

            var helpBox = root.Q<HelpBox>(RecCharacterBindingInspector.EditModeHelpBoxName);
            Assert.That(helpBox, Is.Not.Null);
            Assert.That(helpBox.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            AssertButtonEnabled(root, RecCharacterBindingInspector.StartRecordingButtonName, false);
            AssertButtonEnabled(root, RecCharacterBindingInspector.StopRecordingButtonName, false);
            AssertButtonEnabled(root, RecCharacterBindingInspector.LoadRecordingButtonName, false);
            AssertButtonEnabled(root, RecCharacterBindingInspector.StartPlaybackButtonName, false);
            AssertButtonEnabled(root, RecCharacterBindingInspector.StopPlaybackButtonName, false);
        }

        [Test]
        public void CreateInspectorGUI_WhenIdle_ShowsFacadeState()
        {
            VisualElement root = BuildInspectorRoot();

            var recordingLabel = root.Q<Label>(RecCharacterBindingInspector.RecordingStateLabelName);
            var playbackLabel = root.Q<Label>(RecCharacterBindingInspector.PlaybackStateLabelName);
            var elapsedLabel = root.Q<Label>(RecCharacterBindingInspector.ElapsedSecondsLabelName);
            var pathLabel = root.Q<Label>(RecCharacterBindingInspector.PathLabelName);

            Assert.That(recordingLabel.text, Is.EqualTo(string.Format(RecCharacterBindingInspector.RecordingStateLabelFormat, false)));
            Assert.That(playbackLabel.text, Is.EqualTo(string.Format(RecCharacterBindingInspector.PlaybackStateLabelFormat, RecPlaybackState.Idle)));
            Assert.That(elapsedLabel.text, Is.EqualTo(string.Format(RecCharacterBindingInspector.ElapsedSecondsLabelFormat, 0d)));
            Assert.That(pathLabel.text, Is.EqualTo(string.Format(RecCharacterBindingInspector.PathLabelFormat, RecCharacterBindingInspector.EmptyPathText)));
        }

        private VisualElement BuildInspectorRoot()
        {
            var binding = _host.GetComponent<RecCharacterBinding>();
            _editor = UnityEditor.Editor.CreateEditor(binding, typeof(RecCharacterBindingInspector));
            Assert.That(_editor, Is.Not.Null);

            VisualElement root = _editor.CreateInspectorGUI();
            Assert.That(root, Is.Not.Null);
            return root;
        }

        private static void AssertButtonEnabled(VisualElement root, string name, bool expected)
        {
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null, $"Missing button '{name}'.");
            Assert.That(button.enabledSelf, Is.EqualTo(expected), $"Button '{name}' enabled state mismatch.");
        }
    }
}
