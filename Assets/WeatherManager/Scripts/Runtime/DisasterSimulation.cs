using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherManager
{
    public sealed class DisasterSimulation : MonoBehaviour
    {
        private sealed class TornadoState
        {
            public int Id;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Strength;
            public float Age;
            public float Life;
            public bool Fire;
            public float WetExposure;
            public float ReigniteCooldown;
        }

        private const int Columns = 88;
        private const int Rows = 60;
        private const float HalfWidth = 52f;
        private const float HalfDepth = 36f;
        private const float FixedStep = 0.1f;
        private const int MaxTornadoes = 5;
        private const int RandomSeed = 148224;

        private readonly float[] water = new float[Columns * Rows];
        private readonly float[] moisture = new float[Columns * Rows];
        private readonly float[] snow = new float[Columns * Rows];
        private readonly float[] heat = new float[Columns * Rows];
        private readonly float[] fire = new float[Columns * Rows];
        private readonly float[] scorch = new float[Columns * Rows];
        private readonly float[] windEnergy = new float[Columns * Rows];
        private readonly float[] electricImpulse = new float[Columns * Rows];
        private readonly List<TornadoState> tornadoes = new List<TornadoState>(MaxTornadoes);
        private readonly List<SurfaceVisualCell> surfaceVisuals = new List<SurfaceVisualCell>(Columns * Rows);
        private readonly List<TornadoSnapshot> tornadoSnapshots = new List<TornadoSnapshot>(MaxTornadoes);
        private readonly SimulationStats stats = new SimulationStats();
        private System.Random random = new System.Random(RandomSeed);

        private WorldBuilder world;
        private BiomeMap biomeMap;
        private WeatherVfxController vfx;
        private WeatherAudioController audioController;
        private float accumulator;
        private float blackoutTimer;
        private float tornadoCooldown;
        private float fireEventCooldown;
        private float chainEventCooldown;
        private float landslideCooldown;
        private int nextTornadoId = 1;
        private bool floodEventActive;
        private bool snowEventActive;
        private bool blackoutEventActive;
        private bool fireEventActive;
        private Vector3 lastFireFeedbackPosition;
        private Vector3 lastFloodFeedbackPosition;
        private Vector3 lastSnowFeedbackPosition;
        private Vector3 lastBlackoutFeedbackPosition;
        private BiomeType lastFireFeedbackBiome = BiomeType.Town;
        private BiomeType lastFloodFeedbackBiome = BiomeType.Lowland;
        private BiomeType lastSnowFeedbackBiome = BiomeType.Highland;
        private BiomeType lastBlackoutFeedbackBiome = BiomeType.Town;

        public event Action<LocalizedMessage, Color> DisasterEvent;

        public SimulationStats Stats => stats;
        public IReadOnlyList<SurfaceVisualCell> SurfaceVisuals => surfaceVisuals;
        public IReadOnlyList<TornadoSnapshot> Tornadoes => tornadoSnapshots;
        public float HalfWorldWidth => HalfWidth;
        public float HalfWorldDepth => HalfDepth;

        public void Initialize(
            WorldBuilder worldBuilder,
            WeatherVfxController weatherVfx,
            WeatherAudioController weatherAudio)
        {
            world = worldBuilder;
            biomeMap = worldBuilder != null ? worldBuilder.Biomes : null;
            vfx = weatherVfx;
            audioController = weatherAudio;
            ResetSimulation();
        }

        public void Step(float simulationDelta, WeatherCastState? cast)
        {
            if (world == null || simulationDelta <= 0f)
            {
                return;
            }

            accumulator += simulationDelta;
            var maxSteps = 8;
            while (accumulator >= FixedStep && maxSteps-- > 0)
            {
                accumulator -= FixedStep;
                SimulateStep(FixedStep, cast);
            }

            world.TickResidents(simulationDelta, this);
            world.TickVehicles(simulationDelta, this);
            RebuildVisualSnapshots();
            vfx?.SyncDisasterVisuals(surfaceVisuals, tornadoSnapshots, blackoutTimer > 0f);
        }

        public void TriggerLightning(Vector3 position, float strength = 1f)
        {
            var clamped = ClampToIsland(position);
            var cell = PositionToIndex(clamped);
            if (cell < 0)
            {
                return;
            }

            var biome = BiomeAtIndex(cell);
            strength *= biome.LightningExposure;
            electricImpulse[cell] = Mathf.Max(electricImpulse[cell], strength);
            heat[cell] = Mathf.Max(heat[cell], 0.92f * strength);
            scorch[cell] = Mathf.Max(scorch[cell], 0.45f * strength);
            var wetGround = biome.Type == BiomeType.Water
                || water[cell] >= 0.12f
                || moisture[cell] >= Mathf.Lerp(0.48f, 0.36f, biome.Wetness);
            var surfaceIgnited = false;
            if (!wetGround)
            {
                var previousFire = fire[cell];
                fire[cell] = Mathf.Max(fire[cell], 0.52f * strength * biome.Flammability);
                surfaceIgnited = fire[cell] > previousFire && fire[cell] > 0.12f;
            }

            var structureIgnited = DamageInRadius(clamped, 2.2f, 26f * strength, !wetGround);
            if (biome.Type == BiomeType.Town)
            {
                blackoutTimer = Mathf.Max(blackoutTimer, 8f + strength * 3f);
                lastBlackoutFeedbackPosition = clamped;
                lastBlackoutFeedbackBiome = biome.Type;
            }
            var ignited = surfaceIgnited || structureIgnited;
            vfx?.PlayLightning(clamped, biome.Type, ignited);
            audioController?.PlayLightning(clamped, biome.Type, ignited);
            var message = ignited
                ? LocalizationService.Message(
                    "event.lightning_ignited",
                    LocalizationService.Biome(biome),
                    LocalizationService.Hazard(biome.Type))
                : wetGround
                    ? LocalizationService.Message("event.lightning_wet_suppressed", LocalizationService.Biome(biome))
                    : biome.Type == BiomeType.Highland
                        ? LocalizationService.Message("event.lightning_highland_rockfall")
                        : LocalizationService.Message("event.lightning_damaged", LocalizationService.Biome(biome));
            DisasterEvent?.Invoke(message, WeatherPalette.Electric);
        }

        public HazardSample Sample(Vector3 position)
        {
            var index = PositionToIndex(position);
            if (index < 0)
            {
                return default;
            }

            var escape = Vector3.zero;
            var wind = windEnergy[index];
            for (var tornadoIndex = 0; tornadoIndex < tornadoes.Count; tornadoIndex++)
            {
                var tornado = tornadoes[tornadoIndex];
                var offset = position - tornado.Position;
                offset.y = 0f;
                var distance = offset.magnitude;
                var influence = Mathf.Clamp01(1f - distance / (3.2f + tornado.Strength));
                if (influence > wind)
                {
                    wind = influence;
                    escape = distance > 0.01f ? offset / distance : Vector3.right;
                }
            }

            if (escape.sqrMagnitude < 0.01f)
            {
                escape = FindSaferDirection(index);
            }

            var biome = biomeMap != null ? biomeMap.Sample(position) : BiomeSample.Default;
            return new HazardSample
            {
                Biome = biome.Type,
                Elevation = biome.Elevation,
                Wetness = biome.Wetness,
                Flammability = biome.Flammability,
                Drainage = biome.Drainage,
                Fire = fire[index],
                Flood = water[index],
                Moisture = moisture[index],
                Snow = snow[index],
                Heat = heat[index],
                Wind = wind,
                Electric = electricImpulse[index],
                EscapeDirection = escape
            };
        }

        public void ResetSimulation()
        {
            Array.Clear(water, 0, water.Length);
            Array.Clear(moisture, 0, moisture.Length);
            Array.Clear(snow, 0, snow.Length);
            Array.Clear(heat, 0, heat.Length);
            Array.Clear(fire, 0, fire.Length);
            Array.Clear(scorch, 0, scorch.Length);
            Array.Clear(windEnergy, 0, windEnergy.Length);
            Array.Clear(electricImpulse, 0, electricImpulse.Length);
            tornadoes.Clear();
            tornadoSnapshots.Clear();
            surfaceVisuals.Clear();
            accumulator = 0f;
            blackoutTimer = 0f;
            tornadoCooldown = 0f;
            fireEventCooldown = 0f;
            chainEventCooldown = 0f;
            landslideCooldown = 0f;
            nextTornadoId = 1;
            floodEventActive = false;
            snowEventActive = false;
            blackoutEventActive = false;
            fireEventActive = false;
            lastFireFeedbackPosition = Vector3.zero;
            lastFloodFeedbackPosition = Vector3.zero;
            lastSnowFeedbackPosition = Vector3.zero;
            lastBlackoutFeedbackPosition = Vector3.zero;
            lastFireFeedbackBiome = BiomeType.Town;
            lastFloodFeedbackBiome = BiomeType.Lowland;
            lastSnowFeedbackBiome = BiomeType.Highland;
            lastBlackoutFeedbackBiome = BiomeType.Town;
            random = new System.Random(RandomSeed);
            stats.Reset();
            world?.ResetWorldState();
            vfx?.ResetAll();
            audioController?.ResetAll();
            RebuildStats();
        }

#if UNITY_EDITOR
        public RainExtinguishAcceptanceSnapshot RunRainExtinguishAcceptance()
        {
            if (world == null || world.Damageables.Count == 0)
            {
                throw new InvalidOperationException("Rain acceptance requires an initialized editable world.");
            }

            ResetSimulation();
            DamageableState testBuilding = null;
            for (var index = 0; index < world.Damageables.Count; index++)
            {
                var candidate = world.Damageables[index];
                if (candidate.Kind == DamageableKind.Building && candidate.Root != null)
                {
                    testBuilding = candidate;
                    break;
                }
            }
            if (testBuilding == null)
            {
                throw new InvalidOperationException("Rain acceptance could not find a building.");
            }

            var center = GameplayPosition(testBuilding);
            var cell = PositionToIndex(center);
            fire[cell] = 0.9f;
            heat[cell] = 0.86f;
            water[cell] = 0f;
            moisture[cell] = 0.02f;
            testBuilding.Burning = true;
            testBuilding.Health = testBuilding.MaxHealth * 0.82f;
            RebuildStats();

            var snapshot = new RainExtinguishAcceptanceSnapshot
            {
                SurfaceFireBefore = fire[cell],
                ActiveFiresBefore = stats.ActiveFires,
                BuildingBurningBefore = testBuilding.Burning,
                BuildingHealthBefore = testBuilding.Health
            };
            var rainCast = new WeatherCastState(
                WeatherType.Rain,
                center,
                Vector3.forward,
                5.2f,
                2f,
                true,
                9001);
            for (var step = 0; step < 20; step++)
            {
                SimulateStep(FixedStep, rainCast);
            }
            RebuildVisualSnapshots();
            snapshot.SurfaceFireAfter = fire[cell];
            snapshot.ActiveFiresAfter = stats.ActiveFires;
            snapshot.BuildingBurningAfter = testBuilding.Burning;
            snapshot.BuildingHealthAfter = testBuilding.Health;
            var normalFirePassed = snapshot.SurfaceFireAfter < 0.07f
                && !snapshot.BuildingBurningAfter
                && snapshot.ActiveFiresAfter < snapshot.ActiveFiresBefore
                && snapshot.BuildingHealthAfter <= snapshot.BuildingHealthBefore + 0.001f;

            ResetSimulation();
            var tornadoCenter = Vector3.zero;
            var tornadoCell = PositionToIndex(tornadoCenter);
            fire[tornadoCell] = 0.82f;
            heat[tornadoCell] = 0.9f;
            var testTornado = new TornadoState
            {
                Id = nextTornadoId++,
                Position = tornadoCenter,
                Velocity = Vector3.zero,
                Strength = 1.18f,
                Age = 0f,
                Life = 10f,
                Fire = true,
                WetExposure = 0f,
                ReigniteCooldown = 0f
            };
            tornadoes.Add(testTornado);
            snapshot.FireTornadoBefore = testTornado.Fire;
            snapshot.FireTornadoStrengthBefore = testTornado.Strength;
            rainCast = new WeatherCastState(
                WeatherType.Rain,
                tornadoCenter,
                Vector3.forward,
                5.2f,
                3f,
                true,
                9002);
            for (var step = 0; step < 30; step++)
            {
                SimulateStep(FixedStep, rainCast);
            }
            snapshot.FireTornadoAfter = testTornado.Fire;
            snapshot.FireTornadoStrengthAfter = testTornado.Strength;
            var fireTornadoPassed = !snapshot.FireTornadoAfter
                && snapshot.FireTornadoStrengthAfter < snapshot.FireTornadoStrengthBefore;
            snapshot.Passed = normalFirePassed && fireTornadoPassed;
            ResetSimulation();
            return snapshot;
        }
#endif

#if UNITY_EDITOR
        public BiomeDifferenceAcceptanceSnapshot RunBiomeDifferenceAcceptance()
        {
            if (world == null || biomeMap == null || biomeMap.Zones.Count < 7)
            {
                throw new InvalidOperationException("Biome acceptance requires the expanded editable world.");
            }

            var snapshot = new BiomeDifferenceAcceptanceSnapshot
            {
                ZoneCount = biomeMap.Zones.Count,
                ForestFire = SimulateBiomeWeather(BiomeType.Forest, WeatherType.Heat, 8f, fire),
                FarmlandFire = SimulateBiomeWeather(BiomeType.Farmland, WeatherType.Heat, 8f, fire),
                TownFlood = SimulateBiomeWeather(BiomeType.Town, WeatherType.Rain, 5f, water),
                LowlandFlood = SimulateBiomeWeather(BiomeType.Lowland, WeatherType.Rain, 5f, water)
            };

            ResetSimulation();
            var town = FindBiomeCenter(BiomeType.Town);
            TriggerLightning(town, 1f);
            snapshot.TownLightning = electricImpulse[PositionToIndex(town)];

            ResetSimulation();
            var highland = FindBiomeCenter(BiomeType.Highland);
            TriggerLightning(highland, 1f);
            snapshot.HighlandLightning = electricImpulse[PositionToIndex(highland)];

            snapshot.WaterFire = SimulateBiomeWeather(BiomeType.Water, WeatherType.Heat, 8f, fire);
            snapshot.Passed = snapshot.ZoneCount >= 7
                && snapshot.ForestFire > 0.1f
                && snapshot.ForestFire > snapshot.FarmlandFire * 1.25f
                && snapshot.LowlandFlood > snapshot.TownFlood * 1.35f
                && snapshot.HighlandLightning > snapshot.TownLightning * 1.25f
                && snapshot.WaterFire <= 0.001f;
            ResetSimulation();
            return snapshot;
        }

        private float SimulateBiomeWeather(
            BiomeType biomeType,
            WeatherType weather,
            float seconds,
            float[] metric)
        {
            ResetSimulation();
            var center = FindBiomeCenter(biomeType);
            var cast = new WeatherCastState(weather, center, Vector3.right, 7.5f, 0f, true, 9800 + (int)biomeType);
            var steps = Mathf.CeilToInt(seconds / FixedStep);
            for (var step = 0; step < steps; step++)
            {
                cast.HoldSeconds = (step + 1) * FixedStep;
                SimulateStep(FixedStep, cast);
            }

            var total = 0f;
            for (var index = 0; index < metric.Length; index++)
            {
                if (BiomeAtIndex(index).Type == biomeType)
                {
                    total += metric[index];
                }
            }
            return total;
        }

        private Vector3 FindBiomeCenter(BiomeType type)
        {
            for (var index = 0; index < biomeMap.Zones.Count; index++)
            {
                var zone = biomeMap.Zones[index];
                if (zone != null && zone.Type == type)
                {
                    return new Vector3(zone.Center.x, 0.08f, zone.Center.y);
                }
            }
            throw new InvalidOperationException($"Biome {type} is not configured.");
        }

        public Vector3 RunBiomeVisualDemo(BiomeType biomeType, WeatherType weather, float seconds = 7f)
        {
            ResetSimulation();
            var center = FindBiomeCenter(biomeType);
            if (weather == WeatherType.Electric)
            {
                TriggerLightning(center, 1f);
                TriggerLightning(center + new Vector3(1.4f, 0f, 0.8f), 0.9f);
            }
            else
            {
                var cast = new WeatherCastState(weather, center, Vector3.right, 8.5f, 0f, true, 9900 + (int)biomeType);
                var steps = Mathf.CeilToInt(seconds / FixedStep);
                for (var step = 0; step < steps; step++)
                {
                    cast.HoldSeconds = (step + 1) * FixedStep;
                    SimulateStep(FixedStep, cast);
                }
            }
            RebuildVisualSnapshots();
            vfx?.SyncDisasterVisuals(surfaceVisuals, tornadoSnapshots, blackoutTimer > 0f);
            return center;
        }
#endif

#if UNITY_EDITOR
        public WeatherFeedbackAcceptanceSnapshot RunWeatherFeedbackAcceptance(WeatherType weatherType)
        {
            if (world == null || world.Damageables.Count == 0)
            {
                throw new InvalidOperationException("Weather feedback acceptance requires an initialized editable world.");
            }

            ResetSimulation();
            DamageableState testBuilding = null;
            for (var index = 0; index < world.Damageables.Count; index++)
            {
                var candidate = world.Damageables[index];
                if (candidate.Kind == DamageableKind.Building && candidate.Root != null)
                {
                    testBuilding = candidate;
                    break;
                }
            }
            if (testBuilding == null)
            {
                throw new InvalidOperationException("Weather feedback acceptance could not find a building.");
            }

            var center = GameplayPosition(testBuilding);
            var cast = new WeatherCastState(
                weatherType,
                center,
                Vector3.right,
                5.2f,
                0f,
                true,
                9100 + (int)weatherType);
            vfx?.SetCasting(cast);
            audioController?.BeginWeather(weatherType, center);

            var steps = weatherType switch
            {
                WeatherType.Wind => 90,
                WeatherType.Rain => 60,
                WeatherType.Electric => 24,
                WeatherType.Snow => 70,
                WeatherType.Heat => 90,
                _ => 60
            };
            switch (weatherType)
            {
                case WeatherType.Rain:
                    ForCellsInRadius(center, 2.6f, (index, falloff) =>
                    {
                        fire[index] = Mathf.Max(fire[index], 0.86f * falloff);
                        heat[index] = Mathf.Max(heat[index], 0.8f * falloff);
                        moisture[index] = Mathf.Min(moisture[index], 0.02f);
                    });
                    testBuilding.Burning = true;
                    testBuilding.Health = testBuilding.MaxHealth * 0.82f;
                    break;
                case WeatherType.Electric:
                    testBuilding.Health = testBuilding.MaxHealth;
                    break;
                case WeatherType.Snow:
                    ForCellsInRadius(center, 2.8f, (index, falloff) =>
                    {
                        snow[index] = Mathf.Max(snow[index], 0.8f * falloff);
                        moisture[index] = Mathf.Max(moisture[index], 0.18f * falloff);
                    });
                    testBuilding.Health = 25f;
                    break;
                case WeatherType.Heat:
                    ForCellsInRadius(center, 2.8f, (index, falloff) =>
                    {
                        heat[index] = Mathf.Max(heat[index], 0.76f * falloff);
                        moisture[index] = Mathf.Min(moisture[index], 0.03f);
                    });
                    break;
            }

            for (var step = 0; step < steps; step++)
            {
                cast.HoldSeconds = (step + 1) * FixedStep;
                if (weatherType == WeatherType.Electric && (step == 2 || step == 8 || step == 14 || step == 20))
                {
                    var strikeOffset = new Vector3((step % 3 - 1) * 1.2f, 0f, (step % 2) * 1.1f);
                    TriggerLightning(center + strikeOffset, 0.95f);
                }
                Step(FixedStep, cast);
            }

            RebuildVisualSnapshots();
            var result = new WeatherFeedbackAcceptanceSnapshot
            {
                Weather = weatherType,
                SimulatedSeconds = steps * FixedStep,
                ActiveFires = stats.ActiveFires,
                FloodedCells = stats.FloodedCells,
                SnowyCells = stats.SnowyCells,
                Tornadoes = stats.Tornadoes,
                FireTornadoes = stats.FireTornadoes,
                DestroyedObjects = stats.DestroyedObjects,
                AliveResidents = stats.AliveResidents,
                InjuredResidents = stats.InjuredResidents,
                DeadResidents = stats.DeadResidents,
                Blackout = stats.Blackout
            };
            result.Passed = weatherType switch
            {
                WeatherType.Wind => result.Tornadoes > 0,
                WeatherType.Rain => result.FloodedCells > 0 && result.ActiveFires == 0 && !testBuilding.Burning,
                WeatherType.Electric => result.Blackout,
                WeatherType.Snow => result.SnowyCells > 0 && result.DestroyedObjects > 0,
                WeatherType.Heat => result.ActiveFires > 0,
                _ => false
            };
            return result;
        }
#endif

        public bool ContainsPoint(Vector3 point)
        {
            var nx = point.x / HalfWidth;
            var nz = point.z / HalfDepth;
            return nx * nx + nz * nz <= 1f;
        }

        public float SampleGroundHeight(Vector3 point)
        {
            return world != null ? world.SampleGroundHeight(point) : 0.08f;
        }

        private void SimulateStep(float delta, WeatherCastState? cast)
        {
            stats.Elapsed += delta;
            tornadoCooldown = Mathf.Max(0f, tornadoCooldown - delta);
            fireEventCooldown = Mathf.Max(0f, fireEventCooldown - delta);
            chainEventCooldown = Mathf.Max(0f, chainEventCooldown - delta);
            landslideCooldown = Mathf.Max(0f, landslideCooldown - delta);
            blackoutTimer = Mathf.Max(0f, blackoutTimer - delta);

            ApplyWeather(delta, cast);
            UpdateCells(delta, cast);
            TryTriggerHighlandLandslide(delta);
            UpdateTornadoes(delta, cast);
            UpdateDamageables(delta);

            RebuildStats();
        }

        private void ApplyWeather(float delta, WeatherCastState? cast)
        {
            if (!cast.HasValue || !cast.Value.Active)
            {
                return;
            }

            var activeCast = cast.Value;
            var extinguishedFireIndex = -1;
            ForCellsInRadius(activeCast.Center, activeCast.Radius, (index, falloff) =>
            {
                var biome = BiomeAtIndex(index);
                switch (activeCast.Type)
                {
                    case WeatherType.Wind:
                        windEnergy[index] = Mathf.Min(
                            1.2f,
                            windEnergy[index] + delta * 0.2f * falloff * biome.WindExposure);
                        moisture[index] = Mathf.Max(
                            0f,
                            moisture[index] - delta * 0.025f * falloff * biome.Drainage);
                        break;
                    case WeatherType.Rain:
                        var fireBeforeRain = fire[index];
                        water[index] = Mathf.Min(
                            1f,
                            water[index] + delta * 0.16f * falloff * biome.FloodAccumulation);
                        moisture[index] = Mathf.Min(
                            1f,
                            moisture[index] + delta * (0.24f + biome.Wetness * 0.16f) * falloff);
                        var suppression = delta
                            * (0.72f + water[index] * 0.34f + moisture[index] * 0.22f + biome.Wetness * 0.18f)
                            * falloff;
                        fire[index] = Mathf.Max(0f, fire[index] - suppression);
                        heat[index] = Mathf.Max(0f, heat[index] - delta * (0.25f + water[index] * 0.18f) * falloff);
                        if (extinguishedFireIndex < 0 && fireBeforeRain > 0.075f && fire[index] <= 0.07f)
                        {
                            extinguishedFireIndex = index;
                        }
                        break;
                    case WeatherType.Snow:
                        snow[index] = Mathf.Min(
                            1f,
                            snow[index] + delta * 0.105f * falloff * biome.SnowLoad);
                        moisture[index] = Mathf.Min(1f, moisture[index] + delta * 0.04f * falloff);
                        fire[index] = Mathf.Max(0f, fire[index] - delta * 0.12f * falloff);
                        heat[index] = Mathf.Max(0f, heat[index] - delta * 0.1f * falloff);
                        break;
                    case WeatherType.Heat:
                        heat[index] = Mathf.Min(
                            1f,
                            heat[index] + delta * 0.14f * falloff * Mathf.Lerp(0.72f, 1.3f, biome.Flammability * 0.5f));
                        moisture[index] = Mathf.Max(
                            0f,
                            moisture[index] - delta * 0.09f * falloff * Mathf.Max(0.3f, biome.Flammability));
                        if (biome.Type != BiomeType.Water && heat[index] > 0.68f && moisture[index] < 0.22f)
                        {
                            fire[index] = Mathf.Max(
                                fire[index],
                                delta * 0.34f * falloff * biome.Flammability);
                        }
                        break;
                }
            });

            if (extinguishedFireIndex >= 0)
            {
                var extinguishedPosition = IndexToPosition(extinguishedFireIndex);
                var extinguishedBiome = BiomeAtIndex(extinguishedFireIndex);
                vfx?.PlaySteam(extinguishedPosition, extinguishedBiome.Type);
                audioController?.PlaySteam(extinguishedPosition, extinguishedBiome.Type);
                AngeHydrantFeedback.NotifyExtinguish(extinguishedPosition);
                if (chainEventCooldown <= 0f)
                {
                    chainEventCooldown = 2.4f;
                    DisasterEvent?.Invoke(LocalizationService.Message("event.rain_extinguished_fire"), WeatherPalette.Rain);
                }
            }

            if (activeCast.Type == WeatherType.Wind && activeCast.HoldSeconds > 2.6f && tornadoCooldown <= 0f)
            {
                var centerIndex = PositionToIndex(activeCast.Center);
                if (centerIndex >= 0 && windEnergy[centerIndex] > 0.46f)
                {
                    SpawnTornado(activeCast.Center, activeCast.Direction);
                }
            }
        }

        private void UpdateCells(float delta, WeatherCastState? cast)
        {
            var snowActive = cast.HasValue && cast.Value.Active && cast.Value.Type == WeatherType.Snow;
            for (var index = 0; index < water.Length; index++)
            {
                var biome = BiomeAtIndex(index);
                water[index] = Mathf.Max(
                    0f,
                    water[index] - delta * (0.002f + biome.Drainage * 0.006f + heat[index] * 0.006f));
                if (!snowActive)
                {
                    snow[index] = Mathf.Max(0f, snow[index] - delta * (0.006f + heat[index] * 0.026f));
                }

                heat[index] = Mathf.Max(0f, heat[index] - delta * 0.014f);
                windEnergy[index] = Mathf.Max(0f, windEnergy[index] - delta * 0.025f);
                electricImpulse[index] = Mathf.Max(0f, electricImpulse[index] - delta * 2.5f);

                if (biome.Type == BiomeType.Water || biome.Flammability <= 0.01f)
                {
                    fire[index] = 0f;
                    heat[index] = Mathf.Min(heat[index], 0.2f);
                    moisture[index] = 1f;
                }
                else if (fire[index] > 0.01f)
                {
                    fire[index] = Mathf.Clamp01(
                        fire[index]
                        + delta * ((0.03f + heat[index] * 0.12f) * biome.Flammability
                        - water[index] * 0.55f
                        - biome.Wetness * 0.06f));
                    heat[index] = Mathf.Min(1f, heat[index] + delta * fire[index] * 0.08f);
                    moisture[index] = Mathf.Max(0f, moisture[index] - delta * fire[index] * 0.16f);
                    scorch[index] = Mathf.Min(1f, scorch[index] + delta * fire[index] * 0.14f);
                    if (fire[index] > 0.55f)
                    {
                        SpreadFire(index, delta);
                    }
                }
                else
                {
                    fire[index] = 0f;
                }
                if (water[index] > 0.025f && biome.Drainage > 0.5f)
                {
                    FlowWaterDownhill(index, delta, biome);
                }
            }
        }

        private void SpreadFire(int index, float delta)
        {
            var sourceBiome = BiomeAtIndex(index);
            if (sourceBiome.Type == BiomeType.Water
                || random.NextDouble() > delta * 0.55f * sourceBiome.FireSpread)
            {
                return;
            }

            var column = index % Columns;
            var row = index / Columns;
            var direction = random.Next(0, 4);
            var nextColumn = column + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
            var nextRow = row + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
            if (nextColumn < 0 || nextColumn >= Columns || nextRow < 0 || nextRow >= Rows)
            {
                return;
            }

            var next = nextRow * Columns + nextColumn;
            var targetBiome = BiomeAtIndex(next);
            if (targetBiome.Type != BiomeType.Water
                && targetBiome.Flammability > 0.05f
                && water[next] < 0.25f
                && moisture[next] < Mathf.Lerp(0.62f, 0.42f, targetBiome.Flammability * 0.5f))
            {
                fire[next] = Mathf.Max(fire[next], 0.1f * targetBiome.Flammability);
                heat[next] = Mathf.Max(heat[next], 0.35f);
            }
        }

        private void SpawnTornado(Vector3 center, Vector3 direction)
        {
            if (tornadoes.Count >= MaxTornadoes)
            {
                return;
            }

            var flatDirection = new Vector3(direction.x, 0f, direction.z);
            if (flatDirection.sqrMagnitude < 0.01f)
            {
                flatDirection = new Vector3(0.85f, 0f, 0.52f);
            }

            flatDirection.Normalize();
            tornadoes.Add(new TornadoState
            {
                Id = nextTornadoId++,
                Position = ClampToIsland(center),
                Velocity = flatDirection * 1.9f,
                Strength = 0.92f,
                Age = 0f,
                Life = 9f + (float)random.NextDouble() * 3f,
                Fire = false,
                WetExposure = 0f,
                ReigniteCooldown = 0f
            });
            tornadoCooldown = 3.8f;
            DisasterEvent?.Invoke(LocalizationService.Message("event.tornado_formed"), WeatherPalette.Wind);
            audioController?.PlayTornado(center);
        }

        private void UpdateTornadoes(float delta, WeatherCastState? cast)
        {
            for (var index = tornadoes.Count - 1; index >= 0; index--)
            {
                var tornado = tornadoes[index];
                tornado.Age += delta;
                tornado.ReigniteCooldown = Mathf.Max(0f, tornado.ReigniteCooldown - delta);
                if (cast.HasValue && cast.Value.Active && cast.Value.Type == WeatherType.Wind)
                {
                    var toCast = cast.Value.Center - tornado.Position;
                    toCast.y = 0f;
                    if (toCast.sqrMagnitude < 42f && toCast.sqrMagnitude > 0.01f)
                    {
                        tornado.Velocity = Vector3.Lerp(tornado.Velocity, toCast.normalized * 2.2f, delta * 0.45f);
                        tornado.Life = Mathf.Min(14f, tornado.Life + delta * 0.35f);
                    }
                }

                tornado.Position += tornado.Velocity * delta;
                tornado.Velocity = Quaternion.Euler(0f, Mathf.Sin(tornado.Age * 0.9f) * 2.4f, 0f) * tornado.Velocity;
                var rainInfluence = RainInfluenceAt(cast, tornado.Position);
                var cell = PositionToIndex(tornado.Position);
                if (cell >= 0)
                {
                    var extinguishedThisStep = false;
                    if (tornado.Fire && rainInfluence > 0f)
                    {
                        tornado.WetExposure += delta * rainInfluence;
                        tornado.Strength = Mathf.Max(0.72f, tornado.Strength - delta * 0.2f * rainInfluence);
                        fire[cell] = Mathf.Max(0f, fire[cell] - delta * 0.9f * rainInfluence);
                        heat[cell] = Mathf.Max(0f, heat[cell] - delta * 0.72f * rainInfluence);
                        if (tornado.WetExposure >= 1.25f && (water[cell] >= 0.08f || moisture[cell] >= 0.24f))
                        {
                            tornado.Fire = false;
                            tornado.Strength = Mathf.Min(tornado.Strength, 0.86f);
                            tornado.ReigniteCooldown = 2.5f;
                            tornado.WetExposure = 0f;
                            fire[cell] = Mathf.Min(fire[cell], 0.05f);
                            heat[cell] = Mathf.Min(heat[cell], 0.24f);
                            extinguishedThisStep = true;
                            var extinguishedBiome = BiomeAtIndex(cell);
                            vfx?.PlaySteam(tornado.Position, extinguishedBiome.Type);
                            audioController?.PlaySteam(tornado.Position, extinguishedBiome.Type);
                            AngeHydrantFeedback.NotifyExtinguish(tornado.Position);
                            DisasterEvent?.Invoke(LocalizationService.Message("event.rain_extinguished_fire_tornado"), WeatherPalette.Rain);
                        }
                    }
                    else if (!tornado.Fire)
                    {
                        tornado.WetExposure = Mathf.Max(0f, tornado.WetExposure - delta * 0.35f);
                    }

                    var localFire = fire[cell];
                    if (!tornado.Fire
                        && !extinguishedThisStep
                        && tornado.ReigniteCooldown <= 0f
                        && rainInfluence < 0.12f
                        && water[cell] < 0.18f
                        && moisture[cell] < 0.5f
                        && localFire > 0.28f)
                    {
                        tornado.Fire = true;
                        tornado.Strength = 1.18f;
                        tornado.WetExposure = 0f;
                        DisasterEvent?.Invoke(LocalizationService.Message("event.fire_tornado_formed"), WeatherPalette.Heat);
                    }

                    if (tornado.Fire)
                    {
                        var sustainedFire = Mathf.Lerp(0.62f, 0.12f, rainInfluence);
                        var sustainedHeat = Mathf.Lerp(0.76f, 0.22f, rainInfluence);
                        fire[cell] = Mathf.Max(fire[cell], sustainedFire);
                        heat[cell] = Mathf.Max(heat[cell], sustainedHeat);
                    }
                }

                DamageInRadius(tornado.Position, 2.4f + tornado.Strength, delta * 16f * tornado.Strength, false);
                if (tornado.Age >= tornado.Life || !ContainsPoint(tornado.Position))
                {
                    tornadoes.RemoveAt(index);
                }
            }
        }

        private void UpdateDamageables(float delta)
        {
            var damageables = world.Damageables;
            for (var index = 0; index < damageables.Count; index++)
            {
                var record = damageables[index];
                if (record.Destroyed || record.Root == null)
                {
                    continue;
                }

                var gameplayPosition = GameplayPosition(record);
                var hazard = Sample(gameplayPosition);
                if (hazard.Fire > 0.12f && hazard.Flood < 0.16f && hazard.Moisture < 0.58f)
                {
                    record.Burning = true;
                }

                if (record.Burning)
                {
                    var rainSoaked = hazard.Flood >= 0.08f || hazard.Moisture >= 0.32f;
                    if (hazard.Fire <= 0.07f && rainSoaked)
                    {
                        record.Burning = false;
                        if (chainEventCooldown <= 0f)
                        {
                            chainEventCooldown = 2.4f;
                            var extinguishedBiome = biomeMap != null
                                ? biomeMap.Sample(gameplayPosition)
                                : BiomeSample.Default;
                            vfx?.PlaySteam(gameplayPosition, extinguishedBiome.Type);
                            audioController?.PlaySteam(gameplayPosition, extinguishedBiome.Type);
                            AngeHydrantFeedback.NotifyExtinguish(gameplayPosition);
                            DisasterEvent?.Invoke(LocalizationService.Message("event.rain_extinguished_structure"), WeatherPalette.Rain);
                        }
                    }
                    else
                    {
                        record.Health -= delta * (3.4f + hazard.Fire * record.FireVulnerability * 7.5f);
                    }
                }

                if (hazard.Flood > 0.38f)
                {
                    record.Health -= delta * hazard.Flood * record.FloodVulnerability * 4.8f;
                }

                if (hazard.Snow > 0.62f)
                {
                    record.Health -= delta * (hazard.Snow - 0.62f) * record.SnowVulnerability * 12f;
                }

                if (hazard.Wind > 0.35f)
                {
                    record.Health -= delta * hazard.Wind * record.WindVulnerability * 7.2f;
                }

                record.Blackout = blackoutTimer > 0f
                    && (record.Kind == DamageableKind.Building || record.Kind == DamageableKind.Infrastructure);
                world.ApplyDamageVisual(record);

                if (record.Health <= 0f)
                {
                    DestroyDamageable(record);
                }
            }
        }

        private void DestroyDamageable(DamageableState record)
        {
            if (record.Destroyed)
            {
                return;
            }

            var gameplayPosition = GameplayPosition(record);
            var hazard = Sample(gameplayPosition);
            var biome = biomeMap != null ? biomeMap.Sample(gameplayPosition) : BiomeSample.Default;
            var wasBurning = record.Burning;
            var snowCollapse = hazard.Snow > 0.55f;
            record.Destroyed = true;
            record.Burning = false;
            record.Health = 0f;
            world.CollapseDamageable(record);
            vfx?.PlayCollapse(gameplayPosition, record.Radius, snowCollapse, biome.Type);
            if (snowCollapse)
            {
                audioController?.PlaySnowCollapse(gameplayPosition, biome.Type);
            }
            else
            {
                audioController?.PlayCollapse(gameplayPosition, biome.Type);
            }
            var message = snowCollapse
                ? LocalizationService.Message("event.collapse_snow", LocalizationService.Biome(biome))
                : hazard.Flood > 0.34f
                    ? LocalizationService.Message("event.collapse_flood", LocalizationService.Biome(biome))
                    : hazard.Wind > 0.34f
                        ? LocalizationService.Message("event.collapse_wind", LocalizationService.Biome(biome))
                        : hazard.Fire > 0.12f || wasBurning
                            ? LocalizationService.Message("event.collapse_fire", LocalizationService.Biome(biome))
                            : hazard.Electric > 0.1f
                                ? LocalizationService.Message("event.collapse_lightning", LocalizationService.Biome(biome))
                                : LocalizationService.Message("event.collapse_generic");
            var color = snowCollapse
                ? WeatherPalette.Snow
                : hazard.Flood > 0.34f
                    ? WeatherPalette.Rain
                    : hazard.Wind > 0.34f
                        ? WeatherPalette.Wind
                        : new Color(0.9f, 0.42f, 0.2f);
            DisasterEvent?.Invoke(message, color);
        }

        private bool DamageInRadius(Vector3 center, float radius, float damage, bool ignite)
        {
            var damageables = world?.Damageables;
            if (damageables == null)
            {
                return false;
            }

            var ignitedAny = false;
            var radiusSquared = radius * radius;
            for (var index = 0; index < damageables.Count; index++)
            {
                var record = damageables[index];
                if (record.Destroyed || record.Root == null)
                {
                    continue;
                }

                var distanceSquared = (GameplayPosition(record) - center).sqrMagnitude;
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                var falloff = 1f - Mathf.Sqrt(distanceSquared) / radius;
                record.Health -= damage * falloff;
                if (ignite && record.FireVulnerability > 0.35f)
                {
                    ignitedAny |= !record.Burning;
                    record.Burning = true;
                }
            }
            return ignitedAny;
        }

        private static float RainInfluenceAt(WeatherCastState? cast, Vector3 position)
        {
            if (!cast.HasValue || !cast.Value.Active || cast.Value.Type != WeatherType.Rain)
            {
                return 0f;
            }

            var offset = position - cast.Value.Center;
            offset.y = 0f;
            var radius = Mathf.Max(0.01f, cast.Value.Radius);
            return Mathf.Clamp01(1f - offset.magnitude / radius);
        }

        private void RebuildStats()
        {
            stats.ActiveFires = 0;
            stats.FloodedCells = 0;
            stats.SnowyCells = 0;
            stats.ScorchedCells = 0;
            var landCells = 0;
            var strongestFloodedIndex = -1;
            var strongestFireIndex = -1;
            var strongestSnowIndex = -1;
            var strongestFlood = 0f;
            var strongestFire = 0f;
            var strongestSnow = 0f;
            for (var index = 0; index < water.Length; index++)
            {
                var position = IndexToPosition(index);
                if (!ContainsPoint(position))
                {
                    continue;
                }

                landCells++;
                if (water[index] > 0.14f)
                {
                    stats.FloodedCells++;
                    if (water[index] > strongestFlood)
                    {
                        strongestFlood = water[index];
                        strongestFloodedIndex = index;
                    }
                }
                if (snow[index] > 0.08f)
                {
                    stats.SnowyCells++;
                    if (snow[index] > strongestSnow)
                    {
                        strongestSnow = snow[index];
                        strongestSnowIndex = index;
                    }
                }
                if (scorch[index] > 0.08f) stats.ScorchedCells++;
                if (fire[index] > 0.12f)
                {
                    stats.ActiveFires++;
                    if (fire[index] > strongestFire)
                    {
                        strongestFire = fire[index];
                        strongestFireIndex = index;
                    }
                }
            }

            stats.FloodedPercent = landCells > 0 ? stats.FloodedCells * 100f / landCells : 0f;
            stats.Tornadoes = tornadoes.Count;
            stats.FireTornadoes = 0;
            for (var index = 0; index < tornadoes.Count; index++)
            {
                if (tornadoes[index].Fire) stats.FireTornadoes++;
            }

            stats.TotalDamageables = world?.Damageables.Count ?? 0;
            stats.DestroyedObjects = 0;
            if (world != null)
            {
                for (var index = 0; index < world.Damageables.Count; index++)
                {
                    if (world.Damageables[index].Destroyed) stats.DestroyedObjects++;
                }
                world.FillResidentStats(stats);
            }
            stats.Blackout = blackoutTimer > 0f;

            if (strongestFloodedIndex >= 0)
            {
                lastFloodFeedbackPosition = IndexToPosition(strongestFloodedIndex);
                lastFloodFeedbackBiome = BiomeAtIndex(strongestFloodedIndex).Type;
            }
            if (strongestSnowIndex >= 0)
            {
                lastSnowFeedbackPosition = IndexToPosition(strongestSnowIndex);
                lastSnowFeedbackBiome = BiomeAtIndex(strongestSnowIndex).Type;
            }
            if (strongestFireIndex >= 0)
            {
                lastFireFeedbackPosition = IndexToPosition(strongestFireIndex);
                lastFireFeedbackBiome = BiomeAtIndex(strongestFireIndex).Type;
            }

            if (stats.FloodedCells >= 10 && !floodEventActive)
            {
                floodEventActive = true;
                if (strongestFloodedIndex >= 0)
                {
                    vfx?.PlayFlood(lastFloodFeedbackPosition, lastFloodFeedbackBiome, strongestFlood);
                    audioController?.PlayFlood(lastFloodFeedbackPosition, lastFloodFeedbackBiome);
                }
                var floodedBiome = strongestFloodedIndex >= 0 ? BiomeAtIndex(strongestFloodedIndex) : BiomeSample.Default;
                var floodMessage = floodedBiome.Type == BiomeType.Lowland || floodedBiome.Type == BiomeType.Water
                    ? LocalizationService.Message("event.flood_lowland")
                    : floodedBiome.Type == BiomeType.Farmland
                        ? LocalizationService.Message("event.flood_farmland")
                        : floodedBiome.Type == BiomeType.Town
                            ? LocalizationService.Message("event.flood_town")
                            : LocalizationService.Message("event.flood_generic", LocalizationService.Biome(floodedBiome));
                DisasterEvent?.Invoke(floodMessage, WeatherPalette.Rain);
            }
            else if (stats.FloodedCells < 4 && floodEventActive)
            {
                floodEventActive = false;
                PlayRecoveryFeedback(lastFloodFeedbackPosition, lastFloodFeedbackBiome, WeatherType.Rain);
            }

            if (stats.SnowyCells >= 10 && !snowEventActive)
            {
                snowEventActive = true;
                if (strongestSnowIndex >= 0)
                {
                    vfx?.PlaySnowLoad(lastSnowFeedbackPosition, lastSnowFeedbackBiome, strongestSnow);
                    audioController?.PlaySnowLoad(lastSnowFeedbackPosition, lastSnowFeedbackBiome);
                }
                DisasterEvent?.Invoke(LocalizationService.Message("event.snow_load"), WeatherPalette.Snow);
            }
            else if (stats.SnowyCells < 4 && snowEventActive)
            {
                snowEventActive = false;
                PlayRecoveryFeedback(lastSnowFeedbackPosition, lastSnowFeedbackBiome, WeatherType.Snow);
            }

            if (stats.Blackout && !blackoutEventActive)
            {
                blackoutEventActive = true;
                DisasterEvent?.Invoke(LocalizationService.Message("event.town_blackout"), WeatherPalette.Electric);
            }
            else if (!stats.Blackout)
            {
                if (blackoutEventActive)
                {
                    PlayRecoveryFeedback(lastBlackoutFeedbackPosition, lastBlackoutFeedbackBiome, WeatherType.Electric);
                }
                blackoutEventActive = false;
            }

            if (stats.ActiveFires > 0)
            {
                fireEventActive = true;
                if (fireEventCooldown > 0f)
                {
                    return;
                }

                fireEventCooldown = 4f;
                var fireBiome = strongestFireIndex >= 0 ? BiomeAtIndex(strongestFireIndex) : BiomeSample.Default;
                var fireMessage = fireBiome.Type switch
                {
                    BiomeType.Forest => LocalizationService.Message("event.fire_forest"),
                    BiomeType.Farmland => LocalizationService.Message("event.fire_farmland"),
                    BiomeType.Town => LocalizationService.Message("event.fire_town"),
                    BiomeType.Highland => LocalizationService.Message("event.fire_highland"),
                    BiomeType.Lowland => LocalizationService.Message("event.fire_lowland"),
                    _ => LocalizationService.Message("event.fire_generic", LocalizationService.Biome(fireBiome))
                };
                DisasterEvent?.Invoke(fireMessage, WeatherPalette.Heat);
                if (strongestFireIndex >= 0)
                {
                    vfx?.PlayFire(lastFireFeedbackPosition, fireBiome.Type, strongestFire);
                    audioController?.PlayFire(lastFireFeedbackPosition, fireBiome.Type);
                }
            }
            else if (fireEventActive)
            {
                fireEventActive = false;
                PlayRecoveryFeedback(lastFireFeedbackPosition, lastFireFeedbackBiome, WeatherType.Heat);
            }
        }

        private void PlayRecoveryFeedback(Vector3 position, BiomeType biome, WeatherType resolvedHazard)
        {
            vfx?.PlayRecovery(position, biome, resolvedHazard);
            audioController?.PlayRecovery(position, biome, resolvedHazard);
        }

        private void RebuildVisualSnapshots()
        {
            surfaceVisuals.Clear();
            for (var index = 0; index < water.Length; index++)
            {
                if (water[index] <= 0.035f && snow[index] <= 0.035f && scorch[index] <= 0.045f && fire[index] <= 0.07f)
                {
                    continue;
                }

                var position = IndexToPosition(index);
                if (!ContainsPoint(position))
                {
                    continue;
                }

                surfaceVisuals.Add(new SurfaceVisualCell(position, water[index], snow[index], scorch[index], fire[index]));
            }

            tornadoSnapshots.Clear();
            for (var index = 0; index < tornadoes.Count; index++)
            {
                var tornado = tornadoes[index];
                tornadoSnapshots.Add(new TornadoSnapshot(
                    tornado.Id,
                    tornado.Position,
                    tornado.Velocity,
                    tornado.Strength,
                    tornado.Age,
                    tornado.Fire));
            }
        }

        private void ForCellsInRadius(Vector3 center, float radius, Action<int, float> action)
        {
            var minColumn = Mathf.Clamp(Mathf.FloorToInt((center.x - radius + HalfWidth) / (HalfWidth * 2f) * Columns), 0, Columns - 1);
            var maxColumn = Mathf.Clamp(Mathf.CeilToInt((center.x + radius + HalfWidth) / (HalfWidth * 2f) * Columns), 0, Columns - 1);
            var minRow = Mathf.Clamp(Mathf.FloorToInt((center.z - radius + HalfDepth) / (HalfDepth * 2f) * Rows), 0, Rows - 1);
            var maxRow = Mathf.Clamp(Mathf.CeilToInt((center.z + radius + HalfDepth) / (HalfDepth * 2f) * Rows), 0, Rows - 1);
            for (var row = minRow; row <= maxRow; row++)
            {
                for (var column = minColumn; column <= maxColumn; column++)
                {
                    var index = row * Columns + column;
                    var position = IndexToPosition(index);
                    if (!ContainsPoint(position))
                    {
                        continue;
                    }

                    var distance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(center.x, center.z));
                    if (distance <= radius)
                    {
                        action(index, 1f - distance / radius);
                    }
                }
            }
        }

        private int PositionToIndex(Vector3 position)
        {
            if (!ContainsPoint(position))
            {
                return -1;
            }

            var column = Mathf.Clamp(Mathf.FloorToInt((position.x + HalfWidth) / (HalfWidth * 2f) * Columns), 0, Columns - 1);
            var row = Mathf.Clamp(Mathf.FloorToInt((position.z + HalfDepth) / (HalfDepth * 2f) * Rows), 0, Rows - 1);
            return row * Columns + column;
        }

        private Vector3 IndexToPosition(int index)
        {
            var column = index % Columns;
            var row = index / Columns;
            var x = -HalfWidth + (column + 0.5f) * (HalfWidth * 2f / Columns);
            var z = -HalfDepth + (row + 0.5f) * (HalfDepth * 2f / Rows);
            var position = new Vector3(x, 0f, z);
            position.y = SampleGroundHeight(position) + 0.08f;
            return position;
        }

        private Vector3 FindSaferDirection(int index)
        {
            var currentThreat = CellThreat(index);
            if (currentThreat < 0.08f)
            {
                return Vector3.zero;
            }

            var column = index % Columns;
            var row = index / Columns;
            var current = IndexToPosition(index);
            var direction = Vector3.zero;
            AccumulateSaferDirection(column - 1, row, currentThreat, current, ref direction);
            AccumulateSaferDirection(column + 1, row, currentThreat, current, ref direction);
            AccumulateSaferDirection(column, row - 1, currentThreat, current, ref direction);
            AccumulateSaferDirection(column, row + 1, currentThreat, current, ref direction);
            if (direction.sqrMagnitude > 0.001f)
            {
                direction.y = 0f;
                return direction.normalized;
            }

            var angle = ((index * 73) % 360) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        private void AccumulateSaferDirection(
            int column,
            int row,
            float currentThreat,
            Vector3 current,
            ref Vector3 direction)
        {
            if (column < 0 || column >= Columns || row < 0 || row >= Rows)
            {
                return;
            }

            var neighborIndex = row * Columns + column;
            var neighbor = IndexToPosition(neighborIndex);
            if (!ContainsPoint(neighbor))
            {
                return;
            }

            var improvement = currentThreat - CellThreat(neighborIndex);
            if (improvement > 0f)
            {
                direction += (neighbor - current).normalized * improvement;
            }
        }

        private float CellThreat(int index)
        {
            return Mathf.Clamp01(
                fire[index] * 1.2f
                + water[index] * 1.05f
                + snow[index] * 0.18f
                + heat[index] * 0.2f
                + windEnergy[index] * 1.25f
                + electricImpulse[index] * 1.4f);
        }

        private BiomeSample BiomeAtIndex(int index)
        {
            return biomeMap != null ? biomeMap.Sample(IndexToPosition(index)) : BiomeSample.Default;
        }

        private static string BiomeHazardLabel(BiomeType type)
        {
            return type switch
            {
                BiomeType.Forest => "forest wildfire",
                BiomeType.Farmland => "field fire",
                BiomeType.Highland => "highland scrub fire",
                BiomeType.Lowland => "lowland fire",
                BiomeType.Town => "town fire",
                _ => "surface fire"
            };
        }

        private void FlowWaterDownhill(int index, float delta, BiomeSample sourceBiome)
        {
            var column = index % Columns;
            var row = index / Columns;
            var bestIndex = -1;
            var bestElevation = sourceBiome.Elevation;
            FindLowerNeighbor(column - 1, row, ref bestIndex, ref bestElevation);
            FindLowerNeighbor(column + 1, row, ref bestIndex, ref bestElevation);
            FindLowerNeighbor(column, row - 1, ref bestIndex, ref bestElevation);
            FindLowerNeighbor(column, row + 1, ref bestIndex, ref bestElevation);
            if (bestIndex < 0) return;

            var transfer = Mathf.Min(
                water[index] * 0.16f,
                delta * (0.025f + sourceBiome.Drainage * 0.07f));
            water[index] = Mathf.Max(0f, water[index] - transfer);
            water[bestIndex] = Mathf.Min(1f, water[bestIndex] + transfer * 0.94f);
            moisture[bestIndex] = Mathf.Min(1f, moisture[bestIndex] + transfer * 0.28f);
        }

        private void FindLowerNeighbor(int column, int row, ref int bestIndex, ref float bestElevation)
        {
            if (column < 0 || column >= Columns || row < 0 || row >= Rows) return;
            var candidate = row * Columns + column;
            var position = IndexToPosition(candidate);
            if (!ContainsPoint(position)) return;
            var elevation = BiomeAtIndex(candidate).Elevation;
            if (elevation >= bestElevation - 0.02f) return;
            bestElevation = elevation;
            bestIndex = candidate;
        }

        private void TryTriggerHighlandLandslide(float delta)
        {
            if (landslideCooldown > 0f || random.NextDouble() > delta * 0.16f) return;
            var index = random.Next(0, water.Length);
            var biome = BiomeAtIndex(index);
            if (biome.Type != BiomeType.Highland
                || biome.Slope < 0.16f
                || (water[index] < 0.24f && snow[index] < 0.48f)) return;

            var position = IndexToPosition(index);
            var intensity = Mathf.Clamp01(water[index] * 1.4f + snow[index] * 0.9f);
            DamageInRadius(position, 3.8f, 16f + intensity * 22f, false);
            var snowDriven = snow[index] > water[index];
            vfx?.PlayCollapse(position, 2.6f + intensity, snowDriven, biome.Type);
            if (snowDriven) audioController?.PlaySnowCollapse(position, biome.Type);
            else audioController?.PlayCollapse(position, biome.Type);
            landslideCooldown = 6f;
            DisasterEvent?.Invoke(LocalizationService.Message("event.highland_landslide"), new Color(0.72f, 0.5f, 0.25f));
        }

        private Vector3 ClampToIsland(Vector3 position)
        {
            var normalized = new Vector2(position.x / HalfWidth, position.z / HalfDepth);
            if (normalized.sqrMagnitude <= 0.94f)
            {
                position.y = SampleGroundHeight(position) + 0.08f;
                return position;
            }

            normalized = normalized.normalized * 0.96f;
            var clamped = new Vector3(normalized.x * HalfWidth, 0f, normalized.y * HalfDepth);
            clamped.y = SampleGroundHeight(clamped) + 0.08f;
            return clamped;
        }

        private Vector3 GameplayPosition(DamageableState record)
        {
            if (record == null)
            {
                return Vector3.zero;
            }

            return world != null ? world.GetGameplayPosition(record) : record.Position;
        }
    }
}
