using System;
using System.IO;
using Hidano.FacialControl.Rec.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.FileSystem
{
    /// <summary>
    /// Reads REC sidecar files from disk and reports recovery or failure through Unity logs.
    /// </summary>
    public static class RecFileReader
    {
        public static bool TryRead(string filePath, out RecBinaryFormat.ReadResult result)
        {
            result = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                Debug.LogError("REC load failed because filePath was null or empty.");
                return false;
            }

            if (!File.Exists(filePath))
            {
                Debug.LogError($"REC load failed because file '{filePath}' did not exist.");
                return false;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                Debug.LogError($"REC load failed while reading '{filePath}': {ex.Message}");
                return false;
            }

            if (!RecBinaryFormat.TryRead(bytes, out result, out string error))
            {
                Debug.LogError($"REC load failed for '{filePath}': {error}");
                return false;
            }

            if (result.RecoveredFromTruncatedTail)
            {
                Debug.LogWarning($"REC load recovered a truncated tail for '{filePath}'.");
            }

            return true;
        }
    }
}
