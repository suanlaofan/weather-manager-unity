using System;
using UnityEngine;

namespace WeatherManager
{
    public enum WeatherType
    {
        Wind,
        Rain,
        Electric,
        Snow,
        Heat
    }

    public enum SeasonType
    {
        Spring,
        Summer,
        Autumn,
        Winter
    }

    public enum DayPhase
    {
        Day,
        Night
    }

    public enum DamageableKind
    {
        Building,
        Tree,
        Farm,
        Vehicle,
        Infrastructure
    }

    public enum BiomeType
    {
        Town,
        Forest,
        Farmland,
        Highland,
        Lowland,
        Water
    }

    [Serializable]
    public readonly struct BiomeSample
    {
        public static readonly BiomeSample Default = new BiomeSample(
            BiomeType.Town, "Town", 0f, 0.3f, 1f, 1f, 1f, 1f, 1f, 1f, 1f);

        public BiomeSample(
            BiomeType type,
            string name,
            float elevation,
            float wetness,
            float flammability,
            float drainage,
            float lightningExposure,
            float fireSpread,
            float floodAccumulation,
            float snowLoad,
            float windExposure,
            float slope = 0f)
        {
            Type = type;
            Name = name;
            Elevation = elevation;
            Wetness = wetness;
            Flammability = flammability;
            Drainage = drainage;
            LightningExposure = lightningExposure;
            FireSpread = fireSpread;
            FloodAccumulation = floodAccumulation;
            SnowLoad = snowLoad;
            WindExposure = windExposure;
            Slope = slope;
        }

        public BiomeType Type { get; }
        public string Name { get; }
        public float Elevation { get; }
        public float Wetness { get; }
        public float Flammability { get; }
        public float Drainage { get; }
        public float LightningExposure { get; }
        public float FireSpread { get; }
        public float FloodAccumulation { get; }
        public float SnowLoad { get; }
        public float WindExposure { get; }
        public float Slope { get; }
    }

    public enum DamageVisualStage
    {
        Healthy,
        Damaged,
        Critical,
        Destroyed
    }

    public enum ResidentLifeState
    {
        Calm,
        Fleeing,
        Injured,
        Dead
    }

    [Serializable]
    public struct WeatherCastState
    {
        public WeatherType Type;
        public Vector3 Center;
        public Vector3 Direction;
        public float Radius;
        public float HoldSeconds;
        public bool Active;
        public int CastId;

        public WeatherCastState(
            WeatherType type,
            Vector3 center,
            Vector3 direction,
            float radius,
            float holdSeconds,
            bool active,
            int castId)
        {
            Type = type;
            Center = center;
            Direction = direction;
            Radius = radius;
            HoldSeconds = holdSeconds;
            Active = active;
            CastId = castId;
        }
    }

    [Serializable]
    public struct HazardSample
    {
        public BiomeType Biome;
        public float Elevation;
        public float Wetness;
        public float Flammability;
        public float Drainage;
        public float Fire;
        public float Flood;
        public float Moisture;
        public float Snow;
        public float Heat;
        public float Wind;
        public float Electric;
        public Vector3 EscapeDirection;

        public float Threat => Mathf.Clamp01(
            Fire * 1.2f
            + Flood * 1.05f
            + Snow * 0.18f
            + Heat * 0.2f
            + Wind * 1.25f
                + Electric * 1.4f);
    }

    [Serializable]
    public struct RainExtinguishAcceptanceSnapshot
    {
        public bool Passed;
        public float SurfaceFireBefore;
        public float SurfaceFireAfter;
        public int ActiveFiresBefore;
        public int ActiveFiresAfter;
        public bool BuildingBurningBefore;
        public bool BuildingBurningAfter;
        public float BuildingHealthBefore;
        public float BuildingHealthAfter;
        public bool FireTornadoBefore;
        public bool FireTornadoAfter;
        public float FireTornadoStrengthBefore;
        public float FireTornadoStrengthAfter;
    }

    [Serializable]
    public struct WeatherFeedbackAcceptanceSnapshot
    {
        public WeatherType Weather;
        public bool Passed;
        public float SimulatedSeconds;
        public int ActiveFires;
        public int FloodedCells;
        public int SnowyCells;
        public int Tornadoes;
        public int FireTornadoes;
        public int DestroyedObjects;
        public int AliveResidents;
        public int InjuredResidents;
        public int DeadResidents;
        public bool Blackout;
    }

    [Serializable]
    public struct BiomeDifferenceAcceptanceSnapshot
    {
        public bool Passed;
        public int ZoneCount;
        public float ForestFire;
        public float FarmlandFire;
        public float TownFlood;
        public float LowlandFlood;
        public float TownLightning;
        public float HighlandLightning;
        public float WaterFire;
    }

    [Serializable]
    public sealed class SimulationStats
    {
        public int TotalResidents;
        public int AliveResidents;
        public int InjuredResidents;
        public int DeadResidents;
        public int FleeingResidents;
        public int ActiveFires;
        public int Tornadoes;
        public int FireTornadoes;
        public int FloodedCells;
        public int SnowyCells;
        public int ScorchedCells;
        public int DestroyedObjects;
        public int TotalDamageables;
        public bool Blackout;
        public float FloodedPercent;
        public float Elapsed;

        public void Reset()
        {
            TotalResidents = 0;
            AliveResidents = 0;
            InjuredResidents = 0;
            DeadResidents = 0;
            FleeingResidents = 0;
            ActiveFires = 0;
            Tornadoes = 0;
            FireTornadoes = 0;
            FloodedCells = 0;
            SnowyCells = 0;
            ScorchedCells = 0;
            DestroyedObjects = 0;
            TotalDamageables = 0;
            Blackout = false;
            FloodedPercent = 0f;
            Elapsed = 0f;
        }
    }

    [Serializable]
    public sealed class DamageableState
    {
        public int Id;
        public DamageableKind Kind;
        public Transform Root;
        public Renderer[] Renderers = Array.Empty<Renderer>();
        public Color[] BaseColors = Array.Empty<Color>();
        // Gameplay state is authored in the tabletop island's local coordinate
        // system.  A PICO MR session moves and scales that island into physical
        // metres, so these local values are the only safe transform baseline
        // for Reset.  The legacy InitialPosition/InitialRotation remain the
        // gameplay-space values used by simulation code and old baked scenes.
        public Vector3 InitialLocalPosition;
        public Quaternion InitialLocalRotation = Quaternion.identity;
        public bool HasLocalTransformBaseline;
        public Vector3 InitialPosition;
        public Quaternion InitialRotation;
        public Vector3 InitialScale;
        public float Radius;
        public float MaxHealth;
        public float Health;
        public float FireVulnerability;
        public float FloodVulnerability;
        public float SnowVulnerability;
        public float WindVulnerability;
        public int VehicleRouteTargetIndex = -1;
        public float VehicleSpeed;
        public bool Burning;
        public bool Destroyed;
        public bool Blackout;
        public DamageVisualStage VisualStage = DamageVisualStage.Healthy;

        public Vector3 Position => Root != null ? Root.position : InitialPosition;
    }

    public readonly struct SurfaceVisualCell
    {
        public readonly Vector3 Position;
        public readonly float Water;
        public readonly float Snow;
        public readonly float Scorch;
        public readonly float Fire;

        public SurfaceVisualCell(Vector3 position, float water, float snow, float scorch, float fire)
        {
            Position = position;
            Water = water;
            Snow = snow;
            Scorch = scorch;
            Fire = fire;
        }
    }

    public readonly struct TornadoSnapshot
    {
        public readonly int Id;
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;
        public readonly float Strength;
        public readonly float Age;
        public readonly bool IsFireTornado;

        public TornadoSnapshot(
            int id,
            Vector3 position,
            Vector3 velocity,
            float strength,
            float age,
            bool isFireTornado)
        {
            Id = id;
            Position = position;
            Velocity = velocity;
            Strength = strength;
            Age = age;
            IsFireTornado = isFireTornado;
        }
    }

    public static class WeatherPalette
    {
        public static readonly Color Wind = new Color(0.25f, 0.78f, 0.82f);
        public static readonly Color Rain = new Color(0.2f, 0.48f, 0.92f);
        public static readonly Color Electric = new Color(1f, 0.88f, 0.28f);
        public static readonly Color Snow = new Color(0.82f, 0.94f, 1f);
        public static readonly Color Heat = new Color(1f, 0.36f, 0.14f);

        public static Color For(WeatherType type)
        {
            return type switch
            {
                WeatherType.Wind => Wind,
                WeatherType.Rain => Rain,
                WeatherType.Electric => Electric,
                WeatherType.Snow => Snow,
                WeatherType.Heat => Heat,
                _ => Color.white
            };
        }

        public static string Label(WeatherType type)
        {
            return LocalizationService.Text(LocalizationService.Weather(type).Key);
        }
    }
}
