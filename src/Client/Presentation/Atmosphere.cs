using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// Sky, sun and air (concept video): a low golden sun under a pink-to-purple sky with haze and
    /// bloom, and a blue moonlit night for the raids. <see cref="Night"/> blends between the two.
    /// </summary>
    public partial class Atmosphere : Node3D
    {
        /// <summary>Night blends in and out at this rate (per second), so a raid's dusk takes a few seconds.</summary>
        private const float BlendRate = 0.35f;

        private static readonly Color DaySun = new Color(1.0f, 0.74f, 0.52f);
        private static readonly Color NightSun = new Color(0.50f, 0.62f, 1.0f);
        private static readonly Color DayAmbient = new Color(0.66f, 0.52f, 0.68f);
        private static readonly Color NightAmbient = new Color(0.17f, 0.22f, 0.42f);
        private static readonly Color DayFog = new Color(0.93f, 0.62f, 0.60f);
        private static readonly Color NightFog = new Color(0.06f, 0.10f, 0.22f);
        /// <summary>Towards the evening sun: low in the south-east, so it rakes along the street.</summary>
        private static readonly Vector3 ToSun = new Vector3(0.80f, 0.33f, 0.50f).Normalized();
        private static readonly Vector3 ToMoon = new Vector3(-0.35f, 0.75f, 0.55f).Normalized();

        /// <summary>0 = golden hour, 1 = night. What it is heading for; <see cref="Night"/> follows.</summary>
        public float TargetNight;
        /// <summary>0 = clear, 1 = doc 10 §6's Sea Fog. What it is heading for; <see cref="Fog"/> follows.</summary>
        public float TargetFog;
        public float Fog { get; private set; }
        private static readonly Color SeaFogColour = new Color(0.78f, 0.80f, 0.84f);
        private const float ClearDensity = 0.0045f, SeaFogDensity = 0.06f, FogBlendRate = 0.25f;
        public float Night { get; private set; }
        public DirectionalLight3D Sun { get; private set; } = null!;

        private Environment _environment = null!;
        private ShaderMaterial _sky = null!;

        /// <param name="cheap">No shadows and no bloom (see <see cref="WorldView.Cheap"/>).</param>
        public void Build(bool cheap = false)
        {
            _sky = Shaders.Sky();
            _environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = _sky, ProcessMode = Sky.ProcessModeEnum.Incremental, RadianceSize = Sky.RadianceSizeEnum.Size128 },
                AmbientLightSource = Environment.AmbientSource.Color,
                ReflectedLightSource = cheap ? Environment.ReflectionSource.Disabled : Environment.ReflectionSource.Sky,
                TonemapMode = Environment.ToneMapper.Filmic,
                TonemapWhite = 6f,
                GlowEnabled = !cheap,
                GlowIntensity = 0.5f,
                GlowStrength = 1.0f,
                GlowBloom = 0.08f,
                GlowHdrThreshold = 1.0f,
                GlowBlendMode = Environment.GlowBlendModeEnum.Screen,
                FogEnabled = true,
                FogDensity = 0.0045f,
                FogAerialPerspective = 0.4f,
                FogSkyAffect = 0.15f,
                FogSunScatter = 0.25f,
                SsaoEnabled = !cheap,
                SsaoRadius = 1.2f,
                SsaoIntensity = 1.6f,
                AdjustmentEnabled = true,
                AdjustmentSaturation = 1.12f,
                AdjustmentContrast = 1.04f,
            };
            AddChild(new WorldEnvironment { Name = "Environment", Environment = _environment });
            Sun = new DirectionalLight3D
            {
                Name = "Sun",
                ShadowEnabled = !cheap,
                DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
                DirectionalShadowMaxDistance = 110f,
                DirectionalShadowBlendSplits = true,
                ShadowBias = 0.05f,
                ShadowNormalBias = 1.5f,
                ShadowBlur = 1.5f,
                LightAngularDistance = 1.2f,
            };
            AddChild(Sun);
            Apply();
        }

        public override void _Process(double delta)
        {
            if (Mathf.IsEqualApprox(Night, TargetNight) && Mathf.IsEqualApprox(Fog, TargetFog)) return;
            Night = Mathf.MoveToward(Night, TargetNight, BlendRate * (float)delta);
            // The fog rolls in over four seconds.
            Fog = Mathf.MoveToward(Fog, TargetFog, FogBlendRate * (float)delta);
            Apply();
        }

        /// <summary>Jumps to the target without the blend (the first frame, tests, screenshots).</summary>
        public void Snap()
        {
            Night = TargetNight;
            Fog = TargetFog;
            Apply();
        }

        private void Apply()
        {
            float n = Mathf.SmoothStep(0f, 1f, Night);
            // The light swings from the sun to the moon; halfway it is dim, so the swing is not seen.
            var toLight = ToSun.Lerp(ToMoon, n).Normalized();
            Sun.Basis = Basis.LookingAt(-toLight, Vector3.Up);
            Sun.LightColor = DaySun.Lerp(NightSun, n);
            Sun.LightEnergy = Mathf.Lerp(1.7f, 0.4f, n) * (1f - 0.6f * Mathf.Sin(n * Mathf.Pi));
            _environment.AmbientLightColor = DayAmbient.Lerp(NightAmbient, n);
            _environment.AmbientLightEnergy = Mathf.Lerp(0.6f, 0.55f, n);
            float f = Mathf.SmoothStep(0f, 1f, Fog);
            _environment.FogLightColor = DayFog.Lerp(NightFog, n).Lerp(SeaFogColour.Lerp(NightFog, n * 0.8f), f);
            _environment.FogDensity = Mathf.Lerp(ClearDensity, SeaFogDensity, f);
            _environment.FogSkyAffect = Mathf.Lerp(0.15f, 0.9f, f);
            // Its own grey, not the sky's colour behind it.
            _environment.FogAerialPerspective = Mathf.Lerp(0.4f, 0f, f);
            _environment.FogLightEnergy = Mathf.Lerp(1.0f, 0.8f, n);
            _sky.SetShaderParameter("night", n);
        }
    }
}
