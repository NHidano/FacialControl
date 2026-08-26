#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using System;
using System.IO;
using System.Linq;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.LipSync.Adapters;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests
{
    [TestFixture]
    public class FacialCharacterProfileSO_RoutingAutoWireJsonRoundTripTests
    {
        [Test]
        public void AutoWire_OverlayLayer_ExportProfileJsonWithoutManualFix_PreservesWiringAndSlots()
        {
            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            string assetName = "RoutingAutoWireRoundTrip_" + Guid.NewGuid().ToString("N");
            string profilePath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(assetName);
            string profileDirectory = Path.GetDirectoryName(profilePath);

            try
            {
                profile.name = assetName;
                profile.Layers.Add(new LayerDefinitionSerializable
                {
                    name = "overlay",
                    inputSources =
                    {
                        new InputSourceDeclarationSerializable
                        {
                            id = "lipsync-overlay:a",
                            weight = 0.25f,
                            optionsJson = "{\"keep\":true}",
                        },
                    },
                });

                AddReferencedDefaultOverlays(profile);

                var autoWireService = new AutoWireService();
                var serializedObject = new SerializedObject(profile);
                autoWireService.AutoWire(
                    serializedObject,
                    new ULipSyncAdapterBinding(),
                    profile.Layers.Select(layer => layer.name).ToArray());

                bool exported = FacialCharacterProfileExporter.ExportProfileJson(profile);

                Assert.That(exported, Is.True, "自動配線後の SO は手修正なしで profile.json を出力できる必要があります。");
                Assert.That(File.Exists(profilePath), Is.True, "Exporter は profile.json を生成する必要があります。");

                string json = File.ReadAllText(profilePath);
                ProfileSnapshotDto parsed = new SystemTextJsonParser().ParseProfileSnapshotV2(json);

                Assert.That(parsed.layers, Has.Count.EqualTo(1));
                Assert.That(parsed.layers[0].name, Is.EqualTo("overlay"));
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "lipsync-overlay:a",
                        "lipsync-overlay:i",
                        "lipsync-overlay:u",
                        "lipsync-overlay:e",
                        "lipsync-overlay:o",
                    },
                    parsed.layers[0].inputSources.Select(source => source.id).ToArray());
                Assert.That(parsed.layers[0].inputSources[0].weight, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(parsed.layers[0].inputSources.Skip(1).All(source => Mathf.Approximately(source.weight, 1f)), Is.True);
                Assert.That(parsed.layers[0].inputSources[0].optionsJson, Is.EqualTo("{\"keep\":true}"));

                CollectionAssert.AreEqual(
                    new[] { "a", "i", "u", "e", "o" },
                    parsed.slots);
                CollectionAssert.AreEqual(
                    new[] { "a", "i", "u", "e", "o" },
                    parsed.defaultOverlays.Select(overlay => overlay.slot).ToArray());
            }
            finally
            {
                if (!string.IsNullOrEmpty(profileDirectory) && Directory.Exists(profileDirectory))
                {
                    Directory.Delete(profileDirectory, true);
                }

                Object.DestroyImmediate(profile);
            }
        }

        private static void AddReferencedDefaultOverlays(FacialCharacterProfileSO profile)
        {
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "a", suppress = true });
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "i", suppress = true });
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "u", suppress = true });
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "e", suppress = true });
            profile.DefaultOverlays.Add(new OverlaySlotBindingSerializable { slot = "o", suppress = true });
        }
    }
}
#endif
