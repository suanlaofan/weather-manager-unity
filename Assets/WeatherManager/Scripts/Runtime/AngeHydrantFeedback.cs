using System.Collections.Generic;
using UnityEngine;

namespace WeatherManager
{
    [DisallowMultipleComponent]
    public sealed class AngeHydrantFeedback : MonoBehaviour
    {
        private static readonly List<AngeHydrantFeedback> Active = new List<AngeHydrantFeedback>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Renderer[] renderers;
        [SerializeField] private float feedbackRadius = 15f;
        private MaterialPropertyBlock propertyBlock;
        private float pulse;
        private WorldBuilder world;

        public void Configure(Renderer[] targetRenderers, float radius)
        {
            renderers = targetRenderers;
            feedbackRadius = Mathf.Max(1f, radius);
        }

        public static void NotifyExtinguish(Vector3 position)
        {
            AngeHydrantFeedback nearest = null;
            var nearestDistance = float.PositiveInfinity;
            for (var index = 0; index < Active.Count; index++)
            {
                var hydrant = Active[index];
                if (hydrant == null) continue;
                // Extinguish events originate from DisasterSimulation in
                // authored gameplay coordinates. Convert each hydrant from its
                // potentially transformed MR tabletop pose before comparing the
                // radius, otherwise the physical meter-scale transform makes
                // every nearest-hydrant lookup incorrect.
                var distance = (hydrant.GetGameplayPosition() - position).sqrMagnitude;
                if (distance >= nearestDistance || distance > hydrant.feedbackRadius * hydrant.feedbackRadius)
                {
                    continue;
                }
                nearest = hydrant;
                nearestDistance = distance;
            }
            nearest?.Trigger();
        }

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
            ClearFeedback();
        }

        private void Update()
        {
            if (pulse <= 0f) return;
            pulse = Mathf.Max(0f, pulse - Time.deltaTime);
            var amount = Mathf.Sin(pulse * 18f) * 0.5f + 0.5f;
            ApplyFeedback(Color.Lerp(new Color(0.15f, 0.55f, 1f), Color.white, amount));
            if (pulse <= 0f) ClearFeedback();
        }

        private void Trigger()
        {
            pulse = 1.15f;
        }

        private void ApplyFeedback(Color color)
        {
            if (renderers == null) return;
            propertyBlock ??= new MaterialPropertyBlock();
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null) continue;
                propertyBlock.Clear();
                propertyBlock.SetColor(BaseColorId, color);
                propertyBlock.SetColor(ColorId, color);
                propertyBlock.SetColor(EmissionColorId, color * 0.55f);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void ClearFeedback()
        {
            if (renderers == null) return;
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null) renderers[index].SetPropertyBlock(null);
            }
        }

        private Vector3 GetGameplayPosition()
        {
            if (world == null)
            {
                world = FindFirstObjectByType<WorldBuilder>();
            }

            return world != null ? world.GetGameplayPosition(transform) : transform.position;
        }
    }
}
