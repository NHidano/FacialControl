using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.InputSystem.Adapters.ScriptableObject;
using Hidano.FacialControl.InputSystem.Editor.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using InputBindingMode = Hidano.FacialControl.InputSystem.Adapters.ScriptableObject.BindingMode;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.InputSystem.Tests.EditMode.Adapters.AdapterBindings
{
    [TestFixture]
    public class InputSystemAdapterBindingDrawerTests
    {
        private const string BlinkSlotName = "blink";
        private const string WinkSlotName = "wink";
        private const string SparkleSlotName = "sparkle";

        private TestProfileSO _profileSo;
        private ScriptableObject _hostSo;
        private SerializedObject _serializedObject;

        [TearDown]
        public void TearDown()
        {
            _serializedObject?.Dispose();
            _serializedObject = null;

            if (_hostSo != null)
            {
                Object.DestroyImmediate(_hostSo);
                _hostSo = null;
            }

            if (_profileSo != null)
            {
                Object.DestroyImmediate(_profileSo);
                _profileSo = null;
            }
        }

        [Test]
        public void CreatePropertyGUI_ExpressionBindingsList_ShowsAlternatingRowBackgrounds()
        {
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            _profileSo.WritableAdapterBindings.Add(CreateBinding(BlinkSlotName));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            var root = new InputSystemAdapterBindingDrawer().CreatePropertyGUI(bindingProperty);
            var listView = root.Q<ListView>(InputSystemAdapterBindingDrawer.ExpressionBindingsListName);

            Assert.That(listView, Is.Not.Null);
            Assert.That(
                listView.showAlternatingRowBackgrounds,
                Is.EqualTo(AlternatingRowBackground.ContentOnly),
                "各キーバインディング行の境界を視認できるよう交互背景を表示する必要があります。");
        }

        [Test]
        public void CreatePropertyGUI_SavedCollapsedFoldoutState_IsRestored()
        {
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            _profileSo.WritableAdapterBindings.Add(CreateBinding(BlinkSlotName));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);
            SerializedProperty listProperty = bindingProperty.FindPropertyRelative("_expressionBindings");
            string key = ListViewFoldoutStatePersistence.GetSessionStateKey(listProperty);
            try
            {
                SessionState.SetBool(key, false);

                var root = new InputSystemAdapterBindingDrawer().CreatePropertyGUI(bindingProperty);
                var listView = root.Q<ListView>(InputSystemAdapterBindingDrawer.ExpressionBindingsListName);
                var foldout = listView.Q<Foldout>(className: BaseListView.foldoutHeaderUssClassName);

                Assert.That(foldout, Is.Not.Null);
                Assert.That(foldout.value, Is.False,
                    "キーバインディングリストの折りたたみ状態が SessionState から復元される必要があります。");
            }
            finally
            {
                SessionState.EraseBool(key);
            }
        }

        [Test]
        public void BindExpressionBindingRow_OverlaySlot_RendersDropdownFromProfileSlots()
        {
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            SetSlots(_profileSo, BlinkSlotName, WinkSlotName);
            _profileSo.WritableAdapterBindings.Add(CreateBinding(BlinkSlotName));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            var row = new VisualElement();
            InvokeBindExpressionBindingRow(row, 0, bindingProperty);

            var dropdown = row.Q<DropdownField>(InputSystemAdapterBindingDrawer.OverlaySlotDropdownName);
            var help = row.Q<HelpBox>(InputSystemAdapterBindingDrawer.OverlaySlotHelpName);
            Assert.That(dropdown, Is.Not.Null, "overlaySlot は PropertyField ではなく DropdownField として描画されるべき。");
            Assert.That(dropdown.choices, Is.EqualTo(new[] { string.Empty, BlinkSlotName, WinkSlotName }));
            Assert.That(dropdown.value, Is.EqualTo(BlinkSlotName));
            Assert.That(dropdown.enabledSelf, Is.True);

            SetSlots(_profileSo, BlinkSlotName, WinkSlotName, SparkleSlotName);
            InvokeRefreshOverlaySlotChoices(
                dropdown,
                help,
                bindingProperty,
                FindOverlaySlotProperty(bindingProperty));

            Assert.That(dropdown.choices, Is.EqualTo(new[] { string.Empty, BlinkSlotName, WinkSlotName, SparkleSlotName }));
        }

        [Test]
        public void TickOverlaySlotRefresh_SerializedObjectDisposed_ReturnsFalseWithoutError()
        {
            // 回帰テスト: FacialCharacterProfileSO 編集後に Inspector を別オブジェクトへ切り替えると、
            // 破棄済み SerializedObject に対して定期リフレッシュが so.Update() を呼び
            // NullReferenceException が大量発生していた。破棄後は false を返して停止すべき。
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            SetSlots(_profileSo, BlinkSlotName);
            _profileSo.WritableAdapterBindings.Add(CreateBinding(BlinkSlotName));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            var row = new VisualElement();
            InvokeBindExpressionBindingRow(row, 0, bindingProperty);
            var dropdown = row.Q<DropdownField>(InputSystemAdapterBindingDrawer.OverlaySlotDropdownName);
            var help = row.Q<HelpBox>(InputSystemAdapterBindingDrawer.OverlaySlotHelpName);

            _serializedObject.Dispose();

            bool result = true;
            Assert.DoesNotThrow(
                () => result = InvokeTickOverlaySlotRefresh(dropdown, help, bindingProperty, 0),
                "SerializedObject 破棄後のティックは例外を出さずに停止判定を返すべき。");
            Assert.That(result, Is.False, "破棄後のティックは false（タイマー停止）を返すべき。");
        }

        [Test]
        public void TickOverlaySlotRefresh_ValidSerializedObject_ReturnsTrueAndRefreshesChoices()
        {
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            SetSlots(_profileSo, BlinkSlotName);
            _profileSo.WritableAdapterBindings.Add(CreateBinding(BlinkSlotName));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            var row = new VisualElement();
            InvokeBindExpressionBindingRow(row, 0, bindingProperty);
            var dropdown = row.Q<DropdownField>(InputSystemAdapterBindingDrawer.OverlaySlotDropdownName);
            var help = row.Q<HelpBox>(InputSystemAdapterBindingDrawer.OverlaySlotHelpName);

            SetSlots(_profileSo, BlinkSlotName, WinkSlotName);
            bool result = InvokeTickOverlaySlotRefresh(dropdown, help, bindingProperty, 0);

            Assert.That(result, Is.True, "有効な SerializedObject に対するティックは true（継続）を返すべき。");
            Assert.That(dropdown.choices, Is.EqualTo(new[] { string.Empty, BlinkSlotName, WinkSlotName }));
        }

        [Test]
        public void BindExpressionBindingRow_WhenProfileSoIsUnavailable_DisablesOverlaySlotDropdownAndShowsHelp()
        {
            var host = ScriptableObject.CreateInstance<NonProfileBindingHost>();
            _hostSo = host;
            host.binding = CreateBinding(BlinkSlotName);
            SerializedProperty bindingProperty = CreateHostBindingProperty(host);

            var row = new VisualElement();
            InvokeBindExpressionBindingRow(row, 0, bindingProperty);

            var dropdown = row.Q<DropdownField>(InputSystemAdapterBindingDrawer.OverlaySlotDropdownName);
            var help = row.Q<HelpBox>(InputSystemAdapterBindingDrawer.OverlaySlotHelpName);

            Assert.That(dropdown, Is.Not.Null);
            Assert.That(dropdown.enabledSelf, Is.False);
            Assert.That(dropdown.value, Is.EqualTo(BlinkSlotName));
            Assert.That(help, Is.Not.Null);
            Assert.That(help.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            StringAssert.Contains("FacialCharacterProfileSO", help.text);
            StringAssert.Contains("Slots", help.text);
        }

        [Test]
        public void BindExpressionBindingRow_PreservesNewFieldOrder()
        {
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            _profileSo.WritableAdapterBindings.Add(CreateBinding(InputBindingMode.Gaze, useDistinctLeftRight: true));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            var row = new VisualElement();
            InvokeBindExpressionBindingRow(row, 0, bindingProperty);

            string[] orderedFieldNames = row.Children()
                .Select(child => child.name)
                .Where(IsExpressionBindingPrimaryFieldName)
                .ToArray();

            Assert.That(orderedFieldNames, Is.EqualTo(new[]
            {
                InputSystemAdapterBindingDrawer.ExpressionDropdownName,
                InputSystemAdapterBindingDrawer.BindingModeFieldName,
                InputSystemAdapterBindingDrawer.GazeUseDistinctToggleName,
                InputSystemAdapterBindingDrawer.GazeLeftActionDropdownName,
                InputSystemAdapterBindingDrawer.GazeRightActionDropdownName,
                InputSystemAdapterBindingDrawer.ActionDropdownName,
                InputSystemAdapterBindingDrawer.TriggerModeFieldName,
            }));
        }

        [Test]
        public void UpdateGazeActionFieldState_GazeAndUseDistinct_DisablesActionFieldWithoutHiding()
        {
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            _profileSo.WritableAdapterBindings.Add(CreateBinding(InputBindingMode.Gaze, useDistinctLeftRight: true));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            var row = new VisualElement();
            InvokeBindExpressionBindingRow(row, 0, bindingProperty);

            var actionField = row.Q<DropdownField>(InputSystemAdapterBindingDrawer.ActionDropdownName);
            Assert.That(actionField, Is.Not.Null);
            Assert.That(actionField.enabledSelf, Is.False);
            Assert.That(actionField.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void UpdateGazeActionFieldState_GazeWithoutUseDistinct_EnablesActionField()
        {
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            _profileSo.WritableAdapterBindings.Add(CreateBinding(InputBindingMode.Gaze, useDistinctLeftRight: false));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            var row = new VisualElement();
            InvokeBindExpressionBindingRow(row, 0, bindingProperty);

            var actionField = row.Q<DropdownField>(InputSystemAdapterBindingDrawer.ActionDropdownName);
            Assert.That(actionField, Is.Not.Null);
            Assert.That(actionField.enabledSelf, Is.True);
            Assert.That(actionField.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        private SerializedProperty CreateProfileBindingProperty(TestProfileSO so)
        {
            _serializedObject?.Dispose();
            _serializedObject = new SerializedObject(so);
            _serializedObject.Update();
            SerializedProperty list = _serializedObject.FindProperty("_adapterBindings");
            Assert.That(list, Is.Not.Null);
            Assert.That(list.arraySize, Is.EqualTo(1));
            return list.GetArrayElementAtIndex(0);
        }

        private SerializedProperty CreateHostBindingProperty(NonProfileBindingHost host)
        {
            _serializedObject?.Dispose();
            _serializedObject = new SerializedObject(host);
            _serializedObject.Update();
            SerializedProperty property = _serializedObject.FindProperty("binding");
            Assert.That(property, Is.Not.Null);
            return property;
        }

        private static InputSystemAdapterBinding CreateBinding(string overlaySlot)
        {
            var binding = new InputSystemAdapterBinding();
            binding.Configure(
                asset: null,
                actionMapName: "Expression",
                expressionBindings: new[]
                {
                    new ExpressionBindingEntry
                    {
                        bindingMode = InputBindingMode.Overlay,
                        actionName = "RightTrigger",
                        overlaySlot = overlaySlot,
                        overlayTargetLayer = "overlay",
                    },
                });
            return binding;
        }

        private static InputSystemAdapterBinding CreateBinding(
            InputBindingMode bindingMode,
            bool useDistinctLeftRight)
        {
            var binding = new InputSystemAdapterBinding();
            binding.Configure(
                asset: null,
                actionMapName: "Expression",
                expressionBindings: new[]
                {
                    new ExpressionBindingEntry
                    {
                        bindingMode = bindingMode,
                        actionName = "Gaze",
                        useDistinctLeftRight = useDistinctLeftRight,
                        actionNameLeft = "GazeLeft",
                        actionNameRight = "GazeRight",
                        expressionId = "look",
                    },
                });
            return binding;
        }

        private static bool IsExpressionBindingPrimaryFieldName(string name)
        {
            return name == InputSystemAdapterBindingDrawer.ExpressionDropdownName
                || name == InputSystemAdapterBindingDrawer.BindingModeFieldName
                || name == InputSystemAdapterBindingDrawer.GazeUseDistinctToggleName
                || name == InputSystemAdapterBindingDrawer.GazeLeftActionDropdownName
                || name == InputSystemAdapterBindingDrawer.GazeRightActionDropdownName
                || name == InputSystemAdapterBindingDrawer.ActionDropdownName
                || name == InputSystemAdapterBindingDrawer.TriggerModeFieldName;
        }

        private static void InvokeBindExpressionBindingRow(
            VisualElement row,
            int index,
            SerializedProperty bindingProperty)
        {
            MethodInfo method = typeof(InputSystemAdapterBindingDrawer).GetMethod(
                "BindExpressionBindingRow",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { row, index, bindingProperty });
        }

        private static void InvokeRefreshOverlaySlotChoices(
            DropdownField dropdown,
            HelpBox help,
            SerializedProperty bindingProperty,
            SerializedProperty overlaySlotProperty)
        {
            MethodInfo method = typeof(InputSystemAdapterBindingDrawer).GetMethod(
                "RefreshOverlaySlotChoices",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { dropdown, help, bindingProperty, overlaySlotProperty });
        }

        private static bool InvokeTickOverlaySlotRefresh(
            DropdownField dropdown,
            HelpBox help,
            SerializedProperty bindingProperty,
            int index)
        {
            MethodInfo method = typeof(InputSystemAdapterBindingDrawer).GetMethod(
                "TickOverlaySlotRefresh",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null,
                "定期リフレッシュの本体は TickOverlaySlotRefresh として切り出されているべき。");
            try
            {
                return (bool)method.Invoke(null, new object[] { dropdown, help, bindingProperty, index });
            }
            catch (TargetInvocationException ex)
            {
                // 呼び出し先の例外をそのままテストへ伝搬させる。
                throw ex.InnerException ?? ex;
            }
        }

        private static SerializedProperty FindOverlaySlotProperty(SerializedProperty bindingProperty)
        {
            SerializedProperty list = bindingProperty.FindPropertyRelative("_expressionBindings");
            Assert.That(list, Is.Not.Null);
            Assert.That(list.arraySize, Is.EqualTo(1));
            SerializedProperty slot = list.GetArrayElementAtIndex(0).FindPropertyRelative("overlaySlot");
            Assert.That(slot, Is.Not.Null);
            return slot;
        }

        private static void SetSlots(FacialCharacterProfileSO so, params string[] slots)
        {
            FieldInfo field = typeof(FacialCharacterProfileSO).GetField(
                "_slots",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(so, new List<string>(slots));
        }

        private sealed class TestProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
        }

        private sealed class NonProfileBindingHost : ScriptableObject
        {
            [SerializeReference] public InputSystemAdapterBinding binding;
        }
    }
}
