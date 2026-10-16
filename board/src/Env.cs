using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UtilityBoard
{
    // LOCAL-ONLY time-of-day and weather visuals. Uses only BetterDayNightManager's local methods
    // (SetTimeOfDay / ClearTimeOfDay / SetFixedWeather / ClearFixedWeather) - never the *Networked variants or RPCs.
    // Snow and fog are this mod's own client-side effects. Everything is restored by RestoreAll().
    public sealed class Env
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        public enum Preset { Sunrise, Morning, Daytime, Sunset, Nighttime }
        public static readonly string[] PresetNames = { "Sunrise", "Morning", "Daytime", "Sunset", "Nighttime" };
        public enum Weather { Default, Clear, Rain, Snow, Fog, Thunderstorm }

        private Type t; private object dnm;
        public string[] LightmapNames = new string[0]; public double[] Ranges = new double[0];
        public int[] PresetIndex = { -1, -1, -1, -1, -1 };
        public bool[] PresetEnabled = { false, false, false, false, false };   // only presets verified in the self-test are switched on (see Plugin)
        public int ActivePreset = -1; public Weather ActiveWeather = Weather.Default; public float Intensity = .6f; public bool Lightning;
        public string Note = ""; public int LightningIndex = -1;
        private readonly Action<string> log;
        public Env(Action<string> l) { log = l; }

        public bool Ready()
        {
            if (dnm != null && !(dnm is UnityEngine.Object uo && uo == null)) return true;
            if (t == null) t = GTApi.GameType("BetterDayNightManager"); if (t == null) { Note = "BetterDayNightManager missing"; return false; }
            var f = t.GetField("instance", All); dnm = f?.GetValue(null); if (dnm == null || (dnm is UnityEngine.Object o && o == null)) { dnm = null; return false; }
            try
            {
                LightmapNames = t.GetField("dayNightLightmapNames", All)?.GetValue(dnm) as string[] ?? new string[0];
                Ranges = t.GetProperty("timeOfDayRange", All)?.GetValue(dnm, null) as double[] ?? new double[0];
            }
            catch (Exception e) { log("Env: reading lightmap names failed " + e.Message); }
            AutoMap();
            LightningIndex = Array.FindIndex(LightmapNames, n => (n ?? "").ToLowerInvariant().Contains("lightning"));
            log("Env: BetterDayNightManager found. lightmaps [" + string.Join(", ", LightmapNames) + "] ranges [" + string.Join(", ", Array.ConvertAll(Ranges, r => r.ToString("0.##"))) + "] preset map " + string.Join(",", PresetIndex));
            return true;
        }
        // Map each preset to a lightmap by name; config can override (Plugin passes "a,b,c,d,e").
        public string Override = "auto";
        private void AutoMap()
        {
            if (Override != "auto") { var p = Override.Split(','); for (int i = 0; i < 5 && i < p.Length; i++) { int v; if (int.TryParse(p[i].Trim(), out v) && v >= 0 && v < LightmapNames.Length) PresetIndex[i] = v; } return; }
            string[][] keys = { new[] { "sunrise", "dawn" }, new[] { "morning", "10am", "9am", "8am", "11am" }, new[] { "noon", "12pm", "1pm", "day", "afternoon" }, new[] { "sunset", "evening", "dusk" }, new[] { "night", "midnight" } };
            for (int p = 0; p < 5; p++)
            {
                PresetIndex[p] = -1;
                for (int i = 0; i < LightmapNames.Length && PresetIndex[p] < 0; i++) foreach (var k in keys[p]) if ((LightmapNames[i] ?? "").ToLowerInvariant().Contains(k) && Array.IndexOf(PresetIndex, i) < 0) { PresetIndex[p] = i; break; }
            }
        }

        private object Call(string name, params object[] a)
        {
            var types = Array.ConvertAll(a, x => x.GetType());
            var m = t.GetMethod(name, All, null, types, null);
            if (m == null) { foreach (var mm in t.GetMethods(All)) if (mm.Name == name && mm.GetParameters().Length == a.Length) { m = mm; break; } }
            if (m == null) throw new MissingMethodException(name);
            var ps = m.GetParameters(); for (int i = 0; i < a.Length; i++) if (ps[i].ParameterType.IsEnum) a[i] = Enum.ToObject(ps[i].ParameterType, a[i]);
            return m.Invoke(dnm, a);
        }
        public string TimeString() { try { return Ready() ? Call("GetTimeOfDayString") as string : "?"; } catch (Exception) { return "?"; } }
        public string WeatherString() { try { return Ready() ? Call("GetWeatherString") as string : "?"; } catch (Exception) { return "?"; } }
        public int CurrentIndex() { try { return Ready() ? (int)t.GetField("currentTimeIndex", All).GetValue(dnm) : -1; } catch (Exception) { return -1; } }
        private int LastSent() { try { return (int)t.GetField("lastSentTimeIndex", All).GetValue(dnm); } catch (Exception) { return -2; } }
        private string Setting() { try { return t.GetField("currentSetting", All).GetValue(dnm).ToString(); } catch (Exception) { return "?"; } }

        public string SetPreset(int p, bool force = false)
        {
            if (!Ready()) return "Day/night system not loaded yet";
            if (p < 0 || p > 4 || PresetIndex[p] < 0) return "Not available on this build";
            if (!PresetEnabled[p] && !force) return "Not verified on this build";
            try
            {
                // SetTimeOfDay puts the clock exactly on the slot boundary, where floating-point rounding can land in the
                // previous slot (the game itself special-cases "10am" for this). Nudge 2 minutes into the slot.
                Call("SetTimeOfDay", PresetIndex[p], false);
                var ct = t.GetField("currentTime", All); if (ct != null) ct.SetValue(dnm, (double)ct.GetValue(dnm) + 120.0);
                t.GetMethod("FindTimeOfDayIndex", All)?.Invoke(dnm, null); Call("UpdateTimeOfDay", true);
                ActivePreset = p; log("Env: time preset " + PresetNames[p] + " -> lightmap " + PresetIndex[p] + " '" + LightmapNames[PresetIndex[p]] + "' now " + TimeString()); return null; }
            catch (Exception e) { log("Env: SetTimeOfDay failed " + e); return "Failed: " + e.Message; }
        }
        public void Automatic()
        {
            ActivePreset = -1; if (!Ready()) return;
            try { Call("ClearTimeOfDay", true); log("Env: time -> game automatic cycle (" + TimeString() + ")"); } catch (Exception e) { log("Env: ClearTimeOfDay failed " + e.Message); }
        }
        // Another client (room master in a private room) can push a time; re-apply our local choice if it was replaced.
        private float nextCheck;
        public void Tick(Transform cam)
        {
            if (ActivePreset >= 0 && Time.unscaledTime > nextCheck)
            {
                nextCheck = Time.unscaledTime + 2f;
                if (Ready() && (Setting() != "Static" || LastSent() != PresetIndex[ActivePreset])) { log("Env: time was changed by the game - re-applying " + PresetNames[ActivePreset]); SetPreset(ActivePreset, true); }
            }
            if (rain != null) { if (cam != null) rain.transform.position = cam.position + Vector3.up * 8f; var rem = rain.emission; rem.rateOverTimeMultiplier = Mathf.Lerp(300f, 5200f, ActiveWeather == Weather.Thunderstorm ? Mathf.Max(Intensity, .8f) : Intensity); }
            if (snow != null) { if (cam != null) snow.transform.position = cam.position + Vector3.up * 7f; var em = snow.emission; em.rateOverTimeMultiplier = 900f * Intensity; }
            if (ActiveWeather == Weather.Rain || ActiveWeather == Weather.Thunderstorm) ApplyRainIntensity(ActiveWeather == Weather.Thunderstorm ? Mathf.Max(Intensity, .8f) : Intensity);
            if (fogOn) RenderSettings.fogDensity = FogDensity();
            if (ActiveWeather == Weather.Thunderstorm && Lightning && Time.unscaledTime > nextFlash) { nextFlash = Time.unscaledTime + UnityEngine.Random.Range(9f, 20f); Flash(); }
        }

        // ---------- weather ----------
        private readonly List<ParticleSystem> rainPS = new List<ParticleSystem>(); private readonly Dictionary<ParticleSystem, float> rainRate = new Dictionary<ParticleSystem, float>();
        public int RainSystems => rainPS.Count; private bool rainSearched;
        private void FindRain()
        {
            if (rainPS.Count > 0 || rainSearched || !Ready()) return;
            try
            {
                var arr = t.GetField("weatherSystems", All)?.GetValue(dnm) as Array;
                if (arr != null) foreach (var o in arr) { var c = o as Component; if (c == null) continue; foreach (var ps in c.GetComponentsInChildren<ParticleSystem>(true)) if (!rainRate.ContainsKey(ps)) { rainPS.Add(ps); rainRate[ps] = ps.emission.rateOverTimeMultiplier; } }
                if (rainPS.Count == 0) foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>()) if (ps != null && ps.gameObject.scene.IsValid() && ps.name.ToLowerInvariant().Contains("rain") && !ps.name.StartsWith("UB_")) { rainPS.Add(ps); rainRate[ps] = ps.emission.rateOverTimeMultiplier; }
                log("Env: " + rainPS.Count + " game rain particle systems" + (rainPS.Count > 0 ? " (" + rainPS[0].name + ")" : ""));
                rainSearched = true;
            }
            catch (Exception e) { log("Env: rain lookup failed " + e.Message); }
        }
        private float rainK = -1;
        private void ApplyRainIntensity(float k)
        {
            if (Mathf.Abs(k - rainK) < .001f) return; rainK = k; FindRain();
            foreach (var ps in rainPS) if (ps != null) { var em = ps.emission; em.rateOverTimeMultiplier = rainRate[ps] * Mathf.Lerp(.1f, 1.6f, k); }
        }
        private void RestoreRainRates() { foreach (var ps in rainPS) if (ps != null) { var em = ps.emission; em.rateOverTimeMultiplier = rainRate[ps]; } rainK = -1; }
        public int RainAlive() { int n = 0; foreach (var ps in rainPS) if (ps != null) n += ps.particleCount; return n; }
        public float RainRateNow() { float r = 0; foreach (var ps in rainPS) if (ps != null && ps.gameObject.activeInHierarchy) r += ps.emission.rateOverTimeMultiplier; return r; }

        // The game's rain particle systems are not present in every map, so the mod also draws its own local rain.
        private ParticleSystem rain; public int OwnRainAlive => rain != null ? rain.particleCount : 0;
        private void RainOn(bool on)
        {
            if (!on) { if (rain != null) UnityEngine.Object.Destroy(rain.gameObject); rain = null; return; }
            if (rain != null) return;
            var go = new GameObject("UB_Rain"); rain = go.AddComponent<ParticleSystem>(); rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = rain.main; m.loop = true; m.startLifetime = 1.4f; m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(.012f, .02f); m.gravityModifier = 0f;
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 8000; m.startColor = new Color(.78f, .84f, 1f, .55f);
            var v = rain.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; v.x = new ParticleSystem.MinMaxCurve(-.4f, .4f); v.y = new ParticleSystem.MinMaxCurve(-11f, -9f); v.z = new ParticleSystem.MinMaxCurve(-.4f, .4f);
            var sh = rain.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(22f, .5f, 22f);
            var col = rain.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.lifetimeLoss = 1f; col.quality = ParticleSystemCollisionQuality.Low; col.collidesWith = ~(1 << 4) & ~(1 << 18);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .035f; r.lengthScale = 1f; r.sharedMaterial = SnowMaterial();
            rain.Play();
        }
        private ParticleSystem snow; public string SnowMaterialNote = "";
        public int SnowAlive => snow != null ? snow.particleCount : 0;
        private void SnowOn(bool on)
        {
            if (!on) { if (snow != null) UnityEngine.Object.Destroy(snow.gameObject); snow = null; return; }
            if (snow != null) return;
            var go = new GameObject("UB_Snow"); snow = go.AddComponent<ParticleSystem>(); snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = snow.main; m.loop = true; m.startLifetime = 9f; m.startSpeed = .4f; m.startSize = new ParticleSystem.MinMaxCurve(.04f, .08f); m.gravityModifier = .045f;
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 6000; m.startColor = new Color(1f, 1f, 1f, .9f);
            var sh = snow.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(26f, .5f, 26f);
            var em = snow.emission; em.rateOverTimeMultiplier = 900f * Intensity;
            var no = snow.noise; no.enabled = true; no.strength = .35f; no.frequency = .25f;
            var col = snow.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.lifetimeLoss = 1f; col.quality = ParticleSystemCollisionQuality.Low; col.collidesWith = ~(1 << 4) & ~(1 << 18);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Billboard; r.sharedMaterial = SnowMaterial();
            snow.Play();
        }
        private Material SnowMaterial()
        {
            FindRain();
            foreach (var ps in rainPS) { var pr = ps != null ? ps.GetComponent<ParticleSystemRenderer>() : null; if (pr != null && pr.sharedMaterial != null) { var m = new Material(pr.sharedMaterial); SnowMaterialNote = "copied from game rain '" + pr.sharedMaterial.shader.name + "'"; return m; } }
            foreach (var n in new[] { "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit", "Sprites/Default", "Unlit/Color" })
            { var s = Shader.Find(n); if (s != null) { SnowMaterialNote = "shader " + n; return new Material(s); } }
            SnowMaterialNote = "default primitive material"; var g = GameObject.CreatePrimitive(PrimitiveType.Quad); var mm = g.GetComponent<Renderer>().sharedMaterial; UnityEngine.Object.Destroy(g); return mm;
        }

        // Light fog only (capped) so it never hides other players / gives an advantage or handicap.
        public const float FogMax = .012f;
        private bool fogOn, fogSaved, oFog; private FogMode oMode; private float oDen; private Color oCol;
        private float FogDensity() => .003f + (FogMax - .003f) * Intensity;
        private void FogOn(bool on)
        {
            if (on && !fogOn) { if (!fogSaved) { oFog = RenderSettings.fog; oMode = RenderSettings.fogMode; oDen = RenderSettings.fogDensity; oCol = RenderSettings.fogColor; fogSaved = true; }
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogColor = new Color(.74f, .71f, .82f); RenderSettings.fogDensity = FogDensity(); fogOn = true; }
            else if (!on && fogOn) { RenderSettings.fog = oFog; RenderSettings.fogMode = oMode; RenderSettings.fogDensity = oDen; RenderSettings.fogColor = oCol; fogOn = false; }
        }
        public bool FogActive => fogOn;
        // Verified in the self-test: Gorilla Tag's own shaders ignore Unity scene fog, so the fog control is disabled.
        public static bool FogSupported = false;

        private float nextFlash; public int Flashes;
        private void Flash()
        {
            try { int bright = LightningIndex >= 0 ? LightningIndex : PresetIndex[2] >= 0 ? PresetIndex[2] : 0; Call("AnimateLightFlash", bright, .05f, .08f, .35f); Flashes++; } catch (Exception e) { log("Env: flash failed " + e.Message); }
        }

        public string SetWeather(Weather w)
        {
            if (!Ready()) return "Day/night system not loaded yet";
            try
            {
                if (w == Weather.Fog && !FogSupported) return "Fog not supported: the game's shaders ignore scene fog";
                SnowOn(w == Weather.Snow); FogOn(w == Weather.Fog); RainOn(w == Weather.Rain || w == Weather.Thunderstorm);
                if (w == Weather.Rain || w == Weather.Thunderstorm) { Call("SetFixedWeather", 1, true); rainK = -1; }
                else { RestoreRainRates(); if (w == Weather.Default) Call("ClearFixedWeather", true); else Call("SetFixedWeather", 0, true); }
                ActiveWeather = w; nextFlash = Time.unscaledTime + 6f;
                log("Env: weather " + w + " (game weather " + WeatherString() + ", intensity " + Mathf.RoundToInt(Intensity * 100) + "%)"); return null;
            }
            catch (Exception e) { log("Env: weather failed " + e); return "Failed: " + e.Message; }
        }
        public void RestoreAll(string why)
        {
            bool any = ActivePreset >= 0 || ActiveWeather != Weather.Default || snow != null || rain != null || fogOn;
            if (!any) return;
            SnowOn(false); FogOn(false); RainOn(false); RestoreRainRates();
            if (Ready()) { try { Call("ClearFixedWeather", true); Call("ClearTimeOfDay", true); } catch (Exception e) { log("Env: restore failed " + e.Message); } }
            ActivePreset = -1; ActiveWeather = Weather.Default; log("Env: restored game default time + weather (" + why + ")");
        }
    }
}
