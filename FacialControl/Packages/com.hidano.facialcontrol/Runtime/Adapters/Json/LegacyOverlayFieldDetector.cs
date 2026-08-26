using System;
using System.Collections.Generic;
using System.Text;

namespace Hidano.FacialControl.Adapters.Json
{
    internal static class LegacyOverlayFieldDetector
    {
        private const string LegacyFieldName = "expressionId";

        public static void RejectLegacyExpressionIdInOverlays(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            var stack = new List<Context>(16);
            int i = 0;

            while (i < json.Length)
            {
                char c = json[i];
                if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '{':
                        PushContext(stack, ContextKind.Object);
                        i++;
                        break;
                    case '[':
                        PushContext(stack, ContextKind.Array);
                        i++;
                        break;
                    case '}':
                        if (stack.Count > 0)
                        {
                            stack.RemoveAt(stack.Count - 1);
                            MarkValueCompleted(stack);
                        }
                        i++;
                        break;
                    case ']':
                        if (stack.Count > 0)
                        {
                            stack.RemoveAt(stack.Count - 1);
                            MarkValueCompleted(stack);
                        }
                        i++;
                        break;
                    case '"':
                        string value = ReadString(json, ref i);
                        if (IsObjectKey(json, i, stack))
                        {
                            var context = stack[stack.Count - 1];
                            if (string.Equals(value, LegacyFieldName, StringComparison.Ordinal)
                                && IsOverlayBindingScope(context.Path))
                            {
                                throw new FormatException(
                                    $"Legacy field 'expressionId' detected in OverlaySlotBinding scope (path={context.Path}.{LegacyFieldName})");
                            }

                            context.PendingProperty = value;
                            stack[stack.Count - 1] = context;
                            i = SkipColon(json, i);
                        }
                        else
                        {
                            MarkValueCompleted(stack);
                        }
                        break;
                    case ':':
                        i++;
                        break;
                    default:
                        SkipPrimitive(json, ref i);
                        MarkValueCompleted(stack);
                        break;
                }
            }
        }

        private static void PushContext(List<Context> stack, ContextKind kind)
        {
            string path = string.Empty;
            if (stack.Count > 0)
            {
                var parent = stack[stack.Count - 1];
                if (parent.Kind == ContextKind.Array)
                {
                    path = AppendIndex(parent.Path, parent.NextIndex);
                }
                else if (!string.IsNullOrEmpty(parent.PendingProperty))
                {
                    path = AppendProperty(parent.Path, parent.PendingProperty);
                    parent.PendingProperty = null;
                    stack[stack.Count - 1] = parent;
                }
            }

            stack.Add(new Context(kind, path));
        }

        private static void MarkValueCompleted(List<Context> stack)
        {
            if (stack.Count == 0)
                return;

            var parent = stack[stack.Count - 1];
            if (parent.Kind == ContextKind.Array)
            {
                parent.NextIndex++;
                stack[stack.Count - 1] = parent;
            }
            else
            {
                parent.PendingProperty = null;
                stack[stack.Count - 1] = parent;
            }
        }

        private static bool IsObjectKey(string json, int index, List<Context> stack)
        {
            if (stack.Count == 0 || stack[stack.Count - 1].Kind != ContextKind.Object)
                return false;

            int i = index;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            return i < json.Length && json[i] == ':';
        }

        private static int SkipColon(string json, int index)
        {
            int i = index;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i++;
            }

            return i < json.Length && json[i] == ':' ? i + 1 : index;
        }

        private static string ReadString(string json, ref int index)
        {
            var sb = new StringBuilder();
            index++;

            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '\\')
                {
                    if (index >= json.Length)
                        break;

                    char escaped = json[index++];
                    sb.Append(escaped);
                    continue;
                }

                if (c == '"')
                    break;

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static void SkipPrimitive(string json, ref int index)
        {
            while (index < json.Length)
            {
                char c = json[index];
                if (c == ',' || c == '}' || c == ']' || char.IsWhiteSpace(c))
                    break;
                index++;
            }
        }

        private static bool IsOverlayBindingScope(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (IsArrayElementAtPath(path, "defaultOverlays"))
                return true;

            return path.StartsWith("expressions[", StringComparison.Ordinal)
                && path.Contains("].snapshot.overlays[")
                && path[path.Length - 1] == ']';
        }

        private static bool IsArrayElementAtPath(string path, string propertyName)
        {
            if (!path.StartsWith(propertyName + "[", StringComparison.Ordinal))
                return false;

            int close = path.IndexOf(']', propertyName.Length + 1);
            return close >= 0 && close == path.Length - 1;
        }

        private static string AppendProperty(string basePath, string property)
        {
            return string.IsNullOrEmpty(basePath) ? property : basePath + "." + property;
        }

        private static string AppendIndex(string basePath, int index)
        {
            return basePath + "[" + index + "]";
        }

        private enum ContextKind
        {
            Object,
            Array,
        }

        private struct Context
        {
            public Context(ContextKind kind, string path)
            {
                Kind = kind;
                Path = path;
                NextIndex = 0;
                PendingProperty = null;
            }

            public ContextKind Kind;
            public string Path;
            public int NextIndex;
            public string PendingProperty;
        }
    }
}
