using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherManager
{
    public sealed class WeatherVfxController : MonoBehaviour
    {
        private const int MaxSurfaceInstances = 2048;
        private const int MaxFireInstances = 256;
        private const int MaxTornadoInstances = 64;
        private const int MaxRippleInstances = 512;
        private const int MaxDebrisInstances = 40;
        private const int LightningPoolSize = 6;
        private const int CollapsePoolSize = 8;
        private const int BurstPoolSize = 12;
        private const int BoltPointCount = 8;

        private enum EnvironmentalBurstKind
        {
            WindDebris,
            RainSplash,
            ElectricSpark,
            SnowPowder,
            HeatEmber,
            Steam,
            FireEmber,
            MudSplash,
            RockDebris,
            Recovery
        }

        [System.Serializable]
        private sealed class LightningSlot
        {
            public GameObject Root;
            public LineRenderer Bolt;
            public LineRenderer[] Branches;
            public Light Flash;
            public ParticleSystem Impact;
            public MeshRenderer Ring;
            [System.NonSerialized] public float Age;
            [System.NonSerialized] public bool Active;
        }

        [System.Serializable]
        private sealed class CollapseSlot
        {
            public GameObject Root;
            public ParticleSystem Dust;
            public MeshRenderer Ring;
            [System.NonSerialized] public float Age;
            [System.NonSerialized] public float BaseScale;
            [System.NonSerialized] public bool Active;
        }

        [System.Serializable]
        private sealed class BurstSlot
        {
            public Transform Root;
            public ParticleSystem Particles;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Matrix4x4[] waterMatrices = new Matrix4x4[MaxSurfaceInstances];
        private readonly Matrix4x4[] snowMatrices = new Matrix4x4[MaxSurfaceInstances];
        private readonly Matrix4x4[] scorchMatrices = new Matrix4x4[MaxSurfaceInstances];
        private readonly Matrix4x4[] fireMatrices = new Matrix4x4[MaxFireInstances];
        private readonly Matrix4x4[] smokeMatrices = new Matrix4x4[MaxFireInstances];
        private readonly Matrix4x4[] tornadoMatrices = new Matrix4x4[MaxTornadoInstances];
        private readonly Matrix4x4[] fireTornadoMatrices = new Matrix4x4[MaxTornadoInstances];
        private readonly Matrix4x4[] rippleMatrices = new Matrix4x4[MaxRippleInstances];
        private readonly Matrix4x4[] debrisMatrices = new Matrix4x4[MaxDebrisInstances];

        [Header("Baked VFX references")]
        [SerializeField] private Transform effectRoot;
        // Simulation coordinates are authored in the island's local gameplay
        // space.  In MR the island is reparented and scaled onto a physical
        // table, while Graphics.RenderMeshInstanced bypasses Transform
        // inheritance entirely.  Keep this root explicit so every visual
        // route can use the same gameplay-to-world conversion.
        [SerializeField] private Transform gameplayRoot;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Mesh surfaceMesh;
        [SerializeField] private Mesh coneMesh;
        [SerializeField] private Mesh ringMesh;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Material snowMaterial;
        [SerializeField] private Material scorchMaterial;
        [SerializeField] private Material fireMaterial;
        [SerializeField] private Material smokeMaterial;
        [SerializeField] private Material tornadoMaterial;
        [SerializeField] private Material fireTornadoMaterial;
        [SerializeField] private Material rippleMaterial;
        [SerializeField] private Material debrisMaterial;
        [SerializeField] private Material cursorMaterial;
        [SerializeField] private GameObject cursor;
        [SerializeField] private MeshRenderer cursorRenderer;
        [SerializeField] private LineRenderer castTrajectory;
        [SerializeField] private ParticleSystem weatherParticles;
        [SerializeField] private ParticleSystem weatherAccentParticles;
        [SerializeField] private MeshFilter waterPrototype;
        [SerializeField] private MeshFilter snowPrototype;
        [SerializeField] private MeshFilter scorchPrototype;
        [SerializeField] private MeshFilter firePrototype;
        [SerializeField] private MeshFilter smokePrototype;
        [SerializeField] private MeshFilter tornadoPrototype;
        [SerializeField] private MeshFilter fireTornadoPrototype;
        [SerializeField] private MeshFilter ripplePrototype;
        [SerializeField] private MeshFilter debrisPrototype;
        [SerializeField] private MeshFilter snowCollapsePrototype;
        [SerializeField] private Transform transientPoolRoot;
        [SerializeField] private LightningSlot[] lightningPool;
        [SerializeField] private CollapseSlot[] collapsePool;
        [SerializeField] private BurstSlot[] burstPool;
        [SerializeField] private Light skyFlashLight;
        [SerializeField] private Light blackoutBeacon;
        [SerializeField] private Material lightningLineMaterial;
        [SerializeField] private Material lightningRingMaterial;
        [SerializeField] private Material collapseRingMaterial;
        [SerializeField] private Material snowCollapseRingMaterial;
        [SerializeField] private Material burstParticleMaterial;
        [SerializeField] private Material trajectoryMaterial;
        [SerializeField] private WeatherParticlePresentation presentation;
        [SerializeField] private WeatherSurfacePresentation surfacePresentation;
        private float surfaceTimer;
        private ParticleSystem[] clockedTransientParticles;
        public WeatherParticlePresentation Presentation => presentation;
        private WeatherType activeWeather;
        private bool casting;
        private float weatherFade;
        private float targetWeatherFade;
        private int waterCount;
        private int snowCount;
        private int scorchCount;
        private int fireCount;
        private int tornadoCount;
        private int fireTornadoCount;
        private int rippleCount;
        private int debrisCount;
        private float visualTime;
        private bool blackout;
        private Vector3 cursorBaseScale = Vector3.one;
        private bool cursorValid;
        private float castCharge;
        private float skyFlash;
        private float cameraShakeTime;
        private float cameraShakeStrength;
        private Vector3 lastCameraShakeOffset;
        private float weatherFeedbackCooldown;
        private float steamCooldown;
        private float emberCooldown;
        private int nextLightningSlot;
        private int nextCollapseSlot;
        private int nextBurstSlot;
        private bool transientPoolsReady;
        private MaterialPropertyBlock propertyBlock;
        private MrTabletopExperience tabletopExperience;
        private Vector3 lastGameplayCursorPosition;
        private bool hasGameplayCursorPosition;

        public void Initialize(Transform parent, Camera camera)
        {
            if (effectRoot != null)
            {
                PrepareBakedScene(parent, camera);
                return;
            }
            worldCamera = camera;
            effectRoot = new GameObject("WeatherVfx").transform;
            effectRoot.SetParent(parent, false);
            surfaceMesh = CreateSurfaceMesh();
            coneMesh = CreateConeMesh(9);
            ringMesh = CreateRingMesh(18);
            waterMaterial = CreateMaterial("WaterSurface", new Color(0.1f, 0.47f, 0.84f, 0.62f), true);
            snowMaterial = CreateMaterial("SnowSurface", new Color(0.88f, 0.96f, 1f, 0.96f), false);
            scorchMaterial = CreateMaterial("ScorchSurface", new Color(0.13f, 0.09f, 0.07f, 0.82f), true);
            fireMaterial = CreateMaterial("Fire", new Color(1f, 0.28f, 0.035f, 0.92f), true);
            smokeMaterial = CreateMaterial("Smoke", new Color(0.14f, 0.16f, 0.18f, 0.48f), true);
            tornadoMaterial = CreateMaterial("Tornado", new Color(0.62f, 0.82f, 0.83f, 0.55f), true);
            fireTornadoMaterial = CreateMaterial("FireTornado", new Color(1f, 0.3f, 0.055f, 0.75f), true);
            rippleMaterial = CreateMaterial("FloodRipples", new Color(0.42f, 0.82f, 1f, 0.42f), true);
            debrisMaterial = CreateMaterial("TornadoDebris", new Color(0.54f, 0.42f, 0.25f, 0.84f), true);
            cursorMaterial = CreateMaterial("CastCursor", new Color(0.25f, 0.78f, 0.82f, 0.2f), true);
            cursor = CreateCursor();
            weatherParticles = CreateWeatherParticleSystem("WeatherPrimary");
            weatherAccentParticles = CreateWeatherParticleSystem("WeatherAccent");
            propertyBlock = new MaterialPropertyBlock();
            EnsureBakedPrototypes();
            EnsureFeedbackObjects();
            EnsureTransientPools();
            ResetAll();
        }

        public void PrepareBakedScene(Transform parent, Camera camera)
        {
            worldCamera = camera;
            if (effectRoot == null)
            {
                effectRoot = transform.Find("WeatherVfx");
            }
            if (effectRoot == null)
            {
                Initialize(parent != null ? parent : transform, camera);
                return;
            }

            cursor ??= FindChildObject("CastCursor");
            if (cursor != null) cursorRenderer ??= cursor.GetComponent<MeshRenderer>();
            weatherParticles ??= FindChildComponent<ParticleSystem>("WeatherPrimary");
            weatherAccentParticles ??= FindChildComponent<ParticleSystem>("WeatherAccent");
            propertyBlock ??= new MaterialPropertyBlock();
            ResolveGameplayRoot();
            EnsureBakedPrototypes();
            RebindPrototypeAssets();
            EnsureFeedbackObjects();
            EnsureTransientPools();
            RebindFeedbackAssets();
            PreparePresentation();
            if (Application.isPlaying) ResetAll();
        }

        public void PreparePresentation(WeatherPresentationSettings settings = null)
        {
            ResolveGameplayRoot();
            if (cursor != null)
            {
                var outline = cursor.GetComponent<MeshFilter>();
                if (outline.sharedMesh == null || outline.sharedMesh.vertexCount != 128)
                    outline.sharedMesh = CreateRingMesh(64, .955f);
            }
            if (trajectoryMaterial != null)
            {
                trajectoryMaterial.shader = Shader.Find("WeatherManager/Weather Particle");
                trajectoryMaterial.SetColor("_BaseColor", Color.white);
            }
            if (presentation == null) presentation = effectRoot.GetComponentInChildren<WeatherParticlePresentation>(true);
            if (presentation == null)
            {
                var go = new GameObject("Weather Presentation");
                go.transform.SetParent(effectRoot, false);
                presentation = go.AddComponent<WeatherParticlePresentation>();
            }
            presentation.Configure(gameplayRoot, settings);
            if (surfacePresentation == null)
            {
                var go = new GameObject("Weather Surfaces"); go.transform.SetParent(gameplayRoot != null ? gameplayRoot : effectRoot, false);
                surfacePresentation = go.AddComponent<WeatherSurfacePresentation>();
            }
            if (presentation.Settings != null) surfacePresentation.Configure(presentation.Settings.surface);
            if (weatherParticles != null) { weatherParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); weatherParticles.gameObject.SetActive(false); }
            if (weatherAccentParticles != null) { weatherAccentParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); weatherAccentParticles.gameObject.SetActive(false); }
            clockedTransientParticles = transientPoolRoot.GetComponentsInChildren<ParticleSystem>(true);
        }

        public void RefreshBakedAssetReferences()
        {
            RebindPrototypeAssets();
            RebindFeedbackAssets();
        }

        private void OnDisable()
        {
            if (worldCamera != null
                && !MrRuntimeBridge.IsXrPresentationActive
                && lastCameraShakeOffset.sqrMagnitude > 0f)
            {
                worldCamera.transform.position -= lastCameraShakeOffset;
            }
            lastCameraShakeOffset = Vector3.zero;
            RecycleAllTransientEffects();
        }

        public void SetCursor(Vector3 position, bool valid, WeatherType type, float radius)
        {
            if (cursor == null)
            {
                return;
            }

            cursor.SetActive(!casting);
            SetGameplayPosition(cursor.transform, position + Vector3.up * 0.025f);
            lastGameplayCursorPosition = position;
            hasGameplayCursorPosition = true;
            cursorValid = valid;
            castCharge = 0f;
            cursorBaseScale = new Vector3(radius, 0.04f, radius);
            cursor.transform.localScale = cursorBaseScale;
            ApplyRendererColor(
                cursorRenderer,
                valid ? WithAlpha(WeatherPalette.For(type), 0.20f) : new Color(1f, 0.16f, 0.1f, 0.30f));
            if (castTrajectory != null) castTrajectory.enabled = false;
        }

        public void SetCasting(WeatherCastState? cast, bool immediate = false)
        {
            presentation?.SetCast(cast, immediate);
            casting = cast.HasValue && cast.Value.Active;
            targetWeatherFade = casting ? 1f : 0f;
            if (cursor != null)
            {
                cursor.SetActive(casting || cursor.activeSelf);
            }

            if (!casting)
            {
                castCharge = 0f;
                if (castTrajectory != null) castTrajectory.enabled = false;
                if (immediate)
                {
                    weatherFade = 0f;
                    if (weatherParticles != null)
                    {
                        weatherParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    if (weatherAccentParticles != null)
                    {
                        weatherAccentParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
                else
                {
                    if (weatherParticles != null)
                    {
                        weatherParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                    if (weatherAccentParticles != null)
                    {
                        weatherAccentParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
                return;
            }

            var activeCast = cast.Value;
            if (presentation == null && weatherParticles != null
                && (activeWeather != activeCast.Type || !weatherParticles.isPlaying))
            {
                activeWeather = activeCast.Type;
                ConfigureWeatherParticles(activeCast.Type);
                weatherParticles.Play(true);
                if (weatherAccentParticles != null)
                {
                    weatherAccentParticles.Play(true);
                }
            }

            if (weatherParticles != null) SetGameplayPosition(weatherParticles.transform, activeCast.Center);
            if (weatherAccentParticles != null) SetGameplayPosition(weatherAccentParticles.transform, activeCast.Center);
            cursor.SetActive(true);
            SetGameplayPosition(cursor.transform, activeCast.Center + Vector3.up * 0.03f);
            lastGameplayCursorPosition = activeCast.Center;
            hasGameplayCursorPosition = true;
            cursorValid = true;
            castCharge = Mathf.Clamp01(activeCast.HoldSeconds / 3f);
            cursorBaseScale = new Vector3(activeCast.Radius, 0.04f, activeCast.Radius);
            cursor.transform.localScale = cursorBaseScale;
            ApplyRendererColor(
                cursorRenderer,
                WithAlpha(WeatherPalette.For(activeCast.Type), Mathf.Lerp(0.24f, 0.34f, castCharge)));
            UpdateCastTrajectory(activeCast);
        }

        public void SyncDisasterVisuals(
            IReadOnlyList<SurfaceVisualCell> cells,
            IReadOnlyList<TornadoSnapshot> tornadoes,
            bool isBlackout)
        {
            presentation?.Sync(cells, tornadoes);
            if (surfacePresentation != null && surfaceTimer <= 0)
            {
                surfacePresentation.Sync(cells, .1f); surfaceTimer = .1f;
            }
            if (isBlackout && !blackout)
            {
                skyFlash = Mathf.Max(skyFlash, 0.36f);
            }
            blackout = isBlackout;
            waterCount = 0;
            snowCount = 0;
            scorchCount = 0;
            fireCount = 0;
            rippleCount = 0;
            debrisCount = 0;
            var steamPosition = Vector3.zero;
            var emberPosition = Vector3.zero;
            var hasSteam = false;
            var hasEmbers = false;

            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                if (cell.Water > 0.035f && waterCount < waterMatrices.Length)
                {
                    var scale = new Vector3(0.92f, 1f, 0.92f) * (0.86f + cell.Water * 0.18f);
                    waterMatrices[waterCount++] = GameplayToWorldMatrix(Matrix4x4.TRS(
                        cell.Position + Vector3.up * (0.035f + cell.Water * 0.045f),
                        Quaternion.identity,
                        scale));

                    if ((index & 1) == 0 && rippleCount < rippleMatrices.Length)
                    {
                        var phase = Mathf.Repeat(visualTime * 0.7f + index * 0.173f, 1f);
                        var rippleScale = Mathf.Lerp(0.28f, 0.92f, phase) * (0.72f + cell.Water * 0.38f);
                        rippleMatrices[rippleCount++] = GameplayToWorldMatrix(Matrix4x4.TRS(
                            cell.Position + Vector3.up * (0.085f + cell.Water * 0.045f),
                            Quaternion.Euler(0f, index * 23f, 0f),
                            new Vector3(rippleScale, 0.018f, rippleScale)));
                    }
                }
                if (cell.Snow > 0.035f && snowCount < snowMatrices.Length)
                {
                    var scale = new Vector3(0.9f, 1f, 0.9f) * (0.82f + cell.Snow * 0.2f);
                    snowMatrices[snowCount++] = GameplayToWorldMatrix(Matrix4x4.TRS(
                        cell.Position + Vector3.up * (0.05f + cell.Snow * 0.08f),
                        Quaternion.identity,
                        scale));
                }
                if (cell.Scorch > 0.045f && scorchCount < scorchMatrices.Length)
                {
                    scorchMatrices[scorchCount++] = GameplayToWorldMatrix(Matrix4x4.TRS(
                        cell.Position + Vector3.up * 0.018f,
                        Quaternion.Euler(0f, index * 19f, 0f),
                        new Vector3(0.76f + cell.Scorch * 0.28f, 1f, 0.76f + cell.Scorch * 0.28f)));
                }
                if (cell.Fire > 0.07f && fireCount < fireMatrices.Length)
                {
                    var pulse = 0.78f + Mathf.Sin(visualTime * 8f + index) * 0.12f;
                    fireMatrices[fireCount] = GameplayToWorldMatrix(Matrix4x4.TRS(
                        cell.Position + Vector3.up * (0.38f + cell.Fire * 0.25f),
                        Quaternion.Euler(0f, visualTime * 110f + index * 31f, 0f),
                        new Vector3(0.25f, 0.65f + cell.Fire * 0.45f, 0.25f) * pulse));
                    smokeMatrices[fireCount] = GameplayToWorldMatrix(Matrix4x4.TRS(
                        cell.Position + Vector3.up * (1.05f + cell.Fire * 0.55f),
                        Quaternion.Euler(0f, -visualTime * 30f + index * 17f, 0f),
                        Vector3.one * (0.28f + cell.Fire * 0.25f)));
                    fireCount++;
                    if (!hasEmbers && cell.Fire > 0.34f)
                    {
                        hasEmbers = true;
                        emberPosition = cell.Position;
                    }
                }

                if (!hasSteam && cell.Water > 0.12f && cell.Fire > 0.025f)
                {
                    hasSteam = true;
                    steamPosition = cell.Position;
                }
            }

            tornadoCount = 0;
            fireTornadoCount = 0;
            for (var index = 0; index < tornadoes.Count; index++)
            {
                var tornado = tornadoes[index];
                for (var ring = 0; ring < 7; ring++)
                {
                    var height = 0.28f + ring * 0.43f;
                    var width = 0.42f + ring * 0.18f + tornado.Strength * 0.12f;
                    var matrix = GameplayToWorldMatrix(Matrix4x4.TRS(
                        tornado.Position + Vector3.up * height,
                        Quaternion.Euler(0f, visualTime * (170f + ring * 12f) + ring * 27f, 0f),
                        new Vector3(width, 0.7f, width)));
                    if (tornado.IsFireTornado)
                    {
                        if (fireTornadoCount < fireTornadoMatrices.Length)
                        {
                            fireTornadoMatrices[fireTornadoCount++] = matrix;
                        }
                    }
                    else if (tornadoCount < tornadoMatrices.Length)
                    {
                        tornadoMatrices[tornadoCount++] = matrix;
                    }

                    if (debrisCount < debrisMatrices.Length && (ring & 1) == 0)
                    {
                        var orbit = visualTime * (4.7f + ring * 0.24f) + index * 1.37f + ring;
                        var debrisRadius = width * 0.9f;
                        var debrisPosition = tornado.Position + new Vector3(
                            Mathf.Cos(orbit) * debrisRadius,
                            height + 0.14f,
                            Mathf.Sin(orbit) * debrisRadius);
                        debrisMatrices[debrisCount++] = GameplayToWorldMatrix(Matrix4x4.TRS(
                            debrisPosition,
                            Quaternion.Euler(orbit * Mathf.Rad2Deg, visualTime * 210f, ring * 17f),
                            Vector3.one * (0.07f + tornado.Strength * 0.035f)));
                    }
                }
            }

            if (hasSteam && steamCooldown <= 0f)
            {
                steamCooldown = 0.34f;
                PlayEnvironmentalBurst(EnvironmentalBurstKind.Steam, steamPosition + Vector3.up * 0.12f, 15);
            }
            if (hasEmbers && emberCooldown <= 0f)
            {
                emberCooldown = 0.42f;
                PlayEnvironmentalBurst(EnvironmentalBurstKind.FireEmber, emberPosition + Vector3.up * 0.42f, 12);
            }
        }

        public void PlayLightning(Vector3 position)
        {
            PlayLightning(position, BiomeType.Town, false);
        }

        public void PlayLightning(Vector3 position, BiomeType biome, bool ignited)
        {
            EnsureTransientPools();
            var slot = AcquireLightningSlot();
            ActivateLightning(slot, position);
            switch (biome)
            {
                case BiomeType.Highland:
                    PlayEnvironmentalBurst(EnvironmentalBurstKind.RockDebris, position + Vector3.up * 0.16f, 28);
                    cameraShakeTime = Mathf.Max(cameraShakeTime, 0.38f);
                    cameraShakeStrength = Mathf.Max(cameraShakeStrength, 0.22f);
                    break;
                case BiomeType.Forest:
                    PlayEnvironmentalBurst(
                        ignited ? EnvironmentalBurstKind.FireEmber : EnvironmentalBurstKind.WindDebris,
                        position + Vector3.up * 0.22f,
                        ignited ? 22 : 12);
                    break;
                case BiomeType.Farmland:
                    PlayEnvironmentalBurst(
                        ignited ? EnvironmentalBurstKind.HeatEmber : EnvironmentalBurstKind.MudSplash,
                        position + Vector3.up * 0.12f,
                        18);
                    break;
                case BiomeType.Lowland:
                case BiomeType.Water:
                    PlayEnvironmentalBurst(EnvironmentalBurstKind.RainSplash, position + Vector3.up * 0.06f, 24);
                    break;
                default:
                    PlayEnvironmentalBurst(EnvironmentalBurstKind.ElectricSpark, position + Vector3.up * 0.18f, 16);
                    break;
            }
        }

        public void PlayCollapse(Vector3 position, float radius, bool snowCollapse)
        {
            PlayCollapse(position, radius, snowCollapse, BiomeType.Town);
        }

        public void PlayCollapse(Vector3 position, float radius, bool snowCollapse, BiomeType biome)
        {
            EnsureTransientPools();
            var slot = AcquireCollapseSlot();
            ActivateCollapse(slot, position, radius, snowCollapse);
            if (biome == BiomeType.Highland)
            {
                PlayEnvironmentalBurst(EnvironmentalBurstKind.RockDebris, position + Vector3.up * 0.18f, 28);
            }
            else if (biome == BiomeType.Forest)
            {
                PlayEnvironmentalBurst(EnvironmentalBurstKind.WindDebris, position + Vector3.up * 0.2f, 18);
            }
        }

        public void PlaySteam(Vector3 position)
        {
            PlaySteam(position, BiomeType.Town);
        }

        public void PlaySteam(Vector3 position, BiomeType biome)
        {
            EnsureTransientPools();
            var count = biome == BiomeType.Forest || biome == BiomeType.Farmland ? 30 : 24;
            PlayEnvironmentalBurst(EnvironmentalBurstKind.Steam, position + Vector3.up * 0.12f, count);
            PlayEnvironmentalBurst(EnvironmentalBurstKind.Recovery, position + Vector3.up * 0.08f, 10);
        }

        public void PlayFlood(Vector3 position, BiomeType biome, float intensity)
        {
            EnsureTransientPools();
            var count = Mathf.RoundToInt(Mathf.Lerp(18f, 38f, Mathf.Clamp01(intensity)));
            var kind = biome == BiomeType.Farmland ? EnvironmentalBurstKind.MudSplash : EnvironmentalBurstKind.RainSplash;
            PlayEnvironmentalBurst(kind, position + Vector3.up * 0.06f, count);
            if (biome == BiomeType.Highland)
            {
                PlayEnvironmentalBurst(EnvironmentalBurstKind.RockDebris, position + Vector3.up * 0.15f, 18);
            }
            cameraShakeTime = Mathf.Max(cameraShakeTime, biome == BiomeType.Lowland ? 0.2f : 0.12f);
            cameraShakeStrength = Mathf.Max(cameraShakeStrength, biome == BiomeType.Lowland ? 0.09f : 0.045f);
        }

        public void PlayFire(Vector3 position, BiomeType biome, float intensity)
        {
            EnsureTransientPools();
            var count = Mathf.RoundToInt(Mathf.Lerp(14f, 30f, Mathf.Clamp01(intensity)));
            PlayEnvironmentalBurst(EnvironmentalBurstKind.FireEmber, position + Vector3.up * 0.42f, count);
            if (biome == BiomeType.Forest)
            {
                PlayEnvironmentalBurst(EnvironmentalBurstKind.WindDebris, position + Vector3.up * 0.2f, 14);
            }
            else if (biome == BiomeType.Farmland)
            {
                PlayEnvironmentalBurst(EnvironmentalBurstKind.HeatEmber, position + Vector3.up * 0.18f, 16);
            }
            else if (biome == BiomeType.Lowland)
            {
                PlayEnvironmentalBurst(EnvironmentalBurstKind.Steam, position + Vector3.up * 0.12f, 10);
            }
        }

        public void PlaySnowLoad(Vector3 position, BiomeType biome, float intensity)
        {
            EnsureTransientPools();
            var count = Mathf.RoundToInt(Mathf.Lerp(18f, 36f, Mathf.Clamp01(intensity)));
            PlayEnvironmentalBurst(EnvironmentalBurstKind.SnowPowder, position + Vector3.up * 0.08f, count);
            if (biome == BiomeType.Highland)
            {
                PlayEnvironmentalBurst(EnvironmentalBurstKind.RockDebris, position + Vector3.up * 0.12f, 12);
            }
        }

        public void PlayRecovery(Vector3 position, BiomeType biome, WeatherType resolvedHazard)
        {
            EnsureTransientPools();
            PlayEnvironmentalBurst(EnvironmentalBurstKind.Recovery, position + Vector3.up * 0.12f, 22);
            if (resolvedHazard == WeatherType.Rain)
            {
                PlayEnvironmentalBurst(
                    biome == BiomeType.Farmland ? EnvironmentalBurstKind.MudSplash : EnvironmentalBurstKind.RainSplash,
                    position + Vector3.up * 0.04f,
                    8);
            }
            else if (resolvedHazard == WeatherType.Snow)
            {
                PlayEnvironmentalBurst(EnvironmentalBurstKind.SnowPowder, position + Vector3.up * 0.06f, 10);
            }
        }

        public void Tick(float unscaledDelta, float simulationDelta)
        {
            visualTime += simulationDelta;
            surfaceTimer -= simulationDelta;
            presentation?.Tick(simulationDelta);
            if (clockedTransientParticles != null && simulationDelta > 0f)
                foreach (var particles in clockedTransientParticles)
                    if (particles != null && particles.gameObject.activeInHierarchy && particles.particleCount > 0)
                        particles.Simulate(simulationDelta, false, false, false);
            steamCooldown = Mathf.Max(0f, steamCooldown - simulationDelta);
            emberCooldown = Mathf.Max(0f, emberCooldown - simulationDelta);
            weatherFeedbackCooldown = Mathf.Max(0f, weatherFeedbackCooldown - simulationDelta);
            weatherFade = Mathf.MoveTowards(weatherFade, targetWeatherFade, unscaledDelta * 4f);
            if (cursor != null && cursor.activeSelf)
            {
                var frequency = cursorValid ? 5.4f : 9f;
                var amplitude = casting ? Mathf.Lerp(0.025f, 0.075f, castCharge) : cursorValid ? 0.018f : 0.055f;
                var pulse = 1f + Mathf.Sin(visualTime * frequency) * amplitude;
                cursor.transform.localScale = new Vector3(
                    cursorBaseScale.x * pulse,
                    cursorBaseScale.y,
                    cursorBaseScale.z * pulse);
            }
            if (weatherParticles != null)
            {
                var emission = weatherParticles.emission;
                emission.rateOverTimeMultiplier = Mathf.Lerp(0f, 1f, weatherFade);
                var accentEmission = weatherAccentParticles.emission;
                accentEmission.rateOverTimeMultiplier = Mathf.Lerp(0f, 1f, weatherFade);
            }

            if (presentation == null && casting && weatherFeedbackCooldown <= 0f)
            {
                EmitCastingFeedback();
            }

            UpdateTransientEffects(simulationDelta);
            UpdateGlobalFeedback(simulationDelta);
        }

        public void ResetAll()
        {
            presentation?.ResetPresentation();
            surfacePresentation?.ResetSurfaces();
            surfaceTimer = 0;
            StopAllCoroutines();
            casting = false;
            targetWeatherFade = 0f;
            weatherFade = 0f;
            waterCount = 0;
            snowCount = 0;
            scorchCount = 0;
            fireCount = 0;
            tornadoCount = 0;
            fireTornadoCount = 0;
            rippleCount = 0;
            debrisCount = 0;
            blackout = false;
            skyFlash = 0f;
            cameraShakeTime = 0f;
            cameraShakeStrength = 0f;
            if (worldCamera != null
                && !MrRuntimeBridge.IsXrPresentationActive
                && lastCameraShakeOffset.sqrMagnitude > 0f)
            {
                worldCamera.transform.position -= lastCameraShakeOffset;
            }
            lastCameraShakeOffset = Vector3.zero;
            weatherFeedbackCooldown = 0f;
            steamCooldown = 0f;
            emberCooldown = 0f;
            castCharge = 0f;
            hasGameplayCursorPosition = false;
            if (cursor != null) cursor.SetActive(false);
            if (castTrajectory != null) castTrajectory.enabled = false;
            if (weatherParticles != null)
            {
                weatherParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (weatherAccentParticles != null)
            {
                weatherAccentParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (skyFlashLight != null) skyFlashLight.intensity = 0f;
            if (blackoutBeacon != null) blackoutBeacon.intensity = 0f;
            RecycleAllTransientEffects();
        }

        private void LateUpdate()
        {
            if (surfacePresentation == null)
            {
                DrawInstanced(surfaceMesh, waterMaterial, waterMatrices, waterCount);
                DrawInstanced(surfaceMesh, snowMaterial, snowMatrices, snowCount);
            }
            DrawInstanced(surfaceMesh, scorchMaterial, scorchMatrices, scorchCount);
            DrawInstanced(ringMesh, rippleMaterial, rippleMatrices, rippleCount);
            if (presentation == null)
            {
                DrawInstanced(coneMesh, fireMaterial, fireMatrices, fireCount);
                DrawInstanced(coneMesh, smokeMaterial, smokeMatrices, fireCount);
                DrawInstanced(ringMesh, tornadoMaterial, tornadoMatrices, tornadoCount);
                DrawInstanced(ringMesh, fireTornadoMaterial, fireTornadoMatrices, fireTornadoCount);
            }
            DrawInstanced(coneMesh, debrisMaterial, debrisMatrices, debrisCount);
        }

        private GameObject CreateCursor()
        {
            var objectRoot = new GameObject("CastCursor");
            objectRoot.transform.SetParent(effectRoot, false);
            var filter = objectRoot.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateRingMesh(64, .955f);
            var renderer = objectRoot.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = cursorMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            cursorRenderer = renderer;
            return objectRoot;
        }

        private void UpdateCastTrajectory(WeatherCastState activeCast)
        {
            if (castTrajectory == null)
            {
                return;
            }

            var direction = activeCast.Direction;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
            else direction.Normalize();
            var start = activeCast.Center + Vector3.up * 0.12f;
            var length = Mathf.Lerp(1.1f, 3.4f, castCharge);
            var color = WithAlpha(WeatherPalette.For(activeCast.Type), Mathf.Lerp(0.35f, 0.9f, castCharge));
            castTrajectory.enabled = true;
            castTrajectory.widthMultiplier = Mathf.Lerp(0.045f, 0.12f, castCharge) * Mathf.Abs(effectRoot.lossyScale.x);
            castTrajectory.startColor = color;
            castTrajectory.endColor = WithAlpha(color, 0.06f);
            castTrajectory.SetPosition(0, GameplayToLocalPosition(castTrajectory.transform, start));
            castTrajectory.SetPosition(1, GameplayToLocalPosition(castTrajectory.transform, start - direction * length));
        }

        private void EnsureFeedbackObjects()
        {
            if (effectRoot == null)
            {
                return;
            }

            castTrajectory ??= FindChildComponent<LineRenderer>("CastTrajectory");
            if (castTrajectory != null && castTrajectory.sharedMaterial != null)
            {
                trajectoryMaterial = castTrajectory.sharedMaterial;
            }
            trajectoryMaterial ??= CreateMaterial("CastTrajectory", new Color(0.35f, 0.86f, 0.92f, 0.72f), true);
            if (castTrajectory == null)
            {
                var trajectoryObject = new GameObject("CastTrajectory");
                trajectoryObject.transform.SetParent(effectRoot, false);
                castTrajectory = trajectoryObject.AddComponent<LineRenderer>();
            }
            // The trajectory belongs to the tabletop just like the cursor. A
            // local line prevents it from being left in the old global world
            // position when the physical table anchor changes.
            castTrajectory.useWorldSpace = false;
            castTrajectory.positionCount = 2;
            castTrajectory.numCapVertices = 2;
            castTrajectory.numCornerVertices = 2;
            castTrajectory.shadowCastingMode = ShadowCastingMode.Off;
            castTrajectory.receiveShadows = false;
            castTrajectory.sharedMaterial = trajectoryMaterial;
            castTrajectory.enabled = false;

            transientPoolRoot ??= effectRoot.Find("TransientPools");
            if (transientPoolRoot == null)
            {
                transientPoolRoot = new GameObject("TransientPools").transform;
                transientPoolRoot.SetParent(effectRoot, false);
            }

            skyFlashLight ??= FindChildComponent<Light>("SkyFlash");
            if (skyFlashLight == null)
            {
                var flashObject = new GameObject("SkyFlash");
                flashObject.transform.SetParent(transientPoolRoot, false);
                flashObject.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
                skyFlashLight = flashObject.AddComponent<Light>();
            }
            skyFlashLight.type = LightType.Point;
            skyFlashLight.color = new Color(0.72f, 0.84f, 1f);
            skyFlashLight.shadows = LightShadows.None;
            skyFlashLight.intensity = 0f;

            blackoutBeacon ??= FindChildComponent<Light>("BlackoutHazardBeacon");
            if (blackoutBeacon == null)
            {
                var beaconObject = new GameObject("BlackoutHazardBeacon");
                beaconObject.transform.SetParent(transientPoolRoot, false);
                beaconObject.transform.localPosition = new Vector3(0f, 11f, 0f);
                blackoutBeacon = beaconObject.AddComponent<Light>();
            }
            blackoutBeacon.type = LightType.Point;
            blackoutBeacon.range = 34f;
            blackoutBeacon.color = new Color(1f, 0.18f, 0.06f);
            blackoutBeacon.shadows = LightShadows.None;
            blackoutBeacon.intensity = 0f;
            blackoutBeacon.enabled = false;
        }

        private void EnsureTransientPools()
        {
            if (effectRoot == null)
            {
                return;
            }
            if (transientPoolsReady
                && lightningPool != null
                && collapsePool != null
                && burstPool != null)
            {
                return;
            }

            EnsureFeedbackObjects();
            lightningLineMaterial ??= CreateMaterial("LightningLine", new Color(1f, 0.95f, 0.62f, 1f), true);
            lightningRingMaterial ??= CreateMaterial("LightningImpactRing", new Color(1f, 0.82f, 0.18f, 0.78f), true);
            collapseRingMaterial ??= CreateMaterial("CollapseImpactRing", new Color(0.62f, 0.42f, 0.24f, 0.62f), true);
            snowCollapseRingMaterial ??= CreateMaterial("SnowCollapseRing", new Color(0.82f, 0.94f, 1f, 0.78f), true);
            burstParticleMaterial ??= CreateMaterial("PooledBurstParticle", Color.white, true, true);

            var lightningRoot = EnsureChild(transientPoolRoot, "LightningPool");
            if (lightningPool == null || lightningPool.Length != LightningPoolSize)
            {
                lightningPool = new LightningSlot[LightningPoolSize];
            }
            for (var index = 0; index < lightningPool.Length; index++)
            {
                lightningPool[index] = EnsureLightningSlot(lightningRoot, lightningPool[index], index);
            }

            var collapseRoot = EnsureChild(transientPoolRoot, "CollapsePool");
            if (collapsePool == null || collapsePool.Length != CollapsePoolSize)
            {
                collapsePool = new CollapseSlot[CollapsePoolSize];
            }
            for (var index = 0; index < collapsePool.Length; index++)
            {
                collapsePool[index] = EnsureCollapseSlot(collapseRoot, collapsePool[index], index);
            }

            var environmentalRoot = EnsureChild(transientPoolRoot, "EnvironmentalBurstPool");
            if (burstPool == null || burstPool.Length != BurstPoolSize)
            {
                burstPool = new BurstSlot[BurstPoolSize];
            }
            for (var index = 0; index < burstPool.Length; index++)
            {
                burstPool[index] = EnsureBurstSlot(environmentalRoot, burstPool[index], index);
            }
            transientPoolsReady = true;
        }

        private LightningSlot EnsureLightningSlot(Transform parent, LightningSlot slot, int index)
        {
            slot ??= new LightningSlot();
            var root = slot.Root != null
                ? slot.Root.transform
                : EnsureChild(parent, $"Lightning_{index:00}");
            slot.Root = root.gameObject;

            var boltTransform = EnsureChild(root, "Bolt");
            slot.Bolt = boltTransform.GetComponent<LineRenderer>();
            if (slot.Bolt == null) slot.Bolt = boltTransform.gameObject.AddComponent<LineRenderer>();
            slot.Bolt.useWorldSpace = false;
            slot.Bolt.positionCount = BoltPointCount;
            slot.Bolt.widthMultiplier = 0.11f * Mathf.Abs(effectRoot.lossyScale.x);
            slot.Bolt.numCornerVertices = 2;
            slot.Bolt.numCapVertices = 2;
            slot.Bolt.shadowCastingMode = ShadowCastingMode.Off;
            slot.Bolt.receiveShadows = false;
            slot.Bolt.sharedMaterial = lightningLineMaterial;
            if (slot.Branches == null || slot.Branches.Length != 2) slot.Branches = new LineRenderer[2];
            for (var i = 0; i < 2; i++)
            {
                var branchRoot = EnsureChild(root, "Branch_" + i);
                var line = branchRoot.GetComponent<LineRenderer>();
                if (line == null) line = branchRoot.gameObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false; line.positionCount = 4; line.widthMultiplier = .055f * Mathf.Abs(effectRoot.lossyScale.x);
                line.sharedMaterial = lightningLineMaterial; line.numCapVertices = 2;
                line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                slot.Branches[i] = line;
            }

            var flashTransform = EnsureChild(root, "PointFlash");
            flashTransform.localPosition = Vector3.up * 1.2f;
            slot.Flash = flashTransform.GetComponent<Light>();
            if (slot.Flash == null) slot.Flash = flashTransform.gameObject.AddComponent<Light>();
            slot.Flash.type = LightType.Point;
            slot.Flash.range = 13f;
            slot.Flash.color = new Color(0.78f, 0.88f, 1f);
            slot.Flash.shadows = LightShadows.None;
            slot.Flash.intensity = 0f;
            slot.Flash.enabled = false;

            var impactTransform = EnsureChild(root, "ImpactSparks");
            impactTransform.localPosition = Vector3.up * 0.12f;
            slot.Impact = EnsurePooledParticleSystem(impactTransform);

            var ringTransform = EnsureChild(root, "ImpactRing");
            ringTransform.localPosition = Vector3.up * 0.035f;
            var ringFilter = ringTransform.GetComponent<MeshFilter>();
            if (ringFilter == null) ringFilter = ringTransform.gameObject.AddComponent<MeshFilter>();
            ringFilter.sharedMesh = ringMesh;
            slot.Ring = ringTransform.GetComponent<MeshRenderer>();
            if (slot.Ring == null) slot.Ring = ringTransform.gameObject.AddComponent<MeshRenderer>();
            slot.Ring.sharedMaterial = lightningRingMaterial;
            slot.Ring.shadowCastingMode = ShadowCastingMode.Off;
            slot.Ring.receiveShadows = false;
            slot.Ring.enabled = false;
            slot.Root.SetActive(false);
            return slot;
        }

        private CollapseSlot EnsureCollapseSlot(Transform parent, CollapseSlot slot, int index)
        {
            slot ??= new CollapseSlot();
            var root = slot.Root != null
                ? slot.Root.transform
                : EnsureChild(parent, $"Collapse_{index:00}");
            slot.Root = root.gameObject;

            var dustTransform = EnsureChild(root, "Dust");
            dustTransform.localPosition = Vector3.up * 0.22f;
            slot.Dust = EnsurePooledParticleSystem(dustTransform);

            var ringTransform = EnsureChild(root, "ShockRing");
            ringTransform.localPosition = Vector3.up * 0.04f;
            var ringFilter = ringTransform.GetComponent<MeshFilter>();
            if (ringFilter == null) ringFilter = ringTransform.gameObject.AddComponent<MeshFilter>();
            ringFilter.sharedMesh = ringMesh;
            slot.Ring = ringTransform.GetComponent<MeshRenderer>();
            if (slot.Ring == null) slot.Ring = ringTransform.gameObject.AddComponent<MeshRenderer>();
            slot.Ring.sharedMaterial = collapseRingMaterial;
            slot.Ring.shadowCastingMode = ShadowCastingMode.Off;
            slot.Ring.receiveShadows = false;
            slot.Ring.enabled = false;
            slot.Root.SetActive(false);
            return slot;
        }

        private BurstSlot EnsureBurstSlot(Transform parent, BurstSlot slot, int index)
        {
            slot ??= new BurstSlot();
            slot.Root ??= EnsureChild(parent, $"Burst_{index:00}");
            slot.Particles = EnsurePooledParticleSystem(slot.Root);
            slot.Root.gameObject.SetActive(true);
            return slot;
        }

        private ParticleSystem EnsurePooledParticleSystem(Transform parent)
        {
            var system = parent.GetComponent<ParticleSystem>();
            if (system == null) system = parent.gameObject.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 32;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = 0.7f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = burstParticleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private LightningSlot AcquireLightningSlot()
        {
            for (var offset = 0; offset < lightningPool.Length; offset++)
            {
                var index = (nextLightningSlot + offset) % lightningPool.Length;
                if (!lightningPool[index].Active)
                {
                    nextLightningSlot = (index + 1) % lightningPool.Length;
                    return lightningPool[index];
                }
            }
            var reused = lightningPool[nextLightningSlot];
            nextLightningSlot = (nextLightningSlot + 1) % lightningPool.Length;
            RecycleLightning(reused);
            return reused;
        }

        private CollapseSlot AcquireCollapseSlot()
        {
            for (var offset = 0; offset < collapsePool.Length; offset++)
            {
                var index = (nextCollapseSlot + offset) % collapsePool.Length;
                if (!collapsePool[index].Active)
                {
                    nextCollapseSlot = (index + 1) % collapsePool.Length;
                    return collapsePool[index];
                }
            }
            var reused = collapsePool[nextCollapseSlot];
            nextCollapseSlot = (nextCollapseSlot + 1) % collapsePool.Length;
            RecycleCollapse(reused);
            return reused;
        }

        private void ActivateLightning(LightningSlot slot, Vector3 position)
        {
            SetGameplayPosition(slot.Root.transform, position);
            slot.Root.SetActive(true);
            slot.Active = true;
            slot.Age = 0f;
            slot.Bolt.enabled = true;
            slot.Bolt.widthMultiplier = 0.11f * Mathf.Abs(effectRoot.lossyScale.x);
            for (var index = 0; index < BoltPointCount; index++)
            {
                var t = index / (BoltPointCount - 1f);
                var jitter = index == 0 || index == BoltPointCount - 1
                    ? Vector3.zero
                    : new Vector3(Random.Range(-0.45f, 0.45f), 0f, Random.Range(-0.45f, 0.45f));
                slot.Bolt.SetPosition(index, Vector3.up * Mathf.Lerp(10f, 0.15f, t) + jitter);
            }
            for (var b = 0; b < slot.Branches.Length; b++)
            {
                var line = slot.Branches[b]; line.enabled = true;
                line.widthMultiplier = .055f * Mathf.Abs(effectRoot.lossyScale.x);
                var start = slot.Bolt.GetPosition(b + 2);
                var direction = new Vector3(b == 0 ? -1.6f : 1.8f, -1.8f, b == 0 ? .7f : -.8f);
                for (var j = 0; j < 4; j++) line.SetPosition(j, start + direction * (j / 3f) + (j == 0 ? Vector3.zero : Random.insideUnitSphere * .13f));
            }
            slot.Flash.intensity = 0f;
            slot.Ring.enabled = true;
            slot.Ring.transform.localScale = Vector3.one * 0.65f;
            ApplyRendererColor(slot.Ring, new Color(1f, 0.86f, 0.24f, 0.78f));
            EmitParticles(slot.Impact, EnvironmentalBurstKind.ElectricSpark, 42);
            skyFlash = 1f;
            if (skyFlashLight != null)
            {
                SetGameplayPosition(skyFlashLight.transform, position + Vector3.up * 2.5f);
                skyFlashLight.range = 7f * (gameplayRoot != null ? gameplayRoot.lossyScale.x : 1f);
            }
            cameraShakeTime = Mathf.Max(cameraShakeTime, 0.32f);
            cameraShakeStrength = Mathf.Max(cameraShakeStrength, 0.16f);
        }

        private void ActivateCollapse(CollapseSlot slot, Vector3 position, float radius, bool snowCollapse)
        {
            SetGameplayPosition(slot.Root.transform, position);
            slot.Root.SetActive(true);
            slot.Active = true;
            slot.Age = 0f;
            slot.BaseScale = Mathf.Max(0.55f, radius * 0.45f);
            slot.Ring.enabled = true;
            slot.Ring.sharedMaterial = snowCollapse ? snowCollapseRingMaterial : collapseRingMaterial;
            slot.Ring.transform.localScale = Vector3.one * slot.BaseScale;
            ApplyRendererColor(
                slot.Ring,
                snowCollapse
                    ? new Color(0.84f, 0.95f, 1f, 0.78f)
                    : new Color(0.62f, 0.43f, 0.25f, 0.68f));
            EmitParticles(
                slot.Dust,
                snowCollapse ? EnvironmentalBurstKind.SnowPowder : EnvironmentalBurstKind.WindDebris,
                36);
            if (snowCollapse)
            {
                PlayEnvironmentalBurst(EnvironmentalBurstKind.SnowPowder, position + Vector3.up * 0.16f, 20);
            }
            cameraShakeTime = Mathf.Max(cameraShakeTime, 0.24f);
            cameraShakeStrength = Mathf.Max(cameraShakeStrength, Mathf.Clamp(radius * 0.035f, 0.05f, 0.13f));
        }

        private void UpdateTransientEffects(float delta)
        {
            if (lightningPool != null)
            {
                for (var index = 0; index < lightningPool.Length; index++)
                {
                    var slot = lightningPool[index];
                    if (slot == null || !slot.Active) continue;
                    slot.Age += delta;
                    slot.Bolt.enabled = slot.Age < 0.13f;
                    if (slot.Branches != null) foreach (var branch in slot.Branches) if (branch != null) branch.enabled = slot.Bolt.enabled;
                    slot.Bolt.widthMultiplier = Mathf.Lerp(0.11f, 0.025f, Mathf.Clamp01(slot.Age / 0.13f)) * Mathf.Abs(effectRoot.lossyScale.x);
                    slot.Flash.intensity = Mathf.Lerp(9f, 0f, Mathf.Clamp01(slot.Age / 0.24f));
                    var ringProgress = Mathf.Clamp01(slot.Age / 0.46f);
                    slot.Ring.enabled = ringProgress < 1f;
                    slot.Ring.transform.localScale = Vector3.one * Mathf.Lerp(0.65f, 4.8f, ringProgress);
                    if (slot.Age >= 0.95f && !slot.Impact.IsAlive(true)) RecycleLightning(slot);
                }
            }

            if (collapsePool != null)
            {
                for (var index = 0; index < collapsePool.Length; index++)
                {
                    var slot = collapsePool[index];
                    if (slot == null || !slot.Active) continue;
                    slot.Age += delta;
                    var ringProgress = Mathf.Clamp01(slot.Age / 0.68f);
                    slot.Ring.enabled = ringProgress < 1f;
                    slot.Ring.transform.localScale = Vector3.one * (slot.BaseScale * Mathf.Lerp(1f, 3.7f, ringProgress));
                    if (slot.Age >= 1.25f && !slot.Dust.IsAlive(true)) RecycleCollapse(slot);
                }
            }
        }

        private void UpdateGlobalFeedback(float delta)
        {
            skyFlash = Mathf.MoveTowards(skyFlash, 0f, delta * 4.8f);
            // URP point lights attenuate with distance squared; table scaling must scale their intensity too.
            var lightScale = gameplayRoot != null ? Mathf.Abs(gameplayRoot.lossyScale.x) : 1f;
            if (skyFlashLight != null) skyFlashLight.intensity = skyFlash * 2.4f * lightScale * lightScale;
            if (blackoutBeacon != null)
            {
                var target = blackout ? 0.18f + Mathf.Sin(visualTime * 3.2f) * 0.07f : 0f;
                blackoutBeacon.intensity = Mathf.MoveTowards(blackoutBeacon.intensity, target, delta * 0.75f);
            }

            var canMoveCamera = worldCamera != null && !MrRuntimeBridge.IsXrPresentationActive
                && (tabletopExperience == null || !tabletopExperience.IsMrActive);
            if (canMoveCamera && lastCameraShakeOffset.sqrMagnitude > 0f)
            {
                worldCamera.transform.position -= lastCameraShakeOffset;
                lastCameraShakeOffset = Vector3.zero;
            }
            else if (!canMoveCamera)
            {
                lastCameraShakeOffset = Vector3.zero;
            }
            if (canMoveCamera && cameraShakeTime > 0f)
            {
                cameraShakeTime = Mathf.Max(0f, cameraShakeTime - delta);
                var falloff = Mathf.Clamp01(cameraShakeTime / 0.32f);
                lastCameraShakeOffset = Random.insideUnitSphere * (cameraShakeStrength * falloff);
                worldCamera.transform.position += lastCameraShakeOffset;
                if (cameraShakeTime <= 0f) cameraShakeStrength = 0f;
            }
            else if (!canMoveCamera)
            {
                cameraShakeTime = 0f;
                cameraShakeStrength = 0f;
            }
        }

        private void EmitCastingFeedback()
        {
            var position = hasGameplayCursorPosition ? lastGameplayCursorPosition : Vector3.zero;
            switch (activeWeather)
            {
                case WeatherType.Wind:
                    weatherFeedbackCooldown = 0.17f;
                    PlayEnvironmentalBurst(EnvironmentalBurstKind.WindDebris, position + Vector3.up * 0.18f, 8);
                    break;
                case WeatherType.Rain:
                    weatherFeedbackCooldown = 0.14f;
                    PlayEnvironmentalBurst(EnvironmentalBurstKind.RainSplash, position + Vector3.up * 0.04f, 12);
                    break;
                case WeatherType.Electric:
                    weatherFeedbackCooldown = 0.24f;
                    PlayEnvironmentalBurst(EnvironmentalBurstKind.ElectricSpark, position + Vector3.up * 0.12f, 9);
                    break;
                case WeatherType.Snow:
                    weatherFeedbackCooldown = 0.28f;
                    PlayEnvironmentalBurst(EnvironmentalBurstKind.SnowPowder, position + Vector3.up * 0.06f, 10);
                    break;
                case WeatherType.Heat:
                    weatherFeedbackCooldown = 0.2f;
                    PlayEnvironmentalBurst(EnvironmentalBurstKind.HeatEmber, position + Vector3.up * 0.16f, 10);
                    break;
            }
        }

        private void PlayEnvironmentalBurst(EnvironmentalBurstKind kind, Vector3 position, int count)
        {
            if (burstPool == null || burstPool.Length == 0)
            {
                return;
            }
            BurstSlot slot = null;
            for (var i = 0; i < burstPool.Length; i++)
            {
                var index = (nextBurstSlot + i) % burstPool.Length;
                if (burstPool[index].Particles.particleCount > 0) continue;
                slot = burstPool[index]; nextBurstSlot = (index + 1) % burstPool.Length; break;
            }
            if (slot == null) return;
            SetGameplayPosition(slot.Root, position);
            slot.Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            EmitParticles(slot.Particles, kind, count);
        }

        private Gradient burstFade;
        private AnimationCurve burstFrames;
        private void EmitParticles(ParticleSystem system, EnvironmentalBurstKind kind, int count)
        {
            if (system == null || count <= 0)
            {
                return;
            }

            var config = presentation != null ? presentation.Settings : null;
            if (config != null)
            {
                var material = kind == EnvironmentalBurstKind.Steam ? config.steam
                    : kind == EnvironmentalBurstKind.SnowPowder ? config.weather[3].material
                    : kind == EnvironmentalBurstKind.RainSplash || kind == EnvironmentalBurstKind.MudSplash ? config.splash
                    : kind == EnvironmentalBurstKind.WindDebris || kind == EnvironmentalBurstKind.RockDebris ? config.cloud : config.spark;
                system.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
                var sheet = system.textureSheetAnimation; sheet.enabled = kind == EnvironmentalBurstKind.Steam;
                if (sheet.enabled) { burstFrames ??= AnimationCurve.Linear(0,0,1,1); sheet.numTilesX = 4; sheet.numTilesY = 4; sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, burstFrames); }
                var fade = system.colorOverLifetime; fade.enabled = true;
                if (burstFade == null) { burstFade = new Gradient(); burstFade.SetKeys(new[] { new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1) },
                    new[] {new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)}); } fade.color = burstFade;
            }
            var color = BurstColor(kind);
            for (var index = 0; index < count; index++)
            {
                var radial = Random.insideUnitCircle;
                var upward = Random.Range(0.3f, 1f);
                var velocity = kind switch
                {
                    EnvironmentalBurstKind.WindDebris => new Vector3(4.8f + Random.Range(0f, 2.6f), upward, radial.y * 2.2f),
                    EnvironmentalBurstKind.RainSplash => new Vector3(radial.x * 1.6f, 1.3f + upward * 1.7f, radial.y * 1.6f),
                    EnvironmentalBurstKind.ElectricSpark => Random.onUnitSphere * Random.Range(3.8f, 7f),
                    EnvironmentalBurstKind.SnowPowder => new Vector3(radial.x * 1.8f, 0.5f + upward * 1.4f, radial.y * 1.8f),
                    EnvironmentalBurstKind.HeatEmber => new Vector3(radial.x * 0.7f, 1.4f + upward * 2f, radial.y * 0.7f),
                    EnvironmentalBurstKind.Steam => new Vector3(radial.x * 0.9f, 1.2f + upward * 1.8f, radial.y * 0.9f),
                    EnvironmentalBurstKind.MudSplash => new Vector3(radial.x * 1.3f, 0.7f + upward * 1.35f, radial.y * 1.3f),
                    EnvironmentalBurstKind.RockDebris => new Vector3(radial.x * 3.2f, 1.4f + upward * 3.2f, radial.y * 3.2f),
                    EnvironmentalBurstKind.Recovery => new Vector3(radial.x * 0.8f, 0.9f + upward * 1.5f, radial.y * 0.8f),
                    _ => new Vector3(radial.x, 1.8f + upward * 2.4f, radial.y)
                };
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(radial.x, 0f, radial.y) * 0.22f,
                    velocity = velocity,
                    startColor = color,
                    startLifetime = BurstLifetime(kind) * Random.Range(0.78f, 1.18f),
                    startSize = BurstSize(kind) * Random.Range(0.72f, 1.2f) * (kind == EnvironmentalBurstKind.Steam ? 3.8f : 2.2f)
                };
                system.Emit(emit, 1);
            }
        }

        private static Color BurstColor(EnvironmentalBurstKind kind)
        {
            return kind switch
            {
                EnvironmentalBurstKind.WindDebris => new Color(0.56f, 0.75f, 0.62f, 0.78f),
                EnvironmentalBurstKind.RainSplash => new Color(0.42f, 0.76f, 1f, 0.82f),
                EnvironmentalBurstKind.ElectricSpark => new Color(1f, 0.9f, 0.25f, 1f),
                EnvironmentalBurstKind.SnowPowder => new Color(0.88f, 0.97f, 1f, 0.92f),
                EnvironmentalBurstKind.HeatEmber => new Color(1f, 0.37f, 0.08f, 0.9f),
                EnvironmentalBurstKind.Steam => new Color(0.82f, 0.88f, 0.9f, 0.62f),
                EnvironmentalBurstKind.MudSplash => new Color(0.42f, 0.3f, 0.16f, 0.86f),
                EnvironmentalBurstKind.RockDebris => new Color(0.48f, 0.43f, 0.38f, 0.9f),
                EnvironmentalBurstKind.Recovery => new Color(0.34f, 0.94f, 0.66f, 0.86f),
                _ => new Color(1f, 0.24f, 0.035f, 0.95f)
            };
        }

        private static float BurstLifetime(EnvironmentalBurstKind kind)
        {
            return kind switch
            {
                EnvironmentalBurstKind.WindDebris => 0.65f,
                EnvironmentalBurstKind.RainSplash => 0.42f,
                EnvironmentalBurstKind.ElectricSpark => 0.34f,
                EnvironmentalBurstKind.SnowPowder => 0.82f,
                EnvironmentalBurstKind.Steam => 1.1f,
                EnvironmentalBurstKind.MudSplash => 0.58f,
                EnvironmentalBurstKind.RockDebris => 0.82f,
                EnvironmentalBurstKind.Recovery => 0.9f,
                _ => 0.75f
            };
        }

        private static float BurstSize(EnvironmentalBurstKind kind)
        {
            return kind switch
            {
                EnvironmentalBurstKind.WindDebris => 0.075f,
                EnvironmentalBurstKind.RainSplash => 0.055f,
                EnvironmentalBurstKind.ElectricSpark => 0.065f,
                EnvironmentalBurstKind.SnowPowder => 0.12f,
                EnvironmentalBurstKind.Steam => 0.18f,
                EnvironmentalBurstKind.MudSplash => 0.095f,
                EnvironmentalBurstKind.RockDebris => 0.13f,
                EnvironmentalBurstKind.Recovery => 0.085f,
                _ => 0.07f
            };
        }

        private void RecycleAllTransientEffects()
        {
            if (lightningPool != null)
            {
                for (var index = 0; index < lightningPool.Length; index++) RecycleLightning(lightningPool[index]);
            }
            if (collapsePool != null)
            {
                for (var index = 0; index < collapsePool.Length; index++) RecycleCollapse(collapsePool[index]);
            }
            if (burstPool != null)
            {
                for (var index = 0; index < burstPool.Length; index++)
                {
                    var slot = burstPool[index];
                    if (slot != null && slot.Particles != null)
                    {
                        slot.Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
            }
            nextLightningSlot = 0;
            nextCollapseSlot = 0;
            nextBurstSlot = 0;
        }

        private static void RecycleLightning(LightningSlot slot)
        {
            if (slot == null) return;
            slot.Active = false;
            slot.Age = 0f;
            if (slot.Bolt != null) slot.Bolt.enabled = false;
            if (slot.Flash != null) slot.Flash.intensity = 0f;
            if (slot.Ring != null) slot.Ring.enabled = false;
            if (slot.Impact != null)
            {
                slot.Impact.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (slot.Root != null) slot.Root.SetActive(false);
        }

        private static void RecycleCollapse(CollapseSlot slot)
        {
            if (slot == null) return;
            slot.Active = false;
            slot.Age = 0f;
            slot.BaseScale = 0f;
            if (slot.Ring != null) slot.Ring.enabled = false;
            if (slot.Dust != null)
            {
                slot.Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (slot.Root != null) slot.Root.SetActive(false);
        }

        private void ApplyRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(propertyBlock);
        }

        private static Transform EnsureChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child != null) return child;
            child = new GameObject(childName).transform;
            child.SetParent(parent, false);
            return child;
        }

        private void EnsureBakedPrototypes()
        {
            if (effectRoot == null || surfaceMesh == null || coneMesh == null || ringMesh == null) return;
            rippleMaterial ??= CreateMaterial("FloodRipples", new Color(0.42f, 0.82f, 1f, 0.42f), true);
            debrisMaterial ??= CreateMaterial("TornadoDebris", new Color(0.54f, 0.42f, 0.25f, 0.84f), true);
            snowCollapseRingMaterial ??= CreateMaterial("SnowCollapseRing", new Color(0.82f, 0.94f, 1f, 0.78f), true);
            var prototypes = effectRoot.Find("SurfaceRenderers");
            if (prototypes == null)
            {
                prototypes = new GameObject("SurfaceRenderers").transform;
                prototypes.SetParent(effectRoot, false);
            }
            waterPrototype = EnsurePrototype(prototypes, "FloodSurfaceRenderer", surfaceMesh, waterMaterial, waterPrototype);
            snowPrototype = EnsurePrototype(prototypes, "SnowSurfaceRenderer", surfaceMesh, snowMaterial, snowPrototype);
            scorchPrototype = EnsurePrototype(prototypes, "ScorchSurfaceRenderer", surfaceMesh, scorchMaterial, scorchPrototype);
            firePrototype = EnsurePrototype(prototypes, "FireRenderer", coneMesh, fireMaterial, firePrototype);
            smokePrototype = EnsurePrototype(prototypes, "SmokeRenderer", coneMesh, smokeMaterial, smokePrototype);
            tornadoPrototype = EnsurePrototype(prototypes, "TornadoRenderer", ringMesh, tornadoMaterial, tornadoPrototype);
            fireTornadoPrototype = EnsurePrototype(prototypes, "FireTornadoRenderer", ringMesh, fireTornadoMaterial, fireTornadoPrototype);
            ripplePrototype = EnsurePrototype(prototypes, "FloodRippleRenderer", ringMesh, rippleMaterial, ripplePrototype);
            debrisPrototype = EnsurePrototype(prototypes, "TornadoDebrisRenderer", coneMesh, debrisMaterial, debrisPrototype);
            snowCollapsePrototype = EnsurePrototype(
                prototypes,
                "SnowCollapseRenderer",
                ringMesh,
                snowCollapseRingMaterial,
                snowCollapsePrototype);
        }

        private static MeshFilter EnsurePrototype(
            Transform parent,
            string objectName,
            Mesh mesh,
            Material material,
            MeshFilter existing)
        {
            var filter = existing;
            if (filter == null)
            {
                var child = parent.Find(objectName);
                if (child == null)
                {
                    child = new GameObject(objectName).transform;
                    child.SetParent(parent, false);
                }
                filter = child.GetComponent<MeshFilter>();
                if (!filter) filter = child.gameObject.AddComponent<MeshFilter>();
            }
            filter.sharedMesh = mesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer) renderer = filter.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return filter;
        }

        private void RebindPrototypeAssets()
        {
            if (waterPrototype != null)
            {
                surfaceMesh = waterPrototype.sharedMesh;
                waterMaterial = waterPrototype.GetComponent<Renderer>()?.sharedMaterial;
            }
            if (snowPrototype != null) snowMaterial = snowPrototype.GetComponent<Renderer>()?.sharedMaterial;
            if (scorchPrototype != null) scorchMaterial = scorchPrototype.GetComponent<Renderer>()?.sharedMaterial;
            if (firePrototype != null)
            {
                coneMesh = firePrototype.sharedMesh;
                fireMaterial = firePrototype.GetComponent<Renderer>()?.sharedMaterial;
            }
            if (smokePrototype != null) smokeMaterial = smokePrototype.GetComponent<Renderer>()?.sharedMaterial;
            if (tornadoPrototype != null)
            {
                ringMesh = tornadoPrototype.sharedMesh;
                tornadoMaterial = tornadoPrototype.GetComponent<Renderer>()?.sharedMaterial;
            }
            if (fireTornadoPrototype != null) fireTornadoMaterial = fireTornadoPrototype.GetComponent<Renderer>()?.sharedMaterial;
            if (ripplePrototype != null) rippleMaterial = ripplePrototype.GetComponent<Renderer>()?.sharedMaterial;
            if (debrisPrototype != null) debrisMaterial = debrisPrototype.GetComponent<Renderer>()?.sharedMaterial;
            if (snowCollapsePrototype != null) snowCollapseRingMaterial = snowCollapsePrototype.GetComponent<Renderer>()?.sharedMaterial;
            if (cursorRenderer == null && cursor != null) cursorRenderer = cursor.GetComponent<MeshRenderer>();
            if (cursorRenderer != null) cursorMaterial = cursorRenderer.sharedMaterial;
        }

        private void RebindFeedbackAssets()
        {
            if (castTrajectory != null) trajectoryMaterial = castTrajectory.sharedMaterial;
            if (lightningPool != null && lightningPool.Length > 0 && lightningPool[0] != null)
            {
                lightningLineMaterial = lightningPool[0].Bolt != null ? lightningPool[0].Bolt.sharedMaterial : lightningLineMaterial;
                lightningRingMaterial = lightningPool[0].Ring != null ? lightningPool[0].Ring.sharedMaterial : lightningRingMaterial;
            }
            if (collapsePool != null && collapsePool.Length > 0 && collapsePool[0] != null)
            {
                collapseRingMaterial = collapsePool[0].Ring != null ? collapsePool[0].Ring.sharedMaterial : collapseRingMaterial;
            }
            if (burstPool != null
                && burstPool.Length > 0
                && burstPool[0] != null
                && burstPool[0].Particles != null)
            {
                var renderer = burstPool[0].Particles.GetComponent<ParticleSystemRenderer>();
                if (renderer != null) burstParticleMaterial = renderer.sharedMaterial;
            }
        }

        private GameObject FindChildObject(string objectName)
        {
            var transforms = effectRoot.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == objectName) return transforms[index].gameObject;
            }
            return null;
        }

        private T FindChildComponent<T>(string objectName) where T : Component
        {
            var child = FindChildObject(objectName);
            return child != null ? child.GetComponent<T>() : null;
        }

        private ParticleSystem CreateWeatherParticleSystem(string objectName)
        {
            var particleObject = new GameObject(objectName);
            particleObject.transform.SetParent(effectRoot, false);
            var system = particleObject.AddComponent<ParticleSystem>();
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = CreateMaterial(objectName + "Material", Color.white, true, true);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private void ConfigureWeatherParticles(WeatherType type)
        {
            ConfigureParticleLayer(weatherParticles, type, false);
            ConfigureParticleLayer(weatherAccentParticles, type, true);
        }

        private static void ConfigureParticleLayer(ParticleSystem system, WeatherType type, bool accent)
        {
            if (system == null) return;
            system.Clear(true);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            // Weather particle emitters are descendants of the tabletop.  Use
            // local simulation so emitted particles follow both an MR table
            // placement and its real-world scale rather than remaining at the
            // original global Unity origin.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = accent ? 280 : 900;
            main.startLifetime = type switch
            {
                WeatherType.Rain => accent ? 0.35f : 0.58f,
                WeatherType.Snow => accent ? 2.8f : 3.8f,
                WeatherType.Wind => accent ? 0.7f : 1.2f,
                WeatherType.Heat => accent ? 0.65f : 1.1f,
                _ => accent ? 0.28f : 0.48f
            };
            main.startSpeed = type switch
            {
                WeatherType.Rain => 14f,
                WeatherType.Snow => 1.1f,
                WeatherType.Wind => 7f,
                WeatherType.Heat => 2.4f,
                _ => 5.5f
            };
            main.startSize = type switch
            {
                WeatherType.Rain => accent ? 0.045f : 0.028f,
                WeatherType.Snow => accent ? 0.12f : 0.075f,
                WeatherType.Wind => accent ? 0.09f : 0.045f,
                WeatherType.Heat => accent ? 0.11f : 0.06f,
                _ => accent ? 0.1f : 0.055f
            };
            main.startColor = WithAlpha(WeatherPalette.For(type), type == WeatherType.Snow ? 0.92f : 0.72f);
            main.gravityModifier = type == WeatherType.Snow ? 0.08f : type == WeatherType.Rain ? 0.5f : -0.02f;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = accent ? 38f : type == WeatherType.Rain ? 330f : 165f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = type == WeatherType.Rain || type == WeatherType.Snow
                ? new Vector3(8.5f, 0.4f, 8.5f)
                : new Vector3(7.5f, 2.5f, 7.5f);
            shape.position = type == WeatherType.Rain || type == WeatherType.Snow
                ? new Vector3(0f, 7f, 0f)
                : new Vector3(0f, 1.4f, 0f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            switch (type)
            {
                case WeatherType.Rain:
                    velocity.x = 0.8f;
                    velocity.y = -15f;
                    velocity.z = 0.35f;
                    break;
                case WeatherType.Snow:
                    velocity.x = 0.35f;
                    velocity.y = -0.85f;
                    velocity.z = 0.18f;
                    break;
                case WeatherType.Wind:
                    velocity.x = 7f;
                    velocity.y = 0.4f;
                    velocity.z = 2.5f;
                    break;
                case WeatherType.Heat:
                    velocity.x = 0.2f;
                    velocity.y = 2.8f;
                    velocity.z = -0.15f;
                    break;
                case WeatherType.Electric:
                    velocity.x = 1.2f;
                    velocity.y = 1.8f;
                    velocity.z = 0.8f;
                    break;
            }

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = type == WeatherType.Wind ? 1.7f : type == WeatherType.Snow ? 0.65f : 0.28f;
            noise.frequency = 0.7f;
            noise.scrollSpeed = 0.55f;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = type == WeatherType.Rain || type == WeatherType.Wind
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = type == WeatherType.Rain ? 2.8f : type == WeatherType.Wind ? 1.7f : 1f;
            renderer.cameraVelocityScale = 0f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void DrawInstanced(Mesh mesh, Material material, Matrix4x4[] matrices, int count)
        {
            if (mesh == null || material == null || count <= 0 || worldCamera == null)
            {
                return;
            }

            var parameters = new RenderParams(material)
            {
                camera = worldCamera,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
                worldBounds = GameplayWorldBounds(new Bounds(Vector3.zero, new Vector3(120f, 40f, 100f)))
            };
            for (var offset = 0; offset < count; offset += 1023)
            {
                var batchCount = Mathf.Min(1023, count - offset);
                Graphics.RenderMeshInstanced(parameters, mesh, 0, matrices, batchCount, offset);
            }
        }

        private void ResolveGameplayRoot()
        {
            if (gameplayRoot != null)
            {
                return;
            }

            if (tabletopExperience == null)
            {
                tabletopExperience = FindFirstObjectByType<MrTabletopExperience>();
            }
            if (tabletopExperience != null && tabletopExperience.TabletopIsland != null)
            {
                gameplayRoot = tabletopExperience.TabletopIsland;
                return;
            }

            // Dynamic/editor fallback: find the authored island in the
            // hierarchy rather than assuming the VFX root is at world origin.
            for (var current = effectRoot; current != null; current = current.parent)
            {
                if (current.name == "Weather Tabletop Island")
                {
                    gameplayRoot = current;
                    return;
                }
            }
        }

        private Vector3 GameplayToWorldPoint(Vector3 gameplayPosition)
        {
            ResolveGameplayRoot();
            return gameplayRoot != null
                ? gameplayRoot.TransformPoint(gameplayPosition)
                : gameplayPosition;
        }

        private Vector3 GameplayToLocalPosition(Transform target, Vector3 gameplayPosition)
        {
            if (target == null)
            {
                return gameplayPosition;
            }
            return target.InverseTransformPoint(GameplayToWorldPoint(gameplayPosition));
        }

        private void SetGameplayPosition(Transform target, Vector3 gameplayPosition)
        {
            if (target == null)
            {
                return;
            }
            target.position = GameplayToWorldPoint(gameplayPosition);
        }

        private Matrix4x4 GameplayToWorldMatrix(Matrix4x4 gameplayMatrix)
        {
            ResolveGameplayRoot();
            return gameplayRoot != null
                ? gameplayRoot.localToWorldMatrix * gameplayMatrix
                : gameplayMatrix;
        }

        private Bounds GameplayWorldBounds(Bounds gameplayBounds)
        {
            ResolveGameplayRoot();
            if (gameplayRoot == null)
            {
                return gameplayBounds;
            }

            var matrix = gameplayRoot.localToWorldMatrix;
            var localExtents = gameplayBounds.extents;
            var worldExtents = new Vector3(
                Mathf.Abs(matrix.m00) * localExtents.x + Mathf.Abs(matrix.m01) * localExtents.y + Mathf.Abs(matrix.m02) * localExtents.z,
                Mathf.Abs(matrix.m10) * localExtents.x + Mathf.Abs(matrix.m11) * localExtents.y + Mathf.Abs(matrix.m12) * localExtents.z,
                Mathf.Abs(matrix.m20) * localExtents.x + Mathf.Abs(matrix.m21) * localExtents.y + Mathf.Abs(matrix.m22) * localExtents.z);
            return new Bounds(matrix.MultiplyPoint3x4(gameplayBounds.center), worldExtents * 2f);
        }

        private static Material CreateMaterial(string name, Color color, bool transparent, bool particle = false)
        {
            var resourceName = particle ? "WeatherManager/RuntimeParticle" : "WeatherManager/RuntimeUnlit";
            var baseMaterial = Resources.Load<Material>(resourceName);
            Material material;
            if (baseMaterial != null)
            {
                material = new Material(baseMaterial);
            }
            else
            {
                var shader = Shader.Find(particle
                    ? "Universal Render Pipeline/Particles/Unlit"
                    : "Universal Render Pipeline/Unlit");
                shader ??= Shader.Find(particle ? "Particles/Standard Unlit" : "Unlit/Color");
                if (shader == null)
                {
                    throw new System.InvalidOperationException(
                        $"Weather Manager runtime material '{resourceName}' is missing and no fallback shader is available.");
                }
                material = new Material(shader);
            }

            material.name = name;
            material.color = color;
            material.enableInstancing = true;
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.renderQueue = (int)RenderQueue.Transparent;
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            return material;
        }

        private static Mesh CreateSurfaceMesh()
        {
            var mesh = new Mesh { name = "SurfaceCellMesh" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateConeMesh(int segments)
        {
            var vertices = new Vector3[segments + 2];
            vertices[0] = new Vector3(0f, 1f, 0f);
            vertices[1] = Vector3.zero;
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (float)segments * Mathf.PI * 2f;
                vertices[index + 2] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            var triangles = new int[segments * 6];
            for (var index = 0; index < segments; index++)
            {
                var next = (index + 1) % segments;
                var offset = index * 6;
                triangles[offset] = 0;
                triangles[offset + 1] = index + 2;
                triangles[offset + 2] = next + 2;
                triangles[offset + 3] = 1;
                triangles[offset + 4] = next + 2;
                triangles[offset + 5] = index + 2;
            }
            var mesh = new Mesh { name = "LowPolyCone" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateRingMesh(int segments, float inner = .82f)
        {
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (float)segments * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[index * 2] = direction * inner;
                vertices[index * 2 + 1] = direction;
                var next = (index + 1) % segments;
                var offset = index * 6;
                triangles[offset] = index * 2;
                triangles[offset + 1] = next * 2 + 1;
                triangles[offset + 2] = index * 2 + 1;
                triangles[offset + 3] = index * 2;
                triangles[offset + 4] = next * 2;
                triangles[offset + 5] = next * 2 + 1;
            }
            var mesh = new Mesh { name = "LowPolyRing" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
