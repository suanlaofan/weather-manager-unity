using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.SpatialTracking;
using Unity.XR.CoreUtils;
using Unity.XR.PXR;

namespace WeatherManager.Editor
{
    public static class WeatherManagerSceneBuilder
    {
        private const string SceneDirectory = "Assets/WeatherManager/Scenes";
        private const string ScenePath = SceneDirectory + "/WeatherManager.unity";
        private const string GeneratedDirectory = "Assets/WeatherManager/Generated";
        private const string MeshDirectory = GeneratedDirectory + "/Meshes";
        private const string MaterialDirectory = GeneratedDirectory + "/Materials";
        private const string PrefabDirectory = "Assets/WeatherManager/Prefabs";
        private const string RuntimeMaterialDirectory = "Assets/WeatherManager/Resources/WeatherManager";
        private const string MacBuildPath = "Builds/macOS/WeatherManager.app";

        [MenuItem("Weather Manager/Bake Editable Scene")]
        [MenuItem("Weather Manager/Build Runtime Scene")]
        public static void BuildRuntimeScene()
        {
            EnsureDirectories();
            EnsureRuntimeMaterials();
            var presentationSettings = WeatherPresentationAssetBuilder.Build();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "WeatherManager";

            // The scene stays fully editable in the desktop editor, while this
            // hierarchy becomes a physical tabletop only once PICO has started
            // an active XR session.  It prevents the old 2D app-window layout.
            var mrRigRoot = CreateRoot("MR Rig");
            var xrOrigin = mrRigRoot.AddComponent<XROrigin>();
            xrOrigin.Origin = mrRigRoot;
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            var pxrManager = mrRigRoot.AddComponent<PXR_Manager>();
            // MRC is an external-camera compositor path, distinct from headset
            // video see-through.  Disable it in the authored scene so it cannot
            // compete with the HMD/XR view at startup.
            pxrManager.openMRC = false;
            var cameraOffset = CreateChild(mrRigRoot.transform, "Camera Offset");
            cameraOffset.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            xrOrigin.CameraFloorOffsetObject = cameraOffset;

            var tabletopAnchor = CreateChild(mrRigRoot.transform, "Weather Tabletop Anchor");
            var tabletopRoot = CreateChild(tabletopAnchor.transform, "Weather Tabletop Island");
            var environmentRoot = CreateChild(tabletopRoot.transform, "Environment");
            var buildingsRoot = CreateChild(tabletopRoot.transform, "Buildings");
            var vegetationRoot = CreateChild(tabletopRoot.transform, "Vegetation");
            var residentsRoot = CreateChild(tabletopRoot.transform, "Residents");
            var gameplayRoot = CreateChild(tabletopRoot.transform, "GameplaySystems");
            var vfxPoolsRoot = CreateChild(tabletopRoot.transform, "VFXPools");
            var lightingRoot = CreateChild(tabletopRoot.transform, "Lighting");
            var cameraRoot = CreateChild(cameraOffset.transform, "Main Camera");
            // UI has its own meter-sized anchor, independent of head pose and island scale.
            var dashboardRoot = CreateChild(mrRigRoot.transform, "Spatial UI Anchor");
            dashboardRoot.transform.localPosition = new Vector3(0f, 1.32f, 1.2f);
            var uiRoot = CreateChild(dashboardRoot.transform, "UI");

            var leftController = BuildControllerRay(mrRigRoot.transform, "Left Controller", false);
            var rightController = BuildControllerRay(mrRigRoot.transform, "Right Controller", true);

            var worldCamera = BuildCamera(cameraRoot);
            xrOrigin.Camera = worldCamera;
            ConfigureTrackedPoseDriver(
                worldCamera.gameObject,
                TrackedPoseDriver.DeviceType.GenericXRDevice,
                TrackedPoseDriver.TrackedPose.Center);
            ConfigureTrackedPoseDriver(
                leftController,
                TrackedPoseDriver.DeviceType.GenericXRController,
                TrackedPoseDriver.TrackedPose.LeftPose);
            ConfigureTrackedPoseDriver(
                rightController,
                TrackedPoseDriver.DeviceType.GenericXRController,
                TrackedPoseDriver.TrackedPose.RightPose);
            BuildLighting(lightingRoot, out var sun, out var moonFill);

            var manager = gameplayRoot.AddComponent<GameManager>();
            var mrTabletop = mrRigRoot.AddComponent<MrTabletopExperience>();
            var world = gameplayRoot.AddComponent<WorldBuilder>();
            var simulation = gameplayRoot.AddComponent<DisasterSimulation>();
            var casting = gameplayRoot.AddComponent<WeatherCastingController>();
            var audio = gameplayRoot.AddComponent<WeatherAudioController>();
            var vfx = vfxPoolsRoot.AddComponent<WeatherVfxController>();
            var ui = uiRoot.AddComponent<UIController>();
            var cameraController = cameraRoot.AddComponent<CameraController>();

            world.PrepareBakedScene(
                environmentRoot.transform,
                buildingsRoot.transform,
                vegetationRoot.transform,
                residentsRoot.transform);
            world.SetDayPhase(DayPhase.Day);
            MoveNightLights(environmentRoot.transform, lightingRoot.transform);

            vfx.Initialize(vfxPoolsRoot.transform, worldCamera);
            audio.Initialize(gameplayRoot.transform, worldCamera.transform);
            ui.Build(manager);

            vfx.PrepareBakedScene(vfxPoolsRoot.transform, worldCamera);
            audio.PrepareBakedScene(gameplayRoot.transform, worldCamera.transform);
            ui.PrepareBakedScene(uiRoot.transform, manager);
            NormalizeEventSystems(uiRoot.transform);
            // Serialize the tracked-controller UI route into the authored
            // scene now; desktop fallback keeps this component disabled until
            // MrTabletopExperience switches the Canvas to World Space.
            ui.PrepareSpatialInput(mrRigRoot.transform);
            ui.BuildEditableSpatialPanel(dashboardRoot.transform);

            manager.ConfigureSceneReferences(
                world,
                simulation,
                casting,
                vfx,
                audio,
                ui,
                cameraController,
                worldCamera,
                sun,
                moonFill,
                gameplayRoot.transform);
            mrTabletop.Configure(tabletopRoot.transform, dashboardRoot.transform, worldCamera, ui);

            AssetizeSceneResources(scene);
            var angeReport = AngeAssetReplacement.ApplyToScene(scene, world, false);
            var terrainSnapDelta = world.SnapEditableSceneToTerrain();
            world.RefreshBakedAssetReferences();
            var sceneResidents = residentsRoot.GetComponentsInChildren<ResidentAgent>();
            if (sceneResidents.Length > 0)
            {
                var sharedMotions = ResidentMotionAssetBuilder.Build(sceneResidents[0]);
                foreach (var resident in sceneResidents)
                {
                    var motion = resident.GetComponent<ResidentMotionController>();
                    if (motion == null) motion = resident.gameObject.AddComponent<ResidentMotionController>();
                    motion.Configure(sharedMotions);
                }
            }
            WeatherEnvironmentAssetBuilder.Build(world, simulation);
            vfx.PrepareBakedScene(vfxPoolsRoot.transform, worldCamera);
            vfx.PreparePresentation(presentationSettings);
            vfx.RefreshBakedAssetReferences();
            AssetizeSceneResources(scene);
            AddEditableColliders(buildingsRoot.transform, vegetationRoot.transform);
            SaveReusablePrefabs(residentsRoot, vfxPoolsRoot);
            WeatherManagerRevisionTools.ClearIslandStaticFlags(tabletopRoot.transform);
            WeatherManagerRevisionTools.BakeControllerRay(mrRigRoot.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException("Failed to save editable Weather Manager scene.");
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ConfigurePlayerSettings();
            Selection.activeGameObject = environmentRoot;
            SceneView.lastActiveSceneView?.FrameSelected();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"WEATHER_MANAGER_ANGE_REPLACEMENT:buildings={angeReport.Buildings}"
                + $":trees={angeReport.Trees}:props={angeReport.Props}:prefabs={angeReport.Prefabs}");
            Debug.Log($"WEATHER_MANAGER_TERRAIN_SNAP:maxMoved={terrainSnapDelta:0.000}");
            Debug.Log($"WEATHER_MANAGER_EDITABLE_SCENE_READY:{ScenePath}");
        }

        [MenuItem("Weather Manager/Validate Continuous Terrain %#&t")]
        public static void ValidateContinuousTerrain()
        {
            var terrain = Object.FindFirstObjectByType<LowPolyTerrainGenerator>(FindObjectsInactive.Include);
            if (terrain == null) throw new BuildFailedException("Continuous low-poly terrain is missing.");
            var snapshot = terrain.ValidateInvariants();
            if (!snapshot.Passed)
            {
                throw new BuildFailedException(
                    $"Terrain invariants failed: seam={snapshot.SeamHeightDelta:0.0000}, peak={snapshot.PeakHeight:0.00}, "
                    + $"lake={snapshot.LakeFloorHeight:0.00}, town={snapshot.TownHeight:0.00}, farm={snapshot.FarmlandHeight:0.00}, "
                    + $"triangles={snapshot.TerrainTriangles}, renderers={snapshot.TerrainRenderers}.");
            }
            Debug.Log(
                $"WEATHER_MANAGER_TERRAIN_PASS:seam={snapshot.SeamHeightDelta:0.0000}:peak={snapshot.PeakHeight:0.00}"
                + $":lake={snapshot.LakeFloorHeight:0.00}:town={snapshot.TownHeight:0.00}:farm={snapshot.FarmlandHeight:0.00}"
                + $":triangles={snapshot.TerrainTriangles}:renderers={snapshot.TerrainRenderers}");
        }

        public static void ValidateScene()
        {
            BuildRuntimeScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var requiredRoots = new[]
            {
                "Environment", "Buildings", "Vegetation", "Residents", "GameplaySystems",
                "VFXPools", "Lighting", "UI", "Main Camera"
            };
            foreach (var rootName in requiredRoots)
            {
                if (GameObject.Find(rootName) == null)
                {
                    throw new BuildFailedException($"Editable scene is missing hierarchy root: {rootName}");
                }
            }

            var managers = Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include);
            var residents = Object.FindObjectsByType<ResidentAgent>(FindObjectsInactive.Include);
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
            var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            if (scene.path != ScenePath
                || managers.Length != 1
                || residents.Length != 84
                || renderers.Length < 500
                || eventSystems.Length != 1)
            {
                throw new BuildFailedException(
                    $"Weather Manager editable scene validation failed: managers={managers.Length}, residents={residents.Length}, renderers={renderers.Length}, eventSystems={eventSystems.Length}.");
            }
            Debug.Log($"WEATHER_MANAGER_VALIDATION_PASS:residents={residents.Length}:renderers={renderers.Length}:eventSystems={eventSystems.Length}");
        }

        [MenuItem("Weather Manager/Reopen and Validate Editable Scene")]
        public static void ReopenAndValidateEditableScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var biomeMaps = Object.FindObjectsByType<BiomeMap>(FindObjectsInactive.Include);
            var bindings = Object.FindObjectsByType<AngeVisualBinding>(FindObjectsInactive.Include);
            var vehicles = 0;
            var vehicleModels = new HashSet<string>();
            for (var index = 0; index < bindings.Length; index++)
            {
                if (bindings[index].DamageableKind != DamageableKind.Vehicle) continue;
                vehicles++;
                vehicleModels.Add(bindings[index].SourceModel);
            }

            if (scene.path != ScenePath
                || biomeMaps.Length != 1
                || biomeMaps[0].Zones.Count < 7
                || vehicles != 12
                || vehicleModels.Count != 3)
            {
                throw new BuildFailedException(
                    $"Expanded scene reopen validation failed: maps={biomeMaps.Length}, zones="
                    + $"{(biomeMaps.Length == 1 ? biomeMaps[0].Zones.Count : 0)}, vehicles={vehicles}, models={vehicleModels.Count}.");
            }

            Debug.Log(
                $"WEATHER_MANAGER_BIOME_SCENE_REOPEN_PASS:zones={biomeMaps[0].Zones.Count}"
                + $":vehicles={vehicles}:vehicleModels={vehicleModels.Count}:angeBindings={bindings.Length}");
        }

        [MenuItem("Weather Manager/Apply Compact Island Update %#&i")]
        public static void ApplyCompactIslandUpdate()
        {
            EnsureDirectories();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var world = Object.FindFirstObjectByType<WorldBuilder>(FindObjectsInactive.Include);
            var ui = Object.FindFirstObjectByType<UIController>(FindObjectsInactive.Include);
            var manager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            if (world == null || ui == null || manager == null)
            {
                throw new BuildFailedException(
                    $"Compact island update requires world/UI/manager: world={world != null}:ui={ui != null}:manager={manager != null}");
            }

            var removedOutside = world.RebuildCompactIslandSurface();
            var alignedVehicles = world.AlignBakedVehiclesToRoads();
            ui.PrepareBakedScene(ui.transform, manager);
            ui.DisableSeasonControls();
            AssetizeSceneResources(scene);
            world.RefreshBakedAssetReferences();
            EditorUtility.SetDirty(world);
            EditorUtility.SetDirty(ui);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException("Failed to save compact Weather Manager scene.");
            }
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"WEATHER_MANAGER_COMPACT_ISLAND_PASS:removedOutside={removedOutside}"
                + $":alignedVehicles={alignedVehicles}:seasonControls=False:scene={ScenePath}");
        }

        [MenuItem("Weather Manager/Restore Full Terrain")]
        public static void RestoreFullTerrain()
        {
            EnsureDirectories();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var world = Object.FindFirstObjectByType<WorldBuilder>(FindObjectsInactive.Include);
            if (world == null)
            {
                throw new BuildFailedException("Full terrain restore requires the editable WorldBuilder.");
            }

            var snapshot = world.RebuildFullTerrainSurface();
            AssetizeSceneResources(scene);
            EditorUtility.SetDirty(world);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException("Failed to save the restored full-terrain Weather Manager scene.");
            }
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"WEATHER_MANAGER_FULL_TERRAIN_RESTORE_PASS:triangles={snapshot.TerrainTriangles}"
                + $":renderers={snapshot.TerrainRenderers}:sceneSaved=True:scene={ScenePath}");
        }

        [MenuItem("Weather Manager/Log Runtime Snapshot")]
        public static void LogRuntimeSnapshot()
        {
            var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            var managers = Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include);
            var residents = Object.FindObjectsByType<ResidentAgent>(FindObjectsInactive.Include);
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
            var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            var worlds = Object.FindObjectsByType<WorldBuilder>(FindObjectsInactive.Include);
            var runtimeRoots = 0;
            for (var index = 0; index < transforms.Length; index++)
            {
                var objectName = transforms[index].name;
                if (objectName == "RuntimeWorld" || objectName == "WeatherManager_GeneratedWorld")
                {
                    runtimeRoots++;
                }
            }

            var damageables = worlds.Length == 1 ? worlds[0].Damageables.Count : -1;
            var stats = managers.Length == 1 ? managers[0].Stats : null;
            Debug.Log(
                $"WEATHER_MANAGER_RUNTIME_SNAPSHOT:playing={Application.isPlaying}"
                + $":gameObjects={transforms.Length}:renderers={renderers.Length}"
                + $":managers={managers.Length}:worlds={worlds.Length}:runtimeRoots={runtimeRoots}"
                + $":residents={residents.Length}:damageables={damageables}:eventSystems={eventSystems.Length}"
                + $":alive={stats?.AliveResidents ?? -1}:dead={stats?.DeadResidents ?? -1}"
                + $":fires={stats?.ActiveFires ?? -1}:flooded={stats?.FloodedCells ?? -1}"
                + $":tornadoes={stats?.Tornadoes ?? -1}:destroyed={stats?.DestroyedObjects ?? -1}");
        }

        [MenuItem("Weather Manager/Run UI Interaction Acceptance %#&u")]
        public static void RunUiInteractionAcceptance()
        {
            if (!Application.isPlaying)
            {
                FailUiAcceptance("playMode=False");
            }

            var managers = Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include);
            var simulations = Object.FindObjectsByType<DisasterSimulation>(FindObjectsInactive.Include);
            var userInterfaces = Object.FindObjectsByType<UIController>(FindObjectsInactive.Include);
            if (managers.Length != 1 || simulations.Length != 1 || userInterfaces.Length != 1)
            {
                FailUiAcceptance(
                    $"managers={managers.Length}:simulations={simulations.Length}:ui={userInterfaces.Length}");
            }

            var ui = userInterfaces[0];
            var resetButton = FindNamedComponent<Button>(ui.transform, "ResetButton");
            var title = FindNamedComponent<Text>(ui.transform, "Title");
            var statsTitle = FindNamedComponent<Text>(ui.transform, "StatsTitle");
            var languageControl = FindNamedComponent<RectTransform>(ui.transform, "LanguageControl");
            var resetLabel = resetButton != null ? resetButton.GetComponentInChildren<Text>(true) : null;
            if (resetButton == null || title == null || statsTitle == null || resetLabel == null)
            {
                FailUiAcceptance(
                    $"resetButton={resetButton != null}:title={title != null}"
                    + $":statsTitle={statsTitle != null}:resetLabel={resetLabel != null}");
            }

            // English-only build: labels must resolve to English and the legacy language toggle stays hidden.
            var englishLanguage = LocalizationService.Current == AppLanguage.English;
            var englishTitleValid = title.text == LocalizationService.Text("app.title");
            var englishStatsValid = statsTitle.text == LocalizationService.Text("label.city_status");
            var englishResetValid = resetLabel.text == LocalizationService.Text("button.reset");
            var languageToggleHidden = languageControl == null || !languageControl.gameObject.activeInHierarchy;
            if (!englishLanguage || !englishTitleValid || !englishStatsValid || !englishResetValid || !languageToggleHidden)
            {
                FailUiAcceptance(
                    $"englishLanguage={englishLanguage}:englishTitle={englishTitleValid}"
                    + $":englishStats={englishStatsValid}:englishReset={englishResetValid}"
                    + $":languageToggleHidden={languageToggleHidden}");
            }

            simulations[0].RunBiomeVisualDemo(BiomeType.Forest, WeatherType.Heat, 7f);
            var before = managers[0].Stats;
            var disasterCreated = before != null && before.ActiveFires > 0;
            if (!disasterCreated)
            {
                FailUiAcceptance(
                    $"disasterCreated=False:fires={before?.ActiveFires ?? -1}:flooded={before?.FloodedCells ?? -1}"
                    + $":tornadoes={before?.Tornadoes ?? -1}:destroyed={before?.DestroyedObjects ?? -1}"
                    + $":dead={before?.DeadResidents ?? -1}");
            }

            var firesBeforeReset = before.ActiveFires;
            resetButton.onClick.Invoke();
            var after = managers[0].Stats;
            var resetPassed = after != null
                && after.ActiveFires == 0
                && after.FloodedCells == 0
                && after.Tornadoes == 0
                && after.FireTornadoes == 0
                && after.DestroyedObjects == 0
                && after.DeadResidents == 0
                && after.InjuredResidents == 0
                && after.SnowyCells == 0
                && after.ScorchedCells == 0
                && !after.Blackout;
            if (!resetPassed)
            {
                FailUiAcceptance(
                    $"reset=False:fires={after?.ActiveFires ?? -1}:flooded={after?.FloodedCells ?? -1}"
                    + $":tornadoes={after?.Tornadoes ?? -1}:fireTornadoes={after?.FireTornadoes ?? -1}"
                    + $":destroyed={after?.DestroyedObjects ?? -1}:dead={after?.DeadResidents ?? -1}"
                    + $":injured={after?.InjuredResidents ?? -1}:snow={after?.SnowyCells ?? -1}"
                    + $":scorched={after?.ScorchedCells ?? -1}:blackout={after?.Blackout}");
            }

            Debug.Log(
                $"WEATHER_MANAGER_UI_ACCEPTANCE:pass=True:language=EN"
                + $":firesBeforeReset={firesBeforeReset}:firesAfterReset={after.ActiveFires}"
                + $":floodAfterReset={after.FloodedCells}:tornadoesAfterReset={after.Tornadoes}"
                + $":destroyedAfterReset={after.DestroyedObjects}:deadAfterReset={after.DeadResidents}");

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                var evidenceDirectory = Path.Combine(projectRoot, "Evidence/PlayMode");
                Directory.CreateDirectory(evidenceDirectory);
                ScreenCapture.CaptureScreenshot(Path.Combine(evidenceDirectory, "02-game-en.png"), 1);
            }
        }

        private static T FindNamedComponent<T>(Transform root, string objectName) where T : Component
        {
            if (root == null) return null;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == objectName)
                {
                    return transforms[index].GetComponent<T>();
                }
            }
            return null;
        }

        private static void FailUiAcceptance(string fields)
        {
            Debug.LogError($"WEATHER_MANAGER_UI_ACCEPTANCE:pass=False:{fields}");
            throw new BuildFailedException($"UI interaction acceptance failed: {fields}");
        }

        [MenuItem("Weather Manager/Run Rain Extinguish Acceptance")]
        public static void RunRainExtinguishAcceptance()
        {
            if (!Application.isPlaying)
            {
                throw new BuildFailedException("Rain extinguish acceptance must run in Editor Play Mode.");
            }

            var simulations = Object.FindObjectsByType<DisasterSimulation>(FindObjectsInactive.Include);
            if (simulations.Length != 1)
            {
                throw new BuildFailedException(
                    $"Rain extinguish acceptance requires one simulation, found {simulations.Length}.");
            }

            var snapshot = simulations[0].RunRainExtinguishAcceptance();
            Debug.Log(
                $"WEATHER_MANAGER_RAIN_ACCEPTANCE:pass={snapshot.Passed}"
                + $":surfaceFire={snapshot.SurfaceFireBefore:F3}->{snapshot.SurfaceFireAfter:F3}"
                + $":activeFires={snapshot.ActiveFiresBefore}->{snapshot.ActiveFiresAfter}"
                + $":buildingBurning={snapshot.BuildingBurningBefore}->{snapshot.BuildingBurningAfter}"
                + $":buildingHealth={snapshot.BuildingHealthBefore:F2}->{snapshot.BuildingHealthAfter:F2}"
                + $":fireTornado={snapshot.FireTornadoBefore}->{snapshot.FireTornadoAfter}"
                + $":tornadoStrength={snapshot.FireTornadoStrengthBefore:F3}->{snapshot.FireTornadoStrengthAfter:F3}");
            if (!snapshot.Passed)
            {
                throw new BuildFailedException("Rain extinguish deterministic acceptance failed.");
            }
        }

        [MenuItem("Weather Manager/Run Biome Difference Acceptance")]
        public static void RunBiomeDifferenceAcceptance()
        {
            if (!Application.isPlaying)
            {
                throw new BuildFailedException("Biome difference acceptance must run in Editor Play Mode.");
            }

            var simulations = Object.FindObjectsByType<DisasterSimulation>(FindObjectsInactive.Include);
            if (simulations.Length != 1)
            {
                throw new BuildFailedException($"Biome acceptance requires one simulation, found {simulations.Length}.");
            }

            var snapshot = simulations[0].RunBiomeDifferenceAcceptance();
            Debug.Log(
                $"WEATHER_MANAGER_BIOME_ACCEPTANCE:pass={snapshot.Passed}:zones={snapshot.ZoneCount}"
                + $":forestFire={snapshot.ForestFire:F3}:farmlandFire={snapshot.FarmlandFire:F3}"
                + $":townFlood={snapshot.TownFlood:F3}:lowlandFlood={snapshot.LowlandFlood:F3}"
                + $":townLightning={snapshot.TownLightning:F3}:highlandLightning={snapshot.HighlandLightning:F3}"
                + $":waterFire={snapshot.WaterFire:F3}");
            if (!snapshot.Passed)
            {
                throw new BuildFailedException("Biome difference acceptance failed.");
            }
        }

        [MenuItem("Weather Manager/Biome Demos/01 Town Lightning")]
        private static void DemoTown() => RunBiomeDemo(BiomeType.Town, WeatherType.Electric);

        [MenuItem("Weather Manager/Biome Demos/02 Forest Heat")]
        private static void DemoForest() => RunBiomeDemo(BiomeType.Forest, WeatherType.Heat);

        [MenuItem("Weather Manager/Biome Demos/03 Farmland Heat")]
        private static void DemoFarmland() => RunBiomeDemo(BiomeType.Farmland, WeatherType.Heat);

        [MenuItem("Weather Manager/Biome Demos/04 Highland Snow")]
        private static void DemoHighland() => RunBiomeDemo(BiomeType.Highland, WeatherType.Snow);

        [MenuItem("Weather Manager/Biome Demos/05 Lowland Rain")]
        private static void DemoLowland() => RunBiomeDemo(BiomeType.Lowland, WeatherType.Rain);

        private static void RunBiomeDemo(BiomeType biome, WeatherType weather)
        {
            if (!Application.isPlaying)
            {
                throw new BuildFailedException("Biome demos must run in Editor Play Mode.");
            }
            var simulations = Object.FindObjectsByType<DisasterSimulation>(FindObjectsInactive.Include);
            var cameras = Object.FindObjectsByType<CameraController>(FindObjectsInactive.Include);
            var castingControllers = Object.FindObjectsByType<WeatherCastingController>(FindObjectsInactive.Include);
            var userInterfaces = Object.FindObjectsByType<UIController>(FindObjectsInactive.Include);
            if (simulations.Length != 1
                || cameras.Length != 1
                || castingControllers.Length != 1
                || userInterfaces.Length != 1)
            {
                throw new BuildFailedException("Biome demo requires one simulation, camera, casting controller, and UI.");
            }
            userInterfaces[0].ResetTransientFeedback();
            castingControllers[0].SetWeather(weather);
            var center = simulations[0].RunBiomeVisualDemo(biome, weather);
            cameras[0].FocusOn(center, biome == BiomeType.Town ? 42f : 36f);
            var stats = simulations[0].Stats;
            Debug.Log(
                $"WEATHER_MANAGER_BIOME_DEMO:biome={biome}:weather={weather}"
                + $":fires={stats.ActiveFires}:flood={stats.FloodedCells}:snow={stats.SnowyCells}"
                + $":destroyed={stats.DestroyedObjects}:blackout={stats.Blackout}");
        }

        [MenuItem("Weather Manager/Run Selected Weather Feedback")]
        public static void RunSelectedWeatherFeedback()
        {
            if (!Application.isPlaying)
            {
                throw new BuildFailedException("Weather feedback acceptance must run in Editor Play Mode.");
            }

            var simulations = Object.FindObjectsByType<DisasterSimulation>(FindObjectsInactive.Include);
            var castingControllers = Object.FindObjectsByType<WeatherCastingController>(FindObjectsInactive.Include);
            var userInterfaces = Object.FindObjectsByType<UIController>(FindObjectsInactive.Include);
            if (simulations.Length != 1 || castingControllers.Length != 1 || userInterfaces.Length != 1)
            {
                throw new BuildFailedException(
                    "Weather feedback acceptance requires one simulation, casting controller, and UI controller.");
            }

            var weather = castingControllers[0].SelectedWeather;
            var snapshot = simulations[0].RunWeatherFeedbackAcceptance(weather);
            userInterfaces[0].SetSelectedWeather(weather);
            userInterfaces[0].SetCasting(true);
            Debug.Log(
                $"WEATHER_MANAGER_WEATHER_ACCEPTANCE:weather={snapshot.Weather}:pass={snapshot.Passed}"
                + ":capture=editor-deterministic-runtime-api"
                + $":seconds={snapshot.SimulatedSeconds:F1}:fires={snapshot.ActiveFires}"
                + $":flooded={snapshot.FloodedCells}:snowy={snapshot.SnowyCells}"
                + $":tornadoes={snapshot.Tornadoes}:fireTornadoes={snapshot.FireTornadoes}"
                + $":destroyed={snapshot.DestroyedObjects}:blackout={snapshot.Blackout}"
                + $":alive={snapshot.AliveResidents}:injured={snapshot.InjuredResidents}:dead={snapshot.DeadResidents}");
            if (!snapshot.Passed)
            {
                throw new BuildFailedException($"{weather} feedback acceptance failed.");
            }
        }

        [MenuItem("Weather Manager/Build macOS Player %#&b")]
        public static void BuildMac()
        {
            if (!File.Exists(ScenePath))
            {
                throw new BuildFailedException($"Saved editable scene is missing: {ScenePath}");
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Directory.CreateDirectory(Path.GetDirectoryName(MacBuildPath) ?? "Builds/macOS");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = MacBuildPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development | BuildOptions.CleanBuildCache
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Weather Manager build failed: {report.summary.result}, errors={report.summary.totalErrors}");
            }
            Debug.Log($"WEATHER_MANAGER_BUILD_PASS:{report.summary.outputPath}:{report.summary.totalSize}");
        }

        private static GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            return root;
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static GameObject BuildControllerRay(Transform parent, string name, bool rightHand)
        {
            var controller = CreateChild(parent, name);
            controller.transform.localPosition = rightHand
                ? new Vector3(0.24f, 1.28f, 0.28f)
                : new Vector3(-0.24f, 1.28f, 0.28f);
            var ray = CreateChild(controller.transform, rightHand ? "Right Hand Ray" : "Left Hand Ray");
            var line = ray.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPositions(new[] { Vector3.zero, Vector3.forward * 1.5f });
            line.startWidth = 0.004f;
            line.endWidth = 0.0015f;
            line.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            line.startColor = rightHand ? new Color(0.2f, 0.82f, 1f, 0.85f) : new Color(0.45f, 0.8f, 1f, 0.6f);
            line.endColor = new Color(1f, 1f, 1f, 0f);
            return controller;
        }

        private static void ConfigureTrackedPoseDriver(
            GameObject target,
            TrackedPoseDriver.DeviceType deviceType,
            TrackedPoseDriver.TrackedPose pose)
        {
            var driver = target.AddComponent<TrackedPoseDriver>();
            if (!driver.SetPoseSource(deviceType, pose))
            {
                throw new BuildFailedException(
                    $"Unable to configure tracked pose for {target.name}: {deviceType}/{pose}.");
            }

            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            // Keep the authored desktop pose only as a no-device fallback.  When
            // PICO has an active session, the device pose directly drives this node.
            driver.UseRelativeTransform = false;
        }

        private static Camera BuildCamera(GameObject cameraRoot)
        {
            cameraRoot.tag = "MainCamera";
            // XR camera transforms begin at the neutral tracking origin. The
            // desktop overview is applied later by CameraController only for
            // a confirmed no-XR fallback. Authoring the old overview here
            // made the first PICO tabletop anchor inherit (0,56,-67) and
            // disappear outside the headset view.
            cameraRoot.transform.localPosition = Vector3.zero;
            cameraRoot.transform.localRotation = Quaternion.identity;
            var camera = cameraRoot.AddComponent<Camera>();
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 280f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.48f, 0.76f, 0.86f);
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.allowHDR = true;
            camera.allowMSAA = true;
            cameraRoot.AddComponent<AudioListener>();
            return camera;
        }

        private static void BuildLighting(GameObject lightingRoot, out Light sun, out Light moonFill)
        {
            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(lightingRoot.transform, false);
            sunObject.transform.rotation = Quaternion.Euler(46f, -32f, 0f);
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.78f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            RenderSettings.sun = sun;

            var moonObject = new GameObject("MoonFill");
            moonObject.transform.SetParent(lightingRoot.transform, false);
            moonObject.transform.rotation = Quaternion.Euler(58f, 145f, 0f);
            moonFill = moonObject.AddComponent<Light>();
            moonFill.type = LightType.Directional;
            moonFill.color = new Color(0.32f, 0.48f, 0.85f);
            moonFill.intensity = 0f;
            moonFill.shadows = LightShadows.None;
        }

        private static void ReorganizeWorld(
            Transform environmentRoot,
            Transform buildingsRoot,
            Transform vegetationRoot,
            Transform residentsRoot)
        {
            var generated = environmentRoot.Find("WeatherManager_GeneratedWorld");
            if (generated == null) throw new BuildFailedException("WorldBuilder did not create an editable world root.");

            var staticRoot = generated.Find("Static Environment");
            MoveChildren(staticRoot, environmentRoot);

            var damageableRoot = generated.Find("Damageable World");
            if (damageableRoot != null)
            {
                var children = SnapshotChildren(damageableRoot);
                foreach (var child in children)
                {
                    var destination = child.name.StartsWith("Building")
                        ? buildingsRoot
                        : child.name.StartsWith("Tree") || child.name.StartsWith("Farm")
                            ? vegetationRoot
                            : environmentRoot;
                    child.SetParent(destination, true);
                }
            }

            var generatedResidents = generated.Find("Residents");
            MoveChildren(generatedResidents, residentsRoot);
            Object.DestroyImmediate(generated.gameObject);
        }

        private static void MoveNightLights(Transform gameplayRoot, Transform lightingRoot)
        {
            var nightLights = gameplayRoot.Find("NightLights");
            if (nightLights != null) nightLights.SetParent(lightingRoot, true);
        }

        private static void NormalizeEventSystems(Transform uiRoot)
        {
            var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            EventSystem keeper = null;
            foreach (var eventSystem in eventSystems)
            {
                if (eventSystem == null) continue;
                if (keeper == null)
                {
                    keeper = eventSystem;
                    keeper.name = "EventSystem";
                    keeper.transform.SetParent(uiRoot, false);
                    continue;
                }
                Object.DestroyImmediate(eventSystem.gameObject);
            }

            if (keeper == null)
            {
                throw new BuildFailedException("UIController did not create the required EventSystem.");
            }
        }

        private static void MoveChildren(Transform source, Transform destination)
        {
            if (source == null) return;
            var children = SnapshotChildren(source);
            foreach (var child in children) child.SetParent(destination, true);
        }

        private static List<Transform> SnapshotChildren(Transform parent)
        {
            var children = new List<Transform>(parent != null ? parent.childCount : 0);
            if (parent == null) return children;
            for (var index = 0; index < parent.childCount; index++) children.Add(parent.GetChild(index));
            return children;
        }

        private static void AssetizeSceneResources(Scene scene)
        {
            var meshAssets = new Dictionary<Mesh, Mesh>();
            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                if (filter.gameObject.scene != scene || filter.sharedMesh == null) continue;
                filter.sharedMesh = PersistMesh(filter.sharedMesh, meshAssets);
            }
            foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include))
            {
                if (collider.gameObject.scene != scene || collider.sharedMesh == null) continue;
                collider.sharedMesh = PersistMesh(collider.sharedMesh, meshAssets);
            }

            var materialAssets = new Dictionary<Material, Material>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                if (renderer.gameObject.scene != scene) continue;
                var materials = renderer.sharedMaterials;
                for (var index = 0; index < materials.Length; index++)
                {
                    if (materials[index] != null) materials[index] = PersistMaterial(materials[index], materialAssets);
                }
                renderer.sharedMaterials = materials;
            }
            RebindSerializedAssetReferences(scene, meshAssets, materialAssets);
        }

        private static void RebindSerializedAssetReferences(
            Scene scene,
            IReadOnlyDictionary<Mesh, Mesh> meshes,
            IReadOnlyDictionary<Material, Material> materials)
        {
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (behaviour == null || behaviour.gameObject.scene != scene) continue;
                var serialized = new SerializedObject(behaviour);
                var property = serialized.GetIterator();
                var enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var reference = property.objectReferenceValue;
                    if (reference is Mesh mesh && meshes.TryGetValue(mesh, out var meshAsset))
                    {
                        property.objectReferenceValue = meshAsset;
                    }
                    else if (reference is Material material
                        && materials.TryGetValue(material, out var materialAsset))
                    {
                        property.objectReferenceValue = materialAsset;
                    }
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Mesh PersistMesh(Mesh source, IDictionary<Mesh, Mesh> cache)
        {
            if (AssetDatabase.Contains(source)) return source;
            if (cache.TryGetValue(source, out var cached)) return cached;
            var path = $"{MeshDirectory}/{Sanitize(source.name)}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var copy = Object.Instantiate(source);
            copy.name = source.name;
            if (asset == null)
            {
                AssetDatabase.CreateAsset(copy, path);
                asset = copy;
            }
            else
            {
                EditorUtility.CopySerialized(copy, asset);
                Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(asset);
            }
            cache[source] = asset;
            return asset;
        }

        private static Material PersistMaterial(Material source, IDictionary<Material, Material> cache)
        {
            if (AssetDatabase.Contains(source)) return source;
            if (cache.TryGetValue(source, out var cached)) return cached;
            var path = $"{MaterialDirectory}/{Sanitize(source.name)}.mat";
            var asset = AssetDatabase.LoadAssetAtPath<Material>(path);
            var copy = new Material(source) { name = source.name };
            if (asset == null)
            {
                AssetDatabase.CreateAsset(copy, path);
                asset = copy;
            }
            else
            {
                EditorUtility.CopySerialized(copy, asset);
                Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(asset);
            }
            cache[source] = asset;
            return asset;
        }

        private static void AddEditableColliders(params Transform[] roots)
        {
            foreach (var root in roots)
            {
                foreach (Transform child in root)
                {
                    if (child.GetComponent<Collider>() != null) continue;
                    var renderers = child.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) continue;
                    var bounds = renderers[0].bounds;
                    for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
                    var collider = child.gameObject.AddComponent<BoxCollider>();
                    collider.center = child.InverseTransformPoint(bounds.center);
                    var localExtents = child.InverseTransformVector(bounds.extents);
                    collider.size = new Vector3(
                        Mathf.Abs(localExtents.x) * 2f,
                        Mathf.Abs(localExtents.y) * 2f,
                        Mathf.Abs(localExtents.z) * 2f);
                }
            }
        }

        private static void SaveReusablePrefabs(GameObject residentsRoot, GameObject vfxPoolsRoot)
        {
            if (residentsRoot.transform.childCount > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(
                    residentsRoot.transform.GetChild(0).gameObject,
                    PrefabDirectory + "/Resident.prefab");
            }
            PrefabUtility.SaveAsPrefabAsset(vfxPoolsRoot, PrefabDirectory + "/VFXPools.prefab");
        }

        private static void EnsureDirectories()
        {
            Directory.CreateDirectory(SceneDirectory);
            Directory.CreateDirectory(MeshDirectory);
            Directory.CreateDirectory(MaterialDirectory);
            Directory.CreateDirectory(PrefabDirectory);
            Directory.CreateDirectory(RuntimeMaterialDirectory);
        }

        private static void EnsureRuntimeMaterials()
        {
            CreateOrUpdateRuntimeMaterial(RuntimeMaterialDirectory + "/RuntimeLit.mat", "Universal Render Pipeline/Lit", false);
            CreateOrUpdateRuntimeMaterial(RuntimeMaterialDirectory + "/RuntimeUnlit.mat", "Universal Render Pipeline/Unlit", false);
            CreateOrUpdateRuntimeMaterial(RuntimeMaterialDirectory + "/RuntimeParticle.mat", "Universal Render Pipeline/Particles/Unlit", true);
        }

        private static void CreateOrUpdateRuntimeMaterial(string assetPath, string shaderName, bool transparent)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new BuildFailedException($"Required runtime shader is unavailable: {shaderName}");
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                material.shader = shader;
            }
            material.name = Path.GetFileNameWithoutExtension(assetPath);
            material.color = Color.white;
            material.enableInstancing = true;
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", transparent ? 1f : 0f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", transparent ? 0f : 1f);
            if (transparent)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = -1;
            }
            EditorUtility.SetDirty(material);
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "Weather Manager Studio";
            PlayerSettings.productName = "Weather Manager";
            PlayerSettings.bundleVersion = "0.2.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.weathermanager.sandbox");
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        private static string Sanitize(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "GeneratedAsset" : value.Replace('/', '_');
        }
    }
}
