using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    public sealed class GazeAdvertisementResolverTests
    {
        [Test]
        public void Parse_FlatPairs_ReturnsEntriesInPayloadOrder()
        {
            var entries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;

            GazeAdvertisementResolver.Parse(
                new[] { "eye_look", "ARKit_8BS", "gaze", "VRChat_XY" },
                entries,
                ref warned);

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].ExpressionId, Is.EqualTo("eye_look"));
            Assert.That(entries[0].Format, Is.EqualTo("ARKit_8BS"));
            Assert.That(entries[1].ExpressionId, Is.EqualTo("gaze"));
        }

        [Test]
        public void Parse_InvalidShapeAndEmptyIds_SkipsInvalidPairs()
        {
            var entries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;

            GazeAdvertisementResolver.Parse(
                new[] { "valid", "VRChat_XY", "orphan", "VRChat_XY", "", "ARKit_8BS" },
                entries,
                ref warned);

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].ExpressionId, Is.EqualTo("valid"));
            Assert.That(entries[1].ExpressionId, Is.EqualTo("orphan"));
            Assert.That(warned, Is.False);
        }

        [Test]
        public void Parse_UnknownFormat_SkipsPairAndWarnsOnlyOnce()
        {
            var entries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;

            GazeAdvertisementResolver.Parse(
                new[] { "bad", "Unknown", "also_bad", "Unknown", "good", "VRChat_XY" },
                entries,
                ref warned);
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].ExpressionId, Is.EqualTo("good"));

            GazeAdvertisementResolver.Parse(
                new[] { "still_bad", "Unknown" },
                entries,
                ref warned);

            Assert.That(entries, Is.Empty);
            Assert.That(warned, Is.True);
        }

        [Test]
        public void Parse_DuplicateIds_FirstPairWinsOrdinal()
        {
            var entries = new List<GazeAdvertisementResolver.GazeAdvertisement>();
            bool warned = false;

            GazeAdvertisementResolver.Parse(
                new[] { "gaze", "VRChat_XY", "gaze", "ARKit_8BS", "Gaze", "ARKit_8BS" },
                entries,
                ref warned);

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Format, Is.EqualTo("VRChat_XY"));
            Assert.That(entries[1].ExpressionId, Is.EqualTo("Gaze"));
        }

        [Test]
        public void ComputeNormalizedHash_OrderChangesDoNotChangeHash()
        {
            var first = new[]
            {
                new GazeAdvertisementResolver.GazeAdvertisement("z", "VRChat_XY"),
                new GazeAdvertisementResolver.GazeAdvertisement("a", "ARKit_8BS")
            };
            var second = new[]
            {
                new GazeAdvertisementResolver.GazeAdvertisement("a", "ARKit_8BS"),
                new GazeAdvertisementResolver.GazeAdvertisement("z", "VRChat_XY")
            };

            Assert.That(
                GazeAdvertisementResolver.ComputeNormalizedHash(first),
                Is.EqualTo(GazeAdvertisementResolver.ComputeNormalizedHash(second)));
        }

        [Test]
        public void ComputeNormalizedHash_ContentOrFormatChangesChangeHash()
        {
            var baseline = new[] { new GazeAdvertisementResolver.GazeAdvertisement("gaze", "VRChat_XY") };

            Assert.That(
                GazeAdvertisementResolver.ComputeNormalizedHash(
                    new[] { new GazeAdvertisementResolver.GazeAdvertisement("other", "VRChat_XY") }),
                Is.Not.EqualTo(GazeAdvertisementResolver.ComputeNormalizedHash(baseline)));
            Assert.That(
                GazeAdvertisementResolver.ComputeNormalizedHash(
                    new[] { new GazeAdvertisementResolver.GazeAdvertisement("gaze", "ARKit_8BS") }),
                Is.Not.EqualTo(GazeAdvertisementResolver.ComputeNormalizedHash(baseline)));
        }
    }
}
