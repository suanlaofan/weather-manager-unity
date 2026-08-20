using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherManager
{
    [DisallowMultipleComponent]
    public sealed class AngeVisualBinding : MonoBehaviour
    {
        [SerializeField] private string sourceAssetId;
        [SerializeField] private string sourceModel;
        [SerializeField] private DamageableKind damageableKind;
        [SerializeField] private Transform legacyVisual;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform criticalVisual;
        [SerializeField] private Transform destroyedVisual;
        [SerializeField] private Vector3 targetSize = Vector3.one;
        [SerializeField] private GameObject sourcePrefab;
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();

        public string SourceAssetId => sourceAssetId;
        public string SourceModel => sourceModel;
        public DamageableKind DamageableKind => damageableKind;
        public Transform LegacyVisual => legacyVisual;
        public Transform VisualRoot => visualRoot;
        public Transform CriticalVisual => criticalVisual;
        public Transform DestroyedVisual => destroyedVisual;
        public Vector3 TargetSize => targetSize;
        public GameObject SourcePrefab => sourcePrefab;
        public Renderer[] Renderers => renderers;

        public void Configure(
            string assetId,
            string modelName,
            DamageableKind kind,
            Transform legacy,
            Transform visual,
            Transform critical,
            Transform destroyed,
            Vector3 size,
            GameObject prefab,
            Renderer[] replacementRenderers)
        {
            sourceAssetId = assetId;
            sourceModel = modelName;
            damageableKind = kind;
            legacyVisual = legacy;
            visualRoot = visual;
            criticalVisual = critical;
            destroyedVisual = destroyed;
            targetSize = size;
            sourcePrefab = prefab;
            renderers = replacementRenderers ?? Array.Empty<Renderer>();
            ApplyStage(DamageVisualStage.Healthy);
        }

        public void RefreshRenderers()
        {
            var collected = new List<Renderer>();
            AddRenderers(visualRoot, collected);
            AddRenderers(criticalVisual, collected);
            AddRenderers(destroyedVisual, collected);
            renderers = collected.ToArray();
        }

        public void ApplyStage(DamageVisualStage stage)
        {
            var useCritical = stage == DamageVisualStage.Critical && criticalVisual != null;
            var useDestroyed = stage == DamageVisualStage.Destroyed && destroyedVisual != null;
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(!useCritical && !useDestroyed);
            }
            if (criticalVisual != null)
            {
                criticalVisual.gameObject.SetActive(useCritical);
            }
            if (destroyedVisual != null)
            {
                destroyedVisual.gameObject.SetActive(useDestroyed);
            }
        }

        private static void AddRenderers(Transform root, ICollection<Renderer> output)
        {
            if (root == null) return;
            var found = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < found.Length; index++)
            {
                output.Add(found[index]);
            }
        }
    }
}
