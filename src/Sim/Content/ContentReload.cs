using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PerformativeMail.Sim.Content
{
    /// <summary>
    /// Hot reload (doc 07 §6: "pressing F6 in a debug build reloads content and re-applies perks
    /// for rapid balancing"). The files are loaded and validated as at boot; if every table still
    /// has the same ids in the same order, and the maps and street names are the same, the new
    /// values are copied into the defs already loaded — so every construct, vehicle, stack, perk
    /// and card that holds a def sees the new numbers at once — and the balance table is swapped
    /// in place. Anything else (an id added, removed, renamed or moved; a map changed) needs a
    /// restart, and nothing is changed.
    /// </summary>
    public sealed partial class ContentDb
    {
        /// <summary>
        /// Loads <paramref name="source"/> and takes its values over if it is the same content in
        /// shape. False, with what stands in the way, and nothing changed otherwise.
        /// </summary>
        public bool ReloadFrom(IContentSource source, out List<string> problems)
        {
            var next = TryLoad(source, out problems);
            if (problems.Count > 0) return false;
            ShapeProblems(next, problems);
            if (problems.Count > 0) return false;

            var map = new Dictionary<object, object>(ByReference.Instance);
            var tables = Tables().ToList();
            foreach (var table in tables)
            {
                var mine = (IList)table.GetValue(this)!;
                var theirs = (IList)table.GetValue(next)!;
                for (int i = 0; i < mine.Count; i++) map[theirs[i]!] = mine[i]!;
            }
            map[next.Waves] = Waves;
            map[next.MailMix] = MailMix;
            foreach (var pair in map) CopyInto(pair.Key, pair.Value, map);

            Balance.TakeOver(next.Balance);
            _nodesByProp.Clear();
            foreach (var pair in next._nodesByProp) _nodesByProp[pair.Key] = (NodeDef)map[pair.Value];
            ContentHash = next.ContentHash;
            return true;
        }

        /// <summary>Every table of defs with ids: the content's lists.</summary>
        private static IEnumerable<PropertyInfo> Tables()
        {
            foreach (var p in typeof(ContentDb).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = p.PropertyType;
                if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(IReadOnlyList<>)) continue;
                var element = t.GetGenericArguments()[0];
                if (element.IsClass && element != typeof(string) && element.GetProperty("Id")?.PropertyType == typeof(string)) yield return p;
            }
        }

        private void ShapeProblems(ContentDb next, List<string> problems)
        {
            foreach (var table in Tables())
            {
                var mine = (IList)table.GetValue(this)!;
                var theirs = (IList)table.GetValue(next)!;
                var idOf = table.PropertyType.GetGenericArguments()[0].GetProperty("Id")!;
                var a = mine.Cast<object>().Select(d => (string)idOf.GetValue(d)!).ToList();
                var b = theirs.Cast<object>().Select(d => (string)idOf.GetValue(d)!).ToList();
                if (a.SequenceEqual(b)) continue;
                var added = b.Except(a).ToList();
                var gone = a.Except(b).ToList();
                string what = added.Count + gone.Count == 0 ? "the order of its ids changed"
                    : string.Join(", ", added.Select(id => "+" + id).Concat(gone.Select(id => "-" + id)));
                problems.Add($"{table.Name}: {what} (that needs a restart)");
            }
            if (!StreetNames.SequenceEqual(next.StreetNames)) problems.Add("StreetNames: the street names changed (that needs a restart)");
            if (_maps.Count != next._maps.Count || _maps.Any(m => !next._maps.TryGetValue(m.Key, out var other) || other.MapHash != m.Value.MapHash))
                problems.Add("Maps: a map changed (that needs a restart)");
        }

        /// <summary>Every settable property of <paramref name="from"/> onto <paramref name="to"/>, defs in it turned into the loaded ones.</summary>
        private static void CopyInto(object from, object to, Dictionary<object, object> map)
        {
            foreach (var p in from.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                p.SetValue(to, Remap(p.GetValue(from), p.PropertyType, map));
            }
        }

        /// <summary>A value of the new content as the loaded content has it: its defs swapped for the loaded ones, in lists, pairs and small structs alike.</summary>
        private static object? Remap(object? value, Type declared, Dictionary<object, object> map)
        {
            if (value == null) return null;
            if (map.TryGetValue(value, out var mine)) return mine;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal) return value;
            if (value is Array array && type.GetElementType()!.IsPrimitive) return value;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                var args = type.GetGenericArguments();
                var key = Remap(type.GetProperty("Key")!.GetValue(value), args[0], map);
                var val = Remap(type.GetProperty("Value")!.GetValue(value), args[1], map);
                return Activator.CreateInstance(type, key, val);
            }
            if (value is IDictionary dictionary)
            {
                var args = DictionaryArgs(declared) ?? DictionaryArgs(type)!;
                var copy = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args))!;
                foreach (DictionaryEntry e in dictionary) copy[Remap(e.Key, args[0], map)!] = Remap(e.Value, args[1], map);
                return copy;
            }
            if (value is IEnumerable list)
            {
                var element = ElementOf(declared) ?? ElementOf(type) ?? typeof(object);
                var items = list.Cast<object?>().Select(v => Remap(v, element, map)).ToList();
                var result = Array.CreateInstance(element, items.Count);
                for (int i = 0; i < items.Count; i++) result.SetValue(items[i], i);
                return result;
            }
            if (type.IsValueType)
            {
                // A small struct (a recipe's input, a kit's stack, a drop): its fields, a def among them.
                object boxed = value;
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    f.SetValue(boxed, Remap(f.GetValue(boxed), f.FieldType, map));
                return boxed;
            }
            return value;
        }

        private static Type? ElementOf(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            var e = (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? t : null)
                ?? t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return e?.GetGenericArguments()[0];
        }

        private static Type[]? DictionaryArgs(Type t)
        {
            var d = (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) ? t : null)
                ?? t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
            return d?.GetGenericArguments();
        }

        private sealed class ByReference : IEqualityComparer<object>
        {
            public static readonly ByReference Instance = new ByReference();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
