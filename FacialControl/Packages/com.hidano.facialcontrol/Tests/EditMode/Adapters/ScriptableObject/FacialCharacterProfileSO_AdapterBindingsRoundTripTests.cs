using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Adapters;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings
{
    /// <summary>
    /// <see cref="Hidano.FacialControl.Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO"/>
    /// に追加される <c>[SerializeReference] List&lt;AdapterBindingBase&gt; _adapterBindings</c> field の
    /// round-trip 挙動（具象型 identity / slug / 各 field の保存）を検証する。
    /// 同型複数登録、空 list、null 要素も含む。
    /// </summary>
    [TestFixture]
    public class FacialCharacterProfileSO_AdapterBindingsRoundTripTests
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_FacialCharacterProfileSO_AdapterBindingsRoundTrip";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }
            _assetPath = TempFolderPath + "/AdapterBindingsRoundTrip_" + Guid.NewGuid().ToString("N") + ".asset";
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
        public void AdapterBindings_TwoConcreteTypes_RoundTripPreservesTypeAndFieldValues()
        {
            // 異なる派生型 2 種を _adapterBindings に追加し、
            // CreateAsset → LoadAssetAtPath → 内容（slug / 各 field）一致を assert する。
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            so.WritableAdapterBindings.Add(new MockTriggerAdapterBinding
            {
                Slug = "trigger-1",
                TriggerThreshold = 7,
            });
            so.WritableAdapterBindings.Add(new MockAnalogAdapterBinding
            {
                Slug = "analog-1",
                AnalogScale = 0.5f,
            });

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null, "SO は disk から再読み込みできるはず");
            Assert.That(loaded.AdapterBindings, Is.Not.Null);
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(2));
            Assert.That(loaded.AdapterBindings[0], Is.InstanceOf<MockTriggerAdapterBinding>(),
                "Index 0 は MockTriggerAdapterBinding の concrete type を保持するはず");
            Assert.That(loaded.AdapterBindings[1], Is.InstanceOf<MockAnalogAdapterBinding>(),
                "Index 1 は MockAnalogAdapterBinding の concrete type を保持するはず");

            var trigger = (MockTriggerAdapterBinding)loaded.AdapterBindings[0];
            Assert.That(trigger.Slug, Is.EqualTo("trigger-1"));
            Assert.That(trigger.TriggerThreshold, Is.EqualTo(7));

            var analog = (MockAnalogAdapterBinding)loaded.AdapterBindings[1];
            Assert.That(analog.Slug, Is.EqualTo("analog-1"));
            Assert.That(analog.AnalogScale, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void AdapterBindings_SameTypeMultipleInstances_RoundTripsIndependently()
        {
            // 同型 binding を複数登録できる（OSC × 2 等を想定）。
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            so.WritableAdapterBindings.Add(new MockTriggerAdapterBinding { Slug = "first", TriggerThreshold = 1 });
            so.WritableAdapterBindings.Add(new MockTriggerAdapterBinding { Slug = "second", TriggerThreshold = 2 });

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(2));
            var first = (MockTriggerAdapterBinding)loaded.AdapterBindings[0];
            var second = (MockTriggerAdapterBinding)loaded.AdapterBindings[1];
            Assert.That(first.Slug, Is.EqualTo("first"));
            Assert.That(first.TriggerThreshold, Is.EqualTo(1));
            Assert.That(second.Slug, Is.EqualTo("second"));
            Assert.That(second.TriggerThreshold, Is.EqualTo(2));
            Assert.That(ReferenceEquals(first, second), Is.False,
                "同型でも独立した instance として round-trip するはず");
        }

        [Test]
        public void AdapterBindings_EmptyList_RoundTripsAsEmpty()
        {
            // zero 個の binding でも許容され、null ではなく空 collection として復元される。
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.AdapterBindings, Is.Not.Null,
                "空 list でも null ではなく空 collection として復元されるはず");
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void AdapterBindings_NullElementBetweenValidEntries_DoesNotBreakSubsequentLoad()
        {
            // null 要素（型欠落 simulation）を含んでいても asset 全体の load は中断されず、
            // null 要素は null のまま、前後の binding は完全な状態で round-trip する。
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            so.WritableAdapterBindings.Add(new MockTriggerAdapterBinding { Slug = "ok-front", TriggerThreshold = 1 });
            so.WritableAdapterBindings.Add(null);
            so.WritableAdapterBindings.Add(new MockAnalogAdapterBinding { Slug = "ok-back", AnalogScale = 1.5f });

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(so);

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null,
                "null 要素を含んでいても SO 全体の load は失敗しない");
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(3));

            Assert.That(loaded.AdapterBindings[0], Is.InstanceOf<MockTriggerAdapterBinding>());
            Assert.That(loaded.AdapterBindings[1], Is.Null,
                "null 要素は null のまま round-trip する");
            Assert.That(loaded.AdapterBindings[2], Is.InstanceOf<MockAnalogAdapterBinding>());

            var front = (MockTriggerAdapterBinding)loaded.AdapterBindings[0];
            Assert.That(front.Slug, Is.EqualTo("ok-front"));
            Assert.That(front.TriggerThreshold, Is.EqualTo(1));

            var back = (MockAnalogAdapterBinding)loaded.AdapterBindings[2];
            Assert.That(back.Slug, Is.EqualTo("ok-back"));
            Assert.That(back.AnalogScale, Is.EqualTo(1.5f).Within(1e-6f));
        }
    }

    /// <summary>
    /// Round-trip 検証用の Mock <see cref="AdapterBindingBase"/> 派生型（trigger 系）。
    /// <c>[SerializeReference]</c> の polymorphic 復元と field 値保存を確認する目的に限定する。
    /// </summary>
    [Serializable]
    public sealed class MockTriggerAdapterBinding : AdapterBindingBase
    {
        public int TriggerThreshold;
    }

    /// <summary>
    /// Round-trip 検証用の Mock <see cref="AdapterBindingBase"/> 派生型（analog 系）。
    /// </summary>
    [Serializable]
    public sealed class MockAnalogAdapterBinding : AdapterBindingBase
    {
        public float AnalogScale;
    }
}
