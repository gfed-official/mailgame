using System.Collections.Generic;
using System.Text.Json;

namespace PerformativeMail.Sim.Content
{
    /// <summary>Collects validation errors while reading one content file.</summary>
    internal sealed class JsonCtx
    {
        public string File { get; }
        public List<string> Errors { get; }

        public JsonCtx(string file, List<string> errors) { File = file; Errors = errors; }

        public void Error(string message) => Errors.Add($"{File}: {message}");
    }

    internal static class J
    {
        public static readonly JsonDocumentOptions Options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static JsonDocument? Parse(JsonCtx ctx, string text)
        {
            try { return JsonDocument.Parse(text, Options); }
            catch (JsonException e) { ctx.Error($"invalid JSON: {e.Message}"); return null; }
        }

        public static bool Has(JsonElement obj, string name) =>
            obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out _);

        public static string Str(JsonCtx ctx, JsonElement obj, string name, string? fallback = null)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString()!;
            if (fallback != null && !obj.TryGetProperty(name, out _)) return fallback;
            ctx.Error($"missing or non-string field '{name}'");
            return fallback ?? "";
        }

        public static double Num(JsonCtx ctx, JsonElement obj, string name, double? fallback = null)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            if (fallback.HasValue && !obj.TryGetProperty(name, out _)) return fallback.Value;
            ctx.Error($"missing or non-numeric field '{name}'");
            return fallback ?? 0;
        }

        public static int Int(JsonCtx ctx, JsonElement obj, string name, int? fallback = null)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) return i;
            if (fallback.HasValue && !obj.TryGetProperty(name, out _)) return fallback.Value;
            ctx.Error($"missing or non-integer field '{name}'");
            return fallback ?? 0;
        }

        public static bool Bool(JsonCtx ctx, JsonElement obj, string name, bool? fallback = null)
        {
            if (obj.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
                return v.GetBoolean();
            if (fallback.HasValue && !obj.TryGetProperty(name, out _)) return fallback.Value;
            ctx.Error($"missing or non-boolean field '{name}'");
            return fallback ?? false;
        }

        public static double[] NumArray(JsonCtx ctx, JsonElement obj, string name, int expectedLength = -1)
        {
            if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            {
                ctx.Error($"missing or non-array field '{name}'");
                return new double[expectedLength < 0 ? 0 : expectedLength];
            }
            return NumArrayOf(ctx, v, name, expectedLength);
        }

        public static double[] NumArrayOf(JsonCtx ctx, JsonElement array, string label, int expectedLength = -1)
        {
            var list = new List<double>();
            foreach (var e in array.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Number) { ctx.Error($"'{label}' must contain only numbers"); list.Add(0); }
                else list.Add(e.GetDouble());
            }
            if (expectedLength >= 0 && list.Count != expectedLength)
            {
                ctx.Error($"'{label}' must have {expectedLength} elements, has {list.Count}");
                while (list.Count < expectedLength) list.Add(0);
            }
            return list.ToArray();
        }

        public static int[] IntArray(JsonCtx ctx, JsonElement obj, string name, int expectedLength = -1)
        {
            double[] d = NumArray(ctx, obj, name, expectedLength);
            var result = new int[d.Length];
            for (int i = 0; i < d.Length; i++)
            {
                result[i] = (int)d[i];
                if (result[i] != d[i]) ctx.Error($"'{name}' must contain integers");
            }
            return result;
        }

        public static List<string> StrArray(JsonCtx ctx, JsonElement obj, string name, bool optional = false)
        {
            var list = new List<string>();
            if (!obj.TryGetProperty(name, out var v))
            {
                if (!optional) ctx.Error($"missing array field '{name}'");
                return list;
            }
            if (v.ValueKind != JsonValueKind.Array) { ctx.Error($"'{name}' must be an array"); return list; }
            foreach (var e in v.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String) ctx.Error($"'{name}' must contain only strings");
                else list.Add(e.GetString()!);
            }
            return list;
        }
    }
}
