using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
#endif
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.Playable
{
    /// <summary>
    /// <see cref="FacialController"/> の新 <c>_characterSO</c> 経路を検証する。
    /// SerializeField の存在 / プロパティのアクセシビリティ / 旧 ProfileSO との共存を確認する。
    /// </summary>
    [TestFixture]
    public class FacialControllerCharacterSOTests
    {
        private sealed class TestCharacterSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
            public List<GazeBindingConfig> WritableGazeConfigs => _gazeConfigs;

            public override FacialProfile LoadProfile()
            {
                var layers = new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
                };
                return new FacialProfile("2.0", layers);
            }
        }

        [Test]
        public void CharacterSO_Setter_PersistsValue()
        {
            var go = new GameObject("FacialControllerHost");
            try
            {
                go.AddComponent<Animator>();
                var controller = go.AddComponent<FacialController>();
                var so = ScriptableObject.CreateInstance<TestCharacterSO>();
                so.name = "MyChar";

                controller.CharacterSO = so;

                Assert.That(controller.CharacterSO, Is.SameAs(so));
                Object.DestroyImmediate(so);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CharacterSO_SerializeField_IsExposedToInspector()
        {
            var go = new GameObject("FacialControllerHost");
            try
            {
                go.AddComponent<Animator>();
                var controller = go.AddComponent<FacialController>();
                var serialized = new SerializedObject(controller);
                var prop = serialized.FindProperty("_characterSO");

                Assert.That(prop, Is.Not.Null,
                    "FacialController に _characterSO の SerializeField が存在するはず。");
                Assert.That(prop.propertyType, Is.EqualTo(SerializedPropertyType.ObjectReference));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Initialize_NoSOConfigured_DoesNothing()
        {
            var go = new GameObject("FacialControllerHost");
            try
            {
                go.AddComponent<Animator>();
                var controller = go.AddComponent<FacialController>();

                // _characterSO も _profileSO も未設定。Initialize は警告も出さず単に return する。
                controller.Initialize();

                Assert.That(controller.IsInitialized, Is.False);
                Assert.That(controller.CurrentProfile, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE
        [Test]
        public void Initialize_InputSystemBinding_InjectsSORootGazeConfigsByReferenceInEditMode()
        {
            var go = CreateControllerHost();
            var so = ScriptableObject.CreateInstance<TestCharacterSO>();
            try
            {
                var controller = go.AddComponent<FacialController>();
                var binding = new InputSystemAdapterBinding { Slug = "input-system-gaze-injection-editmode" };
                so.WritableAdapterBindings.Add(binding);
                so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "expr-gaze" });

                controller.CharacterSO = so;
                controller.Initialize();

                object injected = ReadInjectedGazeConfigs(binding);
                Assert.That(injected, Is.SameAs(so.GazeConfigs),
                    "EditMode の FacialController.Initialize 経路でも SO ルート GazeConfigs が参照同値で注入されるべき。");
            }
            finally
            {
                Object.DestroyImmediate(so);
                Object.DestroyImmediate(go);
            }
        }
#endif

        private static GameObject CreateControllerHost()
        {
            var go = new GameObject("FacialControllerHost");
            go.AddComponent<Animator>();
            var meshGo = new GameObject("Mesh");
            meshGo.transform.SetParent(go.transform);
            meshGo.AddComponent<SkinnedMeshRenderer>();
            return go;
        }

#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE
        private static object ReadInjectedGazeConfigs(InputSystemAdapterBinding binding)
        {
            var field = typeof(InputSystemAdapterBinding).GetField(
                "_injectedGazeConfigs",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                "InputSystemAdapterBinding._injectedGazeConfigs は runtime 注入ハンドルとして存在するべき。");
            return field.GetValue(binding);
        }
#endif
    }
}
