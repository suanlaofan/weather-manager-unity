using System;
using UnityEngine;

namespace WeatherManager
{
    [CreateAssetMenu(menuName = "Weather Manager/Presentation Settings")]
    public sealed class WeatherPresentationSettings : ScriptableObject
    {
        [Serializable] public sealed class WeatherLayer
        {
            public WeatherType weather;
            public Material material;
            [Min(1)] public int capacity;
            [Min(0)] public float particlesPerSecond;
            public Vector2 size;
            public Vector2 lifetime;
            public Color color = Color.white;
        }
        public WeatherLayer[] weather = new WeatherLayer[5];
        public Material surface;
        public Material flame, smoke, spark, splash, ripple, cloud, steam, funnel, fireFunnel;
        [Range(.1f, 1f)] public float density = 1f;
        [Range(0f, .4f)] public float smallTableReadability = .18f;
        [Range(.1f, .5f)] public float establishSeconds = .25f;
    }
}
