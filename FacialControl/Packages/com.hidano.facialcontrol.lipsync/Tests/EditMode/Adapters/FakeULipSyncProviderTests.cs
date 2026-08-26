using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    public class FakeULipSyncProviderTests
    {
        [Test]
        public void TryComposePhonemeWeights_WhenScripted_ReturnsScriptedWeights()
        {
            var provider = new FakeULipSyncProvider(4);
            provider.SetPhonemeWeights("A", 0.25f, 0.5f, 0.75f, 1f);
            var output = new float[4];

            bool result = provider.TryComposePhonemeWeights("A", output);

            Assert.That(result, Is.True);
            Assert.That(output, Is.EqualTo(new[] { 0.25f, 0.5f, 0.75f, 1f }));
        }

        [Test]
        public void IsActive_WhenStopped_ReturnsFalse()
        {
            var provider = new FakeULipSyncProvider(2);

            provider.SetActive(false);

            Assert.That(provider.IsActive, Is.False);
        }
    }
}
