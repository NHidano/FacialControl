using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class LayerSlotTests
    {
        [Test]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            var values = new[]
            {
                new BlendShapeMapping("Fcl_MTH_A", 0.5f)
            };

            var slot = new LayerSlot("lipsync", values);

            Assert.AreEqual("lipsync", slot.Layer);
            Assert.AreEqual(1, slot.BlendShapeValues.Length);
            Assert.AreEqual("Fcl_MTH_A", slot.BlendShapeValues.Span[0].Name);
            Assert.AreEqual(0.5f, slot.BlendShapeValues.Span[0].Value);
        }

        [Test]
        public void Constructor_MultipleBlendShapeValues_CreatesInstance()
        {
            var values = new[]
            {
                new BlendShapeMapping("Fcl_MTH_A", 0.3f),
                new BlendShapeMapping("Fcl_MTH_O", 0.7f),
                new BlendShapeMapping("Fcl_MTH_U", 1.0f)
            };

            var slot = new LayerSlot("lipsync", values);

            Assert.AreEqual(3, slot.BlendShapeValues.Length);
            Assert.AreEqual("Fcl_MTH_A", slot.BlendShapeValues.Span[0].Name);
            Assert.AreEqual("Fcl_MTH_O", slot.BlendShapeValues.Span[1].Name);
            Assert.AreEqual("Fcl_MTH_U", slot.BlendShapeValues.Span[2].Name);
        }

        [Test]
        public void Constructor_EmptyBlendShapeValues_CreatesInstance()
        {
            var slot = new LayerSlot("emotion", Array.Empty<BlendShapeMapping>());

            Assert.AreEqual("emotion", slot.Layer);
            Assert.AreEqual(0, slot.BlendShapeValues.Length);
        }

        [Test]
        public void Constructor_NullLayer_ThrowsArgumentNullException()
        {
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            Assert.Throws<ArgumentNullException>(() =>
                new LayerSlot(null, values));
        }

        [Test]
        public void Constructor_EmptyLayer_ThrowsArgumentException()
        {
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            Assert.Throws<ArgumentException>(() =>
                new LayerSlot("", values));
        }

        [Test]
        public void Constructor_WhitespaceLayer_ThrowsArgumentException()
        {
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            Assert.Throws<ArgumentException>(() =>
                new LayerSlot("   ", values));
        }

        [Test]
        public void Constructor_NullBlendShapeValues_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LayerSlot("lipsync", null));
        }

        [Test]
        public void Constructor_JapaneseLayerName_CreatesInstance()
        {
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            var slot = new LayerSlot("リップシンク", values);

            Assert.AreEqual("リップシンク", slot.Layer);
        }

        [Test]
        public void Constructor_SpecialCharacterLayerName_CreatesInstance()
        {
            var values = new[] { new BlendShapeMapping("test", 0.5f) };

            var slot = new LayerSlot("layer-01_test", values);

            Assert.AreEqual("layer-01_test", slot.Layer);
        }

        [Test]
        public void BlendShapeValues_IsDefensiveCopy_OriginalArrayModificationDoesNotAffect()
        {
            var values = new[]
            {
                new BlendShapeMapping("Fcl_MTH_A", 0.5f)
            };

            var slot = new LayerSlot("lipsync", values);

            // 元配列を変更しても LayerSlot の値は変わらない
            values[0] = new BlendShapeMapping("modified", 1.0f);

            Assert.AreEqual("Fcl_MTH_A", slot.BlendShapeValues.Span[0].Name);
            Assert.AreEqual(0.5f, slot.BlendShapeValues.Span[0].Value);
        }
    }
}
