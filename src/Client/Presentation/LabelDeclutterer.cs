using System;
using System.Collections.Generic;
using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// Each frame decides which world labels are drawn (<see cref="LabelDeclutter"/>): far ones thin
    /// out, labels that would lie on each other leave the nearest, and none lies on the HUD.
    /// </summary>
    public partial class LabelDeclutterer : Node
    {
        /// <summary>Room left around a label's text, in HUD units.</summary>
        private const float Padding = 6f;

        /// <summary>The parts of the screen that belong to the HUD right now, in its own units.</summary>
        public Func<IReadOnlyList<ScreenBox>>? KeepClear;

        /// <summary>Labels looked at in the last step, and how many of them were told to stay away.</summary>
        public int Seen { get; private set; }
        public int Hidden { get; private set; }

        private static readonly ScreenBox[] Nothing = new ScreenBox[0];

        private readonly List<LabelCandidate> _candidates = new List<LabelCandidate>();
        private readonly List<LabelCandidate> _pool = new List<LabelCandidate>();

        public override void _Ready()
        {
            // After the labels have been sized for this frame's camera.
            ProcessPriority = 110;
        }

        public override void _Process(double delta) => Step((float)delta);

        /// <summary>One decision for the frame; <paramref name="delta"/> seconds of fading towards it.</summary>
        public void Step(float delta)
        {
            var viewport = GetViewport();
            var camera = viewport?.GetCamera3D();
            if (viewport == null || camera == null) return;
            float height = viewport.GetVisibleRect().Size.Y;
            var eye = camera.GlobalPosition;

            _candidates.Clear();
            var labels = SizedLabel3D.Live;
            for (int i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                if (!label.IsVisibleInTree()) continue;
                var at = label.GlobalPosition;
                // Behind the eyes nothing is drawn; it comes back in full when looked at again.
                if (camera.IsPositionBehind(at)) continue;
                var candidate = Take();
                var centre = camera.UnprojectPosition(at);
                float tall = label.ScreenFraction(camera) * height;
                candidate.Box = ScreenBox.Around(centre.X, centre.Y, tall * label.Aspect + Padding * 2f, tall + Padding * 2f);
                candidate.Distance = eye.DistanceTo(at);
                candidate.Rank = label.Rank;
                candidate.Slides = label.Slides;
                candidate.Fade = LabelDeclutter.DistanceFade(candidate.Distance, label.FadeStart, label.FadeEnd);
                candidate.Owner = label;
                _candidates.Add(candidate);
            }

            LabelDeclutter.Resolve(_candidates, KeepClear?.Invoke() ?? Nothing);

            Seen = _candidates.Count;
            Hidden = 0;
            foreach (var candidate in _candidates)
            {
                if (!candidate.Shown) Hidden++;
                var label = (SizedLabel3D)candidate.Owner!;
                label.Show(candidate.Shown ? candidate.Fade : 0f, delta);
                if (label.Slides && (candidate.Shown || candidate.SlideY == 0f)) label.Slide(candidate.SlideY, camera, height);
                candidate.Owner = null;
                _pool.Add(candidate);
            }
        }

        private LabelCandidate Take()
        {
            if (_pool.Count == 0) return new LabelCandidate();
            var candidate = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            return candidate;
        }
    }
}
