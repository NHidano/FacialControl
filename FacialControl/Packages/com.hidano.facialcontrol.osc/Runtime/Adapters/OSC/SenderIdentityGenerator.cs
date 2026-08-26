using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    public static class SenderIdentityGenerator
    {
        public static SenderIdentity Generate()
        {
            return new SenderIdentity(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }
}
