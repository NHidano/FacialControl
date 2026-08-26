using System;
using System.Collections;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Samples.MultiSourceBlendBasicSample
{
    [Serializable]
    [FacialAdapterBinding(displayName: "Mock Trigger")]
    public sealed class MockTriggerAdapterBinding : AdapterBindingBase
    {
        public float Blink = 1.0f;
        public float Smile = 0.2f;
        public float MouthOpen = 0.0f;

        public override void OnStart(in AdapterBuildContext ctx)
        {
            var slug = AdapterSlug.Parse(Slug);
            var source = new MockTriggerInputSource(
                slug.Value,
                ctx.BlendShapeNames.Count,
                Blink,
                Smile,
                MouthOpen);

            ctx.InputSourceRegistry.Register(slug, source);
        }

        private sealed class MockTriggerInputSource : IInputSource
        {
            private readonly float _blink;
            private readonly float _smile;
            private readonly float _mouthOpen;

            public MockTriggerInputSource(
                string id,
                int blendShapeCount,
                float blink,
                float smile,
                float mouthOpen)
            {
                Id = id;
                BlendShapeCount = blendShapeCount;
                ContributeMask = new BitArray(blendShapeCount, true);
                _blink = Clamp01(blink);
                _smile = Clamp01(smile);
                _mouthOpen = Clamp01(mouthOpen);
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ExpressionTrigger;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }

            public bool TryWriteValues(Span<float> output)
            {
                WriteIfPresent(output, 0, _blink);
                WriteIfPresent(output, 1, _smile);
                WriteIfPresent(output, 2, _mouthOpen);
                return true;
            }

            private static void WriteIfPresent(Span<float> output, int index, float value)
            {
                if (index < output.Length)
                {
                    output[index] = value;
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
