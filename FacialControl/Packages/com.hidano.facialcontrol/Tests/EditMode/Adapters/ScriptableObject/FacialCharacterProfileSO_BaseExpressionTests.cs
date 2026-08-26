using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using RoundTripProfileSO = Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings.TestFacialCharacterProfileSO;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    [TestFixture]
    public class FacialCharacterProfileSO_BaseExpressionTests
    {
        private const string BaseExpressionTypeName =
            "Hidano.FacialControl.Adapters.ScriptableObject.Serializable.BaseExpressionSerializable";
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_FacialCharacterProfileSO_BaseExpressionRoundTrip";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/BaseExpressionRoundTrip_" + Guid.NewGuid().ToString("N") + ".asset";
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
        public void BaseExpression_Unset_ReturnsEmptyCachedBlendShapeSnapshot()
        {
            var so = ScriptableObject.CreateInstance<RoundTripProfileSO>();
            try
            {
                object baseExpression = GetBaseExpression(so);
                var cachedSnapshot = GetCachedSnapshot(baseExpression);

                Assert.That(cachedSnapshot, Is.Not.Null,
                    "Unset _baseExpression must be exposed as an empty cached snapshot.");
                Assert.That(cachedSnapshot.blendShapes, Is.Not.Null,
                    "Unset _baseExpression must expose an empty blendShapes list, not null.");
                Assert.That(cachedSnapshot.blendShapes, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void BaseExpression_NullSerializedField_GetterDoesNotThrow()
        {
            var so = ScriptableObject.CreateInstance<RoundTripProfileSO>();
            try
            {
                Type baseExpressionType = ResolveBaseExpressionType();
                FieldInfo field = GetBaseExpressionField(baseExpressionType);
                field.SetValue(so, null);

                object baseExpression = null;
                Assert.DoesNotThrow(() => baseExpression = GetBaseExpression(so));
                Assert.That(baseExpression, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void BaseExpression_AnimationClipAndCachedSnapshot_RoundTripsThroughAssetReload()
        {
            var so = ScriptableObject.CreateInstance<RoundTripProfileSO>();
            var clip = new AnimationClip { name = "BaseExpression_RoundTripClip" };

            try
            {
                Type baseExpressionType = ResolveBaseExpressionType();
                object baseExpression = Activator.CreateInstance(baseExpressionType);
                SetMemberValue(baseExpression, "animationClip", clip);
                SetMemberValue(baseExpression, "cachedSnapshot", CreateSnapshot());
                GetBaseExpressionField(baseExpressionType).SetValue(so, baseExpression);

                AssetDatabase.CreateAsset(so, _assetPath);
                AssetDatabase.AddObjectToAsset(clip, so);
                EditorUtility.SetDirty(so);
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
                Resources.UnloadAsset(so);
                Resources.UnloadAsset(clip);

                var loaded = AssetDatabase.LoadAssetAtPath<RoundTripProfileSO>(_assetPath);
                Assert.That(loaded, Is.Not.Null);

                object loadedBaseExpression = GetBaseExpression(loaded);
                var loadedClip = GetMemberValue(loadedBaseExpression, "animationClip") as AnimationClip;
                var loadedSnapshot = GetCachedSnapshot(loadedBaseExpression);

                Assert.That(loadedClip, Is.Not.Null);
                Assert.That(loadedClip.name, Is.EqualTo("BaseExpression_RoundTripClip"));
                Assert.That(loadedSnapshot, Is.Not.Null);
                Assert.That(loadedSnapshot.blendShapes, Has.Count.EqualTo(2));
                AssertBlendShape(loadedSnapshot.blendShapes[0], "Body", "Brow_Angry", 64.5f);
                AssertBlendShape(loadedSnapshot.blendShapes[1], "Face", "Eye_Narrow", 28.25f);

                using (var serialized = new SerializedObject(loaded))
                {
                    var rootProperty = serialized.FindProperty("_baseExpression");
                    Assert.That(rootProperty, Is.Not.Null,
                        "_baseExpression must be serialized at the FacialCharacterProfileSO root.");
                }
            }
            finally
            {
                if (so != null && !EditorUtility.IsPersistent(so))
                {
                    Object.DestroyImmediate(so);
                }

                if (clip != null && !EditorUtility.IsPersistent(clip))
                {
                    Object.DestroyImmediate(clip);
                }
            }
        }

        private static ExpressionSnapshotDto CreateSnapshot()
        {
            return new ExpressionSnapshotDto
            {
                blendShapes = new List<BlendShapeSnapshotDto>
                {
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = "Body",
                        name = "Brow_Angry",
                        value = 64.5f,
                    },
                    new BlendShapeSnapshotDto
                    {
                        rendererPath = "Face",
                        name = "Eye_Narrow",
                        value = 28.25f,
                    },
                },
            };
        }

        private static object GetBaseExpression(FacialCharacterProfileSO so)
        {
            Type baseExpressionType = ResolveBaseExpressionType();
            PropertyInfo property = typeof(FacialCharacterProfileSO).GetProperty(
                "BaseExpression",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(property, Is.Not.Null,
                "FacialCharacterProfileSO must expose public BaseExpression getter.");
            Assert.That(property.PropertyType, Is.EqualTo(baseExpressionType));

            object value = null;
            Assert.DoesNotThrow(() => value = property.GetValue(so));
            Assert.That(value, Is.Not.Null);
            return value;
        }

        private static FieldInfo GetBaseExpressionField(Type baseExpressionType)
        {
            FieldInfo field = typeof(FacialCharacterProfileSO).GetField(
                "_baseExpression",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                "FacialCharacterProfileSO must serialize _baseExpression at the root.");
            Assert.That(field.FieldType, Is.EqualTo(baseExpressionType));
            return field;
        }

        private static Type ResolveBaseExpressionType()
        {
            Type direct = Type.GetType(BaseExpressionTypeName + ", Hidano.FacialControl.Adapters");
            if (direct != null)
            {
                return direct;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(BaseExpressionTypeName);
                if (found != null)
                {
                    return found;
                }
            }

            Assert.Fail(BaseExpressionTypeName + " must exist as the slim ScriptableObject serializable type.");
            return null;
        }

        private static ExpressionSnapshotDto GetCachedSnapshot(object baseExpression)
        {
            return GetMemberValue(baseExpression, "cachedSnapshot") as ExpressionSnapshotDto;
        }

        private static object GetMemberValue(object target, string name)
        {
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(target);
            }

            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return property.GetValue(target);
            }

            Assert.Fail(type.FullName + " must expose " + name + ".");
            return null;
        }

        private static void SetMemberValue(object target, string name, object value)
        {
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                property.SetValue(target, value);
                return;
            }

            Assert.Fail(type.FullName + " must expose " + name + ".");
        }

        private static void AssertBlendShape(
            BlendShapeSnapshotDto actual,
            string expectedRendererPath,
            string expectedName,
            float expectedValue)
        {
            Assert.That(actual.rendererPath, Is.EqualTo(expectedRendererPath));
            Assert.That(actual.name, Is.EqualTo(expectedName));
            Assert.That(actual.value, Is.EqualTo(expectedValue).Within(1e-6f));
        }
    }
}
