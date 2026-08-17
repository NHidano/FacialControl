#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE && FACIALCONTROL_HAS_OSC_MODULE
using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.AdapterBindings.ARKit;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using UnityEditor;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings
{
    /// <summary>
    /// 1 個の <see cref="Hidano.FacialControl.Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO"/>
    /// に <see cref="InputSystemAdapterBinding"/> + <see cref="OscReceiverAdapterBinding"/> +
    /// <see cref="ArKitOscAdapterBinding"/> の 3 種を同時に保持し、
    /// <c>AssetDatabase.CreateAsset</c> → <c>LoadAssetAtPath</c> の round-trip で
    /// 3 種の concrete type identity と各 inline serialized field が維持されることを assert する。
    /// task 12.3, 6.6 に対応する Phase 2 完了後検証。
    /// </summary>
    [TestFixture]
    public class FacialCharacterProfileSO_MultiAdapterBindingRoundTripTests
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_FacialCharacterProfileSO_MultiAdapterBindingRoundTrip";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }
            _assetPath = TempFolderPath + "/MultiAdapterBindingRoundTrip_" + Guid.NewGuid().ToString("N") + ".asset";
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_assetPath))
            {
                AssetDatabase.DeleteAsset(_assetPath);
                _assetPath = null;
            }
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                var remaining = AssetDatabase.FindAssets(string.Empty, new[] { TempFolderPath });
                if (remaining == null || remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(TempFolderPath);
                }
            }
        }

        [Test]
        public void AdapterBindings_InputSystemAndOscAndArKit_RoundTripPreservesConcreteTypeIdentity()
        {
            // 単一 SO に 3 種 binding を同時保持できることを round-trip で検証する。
            // task 8.7: OscReceiverAdapterBinding は OscRuntimeSettingsSO sub-asset 経由で環境設定を保持する
            // 新経路を使用する (旧 _endpoint / _port 直 SerializeField 経路は廃止済み)。
            var so = UnityEngine.ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();

            var input = new InputSystemAdapterBinding
            {
                Slug = "input-system",
                ActionMapName = "Expression",
            };
            so.WritableAdapterBindings.Add(input);

            var oscSettings = UnityEngine.ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            oscSettings.name = "OscRuntimeSettings";
            oscSettings.FromJson(
                "{\"listenEndpoint\":\"192.168.1.10\",\"listenPort\":39539,\"stalenessSeconds\":0.25}");

            var osc = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Settings = oscSettings,
            };
            so.WritableAdapterBindings.Add(osc);

            var arkit = new ArKitOscAdapterBinding
            {
                Slug = "arkit",
                Endpoint = "192.168.1.20",
                Port = 39540,
                StalenessSeconds = 0.5f,
                ArKitParameterNames = new[] { "jawOpen", "eyeBlinkLeft", "eyeBlinkRight" },
            };
            so.WritableAdapterBindings.Add(arkit);

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.AddObjectToAsset(oscSettings, so);
            AssetDatabase.SaveAssets();
            UnityEngine.Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null, "SO は disk から再読み込みできるはず");
            Assert.That(loaded.AdapterBindings, Is.Not.Null);
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(3));

            // 3 種の concrete type identity が維持されていること。
            Assert.That(loaded.AdapterBindings[0], Is.InstanceOf<InputSystemAdapterBinding>(),
                "Index 0 は InputSystemAdapterBinding として round-trip するはず");
            Assert.That(loaded.AdapterBindings[1], Is.InstanceOf<OscReceiverAdapterBinding>(),
                "Index 1 は OscReceiverAdapterBinding として round-trip するはず");
            Assert.That(loaded.AdapterBindings[2], Is.InstanceOf<ArKitOscAdapterBinding>(),
                "Index 2 は ArKitOscAdapterBinding として round-trip するはず");

            var loadedInput = (InputSystemAdapterBinding)loaded.AdapterBindings[0];
            Assert.That(loadedInput.Slug, Is.EqualTo("input-system"));
            Assert.That(loadedInput.ActionMapName, Is.EqualTo("Expression"));

            var loadedOsc = (OscReceiverAdapterBinding)loaded.AdapterBindings[1];
            Assert.That(loadedOsc.Slug, Is.EqualTo("osc"));
            Assert.That(loadedOsc.Settings, Is.Not.Null,
                "OscRuntimeSettingsSO sub-asset 参照が round-trip するべき。");
            Assert.That(loadedOsc.Endpoint, Is.EqualTo("192.168.1.10"));
            Assert.That(loadedOsc.Port, Is.EqualTo(39539));
            Assert.That(loadedOsc.StalenessSeconds, Is.EqualTo(0.25f).Within(1e-6f));

            var loadedArKit = (ArKitOscAdapterBinding)loaded.AdapterBindings[2];
            Assert.That(loadedArKit.Slug, Is.EqualTo("arkit"));
            Assert.That(loadedArKit.Endpoint, Is.EqualTo("192.168.1.20"));
            Assert.That(loadedArKit.Port, Is.EqualTo(39540));
            Assert.That(loadedArKit.StalenessSeconds, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(loadedArKit.ArKitParameterNames,
                Is.EqualTo(new[] { "jawOpen", "eyeBlinkLeft", "eyeBlinkRight" }));

            // 3 instance が独立した参照であること（[SerializeReference] が
            // polymorphic instance を共有してしまわないことを確認）。
            Assert.That(ReferenceEquals(loaded.AdapterBindings[0], loaded.AdapterBindings[1]), Is.False);
            Assert.That(ReferenceEquals(loaded.AdapterBindings[1], loaded.AdapterBindings[2]), Is.False);
            Assert.That(ReferenceEquals(loaded.AdapterBindings[0], loaded.AdapterBindings[2]), Is.False);

            // SerializedProperty レイヤでも 3 件の concrete FullTypeName が解決されていることを確認する。
            using (var serialized = new SerializedObject(loaded))
            {
                var listProp = serialized.FindProperty("_adapterBindings");
                Assert.That(listProp, Is.Not.Null, "_adapterBindings は SerializedObject から発見できるはず");
                Assert.That(listProp.arraySize, Is.EqualTo(3));

                var elemInput = listProp.GetArrayElementAtIndex(0);
                Assert.That(elemInput.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(elemInput.managedReferenceFullTypename,
                    Does.Contain(typeof(InputSystemAdapterBinding).FullName));

                var elemOsc = listProp.GetArrayElementAtIndex(1);
                Assert.That(elemOsc.managedReferenceFullTypename,
                    Does.Contain(typeof(OscReceiverAdapterBinding).FullName));

                var elemArKit = listProp.GetArrayElementAtIndex(2);
                Assert.That(elemArKit.managedReferenceFullTypename,
                    Does.Contain(typeof(ArKitOscAdapterBinding).FullName));
            }
        }
    }
}
#endif
