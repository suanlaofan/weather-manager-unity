using System.Runtime.CompilerServices;
using UnityEngine;

namespace WeatherManager
{
    public static class WorldBuilderCompatibility
    {
        private sealed class VisualState
        {
            public Light[] NightLights;
            public float[] AuthoredRanges;
            public DayPhase Phase;
            public float Time;
        }

        private static readonly ConditionalWeakTable<WorldBuilder, VisualState> States =
            new ConditionalWeakTable<WorldBuilder, VisualState>();

        public static void Build(this WorldBuilder world, Transform root)
        {
            if (world.HasBakedScene)
            {
                world.InitializeBakedRuntime();
            }
            else
            {
                world.BuildWorld(root);
            }
            EnsureVisualState(world);
        }

        public static void SetDayPhase(this WorldBuilder world, DayPhase phase)
        {
            var state = EnsureVisualState(world);
            state.Phase = phase;
            ApplyNightLights(state, phase == DayPhase.Night ? 1f : 0f);
        }

        public static void TickVisuals(
            this WorldBuilder world,
            float unscaledDelta,
            float simulationDelta,
            DayPhase phase)
        {
            var state = EnsureVisualState(world);
            state.Phase = phase;
            state.Time += unscaledDelta;
            var night = phase == DayPhase.Night ? 1f : 0f;
            for (var index = 0; index < state.NightLights.Length; index++)
            {
                var light = state.NightLights[index];
                if (light == null)
                {
                    continue;
                }
                var pulse = 0.94f + Mathf.Sin(state.Time * 2.2f + index * 0.7f) * 0.06f;
                var scale=Mathf.Abs(light.transform.lossyScale.x);
                light.range=state.AuthoredRanges[index]*scale;
                light.intensity = Mathf.Lerp(light.intensity, night * 1.55f * pulse*scale*scale, 1f - Mathf.Exp(-unscaledDelta * 4f));
                light.enabled = night>.01f||light.intensity>0.000001f;
            }
        }

        private static VisualState EnsureVisualState(WorldBuilder world)
        {
            if (States.TryGetValue(world, out var existing))
            {
                return existing;
            }

            var state = new VisualState
            {
                NightLights = world.GetNightLights(!world.HasBakedScene),
                Phase = DayPhase.Day,
                Time = 0f
            };
            state.AuthoredRanges=new float[state.NightLights.Length];
            for(var i=0;i<state.NightLights.Length;i++)state.AuthoredRanges[i]=state.NightLights[i]!=null?state.NightLights[i].range:0;
            States.Add(world, state);
            return state;
        }

        private static void ApplyNightLights(VisualState state, float intensity)
        {
            for (var index = 0; index < state.NightLights.Length; index++)
            {
                var light = state.NightLights[index];
                if (light == null)
                {
                    continue;
                }
                var scale=Mathf.Abs(light.transform.lossyScale.x);
                light.range=state.AuthoredRanges[index]*scale;
                light.intensity = intensity * 1.55f*scale*scale;
                light.enabled = intensity > 0.01f;
            }
        }
    }
}
