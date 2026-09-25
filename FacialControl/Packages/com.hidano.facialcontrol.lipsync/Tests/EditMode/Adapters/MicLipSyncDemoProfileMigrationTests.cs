using System.IO;
using System.Linq;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    public sealed class MicLipSyncDemoProfileMigrationTests
    {
        private const string ProfileRelativePath = "StreamingAssets/FacialControl/MicLipSyncDemoProfile/profile.json";

        [Test]
        public void Load_MicLipSyncDemoProfileJson_ContainsFivePhonemeSlots()
        {
            FacialProfile profile = LoadProfile();

            CollectionAssert.AreEqual(
                new[]
                {
                    PhonemeOverlaySlots.A,
                    PhonemeOverlaySlots.I,
                    PhonemeOverlaySlots.U,
                    PhonemeOverlaySlots.E,
                    PhonemeOverlaySlots.O
                },
                profile.Slots.ToArray());
        }

        [Test]
        public void Load_MicLipSyncDemoProfileJson_OverlayLayerHasLipSyncOverlayIds()
        {
            FacialProfile profile = LoadProfile();

            var overlayLayer = profile.FindLayerByName("overlay");
            Assert.That(overlayLayer.HasValue, Is.True);

            InputSourceDeclaration[] overlayInputs = profile.LayerInputSources.Span[0];
            CollectionAssert.AreEqual(
                new[]
                {
                    "overlay:a",
                    "lipsync-overlay:a",
                    "overlay:i",
                    "lipsync-overlay:i",
                    "overlay:u",
                    "lipsync-overlay:u",
                    "overlay:e",
                    "lipsync-overlay:e",
                    "overlay:o",
                    "lipsync-overlay:o"
                },
                overlayInputs.Select(input => input.Id).ToArray());
        }

        [Test]
        public void Load_MicLipSyncDemoProfileJson_DoesNotReferenceLegacyUlipsyncSlug()
        {
            string json = LoadJson();

            Assert.That(json, Does.Not.Contain(@"""ulipsync"""));
            Assert.DoesNotThrow(() => LoadProfile());
        }

        private static FacialProfile LoadProfile()
        {
            return new SystemTextJsonParser().ParseProfile(LoadJson());
        }

        private static string LoadJson()
        {
            string path = Path.Combine(UnityEngine.Application.dataPath, ProfileRelativePath);
            return File.ReadAllText(path);
        }
    }
}
