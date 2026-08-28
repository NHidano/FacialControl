using System;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// 1 件の <see cref="GazeBindingConfig"/> と、それを駆動する <see cref="IAnalogInputSource"/> のペア。
    /// <see cref="GazeBonePoseProvider"/> へ渡すための値オブジェクトで、入力源解決の責務を呼出側に閉じ込める。
    /// </summary>
    /// <remarks>
    /// <para>
    /// InputSystem 経路では <c>ExpressionBindingEntry.actionName</c>（bindingMode = Gaze）を
    /// sourceId として事前に解決済みの <see cref="IAnalogInputSource"/> をペアにする。
    /// OSC・ARKit 経路ではそれぞれの sourceId 体系で解決する。本構造体は入力方式に依存しない。
    /// </para>
    /// </remarks>
    public readonly struct GazeBoneBinding
    {
        public GazeChannel Channel { get; }
        public IAnalogInputSource Source { get; }
        public IAnalogInputSource LeftSource { get; }
        public IAnalogInputSource RightSource { get; }

        public GazeBoneBinding(GazeChannel channel, IAnalogInputSource source)
            : this(channel, source, source)
        {
        }

        public GazeBoneBinding(
            GazeChannel channel,
            IAnalogInputSource leftSource,
            IAnalogInputSource rightSource)
        {
            Channel = channel;
            Source = leftSource ?? rightSource;
            LeftSource = leftSource;
            RightSource = rightSource;
        }

        [Obsolete("GazeChannel を使用してください。")]
        public GazeBoneBinding(GazeBindingConfig config, IAnalogInputSource source)
            : this(ToChannel(config), source, source)
        {
        }

        [Obsolete("GazeChannel を使用してください。")]
        public GazeBoneBinding(
            GazeBindingConfig config,
            IAnalogInputSource leftSource,
            IAnalogInputSource rightSource)
            : this(ToChannel(config), leftSource, rightSource)
        {
        }

        private static GazeChannel ToChannel(GazeBindingConfig config)
        {
            if (config == null) return null;
            return new GazeChannel
            {
                id = config.expressionId,
                useDistinctLeftRight = config.useDistinctLeftRight,
                sourceIdLeft = config.sourceIdLeft,
                sourceIdRight = config.sourceIdRight,
                leftEyeBonePath = config.leftEyeBonePath,
                leftEyeInitialRotation = config.leftEyeInitialRotation,
                leftEyeYawAxisLocal = config.leftEyeYawAxisLocal,
                leftEyePitchAxisLocal = config.leftEyePitchAxisLocal,
                rightEyeBonePath = config.rightEyeBonePath,
                rightEyeInitialRotation = config.rightEyeInitialRotation,
                rightEyeYawAxisLocal = config.rightEyeYawAxisLocal,
                rightEyePitchAxisLocal = config.rightEyePitchAxisLocal,
                lookUpAngle = config.lookUpAngle,
                lookDownAngle = config.lookDownAngle,
                outerYawAngle = config.outerYawAngle,
                innerYawAngle = config.innerYawAngle
            };
        }
    }
}
