using System.Collections;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class LayerBlenderMaskTests
    {
        private const float Tolerance = 1e-6f;

        [Test]
        public void Blend_PartialMask_KeepsPreviousOutputForFalseIndexes()
        {
            BitArray mask = new BitArray(new[] { true, false, true, false });
            var layers = new[]
            {
                LayerBlenderTestSupport.CreateLayerInput(
                    priority: 0,
                    weight: 1.0f,
                    values: new[] { 1.0f, 1.0f, 0.0f, 0.0f },
                    mask: mask),
            };
            var output = new[] { 0.2f, 0.4f, 0.6f, 0.8f };

            LayerBlender.Blend(layers, output);

            Assert.That(output, Is.EqualTo(new[] { 1.0f, 0.4f, 0.0f, 0.8f }).Within(Tolerance));
        }

        [Test]
        public void Blend_NullMask_TreatsLayerAsAllSet()
        {
            var layers = new[]
            {
                LayerBlenderTestSupport.CreateLayerInput(
                    priority: 0,
                    weight: 0.5f,
                    values: new[] { 0.2f, 0.8f, 1.0f },
                    mask: null),
                LayerBlenderTestSupport.CreateLayerInput(
                    priority: 1,
                    weight: 0.75f,
                    values: new[] { 1.0f, 0.0f, 0.5f },
                    mask: null),
            };
            var output = new[] { 0.9f, 0.9f, 0.9f };
            float[] expected = LayerBlenderTestSupport.LegacyBlend(
                new[]
                {
                    (priority: 0, weight: 0.5f, values: new[] { 0.2f, 0.8f, 1.0f }),
                    (priority: 1, weight: 0.75f, values: new[] { 1.0f, 0.0f, 0.5f }),
                },
                outputLength: output.Length);

            LayerBlender.Blend(layers, output);

            Assert.That(output, Is.EqualTo(expected).Within(Tolerance));
        }
    }
}
