using NUnit.Framework;
using Hidano.FacialControl.Tests.Shared;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class ContributeMaskTestHelperTests
    {
        [Test]
        public void AllSetContributeMask_WithPositiveCount_ReturnsMaskWithMatchingLength()
        {
            var mask = ContributeMaskTestHelper.AllSetContributeMask(4);

            Assert.AreEqual(4, mask.Length);
        }

        [Test]
        public void AllSetContributeMask_WithPositiveCount_SetsEveryBit()
        {
            var mask = ContributeMaskTestHelper.AllSetContributeMask(5);

            for (int i = 0; i < mask.Length; i++)
            {
                Assert.IsTrue(mask[i], $"index {i} は true であること");
            }
        }

        [Test]
        public void AllSetContributeMask_WithZeroCount_ReturnsEmptyMask()
        {
            var mask = ContributeMaskTestHelper.AllSetContributeMask(0);

            Assert.AreEqual(0, mask.Length);
        }
    }
}
