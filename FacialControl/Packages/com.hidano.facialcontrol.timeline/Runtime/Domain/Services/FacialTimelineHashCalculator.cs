using System;
using System.Text;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Domain.Services
{
    public static class FacialTimelineHashCalculator
    {
        public const float DefaultSampleRate = 60f;

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static ulong ComputeHash(
            TimelineAsset timeline,
            FacialProfile profile,
            float sampleRate = DefaultSampleRate)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            Fnv1A64Writer writer = Fnv1A64Writer.Create();
            writer.WriteString("facial-timeline-hash/v1");

            WriteTimeline(ref writer, timeline);
            WriteProfile(ref writer, profile);
            writer.WriteSingle(sampleRate);

            return writer.Hash;
        }

        public static string ComputeHashHex(
            TimelineAsset timeline,
            FacialProfile profile,
            float sampleRate = DefaultSampleRate)
        {
            return ComputeHash(timeline, profile, sampleRate).ToString("x16");
        }

        private static void WriteTimeline(ref Fnv1A64Writer writer, TimelineAsset timeline)
        {
            writer.WriteString("timeline");

            foreach (TrackAsset rootTrack in timeline.GetOutputTracks())
            {
                WriteTrackRecursive(ref writer, rootTrack, depth: 0);
            }
        }

        private static void WriteTrackRecursive(ref Fnv1A64Writer writer, TrackAsset track, int depth)
        {
            if (track == null)
            {
                return;
            }

            if (track is FacialExpressionTrack expressionTrack)
            {
                WriteExpressionTrack(ref writer, expressionTrack, depth);
            }
            else if (track is FacialValueTrack valueTrack)
            {
                WriteValueTrack(ref writer, valueTrack, depth);
            }

            foreach (TrackAsset childTrack in track.GetChildTracks())
            {
                WriteTrackRecursive(ref writer, childTrack, depth + 1);
            }
        }

        private static void WriteExpressionTrack(
            ref Fnv1A64Writer writer,
            FacialExpressionTrack track,
            int depth)
        {
            writer.WriteString("expression-track");
            writer.WriteInt32(depth);

            TimelineClip[] clips = CopyClips(track);
            writer.WriteInt32(clips.Length);

            for (int i = 0; i < clips.Length; i++)
            {
                TimelineClip clip = clips[i];
                var asset = clip.asset as FacialExpressionClip;

                writer.WriteString("expression-clip");
                writer.WriteDouble(clip.start);
                writer.WriteDouble(clip.duration);
                writer.WriteString(asset != null ? asset.ExpressionId : string.Empty);
            }
        }

        private static void WriteValueTrack(
            ref Fnv1A64Writer writer,
            FacialValueTrack track,
            int depth)
        {
            writer.WriteString("value-track");
            writer.WriteInt32(depth);
            writer.WriteString(track.ChannelSubId);
            writer.WriteInt32((int)track.ChannelKind);

            TimelineClip[] clips = CopyClips(track);
            writer.WriteInt32(clips.Length);

            for (int i = 0; i < clips.Length; i++)
            {
                TimelineClip clip = clips[i];
                var asset = clip.asset as FacialValueClip;
                AnimationCurve[] axes = asset != null ? asset.Axes : Array.Empty<AnimationCurve>();

                writer.WriteString("value-clip");
                writer.WriteDouble(clip.start);
                writer.WriteDouble(clip.duration);
                writer.WriteInt32(axes.Length);

                for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
                {
                    WriteCurve(ref writer, axes[axisIndex]);
                }
            }
        }

        private static void WriteProfile(ref Fnv1A64Writer writer, FacialProfile profile)
        {
            writer.WriteString("profile");
            writer.WriteString(profile.SchemaVersion);

            Expression[] expressions = Copy(profile.Expressions);
            Array.Sort(expressions, CompareExpressionById);

            writer.WriteInt32(expressions.Length);

            for (int i = 0; i < expressions.Length; i++)
            {
                Expression expression = expressions[i];
                writer.WriteString(expression.Id);
                writer.WriteString(expression.Layer);
                writer.WriteInt32((int)expression.OverrideMask);
                writer.WriteString(expression.SnapshotId);
                writer.WriteSingle(expression.TransitionDuration);

                TransitionCurve transitionCurve = expression.TransitionCurve;
                writer.WriteInt32((int)transitionCurve.Type);
                CurveKeyFrame[] transitionKeys = Copy(transitionCurve.Keys);
                writer.WriteInt32(transitionKeys.Length);
                for (int keyIndex = 0; keyIndex < transitionKeys.Length; keyIndex++)
                {
                    CurveKeyFrame key = transitionKeys[keyIndex];
                    writer.WriteSingle(key.Time);
                    writer.WriteSingle(key.Value);
                    writer.WriteSingle(key.InTangent);
                    writer.WriteSingle(key.OutTangent);
                }

                BlendShapeMapping[] mappings = Copy(expression.BlendShapeValues);
                writer.WriteInt32(mappings.Length);
                for (int mappingIndex = 0; mappingIndex < mappings.Length; mappingIndex++)
                {
                    BlendShapeMapping mapping = mappings[mappingIndex];
                    writer.WriteString(mapping.Name);
                    writer.WriteSingle(mapping.Value);
                    writer.WriteString(mapping.Renderer);
                }
            }
        }

        private static int CompareExpressionById(Expression left, Expression right)
        {
            return StringComparer.Ordinal.Compare(left.Id, right.Id);
        }

        private static TimelineClip[] CopyClips(TrackAsset track)
        {
            int count = 0;
            foreach (TimelineClip _ in track.GetClips())
            {
                count++;
            }

            if (count == 0)
            {
                return Array.Empty<TimelineClip>();
            }

            var clips = new TimelineClip[count];
            int index = 0;
            foreach (TimelineClip clip in track.GetClips())
            {
                clips[index++] = clip;
            }

            return clips;
        }

        private static T[] Copy<T>(ReadOnlyMemory<T> memory)
        {
            if (memory.Length == 0)
            {
                return Array.Empty<T>();
            }

            var copy = new T[memory.Length];
            memory.Span.CopyTo(copy);
            return copy;
        }

        private static void WriteCurve(ref Fnv1A64Writer writer, AnimationCurve curve)
        {
            writer.WriteBoolean(curve != null);
            if (curve == null)
            {
                return;
            }

            writer.WriteInt32((int)curve.preWrapMode);
            writer.WriteInt32((int)curve.postWrapMode);

            Keyframe[] keys = curve.keys;
            writer.WriteInt32(keys.Length);
            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                Keyframe key = keys[keyIndex];
                writer.WriteSingle(key.time);
                writer.WriteSingle(key.value);
                writer.WriteSingle(key.inTangent);
                writer.WriteSingle(key.outTangent);
                writer.WriteSingle(key.inWeight);
                writer.WriteSingle(key.outWeight);
                writer.WriteInt32((int)key.weightedMode);
            }
        }

        private struct Fnv1A64Writer
        {
            private static readonly byte[] EmptyStringBytes = Array.Empty<byte>();

            public ulong Hash;

            public static Fnv1A64Writer Create()
            {
                return new Fnv1A64Writer
                {
                    Hash = FnvOffsetBasis,
                };
            }

            public void WriteBoolean(bool value)
            {
                WriteByte(value ? (byte)1 : (byte)0);
            }

            public void WriteByte(byte value)
            {
                Hash ^= value;
                Hash *= FnvPrime;
            }

            public void WriteInt32(int value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public void WriteSingle(float value)
            {
                WriteInt32(BitConverter.SingleToInt32Bits(value));
            }

            public void WriteDouble(double value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public void WriteString(string value)
            {
                if (value == null)
                {
                    WriteBoolean(false);
                    return;
                }

                WriteBoolean(true);
                byte[] bytes = value.Length == 0 ? EmptyStringBytes : Encoding.UTF8.GetBytes(value);
                WriteInt32(bytes.Length);
                WriteBytes(bytes);
            }

            private void WriteBytes(byte[] bytes)
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    WriteByte(bytes[i]);
                }
            }
        }
    }
}
