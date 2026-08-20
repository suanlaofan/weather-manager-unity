using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherManager
{
    [Serializable]
    public sealed class BiomeZoneDefinition
    {
        public string Name;
        public BiomeType Type;
        public Vector2 Center;
        public Vector2 Size = new Vector2(20f, 20f);
        [Range(-1f, 8f)] public float Elevation;
        [Range(0f, 1f)] public float Wetness;
        [Range(0f, 2f)] public float Flammability = 1f;
        [Range(0f, 2f)] public float Drainage = 1f;
        [Range(0f, 2f)] public float LightningExposure = 1f;
        [Range(0f, 2f)] public float FireSpread = 1f;
        [Range(0f, 2f)] public float FloodAccumulation = 1f;
        [Range(0f, 2f)] public float SnowLoad = 1f;
        [Range(0f, 2f)] public float WindExposure = 1f;
    }

    [DisallowMultipleComponent]
    public sealed class BiomeMap : MonoBehaviour
    {
        [SerializeField] private float halfWidth = 52f;
        [SerializeField] private float halfDepth = 36f;
        [SerializeField] private LowPolyTerrainGenerator terrain;
        [SerializeField] private List<BiomeZoneDefinition> zones = new List<BiomeZoneDefinition>();

        public float HalfWidth => halfWidth;
        public float HalfDepth => halfDepth;
        public IReadOnlyList<BiomeZoneDefinition> Zones => zones;

        public void ConfigureDefaults(float worldHalfWidth, float worldHalfDepth)
        {
            halfWidth = worldHalfWidth;
            halfDepth = worldHalfDepth;
            zones = new List<BiomeZoneDefinition>
            {
                Zone("Town", BiomeType.Town, 0f, -4f, 38f, 28f, 2.75f, 0.32f, 0.55f, 1.32f, 1f, 0.85f, 0.72f, 0.9f, 0.82f),
                Zone("Forest", BiomeType.Forest, -13f, 15f, 44f, 36f, 4.2f, 0.38f, 1.28f, 0.62f, 1.12f, 1.38f, 0.8f, 1.28f, 1.18f),
                Zone("Farmland", BiomeType.Farmland, -31f, -20f, 38f, 29f, 2.9f, 0.24f, 1.42f, 0.48f, 0.95f, 1.58f, 1.08f, 0.72f, 1.34f),
                Zone("Highland", BiomeType.Highland, -36f, 18f, 32f, 31f, 12f, 0.18f, 0.68f, 1.68f, 1.58f, 0.72f, 0.36f, 1.45f, 1.48f),
                Zone("River Valley", BiomeType.Lowland, 29f, -17f, 43f, 34f, 1.1f, 0.74f, 0.26f, 0.18f, 0.82f, 0.32f, 1.78f, 0.52f, 0.88f),
                Zone("River Channel", BiomeType.Water, 29f, -16f, 10f, 38f, 0.9f, 1f, 0f, 0f, 0.7f, 0f, 2f, 0f, 0.72f),
                Zone("Coastal Lake", BiomeType.Water, 17f, 3f, 22f, 17f, 0.9f, 1f, 0f, 0f, 0.7f, 0f, 2f, 0f, 0.72f)
            };
        }

        public void ConfigureTerrain(LowPolyTerrainGenerator terrainSurface)
        {
            terrain = terrainSurface;
        }

        public BiomeSample Sample(Vector3 worldPosition)
        {
            if (zones == null || zones.Count == 0)
            {
                return BiomeSample.Default;
            }

            var actualElevation = terrain != null ? terrain.SampleHeight(worldPosition) : worldPosition.y;
            var actualSlope = terrain != null ? terrain.SampleSlope(worldPosition) : 0f;
            var terrainWetness = terrain != null ? terrain.SampleWetness(worldPosition) : 0f;
            var waterPoint = terrain != null && terrain.IsWater(worldPosition);
            BiomeZoneDefinition best = null;
            var bestScore = float.NegativeInfinity;
            for (var index = 0; index < zones.Count; index++)
            {
                var zone = zones[index];
                if (zone == null) continue;
                var half = new Vector2(
                    Mathf.Max(0.1f, zone.Size.x * 0.5f),
                    Mathf.Max(0.1f, zone.Size.y * 0.5f));
                var offset = new Vector2(worldPosition.x, worldPosition.z) - zone.Center;
                var normalizedDistance = new Vector2(offset.x / half.x, offset.y / half.y).magnitude;
                var score = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.45f, normalizedDistance));
                if (zone.Type == BiomeType.Water) score += waterPoint ? 8f : -8f;
                if (zone.Type == BiomeType.Highland) score += Mathf.InverseLerp(5f, 13f, actualElevation) * 1.8f + actualSlope * 1.2f;
                if (zone.Type == BiomeType.Lowland) score += terrainWetness * 1.35f + (1f - Mathf.InverseLerp(1f, 5f, actualElevation)) * 0.7f;
                if (zone.Type == BiomeType.Forest) score += Mathf.InverseLerp(2.5f, 8f, actualElevation) * 0.35f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = zone;
            }

            if (best == null) return BiomeSample.Default;
            var wetness = Mathf.Clamp01(Mathf.Lerp(best.Wetness, terrainWetness, 0.72f));
            var flammability = best.Type == BiomeType.Water
                ? 0f
                : best.Flammability * Mathf.Lerp(1.08f, 0.42f, wetness);
            var drainage = best.Drainage * Mathf.Lerp(0.82f, 1.55f, actualSlope);
            var lightning = best.LightningExposure * Mathf.Lerp(0.82f, 1.38f, Mathf.InverseLerp(1f, 14f, actualElevation));
            var flood = best.FloodAccumulation * Mathf.Lerp(1.45f, 0.38f, Mathf.InverseLerp(0.8f, 9f, actualElevation));
            return best == null
                ? BiomeSample.Default
                : new BiomeSample(
                    best.Type,
                    best.Name,
                    actualElevation,
                    wetness,
                    flammability,
                    drainage,
                    lightning,
                    best.FireSpread * Mathf.Lerp(1.12f, 0.58f, wetness),
                    flood,
                    best.SnowLoad * Mathf.Lerp(0.75f, 1.35f, Mathf.InverseLerp(2f, 14f, actualElevation)),
                    best.WindExposure * Mathf.Lerp(0.8f, 1.45f, actualSlope + Mathf.InverseLerp(4f, 14f, actualElevation)),
                    actualSlope);
        }

        private static BiomeZoneDefinition Zone(
            string name,
            BiomeType type,
            float x,
            float z,
            float width,
            float depth,
            float elevation,
            float wetness,
            float flammability,
            float drainage,
            float lightning,
            float fireSpread,
            float flood,
            float snow,
            float wind)
        {
            return new BiomeZoneDefinition
            {
                Name = name,
                Type = type,
                Center = new Vector2(x, z),
                Size = new Vector2(width, depth),
                Elevation = elevation,
                Wetness = wetness,
                Flammability = flammability,
                Drainage = drainage,
                LightningExposure = lightning,
                FireSpread = fireSpread,
                FloodAccumulation = flood,
                SnowLoad = snow,
                WindExposure = wind
            };
        }

        private void OnDrawGizmosSelected()
        {
            if (zones == null) return;
            for (var index = 0; index < zones.Count; index++)
            {
                var zone = zones[index];
                if (zone == null) continue;
                Gizmos.color = BiomePalette.For(zone.Type, 0.42f);
                Gizmos.DrawWireCube(
                    new Vector3(zone.Center.x, zone.Elevation, zone.Center.y),
                    new Vector3(zone.Size.x, 0.35f, zone.Size.y));
            }
        }
    }

    public static class BiomePalette
    {
        public static Color For(BiomeType type, float alpha = 1f)
        {
            var color = type switch
            {
                BiomeType.Town => new Color(0.52f, 0.58f, 0.48f),
                BiomeType.Forest => new Color(0.14f, 0.46f, 0.23f),
                BiomeType.Farmland => new Color(0.64f, 0.56f, 0.24f),
                BiomeType.Highland => new Color(0.45f, 0.48f, 0.48f),
                BiomeType.Lowland => new Color(0.18f, 0.52f, 0.55f),
                BiomeType.Water => new Color(0.12f, 0.4f, 0.68f),
                _ => Color.gray
            };
            color.a = alpha;
            return color;
        }
    }
}
