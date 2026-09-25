using System;
using System.Collections;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Samples.MultiSourceBlendBasicSample
{
    [Serializable]
    [FacialAdapterBinding(displayName: "Mock Analog")]
    public sealed class MockAnalogAdapterBinding : AdapterBindingBase
    {
        public float Scale = 1.0f;
        public float Blink = 0.0f;
        public float Smile = 0.7f;
        public float MouthOpen = 0.9f;

        public override void OnStart(in AdapterBuildContext ctx)
        {
            var slug = AdapterSlug.Parse(Slug);
            var source = new MockAnalogInputSource(
                slug.Value,
                ctx.BlendShapeNames.Count,
                Scale,
                Blink,
                Smile,
                MouthOpen);

            ctx.InputSourceRegistry.Register(slug, source);
        }

        private sealed class MockAnalogInputSource : IInputSource
        {
            private readonly float _scale;
            private readonly float _blink;
            private readonly float _smile;
            private readonly float _mouthOpen;

            public MockAnalogInputSource(
                string id,
                int blendShapeCount,
                float scale,
                float blink,
                float smile,
                float mouthOpen)
            {
                Id = id;
                BlendShapeCount = blendShapeCount;
                ContributeMask = new BitArray(blendShapeCount, true);
                _scale = Clamp01(scale);
                _blink = Clamp01(blink);
                _smile = Clamp01(smile);
                _mouthOpen = Clamp01(mouthOpen);
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }

            public bool TryWriteValues(Span<float> output)
            {
                WriteIfPresent(output, 0, _blink * _scale);
                WriteIfPresent(output, 1, _smile * _scale);
                WriteIfPresent(output, 2, _mouthOpen * _scale);
                return true;
            }

            private static void WriteIfPresent(Span<float> output, int index, float value)
            {
                if (index < output.Length)
                {
                    output[index] = Clamp01(value);
                }
            }

            private static float Clamp01(float value)
            {
                if (value < 0f) return 0f;
                if (value > 1f) return 1f;
                return value;
            }
        }
    }
}
