using System.Collections;

namespace Hidano.FacialControl.Tests.Shared
{
    /// <summary>
    /// contribution mask 対応前の既存 fake が旧 lerp 互換の全 index contribute を表現するための helper。
    /// </summary>
    public static class ContributeMaskTestHelper
    {
        public static BitArray AllSetContributeMask(int blendShapeCount)
        {
            return new BitArray(blendShapeCount, true);
        }
    }
}
