using System.Reflection;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests;

/// <summary>
/// Shared op-descriptor check (ADR-0007 sections 1, 4, 5), reused by every system's descriptor test. For one
/// <see cref="OpDescriptor"/> it verifies: the kind is non-zero and inside its owner's <see cref="OpKindBlock"/>;
/// the payload type is value-only with <c>SizeOf&lt;T&gt; &lt;= 48</c>, equal to <c>PayloadLength</c>; the sum of the
/// field sizes (recursing into nested structs and inline arrays) equals <c>SizeOf&lt;T&gt;</c>, i.e. the struct is
/// padding-free; and every declared reason is at least <see cref="CoreReason.FirstSystemReason"/> and unique.
/// Test-side only (uses reflection).
/// </summary>
/// <example>
/// <code>
/// DescriptorAssert.Check(OpDescriptor.For&lt;MyPayload&gt;(OpKind.PlayerJoin, false, 1, 8), OpKindBlock.Core);
/// </code>
/// </example>
internal static class DescriptorAssert
{
    /// <summary>Largest legal payload in bytes.</summary>
    public const int MaxPayloadBytes = OpPayload.Size;

    /// <summary>Fails the current test with every violation of <see cref="Validate"/>, if there are any.</summary>
    /// <param name="descriptor">The descriptor to check.</param>
    /// <param name="ownerBlock">The block the descriptor's owning system may use.</param>
    public static void Check(OpDescriptor descriptor, OpKindBlock ownerBlock)
    {
        IReadOnlyList<string> violations = Validate(descriptor, ownerBlock);
        if (violations.Count > 0)
        {
            Assert.Fail($"Descriptor for {descriptor.Kind} is invalid: " + string.Join("; ", violations));
        }
    }

    /// <summary>Lists every rule the descriptor breaks (empty when valid).</summary>
    /// <param name="descriptor">The descriptor to check.</param>
    /// <param name="ownerBlock">The block the descriptor's owning system may use.</param>
    /// <returns>Human-readable violations.</returns>
    public static IReadOnlyList<string> Validate(OpDescriptor descriptor, OpKindBlock ownerBlock)
    {
        var violations = new List<string>();

        if (descriptor.Kind == OpKind.None)
        {
            violations.Add("kind is None (0 is invalid)");
        }
        else if (!ownerBlock.Contains(descriptor.Kind))
        {
            violations.Add($"kind 0x{(ushort)descriptor.Kind:X4} is outside the {ownerBlock.Name} block");
        }

        Type? payload = descriptor.PayloadType;
        if (payload is null)
        {
            if (descriptor.PayloadLength != 0)
            {
                violations.Add($"payload-free kind declares PayloadLength {descriptor.PayloadLength}");
            }
        }
        else
        {
            CheckPayload(payload, descriptor.PayloadLength, violations);
        }

        var seen = new HashSet<ushort>();
        foreach (ReasonDef reason in descriptor.Reasons)
        {
            if (reason.Code < CoreReason.FirstSystemReason)
            {
                violations.Add($"reason {reason.Code} ('{reason.Name}') is below {CoreReason.FirstSystemReason}");
            }

            if (!seen.Add(reason.Code))
            {
                violations.Add($"reason {reason.Code} is declared twice");
            }
        }

        return violations;
    }

    /// <summary>Sums the sizes of a type's instance fields, recursing into structs and inline arrays.</summary>
    /// <param name="type">A value type.</param>
    /// <returns>The sum in bytes; compare with <c>Unsafe.SizeOf</c> to detect padding.</returns>
    public static int SumOfFieldSizes(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type.IsPointer)
        {
            return SizeOf(type);
        }

        InlineArrayAttribute? inline = type.GetCustomAttribute<InlineArrayAttribute>();
        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (inline is not null)
        {
            return inline.Length * SumOfFieldSizes(fields[0].FieldType);
        }

        int sum = 0;
        foreach (FieldInfo field in fields)
        {
            FixedBufferAttribute? fixedBuffer = field.GetCustomAttribute<FixedBufferAttribute>();
            sum += fixedBuffer is not null
                ? fixedBuffer.Length * SumOfFieldSizes(fixedBuffer.ElementType)
                : SumOfFieldSizes(field.FieldType);
        }

        return sum;
    }

    /// <summary><c>Unsafe.SizeOf</c> for a runtime type.</summary>
    /// <param name="type">An unmanaged type.</param>
    /// <returns>Size in bytes.</returns>
    public static int SizeOf(Type type)
    {
        MethodInfo method = typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type);
        return (int)method.Invoke(null, null)!;
    }

    internal static void CheckPayload(Type payload, byte declaredLength, List<string> violations)
    {
        string? forbidden = FindForbidden(payload);
        if (forbidden is not null)
        {
            violations.Add($"{payload.Name} is not a plain-integer value type: {forbidden}");
            return;
        }

        int size = SizeOf(payload);
        if (size > MaxPayloadBytes)
        {
            violations.Add($"{payload.Name} is {size} B, over the {MaxPayloadBytes} B payload");
        }

        if (size != declaredLength)
        {
            violations.Add($"{payload.Name} is {size} B but PayloadLength is {declaredLength}");
        }

        int sum = SumOfFieldSizes(payload);
        if (sum != size)
        {
            violations.Add($"{payload.Name} has padding: fields sum to {sum} B but size is {size} B");
        }
    }

    /// <summary>
    /// Finds the first thing that makes a payload type unfit: a reference, pointer, <c>nint</c>/<c>nuint</c>,
    /// <c>Int128</c>/<c>UInt128</c>, floating-point type (<c>float</c>, <c>double</c>, <c>Half</c>, <c>decimal</c>) or an
    /// auto-layout struct, at any nesting depth. Fixed buffers are accepted and sized by <see cref="SumOfFieldSizes"/>.
    /// </summary>
    private static string? FindForbidden(Type type)
    {
        if (type == typeof(nint) || type == typeof(nuint))
        {
            return "nint/nuint is platform-sized";
        }

        if (type.IsPointer)
        {
            return "pointers are not allowed";
        }

        if (type == typeof(Int128) || type == typeof(UInt128))
        {
            return "Int128/UInt128 is not allowed";
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(Half) || type == typeof(decimal))
        {
            return "floating-point types are not allowed";
        }

        if (type.IsPrimitive || type.IsEnum)
        {
            return null;
        }

        if (!type.IsValueType)
        {
            return "reference types are not allowed";
        }

        if (type.IsAutoLayout)
        {
            return "LayoutKind.Auto has no stable layout";
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            FixedBufferAttribute? fixedBuffer = field.GetCustomAttribute<FixedBufferAttribute>();
            string? problem = FindForbidden(fixedBuffer is not null ? fixedBuffer.ElementType : field.FieldType);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }
}
