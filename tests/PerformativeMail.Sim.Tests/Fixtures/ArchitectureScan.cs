using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace PerformativeMail.Sim.Tests;

/// <summary>
/// One finding of an SC-ARCH-1 rule engine (tests/unit/architecture). <see cref="Owner"/> is the nearest non-compiler-generated
/// type that holds the offending member, <see cref="Banned"/> what it names, <see cref="Where"/> the member (diagnostics only:
/// the seen-failing tests compare <see cref="Key"/>, which does not depend on compiler-generated member names).
/// </summary>
/// <param name="Owner">The owning type's name (nested types as <c>Outer+Inner</c>).</param>
/// <param name="Banned">What the member names or reads.</param>
/// <param name="Where">The member and position it was found in.</param>
public sealed record ArchViolation(string Owner, string Banned, string Where)
{
    /// <summary>The stable comparison key: <c>Owner: Banned</c>.</summary>
    public string Key => $"{Owner}: {Banned}";

    /// <inheritdoc />
    public override string ToString() => $"{Key} (in {Where})";
}

/// <summary>
/// Shared reflection helpers for the SC-ARCH-1 architecture rules: member signatures, constituent types (arrays, by-ref,
/// pointers, nullable and generic arguments unwrapped) and the members a method body references, read from its IL with the
/// runtime's own token resolution (<see cref="Module.ResolveMember(int, Type[], Type[])"/>), so aliases and generic
/// instantiations resolve exactly as the CLR sees them. Test-side only; not thread-safe concerns (stateless apart from the
/// read-only opcode tables).
/// </summary>
/// <example>
/// <code>
/// foreach (MethodBase body in ArchitectureScan.MethodsWithBodies(type))
///     foreach (MemberInfo used in ArchitectureScan.BodyReferences(body))
///         Console.WriteLine(used.Name);
/// </code>
/// </example>
public static class ArchitectureScan
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly OpCode[] _oneByte = new OpCode[256];
    private static readonly OpCode[] _twoByte = new OpCode[256];

    static ArchitectureScan()
    {
        foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var op = (OpCode)f.GetValue(null)!;
            ushort value = unchecked((ushort)op.Value);
            if (op.Size == 1)
            {
                _oneByte[value] = op;
            }
            else
            {
                _twoByte[value & 0xFF] = op;
            }
        }
    }

    /// <summary>True when <paramref name="t"/> or any type enclosing it carries <see cref="CompilerGeneratedAttribute"/>.</summary>
    public static bool IsCompilerGenerated(Type t)
    {
        for (Type? c = t; c is not null; c = c.DeclaringType)
        {
            if (c.IsDefined(typeof(CompilerGeneratedAttribute), false))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The nearest type in the enclosing chain of <paramref name="t"/> that is not compiler-generated.</summary>
    public static Type OwnerOf(Type t)
    {
        Type c = t;
        while (c.DeclaringType is not null && c.IsDefined(typeof(CompilerGeneratedAttribute), false))
        {
            c = c.DeclaringType;
        }

        return c;
    }

    /// <summary>A short display name: the type name with enclosing types joined by <c>+</c>, no namespace.</summary>
    public static string NameOf(Type t) => t.DeclaringType is null ? t.Name : NameOf(t.DeclaringType) + "+" + t.Name;

    /// <summary><paramref name="t"/> and every type nested in it, at any depth (compiler-generated ones included).</summary>
    public static IEnumerable<Type> WithNested(Type t)
    {
        yield return t;
        foreach (Type n in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (Type inner in WithNested(n))
            {
                yield return inner;
            }
        }
    }

    /// <summary>
    /// The types <paramref name="t"/> is built from: itself, then (recursively) element types of arrays, by-ref and pointer
    /// types, and generic arguments (which covers <see cref="Nullable{T}"/>).
    /// </summary>
    public static IEnumerable<Type> Constituents(Type t)
    {
        yield return t;
        if (t.HasElementType)
        {
            foreach (Type e in Constituents(t.GetElementType()!))
            {
                yield return e;
            }
        }

        if (t.IsGenericType)
        {
            foreach (Type a in t.GetGenericArguments())
            {
                foreach (Type c in Constituents(a))
                {
                    yield return c;
                }
            }
        }
    }

    /// <summary>
    /// The declared signature types of <paramref name="t"/> (its own members only): fields, properties (and indexer
    /// parameters), events, constructor parameters, method parameters and return types. Each item names its position.
    /// </summary>
    public static IEnumerable<(string Where, Type Type)> SignatureTypes(Type t)
    {
        foreach (FieldInfo f in t.GetFields(Declared))
        {
            yield return ($"field {f.Name}", f.FieldType);
        }

        foreach (PropertyInfo p in t.GetProperties(Declared))
        {
            yield return ($"property {p.Name}", p.PropertyType);
            foreach (ParameterInfo ip in p.GetIndexParameters())
            {
                yield return ($"indexer parameter {ip.Name}", ip.ParameterType);
            }
        }

        foreach (EventInfo e in t.GetEvents(Declared))
        {
            yield return ($"event {e.Name}", e.EventHandlerType!);
        }

        foreach (ConstructorInfo c in t.GetConstructors(Declared))
        {
            foreach (ParameterInfo p in c.GetParameters())
            {
                yield return ($"constructor parameter {p.Name}", p.ParameterType);
            }
        }

        foreach (MethodInfo m in t.GetMethods(Declared))
        {
            yield return ($"return of {m.Name}", m.ReturnType);
            foreach (ParameterInfo p in m.GetParameters())
            {
                yield return ($"parameter {p.Name} of {m.Name}", p.ParameterType);
            }
        }
    }

    /// <summary>The declared methods and constructors of <paramref name="t"/> that have an IL body.</summary>
    public static IEnumerable<MethodBase> MethodsWithBodies(Type t)
        => t.GetMethods(Declared).Cast<MethodBase>()
            .Concat(t.GetConstructors(Declared))
            .Where(m => m.GetMethodBody() is not null);

    /// <summary>
    /// Every field, method, constructor or type token the IL of <paramref name="method"/> references (<c>call</c>,
    /// <c>callvirt</c>, <c>newobj</c>, <c>ld*fld</c>, <c>st*fld</c>, <c>ldftn</c>, <c>ldtoken</c>, <c>box</c>, …).
    /// </summary>
    /// <exception cref="InvalidOperationException">A token does not resolve (the scan refuses to under-report).</exception>
    public static IEnumerable<MemberInfo> BodyReferences(MethodBase method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
        Type[]? typeArgs = method.DeclaringType is { IsGenericType: true } d ? d.GetGenericArguments() : null;
        Type[]? methodArgs = method is MethodInfo { IsGenericMethod: true } mi ? mi.GetGenericArguments() : null;
        var found = new List<MemberInfo>();
        int pos = 0;
        while (pos < il.Length)
        {
            OpCode op = il[pos] == 0xFE ? _twoByte[il[++pos]] : _oneByte[il[pos]];
            pos++;
            switch (op.OperandType)
            {
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    found.Add(Resolve(method, BitConverter.ToInt32(il, pos), typeArgs, methodArgs));
                    pos += 4;
                    break;
                case OperandType.InlineSwitch:
                    pos += 4 + (4 * BitConverter.ToInt32(il, pos));
                    break;
                default:
                    pos += OperandSize(op.OperandType);
                    break;
            }
        }

        return found;
    }

    /// <summary>
    /// The types a referenced member names: a type itself, a field's declaring and field type, a method's declaring, parameter
    /// and return types and (for a generic method instance) its generic arguments.
    /// </summary>
    public static IEnumerable<Type> TypesNamedBy(MemberInfo member)
    {
        IEnumerable<Type> roots = member switch
        {
            Type t => new[] { t },
            FieldInfo f => new[] { f.DeclaringType!, f.FieldType },
            MethodInfo m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType).Append(m.DeclaringType!)
                .Concat(m.IsGenericMethod ? m.GetGenericArguments() : Type.EmptyTypes),
            ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType).Append(c.DeclaringType!),
            _ => Array.Empty<Type>(),
        };
        return roots.SelectMany(Constituents);
    }

    private static MemberInfo Resolve(MethodBase method, int token, Type[]? typeArgs, Type[]? methodArgs)
    {
        try
        {
            return method.Module.ResolveMember(token, typeArgs, methodArgs)!;
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException($"Token 0x{token:X8} in {method.DeclaringType?.FullName}.{method.Name} does not resolve.", e);
        }
    }

    private static int OperandSize(OperandType type) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4,
    };
}
