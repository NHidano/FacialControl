using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    public sealed class AddressPresetKindTests
    {
        [Test]
        public void EnumValues_SerializedValues_AreStable()
        {
            Assert.AreEqual(0, (int)AddressPresetKind.VRChat);
            Assert.AreEqual(1, (int)AddressPresetKind.ARKit);
            Assert.AreEqual(2, (int)AddressPresetKind.Custom);
        }
    }
}
