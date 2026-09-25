#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    public class PhonemeEntrySerializeReferenceSmokeTests
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_PhonemeEntrySerializeReferenceSmoke";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/PhonemeEntrySerializeReferenceSmoke_" + Guid.NewGuid().ToString("N") + ".asset";
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

        [UnityTest]
        public IEnumerator SerializeReference_NestedPhonemeEntries_RoundTripPreservesConcreteTypesAndFields()
        {
            var so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            var clip = new AnimationClip { name = "RoundTrip_AnimationClipPhoneme" };
            var binding = new ULipSyncAdapterBinding { Slug = "ulipsync" };
            binding.PhonemeEntries.Add(new BlendShapePhonemeEntry
            {
                PhonemeId = "A",
                MaxWeight = 83.25f,
                BlendShapeName = "Mouth_A",
            });
            binding.PhonemeEntries.Add(new AnimationClipPhonemeEntry
            {
                PhonemeId = "O",
                MaxWeight = 61.5f,
                Clip = clip,
            });
            so.WritableAdapterBindings.Add(binding);

            AssetDatabase.CreateAsset(so, _assetPath);
            AssetDatabase.AddObjectToAsset(clip, so);
            EditorUtility.SetDirty(so);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            so = null;
            clip = null;
            binding = null;

            yield return Resources.UnloadUnusedAssets();

            var loaded = AssetDatabase.LoadAssetAtPath<TestFacialCharacterProfileSO>(_assetPath);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.AdapterBindings, Is.Not.Null);
            Assert.That(loaded.AdapterBindings.Count, Is.EqualTo(1));
            Assert.That(loaded.AdapterBindings[0], Is.InstanceOf<ULipSyncAdapterBinding>());

            var loadedBinding = (ULipSyncAdapterBinding)loaded.AdapterBindings[0];
            Assert.That(loadedBinding.Slug, Is.EqualTo("ulipsync"));
            Assert.That(loadedBinding.PhonemeEntries, Is.Not.Null);
            Assert.That(loadedBinding.PhonemeEntries.Count, Is.EqualTo(2));
            Assert.That(loadedBinding.PhonemeEntries[0], Is.InstanceOf<BlendShapePhonemeEntry>());
            Assert.That(loadedBinding.PhonemeEntries[1], Is.InstanceOf<AnimationClipPhonemeEntry>());

            var blendShapeEntry = (BlendShapePhonemeEntry)loadedBinding.PhonemeEntries[0];
            Assert.That(blendShapeEntry.PhonemeId, Is.EqualTo("A"));
            Assert.That(blendShapeEntry.MaxWeight, Is.EqualTo(83.25f).Within(1e-6f));
            Assert.That(blendShapeEntry.BlendShapeName, Is.EqualTo("Mouth_A"));

            var animationClipEntry = (AnimationClipPhonemeEntry)loadedBinding.PhonemeEntries[1];
            Assert.That(animationClipEntry.PhonemeId, Is.EqualTo("O"));
            Assert.That(animationClipEntry.MaxWeight, Is.EqualTo(61.5f).Within(1e-6f));
            Assert.That(animationClipEntry.Clip, Is.Not.Null);
            Assert.That(animationClipEntry.Clip.name, Is.EqualTo("RoundTrip_AnimationClipPhoneme"));

            using (var serialized = new SerializedObject(loaded))
            {
                var adapterBindings = serialized.FindProperty("_adapterBindings");
                Assert.That(adapterBindings, Is.Not.Null);
                Assert.That(adapterBindings.arraySize, Is.EqualTo(1));

                var bindingElement = adapterBindings.GetArrayElementAtIndex(0);
                Assert.That(bindingElement.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(bindingElement.managedReferenceValue, Is.Not.Null);
                Assert.That(bindingElement.managedReferenceFullTypename,
                    Does.Contain(typeof(ULipSyncAdapterBinding).FullName));

                var phonemeEntries = bindingElement.FindPropertyRelative(ULipSyncAdapterBinding.PhonemeEntriesFieldName);
                Assert.That(phonemeEntries, Is.Not.Null);
                Assert.That(phonemeEntries.arraySize, Is.EqualTo(2));

                var blendShapeElement = phonemeEntries.GetArrayElementAtIndex(0);
                Assert.That(blendShapeElement.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(blendShapeElement.managedReferenceValue, Is.Not.Null);
                Assert.That(blendShapeElement.managedReferenceFullTypename,
                    Does.Contain(typeof(BlendShapePhonemeEntry).FullName));

                var animationClipElement = phonemeEntries.GetArrayElementAtIndex(1);
                Assert.That(animationClipElement.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(animationClipElement.managedReferenceValue, Is.Not.Null);
                Assert.That(animationClipElement.managedReferenceFullTypename,
                    Does.Contain(typeof(AnimationClipPhonemeEntry).FullName));
            }
        }
    }

    [Serializable]
    public sealed class ULipSyncAdapterBinding : AdapterBindingBase
    {
        public const string PhonemeEntriesFieldName = "_phonemeEntries";

        [SerializeReference] private List<PhonemeEntryBase> _phonemeEntries = new List<PhonemeEntryBase>();

        public List<PhonemeEntryBase> PhonemeEntries => _phonemeEntries;
    }
}
#endif
