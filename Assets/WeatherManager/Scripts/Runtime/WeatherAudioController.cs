using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherManager
{
    public sealed class WeatherAudioController : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const int OneShotPoolSize = 10;

        [Header("Baked audio pool")]
        [SerializeField] private Transform audioRoot;
        [SerializeField] private AudioSource weatherSource;
        [SerializeField] private List<AudioSource> oneShotSources = new List<AudioSource>(OneShotPoolSize);
        private readonly Dictionary<string, float> cueTimes = new Dictionary<string, float>();
        private readonly Dictionary<WeatherType, AudioClip> weatherLoops = new Dictionary<WeatherType, AudioClip>();
        private AudioClip lightningClip;
        private AudioClip collapseClip;
        private AudioClip floodClip;
        private AudioClip steamClip;
        private AudioClip snowCollapseClip;
        private AudioClip snowLoadClip;
        private AudioClip recoveryClip;
        private AudioClip fireClip;
        private AudioClip tornadoClip;
        private AudioClip uiSelectClip;
        private AudioClip uiToggleClip;
        private AudioClip resetClip;
        private AudioClip validTargetClip;
        private AudioClip invalidTargetClip;
        private AudioClip castStartClip;
        private AudioClip castStopClip;
        private bool enabledAudio = true;
        private float targetWeatherVolume;
        private int nextSource;
        private WeatherType activeWeather;
        private bool weatherActive;

        public bool Enabled => enabledAudio;

        public void Initialize(Transform parent, Transform listener)
        {
            if (audioRoot != null)
            {
                PrepareBakedScene(parent, listener);
                return;
            }

            audioRoot = new GameObject("WeatherAudio").transform;
            audioRoot.SetParent(parent, false);

            weatherSource = audioRoot.gameObject.AddComponent<AudioSource>();
            weatherSource.loop = true;
            weatherSource.playOnAwake = false;
            weatherSource.spatialBlend = 0.24f;
            weatherSource.minDistance = 4f;
            weatherSource.maxDistance = 75f;
            weatherSource.rolloffMode = AudioRolloffMode.Linear;
            weatherSource.volume = 0f;

            for (var index = 0; index < OneShotPoolSize; index++)
            {
                var sourceObject = new GameObject($"Cue_{index:00}");
                sourceObject.transform.SetParent(audioRoot, false);
                var source = sourceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0.52f;
                source.minDistance = 2.5f;
                source.maxDistance = 65f;
                source.rolloffMode = AudioRolloffMode.Linear;
                oneShotSources.Add(source);
            }

            EnsureProceduralClips();
        }

        public void PrepareBakedScene(Transform parent, Transform listener)
        {
            if (audioRoot == null)
            {
                audioRoot = FindChild(parent != null ? parent : transform, "WeatherAudio");
            }
            if (audioRoot == null)
            {
                Initialize(parent != null ? parent : transform, listener);
                return;
            }

            weatherSource ??= audioRoot.GetComponent<AudioSource>();
            oneShotSources.Clear();
            var sources = audioRoot.GetComponentsInChildren<AudioSource>(true);
            for (var index = 0; index < sources.Length; index++)
            {
                if (sources[index] != weatherSource) oneShotSources.Add(sources[index]);
            }
            EnsureProceduralClips();
            nextSource = 0;
            if (Application.isPlaying) ResetAll();
        }

        private void EnsureProceduralClips()
        {
            if (weatherLoops.Count == 0)
            {
                weatherLoops[WeatherType.Wind] = CreateNoiseLoop("WindLoop", 2.2f, 0.78f, 0.16f, 0.02f, 8181);
                weatherLoops[WeatherType.Rain] = CreateRainLoop();
                weatherLoops[WeatherType.Electric] = CreateToneClip("ElectricHum", 1.6f, 78f, 0.1f, true);
                weatherLoops[WeatherType.Snow] = CreateNoiseLoop("SnowLoop", 2.4f, 0.12f, 0.035f, 0.005f, 9191);
                weatherLoops[WeatherType.Heat] = CreateToneClip("HeatLoop", 2f, 112f, 0.06f, true);
                lightningClip = CreateLightningClip();
                collapseClip = CreateImpactClip("Collapse", 0.72f, 54f, 0.32f, 7272);
                fireClip = CreateNoiseLoop("FireCue", 0.8f, 0.48f, 0.18f, 0.04f, 3131);
                tornadoClip = CreateNoiseLoop("TornadoCue", 1.2f, 0.9f, 0.24f, 0.04f, 5555);
                uiSelectClip = CreateToneClip("UiSelect", 0.1f, 620f, 0.13f, false);
                uiToggleClip = CreateToneClip("UiToggle", 0.13f, 430f, 0.14f, false);
                resetClip = CreateSweepClip("Reset", 0.32f, 280f, 740f, 0.18f);
            }

            validTargetClip ??= CreateToneClip("ValidTarget", 0.055f, 760f, 0.065f, false);
            invalidTargetClip ??= CreateToneClip("InvalidTarget", 0.09f, 145f, 0.1f, false);
            castStartClip ??= CreateSweepClip("CastStart", 0.16f, 240f, 690f, 0.1f);
            castStopClip ??= CreateSweepClip("CastStop", 0.13f, 580f, 250f, 0.085f);
            floodClip ??= CreateFloodClip();
            steamClip ??= CreateSteamClip();
            snowCollapseClip ??= CreateSnowCollapseClip();
            snowLoadClip ??= CreateImpactClip("SnowLoadCreak", 0.62f, 92f, 0.16f, 5858);
            recoveryClip ??= CreateSweepClip("HazardRecovery", 0.42f, 220f, 520f, 0.13f);
        }

        public void BeginWeather(WeatherType type, Vector3 position)
        {
            activeWeather = type;
            weatherActive = true;
            PlayCastStart(type, position);
            if (weatherSource == null || !weatherLoops.TryGetValue(type, out var weatherClip))
            {
                return;
            }
            weatherSource.transform.position = position + Vector3.up * 1.2f;
            weatherSource.clip = weatherClip;
            weatherSource.pitch = WeatherPitch(type);
            targetWeatherVolume = enabledAudio ? WeatherVolume(type) : 0f;
            if (enabledAudio)
            {
                weatherSource.volume = Mathf.Min(weatherSource.volume, 0.04f);
                weatherSource.Play();
            }
            else
            {
                StopWeatherLoop();
            }
        }

        public void UpdateWeatherPosition(Vector3 position)
        {
            if (weatherSource != null)
            {
                weatherSource.transform.position = position + Vector3.up * 1.2f;
            }
        }

        public void EndWeather(bool immediate)
        {
            var wasActive = weatherActive;
            weatherActive = false;
            targetWeatherVolume = 0f;
            StopWeatherLoop();
            if (wasActive && !immediate)
            {
                PlayCastStop();
            }
        }

        public void SetEnabled(bool value)
        {
            enabledAudio = value;
            if (!value)
            {
                targetWeatherVolume = 0f;
                StopWeatherLoop();
                StopTransients();
            }
            else if (weatherSource != null && weatherActive && weatherLoops.TryGetValue(activeWeather, out var activeClip))
            {
                weatherSource.clip = activeClip;
                weatherSource.pitch = WeatherPitch(activeWeather);
                targetWeatherVolume = WeatherVolume(activeWeather);
                weatherSource.volume = 0f;
                if (!weatherSource.isPlaying) weatherSource.Play();
            }
        }

        public void Tick(float unscaledDelta)
        {
            if (weatherSource == null)
            {
                return;
            }
            weatherSource.volume = Mathf.MoveTowards(weatherSource.volume, targetWeatherVolume, unscaledDelta * 0.55f);
            if (targetWeatherVolume <= 0f && weatherSource.volume <= 0.002f && weatherSource.isPlaying)
            {
                weatherSource.Stop();
                weatherSource.volume = 0f;
            }
        }

        public void PlayLightning(Vector3 position)
        {
            PlayLightning(position, BiomeType.Town, false);
        }

        public void PlayLightning(Vector3 position, BiomeType biome, bool ignited)
        {
            var volume = biome == BiomeType.Highland ? 1f : biome == BiomeType.Water ? 0.72f : 0.9f;
            var pitch = BiomePitch(biome) * (ignited ? 1.04f : 0.98f);
            PlaySpatial(LightningCue(biome), lightningClip, position, volume, 0.08f, pitch);
        }

        public void PlayCollapse(Vector3 position)
        {
            PlayCollapse(position, BiomeType.Town);
        }

        public void PlayCollapse(Vector3 position, BiomeType biome)
        {
            PlaySpatial(
                CollapseCue(biome),
                collapseClip,
                position,
                biome == BiomeType.Highland ? 0.84f : 0.72f,
                0.18f,
                UnityEngine.Random.Range(0.9f, 1.03f) * BiomePitch(biome));
        }

        public void PlayFlood(Vector3 position)
        {
            PlayFlood(position, BiomeType.Lowland);
        }

        public void PlayFlood(Vector3 position, BiomeType biome)
        {
            var volume = biome == BiomeType.Lowland || biome == BiomeType.Water ? 0.72f : 0.58f;
            PlaySpatial(FloodCue(biome), floodClip, position, volume, 1.6f, UnityEngine.Random.Range(0.94f, 1.02f) * BiomePitch(biome));
        }

        public void PlaySteam(Vector3 position)
        {
            PlaySteam(position, BiomeType.Town);
        }

        public void PlaySteam(Vector3 position, BiomeType biome)
        {
            PlaySpatial(SteamCue(biome), steamClip, position, 0.58f, 0.9f, UnityEngine.Random.Range(0.96f, 1.05f) * BiomePitch(biome));
        }

        public void PlaySnowCollapse(Vector3 position)
        {
            PlaySnowCollapse(position, BiomeType.Highland);
        }

        public void PlaySnowCollapse(Vector3 position, BiomeType biome)
        {
            PlaySpatial(SnowCue(biome), snowCollapseClip, position, 0.72f, 0.24f, UnityEngine.Random.Range(0.92f, 1.02f) * BiomePitch(biome));
        }

        public void PlayFire(Vector3 position)
        {
            PlayFire(position, BiomeType.Town);
        }

        public void PlayFire(Vector3 position, BiomeType biome)
        {
            var volume = biome == BiomeType.Forest || biome == BiomeType.Farmland ? 0.46f : 0.36f;
            PlaySpatial(FireCue(biome), fireClip, position, volume, 1.1f, UnityEngine.Random.Range(0.93f, 1.05f) * BiomePitch(biome));
        }

        public void PlaySnowLoad(Vector3 position, BiomeType biome)
        {
            PlaySpatial(SnowLoadCue(biome), snowLoadClip, position, 0.48f, 1.8f, BiomePitch(biome));
        }

        public void PlayRecovery(Vector3 position, BiomeType biome, WeatherType resolvedHazard)
        {
            PlaySpatial(RecoveryCue(resolvedHazard), recoveryClip, position, 0.38f, 0.8f, BiomePitch(biome));
        }

        public void PlayTornado(Vector3 position)
        {
            PlaySpatial("tornado", tornadoClip, position, 0.58f, 1.5f, 0.88f);
        }

        public void PlayUiSelect()
        {
            PlayUi("ui-select", uiSelectClip, 0.26f, 0.045f);
        }

        public void PlayUiToggle()
        {
            PlayUi("ui-toggle", uiToggleClip, 0.28f, 0.07f);
        }

        public void PlayReset()
        {
            PlayUi("reset", resetClip, 0.42f, 0.1f);
        }

        public void PlayTargetFeedback(bool valid, Vector3 position)
        {
            if (valid)
            {
                PlaySpatial("target-valid", validTargetClip, position, 0.22f, 0.12f, 1f);
            }
            else
            {
                PlayUi("target-invalid", invalidTargetClip, 0.26f, 0.12f);
            }
        }

        public void PlayInvalidTarget()
        {
            PlayUi("target-invalid-click", invalidTargetClip, 0.34f, 0.08f);
        }

        public void ResetAll()
        {
            weatherActive = false;
            activeWeather = WeatherType.Wind;
            targetWeatherVolume = 0f;
            StopWeatherLoop();
            StopTransients();
            nextSource = 0;
            cueTimes.Clear();
        }

        public void StopTransients()
        {
            for (var index = 0; index < oneShotSources.Count; index++)
            {
                var source = oneShotSources[index];
                if (source == null) continue;
                source.Stop();
                source.pitch = 1f;
            }
        }

        private void OnDisable()
        {
            ResetAll();
        }

        private static Transform FindChild(Transform parent, string objectName)
        {
            if (parent == null) return null;
            var transforms = parent.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == objectName) return transforms[index];
            }
            return null;
        }

        private void PlaySpatial(string cue, AudioClip clip, Vector3 position, float volume, float cooldown, float pitch)
        {
            if (!CanPlay(cue, cooldown) || clip == null)
            {
                return;
            }
            var source = NextSource();
            source.transform.position = position;
            source.spatialBlend = 0.58f;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume);
        }

        private void PlayUi(string cue, AudioClip clip, float volume, float cooldown)
        {
            if (!CanPlay(cue, cooldown) || clip == null)
            {
                return;
            }
            var source = NextSource();
            source.transform.localPosition = Vector3.zero;
            source.spatialBlend = 0f;
            source.pitch = 1f;
            source.PlayOneShot(clip, volume);
        }

        private bool CanPlay(string cue, float cooldown)
        {
            if (!enabledAudio || oneShotSources.Count == 0)
            {
                return false;
            }
            var now = Time.unscaledTime;
            if (cueTimes.TryGetValue(cue, out var previous) && now - previous < cooldown)
            {
                return false;
            }
            cueTimes[cue] = now;
            return true;
        }

        private AudioSource NextSource()
        {
            var source = oneShotSources[nextSource];
            nextSource = (nextSource + 1) % oneShotSources.Count;
            source.Stop();
            return source;
        }

        private void PlayCastStart(WeatherType type, Vector3 position)
        {
            PlaySpatial("cast-start", castStartClip, position, 0.28f, 0.06f, WeatherPitch(type));
        }

        private void PlayCastStop()
        {
            PlayUi("cast-stop", castStopClip, 0.25f, 0.06f);
        }

        private void StopWeatherLoop()
        {
            if (weatherSource == null) return;
            weatherSource.Stop();
            weatherSource.volume = 0f;
            weatherSource.clip = null;
        }

        private static float WeatherPitch(WeatherType type)
        {
            return type == WeatherType.Snow ? 0.86f : type == WeatherType.Heat ? 0.78f : 1f;
        }

        private static float WeatherVolume(WeatherType type)
        {
            return type switch
            {
                WeatherType.Wind => 0.18f,
                WeatherType.Rain => 0.16f,
                WeatherType.Electric => 0.09f,
                WeatherType.Snow => 0.075f,
                WeatherType.Heat => 0.085f,
                _ => 0.12f
            };
        }

        private static float BiomePitch(BiomeType biome)
        {
            return biome switch
            {
                BiomeType.Forest => 0.92f,
                BiomeType.Farmland => 1.06f,
                BiomeType.Highland => 0.82f,
                BiomeType.Lowland => 0.9f,
                BiomeType.Water => 1.1f,
                _ => 1f
            };
        }

        private static string LightningCue(BiomeType biome) => biome switch
        {
            BiomeType.Forest => "lightning-forest",
            BiomeType.Farmland => "lightning-farmland",
            BiomeType.Highland => "lightning-highland",
            BiomeType.Lowland => "lightning-lowland",
            BiomeType.Water => "lightning-water",
            _ => "lightning-town"
        };

        private static string CollapseCue(BiomeType biome) => biome == BiomeType.Highland ? "collapse-highland" : "collapse-structure";
        private static string FloodCue(BiomeType biome) => biome switch
        {
            BiomeType.Farmland => "flood-farmland",
            BiomeType.Town => "flood-town",
            BiomeType.Highland => "flood-highland",
            _ => "flood-lowland"
        };
        private static string SteamCue(BiomeType biome) => biome == BiomeType.Forest ? "steam-forest" : biome == BiomeType.Farmland ? "steam-farmland" : "steam-structure";
        private static string SnowCue(BiomeType biome) => biome == BiomeType.Highland ? "snow-collapse-highland" : biome == BiomeType.Forest ? "snow-collapse-forest" : "snow-collapse-structure";
        private static string SnowLoadCue(BiomeType biome) => biome == BiomeType.Highland ? "snow-load-highland" : biome == BiomeType.Forest ? "snow-load-forest" : "snow-load-structure";
        private static string FireCue(BiomeType biome) => biome switch
        {
            BiomeType.Forest => "fire-forest",
            BiomeType.Farmland => "fire-farmland",
            BiomeType.Highland => "fire-highland",
            BiomeType.Lowland => "fire-lowland",
            _ => "fire-town"
        };
        private static string RecoveryCue(WeatherType hazard) => hazard switch
        {
            WeatherType.Rain => "recovery-flood",
            WeatherType.Snow => "recovery-snow",
            WeatherType.Electric => "recovery-grid",
            WeatherType.Heat => "recovery-fire",
            _ => "recovery-hazard"
        };

        private static AudioClip CreateNoiseLoop(
            string name,
            float duration,
            float roughness,
            float volume,
            float crackle,
            int seed)
        {
            var length = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[length];
            var random = new System.Random(seed);
            var low = 0f;
            for (var index = 0; index < length; index++)
            {
                var white = (float)(random.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, white, roughness * 0.08f);
                var spark = random.NextDouble() < crackle ? white * 0.7f : 0f;
                var edgeFade = Mathf.Min(1f, Mathf.Min(index, length - index - 1) / (SampleRate * 0.035f));
                data[index] = Mathf.Clamp((low + spark) * volume * edgeFade, -1f, 1f);
            }
            return CreateClip(name, data);
        }

        private static AudioClip CreateRainLoop()
        {
            var length = SampleRate * 2;
            var data = new float[length];
            var random = new System.Random(4242);
            var high = 0f;
            for (var index = 0; index < length; index++)
            {
                var white = (float)(random.NextDouble() * 2.0 - 1.0);
                high = white - high * 0.18f;
                var droplet = random.NextDouble() < 0.008 ? white * 0.45f : 0f;
                var edgeFade = Mathf.Min(1f, Mathf.Min(index, length - index - 1) / (SampleRate * 0.03f));
                data[index] = (high * 0.055f + droplet) * edgeFade;
            }
            return CreateClip("RainLoop", data);
        }

        private static AudioClip CreateLightningClip()
        {
            var length = Mathf.CeilToInt(1.35f * SampleRate);
            var data = new float[length];
            var random = new System.Random(991);
            var low = 0f;
            for (var index = 0; index < length; index++)
            {
                var t = index / (float)SampleRate;
                var decay = Mathf.Exp(-t * 2.5f);
                var white = (float)(random.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, white, 0.06f);
                var crack = t < 0.045f ? white * Mathf.Exp(-t * 48f) : 0f;
                data[index] = Mathf.Clamp((low * 0.52f + crack * 0.8f) * decay, -1f, 1f);
            }
            return CreateClip("Lightning", data);
        }

        private static AudioClip CreateFloodClip()
        {
            var length = Mathf.CeilToInt(1.05f * SampleRate);
            var data = new float[length];
            var random = new System.Random(6161);
            var low = 0f;
            for (var index = 0; index < length; index++)
            {
                var t = index / (float)SampleRate;
                var normalized = index / (float)(length - 1);
                var white = (float)(random.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, white, 0.035f);
                var surge = Mathf.Sin(t * Mathf.Lerp(46f, 27f, normalized) * Mathf.PI * 2f) * 0.18f;
                var splash = random.NextDouble() < 0.009 ? Mathf.Abs(white) * 0.24f : 0f;
                var envelope = Mathf.Sin(normalized * Mathf.PI);
                data[index] = Mathf.Clamp((low * 0.48f + surge + splash) * envelope, -1f, 1f);
            }
            return CreateClip("FloodSurge", data);
        }

        private static AudioClip CreateSteamClip()
        {
            var length = Mathf.CeilToInt(0.82f * SampleRate);
            var data = new float[length];
            var random = new System.Random(8383);
            var previous = 0f;
            for (var index = 0; index < length; index++)
            {
                var normalized = index / (float)(length - 1);
                var white = (float)(random.NextDouble() * 2.0 - 1.0);
                var high = white - previous * 0.62f;
                previous = white;
                var envelope = Mathf.Pow(1f - normalized, 1.7f) * Mathf.Min(1f, normalized * 35f);
                data[index] = Mathf.Clamp(high * envelope * 0.28f, -1f, 1f);
            }
            return CreateClip("SteamBurst", data);
        }

        private static AudioClip CreateSnowCollapseClip()
        {
            var length = Mathf.CeilToInt(0.92f * SampleRate);
            var data = new float[length];
            var random = new System.Random(4747);
            for (var index = 0; index < length; index++)
            {
                var t = index / (float)SampleRate;
                var normalized = index / (float)(length - 1);
                var decay = Mathf.Exp(-t * 4.1f);
                var rumble = Mathf.Sin(t * 43f * Mathf.PI * 2f) * 0.24f;
                var crunch = (float)(random.NextDouble() * 2.0 - 1.0) * (0.38f + Mathf.Sin(normalized * 31f) * 0.08f);
                data[index] = Mathf.Clamp((rumble + crunch) * decay * 0.62f, -1f, 1f);
            }
            return CreateClip("SnowCollapse", data);
        }

        private static AudioClip CreateImpactClip(string name, float duration, float frequency, float volume, int seed)
        {
            var length = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[length];
            var random = new System.Random(seed);
            for (var index = 0; index < length; index++)
            {
                var t = index / (float)SampleRate;
                var decay = Mathf.Exp(-t * 5.2f);
                var tone = Mathf.Sin(t * frequency * Mathf.PI * 2f) * 0.42f;
                var noise = (float)(random.NextDouble() * 2.0 - 1.0) * 0.58f;
                data[index] = (tone + noise) * decay * volume;
            }
            return CreateClip(name, data);
        }

        private static AudioClip CreateToneClip(string name, float duration, float frequency, float volume, bool loopable)
        {
            var length = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[length];
            for (var index = 0; index < length; index++)
            {
                var t = index / (float)SampleRate;
                var envelope = loopable ? 1f : Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
                data[index] = (
                    Mathf.Sin(t * frequency * Mathf.PI * 2f)
                    + Mathf.Sin(t * frequency * 1.51f * Mathf.PI * 2f) * 0.24f)
                    * volume
                    * envelope;
            }
            return CreateClip(name, data);
        }

        private static AudioClip CreateSweepClip(string name, float duration, float fromFrequency, float toFrequency, float volume)
        {
            var length = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[length];
            var phase = 0f;
            for (var index = 0; index < length; index++)
            {
                var normalized = index / (float)(length - 1);
                var frequency = Mathf.Lerp(fromFrequency, toFrequency, normalized);
                phase += frequency / SampleRate * Mathf.PI * 2f;
                var envelope = Mathf.Sin(normalized * Mathf.PI);
                data[index] = Mathf.Sin(phase) * envelope * volume;
            }
            return CreateClip(name, data);
        }

        private static AudioClip CreateClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
