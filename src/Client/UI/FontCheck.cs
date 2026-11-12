using System.Text;
using Godot;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// Finds text the UI cannot draw. A character the font has no glyph for renders as nothing, so a
    /// missing mark goes unseen in review; the smoke runs this over everything on screen instead.
    /// </summary>
    public static class FontCheck
    {
        /// <summary>Every distinct character shown by a label or list under <paramref name="root"/> that its font lacks.</summary>
        public static string MissingGlyphs(Node root)
        {
            var missing = new StringBuilder();
            Collect(root, missing);
            return missing.ToString();
        }

        private static void Collect(Node node, StringBuilder missing)
        {
            switch (node)
            {
                case Label label:
                    Scan(label.GetThemeFont("font"), label.Text, missing);
                    break;
                case ItemList list:
                    var font = list.GetThemeFont("font");
                    for (int i = 0; i < list.ItemCount; i++) Scan(font, list.GetItemText(i), missing);
                    break;
            }
            foreach (var child in node.GetChildren()) Collect(child, missing);
        }

        private static void Scan(Font font, string text, StringBuilder missing)
        {
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                if (!font.HasChar(c) && missing.ToString().IndexOf(c) < 0) missing.Append(c);
            }
        }
    }
}
