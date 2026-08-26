using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// task 2.2 の観測可能完了条件: <see cref="AdapterRuntimeSettingsCollectionSO"/> を
    /// <c>ScriptableObject.CreateInstance</c> で生成し、空 List 状態で <c>TryFind&lt;T&gt;()</c>
    /// が <c>null</c> を返すこと、および同型/ラベル検索の基本動作を検証する。
    /// </summary>
    [TestFixture]
    public class AdapterRuntimeSettingsCollectionSOTests
    {
        public sealed class FakeAlphaSettings : AdapterRuntimeSettingsBase
        {
        }

        public sealed class FakeBetaSettings : AdapterRuntimeSettingsBase
        {
        }

        private AdapterRuntimeSettingsCollectionSO _collection;

        [SetUp]
        public void SetUp()
        {
            _collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_collection != null)
            {
                Object.DestroyImmediate(_collection);
                _collection = null;
            }
        }

        [Test]
        public void Items_OnFreshInstance_ReturnsEmptyList()
        {
            Assert.IsNotNull(_collection.Items);
            Assert.AreEqual(0, _collection.Items.Count);
        }

        [Test]
        public void TryFind_OnEmptyCollection_ReturnsNull()
        {
            var result = _collection.TryFind<FakeAlphaSettings>();

            Assert.IsNull(result);
        }

        [Test]
        public void TryFindWithLabel_OnEmptyCollection_ReturnsNull()
        {
            var result = _collection.TryFind<FakeAlphaSettings>("primary");

            Assert.IsNull(result);
        }

        [Test]
        public void TryFind_WithMatchingType_ReturnsFirstInstance()
        {
            var alpha = ScriptableObject.CreateInstance<FakeAlphaSettings>();
            var beta = ScriptableObject.CreateInstance<FakeBetaSettings>();

            try
            {
                var so = new UnityEditor.SerializedObject(_collection);
                var items = so.FindProperty("_items");
                items.arraySize = 2;
                items.GetArrayElementAtIndex(0).objectReferenceValue = alpha;
                items.GetArrayElementAtIndex(1).objectReferenceValue = beta;
                so.ApplyModifiedPropertiesWithoutUndo();

                var result = _collection.TryFind<FakeAlphaSettings>();

                Assert.AreSame(alpha, result);
            }
            finally
            {
                Object.DestroyImmediate(alpha);
                Object.DestroyImmediate(beta);
            }
        }

        [Test]
        public void TryFind_WithoutMatchingType_ReturnsNull()
        {
            var beta = ScriptableObject.CreateInstance<FakeBetaSettings>();

            try
            {
                var so = new UnityEditor.SerializedObject(_collection);
                var items = so.FindProperty("_items");
                items.arraySize = 1;
                items.GetArrayElementAtIndex(0).objectReferenceValue = beta;
                so.ApplyModifiedPropertiesWithoutUndo();

                var result = _collection.TryFind<FakeAlphaSettings>();

                Assert.IsNull(result);
            }
            finally
            {
                Object.DestroyImmediate(beta);
            }
        }

        [Test]
        public void TryFindWithLabel_MatchingTypeAndLabel_ReturnsInstance()
        {
            var first = ScriptableObject.CreateInstance<FakeAlphaSettings>();
            var second = ScriptableObject.CreateInstance<FakeAlphaSettings>();

            try
            {
                AssignLabel(first, "primary");
                AssignLabel(second, "secondary");

                var collectionSo = new UnityEditor.SerializedObject(_collection);
                var items = collectionSo.FindProperty("_items");
                items.arraySize = 2;
                items.GetArrayElementAtIndex(0).objectReferenceValue = first;
                items.GetArrayElementAtIndex(1).objectReferenceValue = second;
                collectionSo.ApplyModifiedPropertiesWithoutUndo();

                var result = _collection.TryFind<FakeAlphaSettings>("secondary");

                Assert.AreSame(second, result);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void TryFindWithLabel_NoLabelMatch_ReturnsNull()
        {
            var first = ScriptableObject.CreateInstance<FakeAlphaSettings>();

            try
            {
                AssignLabel(first, "primary");

                var collectionSo = new UnityEditor.SerializedObject(_collection);
                var items = collectionSo.FindProperty("_items");
                items.arraySize = 1;
                items.GetArrayElementAtIndex(0).objectReferenceValue = first;
                collectionSo.ApplyModifiedPropertiesWithoutUndo();

                var result = _collection.TryFind<FakeAlphaSettings>("missing");

                Assert.IsNull(result);
            }
            finally
            {
                Object.DestroyImmediate(first);
            }
        }

        [Test]
        public void IndexOf_ItemNotPresent_ReturnsMinusOne()
        {
            var alpha = ScriptableObject.CreateInstance<FakeAlphaSettings>();

            try
            {
                Assert.AreEqual(-1, _collection.IndexOf(alpha));
            }
            finally
            {
                Object.DestroyImmediate(alpha);
            }
        }

        [Test]
        public void IndexOf_Null_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, _collection.IndexOf(null));
        }

        [Test]
        public void IndexOf_ItemPresent_ReturnsIndex()
        {
            var alpha = ScriptableObject.CreateInstance<FakeAlphaSettings>();
            var beta = ScriptableObject.CreateInstance<FakeBetaSettings>();

            try
            {
                var so = new UnityEditor.SerializedObject(_collection);
                var items = so.FindProperty("_items");
                items.arraySize = 2;
                items.GetArrayElementAtIndex(0).objectReferenceValue = alpha;
                items.GetArrayElementAtIndex(1).objectReferenceValue = beta;
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.AreEqual(0, _collection.IndexOf(alpha));
                Assert.AreEqual(1, _collection.IndexOf(beta));
            }
            finally
            {
                Object.DestroyImmediate(alpha);
                Object.DestroyImmediate(beta);
            }
        }

        [Test]
        public void OnEnable_WithNullItem_LogsWarningPerNullEntry()
        {
            var collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();

            try
            {
                var so = new UnityEditor.SerializedObject(collection);
                var items = so.FindProperty("_items");
                items.arraySize = 2;
                items.GetArrayElementAtIndex(0).objectReferenceValue = null;
                items.GetArrayElementAtIndex(1).objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();

                LogAssert.Expect(LogType.Warning, new Regex(@"_items\[0\] が null"));
                LogAssert.Expect(LogType.Warning, new Regex(@"_items\[1\] が null"));

                InvokeOnEnable(collection);
            }
            finally
            {
                Object.DestroyImmediate(collection);
            }
        }

        [Test]
        public void OnEnable_WithoutNullItems_DoesNotLogWarning()
        {
            var collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();
            var alpha = ScriptableObject.CreateInstance<FakeAlphaSettings>();

            try
            {
                var so = new UnityEditor.SerializedObject(collection);
                var items = so.FindProperty("_items");
                items.arraySize = 1;
                items.GetArrayElementAtIndex(0).objectReferenceValue = alpha;
                so.ApplyModifiedPropertiesWithoutUndo();

                InvokeOnEnable(collection);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(collection);
                Object.DestroyImmediate(alpha);
            }
        }

        private static void AssignLabel(AdapterRuntimeSettingsBase target, string label)
        {
            var so = new UnityEditor.SerializedObject(target);
            so.FindProperty("_label").stringValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void InvokeOnEnable(AdapterRuntimeSettingsCollectionSO target)
        {
            var method = typeof(AdapterRuntimeSettingsCollectionSO).GetMethod(
                "OnEnable",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, "OnEnable メソッドをリフレクションで取得できませんでした。");
            method.Invoke(target, null);
        }
    }
}
