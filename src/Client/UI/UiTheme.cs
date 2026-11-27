using System.Linq;
using Godot;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The UI's one Theme (<c>content/ui/theme.tres</c>, the project's <c>gui/theme/custom</c>) and its two
    /// faces (UI polish plan item 2): Nunito for the body, Fredoka for numbers, titles and banners, both
    /// falling back to PM Marks (Balsamiq Sans Bold's ✓ ✕ ✗ and arrows, cut down) for the marks neither has. A
    /// chain is laid out as tall as its tallest face, so the marks are cut to the faces' height and the display
    /// face does not fall back to the taller body face. The fonts are content read at
    /// boot, as the JSON is, so nothing has to be imported first; the theme's styles and colours are its own.
    /// </summary>
    public static class UiTheme
    {
        /// <summary>A label over the world: the body face with a black outline.</summary>
        public const string Hud = "HudLabel";
        /// <summary>A big number, a title or a banner: the display face with a black outline.</summary>
        public const string Display = "DisplayLabel";
        /// <summary>A key drawn as a key: the panel, and the letter on it.</summary>
        public const string Keycap = "Keycap", KeycapLabel = "KeycapLabel";

        private const string FontDir = "res://content/fonts/";
        /// <summary>The OpenType tag 'wght', as the variation dictionary keys it.</summary>
        private const int Weight = 0x77676874;
        public const int BodyWeight = 650, DisplayWeight = 600;

        public static Font? Body { get; private set; }
        public static Font? DisplayFont { get; private set; }

        /// <summary>Puts the faces into the project's theme; once, before the first control is made.</summary>
        public static void Load()
        {
            var theme = ThemeDB.GetProjectTheme();
            if (theme == null || Body != null) return;
            var marks = Open("PMMarks.ttf");
            var nunito = Open("Nunito.ttf");
            var fredoka = Open("Fredoka.ttf");
            if (nunito == null || fredoka == null)
            {
                GD.PushWarning("UiTheme: the fonts under content/fonts did not load; the engine's font stands");
                return;
            }
            Body = Variation(nunito, BodyWeight, marks);
            DisplayFont = Variation(fredoka, DisplayWeight, marks);
            theme.DefaultFont = Body;
            theme.SetFont("font", Display, DisplayFont);
            theme.SetFont("font", KeycapLabel, DisplayFont);
        }

        private static FontFile? Open(string name)
        {
            var file = new FontFile();
            return file.LoadDynamicFont(FontDir + name) == Error.Ok ? file : null;
        }

        private static FontVariation Variation(FontFile file, int weight, params Font?[] fallbacks) => new FontVariation
        {
            BaseFont = file,
            VariationOpentype = new Godot.Collections.Dictionary { { Weight, weight } },
            Fallbacks = new Godot.Collections.Array<Font>(fallbacks.Where(f => f != null).Select(f => f!)),
        };
    }
}
