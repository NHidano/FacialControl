using System;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Domain.Services;

namespace Hidano.FacialControl.Timeline.Adapters.InputSources
{
    /// <summary>
    /// Timeline の active 表情状態だけを既存入力パイプラインへ供給する trigger sink。
    /// blendShapeCount を 0 に固定し、値出力は構造的に発生させない。
    /// </summary>
    public sealed class TimelineExpressionStateSink : ExpressionTriggerInputSourceBase, ITimelineTriggerSink
    {
        private static readonly string[] EmptyBlendShapeNames = Array.Empty<string>();

        public TimelineExpressionStateSink(
            InputSourceId id,
            int maxStackDepth,
            ExclusionMode exclusionMode,
            FacialProfile profile)
            : base(
                id,
                blendShapeCount: 0,
                maxStackDepth: maxStackDepth,
                exclusionMode: exclusionMode,
                blendShapeNames: EmptyBlendShapeNames,
                profile: profile)
        {
        }
    }
}
