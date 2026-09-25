using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// task 8.1: <see cref="AdapterRuntimeSettingsCollectionSO"/> の sub-asset 追加/削除で
    /// 残存する sub-asset の <c>_label</c> / <c>_schemaVersion</c> が消失しないことを検証する。
    /// </summary>
    [TestFixture]
    public class AdapterRuntimeSettingsCollectionSOSubAssetTests
    {
        public sealed class FakeAlphaSettings : AdapterRuntimeSettingsBase
        {
        }

        public sealed class FakeBetaSettings : AdapterRuntimeSettingsBase
        {
        }

        public sealed class FakeGammaSettings : AdapterRuntimeSettingsBase
        {
        }

        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "Temp_AdapterRuntimeSettingsCollectionSOSubAssetTests";
        private const string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _collectionAssetPath;

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.DeleteAsset(TempFolderPath);
            }

            AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            _collectionAssetPath = TempFolderPath + "/TestCollection.asset";
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.DeleteAsset(TempFolderPath);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void SubAssets_AfterAddingMultipleEntries_PreserveLabelAndSchemaVersion()
        {
            var collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();
            AssetDatabase.CreateAsset(collection, _collectionAssetPath);

            var alpha = AddSubAsset<FakeAlphaSettings>(collection, "alpha", schemaVersion: 1);
            var beta = AddSubAsset<FakeBetaSettings>(collection, "beta", schemaVersion: 2);
            var gamma = AddSubAsset<FakeGammaSettings>(collection, "gamma", schemaVersion: 3);
            AppendToItems(collection, alpha, beta, gamma);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_collectionAssetPath, ImportAssetOptions.ForceUpdate);

            var loaded = AssetDatabase.LoadAssetAtPath<AdapterRuntimeSettingsCollectionSO>(_collectionAssetPath);
            Assert.IsNotNull(loaded, "Collection アセットを再ロードできませんでした。");
            Assert.AreEqual(3, loaded.Items.Count, "sub-asset 3 件分が _items に反映されている必要があります。");

            var loadedAlpha = loaded.TryFind<FakeAlphaSettings>("alpha");
            Assert.IsNotNull(loadedAlpha, "alpha sub-asset が再ロード後も TryFind で取得できる必要があります。");
            Assert.AreEqual("alpha", loadedAlpha.Label);
            Assert.AreEqual(1, loadedAlpha.SchemaVersion);

            var loadedBeta = loaded.TryFind<FakeBetaSettings>("beta");
            Assert.IsNotNull(loadedBeta, "beta sub-asset が再ロード後も TryFind で取得できる必要があります。");
            Assert.AreEqual("beta", loadedBeta.Label);
            Assert.AreEqual(2, loadedBeta.SchemaVersion);

            var loadedGamma = loaded.TryFind<FakeGammaSettings>("gamma");
            Assert.IsNotNull(loadedGamma, "gamma sub-asset が再ロード後も TryFind で取得できる必要があります。");
            Assert.AreEqual("gamma", loadedGamma.Label);
            Assert.AreEqual(3, loadedGamma.SchemaVersion);
        }

        [Test]
        public void RemoveSubAsset_MiddleEntry_PreservesRemainingLabelAndSchemaVersion()
        {
            var collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();
            AssetDatabase.CreateAsset(collection, _collectionAssetPath);

            var alpha = AddSubAsset<FakeAlphaSettings>(collection, "alpha", schemaVersion: 11);
            var beta = AddSubAsset<FakeBetaSettings>(collection, "beta", schemaVersion: 22);
            var gamma = AddSubAsset<FakeGammaSettings>(collection, "gamma", schemaVersion: 33);
            AppendToItems(collection, alpha, beta, gamma);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_collectionAssetPath, ImportAssetOptions.ForceUpdate);

            var preRemove = AssetDatabase.LoadAssetAtPath<AdapterRuntimeSettingsCollectionSO>(_collectionAssetPath);
            Assert.AreEqual(3, preRemove.Items.Count, "削除前は 3 件揃っている必要があります。");

            var middle = preRemove.TryFind<FakeBetaSettings>("beta");
            Assert.IsNotNull(middle, "削除対象の beta sub-asset を取得できる必要があります。");

            RemoveSubAssetFromCollection(preRemove, middle);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_collectionAssetPath, ImportAssetOptions.ForceUpdate);

            var loaded = AssetDatabase.LoadAssetAtPath<AdapterRuntimeSettingsCollectionSO>(_collectionAssetPath);
            Assert.IsNotNull(loaded, "削除後の Collection を再ロードできませんでした。");
            Assert.AreEqual(2, loaded.Items.Count, "中央 sub-asset 削除後は 2 件残る必要があります。");

            var loadedAlpha = loaded.TryFind<FakeAlphaSettings>("alpha");
            Assert.IsNotNull(loadedAlpha, "alpha sub-asset が削除後も TryFind で取得できる必要があります。");
            Assert.AreEqual("alpha", loadedAlpha.Label, "alpha の _label は削除前と同値である必要があります。");
            Assert.AreEqual(11, loadedAlpha.SchemaVersion, "alpha の _schemaVersion は削除前と同値である必要があります。");

            var loadedGamma = loaded.TryFind<FakeGammaSettings>("gamma");
            Assert.IsNotNull(loadedGamma, "gamma sub-asset が削除後も TryFind で取得できる必要があります。");
            Assert.AreEqual("gamma", loadedGamma.Label, "gamma の _label は削除前と同値である必要があります。");
            Assert.AreEqual(33, loadedGamma.SchemaVersion, "gamma の _schemaVersion は削除前と同値である必要があります。");

            Assert.IsNull(loaded.TryFind<FakeBetaSettings>("beta"), "削除対象 beta は再ロード後に存在しない必要があります。");
        }

        private static T AddSubAsset<T>(
            AdapterRuntimeSettingsCollectionSO collection,
            string label,
            int schemaVersion)
            where T : AdapterRuntimeSettingsBase
        {
            var sub = ScriptableObject.CreateInstance<T>();
            sub.name = label;
            AssetDatabase.AddObjectToAsset(sub, collection);

            var so = new SerializedObject(sub);
            so.FindProperty("_label").stringValue = label;
            so.FindProperty("_schemaVersion").intValue = schemaVersion;
            so.ApplyModifiedPropertiesWithoutUndo();
            return sub;
        }

        private static void AppendToItems(
            AdapterRuntimeSettingsCollectionSO collection,
            params AdapterRuntimeSettingsBase[] subs)
        {
            var so = new SerializedObject(collection);
            var items = so.FindProperty("_items");
            var startIndex = items.arraySize;
            items.arraySize = startIndex + subs.Length;
            for (var i = 0; i < subs.Length; i++)
            {
                items.GetArrayElementAtIndex(startIndex + i).objectReferenceValue = subs[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RemoveSubAssetFromCollection(
            AdapterRuntimeSettingsCollectionSO collection,
            AdapterRuntimeSettingsBase target)
        {
            var index = collection.IndexOf(target);
            Assert.GreaterOrEqual(index, 0, "削除対象が _items に存在する必要があります。");

            var so = new SerializedObject(collection);
            var items = so.FindProperty("_items");
            items.DeleteArrayElementAtIndex(index);
            // DeleteArrayElementAtIndex で参照型は最初 null 化されることがあるため、再評価する。
            if (items.arraySize > index
                && items.GetArrayElementAtIndex(index).objectReferenceValue == null)
            {
                items.DeleteArrayElementAtIndex(index);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.RemoveObjectFromAsset(target);
            Object.DestroyImmediate(target, allowDestroyingAssets: false);
        }
    }
}
