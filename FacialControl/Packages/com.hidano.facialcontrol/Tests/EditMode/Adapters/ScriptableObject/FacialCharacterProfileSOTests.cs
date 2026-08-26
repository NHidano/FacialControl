using System.IO;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using UnityEditor;
using UnityEngine;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    /// <summary>
    /// 抽象 <see cref="FacialCharacterProfileSO"/> の挙動を検証する。
    /// テスト用の最小具象 SO を介して LoadProfile / BuildFallbackProfile / 規約パス算出をカバーする。
    /// </summary>
    [TestFixture]
    public class FacialCharacterProfileSOTests
    {
        private sealed class TestCharacterSO : FacialCharacterProfileSO
        {
            public System.Collections.Generic.List<GazeBindingConfig> WritableGazeConfigs => _gazeConfigs;
        }

        [Test]
        public void GetStreamingAssetsProfilePath_ValidName_ReturnsExpectedPath()
        {
            string path = FacialCharacterProfileSO.GetStreamingAssetsProfilePath("Miku");

            Assert.That(path, Is.Not.Null);
            string expectedTail = Path.Combine(
                FacialCharacterProfileSO.StreamingAssetsRootFolder,
                "Miku",
                FacialCharacterProfileSO.ProfileJsonFileName);
            StringAssert.EndsWith(expectedTail, path);
        }

        [Test]
        public void GetStreamingAssetsProfilePath_EmptyName_ReturnsNull()
        {
            Assert.That(FacialCharacterProfileSO.GetStreamingAssetsProfilePath(null), Is.Null);
            Assert.That(FacialCharacterProfileSO.GetStreamingAssetsProfilePath(""), Is.Null);
            Assert.That(FacialCharacterProfileSO.GetStreamingAssetsProfilePath("   "), Is.Null);
        }

        [Test]
        public void BuildFallbackProfile_FromInspectorData_ReflectsAllFields()
        {
            var so = ScriptableObject.CreateInstance<TestCharacterSO>();
            try
            {
                so.SchemaVersion = "1.0";
                so.Layers.Add(new LayerDefinitionSerializable
                {
                    name = "emotion",
                    priority = 0,
                    exclusionMode = ExclusionMode.LastWins,
                    inputSources = { new InputSourceDeclarationSerializable { id = "input", weight = 1.0f } },
                });
                so.Expressions.Add(new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.25f,
                });
                so.RendererPaths.Add("Body");

                var serializedObject = new SerializedObject(so);
                SerializedProperty slots = serializedObject.FindProperty("_slots");
                Assert.That(slots, Is.Not.Null);
                Assert.That(slots.isArray, Is.True);
                Assert.That(slots.arraySize, Is.EqualTo(0));
                slots.arraySize = 1;
                slots.GetArrayElementAtIndex(0).stringValue = "blink";
                serializedObject.ApplyModifiedProperties();

                var profile = so.BuildFallbackProfile();

                Assert.That(profile.SchemaVersion, Is.EqualTo("1.0"));
                Assert.That(profile.Layers.Length, Is.EqualTo(1));
                Assert.That(profile.Expressions.Length, Is.EqualTo(1));
                Assert.That(profile.LayerInputSources.Length, Is.EqualTo(1));
                Assert.That(profile.RendererPaths.Length, Is.EqualTo(1));
                Assert.That(so.Slots, Is.Not.Null);
                Assert.That(so.Slots, Has.Count.EqualTo(1));
                Assert.That(profile.Slots.Length, Is.EqualTo(1));
                Assert.That(profile.Slots.Span[0], Is.EqualTo("blink"));
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void GazeConfigs_NewInstance_ExposesNonNullReadOnlyEmptyList()
        {
            var so = ScriptableObject.CreateInstance<TestCharacterSO>();
            try
            {
                IFacialCharacterProfile profile = so;

                Assert.That(profile.GazeConfigs, Is.Not.Null);
                Assert.That(profile.GazeConfigs, Is.Empty);
                Assert.That(so.GazeConfigs, Is.SameAs(profile.GazeConfigs));
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void GazeConfigs_SerializedAtRoot_PreservesValuesBesideAdapterBindings()
        {
            var so = ScriptableObject.CreateInstance<TestCharacterSO>();
            try
            {
                so.WritableGazeConfigs.Add(new GazeBindingConfig
                {
                    expressionId = "eye_look",
                    leftEyeBonePath = "LeftEye",
                });

                var serializedObject = new SerializedObject(so);
                SerializedProperty gazeConfigs = serializedObject.FindProperty("_gazeConfigs");
                SerializedProperty adapterBindings = serializedObject.FindProperty("_adapterBindings");

                Assert.That(gazeConfigs, Is.Not.Null);
                Assert.That(gazeConfigs.isArray, Is.True);
                Assert.That(gazeConfigs.arraySize, Is.EqualTo(1));
                Assert.That(adapterBindings, Is.Not.Null);

                SerializedProperty entry = gazeConfigs.GetArrayElementAtIndex(0);
                Assert.That(entry.FindPropertyRelative("expressionId").stringValue, Is.EqualTo("eye_look"));
                Assert.That(entry.FindPropertyRelative("useDistinctLeftRight").boolValue, Is.False);
                Assert.That(entry.FindPropertyRelative("sourceIdLeft").stringValue, Is.EqualTo(string.Empty));
                Assert.That(entry.FindPropertyRelative("sourceIdRight").stringValue, Is.EqualTo(string.Empty));
                Assert.That(entry.FindPropertyRelative("leftEyeBonePath").stringValue, Is.EqualTo("LeftEye"));
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void LoadProfile_NoStreamingAssetsJson_FallsBackToInspectorData()
        {
            var so = ScriptableObject.CreateInstance<TestCharacterSO>();
            try
            {
                // 既知の存在しないアセット名: 衝突回避のため UUID 風の prefix。
                so.name = "NonExistent_" + System.Guid.NewGuid().ToString("N");
                so.Layers.Add(new LayerDefinitionSerializable
                {
                    name = "emotion",
                    priority = 0,
                    exclusionMode = ExclusionMode.LastWins,
                });

                var profile = so.LoadProfile();

                Assert.That(profile.Layers.Length, Is.EqualTo(1));
                Assert.That(profile.Layers.Span[0].Name, Is.EqualTo("emotion"));
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void CharacterAssetName_ReflectsScriptableObjectName()
        {
            var so = ScriptableObject.CreateInstance<TestCharacterSO>();
            try
            {
                so.name = "MyCharacter";
                Assert.That(so.CharacterAssetName, Is.EqualTo("MyCharacter"));
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }
    }
}
