using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.SerializeReferenceSmoke
{
    /// <summary>
    /// R-B: Unity 6 標準の <c>[SerializeReference]</c> による polymorphic round-trip が
    /// 期待通りに動作することを検証する smoke test。
    /// 一時的な Mock <c>AdapterBindingBase</c> 派生型 2 種を Tests/EditMode 内に閉じて定義し、
    /// <c>AssetDatabase.CreateAsset</c> → <c>LoadAssetAtPath</c> による具象型 identity の保存と
    /// <c>SerializedProperty.managedReferenceFullTypename</c> の挙動を assert する。
    /// fail した場合は <c>[SerializeReference]</c> 仕様を再調査する判断材料となる。
    /// </summary>
    [TestFixture]
    public class SerializeReferenceRoundTripSmokeTests
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_AdapterBindingSerializeReferenceSmoke";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }
            _assetPath = TempFolderPath + "/SerializeReferenceSmoke_" + Guid.NewGuid().ToString("N") + ".asset";
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
        public void SerializeReference_TwoConcreteTypes_RoundTripPreservesTypeIdentity()
        {
            var so = ScriptableObject.CreateInstance<SerializeReferenceTestProfileStub>();
            so.AdapterBindings.Add(new MockTriggerAdapterBindingStub
            {
                slug = "trigger-1",
                triggerThreshold = 7,
            });
            so.AdapterBindings.Add(new MockAnalogAdapterBindingStub
            {
                slug = "analog-1",
                analogScale = 0.5f,
            });

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<SerializeReferenceTestProfileStub>(_assetPath);

            Assert.That(loaded, Is.Not.Null, "SO は disk から再読み込みできるはず");
            Assert.That(loaded.AdapterBindings, Is.Not.Null);
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(2));
            Assert.That(loaded.AdapterBindings[0], Is.InstanceOf<MockTriggerAdapterBindingStub>(),
                "Index 0 は MockTriggerAdapterBindingStub の concrete type を保持するはず");
            Assert.That(loaded.AdapterBindings[1], Is.InstanceOf<MockAnalogAdapterBindingStub>(),
                "Index 1 は MockAnalogAdapterBindingStub の concrete type を保持するはず");

            var trigger = (MockTriggerAdapterBindingStub)loaded.AdapterBindings[0];
            Assert.That(trigger.slug, Is.EqualTo("trigger-1"));
            Assert.That(trigger.triggerThreshold, Is.EqualTo(7));

            var analog = (MockAnalogAdapterBindingStub)loaded.AdapterBindings[1];
            Assert.That(analog.slug, Is.EqualTo("analog-1"));
            Assert.That(analog.analogScale, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void SerializeReference_SameTypeMultipleInstances_PreserveIndependentFieldValues()
        {
            // 同型 binding が複数登録できる（slug 違いの OSC を 2 個など）。
            var so = ScriptableObject.CreateInstance<SerializeReferenceTestProfileStub>();
            so.AdapterBindings.Add(new MockTriggerAdapterBindingStub { slug = "first", triggerThreshold = 1 });
            so.AdapterBindings.Add(new MockTriggerAdapterBindingStub { slug = "second", triggerThreshold = 2 });

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<SerializeReferenceTestProfileStub>(_assetPath);

            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(2));
            var first = (MockTriggerAdapterBindingStub)loaded.AdapterBindings[0];
            var second = (MockTriggerAdapterBindingStub)loaded.AdapterBindings[1];
            Assert.That(first.slug, Is.EqualTo("first"));
            Assert.That(first.triggerThreshold, Is.EqualTo(1));
            Assert.That(second.slug, Is.EqualTo("second"));
            Assert.That(second.triggerThreshold, Is.EqualTo(2));
            Assert.That(ReferenceEquals(first, second), Is.False,
                "同型でも独立した instance として round-trip するはず");
        }

        [Test]
        public void SerializeReference_EmptyList_RoundTripsAsEmpty()
        {
            // AdapterBindings は zero 個でも許容される。
            var so = ScriptableObject.CreateInstance<SerializeReferenceTestProfileStub>();

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<SerializeReferenceTestProfileStub>(_assetPath);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.AdapterBindings, Is.Not.Null,
                "空 list でも null ではなく空 collection として復元されるはず");
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void SerializeReference_ManagedReferenceFullTypename_NonEmptyForResolvableConcreteType()
        {
            var so = ScriptableObject.CreateInstance<SerializeReferenceTestProfileStub>();
            so.AdapterBindings.Add(new MockTriggerAdapterBindingStub
            {
                slug = "trigger-1",
                triggerThreshold = 7,
            });

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<SerializeReferenceTestProfileStub>(_assetPath);
            using (var serialized = new SerializedObject(loaded))
            {
                var listProp = serialized.FindProperty(SerializeReferenceTestProfileStub.AdapterBindingsFieldName);
                Assert.That(listProp, Is.Not.Null,
                    "_adapterBindings は SerializedObject から FindProperty で発見できるはず");
                Assert.That(listProp.arraySize, Is.EqualTo(1));

                var element = listProp.GetArrayElementAtIndex(0);
                Assert.That(element.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference),
                    "要素は ManagedReference として扱われる");
                Assert.That(element.managedReferenceValue, Is.Not.Null,
                    "解決可能な concrete type は managedReferenceValue が non-null");
                Assert.That(element.managedReferenceFullTypename, Is.Not.Empty,
                    "解決可能な concrete type は managedReferenceFullTypename が非空");
                Assert.That(element.managedReferenceFullTypename,
                    Does.Contain(typeof(MockTriggerAdapterBindingStub).FullName),
                    "managedReferenceFullTypename は具象 FullTypeName を含むはず");
            }
        }

        [Test]
        public void SerializeReference_NullElement_HasEmptyManagedReferenceFullTypename()
        {
            // null 要素（型欠落 simulation）に対する managedReferenceFullTypename の挙動を確認。
            // 空文字列の null entry と「型欠落で値だけ null になった entry」の境界を smoke レベルで検証する。
            var so = ScriptableObject.CreateInstance<SerializeReferenceTestProfileStub>();
            so.AdapterBindings.Add(new MockTriggerAdapterBindingStub { slug = "ok", triggerThreshold = 1 });
            so.AdapterBindings.Add(null);

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<SerializeReferenceTestProfileStub>(_assetPath);

            Assert.That(loaded, Is.Not.Null,
                "null 要素を含んでいても asset 全体の load は中断されない");
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(2));
            Assert.That(loaded.AdapterBindings[0], Is.Not.Null);
            Assert.That(loaded.AdapterBindings[1], Is.Null,
                "null 要素は null のまま round-trip する");

            using (var serialized = new SerializedObject(loaded))
            {
                var listProp = serialized.FindProperty(SerializeReferenceTestProfileStub.AdapterBindingsFieldName);
                Assert.That(listProp.arraySize, Is.EqualTo(2));

                var validElement = listProp.GetArrayElementAtIndex(0);
                Assert.That(validElement.managedReferenceValue, Is.Not.Null);
                Assert.That(validElement.managedReferenceFullTypename, Is.Not.Empty);

                var nullElement = listProp.GetArrayElementAtIndex(1);
                Assert.That(nullElement.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(nullElement.managedReferenceValue, Is.Null,
                    "null 要素では managedReferenceValue が null である");
                Assert.That(nullElement.managedReferenceFullTypename, Is.EqualTo(string.Empty),
                    "null 要素では managedReferenceFullTypename が空文字列となる "
                    + "(型欠落と異なり typename 情報そのものが存在しない)");
            }
        }
    }

    /// <summary>
    /// Smoke test 用の Mock 抽象基底。core の <c>AdapterBindingBase</c> 実装に先行して
    /// <c>[SerializeReference]</c> の polymorphic round-trip を検証するため、テストアセンブリ内に閉じて定義する。
    /// </summary>
    [Serializable]
    public abstract class MockAdapterBindingStubBase
    {
        public string slug;
    }

    [Serializable]
    public sealed class MockTriggerAdapterBindingStub : MockAdapterBindingStubBase
    {
        public int triggerThreshold;
    }

    [Serializable]
    public sealed class MockAnalogAdapterBindingStub : MockAdapterBindingStubBase
    {
        public float analogScale;
    }
}
