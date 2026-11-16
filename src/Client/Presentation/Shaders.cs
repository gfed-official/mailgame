using System.Collections.Generic;
using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The surfaces of the concept video that a plain colour cannot give: clapboard siding, shingle
    /// roofs, the dirt-and-cobble path through grass, soft foliage, the sea and the sunset sky.
    /// All of it is procedural (no texture files) and works in both renderers.
    /// </summary>
    public static class Shaders
    {
        private const string Noise = @"
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x), mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}
float fbm(vec2 p) {
    float a = 0.5, s = 0.0;
    for (int i = 0; i < 4; i++) { s += a * vnoise(p); p = p * 2.03 + 17.0; a *= 0.5; }
    return s;
}
";

        private const string GroundCode = @"
shader_type spatial;
uniform sampler2D ground_map : filter_linear, repeat_disable;
uniform vec2 map_origin = vec2(0.0, 0.0);
uniform vec2 map_size = vec2(92.0, 52.0);
uniform vec3 grass_a : source_color = vec3(0.11, 0.25, 0.08);
uniform vec3 grass_b : source_color = vec3(0.19, 0.36, 0.10);
uniform vec3 grass_c : source_color = vec3(0.30, 0.44, 0.13);
uniform vec3 dirt_a : source_color = vec3(0.24, 0.17, 0.15);
uniform vec3 dirt_b : source_color = vec3(0.34, 0.25, 0.21);
uniform vec3 stone_a : source_color = vec3(0.30, 0.28, 0.34);
uniform vec3 stone_b : source_color = vec3(0.47, 0.44, 0.48);
uniform vec3 rock : source_color = vec3(0.40, 0.38, 0.44);
uniform vec3 sand : source_color = vec3(0.78, 0.62, 0.42);
varying vec3 wp;
varying vec3 wn;
" + Noise + @"
// x: distance to the nearest stone's centre, y: that stone's own random number.
vec2 cobble(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    float best = 8.0, id = 0.0;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 g = vec2(float(x), float(y));
            vec2 o = vec2(hash21(i + g), hash21(i + g + 31.7));
            vec2 d = g + 0.15 + 0.7 * o - f;
            float dist = dot(d, d);
            if (dist < best) { best = dist; id = hash21(i + g + 7.3); }
        }
    }
    return vec2(sqrt(best), id);
}
void vertex() {
    wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
void fragment() {
    vec2 uv = (wp.xz - map_origin) / map_size;
    vec3 m = texture(ground_map, uv).rgb;
    m *= step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    float n = fbm(wp.xz * 0.7);
    vec3 grass = mix(grass_a, grass_b, fbm(wp.xz * 0.08 + 40.0));
    grass = mix(grass, grass_c, smoothstep(0.5, 0.8, fbm(wp.xz * 0.3 + 9.0)) * 0.6);
    // Blades: fine streaks, stretched a little one way so it reads as grass and not as sand.
    grass *= 0.72 + 0.34 * vnoise(wp.xz * 7.0) + 0.3 * vnoise(wp.xz * vec2(46.0, 19.0));
    vec3 lawn = mix(grass, grass_c, 0.3);
    vec3 col = mix(grass, lawn, smoothstep(0.35, 0.65, m.g + (n - 0.5) * 0.3));

    // The yard of the Post Office: flagstones with dark joints.
    vec2 flag = cobble(wp.xz * 1.7);
    vec3 paving = mix(stone_a, stone_b, flag.y) * 1.25 * mix(0.5, 1.0, 1.0 - smoothstep(0.40, 0.5, flag.x));
    col = mix(col, paving, smoothstep(0.4, 0.6, m.b + (n - 0.5) * 0.25));

    // The path: packed dirt with ragged edges, cobbles down its middle.
    float path = smoothstep(0.40, 0.56, m.r + (n - 0.5) * 0.4);
    vec3 dirt = mix(dirt_a, dirt_b, fbm(wp.xz * 1.3 + 3.0));
    col = mix(col, dirt, path);
    vec2 cb = cobble(wp.xz * 3.3);
    float core = smoothstep(0.62, 0.9, m.r + (fbm(wp.xz * 0.3 + 70.0) - 0.5) * 0.45);
    float stone = (1.0 - smoothstep(0.36, 0.44, cb.x)) * core * step(0.06, cb.y);
    // Rounded tops: each stone is lighter in its middle than at its rim.
    col = mix(col, mix(stone_a, stone_b, cb.y) * (1.15 - 0.9 * cb.x), stone);

    // Off the flat of the town: sand near the water, rock on anything steep.
    float beach = (1.0 - smoothstep(-3.6, -2.2, wp.y)) * (1.0 - path);
    col = mix(col, sand * (0.9 + 0.2 * n), beach);
    float steep = 1.0 - smoothstep(0.62, 0.80, wn.y + (n - 0.5) * 0.15);
    col = mix(col, rock * (0.75 + 0.5 * fbm(wp.xz * 0.5 + wp.y)), steep);

    ALBEDO = col;
    ROUGHNESS = mix(0.95, 0.65, stone);
    SPECULAR = 0.25;
}
";

        private const string ClapboardCode = @"
shader_type spatial;
uniform vec3 colour : source_color = vec3(0.8, 0.3, 0.35);
uniform float board = 0.21;
varying vec3 op;
void vertex() { op = VERTEX; }
void fragment() {
    float f = fract(op.y / board + 0.5);
    // Each board overlaps the one below: a shadow line under its lip, then a gentle fall-off.
    float shade = mix(0.62, 1.0, smoothstep(0.0, 0.12, f)) * mix(1.0, 0.92, f);
    ALBEDO = colour * shade;
    ROUGHNESS = 0.78;
    SPECULAR = 0.3;
}
";

        private const string ShingleCode = @"
shader_type spatial;
uniform vec3 colour : source_color = vec3(0.2, 0.19, 0.27);
varying vec3 op;
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}
void vertex() { op = VERTEX; }
void fragment() {
    vec2 p = vec2(op.z / 0.42, op.x / 0.30);
    float row = floor(p.y);
    p.x += mod(row, 2.0) * 0.5;
    vec2 f = fract(p);
    float edge = smoothstep(0.0, 0.07, f.x) * smoothstep(0.0, 0.14, f.y);
    float tint = hash21(vec2(floor(p.x), row));
    ALBEDO = colour * mix(0.55, 1.0, edge) * (0.82 + 0.36 * tint);
    ROUGHNESS = 0.7;
    SPECULAR = 0.35;
}
";

        private const string FoliageCode = @"
shader_type spatial;
render_mode diffuse_lambert_wrap;
uniform vec3 colour : source_color = vec3(0.2, 0.45, 0.15);
uniform vec3 tip : source_color = vec3(0.55, 0.7, 0.2);
uniform float base = 0.0;
uniform float height = 1.0;
varying vec3 op;
varying float sky_facing;
" + Noise + @"
void vertex() {
    op = VERTEX;
    sky_facing = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz).y;
}
void fragment() {
    // Lighter towards the top of the plant and on the lumps that face the sky.
    float up = clamp((op.y - base) / height, 0.0, 1.0) * 0.55 + clamp(sky_facing, 0.0, 1.0) * 0.45;
    float leaf = vnoise(op.xz * 9.0 + op.y * 7.0);
    ALBEDO = mix(colour, tip, up * 0.75 + leaf * 0.2) * (0.8 + 0.4 * leaf);
    ROUGHNESS = 0.9;
    SPECULAR = 0.15;
    BACKLIGHT = tip * 0.15;
}
";

        private const string WaterCode = @"
shader_type spatial;
uniform vec3 deep : source_color = vec3(0.05, 0.16, 0.30);
uniform vec3 shallow : source_color = vec3(0.14, 0.36, 0.50);
varying vec3 wp;
" + Noise + @"
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    float w = fbm(wp.xz * 0.12 + vec2(TIME * 0.05, TIME * 0.03));
    float ripple = vnoise(wp.xz * 1.1 + vec2(-TIME * 0.4, TIME * 0.25));
    ALBEDO = mix(deep, shallow, w * 0.6 + ripple * 0.12);
    ROUGHNESS = 0.08 + 0.1 * ripple;
    METALLIC = 0.25;
    SPECULAR = 0.7;
}
";

        private const string SkyCode = @"
shader_type sky;
uniform vec3 top_colour : source_color = vec3(0.30, 0.25, 0.52);
uniform vec3 mid_colour : source_color = vec3(0.72, 0.42, 0.62);
uniform vec3 horizon_colour : source_color = vec3(1.0, 0.66, 0.50);
uniform vec3 sun_colour : source_color = vec3(1.0, 0.86, 0.66);
uniform vec3 night_top : source_color = vec3(0.02, 0.04, 0.12);
uniform vec3 night_horizon : source_color = vec3(0.07, 0.13, 0.27);
uniform float night = 0.0;
" + Noise + @"
void sky() {
    vec3 dir = normalize(EYEDIR);
    float h = max(dir.y, 0.0);
    vec3 day = mix(horizon_colour, mid_colour, smoothstep(0.0, 0.22, h));
    day = mix(day, top_colour, smoothstep(0.15, 0.75, h));
    vec3 dark = mix(night_horizon, night_top, smoothstep(0.0, 0.6, h));

    float toward = max(dot(dir, LIGHT0_DIRECTION), 0.0);
    // The low sun warms the whole side of the sky it sits in, then a wide bloom, then the disc.
    day += sun_colour * (0.2 * pow(toward, 3.0) + 0.35 * pow(toward, 60.0));
    day = mix(day, vec3(1.0, 0.95, 0.85) * 2.2, smoothstep(0.9986, 0.9993, toward));
    dark += vec3(0.55, 0.65, 0.9) * (0.25 * pow(toward, 60.0) + 2.0 * smoothstep(0.9990, 0.9995, toward));

    // Soft streaks of cloud, lit pink from below by the sun. They stand still: a sky that moves
    // has its reflections filtered again every frame.
    vec2 cp = dir.xz / (dir.y + 0.18) * 1.3;
    float cloud = smoothstep(0.50, 0.85, fbm(cp * vec2(0.55, 1.2))) * smoothstep(0.02, 0.25, dir.y);
    day = mix(day, mix(vec3(0.98, 0.62, 0.62), vec3(1.0, 0.9, 0.82), toward), cloud * 0.55);
    dark = mix(dark, vec3(0.10, 0.14, 0.26), cloud * 0.5);

    // Stars come out with the night.
    vec2 sp = dir.xz / (abs(dir.y) + 0.35) * 70.0;
    float star = step(0.992, hash21(floor(sp))) * smoothstep(0.35, 0.0, length(fract(sp) - 0.5)) * smoothstep(0.05, 0.3, dir.y);
    dark += vec3(0.9, 0.95, 1.0) * star * (1.0 - cloud);

    vec3 col = mix(day, dark, night);
    // Below the horizon: the haze the sea fades into.
    col = mix(col, mix(horizon_colour * 0.75, night_horizon, night), smoothstep(0.0, -0.08, dir.y));
    COLOR = col;
}
";

        private const string HealthBarCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;
uniform float hp = 1.0;
void vertex() {
    // Faces the camera wherever it stands, at the node's own scale.
    mat4 facing = mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    facing = facing * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
    MODELVIEW_MATRIX = VIEW_MATRIX * facing;
    MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}
void fragment() {
    float inside = step(0.03, UV.x) * step(UV.x, 0.97) * step(0.2, UV.y) * step(UV.y, 0.8);
    vec3 fill = mix(vec3(0.95, 0.30, 0.22), vec3(0.72, 0.10, 0.12), UV.y);
    vec3 bar = mix(vec3(0.16, 0.17, 0.22), fill, step(UV.x, 0.03 + 0.94 * hp));
    ALBEDO = mix(vec3(0.03, 0.03, 0.05), bar, inside);
}
";

        /// <summary>A bar over an enemy's head; each enemy has its own, set through the <c>hp</c> parameter (0..1).</summary>
        public static ShaderMaterial HealthBar() => new ShaderMaterial { Shader = Get("healthbar", HealthBarCode) };

        private static readonly Dictionary<string, Shader> Compiled = new Dictionary<string, Shader>();
        private static readonly Dictionary<string, ShaderMaterial> Materials = new Dictionary<string, ShaderMaterial>();

        private static Shader Get(string name, string code)
        {
            if (!Compiled.TryGetValue(name, out var shader)) Compiled[name] = shader = new Shader { Code = code };
            return shader;
        }

        /// <param name="groundMap">Red: path, green: lawn, blue: paving, over the rectangle of ground from <paramref name="origin"/>.</param>
        public static ShaderMaterial Ground(Texture2D groundMap, Vector2 origin, Vector2 size)
        {
            var material = new ShaderMaterial { Shader = Get("ground", GroundCode) };
            material.SetShaderParameter("ground_map", groundMap);
            material.SetShaderParameter("map_origin", origin);
            material.SetShaderParameter("map_size", size);
            return material;
        }

        public static ShaderMaterial Clapboard(Color colour) => Coloured("clapboard", ClapboardCode, colour);

        public static ShaderMaterial Shingles(Color colour) => Coloured("shingles", ShingleCode, colour);

        /// <param name="bottom">Mesh-space height where the plant's green starts.</param>
        /// <param name="height">How far above that it reaches.</param>
        public static ShaderMaterial Foliage(Color colour, Color tip, float bottom, float height)
        {
            string key = $"foliage/{colour.ToHtml()}/{tip.ToHtml()}/{bottom:0.##}/{height:0.##}";
            if (Materials.TryGetValue(key, out var cached)) return cached;
            var material = new ShaderMaterial { Shader = Get("foliage", FoliageCode) };
            material.SetShaderParameter("colour", colour);
            material.SetShaderParameter("tip", tip);
            material.SetShaderParameter("base", bottom);
            material.SetShaderParameter("height", height);
            return Materials[key] = material;
        }

        public static ShaderMaterial Water() => new ShaderMaterial { Shader = Get("water", WaterCode) };

        public static ShaderMaterial Sky() => new ShaderMaterial { Shader = Get("sky", SkyCode) };

        private static ShaderMaterial Coloured(string name, string code, Color colour)
        {
            string key = $"{name}/{colour.ToHtml()}";
            if (Materials.TryGetValue(key, out var cached)) return cached;
            var material = new ShaderMaterial { Shader = Get(name, code) };
            material.SetShaderParameter("colour", colour);
            return Materials[key] = material;
        }
    }
}
