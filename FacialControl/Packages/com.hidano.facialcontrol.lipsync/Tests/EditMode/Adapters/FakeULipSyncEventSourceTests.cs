using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    public class FakeULipSyncEventSourceTests
    {
        [Test]
        public void Invoke_SubscribedHandler_CallsHandlerEachTime()
        {
            var source = new FakeULipSyncEventSource();
            var info = new uLipSync.LipSyncInfo();
            var receivedInfo = default(uLipSync.LipSyncInfo);
            var callCount = 0;

            source.OnLipSyncUpdate += received =>
            {
                receivedInfo = received;
                callCount++;
            };

            source.Invoke(info);
            source.Invoke(info);

            Assert.That(callCount, Is.EqualTo(2));
            Assert.That(receivedInfo, Is.EqualTo(info));
        }
    }
}
