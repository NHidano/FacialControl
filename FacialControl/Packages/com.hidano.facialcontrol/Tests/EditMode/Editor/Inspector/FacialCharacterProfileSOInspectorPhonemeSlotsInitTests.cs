using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Inspector;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    [TestFixture]
    public class FacialCharacterProfileSOInspectorPhonemeSlotsInitTests
    {
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
        public void Click_PhonemeSlotsInitButton_AddsMissingReservedSlots()
        {
            _so = CreateProfileWithSlots("custom");

            ClickInitButton(BuildInspectorRoot());

            Assert.That(GetSlots(), Is.EqualTo(new[]
            {
                "custom",
                PhonemeOverlaySlots.A,
                PhonemeOverlaySlots.I,
                PhonemeOverlaySlots.U,
                PhonemeOverlaySlots.E,
                PhonemeOverlaySlots.O,
            }));
        }

        [Test]
        public void Click_PhonemeSlotsInitButton_DoesNotDuplicateExistingSlots()
        {
            _so = CreateProfileWithSlots(PhonemeOverlaySlots.A, "custom", PhonemeOverlaySlots.O);

            ClickInitButton(BuildInspectorRoot());

            var slots = GetSlots();
            Assert.That(Count(slots, PhonemeOverlaySlots.A), Is.EqualTo(1));
            Assert.That(Count(slots, PhonemeOverlaySlots.I), Is.EqualTo(1));
            Assert.That(Count(slots, PhonemeOverlaySlots.U), Is.EqualTo(1));
            Assert.That(Count(slots, PhonemeOverlaySlots.E), Is.EqualTo(1));
            Assert.That(Count(slots, PhonemeOverlaySlots.O), Is.EqualTo(1));
        }

        [Test]
        public void Click_PhonemeSlotsInitButton_DoesNotRemoveCustomSlots()
        {
            _so = CreateProfileWithSlots("blink", "mouth");

            ClickInitButton(BuildInspectorRoot());

            var slots = GetSlots();
            Assert.That(slots, Does.Contain("blink"));
            Assert.That(slots, Does.Contain("mouth"));
        }

        private VisualElement BuildInspectorRoot()
        {
            _editor = UnityEditor.Editor.CreateEditor(_so, typeof(FacialCharacterProfileSOInspector));
            Assert.That(_editor, Is.Not.Null);
            return _editor.CreateInspectorGUI();
        }

        private static void ClickInitButton(VisualElement root)
        {
            var button = root.Q<Button>(FacialCharacterProfileSOInspector.SlotsInitPhonemeButtonName);
            Assert.That(button, Is.Not.Null);
            Assert.That(button.text, Is.EqualTo("Phoneme slots を初期化 (a/i/u/e/o)"));

            var invoke = button.clickable.GetType().GetMethod(
                "Invoke",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(EventBase) },
                modifiers: null);
            Assert.That(invoke, Is.Not.Null, "Button.clickable.Invoke(EventBase) が見つかりません。");
            invoke.Invoke(button.clickable, new object[] { null });
        }

        private static FacialCharacterProfileSO CreateProfileWithSlots(params string[] slots)
        {
            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            so.name = "FacialCharacterProfileSOInspectorPhonemeSlotsInitTests";
            so.Layers.Add(new LayerDefinitionSerializable
            {
                name = "Base",
                priority = 0,
            });
            SetSlots(so, slots);
            return so;
        }

        private static void SetSlots(FacialCharacterProfileSO so, params string[] slots)
        {
            var serialized = new SerializedObject(so);
            serialized.Update();
            var slotsProperty = serialized.FindProperty("_slots");
            Assert.That(slotsProperty, Is.Not.Null);
            slotsProperty.ClearArray();

            for (int i = 0; i < slots.Length; i++)
            {
                slotsProperty.InsertArrayElementAtIndex(i);
                slotsProperty.GetArrayElementAtIndex(i).stringValue = slots[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private List<string> GetSlots()
        {
            return new List<string>(_so.Slots);
        }

        private static int Count(List<string> slots, string slot)
        {
            int count = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (string.Equals(slots[i], slot, System.StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
