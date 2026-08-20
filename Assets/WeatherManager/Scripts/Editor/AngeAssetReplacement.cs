using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherManager.Editor
{
    public static class AngeAssetReplacement
    {
        public readonly struct ReplacementReport
        {
            public ReplacementReport(int buildings, int trees, int props, int prefabs)
            {
                Buildings = buildings;
                Trees = trees;
                Props = props;
                Prefabs = prefabs;
            }

            public int Buildings { get; }
            public int Trees { get; }
            public int Props { get; }
            public int Prefabs { get; }
        }

        private sealed class AssetSpec
        {
            public AssetSpec(string name, string category, string assetId)
            {
                Name = name;
                Category = category;
                AssetId = assetId;
            }

            public string Name { get; }
            public string Category { get; }
            public string AssetId { get; }
            public string ModelPath => $"{AngeRoot}/{Category}/{Name}.glb";
            public string PrefabPath => $"{PrefabRoot}/{Category}/{Name}.prefab";
        }

        private const string AngeRoot = "Assets/WeatherManager/Art/Ange";
        private const string PrefabRoot = AngeRoot + "/Prefabs";

        private static readonly AssetSpec[] BuildingSpecs =
        {
            new AssetSpec("Cabin", "Buildings", "ca47c31b-932b-476c-9c1a-1224d8296cec"),
            new AssetSpec("StoneCottage", "Buildings", "6c7e4d96-d7d7-42b6-b2a3-a3a831275a6a"),
            new AssetSpec("SimpleHouse", "Buildings", "09eac09b-a931-4c50-88a9-883778bb8ea2"),
            new AssetSpec("Lodge", "Buildings", "82e31b3b-8552-4f89-a0da-28e921dce506"),
            new AssetSpec("RedBarn", "Buildings", "0999493d-ec7b-4f85-a28b-a94869748423")
        };

        private static readonly AssetSpec[] VegetationSpecs =
        {
            new AssetSpec("Pine", "Vegetation", "d199eb4d-91b2-499e-9c59-de018dd4cb6c"),
            new AssetSpec("ConeTree", "Vegetation", "e7b51141-7c90-438f-aa46-0def76673de0"),
            new AssetSpec("FacetedTree", "Vegetation", "c77bb4c4-50d2-4a6d-b040-62b1f3b3b741")
        };

        private static readonly AssetSpec[] PropSpecs =
        {
            new AssetSpec("Bench", "Props", "6cf6a035-0171-416e-83b5-318c63400169"),
            new AssetSpec("Cart", "Props", "7b41893f-94be-4889-9a68-b45c739ccc76"),
            new AssetSpec("Well", "Props", "a90005fb-f318-47b0-8c2a-f68854c9964c"),
            new AssetSpec("Lamp", "Props", "4d8ef258-aa02-42e4-9bd2-770134f8f731"),
            new AssetSpec("Fence", "Props", "824d3c8b-ed69-4844-bf1c-b93fbaa029ac"),
            new AssetSpec("Barrel", "Props", "ee018247-b63f-402e-87e3-072ee3f05d73"),
            new AssetSpec("HayBale", "Props", "d4a61384-02be-4ad5-88ed-46ca231a2769"),
            new AssetSpec("Signpost", "Props", "9b4ece76-0988-45ad-aa1c-a66de9a78a47"),
            new AssetSpec("FireHydrant", "Props", "8e4b507e-7bc6-4e6f-93b7-caa6cc87e736"),
            new AssetSpec("Rowboat", "Props", "aa610a8c-af7a-46d9-93a8-0a26969f87e1")
        };

        private static readonly AssetSpec[] LandmarkSpecs =
        {
            new AssetSpec("Windmill", "Landmarks", "3f90ba9e-eb51-4da3-a954-749e9217fd7b"),
            new AssetSpec("WaterWheel", "Landmarks", "ed00a63d-73ac-478e-b3ff-4ec38b8da340"),
            new AssetSpec("Bridge", "Landmarks", "8137a863-2cad-46c3-92c7-5812af6300fe"),
            new AssetSpec("Gazebo", "Landmarks", "5e19b658-fc3a-46f8-9059-b1b52d559067")
        };

        private static readonly AssetSpec[] NatureSpecs =
        {
            new AssetSpec("RockCluster", "Nature", "a17e0aba-5e4b-48f7-89af-2527dded140b"),
            new AssetSpec("MossRock", "Nature", "f8e3ba18-243d-4dcd-91d9-12b09d723070"),
            new AssetSpec("Flowers", "Nature", "3d02fccd-4661-4a8b-8259-723a3fa9d994"),
            new AssetSpec("Bush", "Nature", "8a645f20-6154-4887-a7b0-b7a6b13f49b7"),
            new AssetSpec("FallenTree", "Nature", "64b74960-389e-45f7-8ae4-274004bf71f1"),
            new AssetSpec("DeadTree", "Nature", "6a666ccc-87e7-4094-90f5-2d6a774a335d"),
            new AssetSpec("SplitRailFence", "Nature", "03bf0537-24ab-4abc-86cb-016a72348abc")
        };

        private static readonly AssetSpec[] DisasterSpecs =
        {
            new AssetSpec("RuinedShed", "Disaster", "6bc28bfb-1269-470a-b9dc-205610737a42"),
            new AssetSpec("Stump", "Disaster", "ac911bfc-b8b2-4349-bc8f-c2ce2def111e"),
            new AssetSpec("WoodDebris", "Disaster", "c7e2f587-326f-4da7-9011-d2e8448b2b22")
        };

        private static readonly AssetSpec[] VehicleSpecs =
        {
            new AssetSpec("CompactMint", "Vehicles", "d4cf0f97-6720-46d2-8052-d07282792ec3"),
            new AssetSpec("BlueToyCar", "Vehicles", "0051abff-8e82-4bc7-af8b-2931e6439e1f"),
            new AssetSpec("YellowBlockSedan", "Vehicles", "ed5d3eb6-602b-4b96-accf-ddfe1aef5341"),
            new AssetSpec("WreckedCar", "Vehicles", "e0af7468-03d4-4e8d-b750-28c03c7c0374"),
            new AssetSpec("MiningTruck", "Vehicles", "5233d8c7-93a6-48f3-b95f-0493c32fdf60")
        };

        private static readonly AssetSpec[] FarmSpecs =
        {
            new AssetSpec("RustSilo", "Farm", "d08f7b9d-c693-475a-9478-742255f5e0b1"),
            new AssetSpec("Scarecrow", "Farm", "69d0b7db-7c59-4d9c-af97-96c499856615"),
            new AssetSpec("GrassClump", "Farm", "76a50fd8-d02b-4f9f-b3d8-f7db997402c9"),
            new AssetSpec("StorageShed", "Farm", "41b0b152-7c37-4f17-bd98-68d4f1a9b55a")
        };

        private static readonly AssetSpec[] CoastalSpecs =
        {
            new AssetSpec("WoodenDock", "Coastal", "7a126880-92bf-463b-99a0-048d6cebd559"),
            new AssetSpec("WoodenDam", "Coastal", "ee3a91fe-f9e6-492a-b27d-214ba3e08795"),
            new AssetSpec("StoneBridge", "Coastal", "1388d34e-d5b9-4171-8b94-01f493c0098b")
        };

        private static readonly AssetSpec[] HighlandSpecs =
        {
            new AssetSpec("GreyBoulder", "Highland", "544093dd-3e22-485a-a0ad-1360bba3f35b"),
            new AssetSpec("StackedCliff", "Highland", "ae1c8674-2d79-43c0-8759-fb5741fe14ab")
        };

        [MenuItem("Weather Manager/Import and Apply Ange Assets")]
        public static void ImportAndApply()
        {
            ApplyAndSave(false, "WEATHER_MANAGER_ANGE_REPLACEMENT");
        }

        [MenuItem("Weather Manager/Restore Original Ange Models")]
        public static void RestoreOriginalAngeModels()
        {
            ApplyAndSave(true, "WEATHER_MANAGER_ANGE_ORIGINAL_RESTORE");
        }

        private static void ApplyAndSave(bool rebuildPrefabs, string logMarker)
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException("Ange asset replacement must run in Edit Mode.");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var scene = SceneManager.GetActiveScene();
            var worlds = Object.FindObjectsByType<WorldBuilder>(FindObjectsInactive.Include);
            if (worlds.Length != 1)
            {
                throw new InvalidOperationException("Ange asset replacement requires exactly one WorldBuilder.");
            }

            var report = ApplyToScene(scene, worlds[0], rebuildPrefabs);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new IOException("Failed to save the Ange replacement scene.");
            }
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"{logMarker}:buildings={report.Buildings}"
                + $":trees={report.Trees}:props={report.Props}:prefabs={report.Prefabs}");
        }

        public static ReplacementReport ApplyToScene(Scene scene, WorldBuilder world, bool rebuildPrefabs)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException("A loaded scene is required for Ange asset replacement.");
            }
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            EnsureFolder(PrefabRoot);
            EnsureFolder($"{PrefabRoot}/Buildings");
            EnsureFolder($"{PrefabRoot}/Vegetation");
            EnsureFolder($"{PrefabRoot}/Props");
            EnsureFolder($"{PrefabRoot}/Landmarks");
            EnsureFolder($"{PrefabRoot}/Nature");
            EnsureFolder($"{PrefabRoot}/Disaster");
            EnsureFolder($"{PrefabRoot}/Vehicles");
            EnsureFolder($"{PrefabRoot}/Farm");
            EnsureFolder($"{PrefabRoot}/Coastal");
            EnsureFolder($"{PrefabRoot}/Highland");

            var prefabs = EnsurePrefabs(rebuildPrefabs);
            var buildingsRoot = FindRoot(scene, "Buildings");
            var vegetationRoot = FindRoot(scene, "Vegetation");
            var environmentRoot = FindRoot(scene, "Environment");
            var lightingRoot = FindRoot(scene, "Lighting");

            var buildingRoots = FindDirectChildren(buildingsRoot, "Building ");
            var treeRoots = FindDirectChildren(vegetationRoot, "Tree ");
            var vehicleRoots = FindDirectChildren(buildingsRoot, "Vehicle ");
            if (buildingRoots.Count != 42)
            {
                throw new InvalidOperationException($"Expected 42 buildings, found {buildingRoots.Count}.");
            }
            if (treeRoots.Count != 96)
            {
                throw new InvalidOperationException($"Expected 96 trees, found {treeRoots.Count}.");
            }
            if (vehicleRoots.Count != 12)
            {
                throw new InvalidOperationException($"Expected 12 vehicles, found {vehicleRoots.Count}.");
            }

            for (var index = 0; index < buildingRoots.Count; index++)
            {
                var spec = BuildingSpecs[index % BuildingSpecs.Length];
                ReplaceDamageableVisual(
                    scene,
                    buildingRoots[index],
                    spec,
                    prefabs[spec.Name],
                    prefabs["RuinedShed"],
                    prefabs["WoodDebris"],
                    DamageableKind.Building);
            }
            for (var index = 0; index < treeRoots.Count; index++)
            {
                var spec = VegetationSpecs[index % VegetationSpecs.Length];
                ReplaceDamageableVisual(
                    scene,
                    treeRoots[index],
                    spec,
                    prefabs[spec.Name],
                    null,
                    prefabs["Stump"],
                    DamageableKind.Tree);
            }
            for (var index = 0; index < vehicleRoots.Count; index++)
            {
                var spec = VehicleSpecs[index % 3];
                ReplaceDamageableVisual(
                    scene,
                    vehicleRoots[index],
                    spec,
                    prefabs[spec.Name],
                    null,
                    prefabs["WreckedCar"],
                    DamageableKind.Vehicle);
            }

            var propCount = PlaceProps(scene, environmentRoot, lightingRoot, prefabs);
            world.RefreshBakedAssetReferences();
            EditorUtility.SetDirty(world);
            EditorSceneManager.MarkSceneDirty(scene);
            return new ReplacementReport(buildingRoots.Count, treeRoots.Count, propCount, prefabs.Count);
        }

        private static Dictionary<string, GameObject> EnsurePrefabs(bool rebuild)
        {
            var result = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (var spec in EnumerateSpecs())
            {
                var prefab = rebuild ? null : AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
                if (prefab == null)
                {
                    prefab = BuildPrefab(spec);
                }
                result.Add(spec.Name, prefab);
            }
            return result;
        }

        private static IEnumerable<AssetSpec> EnumerateSpecs()
        {
            foreach (var spec in BuildingSpecs) yield return spec;
            foreach (var spec in VegetationSpecs) yield return spec;
            foreach (var spec in PropSpecs) yield return spec;
            foreach (var spec in LandmarkSpecs) yield return spec;
            foreach (var spec in NatureSpecs) yield return spec;
            foreach (var spec in DisasterSpecs) yield return spec;
            foreach (var spec in VehicleSpecs) yield return spec;
            foreach (var spec in FarmSpecs) yield return spec;
            foreach (var spec in CoastalSpecs) yield return spec;
            foreach (var spec in HighlandSpecs) yield return spec;
        }

        private static GameObject BuildPrefab(AssetSpec spec)
        {
            AssetDatabase.ImportAsset(
                spec.ModelPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var model = LoadModel(spec.ModelPath);
            if (model == null)
            {
                throw new FileNotFoundException($"glTFast did not import a model at {spec.ModelPath}.");
            }

            var wrapper = new GameObject(spec.Name);
            try
            {
                var instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
                if (instance == null)
                {
                    instance = Object.Instantiate(model);
                }
                instance.name = "AngeModel";
                instance.transform.SetParent(wrapper.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    throw new InvalidOperationException($"{spec.Name} contains no Renderer.");
                }

                // Keep every renderer and shared material exactly as glTFast imported it
                // from the original GLB, including its embedded KTX2 textures.
                NormalizeModel(wrapper.transform, instance.transform, renderers);

                var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, spec.PrefabPath, out var success);
                if (!success || prefab == null)
                {
                    throw new IOException($"Failed to save prefab {spec.PrefabPath}.");
                }
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(wrapper);
            }
        }

        private static GameObject LoadModel(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model != null)
            {
                return model;
            }
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (var index = 0; index < assets.Length; index++)
            {
                if (assets[index] is GameObject gameObject)
                {
                    return gameObject;
                }
            }
            return null;
        }

        private static void NormalizeModel(Transform wrapper, Transform model, Renderer[] renderers)
        {
            if (!TryCalculateLocalBounds(wrapper, renderers, out var bounds))
            {
                throw new InvalidOperationException($"{wrapper.name} has no measurable renderer bounds.");
            }
            var scale = 1f / Mathf.Max(0.0001f, bounds.size.y);
            model.localScale = Vector3.one * scale;
            model.localPosition = new Vector3(
                -bounds.center.x * scale,
                -bounds.min.y * scale,
                -bounds.center.z * scale);
        }

        private static void ReplaceDamageableVisual(
            Scene scene,
            Transform root,
            AssetSpec spec,
            GameObject prefab,
            GameObject criticalPrefab,
            GameObject destroyedPrefab,
            DamageableKind kind)
        {
            var binding = root.GetComponent<AngeVisualBinding>();
            var targetSize = binding != null && IsValidSize(binding.TargetSize)
                ? binding.TargetSize
                : MeasureLegacyTarget(root, kind);

            var legacy = root.Find("LegacyVisual");
            if (legacy == null)
            {
                legacy = new GameObject("LegacyVisual").transform;
                legacy.SetParent(root, false);
            }
            legacy.localPosition = Vector3.zero;
            legacy.localRotation = Quaternion.identity;
            legacy.localScale = Vector3.one;

            var children = new List<Transform>(root.childCount);
            for (var index = 0; index < root.childCount; index++)
            {
                children.Add(root.GetChild(index));
            }
            for (var index = 0; index < children.Count; index++)
            {
                var child = children[index];
                if (child == legacy
                    || child.name == "Visual"
                    || child.name == "CriticalVisual"
                    || child.name == "DestroyedVisual")
                {
                    continue;
                }
                child.SetParent(legacy, true);
            }
            legacy.gameObject.SetActive(false);

            var replacementNames = new[] { "Visual", "CriticalVisual", "DestroyedVisual" };
            for (var index = 0; index < replacementNames.Length; index++)
            {
                var previous = root.Find(replacementNames[index]);
                if (previous != null) Object.DestroyImmediate(previous.gameObject);
            }

            var preserveAspect = kind == DamageableKind.Vehicle;
            var visual = CreateStageVisual(scene, root, "Visual", prefab, targetSize, preserveAspect);
            var criticalVisual = criticalPrefab != null
                ? CreateStageVisual(scene, root, "CriticalVisual", criticalPrefab, targetSize, preserveAspect)
                : null;
            var destroyedSize = kind == DamageableKind.Tree
                ? new Vector3(targetSize.x, targetSize.y * 0.32f, targetSize.z)
                : new Vector3(targetSize.x, targetSize.y * 0.34f, targetSize.z);
            var destroyedVisual = destroyedPrefab != null
                ? CreateStageVisual(scene, root, "DestroyedVisual", destroyedPrefab, destroyedSize, preserveAspect)
                : null;
            var renderers = visual.GetComponentsInChildren<Renderer>(true);

            if (binding == null)
            {
                binding = root.gameObject.AddComponent<AngeVisualBinding>();
            }
            binding.Configure(
                spec.AssetId,
                spec.Name,
                kind,
                legacy,
                visual,
                criticalVisual,
                destroyedVisual,
                targetSize,
                prefab,
                renderers);
            binding.RefreshRenderers();
            binding.ApplyStage(DamageVisualStage.Healthy);
            EditorUtility.SetDirty(binding);
            EditorUtility.SetDirty(root.gameObject);
        }

        private static Transform CreateStageVisual(
            Scene scene,
            Transform root,
            string visualName,
            GameObject prefab,
            Vector3 targetSize,
            bool preserveAspect)
        {
            var visual = new GameObject(visualName).transform;
            visual.SetParent(root, false);
            var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to instantiate {prefab.name}.");
            }
            instance.name = "AngeModel";
            instance.transform.SetParent(visual, false);
            ScaleInstanceToTarget(visual, instance.transform, targetSize, preserveAspect);
            visual.gameObject.SetActive(visualName == "Visual");
            return visual;
        }

        private static Vector3 MeasureLegacyTarget(Transform root, DamageableKind kind)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var candidates = new List<Renderer>(renderers.Length);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null
                    || IsUnderNamedChild(renderer.transform, root, "Visual")
                    || IsUnderNamedChild(renderer.transform, root, "CriticalVisual")
                    || IsUnderNamedChild(renderer.transform, root, "DestroyedVisual"))
                {
                    continue;
                }
                candidates.Add(renderer);
            }
            if (TryCalculateLocalBounds(root, candidates.ToArray(), out var bounds)
                && IsValidSize(bounds.size))
            {
                return bounds.size;
            }

            if (kind == DamageableKind.Building)
            {
                var box = root.GetComponent<BoxCollider>();
                if (box != null)
                {
                    return new Vector3(box.size.x, box.size.y, box.size.z);
                }
            }
            else if (kind == DamageableKind.Tree)
            {
                var capsule = root.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    return new Vector3(capsule.radius * 2f, capsule.height, capsule.radius * 2f);
                }
            }
            return kind == DamageableKind.Tree
                ? new Vector3(1.2f, 2f, 1.2f)
                : new Vector3(2f, 2f, 1.8f);
        }

        private static void ScaleInstanceToTarget(
            Transform relativeTo,
            Transform instance,
            Vector3 targetSize,
            bool preserveAspect)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (!TryCalculateLocalBounds(relativeTo, renderers, out var bounds))
            {
                return;
            }
            if (preserveAspect && bounds.size.z > bounds.size.x && targetSize.x > targetSize.z)
            {
                instance.localRotation *= Quaternion.Euler(0f, 90f, 0f);
                TryCalculateLocalBounds(relativeTo, renderers, out bounds);
            }

            if (preserveAspect)
            {
                var uniform = Mathf.Min(
                    targetSize.x / Mathf.Max(0.0001f, bounds.size.x),
                    targetSize.y / Mathf.Max(0.0001f, bounds.size.y),
                    targetSize.z / Mathf.Max(0.0001f, bounds.size.z));
                instance.localScale *= uniform;
            }
            else
            {
                var scale = new Vector3(
                    targetSize.x / Mathf.Max(0.0001f, bounds.size.x),
                    targetSize.y / Mathf.Max(0.0001f, bounds.size.y),
                    targetSize.z / Mathf.Max(0.0001f, bounds.size.z));
                instance.localScale = Vector3.Scale(instance.localScale, scale);
            }
            if (TryCalculateLocalBounds(relativeTo, renderers, out var scaledBounds))
            {
                instance.localPosition += new Vector3(
                    -scaledBounds.center.x,
                    -scaledBounds.min.y,
                    -scaledBounds.center.z);
            }
        }

        private static int PlaceProps(
            Scene scene,
            Transform environmentRoot,
            Transform lightingRoot,
            IReadOnlyDictionary<string, GameObject> prefabs)
        {
            var existing = environmentRoot.Find("AngeProps");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }
            var propsRoot = new GameObject("AngeProps").transform;
            propsRoot.SetParent(environmentRoot, false);

            var count = 0;
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Bench"],
                "Bench",
                0.78f,
                new[]
                {
                    new Pose(new Vector3(-10f, 0.08f, 12.5f), Quaternion.Euler(0f, 90f, 0f)),
                    new Pose(new Vector3(10f, 0.08f, 12.5f), Quaternion.Euler(0f, 90f, 0f)),
                    new Pose(new Vector3(-10f, 0.08f, -12.7f), Quaternion.Euler(0f, 90f, 0f)),
                    new Pose(new Vector3(10f, 0.08f, -12.7f), Quaternion.Euler(0f, 90f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Cart"],
                "Cart",
                1.05f,
                new[]
                {
                    new Pose(new Vector3(-18f, 0.08f, -4.2f), Quaternion.Euler(0f, 25f, 0f)),
                    new Pose(new Vector3(18f, 0.08f, 4.2f), Quaternion.Euler(0f, -155f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Well"],
                "Well",
                1.75f,
                new[]
                {
                    new Pose(new Vector3(0f, 0.08f, 13.4f), Quaternion.identity)
                });

            var simulation = Object.FindFirstObjectByType<DisasterSimulation>(FindObjectsInactive.Include);
            count += PlaceEnvironmentalLandmark(
                scene,
                propsRoot,
                prefabs["Windmill"],
                "Windmill 01",
                new Vector3(-34f, 0.16f, -18f),
                Quaternion.Euler(0f, 28f, 0f),
                4.6f,
                simulation,
                AngeEnvironmentalMotion.WindRotor,
                16f,
                310f);
            count += PlaceEnvironmentalLandmark(
                scene,
                propsRoot,
                prefabs["WaterWheel"],
                "WaterWheel 01",
                new Vector3(36.5f, 0.11f, -15.5f),
                Quaternion.Euler(0f, -90f, 0f),
                2.2f,
                simulation,
                AngeEnvironmentalMotion.WaterWheel,
                5f,
                190f);
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Bridge"],
                "Bridge",
                1.9f,
                new[]
                {
                    new Pose(new Vector3(31f, 0.11f, -10.05f), Quaternion.Euler(0f, 72f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Gazebo"],
                "Gazebo",
                2.6f,
                new[]
                {
                    new Pose(new Vector3(0f, 0.08f, -16f), Quaternion.identity)
                });

            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["RockCluster"],
                "Rock Cluster",
                1.4f,
                new[]
                {
                    new Pose(new Vector3(-39f, 1.8f, 12f), Quaternion.Euler(0f, 15f, 0f)),
                    new Pose(new Vector3(-28f, 2.65f, 27f), Quaternion.Euler(0f, -42f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["MossRock"],
                "Moss Rock",
                1.1f,
                new[]
                {
                    new Pose(new Vector3(-43f, 0.18f, 22f), Quaternion.Euler(0f, 33f, 0f)),
                    new Pose(new Vector3(-18f, 0.18f, 27f), Quaternion.Euler(0f, -18f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Flowers"],
                "Flowers",
                0.62f,
                new[]
                {
                    new Pose(new Vector3(-39f, 0.18f, 9f), Quaternion.identity),
                    new Pose(new Vector3(-23f, 0.18f, 23f), Quaternion.Euler(0f, 42f, 0f)),
                    new Pose(new Vector3(-12f, 0.08f, -12f), Quaternion.Euler(0f, -25f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Bush"],
                "Bush",
                0.95f,
                new[]
                {
                    new Pose(new Vector3(-45f, 0.18f, 15f), Quaternion.identity),
                    new Pose(new Vector3(-27f, 0.18f, 30f), Quaternion.Euler(0f, 65f, 0f)),
                    new Pose(new Vector3(-16f, 0.18f, 8f), Quaternion.Euler(0f, -30f, 0f))
                });

            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Barrel"],
                "Barrel",
                0.78f,
                new[]
                {
                    new Pose(new Vector3(-17.2f, 0.08f, -6.1f), Quaternion.identity),
                    new Pose(new Vector3(-16.3f, 0.08f, -6.4f), Quaternion.Euler(0f, 30f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["HayBale"],
                "Hay Bale",
                0.92f,
                new[]
                {
                    new Pose(new Vector3(-38f, 0.16f, -22f), Quaternion.Euler(0f, 12f, 0f)),
                    new Pose(new Vector3(-36.6f, 0.16f, -22.4f), Quaternion.Euler(0f, -8f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Signpost"],
                "Signpost",
                1.65f,
                new[]
                {
                    new Pose(new Vector3(-3.2f, 0.08f, 14.4f), Quaternion.Euler(0f, 75f, 0f)),
                    new Pose(new Vector3(15.2f, 0.08f, -2.5f), Quaternion.Euler(0f, -15f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Rowboat"],
                "Rowboat",
                0.82f,
                new[]
                {
                    new Pose(new Vector3(42.1f, 0.1f, -27.4f), Quaternion.Euler(0f, -28f, 0f))
                });
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["MiningTruck"],
                "Mining Truck",
                1.2f,
                new[]
                {
                    new Pose(new Vector3(-41f, 2.65f, 9f), Quaternion.Euler(0f, -35f, 0f))
                });

            count += PlaceRepeated(
                scene, propsRoot, prefabs["RustSilo"], "Rust Silo", 5.2f,
                new[] { new Pose(new Vector3(-43f, 0.16f, -18f), Quaternion.Euler(0f, 12f, 0f)) });
            count += PlaceRepeated(
                scene, propsRoot, prefabs["StorageShed"], "Storage Shed", 2.7f,
                new[] { new Pose(new Vector3(-24f, 0.16f, -27f), Quaternion.Euler(0f, -18f, 0f)) });
            count += PlaceRepeated(
                scene, propsRoot, prefabs["Scarecrow"], "Scarecrow", 1.8f,
                new[]
                {
                    new Pose(new Vector3(-34f, 0.16f, -15f), Quaternion.Euler(0f, 20f, 0f)),
                    new Pose(new Vector3(-26f, 0.16f, -21f), Quaternion.Euler(0f, -25f, 0f))
                });
            count += PlaceRepeated(
                scene, propsRoot, prefabs["GrassClump"], "Grass Clump", 0.7f,
                new[]
                {
                    new Pose(new Vector3(-47f, 0.16f, -25f), Quaternion.identity),
                    new Pose(new Vector3(-20f, 0.16f, -15f), Quaternion.Euler(0f, 35f, 0f)),
                    new Pose(new Vector3(37f, 0.08f, -29f), Quaternion.Euler(0f, -12f, 0f))
                });
            count += PlaceRepeated(
                scene, propsRoot, prefabs["SplitRailFence"], "Split Rail Fence", 1.05f,
                new[]
                {
                    new Pose(new Vector3(-43f, 0.16f, -30f), Quaternion.Euler(0f, 90f, 0f)),
                    new Pose(new Vector3(-35f, 0.16f, -30f), Quaternion.Euler(0f, 90f, 0f)),
                    new Pose(new Vector3(-27f, 0.16f, -30f), Quaternion.Euler(0f, 90f, 0f))
                });

            count += PlaceRepeated(
                scene, propsRoot, prefabs["WoodenDock"], "Wooden Dock", 0.95f,
                new[] { new Pose(new Vector3(43.2f, 0.1f, -28.7f), Quaternion.Euler(0f, 62f, 0f)) });
            count += PlaceRepeated(
                scene, propsRoot, prefabs["WoodenDam"], "Wooden Dam", 1.3f,
                new[] { new Pose(new Vector3(41f, 0.11f, -26f), Quaternion.Euler(0f, 48f, 0f)) });
            count += PlaceRepeated(
                scene, propsRoot, prefabs["StoneBridge"], "Stone Bridge", 1.4f,
                new[] { new Pose(new Vector3(38f, 0.11f, -23f), Quaternion.Euler(0f, 54f, 0f)) });

            count += PlaceRepeated(
                scene, propsRoot, prefabs["StackedCliff"], "Stacked Cliff", 5.5f,
                new[]
                {
                    new Pose(new Vector3(-46f, 2.6f, 18f), Quaternion.Euler(0f, -28f, 0f)),
                    new Pose(new Vector3(-31f, 1.8f, 29f), Quaternion.Euler(0f, 16f, 0f))
                });
            count += PlaceRepeated(
                scene, propsRoot, prefabs["GreyBoulder"], "Grey Boulder", 1.65f,
                new[]
                {
                    new Pose(new Vector3(-39f, 1.75f, 8f), Quaternion.Euler(0f, 20f, 0f)),
                    new Pose(new Vector3(-23f, 2.65f, 14f), Quaternion.Euler(0f, 47f, 0f))
                });
            count += PlaceRepeated(
                scene, propsRoot, prefabs["FallenTree"], "Fallen Tree", 1.25f,
                new[]
                {
                    new Pose(new Vector3(-42f, 0.18f, 28f), Quaternion.Euler(0f, 30f, 0f)),
                    new Pose(new Vector3(-18f, 0.18f, 7f), Quaternion.Euler(0f, -22f, 0f))
                });
            count += PlaceRepeated(
                scene, propsRoot, prefabs["DeadTree"], "Dead Tree", 3.2f,
                new[] { new Pose(new Vector3(-47f, 0.18f, 5f), Quaternion.Euler(0f, 12f, 0f)) });

            var hydrantPoses = new[]
            {
                new Pose(new Vector3(-11f, 0.08f, 3.4f), Quaternion.identity),
                new Pose(new Vector3(11f, 0.08f, -3.4f), Quaternion.Euler(0f, 180f, 0f)),
                new Pose(new Vector3(2.8f, 0.08f, 11.5f), Quaternion.Euler(0f, 90f, 0f))
            };
            for (var index = 0; index < hydrantPoses.Length; index++)
            {
                var hydrant = CreatePropInstance(
                    scene,
                    propsRoot,
                    prefabs["FireHydrant"],
                    $"Fire Hydrant {index + 1:00}",
                    hydrantPoses[index].position,
                    hydrantPoses[index].rotation,
                    0.86f,
                    true);
                var feedback = hydrant.GetComponent<AngeHydrantFeedback>();
                if (feedback == null) feedback = hydrant.AddComponent<AngeHydrantFeedback>();
                feedback.Configure(hydrant.GetComponentsInChildren<Renderer>(true), 18f);
                EditorUtility.SetDirty(feedback);
                count++;
            }

            var fencePoses = new List<Pose>();
            for (var index = 0; index < 6; index++)
            {
                fencePoses.Add(new Pose(
                    new Vector3(-15f + index * 6f, 0.08f, -14.2f),
                    Quaternion.Euler(0f, 90f, 0f)));
            }
            count += PlaceRepeated(
                scene,
                propsRoot,
                prefabs["Fence"],
                "Fence",
                0.92f,
                fencePoses.ToArray());

            var nightLights = lightingRoot.Find("NightLights");
            if (nightLights != null)
            {
                for (var index = 0; index < nightLights.childCount; index++)
                {
                    var streetLight = nightLights.GetChild(index);
                    var previousVisual = streetLight.Find("Visual");
                    if (previousVisual != null)
                    {
                        Object.DestroyImmediate(previousVisual.gameObject);
                    }
                    var visual = new GameObject("Visual").transform;
                    visual.SetParent(streetLight, false);
                    visual.localPosition = new Vector3(0f, -streetLight.localPosition.y, 0f);
                    CreatePropInstance(
                        scene,
                        visual,
                        prefabs["Lamp"],
                        $"Lamp {index + 1:00}",
                        Vector3.zero,
                        Quaternion.identity,
                        2.45f,
                        false);
                    count++;
                }
            }
            var terrain = Object.FindFirstObjectByType<LowPolyTerrainGenerator>(FindObjectsInactive.Include);
            if (terrain != null)
            {
                for (var index = propsRoot.childCount - 1; index >= 0; index--)
                {
                    var child = propsRoot.GetChild(index);
                    if (terrain.IsInsidePlayableIsland(child.position, 0.012f)) continue;
                    Object.DestroyImmediate(child.gameObject);
                    count--;
                }
            }
            return count;
        }

        private static int PlaceEnvironmentalLandmark(
            Scene scene,
            Transform parent,
            GameObject prefab,
            string name,
            Vector3 localPosition,
            Quaternion localRotation,
            float targetHeight,
            DisasterSimulation simulation,
            AngeEnvironmentalMotion motion,
            float idleSpeed,
            float maximumSpeed)
        {
            var instance = CreatePropInstance(
                scene,
                parent,
                prefab,
                name,
                localPosition,
                localRotation,
                targetHeight,
                true);
            var rotor = FindDrivenRotor(instance.transform, motion);
            if (motion == AngeEnvironmentalMotion.WindRotor && rotor != null && rotor.name == "WeatherDrivenRotor")
            {
                rotor.gameObject.SetActive(false);
            }
            var animator = instance.GetComponent<AngeEnvironmentalAnimator>();
            if (animator == null) animator = instance.AddComponent<AngeEnvironmentalAnimator>();
            animator.Configure(
                motion,
                simulation,
                rotor,
                Vector3.forward,
                idleSpeed,
                maximumSpeed);
            EditorUtility.SetDirty(animator);
            return 1;
        }

        private static Transform FindDrivenRotor(Transform root, AngeEnvironmentalMotion motion)
        {
            var explicitRotor = root.Find("WeatherDrivenRotor");
            if (explicitRotor != null) return explicitRotor;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            var preferred = motion == AngeEnvironmentalMotion.WaterWheel ? "wheel" : "rotor";
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return transforms[index];
                }
            }
            return root;
        }

        private static int PlaceRepeated(
            Scene scene,
            Transform parent,
            GameObject prefab,
            string name,
            float height,
            IReadOnlyList<Pose> poses)
        {
            for (var index = 0; index < poses.Count; index++)
            {
                CreatePropInstance(
                    scene,
                    parent,
                    prefab,
                    $"{name} {index + 1:00}",
                    poses[index].position,
                    poses[index].rotation,
                    height,
                    true);
            }
            return poses.Count;
        }

        private static GameObject CreatePropInstance(
            Scene scene,
            Transform parent,
            GameObject prefab,
            string name,
            Vector3 localPosition,
            Quaternion localRotation,
            float targetHeight,
            bool addCollider)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to instantiate prop {prefab.name}.");
            }
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = Vector3.one * targetHeight;

            if (addCollider)
            {
                var collider = instance.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    collider = instance.AddComponent<BoxCollider>();
                }
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (TryCalculateLocalBounds(instance.transform, renderers, out var bounds))
                {
                    collider.center = bounds.center;
                    collider.size = bounds.size;
                }
            }
            return instance;
        }

        private static List<Transform> FindDirectChildren(Transform parent, string prefix)
        {
            var result = new List<Transform>();
            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (child.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    result.Add(child);
                }
            }
            result.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
            return result;
        }

        private static Transform FindRoot(Scene scene, string name)
        {
            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == name)
                {
                    return roots[index].transform;
                }
            }
            throw new InvalidOperationException($"Scene root {name} is missing.");
        }

        private static bool IsUnderNamedChild(Transform candidate, Transform root, string childName)
        {
            var current = candidate;
            while (current != null && current != root)
            {
                if (current.name == childName)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private static bool IsValidSize(Vector3 size)
        {
            return size.x > 0.01f && size.y > 0.01f && size.z > 0.01f;
        }

        private static bool TryCalculateLocalBounds(
            Transform relativeTo,
            IReadOnlyList<Renderer> renderers,
            out Bounds bounds)
        {
            bounds = default;
            var found = false;
            for (var index = 0; index < renderers.Count; index++)
            {
                var renderer = renderers[index];
                if (renderer == null) continue;
                var world = renderer.bounds;
                var min = world.min;
                var max = world.max;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    point = relativeTo.InverseTransformPoint(point);
                    if (!found)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }
            return found;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException($"Invalid asset folder {path}.");
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
