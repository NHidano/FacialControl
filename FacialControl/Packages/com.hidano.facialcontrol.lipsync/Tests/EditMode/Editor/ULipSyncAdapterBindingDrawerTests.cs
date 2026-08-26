using System;
using System.Reflection;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Editor.Inspector;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Editor
{
    public class ULipSyncAdapterBindingDrawerTests
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_ULipSyncAdapterBindingDrawerTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private ULipSyncAdapterBindingDrawerTestAsset _asset;
        private SerializedObject _serializedObject;
        private SerializedProperty _bindingProperty;
        private string _assetPath;
        private FakePlayerPrefsBackend _backend;

        [SetUp]
        public void SetUp()
        {
            _backend = new FakePlayerPrefsBackend();
            LipSyncDeviceStore.SetBackend(_backend);

            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/ULipSyncAdapterBindingDrawerTests_"
                + Guid.NewGuid().ToString("N") + ".asset";
            _asset = ScriptableObject.CreateInstance<ULipSyncAdapterBindingDrawerTestAsset>();
            _asset.Binding = new ULipSyncAdapterBinding();
            _serializedObject = new SerializedObject(_asset);
            _bindingProperty = _serializedObject.FindProperty(nameof(ULipSyncAdapterBindingDrawerTestAsset.Binding));
        }

        [TearDown]
        public void TearDown()
        {
            _serializedObject?.Dispose();

            if (!string.IsNullOrEmpty(_assetPath))
            {
                AssetDatabase.DeleteAsset(_assetPath);
                _assetPath = null;
            }

            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                string[] remaining = AssetDatabase.FindAssets(string.Empty, new[] { TempFolderPath });
                if (remaining == null || remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(TempFolderPath);
                }
            }

            if (_asset != null)
            {
                UnityEngine.Object.DestroyImmediate(_asset);
                _asset = null;
            }

            LipSyncDeviceStore.ResetBackend();
            _backend = null;
            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyName);
            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyDisambiguator);
        }

        [Test]
        public void CreatePropertyGUI_BindingProperty_RendersIntegratedSections()
        {
            VisualElement root = CreateDrawerRoot();

            Assert.That(root.ClassListContains(ULipSyncAdapterBindingDrawer.RootClassName), Is.True);
            Assert.That(
                root.Q<AdapterBindingSlugField>(ULipSyncAdapterBindingDrawer.SlugPropertyFieldName),
                Is.Not.Null);
            Assert.That(root.Q<DeviceDescriptorPopup>(), Is.Not.Null);
            Assert.That(
                root.Q<HelpBox>(ULipSyncAdapterBindingDrawer.DeviceDescriptorHelpBoxName),
                Is.Not.Null);
            Assert.That(
                root.Q<ObjectField>(ULipSyncAdapterBindingDrawer.AnalyzerProfileObjectFieldName),
                Is.Not.Null);
            Assert.That(root.Q<PhonemeEntryListView>(), Is.Not.Null);
            Assert.That(
                root.Q<FloatField>(ULipSyncAdapterBindingDrawer.MaxWeightScaleFieldName),
                Is.Not.Null);
        }

        [Test]
        public void AnalyzerProfile_Null_ShowsPackagedDefaultPlaceholder()
        {
            VisualElement root = CreateDrawerRoot();

            var placeholder = root.Q<HelpBox>(
                ULipSyncAdapterBindingDrawer.DefaultAnalyzerProfilePlaceholderName);
            Assert.That(placeholder, Is.Not.Null);
            Assert.That(placeholder.text, Does.Contain("パッケージ同梱既定"));
            Assert.That(placeholder.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            Assert.That(
                root.Q<ObjectField>(ULipSyncAdapterBindingDrawer.AnalyzerProfileObjectFieldName).objectType,
                Is.EqualTo(typeof(uLipSync.Profile)));
        }

        [Test]
        public void DeviceDescriptorPopup_InitialState_LoadsFromDeviceStore()
        {
            _backend.SetString(LipSyncDeviceStore.KeyName, "Preset Mic");
            _backend.SetInt(LipSyncDeviceStore.KeyDisambiguator, 2);

            VisualElement root = CreateDrawerRoot();
            var popup = root.Q<DeviceDescriptorPopup>();

            Assert.That(popup, Is.Not.Null);
            Assert.That(popup.CurrentDescriptor.DeviceName, Is.EqualTo("Preset Mic"));
            Assert.That(popup.CurrentDescriptor.DisambiguatorIndex, Is.EqualTo(2));
        }

        [Test]
        public void DeviceDescriptorPopup_DeviceNameMissing_ShowsHelpBox()
        {
            VisualElement root = CreateDrawerRoot();

            var helpBox = root.Q<HelpBox>(ULipSyncAdapterBindingDrawer.DeviceDescriptorHelpBoxName);

            Assert.That(helpBox, Is.Not.Null);
            Assert.That(helpBox.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void DeviceDescriptorPopup_DeviceNamePresent_HidesHelpBox()
        {
            _backend.SetString(LipSyncDeviceStore.KeyName, "Active Mic");

            VisualElement root = CreateDrawerRoot();

            var helpBox = root.Q<HelpBox>(ULipSyncAdapterBindingDrawer.DeviceDescriptorHelpBoxName);

            Assert.That(helpBox, Is.Not.Null);
            Assert.That(helpBox.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void DeviceDescriptorPopup_ManualOverrideChanged_PersistsToDeviceStore()
        {
            VisualElement root = CreateDrawerRoot();
            var popup = root.Q<DeviceDescriptorPopup>();

            InvokePrivate(popup, "ApplyDeviceNameFromManualOverride", "Disconnected Mic");

            Assert.That(_backend.GetString(LipSyncDeviceStore.KeyName, string.Empty),
                Is.EqualTo("Disconnected Mic"));
            Assert.That(_backend.SaveCallCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void DeviceDescriptorPopup_DisambiguatorIndexChanged_PersistsToDeviceStore()
        {
            VisualElement root = CreateDrawerRoot();
            var popup = root.Q<DeviceDescriptorPopup>();

            InvokePrivate(popup, "ApplyDisambiguatorIndex", 5);

            Assert.That(_backend.GetInt(LipSyncDeviceStore.KeyDisambiguator, 0), Is.EqualTo(5));
            Assert.That(_backend.SaveCallCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void EditedFields_ApplyModifiedProperties_RoundTripsSerializedValues()
        {
            VisualElement root = CreateDrawerRoot();

            var phonemeEntries = root.Q<PhonemeEntryListView>();

            InvokePrivateStatic(
                typeof(ULipSyncAdapterBindingDrawer),
                "SetFloat",
                _bindingProperty,
                "_maxWeightScale",
                1.5f);
            phonemeEntries.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);

            _serializedObject.Update();
            SerializedProperty entries = _bindingProperty.FindPropertyRelative("_phonemeEntries");

            Assert.That(
                _bindingProperty.FindPropertyRelative("_maxWeightScale").floatValue,
                Is.EqualTo(1.5f).Within(1e-6f));
            Assert.That(entries.arraySize, Is.EqualTo(1));
            Assert.That(entries.GetArrayElementAtIndex(0).managedReferenceValue,
                Is.InstanceOf<BlendShapePhonemeEntry>());

            AssetDatabase.CreateAsset(_asset, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(_asset);
            _asset = null;

            var loaded = AssetDatabase.LoadAssetAtPath<ULipSyncAdapterBindingDrawerTestAsset>(_assetPath);
            Assert.That(loaded, Is.Not.Null);

            using (var serialized = new SerializedObject(loaded))
            {
                SerializedProperty loadedBinding =
                    serialized.FindProperty(nameof(ULipSyncAdapterBindingDrawerTestAsset.Binding));
                SerializedProperty loadedEntries =
                    loadedBinding.FindPropertyRelative("_phonemeEntries");

                Assert.That(
                    loadedBinding.FindPropertyRelative("_maxWeightScale").floatValue,
                    Is.EqualTo(1.5f).Within(1e-6f));
                Assert.That(loadedEntries.arraySize, Is.EqualTo(1));
                Assert.That(loadedEntries.GetArrayElementAtIndex(0).managedReferenceValue,
                    Is.InstanceOf<BlendShapePhonemeEntry>());
            }
        }

        private VisualElement CreateDrawerRoot()
        {
            _serializedObject.Update();
            _bindingProperty =
                _serializedObject.FindProperty(nameof(ULipSyncAdapterBindingDrawerTestAsset.Binding));
            var drawer = new ULipSyncAdapterBindingDrawer();
            return drawer.CreatePropertyGUI(_bindingProperty);
        }

        private static void InvokePrivate(
            DeviceDescriptorPopup popup,
            string methodName,
            params object[] args)
        {
            MethodInfo method = typeof(DeviceDescriptorPopup).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(popup, args);
        }

        private static void InvokePrivateStatic(
            Type type,
            string methodName,
            params object[] args)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, args);
        }
    }
}
