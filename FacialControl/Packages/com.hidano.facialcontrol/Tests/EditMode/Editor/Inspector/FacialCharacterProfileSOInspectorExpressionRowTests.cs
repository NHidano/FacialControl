using System.Reflection;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Inspector;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    [TestFixture]
    public class FacialCharacterProfileSOInspectorExpressionRowTests
    {
        private const string BaseLayerName = "base";

        private FacialCharacterProfileSO _so;
        private UnityEditor.Editor _editor;

        [TearDown]
        public void TearDown()
        {
            if (_editor != null)
            {
                Object.DestroyImmediate(_editor);
                _editor = null;
            }

            if (_so != null)
            {
                Object.DestroyImmediate(_so);
                _so = null;
            }
        }

        [Test]
        public void BuildAnimationClipFields_IsGazeTrue_HidesClipField()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression(isGaze: true));

            var root = BuildInspectorRoot();
            var clipField = FindExpressionClipField(root);

            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void BuildAnimationClipFields_IsGazeFalse_ShowsClipField()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression(isGaze: false));

            var root = BuildInspectorRoot();
            var clipField = FindExpressionClipField(root);

            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void ToggleIsGaze_UpdatesClipFieldVisibilityImmediately()
        {
            _so = CreateProfile();
            _so.Expressions.Add(CreateExpression(isGaze: false));

            var root = BuildInspectorRoot();
            InvokeChangeExpressionIsGaze(0, true);
            var clipField = FindExpressionClipField(root);

            Assert.That(_so.Expressions[0].isGaze, Is.True);
            Assert.That(clipField.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        private VisualElement BuildInspectorRoot()
        {
            _editor = UnityEditor.Editor.CreateEditor(_so, typeof(FacialCharacterProfileSOInspector));
            Assert.That(_editor, Is.Not.Null);
            return _editor.CreateInspectorGUI();
        }

        private void InvokeChangeExpressionIsGaze(int index, bool isGaze)
        {
            var method = typeof(FacialCharacterProfileSOInspector).GetMethod(
                "ChangeExpressionIsGaze",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_editor, new object[] { index, isGaze });
        }

        private static ObjectField FindExpressionClipField(VisualElement root)
        {
            var clipField = root.Q<ObjectField>(FacialCharacterProfileSOInspector.ExpressionRowClipFieldName);
            Assert.That(clipField, Is.Not.Null);
            Assert.That(clipField.objectType, Is.EqualTo(typeof(AnimationClip)));
            Assert.That(clipField.allowSceneObjects, Is.False);
            return clipField;
        }

        private static FacialCharacterProfileSO CreateProfile()
        {
            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            so.name = "ExpressionRowInspectorTest";
            so.Layers.Add(new LayerDefinitionSerializable
            {
                name = BaseLayerName,
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
            });
            return so;
        }

        private static ExpressionSerializable CreateExpression(bool isGaze)
        {
            return new ExpressionSerializable
            {
                id = isGaze ? "look" : "smile",
                name = isGaze ? "Look" : "Smile",
                layer = BaseLayerName,
                isGaze = isGaze,
            };
        }
    }
}
