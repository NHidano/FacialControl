using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class FacialProfileSlotsTests
    {
        [Test]
        public void Constructor_NullSlots_InitializesEmptySlots()
        {
            var profile = new FacialProfile("1.0", slots: null);

            Assert.AreEqual(0, profile.Slots.Length);
        }

        [Test]
        public void ValidateSlotReferences_DuplicateSlots_ReturnsDuplicateReference()
        {
            var profile = new FacialProfile(
                "1.0",
                slots: new[] { "blink", "blink" });

            var invalidRefs = profile.ValidateSlotReferences();

            Assert.AreEqual(1, invalidRefs.Count);
            AssertInvalidReference(
                invalidRefs[0],
                "blink",
                InvalidSlotReference.DuplicateReason);
        }

        [Test]
        public void ValidateSlotReferences_ExpressionOverlayUndeclaredSlot_ReturnsUndeclaredReference()
        {
            var expression = CreateExpressionWithOverlay("mouth");
            var profile = new FacialProfile(
                "1.0",
                expressions: new[] { expression },
                slots: new[] { "blink" });

            var invalidRefs = profile.ValidateSlotReferences();

            Assert.AreEqual(1, invalidRefs.Count);
            AssertInvalidReference(
                invalidRefs[0],
                "mouth",
                InvalidSlotReference.UndeclaredReason);
        }

        [Test]
        public void ValidateSlotReferences_DefaultOverlayUndeclaredSlot_ReturnsUndeclaredReference()
        {
            var profile = new FacialProfile(
                "1.0",
                defaultOverlays: new[]
                {
                    new OverlaySlotBinding("mouth", suppress: false, snapshot: null),
                },
                slots: new[] { "blink" });

            var invalidRefs = profile.ValidateSlotReferences();

            Assert.AreEqual(1, invalidRefs.Count);
            AssertInvalidReference(
                invalidRefs[0],
                "mouth",
                InvalidSlotReference.UndeclaredReason);
        }

        [Test]
        public void Slots_IsDefensiveCopy_OriginalArrayModificationDoesNotAffect()
        {
            var slots = new[] { "blink" };
            var profile = new FacialProfile("1.0", slots: slots);

            slots[0] = "mouth";

            Assert.AreEqual("blink", profile.Slots.Span[0]);
        }

        private static Expression CreateExpressionWithOverlay(string slot)
        {
            return new Expression(
                id: "smile",
                name: "Smile",
                layer: "emotion",
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurve: default,
                blendShapeValues: null,
                overlays: new[]
                {
                    new OverlaySlotBinding(slot, suppress: false, snapshot: null),
                });
        }

        private static void AssertInvalidReference(
            InvalidSlotReference reference,
            string slot,
            string reason)
        {
            Assert.AreEqual(slot, reference.Slot);
            Assert.AreEqual(reason, reference.Reason);
        }
    }
}
