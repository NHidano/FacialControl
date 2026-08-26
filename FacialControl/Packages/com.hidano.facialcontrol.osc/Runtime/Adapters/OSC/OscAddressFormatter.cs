using System;
using System.Collections.Generic;
using System.Text;

namespace Hidano.FacialControl.Adapters.OSC
{
    public static class OscAddressFormatter
    {
        public const string VRChatParameterPrefix = "/avatar/parameters/";
        public const string ARKitParameterPrefix = "/ARKit/";
        public const char VRChatGazeXAxis = 'X';
        public const char VRChatGazeYAxis = 'Y';

        public static int GetBlendShapeAddressLength(
            AddressPresetKind preset,
            ReadOnlySpan<char> blendShapeName)
        {
            return GetBlendShapePrefix(preset).Length + blendShapeName.Length;
        }

        public static bool TryFormatBlendShapeAddress(
            AddressPresetKind preset,
            ReadOnlySpan<char> blendShapeName,
            Span<char> destination,
            out int charsWritten)
        {
            ReadOnlySpan<char> prefix = GetBlendShapePrefix(preset);
            charsWritten = prefix.Length + blendShapeName.Length;

            if (destination.Length < charsWritten)
            {
                return false;
            }

            prefix.CopyTo(destination);
            blendShapeName.CopyTo(destination.Slice(prefix.Length));
            return true;
        }

        public static string FormatBlendShapeAddress(AddressPresetKind preset, string blendShapeName)
        {
            return GetBlendShapePrefix(preset).ToString() + (blendShapeName ?? string.Empty);
        }

        public static byte[] FormatBlendShapeAddressUtf8(AddressPresetKind preset, string blendShapeName)
        {
            return Encoding.UTF8.GetBytes(FormatBlendShapeAddress(preset, blendShapeName));
        }

        public static string FormatBlendShapeAddress(string customPrefix, string blendShapeName)
        {
            ValidateCustomPrefix(customPrefix);
            return customPrefix + (blendShapeName ?? string.Empty);
        }

        public static byte[] FormatBlendShapeAddressUtf8(string customPrefix, string blendShapeName)
        {
            return Encoding.UTF8.GetBytes(FormatBlendShapeAddress(customPrefix, blendShapeName));
        }

        public static byte[] GetOrAddBlendShapeAddressUtf8(
            Dictionary<(string name, AddressPresetKind preset), byte[]> addressBytesPool,
            AddressPresetKind preset,
            string blendShapeName)
        {
            if (addressBytesPool == null)
            {
                throw new ArgumentNullException(nameof(addressBytesPool));
            }

            string name = blendShapeName ?? string.Empty;
            var key = (name, preset);
            if (addressBytesPool.TryGetValue(key, out byte[] addressBytes))
            {
                return addressBytes;
            }

            addressBytes = FormatBlendShapeAddressUtf8(preset, name);
            addressBytesPool.Add(key, addressBytes);
            return addressBytes;
        }

        public static byte[] GetOrAddBlendShapeAddressUtf8(
            Dictionary<(string name, string customPrefix), byte[]> addressBytesPool,
            string customPrefix,
            string blendShapeName)
        {
            if (addressBytesPool == null)
            {
                throw new ArgumentNullException(nameof(addressBytesPool));
            }

            ValidateCustomPrefix(customPrefix);
            string name = blendShapeName ?? string.Empty;
            var key = (name, customPrefix);
            if (addressBytesPool.TryGetValue(key, out byte[] addressBytes))
            {
                return addressBytes;
            }

            addressBytes = FormatBlendShapeAddressUtf8(customPrefix, name);
            addressBytesPool.Add(key, addressBytes);
            return addressBytes;
        }

        public static int GetGazeAddressLength(
            AddressPresetKind preset,
            ReadOnlySpan<char> expressionId,
            char axis)
        {
            ValidateGazeAxis(axis);
            return GetGazePrefix(preset).Length + expressionId.Length + 1;
        }

        public static bool TryFormatGazeAddress(
            AddressPresetKind preset,
            ReadOnlySpan<char> expressionId,
            char axis,
            Span<char> destination,
            out int charsWritten)
        {
            ValidateGazeAxis(axis);
            ReadOnlySpan<char> prefix = GetGazePrefix(preset);
            charsWritten = prefix.Length + expressionId.Length + 1;

            if (destination.Length < charsWritten)
            {
                return false;
            }

            prefix.CopyTo(destination);
            expressionId.CopyTo(destination.Slice(prefix.Length));
            destination[charsWritten - 1] = axis;
            return true;
        }

        public static string FormatGazeAddress(AddressPresetKind preset, string expressionId, char axis)
        {
            ValidateGazeAxis(axis);
            return GetGazePrefix(preset).ToString() + (expressionId ?? string.Empty) + axis;
        }

        public static byte[] FormatGazeAddressUtf8(AddressPresetKind preset, string expressionId, char axis)
        {
            return Encoding.UTF8.GetBytes(FormatGazeAddress(preset, expressionId, axis));
        }

        public static byte[] GetOrAddGazeAddressUtf8(
            Dictionary<(string name, AddressPresetKind preset), byte[]> addressBytesPool,
            AddressPresetKind preset,
            string expressionId,
            char axis)
        {
            if (addressBytesPool == null)
            {
                throw new ArgumentNullException(nameof(addressBytesPool));
            }

            ValidateGazeAxis(axis);
            string name = (expressionId ?? string.Empty) + axis;
            var key = (name, preset);
            if (addressBytesPool.TryGetValue(key, out byte[] addressBytes))
            {
                return addressBytes;
            }

            addressBytes = FormatGazeAddressUtf8(preset, expressionId, axis);
            addressBytesPool.Add(key, addressBytes);
            return addressBytes;
        }

        private static ReadOnlySpan<char> GetBlendShapePrefix(AddressPresetKind preset)
        {
            switch (preset)
            {
                case AddressPresetKind.VRChat:
                    return VRChatParameterPrefix.AsSpan();
                case AddressPresetKind.ARKit:
                    return ARKitParameterPrefix.AsSpan();
                default:
                    throw new NotSupportedException($"{preset} address preset is not implemented for sender output yet.");
            }
        }

        private static ReadOnlySpan<char> GetGazePrefix(AddressPresetKind preset)
        {
            switch (preset)
            {
                case AddressPresetKind.VRChat:
                    return VRChatParameterPrefix.AsSpan();
                default:
                    throw new NotSupportedException($"{preset} address preset is not implemented for gaze sender output yet.");
            }
        }

        private static void ValidateCustomPrefix(string customPrefix)
        {
            if (string.IsNullOrEmpty(customPrefix))
            {
                throw new ArgumentException("Custom OSC address prefix must not be null or empty.", nameof(customPrefix));
            }
        }

        private static void ValidateGazeAxis(char axis)
        {
            if (axis != VRChatGazeXAxis && axis != VRChatGazeYAxis)
            {
                throw new ArgumentException("VRChat gaze axis must be X or Y.", nameof(axis));
            }
        }
    }
}
