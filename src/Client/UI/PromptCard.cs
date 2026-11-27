using System.Collections.Generic;
using Godot;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The interact prompt as a card (UI polish plan item 5; ref Peak, Valheim): a bold noun line over verb
    /// rows, each led by a drawn keycap — "MAILBOX · 2 Larch Lane" over "[E] Deliver 3 Letters ✓". The text is
    /// <see cref="HudText.Card"/>'s: the first line the noun, each further line a row, a leading "[key]" the
    /// keycap. A text of one line (Build mode's readout) is a single row with its keys left in the words.
    /// Screen-fixed under the crosshair (ADR-0018 H11); the keycap is a PanelContainer with the theme's
    /// StyleBoxFlat, not an icon.
    /// </summary>
    public partial class PromptCard : VBoxContainer
    {
        public const int MaxRows = 4, FontSize = 18, MinFontSize = 12;

        private sealed class Row
        {
            public HBoxContainer Box = null!;
            public PanelContainer Cap = null!;
            public Label Key = null!, Verb = null!;
        }

        private readonly Label _noun;
        private readonly List<Row> _rows = new List<Row>();
        private int _size = FontSize;
        private float _fittedRoom = -1f;
        private Color _tone = Colors.White;
        private bool _compact;

        /// <summary>The rows without the noun line over them, where there is no room for the whole card.</summary>
        public bool Compact
        {
            get => _compact;
            set
            {
                if (_compact == value) return;
                _compact = value;
                _noun.Visible = _noun.Text.Length > 0 && !value;
            }
        }

        /// <summary>The whole text, as <see cref="HudText.Card"/> put it.</summary>
        public string Text { get; private set; } = "";
        /// <summary>The size the words are drawn at, fitted to the room (18 down to 12).</summary>
        public int DrawnSize => _size;
        /// <summary>The face the rows are drawn in.</summary>
        public Font BodyFont => _rows[0].Verb.GetThemeFont("font");

        public PromptCard()
        {
            Name = "PromptCard";
            MouseFilter = MouseFilterEnum.Ignore;
            Alignment = AlignmentMode.Center;
            AddThemeConstantOverride("separation", 2);
            _noun = Hud.MakeDisplayLabel("Noun", FontSize, HorizontalAlignment.Center);
            AddChild(_noun);
            for (int i = 0; i < MaxRows; i++)
            {
                var row = new Row { Box = new HBoxContainer { Name = $"Row{i}", MouseFilter = MouseFilterEnum.Ignore, Alignment = AlignmentMode.Center } };
                row.Box.AddThemeConstantOverride("separation", 8);
                row.Cap = new PanelContainer { Name = "Keycap", ThemeTypeVariation = UiTheme.Keycap, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
                row.Key = new Label { Name = "Key", ThemeTypeVariation = UiTheme.KeycapLabel, MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
                row.Cap.AddChild(row.Key);
                row.Box.AddChild(row.Cap);
                row.Verb = Hud.MakeLabel("Verb", FontSize, HorizontalAlignment.Left);
                row.Box.AddChild(row.Verb);
                AddChild(row.Box);
                _rows.Add(row);
            }
            SetText("");
        }

        /// <summary>Puts up <paramref name="text"/>; the same text again changes nothing.</summary>
        public void SetText(string text)
        {
            if (text == Text) return;
            Text = text;
            _fittedRoom = -1f;
            string[] lines = text.Length == 0 ? System.Array.Empty<string>() : text.Split('\n');
            bool card = lines.Length > 1;
            _noun.Visible = card && !_compact;
            _noun.Text = card ? lines[0] : "";
            int first = card ? 1 : 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                int at = first + i;
                row.Box.Visible = at < lines.Length;
                if (!row.Box.Visible) continue;
                string line = lines[at];
                string key = card ? KeyOf(line, out line) : "";
                row.Cap.Visible = key.Length > 0;
                row.Key.Text = key.ToUpperInvariant();
                row.Verb.Text = line;
            }
            Paint();
        }

        /// <summary>"[hold E] Deliver …": "hold E", and the rest, capitalised; no leading key: "".</summary>
        public static string KeyOf(string line, out string rest)
        {
            rest = line;
            if (!line.StartsWith('[')) return "";
            int close = line.IndexOf(']');
            if (close < 2) return "";
            rest = line.Substring(close + 1).TrimStart();
            if (rest.Length > 0) rest = char.ToUpperInvariant(rest[0]) + rest.Substring(1);
            return line.Substring(1, close - 1);
        }

        /// <summary>The words take the prompt's tone (green for a match, red for a mismatch); the keycaps keep theirs.</summary>
        public void SetTone(Color tone)
        {
            if (tone == _tone) return;
            _tone = tone;
            Paint();
        }

        private void Paint()
        {
            _noun.Modulate = _tone == Colors.White ? Colors.White : _tone.Lerp(Colors.White, 0.35f);
            foreach (var row in _rows) row.Verb.Modulate = _tone;
        }

        /// <summary>Draws the words smaller, down to 12 px, until the widest line fits <paramref name="room"/> px.</summary>
        public void Fit(float room)
        {
            if (Mathf.IsEqualApprox(room, _fittedRoom)) return;
            _fittedRoom = room;
            int size = FontSize;
            while (size > MinFontSize && Widest(size) > room) size--;
            if (size == _size) return;
            _size = size;
            _noun.AddThemeFontSizeOverride("font_size", size);
            foreach (var row in _rows)
            {
                row.Verb.AddThemeFontSizeOverride("font_size", size);
                row.Key.AddThemeFontSizeOverride("font_size", Mathf.Max(10, size - 4));
            }
        }

        /// <summary>How wide the widest line would be drawn at <paramref name="size"/> px.</summary>
        public float Widest(int size)
        {
            float widest = _noun.Visible ? _noun.GetThemeFont("font").GetStringSize(_noun.Text, HorizontalAlignment.Left, -1, size).X : 0f;
            foreach (var row in _rows)
            {
                if (!row.Box.Visible) continue;
                float w = row.Verb.GetThemeFont("font").GetStringSize(row.Verb.Text, HorizontalAlignment.Left, -1, size).X;
                if (row.Cap.Visible) w += row.Key.GetThemeFont("font").GetStringSize(row.Key.Text, HorizontalAlignment.Left, -1, Mathf.Max(10, size - 4)).X + 12f + 8f;
                widest = Mathf.Max(widest, w);
            }
            return widest;
        }

        /// <summary>The keys drawn as keycaps, top row first.</summary>
        public IEnumerable<string> Keycaps
        {
            get
            {
                foreach (var row in _rows)
                    if (row.Box.Visible && row.Cap.Visible) yield return row.Key.Text;
            }
        }
    }
}
