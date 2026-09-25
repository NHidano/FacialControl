using System;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector.AdapterBindings
{
    // ---------------------------------------------------------------
    // Mock 型定義
    // namespace scope に置いて FQTN を安定化させる（[SerializeReference]
    // round-trip の concrete type 解決に必要）。
    // ---------------------------------------------------------------

    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_ListViewTest_AAA_Simple")]
    public sealed class MockListViewSimpleBinding : AdapterBindingBase { }

    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_ListViewTest_BBB_Other")]
    public sealed class MockListViewOtherBinding : AdapterBindingBase { }

    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_ListViewTest_CCC_ThrowingDrawer")]
    public sealed class MockListViewThrowingDrawerBinding : AdapterBindingBase { }

    /// <summary>
    /// SerializeField を持つ binding。AddBindingFromDescriptor が SerializedProperty 経由で
    /// 追加していないと、子要素 `_settingsObject` の SerializedProperty が初回 Drawer 描画時に
    /// null となり、PropertyField が DOM に生成されない現象 (= 「スロットが出ない」) を再現する。
    /// </summary>
    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_ListViewTest_DDD_HasSerializedField")]
    public sealed class MockListViewWithSerializedField : AdapterBindingBase
    {
        [SerializeField]
        public UnityEngine.Object _settingsObject;
    }

    /// <summary>
    /// task 5.6 で <c>MockListViewThrowingDrawerBinding</c> 用の PropertyDrawer が
    /// CreatePropertyGUI 内で例外を投げることをシミュレートする。
    /// </summary>
    /// <remarks>
    /// task 5.3 では <see cref="AdapterBindingsListView"/> が未実装のため、
    /// 本 PropertyDrawer は task 5.4 (Green) のテスト fixture でのみ意味を持つ。
    /// </remarks>
    [CustomPropertyDrawer(typeof(MockListViewThrowingDrawerBinding))]
    public sealed class MockListViewThrowingDrawerBindingDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            throw new InvalidOperationException(
                "Intentional PropertyDrawer exception from MockListViewThrowingDrawerBindingDrawer for task 5.3");
        }
    }

    /// <summary>
    /// MockListViewWithSerializedField 用 Drawer。
    /// `property.FindPropertyRelative("_settingsObject")` で子 SerializedProperty を取得し
    /// PropertyField を生成する。SerializeReference 追加直後でも子 SerializedProperty が
    /// 解決できれば PropertyField が DOM に配置される。
    /// </summary>
    [CustomPropertyDrawer(typeof(MockListViewWithSerializedField))]
    public sealed class MockListViewWithSerializedFieldDrawer : PropertyDrawer
    {
        public const string SettingsFieldElementName = "mock-listview-settings-field";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            SerializedProperty settingsProp = property.FindPropertyRelative("_settingsObject");
            if (settingsProp == null)
            {
                root.Add(new Label("<missing field: _settingsObject>"));
                return root;
            }
            root.Add(new PropertyField(settingsProp, "Settings Object")
            {
                name = SettingsFieldElementName,
            });
            return root;
        }
    }

    /// <summary>
    /// task 5.3 の観測可能完了条件: <see cref="AdapterBindingsListView"/> が
    /// Add ドロップダウン経由の append + slug auto-populate、Remove + dirty 化、
    /// Reorder、null 要素時の <see cref="MissingAdapterPlaceholderElement"/>、
    /// 同 SO 内 slug 重複時の error class + summary banner、
    /// PropertyDrawer 例外時の per-element fallback element を行うことを assert する
    /// 。
    /// </summary>
    /// <remarks>
    /// 本ファイルは Red 段階のテストであり、
    /// <c>AdapterBindingsListView</c> および <c>MissingAdapterPlaceholderElement</c>
    /// がまだ未実装のため、コンパイル時に CS0246 / CS0234 が発生して Red 状態となる
    /// （task 5.4 の Green 化対象）。
    /// </remarks>
    [TestFixture]
    public class AdapterBindingsListViewTests
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_AdapterBindingsListViewTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;
        private TestFacialCharacterProfileSO _so;
        private SerializedObject _serializedObject;
        private SerializedProperty _listProperty;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/AdapterBindingsListViewTests_" + Guid.NewGuid().ToString("N") + ".asset";
            _so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            AssetDatabase.CreateAsset(_so, _assetPath);
            AssetDatabase.SaveAssets();

            _serializedObject = new SerializedObject(_so);
            _listProperty = _serializedObject.FindProperty("_adapterBindings");
            Assert.IsNotNull(_listProperty,
                "_adapterBindings SerializedProperty が解決できない。task 4.2 で field が追加済みである必要がある。");
        }

        [TearDown]
        public void TearDown()
        {
            _serializedObject = null;
            _listProperty = null;
            _so = null;

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

        // ---------------------------------------------------------------
        // ヘルパー
        // ---------------------------------------------------------------

        private AdapterBindingsListView CreateView()
        {
            var view = new AdapterBindingsListView(_listProperty);

            // bindItem が走るよう panel に attach する。
            // EditorWindow を使わずに済むよう、テスト用の VisualElement を unparented で
            // 使うことも可能だが、UI Toolkit の ListView は panel attach が前提の挙動を
            // 持つため、AdapterBindingsListView 側でも attach 後 rebuild を行う前提とする。
            return view;
        }

        private static AdapterBindingDescriptor RequireDescriptor(Type concreteType)
        {
            var descriptor = AdapterBindingDiscovery.FindByType(concreteType);
            Assert.IsTrue(descriptor.HasValue,
                $"AdapterBindingDiscovery.FindByType({concreteType.FullName}) は non-null を返さなければならない。");
            return descriptor.Value;
        }

        // ---------------------------------------------------------------
        // Add ドロップダウン → Activator.CreateInstance + slug auto-populate
        // ---------------------------------------------------------------

        [Test]
        public void AddBindingFromDescriptor_AppendsConcreteInstanceWithSlugAutoPopulated()
        {
            var view = CreateView();
            var descriptor = RequireDescriptor(typeof(MockListViewSimpleBinding));

            view.AddBindingFromDescriptor(descriptor);

            // 適用後、SerializedObject の _adapterBindings には 1 件追加されているはず。
            _serializedObject.Update();
            Assert.AreEqual(1, _listProperty.arraySize,
                "AddBindingFromDescriptor 後に _adapterBindings の要素数は 1 になるべき。");

            var element = _listProperty.GetArrayElementAtIndex(0);
            Assert.IsNotNull(element.managedReferenceValue,
                "Add 後の要素は Activator.CreateInstance で生成された concrete instance を持つべき。");
            Assert.IsInstanceOf<MockListViewSimpleBinding>(element.managedReferenceValue,
                "Add 後の要素は descriptor.Type と同じ concrete type であるべき 。");

            // Slug が AdapterSlug.FromDisplayName(descriptor.OriginalDisplayName) 由来であることを確認 。
            var expectedSlug = AdapterSlug.FromDisplayName(descriptor.OriginalDisplayName).Value;
            var added = (MockListViewSimpleBinding)element.managedReferenceValue;
            Assert.AreEqual(expectedSlug, added.Slug,
                "Slug は AdapterSlug.FromDisplayName(displayName) で auto-populate されるべき 。");
        }

        [Test]
        public void AddBindingFromDescriptor_SameTypeTwice_AppendsTwoIndependentInstances()
        {
            // 同型 binding を複数追加できる。
            var view = CreateView();
            var descriptor = RequireDescriptor(typeof(MockListViewSimpleBinding));

            view.AddBindingFromDescriptor(descriptor);
            view.AddBindingFromDescriptor(descriptor);

            _serializedObject.Update();
            Assert.AreEqual(2, _listProperty.arraySize,
                "同型 descriptor を 2 回追加すると _adapterBindings の要素数は 2 になるべき 。");

            var first = _listProperty.GetArrayElementAtIndex(0).managedReferenceValue;
            var second = _listProperty.GetArrayElementAtIndex(1).managedReferenceValue;

            Assert.IsInstanceOf<MockListViewSimpleBinding>(first);
            Assert.IsInstanceOf<MockListViewSimpleBinding>(second);
            Assert.IsFalse(ReferenceEquals(first, second),
                "同型でも独立した instance が生成されるべき。");
        }

        // ---------------------------------------------------------------
        // Remove ボタン → list から削除 + dirty 化
        // ---------------------------------------------------------------

        [Test]
        public void RemoveBindingAt_RemovesElementAndMarksAssetDirty()
        {
            var view = CreateView();
            var descA = RequireDescriptor(typeof(MockListViewSimpleBinding));
            var descB = RequireDescriptor(typeof(MockListViewOtherBinding));

            view.AddBindingFromDescriptor(descA);
            view.AddBindingFromDescriptor(descB);

            // dirty 状態を一度クリアして remove に伴う再 dirty 化を観測しやすくする。
            EditorUtility.ClearDirty(_so);
            Assert.IsFalse(EditorUtility.IsDirty(_so),
                "ClearDirty 後の SO は IsDirty=false でなければならない (前提条件)。");

            view.RemoveBindingAt(0);

            _serializedObject.Update();
            Assert.AreEqual(1, _listProperty.arraySize,
                "Remove 後の _adapterBindings の要素数は 1 になるべき 。");

            var remaining = _listProperty.GetArrayElementAtIndex(0).managedReferenceValue;
            Assert.IsInstanceOf<MockListViewOtherBinding>(remaining,
                "index 0 を Remove したら、index 1 だった MockListViewOtherBinding が残るべき。");

            Assert.IsTrue(EditorUtility.IsDirty(_so),
                "Remove 後は SO が dirty 化されているべき 。");
        }

        // ---------------------------------------------------------------
        // 並び替え機能は +/- フッター移行で削除された。
        // 必要になったら別タスクで再導入する。
        // ---------------------------------------------------------------

        // ---------------------------------------------------------------
        // null 要素 → MissingAdapterPlaceholderElement
        // ---------------------------------------------------------------

        [Test]
        public void NullElement_RendersMissingAdapterPlaceholderForRow()
        {
            // managedReferenceValue が null の要素は型欠落 placeholder で描画される。
            // _so.WritableAdapterBindings に null を直接追加し、SerializedObject を再構築する。
            _so.WritableAdapterBindings.Add(null);
            EditorUtility.SetDirty(_so);
            _serializedObject = new SerializedObject(_so);
            _listProperty = _serializedObject.FindProperty("_adapterBindings");

            var view = new AdapterBindingsListView(_listProperty);

            // bindItem が呼ばれるよう panel attach + layout を強制する経路として、
            // AdapterBindingsListView は内部に Q<MissingAdapterPlaceholderElement>() で
            // 検出可能な要素を一つ以上保持しているはず。
            var placeholders = view.Query<MissingAdapterPlaceholderElement>().ToList();
            Assert.GreaterOrEqual(placeholders.Count, 1,
                "null 要素を含む list では MissingAdapterPlaceholderElement が少なくとも 1 件描画されるべき 。");
        }

        // ---------------------------------------------------------------
        // 同 SO 内 slug 重複
        // ---------------------------------------------------------------

        [Test]
        public void DuplicateSlug_AddsErrorClassToRowsAndShowsSummaryBanner()
        {
            // 同じ slug を持つ binding を 2 件追加する。
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "duplicate-slug" });
            _so.WritableAdapterBindings.Add(new MockListViewOtherBinding { Slug = "duplicate-slug" });
            EditorUtility.SetDirty(_so);
            _serializedObject = new SerializedObject(_so);
            _listProperty = _serializedObject.FindProperty("_adapterBindings");

            var view = new AdapterBindingsListView(_listProperty);

            // 重複 row には CSS class facial-control-error が付与される。
            // bindItem 後の row VisualElement に対して Q で error class 要素を取得する。
            var errorRows = view.Query(className: AdapterBindingsListView.ErrorRowClassName).ToList();
            Assert.GreaterOrEqual(errorRows.Count, 2,
                $"slug 重複時は重複している全 row に '{AdapterBindingsListView.ErrorRowClassName}' class が付与されるべき 。");

            // Summary banner が表示されている。
            var banner = view.Q<VisualElement>(name: AdapterBindingsListView.SummaryBannerName);
            Assert.IsNotNull(banner,
                "slug 重複時は Inspector 上端の summary banner が表示されるべき 。");
            Assert.IsTrue(banner.resolvedStyle.display != DisplayStyle.None || banner.style.display != DisplayStyle.None,
                "summary banner は display:none ではなく可視であるべき。");
        }

        [Test]
        public void NoDuplicateSlug_DoesNotAddErrorClassNorShowBanner()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "alpha" });
            _so.WritableAdapterBindings.Add(new MockListViewOtherBinding { Slug = "beta" });
            EditorUtility.SetDirty(_so);
            _serializedObject = new SerializedObject(_so);
            _listProperty = _serializedObject.FindProperty("_adapterBindings");

            var view = new AdapterBindingsListView(_listProperty);

            var errorRows = view.Query(className: AdapterBindingsListView.ErrorRowClassName).ToList();
            Assert.AreEqual(0, errorRows.Count,
                "slug 重複が無い場合、error class が付与された row は無いべき。");

            var banner = view.Q<VisualElement>(name: AdapterBindingsListView.SummaryBannerName);
            if (banner != null)
            {
                // banner 自体が常設で display:none で隠す実装も許容する。
                bool isHiddenInline = banner.style.display == DisplayStyle.None;
                bool isHiddenResolved = banner.resolvedStyle.display == DisplayStyle.None;
                Assert.IsTrue(isHiddenInline || isHiddenResolved,
                    "slug 重複が無い場合、summary banner は非表示 (display:none) であるべき。");
            }
        }

        // ---------------------------------------------------------------
        // PropertyDrawer 例外時の per-element fallback
        // ---------------------------------------------------------------

        [Test]
        public void ThrowingPropertyDrawer_RendersFallbackElementWithoutBreakingOtherRows()
        {
            // PropertyDrawer 例外を bindItem 内で catch + per-element fallback element 表示。
            //         他 row の描画が止まらないこと。
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "ok-front" });
            _so.WritableAdapterBindings.Add(new MockListViewThrowingDrawerBinding { Slug = "boom" });
            _so.WritableAdapterBindings.Add(new MockListViewOtherBinding { Slug = "ok-back" });
            EditorUtility.SetDirty(_so);
            _serializedObject = new SerializedObject(_so);
            _listProperty = _serializedObject.FindProperty("_adapterBindings");

            var view = new AdapterBindingsListView(_listProperty);

            // fallback element が少なくとも 1 件描画されている。
            var fallbackElements = view.Query(className: AdapterBindingsListView.FallbackRowClassName).ToList();
            Assert.GreaterOrEqual(fallbackElements.Count, 1,
                $"PropertyDrawer 例外時は '{AdapterBindingsListView.FallbackRowClassName}' class を持つ fallback element が描画されるべき 。");

            // 他 row の描画が止まっていないこと: 例外を投げない 2 row は通常 PropertyField を持つはず。
            var propertyFields = view.Query<PropertyField>().ToList();
            Assert.GreaterOrEqual(propertyFields.Count, 2,
                "PropertyDrawer 例外を投げない他 row は通常通り PropertyField で描画されているべき 。");
        }

        // ---------------------------------------------------------------
        // SerializeReference 追加直後の Drawer 描画タイミング
        // ---------------------------------------------------------------

        [Test]
        public void AddBindingFromDescriptor_DrawerWithSerializedField_CreatesPropertyFieldInRowImmediately()
        {
            // AdapterBindingsListView.AddBindingFromDescriptor が SerializedProperty 経由で
            // 追加していない場合 (古い list.Add(instance) 経路) は、子 SerializedProperty
            // `_settingsObject` が初回 Rebuild 時に解決できず、Drawer は missing label を出して
            // PropertyField (= ObjectField のスロット) が DOM に生成されない。
            // この regression を防ぐため、追加直後に PropertyField が存在することを確認する。
            var view = CreateView();
            var descriptor = RequireDescriptor(typeof(MockListViewWithSerializedField));

            view.AddBindingFromDescriptor(descriptor);

            var propertyField = view.Q<PropertyField>(name: MockListViewWithSerializedFieldDrawer.SettingsFieldElementName);
            Assert.IsNotNull(propertyField,
                "SerializeReference 追加直後でも Drawer の PropertyField が DOM に存在するべき。" +
                " AdapterBindingsListView.AddBindingFromDescriptor が SerializedProperty 経由で追加していない場合、" +
                " Drawer 内の FindPropertyRelative が null を返し missing label が出る現象を防ぐ。");
        }
    }
}
