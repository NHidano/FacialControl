using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Inspector;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector
{
    [TestFixture]
    public class FacialCharacterProfileSOInspectorPhonemeOverlayFoldoutTests
    {
        private const string EmotionLayerName = "Emotion";

        private FacialCharacterProfileSO _so;
        private UnityEditor.Editor _editor;
        private readonly List<AnimationClip> _clips = new List<AnimationClip>();

        [TearDown]
        public void TearDown()
        {
            if (_editor != null)
            {
                // overlay 操作で予約された自動保存 delayCall を解除してから Editor を破棄する。
                CancelPendingAutoSave(_editor);
                Object.DestroyImmediate(_editor);
                _editor = null;
            }

            for (int i = 0; i < _clips.Count; i++)
            {
                if (_clips[i] != null)
                {
                    Object.DestroyImmediate(_clips[i]);
                }
            }
            _clips.Clear();

            if (_so != null)
            {
                Object.DestroyImmediate(_so);
                _so = null;
            }
        }

        [Test]
        public void Foldout_Collapsed_ShowsSummaryWithDeclaredCount()
        {
            _so = CreateProfileWithSlots(PhonemeOverlaySlots.A, PhonemeOverlaySlots.I, "blink");
            _so.Expressions.Add(CreateExpression());

            var foldout = BuildInspectorRoot().Q<Foldout>(
                FacialCharacterProfileSOInspector.ExpressionPhonemeOverlaysFoldoutName);
            var summary = foldout.Q<Label>(
                FacialCharacterProfileSOInspector.ExpressionPhonemeOverlaysSummaryName);

            Assert.That(foldout, Is.Not.Null);
            Assert.That(foldout.value, Is.False);
            Assert.That(summary, Is.Not.Null);
            Assert.That(summary.text, Is.EqualTo("2/5 declared (override=0, suppress=0)"));
        }

        [Test]
        public void Foldout_Expanded_ShowsPerSlotEditorForDeclaredSlots()
        {
            _so = CreateProfileWithSlots(
                PhonemeOverlaySlots.A,
                PhonemeOverlaySlots.I,
                PhonemeOverlaySlots.U,
                PhonemeOverlaySlots.E,
                PhonemeOverlaySlots.O);
            _so.Expressions.Add(CreateExpression());

            var foldout = BuildInspectorRoot().Q<Foldout>(
                FacialCharacterProfileSOInspector.ExpressionPhonemeOverlaysFoldoutName);
            foldout.value = true;

            var dropdowns = new List<DropdownField>();
            var clipFields = new List<ObjectField>();
            foldout.Query<DropdownField>(FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName)
                .ForEach(dropdowns.Add);
            foldout.Query<ObjectField>(FacialCharacterProfileSOInspector.ExpressionOverlayAnimationClipFieldName)
                .ForEach(clipFields.Add);

            Assert.That(dropdowns, Has.Count.EqualTo(5));
            Assert.That(clipFields, Has.Count.EqualTo(5));
        }

        [Test]
        public void Foldout_Expanded_ShowsHelpBoxForUndeclaredSlot()
        {
            _so = CreateProfileWithSlots(PhonemeOverlaySlots.A);
            _so.Expressions.Add(CreateExpression());

            var foldout = BuildInspectorRoot().Q<Foldout>(
                FacialCharacterProfileSOInspector.ExpressionPhonemeOverlaysFoldoutName);
            foldout.value = true;

            var helps = new List<HelpBox>();
            foldout.Query<HelpBox>(FacialCharacterProfileSOInspector.ExpressionPhonemeOverlayUndeclaredSlotHelpName)
                .ForEach(helps.Add);

            Assert.That(helps, Has.Count.EqualTo(4));
            StringAssert.Contains(PhonemeOverlaySlots.I, helps[0].text);
        }

        [Test]
        public void Foldout_SummaryLabel_ReflectsOverrideAndSuppressCounts()
        {
            var clip = CreateClip("PhonemeOverlay_A_Clip", "Mouth_A", 75f);
            _so = CreateProfileWithSlots(
                PhonemeOverlaySlots.A,
                PhonemeOverlaySlots.I,
                PhonemeOverlaySlots.U);
            _so.Expressions.Add(CreateExpression(
                new OverlaySlotBindingSerializable
                {
                    slot = PhonemeOverlaySlots.A,
                    animationClip = clip,
                    cachedSnapshot = CreateCachedSnapshot("Old_A", 1f),
                },
                new OverlaySlotBindingSerializable
                {
                    slot = PhonemeOverlaySlots.I,
                    suppress = true,
                }));

            var summary = BuildInspectorRoot()
                .Q<Label>(FacialCharacterProfileSOInspector.ExpressionPhonemeOverlaysSummaryName);

            Assert.That(summary, Is.Not.Null);
            Assert.That(summary.text, Is.EqualTo("3/5 declared (override=1, suppress=1)"));
        }

        [Test]
        public void Foldout_OnSlotBindingEdit_TriggersCachedSnapshotBake()
        {
            _so = CreateProfileWithSlots(PhonemeOverlaySlots.A);
            _so.Expressions.Add(CreateExpression(new OverlaySlotBindingSerializable
            {
                slot = PhonemeOverlaySlots.A,
                cachedSnapshot = CreateCachedSnapshot("Stale_A", 99f),
            }));
            var clip = new AnimationClip { name = "PhonemeOverlay_A_EmptyClip" };
            _clips.Add(clip);

            var foldout = BuildInspectorRoot().Q<Foldout>(
                FacialCharacterProfileSOInspector.ExpressionPhonemeOverlaysFoldoutName);
            var dropdown = foldout.Q<DropdownField>(
                FacialCharacterProfileSOInspector.ExpressionOverlayStateDropdownName);
            var clipField = foldout.Q<ObjectField>(
                FacialCharacterProfileSOInspector.ExpressionOverlayAnimationClipFieldName);

            dropdown.value = "Override";
            clipField.value = clip;

            var binding = _so.Expressions[0].overlays[0];
            Assert.That(binding.slot, Is.EqualTo(PhonemeOverlaySlots.A));
            Assert.That(binding.cachedSnapshot, Is.Not.Null);
            Assert.That(IsEmptySnapshot(binding.cachedSnapshot), Is.True);
        }

        private VisualElement BuildInspectorRoot()
        {
            _editor = UnityEditor.Editor.CreateEditor(_so, typeof(FacialCharacterProfileSOInspector));
            Assert.That(_editor, Is.Not.Null);
            return _editor.CreateInspectorGUI();
        }

        private FacialCharacterProfileSO CreateProfileWithSlots(params string[] slots)
        {
            var so = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            so.name = "FacialCharacterProfileSOInspectorPhonemeOverlayFoldoutTests";
            so.Layers.Add(new LayerDefinitionSerializable
            {
                name = EmotionLayerName,
                priority = 0,
            });
            SetSlots(so, slots);
            return so;
        }

        private static ExpressionSerializable CreateExpression(params OverlaySlotBindingSerializable[] overlays)
        {
            return new ExpressionSerializable
            {
                id = "smile",
                name = "Smile",
                layer = EmotionLayerName,
                isGaze = false,
                overlays = overlays != null
                    ? new List<OverlaySlotBindingSerializable>(overlays)
                    : new List<OverlaySlotBindingSerializable>(),
            };
        }

        private AnimationClip CreateClip(string clipName, string blendShapeName, float value)
        {
            var clip = new AnimationClip { name = clipName };
            var binding = EditorCurveBinding.FloatCurve(
                "Face",
                typeof(SkinnedMeshRenderer),
                "blendShape." + blendShapeName);
            AnimationUtility.SetEditorCurve(
                clip,
                binding,
                AnimationCurve.Constant(0f, 0.1f, value));
            _clips.Add(clip);
            return clip;
        }

        private static OverlaySnapshotDto CreateCachedSnapshot(string blendShapeName, float value)
        {
            return new OverlaySnapshotDto
            {
                blendShapes = new List<BlendShapeSnapshotDto>
                {
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = "Face",
                        name = blendShapeName,
                        value = value,
                    },
                },
                bones = new List<BoneSnapshotDto>(),
                rendererPaths = new List<string> { "Face" },
            };
        }

        private static bool IsEmptySnapshot(OverlaySnapshotDto snapshot)
        {
            if (snapshot == null) return true;
            bool hasBlendShapes = snapshot.blendShapes != null && snapshot.blendShapes.Count > 0;
            bool hasBones = snapshot.bones != null && snapshot.bones.Count > 0;
            bool hasRendererPaths = snapshot.rendererPaths != null && snapshot.rendererPaths.Count > 0;
            return !hasBlendShapes && !hasBones && !hasRendererPaths;
        }

        private static void CancelPendingAutoSave(UnityEditor.Editor editor)
        {
            if (editor == null) return;

            const BindingFlags instanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
            var flush = typeof(FacialCharacterProfileSOInspector)
                .GetMethod("FlushAutoSave", instanceNonPublic);
            if (flush != null)
            {
                var del = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(
                    typeof(EditorApplication.CallbackFunction), editor, flush);
                EditorApplication.delayCall -= del;
            }

            var pending = typeof(FacialCharacterProfileSOInspector)
                .GetField("_autoSavePending", instanceNonPublic);
            pending?.SetValue(editor, false);
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
    }
}
