using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Field-by-field comparison of two content sets (not record equality: record equality over an <c>ImmutableArray</c>
/// compares the backing array by reference). Walks public properties, <c>ImmutableArray</c> elements, the items of every
/// <c>DefTable</c> and every balance value by key, and returns one line per difference (empty = equal).
/// </summary>
internal static class ContentWalk
{
    private const int MaxDiffs = 25;

    /// <summary>The differences between <paramref name="a"/> and <paramref name="b"/>, with their member paths.</summary>
    public static List<string> Diff(object a, object b)
    {
        var diffs = new List<string>();
        Walk("$", a, b, diffs);
        return diffs;
    }

    /// <summary>Every leaf value and path of <paramref name="a"/> (for tests that need to know the walker reached something).</summary>
    public static int CountLeaves(object a)
    {
        var leaves = new List<string>();
        Leaves("$", a, leaves);
        return leaves.Count;
    }

    private static void Walk(string path, object? a, object? b, List<string> diffs)
    {
        if (diffs.Count >= MaxDiffs)
        {
            return;
        }

        if (a is null || b is null)
        {
            if (!ReferenceEquals(a, b))
            {
                diffs.Add(path + ": null vs value");
            }

            return;
        }

        Type t = a.GetType();
        if (t != b.GetType())
        {
            diffs.Add(path + ": type " + t.Name + " vs " + b.GetType().Name);
            return;
        }

        if (IsLeaf(t))
        {
            if (!a.Equals(b))
            {
                diffs.Add(path + ": " + a + " vs " + b);
            }

            return;
        }

        if (a is BalanceTable ta)
        {
            WalkBalance(path, ta, (BalanceTable)b, diffs);
            return;
        }

        IEnumerable<object?>? ea = Elements(a);
        if (ea is not null)
        {
            WalkElements(path, ea.ToList(), Elements(b)!.ToList(), diffs);
            return;
        }

        foreach (PropertyInfo p in Props(t))
        {
            Walk(path + "." + p.Name, p.GetValue(a), p.GetValue(b), diffs);
        }
    }

    private static void WalkElements(string path, List<object?> xs, List<object?> ys, List<string> diffs)
    {
        if (xs.Count != ys.Count)
        {
            diffs.Add(path + ": length " + xs.Count + " vs " + ys.Count);
            return;
        }

        for (int i = 0; i < xs.Count; i++)
        {
            Walk(path + "[" + i + "]", xs[i], ys[i], diffs);
        }
    }

    private static void WalkBalance(string path, BalanceTable a, BalanceTable b, List<string> diffs)
    {
        if (!a.Registry.Names.SequenceEqual(b.Registry.Names) || a.Registry.Stamp != b.Registry.Stamp)
        {
            diffs.Add(path + ": registries differ");
            return;
        }

        foreach (string name in a.Registry.Names)
        {
            long x = a.Get(a.Registry.Find(name));
            long y = b.Get(b.Registry.Find(name));
            if (x != y)
            {
                diffs.Add(path + "." + name + ": " + x + " vs " + y);
            }
        }
    }

    private static void Leaves(string path, object? a, List<string> leaves)
    {
        if (a is null || IsLeaf(a.GetType()))
        {
            leaves.Add(path);
        }
        else if (a is BalanceTable t)
        {
            leaves.AddRange(t.Registry.Names.Select(n => path + "." + n));
        }
        else if (Elements(a) is { } es)
        {
            int i = 0;
            foreach (object? e in es)
            {
                Leaves(path + "[" + i++ + "]", e, leaves);
            }
        }
        else
        {
            foreach (PropertyInfo p in Props(a.GetType()))
            {
                Leaves(path + "." + p.Name, p.GetValue(a), leaves);
            }
        }
    }

    private static bool IsLeaf(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(DefIndex) || t == typeof(decimal);

    private static IEnumerable<PropertyInfo> Props(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.CanRead);

    /// <summary>The elements of an <c>ImmutableArray&lt;T&gt;</c> or <c>DefTable&lt;T&gt;</c>, else null.</summary>
    private static IEnumerable<object?>? Elements(object o)
    {
        Type t = o.GetType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
        {
            bool isDefault = (bool)t.GetProperty("IsDefault")!.GetValue(o)!;
            return isDefault ? new List<object?> { "<default>" } : ((IEnumerable)o).Cast<object?>().ToList();
        }

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(DefTable<>))
        {
            object items = t.GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;
            return ((IEnumerable)items).Cast<object?>().ToList();
        }

        return null;
    }
}
