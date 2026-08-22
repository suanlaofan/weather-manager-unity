using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherManager
{
    public sealed class WorldBuilder : MonoBehaviour
    {
        private sealed class VehicleRuntime
        {
            private readonly WorldBuilder world;
            private readonly DamageableState state;
            private readonly Vector3[] route;
            private readonly LowPolyTerrainGenerator terrain;
            private readonly int initialTargetIndex;
            private readonly float speed;
            private int targetIndex;
            private float animationTime;

            public VehicleRuntime(
                WorldBuilder worldBuilder,
                DamageableState damageable,
                Vector3[] path,
                int nextTarget,
                float moveSpeed,
                LowPolyTerrainGenerator terrainSurface)
            {
                world = worldBuilder;
                state = damageable;
                route = path;
                terrain = terrainSurface;
                initialTargetIndex = nextTarget;
                targetIndex = nextTarget;
                speed = moveSpeed;
            }

            public void Tick(float delta, DisasterSimulation simulation)
            {
                if (state == null || state.Root == null || state.Destroyed || route == null || route.Length < 2)
                {
                    return;
                }

                var currentPosition = world != null
                    ? world.GetGameplayPosition(state)
                    : state.Root.position;
                var hazard = simulation.Sample(currentPosition);
                var speedFactor = Mathf.Clamp01(
                    1f
                    - hazard.Flood * 0.84f
                    - hazard.Snow * 0.42f
                    - hazard.Fire * 0.72f
                    - hazard.Wind * 0.34f);
                if (speedFactor < 0.08f)
                {
                    return;
                }

                animationTime += delta;
                var target = route[targetIndex];
                var toTarget = target - currentPosition;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.25f)
                {
                    targetIndex = (targetIndex + 1) % route.Length;
                    target = route[targetIndex];
                    toTarget = target - currentPosition;
                    toTarget.y = 0f;
                }

                if (toTarget.sqrMagnitude <= 0.001f)
                {
                    return;
                }

                var direction = toTarget.normalized;
                var nextPosition = currentPosition + direction * (speed * speedFactor * delta);
                nextPosition.y = (terrain != null ? terrain.SampleHeight(nextPosition) + 0.13f : state.InitialPosition.y)
                    + Mathf.Sin(animationTime * 7f) * hazard.Flood * 0.035f;
                if (world != null)
                {
                    world.SetGameplayPosition(state.Root, nextPosition);
                }
                else
                {
                    state.Root.position = nextPosition;
                }
                var targetRotation = VehicleRoadRotation(direction);
                var currentRotation = world != null
                    ? world.GetGameplayRotation(state.Root)
                    : state.Root.rotation;
                var nextRotation = Quaternion.Slerp(
                    currentRotation,
                    targetRotation,
                    1f - Mathf.Exp(-delta * 7f));
                if (world != null)
                {
                    world.SetGameplayRotation(state.Root, nextRotation);
                }
                else
                {
                    state.Root.rotation = nextRotation;
                }
            }

            public void Reset()
            {
                targetIndex = initialTargetIndex;
                animationTime = 0f;
            }
        }

        private const int BuildingCount = 42;
        private const int TreeCount = 96;
        private const int FarmCount = 22;
        private const int VehicleCount = 12;
        private const int InfrastructureCount = 13;
        private const int ResidentCount = 84;
        private const float HalfWidth = 52f;
        private const float HalfDepth = 36f;
        private const float TownHalfWidth = 18f;
        private const float TownHalfDepth = 13f;
        private const float GoldenAngle = 2.39996323f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Quaternion VehicleForwardCorrection = Quaternion.Euler(0f, -90f, 0f);

        [SerializeField] private List<DamageableState> damageables = new List<DamageableState>(
            BuildingCount + TreeCount + FarmCount + VehicleCount + InfrastructureCount);
        [SerializeField] private List<ResidentAgent> residents = new List<ResidentAgent>(ResidentCount);
        private readonly List<VehicleRuntime> vehicles = new List<VehicleRuntime>(VehicleCount);
        private readonly List<Mesh> ownedMeshes = new List<Mesh>(12);
        private readonly List<Material> ownedMaterials = new List<Material>(40);
        private MaterialPropertyBlock propertyBlock;

        [SerializeField] private bool bakedScene;
        [SerializeField] private Transform generatedRoot;
        [SerializeField] private Transform environmentRoot;
        [SerializeField] private Transform buildingsRoot;
        [SerializeField] private Transform vegetationRoot;
        [SerializeField] private Transform residentsRoot;
        // The common authored island root.  In MR it is moved and uniformly
        // scaled onto a table; simulation coordinates must remain local to it.
        [SerializeField] private Transform gameplayRoot;
        [SerializeField] private Transform nightLightsRoot;
        [SerializeField] private BiomeMap biomeMap;
        [SerializeField] private LowPolyTerrainGenerator terrainGenerator;
        [SerializeField] private Light[] nightLights = Array.Empty<Light>();
        [SerializeField] private Mesh boxMesh;
        [SerializeField] private Mesh cylinderMesh;
        [SerializeField] private Mesh coneMesh;
        [SerializeField] private Mesh octahedronMesh;
        [SerializeField] private Mesh roofMesh;
        [SerializeField] private Mesh discMesh;
        [SerializeField] private Material landMaterial;
        [SerializeField] private Material cliffMaterial;
        [SerializeField] private Material sandMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Material roadMaterial;
        [SerializeField] private Material stoneMaterial;
        [SerializeField] private Material trunkMaterial;
        [SerializeField] private Material foliageMaterial;
        [SerializeField] private Material farmSoilMaterial;
        [SerializeField] private Material cropMaterial;
        [SerializeField] private Material infrastructureMaterial;
        [SerializeField] private Material windowMaterial;
        [SerializeField] private Material doorMaterial;
        [SerializeField] private Material skinMaterial;
        [SerializeField] private Material pantsMaterial;
        [SerializeField] private Material tireMaterial;
        [SerializeField] private Material[] wallMaterials = Array.Empty<Material>();
        [SerializeField] private Material[] roofMaterials = Array.Empty<Material>();
        [SerializeField] private Material[] carMaterials = Array.Empty<Material>();
        [SerializeField] private Material[] shirtMaterials = Array.Empty<Material>();
        [SerializeField] private int nextDamageableId = 1;
        [NonSerialized] private bool ownsGeneratedRoot;

        public IReadOnlyList<DamageableState> Damageables => damageables;
        public BiomeMap Biomes => biomeMap;
        public LowPolyTerrainGenerator TerrainSurface => terrainGenerator;
        public bool HasBakedScene => bakedScene
            && environmentRoot != null
            && buildingsRoot != null
            && vegetationRoot != null
            && residentsRoot != null;

        private void Awake()
        {
            if (HasBakedScene)
            {
                InitializeBakedRuntime();
            }
        }

        public void BuildWorld(Transform parent)
        {
            propertyBlock ??= new MaterialPropertyBlock();
            EnsureResources();
            ClearGeneratedWorld();
            EnsureCollections();
            damageables.Clear();
            residents.Clear();
            vehicles.Clear();
            nextDamageableId = 1;
            bakedScene = false;

            generatedRoot = new GameObject("WeatherManager_GeneratedWorld").transform;
            ownsGeneratedRoot = true;
            generatedRoot.SetParent(parent != null ? parent : transform, false);
            generatedRoot.localPosition = Vector3.zero;
            generatedRoot.localRotation = Quaternion.identity;
            generatedRoot.localScale = Vector3.one;
            environmentRoot = CreateContainer("Environment", generatedRoot);
            buildingsRoot = CreateContainer("Buildings", generatedRoot);
            vegetationRoot = CreateContainer("Vegetation", generatedRoot);
            residentsRoot = CreateContainer("Residents", generatedRoot);

            BuildWorldContents();
            EnsureNightLights(true);
            StaticBatchingUtility.Combine(environmentRoot.gameObject);
        }

        public void PrepareBakedScene(
            Transform bakedEnvironmentRoot,
            Transform bakedBuildingsRoot,
            Transform bakedVegetationRoot,
            Transform bakedResidentsRoot)
        {
            if (bakedEnvironmentRoot == null
                || bakedBuildingsRoot == null
                || bakedVegetationRoot == null
                || bakedResidentsRoot == null)
            {
                throw new ArgumentNullException(nameof(bakedEnvironmentRoot), "All baked world roots are required.");
            }

            propertyBlock ??= new MaterialPropertyBlock();
            EnsureResources();
            ClearGeneratedWorld();
            ClearChildren(bakedEnvironmentRoot);
            ClearChildren(bakedBuildingsRoot);
            ClearChildren(bakedVegetationRoot);
            ClearChildren(bakedResidentsRoot);
            EnsureCollections();
            damageables.Clear();
            residents.Clear();
            vehicles.Clear();
            nextDamageableId = 1;
            generatedRoot = null;
            ownsGeneratedRoot = false;
            environmentRoot = bakedEnvironmentRoot;
            buildingsRoot = bakedBuildingsRoot;
            vegetationRoot = bakedVegetationRoot;
            residentsRoot = bakedResidentsRoot;
            nightLightsRoot = null;
            nightLights = Array.Empty<Light>();
            bakedScene = true;

            BuildWorldContents();
            EnsureNightLights(true);
            InitializeBakedRuntime();
        }

        public void InitializeBakedRuntime()
        {
            propertyBlock ??= new MaterialPropertyBlock();
            EnsureCollections();
            if (terrainGenerator == null && environmentRoot != null)
            {
                terrainGenerator = environmentRoot.GetComponent<LowPolyTerrainGenerator>();
            }
            if (biomeMap == null && environmentRoot != null)
            {
                biomeMap = environmentRoot.GetComponent<BiomeMap>();
            }
            biomeMap?.ConfigureTerrain(terrainGenerator);
            RecoverBakedResourceReferences(false);
            damageables.RemoveAll(record => record == null || record.Root == null);
            residents.RemoveAll(resident => resident == null);

            if (residents.Count == 0 && residentsRoot != null)
            {
                residents.AddRange(residentsRoot.GetComponentsInChildren<ResidentAgent>(true));
            }

            nextDamageableId = 1;
            for (var index = 0; index < damageables.Count; index++)
            {
                var record = damageables[index];
                nextDamageableId = Mathf.Max(nextDamageableId, record.Id + 1);
                if (record.Renderers == null || record.Renderers.Length == 0)
                {
                    record.Renderers = record.Root.GetComponentsInChildren<Renderer>(true);
                }
                if (record.BaseColors == null || record.BaseColors.Length != record.Renderers.Length)
                {
                    RefreshBaseColors(record);
                }
                // Preserve the authored local pose as the Reset baseline.
                // A later initialization can happen after
                // MrTabletopExperience has moved/scaled the tabletop into
                // physical metres; recapturing then would make Reset restore
                // an MR world-space pose and send buildings off the island.
                // New/generated records are captured by RegisterDamageable;
                // older baked scenes migrate their missing local baseline here.
                EnsureInitialPose(record);
            }

            for (var index = 0; index < residents.Count; index++)
            {
                residents[index].SetTerrain(terrainGenerator);
                residents[index].InitializeBakedRuntime();
            }

            RebuildVehicleRuntime();
            EnsureNightLights(false);
        }

        public void RefreshBakedAssetReferences()
        {
            RecoverBakedResourceReferences(true);
            for (var index = 0; index < damageables.Count; index++)
            {
                var record = damageables[index];
                if (record == null || record.Root == null)
                {
                    continue;
                }

                var angeBinding = record.Root.GetComponent<AngeVisualBinding>();
                if (angeBinding != null)
                {
                    angeBinding.RefreshRenderers();
                    record.Renderers = angeBinding.Renderers;
                }
                else
                {
                    record.Renderers = record.Root.GetComponentsInChildren<Renderer>(true);
                }
                RefreshBaseColors(record);
            }
        }

        private void BuildWorldContents()
        {
            EnsureTerrainGenerator();
            EnsureBiomeMap();
            BuildStaticEnvironment();
            BuildBuildings();
            BuildFarms();
            BuildTrees();
            BuildInfrastructure();
            BuildVehicles();
            BuildResidents();
            ApplyDefaultSeasonPalette();
        }

        private void EnsureBiomeMap()
        {
            if (biomeMap == null && environmentRoot != null)
            {
                biomeMap = environmentRoot.GetComponent<BiomeMap>();
            }
            if (biomeMap == null && environmentRoot != null)
            {
                biomeMap = environmentRoot.gameObject.AddComponent<BiomeMap>();
            }
            biomeMap?.ConfigureDefaults(HalfWidth, HalfDepth);
            biomeMap?.ConfigureTerrain(terrainGenerator);
        }

        private void EnsureTerrainGenerator()
        {
            if (terrainGenerator == null && environmentRoot != null)
            {
                terrainGenerator = environmentRoot.GetComponent<LowPolyTerrainGenerator>();
            }
            if (terrainGenerator == null && environmentRoot != null)
            {
                terrainGenerator = environmentRoot.gameObject.AddComponent<LowPolyTerrainGenerator>();
            }
        }

        public float SampleGroundHeight(Vector3 position)
        {
            return terrainGenerator != null ? terrainGenerator.SampleHeight(position) : 0f;
        }

        public Vector3 SampleGroundNormal(Vector3 position)
        {
            return terrainGenerator != null ? terrainGenerator.SampleNormal(position) : Vector3.up;
        }

        /// <summary>
        /// Converts an authored object pose back into simulation/gameplay
        /// coordinates.  This remains identity for the Editor and desktop,
        /// while PICO MR removes the physical tabletop pose and scale.
        /// </summary>
        public Vector3 GetGameplayPosition(Transform source)
        {
            if (source == null)
            {
                return Vector3.zero;
            }

            var root = ResolveGameplayRoot();
            return root != null ? root.InverseTransformPoint(source.position) : source.position;
        }

        public Vector3 GetGameplayPosition(DamageableState record)
        {
            if (record == null)
            {
                return Vector3.zero;
            }

            return record.Root != null ? GetGameplayPosition(record.Root) : record.InitialPosition;
        }

        public Quaternion GetGameplayRotation(Transform source)
        {
            if (source == null)
            {
                return Quaternion.identity;
            }

            var root = ResolveGameplayRoot();
            return root != null ? Quaternion.Inverse(root.rotation) * source.rotation : source.rotation;
        }

        public void SetGameplayPosition(Transform target, Vector3 gameplayPosition)
        {
            if (target == null)
            {
                return;
            }

            var root = ResolveGameplayRoot();
            target.position = root != null ? root.TransformPoint(gameplayPosition) : gameplayPosition;
        }

        public void SetGameplayRotation(Transform target, Quaternion gameplayRotation)
        {
            if (target == null)
            {
                return;
            }

            var root = ResolveGameplayRoot();
            target.rotation = root != null ? root.rotation * gameplayRotation : gameplayRotation;
        }

        public int RebuildCompactIslandSurface()
        {
            EnsureResources();
            EnsureTerrainGenerator();
            terrainGenerator.Build(environmentRoot, waterMaterial, roadMaterial);
            biomeMap?.ConfigureTerrain(terrainGenerator);

            var removed = 0;
            var ocean = environmentRoot != null ? environmentRoot.Find("Ocean") : null;
            if (ocean != null)
            {
                DestroyWorldObject(ocean.gameObject);
                removed++;
            }

            var propsRoot = environmentRoot != null ? environmentRoot.Find("AngeProps") : null;
            if (propsRoot != null)
            {
                for (var index = propsRoot.childCount - 1; index >= 0; index--)
                {
                    var child = propsRoot.GetChild(index);
                    if (terrainGenerator.IsInsidePlayableIsland(child.position, 0.012f)) continue;
                    DestroyWorldObject(child.gameObject);
                    removed++;
                }
            }
            return removed;
        }

        public TerrainInvariantSnapshot RebuildFullTerrainSurface()
        {
            EnsureResources();
            EnsureTerrainGenerator();
            terrainGenerator.Build(environmentRoot, waterMaterial, roadMaterial);
            biomeMap?.ConfigureTerrain(terrainGenerator);
            return terrainGenerator.ValidateInvariants();
        }

        public int AlignBakedVehiclesToRoads()
        {
            var aligned = 0;
            var route = CreateVehicleRoute();
            for (var index = 0; index < damageables.Count; index++)
            {
                var state = damageables[index];
                if (state == null || state.Kind != DamageableKind.Vehicle || state.Root == null) continue;
                var targetIndex = Mathf.Clamp(state.VehicleRouteTargetIndex, 0, route.Length - 1);
                var direction = route[targetIndex] - GetGameplayPosition(state);
                direction.y = 0f;
                if (direction.sqrMagnitude <= 0.001f) continue;
                SetGameplayRotation(state.Root, VehicleRoadRotation(direction));
                CaptureInitialPose(state);
                aligned++;
            }
            RebuildVehicleRuntime();
            return aligned;
        }

        public void TickResidents(float delta, DisasterSimulation simulation)
        {
            if (simulation == null || delta <= 0f) return;
            for (var index = 0; index < residents.Count; index++)
            {
                residents[index]?.Tick(delta, simulation);
            }
        }

        public void TickVehicles(float delta, DisasterSimulation simulation)
        {
            if (simulation == null || delta <= 0f) return;
            for (var index = 0; index < vehicles.Count; index++)
            {
                vehicles[index].Tick(delta, simulation);
            }
        }

        public void FillResidentStats(SimulationStats stats)
        {
            if (stats == null) return;
            var dead = 0;
            var injured = 0;
            var fleeing = 0;
            for (var index = 0; index < residents.Count; index++)
            {
                var resident = residents[index];
                if (resident == null || resident.State == ResidentLifeState.Dead) dead++;
                else if (resident.State == ResidentLifeState.Injured) injured++;
                else if (resident.State == ResidentLifeState.Fleeing) fleeing++;
            }

            stats.TotalResidents = residents.Count;
            stats.DeadResidents = dead;
            stats.AliveResidents = Mathf.Max(0, residents.Count - dead);
            stats.InjuredResidents = injured;
            stats.FleeingResidents = fleeing;
        }

        public void ResetWorldState()
        {
            for (var index = 0; index < damageables.Count; index++)
            {
                var record = damageables[index];
                record.Health = record.MaxHealth;
                record.Burning = false;
                record.Destroyed = false;
                record.Blackout = false;
                record.VisualStage = DamageVisualStage.Healthy;
                if (record.Root != null)
                {
                    EnsureInitialPose(record);
                    record.Root.localPosition = record.InitialLocalPosition;
                    record.Root.localRotation = record.InitialLocalRotation;
                    record.Root.localScale = record.InitialScale;
                }
                ApplyDamageVisual(record);
            }

            LogResetVisualRecovery();

            for (var index = 0; index < vehicles.Count; index++) vehicles[index].Reset();
            for (var index = 0; index < residents.Count; index++) residents[index]?.ResetAgent();
        }

        public void ApplyDamageVisual(DamageableState record)
        {
            if (record == null) return;
            var healthRatio = record.MaxHealth > 0f ? Mathf.Clamp01(record.Health / record.MaxHealth) : 0f;
            var damage = 1f - healthRatio;
            var visualStage = ResolveDamageVisualStage(record, healthRatio);
            if (visualStage != record.VisualStage)
            {
                ApplyDamageStageTransform(record, visualStage);
                record.VisualStage = visualStage;
            }
            var angeBinding = record.Root != null
                ? record.Root.GetComponent<AngeVisualBinding>()
                : null;
            if (angeBinding != null)
            {
                angeBinding.ApplyStage(visualStage);
            }

            var minimumDamageTint = visualStage switch
            {
                DamageVisualStage.Damaged => 0.25f,
                DamageVisualStage.Critical => 0.56f,
                DamageVisualStage.Destroyed => 0.82f,
                _ => 0f
            };
            var damageTint = Mathf.Max(damage * 0.78f, minimumDamageTint);
            if (record.Renderers == null) return;

            propertyBlock ??= new MaterialPropertyBlock();
            var baseColors = record.BaseColors ?? Array.Empty<Color>();
            for (var index = 0; index < record.Renderers.Length; index++)
            {
                var renderer = record.Renderers[index];
                if (renderer == null) continue;
                var baseColor = index < baseColors.Length ? baseColors[index] : ReadMaterialColor(renderer.sharedMaterial);
                var color = Color.Lerp(baseColor, new Color(0.18f, 0.17f, 0.16f, baseColor.a), damageTint);
                if (record.Blackout && (record.Kind == DamageableKind.Building || record.Kind == DamageableKind.Infrastructure))
                {
                    color *= 0.42f;
                    color.a = baseColor.a;
                }
                if (record.Burning)
                {
                    color = Color.Lerp(color, new Color(0.78f, 0.2f, 0.06f, baseColor.a), 0.28f);
                }

                propertyBlock.Clear();
                propertyBlock.SetColor(BaseColorId, color);
                propertyBlock.SetColor(ColorId, color);
                propertyBlock.SetColor(EmissionColorId, record.Burning ? new Color(0.72f, 0.12f, 0.025f) : Color.black);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        public void CollapseDamageable(DamageableState record)
        {
            if (record == null || record.Root == null) return;
            record.Destroyed = true;
            if (record.VisualStage != DamageVisualStage.Destroyed)
            {
                ApplyDamageStageTransform(record, DamageVisualStage.Destroyed);
            }
            record.VisualStage = DamageVisualStage.Destroyed;
            ApplyDamageVisual(record);
        }

        private static DamageVisualStage ResolveDamageVisualStage(DamageableState record, float healthRatio)
        {
            if (record.Destroyed || healthRatio <= 0f)
            {
                return DamageVisualStage.Destroyed;
            }
            if (healthRatio <= 0.33f)
            {
                return DamageVisualStage.Critical;
            }
            if (healthRatio <= 0.66f)
            {
                return DamageVisualStage.Damaged;
            }
            return DamageVisualStage.Healthy;
        }

        private void ApplyDamageStageTransform(DamageableState record, DamageVisualStage stage)
        {
            if (record.Root == null) return;

            var sign = (record.Id & 1) == 0 ? 1f : -1f;
            EnsureInitialPose(record);
            var rotation = record.InitialLocalRotation;
            var scaleMultiplier = Vector3.one;

            switch (record.Kind)
            {
                case DamageableKind.Tree:
                    switch (stage)
                    {
                        case DamageVisualStage.Damaged:
                            rotation *= Quaternion.Euler(sign * 5f, 0f, sign * 7f);
                            scaleMultiplier = new Vector3(1.02f, 0.96f, 1.02f);
                            break;
                        case DamageVisualStage.Critical:
                            rotation *= Quaternion.Euler(sign * 16f, 0f, sign * 13f);
                            scaleMultiplier = new Vector3(1.04f, 0.88f, 1.04f);
                            break;
                        case DamageVisualStage.Destroyed:
                            rotation *= Quaternion.Euler(sign * 78f, 0f, sign * 24f);
                            scaleMultiplier = new Vector3(1f, 0.76f, 1f);
                            break;
                    }
                    break;
                case DamageableKind.Farm:
                    scaleMultiplier = stage switch
                    {
                        DamageVisualStage.Damaged => new Vector3(1.01f, 0.78f, 1.01f),
                        DamageVisualStage.Critical => new Vector3(1.03f, 0.42f, 1.03f),
                        DamageVisualStage.Destroyed => new Vector3(1f, 0.18f, 1f),
                        _ => Vector3.one
                    };
                    break;
                case DamageableKind.Vehicle:
                    scaleMultiplier = stage switch
                    {
                        DamageVisualStage.Damaged => new Vector3(1.02f, 0.9f, 1.01f),
                        DamageVisualStage.Critical => new Vector3(1.06f, 0.62f, 1.05f),
                        DamageVisualStage.Destroyed => new Vector3(1.08f, 0.35f, 1.08f),
                        _ => Vector3.one
                    };
                    if (stage == DamageVisualStage.Destroyed)
                    {
                        rotation = record.Root.localRotation * Quaternion.Euler(0f, 0f, sign * 28f);
                    }
                    else
                    {
                        rotation = record.Root.localRotation;
                    }
                    break;
                case DamageableKind.Infrastructure:
                    switch (stage)
                    {
                        case DamageVisualStage.Damaged:
                            rotation *= Quaternion.Euler(sign * 3f, 0f, sign * 4f);
                            scaleMultiplier = new Vector3(1f, 0.98f, 1f);
                            break;
                        case DamageVisualStage.Critical:
                            rotation *= Quaternion.Euler(sign * 15f, 0f, sign * 10f);
                            scaleMultiplier = new Vector3(1.02f, 0.9f, 1.02f);
                            break;
                        case DamageVisualStage.Destroyed:
                            rotation *= Quaternion.Euler(sign * 64f, 0f, sign * 18f);
                            scaleMultiplier = new Vector3(1.04f, 0.72f, 1.04f);
                            break;
                    }
                    break;
                default:
                    switch (stage)
                    {
                        case DamageVisualStage.Damaged:
                            rotation *= Quaternion.Euler(0f, 0f, sign * 1.8f);
                            scaleMultiplier = new Vector3(1.01f, 0.96f, 1.01f);
                            break;
                        case DamageVisualStage.Critical:
                            rotation *= Quaternion.Euler(0f, 0f, sign * 5.5f);
                            scaleMultiplier = new Vector3(1.03f, 0.84f, 1.03f);
                            break;
                        case DamageVisualStage.Destroyed:
                            rotation *= Quaternion.Euler(0f, 0f, sign * 9f);
                            scaleMultiplier = new Vector3(1.06f, 0.24f, 1.06f);
                            break;
                    }
                    break;
            }

            record.Root.localRotation = rotation;
            record.Root.localScale = Vector3.Scale(record.InitialScale, scaleMultiplier);
        }

        private void ApplyDefaultSeasonPalette()
        {
            EnsureResources();
            SetMaterialColor(landMaterial, new Color(0.37f, 0.68f, 0.36f));
            SetMaterialColor(foliageMaterial, new Color(0.28f, 0.72f, 0.31f));
            SetMaterialColor(cropMaterial, new Color(0.58f, 0.82f, 0.29f));
            SetMaterialColor(farmSoilMaterial, new Color(0.48f, 0.32f, 0.18f));

            for (var index = 0; index < damageables.Count; index++)
            {
                var record = damageables[index];
                if (record.Kind != DamageableKind.Tree && record.Kind != DamageableKind.Farm) continue;
                RefreshBaseColors(record);
                ApplyDamageVisual(record);
            }
        }

        private void BuildStaticEnvironment()
        {
            terrainGenerator?.Build(environmentRoot, waterMaterial, roadMaterial);

            for (var index = 0; index < 22; index++)
            {
                var angle = index / 22f * Mathf.PI * 2f + 0.13f;
                var radius = 0.92f;
                var position = new Vector3(
                    Mathf.Cos(angle) * HalfWidth * radius,
                    0f,
                    Mathf.Sin(angle) * HalfDepth * radius);
                position = TerrainPoint(position, 0.08f);
                var rock = CreatePart(
                    $"Coast Rock {index + 1:00}",
                    environmentRoot,
                    octahedronMesh,
                    stoneMaterial,
                    position,
                    new Vector3(0.35f + (index % 3) * 0.12f, 0.28f + (index % 2) * 0.1f, 0.38f),
                    Quaternion.Euler(index * 11f, index * 37f, 0f),
                    true,
                    true);
                rock.receiveShadows = true;
            }
        }

        private void BuildBiomeTerrain()
        {
            var root = CreateContainer("Biome Regions", environmentRoot);

            var town = CreateContainer("01 Town", root);
            CreatePart("Town Ground", town, boxMesh, landMaterial,
                new Vector3(0f, 0.045f, -2f), new Vector3(36f, 0.07f, 26f),
                Quaternion.identity, true, true);

            var forest = CreateContainer("02 Forest", root);
            CreatePart("Forest Floor", forest, boxMesh, foliageMaterial,
                new Vector3(-31f, 0.08f, 15f), new Vector3(38f, 0.12f, 34f),
                Quaternion.identity, true, true);
            CreatePart("Forest Clearing", forest, discMesh, landMaterial,
                new Vector3(-28f, 0.15f, 14f), new Vector3(6.5f, 1f, 5f),
                Quaternion.identity, true, false);

            var farmland = CreateContainer("03 Farmland", root);
            CreatePart("Farmland Ground", farmland, boxMesh, farmSoilMaterial,
                new Vector3(-31f, 0.07f, -20f), new Vector3(36f, 0.1f, 28f),
                Quaternion.identity, true, true);
            AddRegionalRoad("Farm Track", farmland,
                new Vector3(-31f, 0.135f, -20f), new Vector3(32f, 0.065f, 1.3f));

            var highland = CreateContainer("04 Highland", root);
            CreatePart("Highland Base", highland, boxMesh, stoneMaterial,
                new Vector3(29f, 0.38f, 16f), new Vector3(38f, 0.72f, 32f),
                Quaternion.Euler(0f, -4f, 0f), true, true);
            CreatePart("Highland Mid", highland, boxMesh, cliffMaterial,
                new Vector3(31f, 1.25f, 19f), new Vector3(29f, 1.05f, 22f),
                Quaternion.Euler(0f, 7f, 0f), true, true);
            CreatePart("Highland Summit", highland, boxMesh, stoneMaterial,
                new Vector3(34f, 2.15f, 21f), new Vector3(17f, 0.85f, 12f),
                Quaternion.Euler(0f, -11f, 0f), true, true);

            var lowland = CreateContainer("05 River Valley and Coast", root);
            CreatePart("Lowland Meadow", lowland, boxMesh, landMaterial,
                new Vector3(31f, 0.025f, -19f), new Vector3(38f, 0.04f, 29f),
                Quaternion.identity, true, false);
            CreatePart("River", lowland, boxMesh, waterMaterial,
                new Vector3(26f, 0.085f, -18f), new Vector3(5.5f, 0.055f, 32f),
                Quaternion.Euler(0f, -9f, 0f), true, false);
            CreatePart("Coastal Lake", lowland, discMesh, waterMaterial,
                new Vector3(38f, 0.09f, -20f), new Vector3(10f, 1f, 7f),
                Quaternion.identity, true, false);
        }

        private void AddRegionalRoad(string name, Transform parent, Vector3 position, Vector3 scale)
        {
            CreatePart(name, parent, boxMesh, roadMaterial, position, scale, Quaternion.identity, true, false);
        }

        private void BuildBuildings()
        {
            var created = 0;
            for (var row = 0; row < 5; row++)
            {
                for (var column = 0; column < 6; column++)
                {
                    var index = row * 6 + column;
                    var x = -11f + column * 4.3f;
                    var z = -12f + row * 3.8f;
                    if (Mathf.Abs(x) < 0.5f) x += (row & 1) == 0 ? 1.65f : -1.65f;
                    var width = 1.75f + (index % 3) * 0.22f;
                    var depth = 1.5f + (index % 4) * 0.15f;
                    var height = 1.4f + (index % 5) * 0.26f;
                    CreateBuilding(created++, TerrainPoint(new Vector3(x, 0f, z), 0.08f), width, depth, height);
                }
            }

            var outlyingHomes = new[]
            {
                new Vector3(-42f, 0f, -14f), new Vector3(-35f, 0f, -11f),
                new Vector3(-27f, 0f, -13f), new Vector3(-20f, 0f, -16f),
                new Vector3(-18f, 0f, 12f), new Vector3(-8f, 0f, 15f),
                new Vector3(3f, 0f, 15f), new Vector3(12f, 0f, 14f),
                new Vector3(18f, 0f, 14f), new Vector3(24f, 0f, 15f),
                new Vector3(35f, 0f, 14f), new Vector3(40f, 0f, 9f)
            };
            for (var index = 0; index < outlyingHomes.Length; index++)
            {
                var buildingIndex = created;
                var width = 1.7f + (buildingIndex % 3) * 0.2f;
                var depth = 1.45f + (buildingIndex % 4) * 0.14f;
                var height = 1.35f + (buildingIndex % 5) * 0.23f;
                CreateBuilding(
                    created++,
                    TerrainPoint(outlyingHomes[index], 0.08f),
                    width,
                    depth,
                    height);
            }
        }

        private void CreateBuilding(int index, Vector3 position, float width, float depth, float height)
        {
            var root = CreateObjectRoot($"Building {index + 1:00}", buildingsRoot, position, Quaternion.identity);
            CreatePart("Walls", root, boxMesh, wallMaterials[index % wallMaterials.Length], new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, depth), Quaternion.identity, false, true);
            CreatePart("Roof", root, roofMesh, roofMaterials[index % roofMaterials.Length], new Vector3(0f, height, 0f), new Vector3(width * 1.14f, 1.2f, depth * 1.18f), Quaternion.identity, false, true);
            CreatePart("Door", root, boxMesh, doorMaterial, new Vector3(0f, 0.4f, depth * 0.51f), new Vector3(0.36f, 0.78f, 0.06f), Quaternion.identity, false, true);
            CreatePart("Window Left", root, boxMesh, windowMaterial, new Vector3(-width * 0.25f, height * 0.6f, depth * 0.515f), new Vector3(0.34f, 0.36f, 0.055f), Quaternion.identity, false, true);
            CreatePart("Window Right", root, boxMesh, windowMaterial, new Vector3(width * 0.25f, height * 0.6f, depth * 0.515f), new Vector3(0.34f, 0.36f, 0.055f), Quaternion.identity, false, true);
            AddBoxCollider(root, new Vector3(0f, height * 0.55f, 0f), new Vector3(width, height * 1.1f, depth));
            RegisterDamageable(
                root,
                DamageableKind.Building,
                Mathf.Max(width, depth) * 0.7f,
                105f + (index % 4) * 12f,
                0.68f,
                0.72f,
                0.72f,
                0.74f);
        }

        private void BuildFarms()
        {
            for (var index = 0; index < FarmCount; index++)
            {
                var column = index % 6;
                var row = index / 6;
                var position = TerrainPoint(new Vector3(-44f + column * 5f, 0f, -28f + row * 5.1f), 0.12f);
                var rotation = Quaternion.Euler(0f, (row & 1) == 0 ? 0f : 90f, 0f);
                var root = CreateObjectRoot($"Farm {index + 1:00}", vegetationRoot, position, rotation);
                CreatePart("Soil", root, boxMesh, farmSoilMaterial, new Vector3(0f, 0.03f, 0f), new Vector3(2.6f, 0.08f, 1.55f), Quaternion.identity, false, true);
                for (var cropRow = -2; cropRow <= 2; cropRow++)
                {
                    CreatePart(
                        $"Crop Row {cropRow + 3}",
                        root,
                        boxMesh,
                        cropMaterial,
                        new Vector3(cropRow * 0.42f, 0.16f, 0f),
                        new Vector3(0.18f, 0.24f + (index % 3) * 0.04f, 1.36f),
                        Quaternion.identity,
                        false,
                        true);
                }
                AddBoxCollider(root, new Vector3(0f, 0.12f, 0f), new Vector3(2.6f, 0.24f, 1.55f));
                RegisterDamageable(root, DamageableKind.Farm, 1.5f, 62f, 1.18f, 1.25f, 0.76f, 0.42f);
            }
        }

        private void BuildTrees()
        {
            for (var index = 0; index < TreeCount; index++)
            {
                var highlandTree = index >= 80;
                var localIndex = highlandTree ? index - 80 : index;
                var column = localIndex % (highlandTree ? 4 : 10);
                var row = localIndex / (highlandTree ? 4 : 10);
                var jitterX = ((index * 37) % 11 - 5) * 0.22f;
                var jitterZ = ((index * 53) % 13 - 6) * 0.18f;
                var position = highlandTree
                    ? new Vector3(-48f + column * 5.8f + jitterX, 0f, 8f + row * 5.4f + jitterZ)
                    : new Vector3(-29f + column * 3.8f + jitterX, 0f, 4f + row * 4f + jitterZ);
                position = FindPlantableTreePosition(position, index);
                var yaw = Quaternion.Euler(0f, index * 31f, 0f);
                var root = CreateObjectRoot($"Tree {index + 1:000}", vegetationRoot, position, yaw);
                var scale = 0.78f + (index % 7) * 0.055f;
                CreatePart("Trunk", root, cylinderMesh, trunkMaterial, new Vector3(0f, 0.5f * scale, 0f), new Vector3(0.18f * scale, 1f * scale, 0.18f * scale), Quaternion.identity, false, true);
                CreatePart("Crown Lower", root, coneMesh, foliageMaterial, new Vector3(0f, 1.2f * scale, 0f), new Vector3(0.72f * scale, 1.05f * scale, 0.72f * scale), Quaternion.identity, false, true);
                CreatePart("Crown Upper", root, coneMesh, foliageMaterial, new Vector3(0f, 1.68f * scale, 0f), new Vector3(0.5f * scale, 0.8f * scale, 0.5f * scale), Quaternion.identity, false, true);
                AddCapsuleCollider(root, new Vector3(0f, 0.88f * scale, 0f), 0.34f * scale, 1.76f * scale);
                RegisterDamageable(root, DamageableKind.Tree, 0.72f * scale, 54f, 1.3f, 0.3f, 0.82f, 1.16f);
            }
        }

        private void BuildInfrastructure()
        {
            for (var index = 0; index < InfrastructureCount; index++)
            {
                var angle = index / (float)InfrastructureCount * Mathf.PI * 2f + 0.21f;
                var radius = 0.72f + (index % 3) * 0.04f;
                var position = new Vector3(
                    Mathf.Cos(angle) * TownHalfWidth * radius,
                    0f,
                    Mathf.Sin(angle) * TownHalfDepth * radius - 2f);
                position = TerrainPoint(position, 0.08f);
                var root = CreateObjectRoot($"Infrastructure {index + 1:00}", buildingsRoot, position, Quaternion.identity);
                CreatePart("Pole", root, cylinderMesh, infrastructureMaterial, new Vector3(0f, 1.05f, 0f), new Vector3(0.11f, 2.1f, 0.11f), Quaternion.identity, false, true);
                CreatePart("Crossbar", root, boxMesh, infrastructureMaterial, new Vector3(0f, 1.78f, 0f), new Vector3(0.82f, 0.1f, 0.12f), Quaternion.identity, false, true);
                CreatePart("Lamp", root, octahedronMesh, windowMaterial, new Vector3(0f, 1.98f, 0f), new Vector3(0.16f, 0.18f, 0.16f), Quaternion.identity, false, true);
                AddCapsuleCollider(root, new Vector3(0f, 1.05f, 0f), 0.18f, 2.1f);
                RegisterDamageable(root, DamageableKind.Infrastructure, 0.62f, 76f, 0.38f, 0.9f, 0.66f, 1.08f);
            }
        }

        private void BuildVehicles()
        {
            var route = CreateVehicleRoute();
            for (var index = 0; index < VehicleCount; index++)
            {
                var startIndex = Mathf.FloorToInt(index * route.Length / (float)VehicleCount) % route.Length;
                var nextIndex = (startIndex + 1) % route.Length;
                var root = CreateObjectRoot(
                    $"Vehicle {index + 1:00}",
                    buildingsRoot,
                    route[startIndex],
                    VehicleRoadRotation(route[nextIndex] - route[startIndex]));
                CreatePart("Body", root, boxMesh, carMaterials[index % carMaterials.Length], new Vector3(0f, 0.24f, 0f), new Vector3(1.15f, 0.34f, 0.62f), Quaternion.identity, false, true);
                CreatePart("Cabin", root, boxMesh, windowMaterial, new Vector3(-0.08f, 0.5f, 0f), new Vector3(0.58f, 0.3f, 0.52f), Quaternion.identity, false, true);
                CreateWheel(root, new Vector3(-0.42f, 0.1f, -0.33f));
                CreateWheel(root, new Vector3(0.42f, 0.1f, -0.33f));
                CreateWheel(root, new Vector3(-0.42f, 0.1f, 0.33f));
                CreateWheel(root, new Vector3(0.42f, 0.1f, 0.33f));
                AddBoxCollider(root, new Vector3(0f, 0.3f, 0f), new Vector3(1.15f, 0.6f, 0.68f));
                var state = RegisterDamageable(root, DamageableKind.Vehicle, 0.82f, 68f, 0.6f, 1.05f, 0.46f, 0.84f);
                state.VehicleRouteTargetIndex = nextIndex;
                state.VehicleSpeed = 2.25f + (index % 3) * 0.2f;
                vehicles.Add(new VehicleRuntime(this, state, route, nextIndex, state.VehicleSpeed, terrainGenerator));
            }
        }

        private void BuildResidents()
        {
            for (var index = 0; index < ResidentCount; index++)
            {
                var angle = index * GoldenAngle + 0.37f;
                var radius = Mathf.Sqrt((index + 0.5f) / ResidentCount) * 0.58f;
                var home = new Vector3(
                    Mathf.Cos(angle) * TownHalfWidth * radius,
                    0f,
                    Mathf.Sin(angle) * TownHalfDepth * radius - 2f);
                home = TerrainPoint(home, 0.09f);
                CreateResident(index, home);
            }
        }

        private void CreateResident(int index, Vector3 home)
        {
            var root = CreateObjectRoot($"Resident {index + 1:00}", residentsRoot, home, Quaternion.Euler(0f, index * 53f, 0f));
            var bodyRenderer = CreatePart("Body", root, boxMesh, shirtMaterials[index % shirtMaterials.Length], new Vector3(0f, 0.46f, 0f), new Vector3(0.24f, 0.3f, 0.14f), Quaternion.identity, false, true);
            var headRenderer = CreatePart("Head", root, octahedronMesh, skinMaterial, new Vector3(0f, 0.72f, 0f), new Vector3(0.15f, 0.16f, 0.15f), Quaternion.identity, false, true);
            var leftArm = CreateLimb(root, "Left Arm", new Vector3(-0.17f, 0.57f, 0f), skinMaterial, 0.25f, 0.065f);
            var rightArm = CreateLimb(root, "Right Arm", new Vector3(0.17f, 0.57f, 0f), skinMaterial, 0.25f, 0.065f);
            var leftLeg = CreateLimb(root, "Left Leg", new Vector3(-0.075f, 0.33f, 0f), pantsMaterial, 0.29f, 0.075f);
            var rightLeg = CreateLimb(root, "Right Leg", new Vector3(0.075f, 0.33f, 0f), pantsMaterial, 0.29f, 0.075f);
            AddCapsuleCollider(root, new Vector3(0f, 0.4f, 0f), 0.16f, 0.78f);
            var agent = root.gameObject.AddComponent<ResidentAgent>();
            agent.Configure(
                index + 1,
                root.position,
                TownHalfWidth,
                TownHalfDepth,
                bodyRenderer.transform,
                headRenderer.transform,
                leftArm,
                rightArm,
                leftLeg,
                rightLeg,
                root.GetComponentsInChildren<Renderer>(true));
            agent.SetTerrain(terrainGenerator);
            residents.Add(agent);
        }

        private Transform CreateLimb(Transform parent, string name, Vector3 localPosition, Material material, float length, float thickness)
        {
            var pivot = CreateContainer(name, parent);
            pivot.localPosition = localPosition;
            CreatePart(
                "Mesh",
                pivot,
                cylinderMesh,
                material,
                new Vector3(0f, -length * 0.5f, 0f),
                new Vector3(thickness, length, thickness),
                Quaternion.identity,
                false,
                true);
            return pivot;
        }

        private void CreateWheel(Transform root, Vector3 localPosition)
        {
            CreatePart("Wheel", root, cylinderMesh, tireMaterial, localPosition, new Vector3(0.18f, 0.12f, 0.18f), Quaternion.Euler(0f, 0f, 90f), false, true);
        }

        private Vector3[] CreateVehicleRoute()
        {
            var route = new[]
            {
                new Vector3(-15f, 0f, -11f),
                new Vector3(-7f, 0f, -13f),
                new Vector3(3f, 0f, -12f),
                new Vector3(13f, 0f, -9f),
                new Vector3(15f, 0f, -1f),
                new Vector3(13f, 0f, 7f),
                new Vector3(4f, 0f, 10f),
                new Vector3(-7f, 0f, 9f),
                new Vector3(-15f, 0f, 5f),
                new Vector3(-16f, 0f, -4f)
            };
            for (var index = 0; index < route.Length; index++) route[index] = TerrainPoint(route[index], 0.13f);
            return route;
        }

        private DamageableState RegisterDamageable(
            Transform root,
            DamageableKind kind,
            float radius,
            float health,
            float fireVulnerability,
            float floodVulnerability,
            float snowVulnerability,
            float windVulnerability)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var baseColors = new Color[renderers.Length];
            for (var index = 0; index < renderers.Length; index++) baseColors[index] = ReadMaterialColor(renderers[index].sharedMaterial);
            var state = new DamageableState
            {
                Id = nextDamageableId++,
                Kind = kind,
                Root = root,
                Renderers = renderers,
                BaseColors = baseColors,
                InitialScale = root.localScale,
                Radius = radius,
                MaxHealth = health,
                Health = health,
                FireVulnerability = fireVulnerability,
                FloodVulnerability = floodVulnerability,
                SnowVulnerability = snowVulnerability,
                WindVulnerability = windVulnerability,
                VisualStage = DamageVisualStage.Healthy
            };
            CaptureInitialPose(state);
            damageables.Add(state);
            return state;
        }

        private void RefreshBaseColors(DamageableState record)
        {
            if (record.Renderers == null) return;
            if (record.BaseColors == null || record.BaseColors.Length != record.Renderers.Length)
            {
                record.BaseColors = new Color[record.Renderers.Length];
            }
            for (var index = 0; index < record.Renderers.Length; index++)
            {
                record.BaseColors[index] = ReadMaterialColor(record.Renderers[index] != null ? record.Renderers[index].sharedMaterial : null);
            }
        }

        private void AddStaticRoad(string name, Vector3 position, Vector3 scale)
        {
            CreatePart(name, environmentRoot, boxMesh, roadMaterial, position, scale, Quaternion.identity, true, false);
        }

        private Vector3 TerrainPoint(Vector3 position, float verticalOffset)
        {
            if (terrainGenerator != null)
            {
                if (terrainGenerator.TryProjectToSurface(position, out var surfacePoint, out _))
                {
                    position.y = surfacePoint.y + verticalOffset;
                }
                else
                {
                    position.y = terrainGenerator.SampleHeight(position) + verticalOffset;
                }
            }
            return position;
        }

        private Vector3 FindPlantableTreePosition(Vector3 candidate, int treeIndex)
        {
            if (terrainGenerator == null) return candidate;
            const int attempts = 56;
            const float goldenAngle = 2.399963f;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var radius = attempt == 0 ? 0f : Mathf.Sqrt(attempt) * 1.18f;
                var angle = treeIndex * 0.73f + attempt * goldenAngle;
                var point = candidate + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (Mathf.Abs(point.x) > HalfWidth * 0.88f || Mathf.Abs(point.z) > HalfDepth * 0.88f) continue;
                terrainGenerator.TryProjectToSurface(point, out var surfacePoint, out var surfaceNormal);
                if (terrainGenerator.IsWater(surfacePoint)
                    || surfacePoint.y < terrainGenerator.WaterLevel + 0.45f
                    || surfacePoint.y > terrainGenerator.SnowLine - 0.35f
                    || surfaceNormal.y < 0.82f)
                {
                    continue;
                }

                surfacePoint.y += 0.035f;
                return surfacePoint;
            }

            return TerrainPoint(new Vector3(-18f + treeIndex % 8 * 3.2f, 0f, 7f + treeIndex % 5 * 3.4f), 0.035f);
        }

        public float SnapEditableSceneToTerrain()
        {
            if (terrainGenerator == null) return 0f;
            var maxError = 0f;
            for (var index = 0; index < damageables.Count; index++)
            {
                var record = damageables[index];
                if (record == null || record.Root == null) continue;
                var gameplayPosition = GetGameplayPosition(record);
                var before = gameplayPosition.y;
                var position = TerrainPoint(gameplayPosition, record.Kind == DamageableKind.Farm ? 0.12f : 0.08f);
                SetGameplayPosition(record.Root, position);
                if (record.Kind == DamageableKind.Tree)
                {
                    var rotation = GetGameplayRotation(record.Root);
                    SetGameplayRotation(record.Root, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
                }
                CaptureInitialPose(record);
                maxError = Mathf.Max(maxError, Mathf.Abs(before - position.y));
            }

            for (var index = 0; index < residents.Count; index++)
            {
                var resident = residents[index];
                if (resident == null) continue;
                resident.SetTerrain(terrainGenerator);
                maxError = Mathf.Max(maxError, resident.SnapToTerrain());
            }

            var props = environmentRoot != null ? environmentRoot.Find("AngeProps") : null;
            if (props != null)
            {
                for (var index = 0; index < props.childCount; index++)
                {
                    var item = props.GetChild(index);
                    var before = item.position.y;
                    var waterAsset = item.name.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0
                        || item.name.IndexOf("Dock", StringComparison.OrdinalIgnoreCase) >= 0
                        || item.name.IndexOf("Dam", StringComparison.OrdinalIgnoreCase) >= 0
                        || item.name.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) >= 0
                        || item.name.IndexOf("Water Wheel", StringComparison.OrdinalIgnoreCase) >= 0;
                    var position = item.position;
                    var desiredBottom = waterAsset
                        ? terrainGenerator.WaterLevel + 0.02f
                        : TerrainPoint(position, 0.035f).y;
                    position.y = desiredBottom;
                    item.position = position;
                    var renderers = item.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length > 0)
                    {
                        var bounds = renderers[0].bounds;
                        for (var rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
                        {
                            if (renderers[rendererIndex] != null) bounds.Encapsulate(renderers[rendererIndex].bounds);
                        }
                        var immersion = item.name.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.16f : 0f;
                        item.position += Vector3.up * (desiredBottom - immersion - bounds.min.y);
                    }
                    if (!waterAsset)
                    {
                        item.rotation = Quaternion.Euler(0f, item.eulerAngles.y, 0f);
                    }
                    maxError = Mathf.Max(maxError, Mathf.Abs(before - position.y));
                }
            }
            return maxError;
        }

        private static Transform CreateContainer(string name, Transform parent)
        {
            var transform = new GameObject(name).transform;
            transform.SetParent(parent, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            return transform;
        }

        private static Transform CreateObjectRoot(string name, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var root = CreateContainer(name, parent);
            root.localPosition = localPosition;
            root.localRotation = localRotation;
            return root;
        }

        private static MeshRenderer CreatePart(
            string name,
            Transform parent,
            Mesh mesh,
            Material material,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            bool isStatic,
            bool castShadows)
        {
            var gameObject = new GameObject(name);
            gameObject.isStatic = isStatic;
            var part = gameObject.transform;
            part.SetParent(parent, false);
            part.localPosition = localPosition;
            part.localRotation = localRotation;
            part.localScale = localScale;
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
            return renderer;
        }

        private static void AddBoxCollider(Transform root, Vector3 center, Vector3 size)
        {
            var collider = root.GetComponent<BoxCollider>();
            if (!collider) collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }

        private static void AddCapsuleCollider(Transform root, Vector3 center, float radius, float height)
        {
            var collider = root.GetComponent<CapsuleCollider>();
            if (!collider) collider = root.gameObject.AddComponent<CapsuleCollider>();
            collider.center = center;
            collider.direction = 1;
            collider.radius = radius;
            collider.height = Mathf.Max(height, radius * 2f);
        }

        private void EnsureCollections()
        {
            damageables ??= new List<DamageableState>(
                BuildingCount + TreeCount + FarmCount + VehicleCount + InfrastructureCount);
            residents ??= new List<ResidentAgent>(ResidentCount);
        }

        private void RebuildVehicleRuntime()
        {
            vehicles.Clear();
            var route = CreateVehicleRoute();
            for (var index = 0; index < damageables.Count; index++)
            {
                var state = damageables[index];
                if (state.Kind != DamageableKind.Vehicle || state.Root == null)
                {
                    continue;
                }

                var targetIndex = state.VehicleRouteTargetIndex;
                if (targetIndex < 0 || targetIndex >= route.Length)
                {
                    var nearestIndex = 0;
                var nearestDistance = float.PositiveInfinity;
                for (var routeIndex = 0; routeIndex < route.Length; routeIndex++)
                {
                    var distance = (route[routeIndex] - state.InitialPosition).sqrMagnitude;
                        if (distance >= nearestDistance) continue;
                        nearestDistance = distance;
                        nearestIndex = routeIndex;
                    }
                    targetIndex = (nearestIndex + 1) % route.Length;
                    state.VehicleRouteTargetIndex = targetIndex;
                }

                if (state.VehicleSpeed <= 0f)
                {
                    state.VehicleSpeed = 2.25f + (vehicles.Count % 3) * 0.2f;
                }
                var direction = route[targetIndex] - GetGameplayPosition(state);
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f)
                {
                    SetGameplayRotation(state.Root, VehicleRoadRotation(direction));
                    CaptureInitialPose(state);
                }
                vehicles.Add(new VehicleRuntime(this, state, route, targetIndex, state.VehicleSpeed, terrainGenerator));
            }
        }

        private void EnsureInitialPose(DamageableState record)
        {
            if (record == null || record.Root == null)
            {
                return;
            }

            if (!record.HasLocalTransformBaseline)
            {
                // Existing serialized scenes predate local baseline fields. At
                // Awake they still have their authored, unscaled local pose,
                // so migrate once before MrTabletopExperience applies scale.
                record.InitialLocalPosition = record.Root.localPosition;
                record.InitialLocalRotation = record.Root.localRotation;
                record.HasLocalTransformBaseline = true;
            }

            if (record.InitialScale == Vector3.zero)
            {
                record.InitialScale = record.Root.localScale;
            }
        }

        private void CaptureInitialPose(DamageableState record)
        {
            if (record == null || record.Root == null)
            {
                return;
            }

            record.InitialPosition = GetGameplayPosition(record.Root);
            record.InitialRotation = GetGameplayRotation(record.Root);
            record.InitialLocalPosition = record.Root.localPosition;
            record.InitialLocalRotation = record.Root.localRotation;
            record.InitialScale = record.Root.localScale;
            record.HasLocalTransformBaseline = true;
        }

        private Transform ResolveGameplayRoot()
        {
            if (gameplayRoot != null)
            {
                return gameplayRoot;
            }

            var roots = new[] { environmentRoot, buildingsRoot, vegetationRoot, residentsRoot };
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                for (var current = roots[rootIndex]; current != null; current = current.parent)
                {
                    if (current.name != "Weather Tabletop Island")
                    {
                        continue;
                    }

                    gameplayRoot = current;
                    return gameplayRoot;
                }
            }

            return null;
        }

        private void LogResetVisualRecovery()
        {
            var restored = 0;
            var healthyVisuals = 0;
            var activeRenderers = 0;
            for (var index = 0; index < damageables.Count; index++)
            {
                var record = damageables[index];
                if (record == null || record.Root == null)
                {
                    continue;
                }

                restored++;
                var binding = record.Root.GetComponent<AngeVisualBinding>();
                if (binding != null && binding.VisualRoot != null && binding.VisualRoot.gameObject.activeInHierarchy)
                {
                    healthyVisuals++;
                }

                var renderers = record.Renderers ?? Array.Empty<Renderer>();
                for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    {
                        activeRenderers++;
                    }
                }
            }

            var root = ResolveGameplayRoot();
            Debug.Log(
                $"WEATHER_MANAGER_RESET_VISUALS:records={damageables.Count}:restored={restored}:"
                + $"healthyVisuals={healthyVisuals}:activeRenderers={activeRenderers}:"
                + $"gameplayRoot={(root != null ? root.name : "none")}:"
                + $"rootScale={(root != null ? root.lossyScale.ToString("F3") : "1.000")}");
        }

        private static Quaternion VehicleRoadRotation(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up) * VehicleForwardCorrection
                : Quaternion.identity;
        }

        private static void DestroyWorldObject(GameObject target)
        {
            if (target == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(target);
                return;
            }
#endif
            Destroy(target);
        }

        internal Light[] GetNightLights(bool createIfMissing)
        {
            EnsureNightLights(createIfMissing);
            return nightLights ?? Array.Empty<Light>();
        }

        private void EnsureNightLights(bool createIfMissing)
        {
            if (nightLights != null && Array.Exists(nightLights, light => light != null))
            {
                return;
            }

            if (nightLightsRoot == null && environmentRoot != null)
            {
                nightLightsRoot = environmentRoot.Find("NightLights");
            }
            if (nightLightsRoot != null)
            {
                nightLights = nightLightsRoot.GetComponentsInChildren<Light>(true);
                if (nightLights.Length > 0 || !createIfMissing)
                {
                    return;
                }
            }
            if (!createIfMissing || environmentRoot == null)
            {
                nightLights = Array.Empty<Light>();
                return;
            }

            nightLightsRoot = CreateContainer("NightLights", environmentRoot);
            var positions = new[]
            {
                new Vector3(-8f, 2.6f, -5f),
                new Vector3(-2.5f, 2.6f, -6f),
                new Vector3(3.5f, 2.6f, -4f),
                new Vector3(8f, 2.6f, -1f),
                new Vector3(-7f, 2.6f, 4f),
                new Vector3(0f, 2.6f, 5f),
                new Vector3(7f, 2.6f, 5f),
                new Vector3(11f, 2.6f, 2f)
            };
            nightLights = new Light[positions.Length];
            for (var index = 0; index < positions.Length; index++)
            {
                if (terrainGenerator != null)
                {
                    positions[index].y = terrainGenerator.SampleHeight(positions[index]) + 2.45f;
                }
                var lightObject = new GameObject($"StreetLight_{index:00}");
                lightObject.transform.SetParent(nightLightsRoot, false);
                lightObject.transform.localPosition = positions[index];
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 5.2f;
                light.color = new Color(1f, 0.67f, 0.3f);
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                light.enabled = false;
                nightLights[index] = light;
            }
        }

        private void EnsureResources()
        {
            if (HasBakedScene)
            {
                RecoverBakedResourceReferences(false);
            }
            if (HasCompleteResourceSet()) return;
            boxMesh = Own(CreateBoxMesh());
            cylinderMesh = Own(CreateCylinderMesh(8));
            coneMesh = Own(CreateConeMesh(8));
            octahedronMesh = Own(CreateOctahedronMesh());
            roofMesh = Own(CreateRoofMesh());
            discMesh = Own(CreateDiscMesh(40));

            landMaterial = Own(CreateMaterial("Land", new Color(0.37f, 0.68f, 0.36f)));
            cliffMaterial = Own(CreateMaterial("Cliff", new Color(0.36f, 0.29f, 0.23f)));
            sandMaterial = Own(CreateMaterial("Sand", new Color(0.84f, 0.72f, 0.47f)));
            waterMaterial = Own(CreateMaterial("Water", new Color(0.16f, 0.52f, 0.72f)));
            roadMaterial = Own(CreateMaterial("Road", new Color(0.25f, 0.29f, 0.3f)));
            stoneMaterial = Own(CreateMaterial("Stone", new Color(0.38f, 0.43f, 0.43f)));
            trunkMaterial = Own(CreateMaterial("Trunk", new Color(0.35f, 0.22f, 0.12f)));
            foliageMaterial = Own(CreateMaterial("Foliage", new Color(0.28f, 0.72f, 0.31f)));
            farmSoilMaterial = Own(CreateMaterial("Farm Soil", new Color(0.48f, 0.32f, 0.18f)));
            cropMaterial = Own(CreateMaterial("Crops", new Color(0.58f, 0.82f, 0.29f)));
            infrastructureMaterial = Own(CreateMaterial("Infrastructure", new Color(0.32f, 0.38f, 0.42f)));
            windowMaterial = Own(CreateMaterial("Windows", new Color(0.38f, 0.75f, 0.86f), new Color(0.08f, 0.24f, 0.31f)));
            doorMaterial = Own(CreateMaterial("Doors", new Color(0.34f, 0.19f, 0.11f)));
            skinMaterial = Own(CreateMaterial("Skin", new Color(0.93f, 0.69f, 0.52f)));
            pantsMaterial = Own(CreateMaterial("Pants", new Color(0.15f, 0.24f, 0.34f)));
            tireMaterial = Own(CreateMaterial("Tires", new Color(0.08f, 0.085f, 0.09f)));

            wallMaterials = CreateMaterialArray("Wall", new[]
            {
                new Color(0.82f, 0.46f, 0.36f),
                new Color(0.32f, 0.62f, 0.68f),
                new Color(0.92f, 0.74f, 0.36f),
                new Color(0.57f, 0.49f, 0.72f),
                new Color(0.76f, 0.66f, 0.54f),
                new Color(0.45f, 0.7f, 0.5f)
            });
            roofMaterials = CreateMaterialArray("Roof", new[]
            {
                new Color(0.48f, 0.17f, 0.13f),
                new Color(0.22f, 0.32f, 0.4f),
                new Color(0.62f, 0.3f, 0.14f),
                new Color(0.38f, 0.24f, 0.42f),
                new Color(0.24f, 0.42f, 0.38f)
            });
            carMaterials = CreateMaterialArray("Vehicle", new[]
            {
                new Color(0.82f, 0.18f, 0.15f),
                new Color(0.12f, 0.42f, 0.72f),
                new Color(0.92f, 0.64f, 0.12f),
                new Color(0.24f, 0.7f, 0.45f),
                new Color(0.68f, 0.27f, 0.62f),
                new Color(0.86f, 0.86f, 0.82f)
            });
            shirtMaterials = CreateMaterialArray("Resident Shirt", new[]
            {
                new Color(0.88f, 0.25f, 0.2f),
                new Color(0.18f, 0.52f, 0.82f),
                new Color(0.95f, 0.7f, 0.18f),
                new Color(0.28f, 0.7f, 0.4f),
                new Color(0.64f, 0.34f, 0.72f),
                new Color(0.88f, 0.48f, 0.24f)
            });
        }

        private bool HasCompleteResourceSet()
        {
            return boxMesh != null
                && cylinderMesh != null
                && coneMesh != null
                && octahedronMesh != null
                && roofMesh != null
                && discMesh != null
                && landMaterial != null
                && cliffMaterial != null
                && sandMaterial != null
                && waterMaterial != null
                && roadMaterial != null
                && stoneMaterial != null
                && trunkMaterial != null
                && foliageMaterial != null
                && farmSoilMaterial != null
                && cropMaterial != null
                && infrastructureMaterial != null
                && windowMaterial != null
                && doorMaterial != null
                && skinMaterial != null
                && pantsMaterial != null
                && tireMaterial != null
                && AreMaterialsValid(wallMaterials, 6)
                && AreMaterialsValid(roofMaterials, 5)
                && AreMaterialsValid(carMaterials, 6)
                && AreMaterialsValid(shirtMaterials, 6);
        }

        private static bool AreMaterialsValid(Material[] materials, int expectedCount)
        {
            if (materials == null || materials.Length != expectedCount) return false;
            for (var index = 0; index < materials.Length; index++)
            {
                if (materials[index] == null) return false;
            }
            return true;
        }

        private void RecoverBakedResourceReferences(bool force)
        {
            if (!HasBakedScene || (!force && HasCompleteResourceSet()))
            {
                return;
            }

            var roots = new[] { environmentRoot, buildingsRoot, vegetationRoot, residentsRoot };
            var materialsByName = new Dictionary<string, Material>(StringComparer.Ordinal);
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];
                if (root == null) continue;
                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                for (var index = 0; index < filters.Length; index++)
                {
                    var mesh = filters[index].sharedMesh;
                    if (mesh == null) continue;
                    switch (mesh.name)
                    {
                        case "WM Shared Box": boxMesh = mesh; break;
                        case "WM Shared Cylinder": cylinderMesh = mesh; break;
                        case "WM Shared Cone": coneMesh = mesh; break;
                        case "WM Shared Octahedron": octahedronMesh = mesh; break;
                        case "WM Shared Roof": roofMesh = mesh; break;
                        case "WM Shared Disc": discMesh = mesh; break;
                    }
                }

                var rootRenderers = root.GetComponentsInChildren<Renderer>(true);
                for (var index = 0; index < rootRenderers.Length; index++)
                {
                    var sharedMaterials = rootRenderers[index].sharedMaterials;
                    for (var materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                    {
                        var material = sharedMaterials[materialIndex];
                        if (material != null && !materialsByName.ContainsKey(material.name))
                        {
                            materialsByName.Add(material.name, material);
                        }
                    }
                }
            }

            AssignMaterial(materialsByName, "Land", ref landMaterial);
            AssignMaterial(materialsByName, "Cliff", ref cliffMaterial);
            AssignMaterial(materialsByName, "Sand", ref sandMaterial);
            AssignMaterial(materialsByName, "Water", ref waterMaterial);
            AssignMaterial(materialsByName, "Road", ref roadMaterial);
            AssignMaterial(materialsByName, "Stone", ref stoneMaterial);
            AssignMaterial(materialsByName, "Trunk", ref trunkMaterial);
            AssignMaterial(materialsByName, "Foliage", ref foliageMaterial);
            AssignMaterial(materialsByName, "Farm Soil", ref farmSoilMaterial);
            AssignMaterial(materialsByName, "Crops", ref cropMaterial);
            AssignMaterial(materialsByName, "Infrastructure", ref infrastructureMaterial);
            AssignMaterial(materialsByName, "Windows", ref windowMaterial);
            AssignMaterial(materialsByName, "Doors", ref doorMaterial);
            AssignMaterial(materialsByName, "Skin", ref skinMaterial);
            AssignMaterial(materialsByName, "Pants", ref pantsMaterial);
            AssignMaterial(materialsByName, "Tires", ref tireMaterial);
            wallMaterials = RecoverMaterialArray(materialsByName, "Wall", 6, wallMaterials);
            roofMaterials = RecoverMaterialArray(materialsByName, "Roof", 5, roofMaterials);
            carMaterials = RecoverMaterialArray(materialsByName, "Vehicle", 6, carMaterials);
            shirtMaterials = RecoverMaterialArray(materialsByName, "Resident Shirt", 6, shirtMaterials);
        }

        private static void AssignMaterial(
            IReadOnlyDictionary<string, Material> materials,
            string name,
            ref Material target)
        {
            if (materials.TryGetValue(name, out var material))
            {
                target = material;
            }
        }

        private static Material[] RecoverMaterialArray(
            IReadOnlyDictionary<string, Material> materials,
            string prefix,
            int count,
            Material[] current)
        {
            var result = current != null && current.Length == count ? current : new Material[count];
            for (var index = 0; index < count; index++)
            {
                if (materials.TryGetValue($"{prefix} {index + 1}", out var material))
                {
                    result[index] = material;
                }
            }
            return result;
        }

        private Material[] CreateMaterialArray(string prefix, Color[] colors)
        {
            var result = new Material[colors.Length];
            for (var index = 0; index < colors.Length; index++)
            {
                result[index] = Own(CreateMaterial($"{prefix} {index + 1}", colors[index]));
            }
            return result;
        }

        private static Material CreateMaterial(string name, Color color, Color? emission = null)
        {
            var baseMaterial = Resources.Load<Material>("WeatherManager/RuntimeLit");
            Material material;
            if (baseMaterial != null)
            {
                material = new Material(baseMaterial);
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        "Weather Manager runtime lit material is missing and no fallback shader is available.");
                }
                material = new Material(shader);
            }

            material.name = name;
            material.enableInstancing = true;
            SetMaterialColor(material, color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.18f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (emission.HasValue && material.HasProperty(EmissionColorId))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, emission.Value);
            }
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
            if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
        }

        private static Color ReadMaterialColor(Material material)
        {
            if (material == null) return Color.white;
            if (material.HasProperty(BaseColorId)) return material.GetColor(BaseColorId);
            if (material.HasProperty(ColorId)) return material.GetColor(ColorId);
            return Color.white;
        }

        private T Own<T>(T resource) where T : UnityEngine.Object
        {
            if (resource is Mesh mesh) ownedMeshes.Add(mesh);
            else if (resource is Material material) ownedMaterials.Add(material);
            return resource;
        }

        private void ClearGeneratedWorld()
        {
            if (generatedRoot != null && ownsGeneratedRoot)
            {
                generatedRoot.gameObject.SetActive(false);
                DestroyRuntimeObject(generatedRoot.gameObject);
            }
            generatedRoot = null;
            ownsGeneratedRoot = false;
            if (!bakedScene)
            {
                environmentRoot = null;
                buildingsRoot = null;
                vegetationRoot = null;
                residentsRoot = null;
                nightLightsRoot = null;
                nightLights = Array.Empty<Light>();
            }
        }

        private static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (var index = root.childCount - 1; index >= 0; index--)
            {
                DestroyRuntimeObject(root.GetChild(index).gameObject);
            }
        }

        private void OnDestroy()
        {
            if (generatedRoot != null && ownsGeneratedRoot) DestroyRuntimeObject(generatedRoot.gameObject);
            for (var index = 0; index < ownedMaterials.Count; index++) DestroyRuntimeObject(ownedMaterials[index]);
            for (var index = 0; index < ownedMeshes.Count; index++) DestroyRuntimeObject(ownedMeshes[index]);
            ownedMaterials.Clear();
            ownedMeshes.Clear();
        }

        private static void DestroyRuntimeObject(UnityEngine.Object target)
        {
            if (target == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.EditorUtility.IsPersistent(target)) return;
#endif
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static Mesh CreateBoxMesh()
        {
            var mesh = new Mesh { name = "WM Shared Box" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f)
            };
            mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11, 12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19, 20, 21, 22, 20, 22, 23
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateCylinderMesh(int segments)
        {
            var vertices = new List<Vector3>(segments * 4 + 2);
            var normals = new List<Vector3>(segments * 4 + 2);
            var triangles = new List<int>(segments * 12);
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (float)segments * Mathf.PI * 2f;
                var normal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices.Add(new Vector3(normal.x, -0.5f, normal.z));
                vertices.Add(new Vector3(normal.x, 0.5f, normal.z));
                normals.Add(normal);
                normals.Add(normal);
            }
            for (var index = 0; index < segments; index++)
            {
                var next = (index + 1) % segments;
                triangles.Add(index * 2);
                triangles.Add(next * 2);
                triangles.Add(index * 2 + 1);
                triangles.Add(next * 2);
                triangles.Add(next * 2 + 1);
                triangles.Add(index * 2 + 1);
            }

            var topCenter = vertices.Count;
            vertices.Add(new Vector3(0f, 0.5f, 0f));
            normals.Add(Vector3.up);
            var topStart = vertices.Count;
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle), 0.5f, Mathf.Sin(angle)));
                normals.Add(Vector3.up);
            }
            var bottomCenter = vertices.Count;
            vertices.Add(new Vector3(0f, -0.5f, 0f));
            normals.Add(Vector3.down);
            var bottomStart = vertices.Count;
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle), -0.5f, Mathf.Sin(angle)));
                normals.Add(Vector3.down);
            }
            for (var index = 0; index < segments; index++)
            {
                var next = (index + 1) % segments;
                triangles.Add(topCenter);
                triangles.Add(topStart + next);
                triangles.Add(topStart + index);
                triangles.Add(bottomCenter);
                triangles.Add(bottomStart + index);
                triangles.Add(bottomStart + next);
            }

            var mesh = new Mesh { name = "WM Shared Cylinder" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateConeMesh(int segments)
        {
            var vertices = new List<Vector3>(segments * 3 + 1);
            var triangles = new List<int>(segments * 6);
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (float)segments * Mathf.PI * 2f;
                var nextAngle = (index + 1) / (float)segments * Mathf.PI * 2f;
                var start = vertices.Count;
                vertices.Add(new Vector3(0f, 0.5f, 0f));
                vertices.Add(new Vector3(Mathf.Cos(angle), -0.5f, Mathf.Sin(angle)));
                vertices.Add(new Vector3(Mathf.Cos(nextAngle), -0.5f, Mathf.Sin(nextAngle)));
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }
            var center = vertices.Count;
            vertices.Add(new Vector3(0f, -0.5f, 0f));
            var ring = vertices.Count;
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle), -0.5f, Mathf.Sin(angle)));
            }
            for (var index = 0; index < segments; index++)
            {
                triangles.Add(center);
                triangles.Add(ring + index);
                triangles.Add(ring + (index + 1) % segments);
            }
            var mesh = new Mesh { name = "WM Shared Cone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateOctahedronMesh()
        {
            var mesh = new Mesh { name = "WM Shared Octahedron" };
            mesh.vertices = new[]
            {
                Vector3.up,
                Vector3.down,
                Vector3.left,
                Vector3.right,
                Vector3.forward,
                Vector3.back
            };
            mesh.triangles = new[]
            {
                0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4,
                1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateRoofMesh()
        {
            var mesh = new Mesh { name = "WM Shared Roof" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(0f, 0.5f, 0.5f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1,
                3, 4, 5,
                0, 3, 5, 0, 5, 2,
                1, 2, 5, 1, 5, 4,
                0, 1, 4, 0, 4, 3
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateDiscMesh(int segments)
        {
            var vertices = new Vector3[segments + 1];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            for (var index = 0; index < segments; index++)
            {
                var angle = index / (float)segments * Mathf.PI * 2f;
                vertices[index + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                normals[index + 1] = Vector3.up;
                var triangle = index * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = (index + 1) % segments + 1;
                triangles[triangle + 2] = index + 1;
            }
            var mesh = new Mesh { name = "WM Shared Disc" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
