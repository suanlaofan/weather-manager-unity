using System;
using UnityEngine;

namespace WeatherManager
{
    public sealed class ResidentAgent : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock propertyBlock;

        [SerializeField] private Transform body;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftArm;
        [SerializeField] private Transform rightArm;
        [SerializeField] private Transform leftLeg;
        [SerializeField] private Transform rightLeg;
        [SerializeField] private LowPolyTerrainGenerator terrain;
        // Residents simulate in the authored tabletop coordinate system.  The
        // PICO MR presentation moves and scales that tabletop into metres, so
        // reading Transform.position directly would make Reset and movement
        // escape the island after MR activates.
        private WorldBuilder world;
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();
        [SerializeField] private Color[] baseColors = Array.Empty<Color>();
        [SerializeField] private Vector3 initialPosition;
        [SerializeField] private Quaternion initialRotation;
        [SerializeField] private Vector3 initialScale;
        [SerializeField] private Vector3 bodyBasePosition;
        [SerializeField] private Vector3 headBasePosition;
        [SerializeField] private Quaternion leftArmBaseRotation;
        [SerializeField] private Quaternion rightArmBaseRotation;
        [SerializeField] private Quaternion leftLegBaseRotation;
        [SerializeField] private Quaternion rightLegBaseRotation;
        [SerializeField] private Vector3 homePosition;
        private Vector3 wanderTarget;
        private Quaternion deadRotation;
        [SerializeField] private float worldRadiusX = 21.5f;
        [SerializeField] private float worldRadiusZ = 15.5f;
        private float health = 100f;
        private float animationTime;
        private float wanderTimer;
        private float visualLean;
        private uint randomState;
        [SerializeField] private int agentId;
        [SerializeField] private bool configured;
        [SerializeField] private ResidentLifeState state = ResidentLifeState.Calm;

        public ResidentLifeState State => state;
        public float Health => health;
        public bool IsAlive => State != ResidentLifeState.Dead;
        public bool IsInjured => State == ResidentLifeState.Injured;
        public bool IsFleeing => State == ResidentLifeState.Fleeing;

        private void Awake()
        {
            InitializeBakedRuntime();
        }

        public void InitializeBakedRuntime()
        {
            ResolveWorld();
            if (terrain == null)
            {
                terrain = FindFirstObjectByType<LowPolyTerrainGenerator>(FindObjectsInactive.Include);
            }
            EnsureVisualState();
            if (!configured)
            {
                RecoverConfigurationFromHierarchy();
            }

            if (!configured)
            {
                return;
            }

            EnsureVisualState();

            ResetAgent();
        }

        public void SetTerrain(LowPolyTerrainGenerator terrainSurface)
        {
            terrain = terrainSurface;
        }

        public float SnapToTerrain()
        {
            if (terrain == null) return 0f;
            var position = GetGameplayPosition();
            var before = position.y;
            position.y = terrain.SampleHeight(position) + 0.09f;
            SetGameplayPosition(position);
            homePosition.y = position.y;
            initialPosition.y = position.y;
            wanderTarget.y = position.y;
            return Mathf.Abs(before - position.y);
        }

        public void Configure(
            int id,
            Vector3 home,
            float radiusX,
            float radiusZ,
            Transform bodyTransform,
            Transform headTransform,
            Transform leftArmTransform,
            Transform rightArmTransform,
            Transform leftLegTransform,
            Transform rightLegTransform,
            Renderer[] residentRenderers)
        {
            propertyBlock ??= new MaterialPropertyBlock();
            agentId = id;
            homePosition = home;
            worldRadiusX = Mathf.Max(1f, radiusX);
            worldRadiusZ = Mathf.Max(1f, radiusZ);
            body = bodyTransform;
            head = headTransform;
            leftArm = leftArmTransform;
            rightArm = rightArmTransform;
            leftLeg = leftLegTransform;
            rightLeg = rightLegTransform;
            renderers = residentRenderers ?? Array.Empty<Renderer>();
            CaptureBaseColors();

            initialPosition = home;
            initialRotation = GetGameplayRotation();
            initialScale = transform.localScale;
            bodyBasePosition = body != null ? body.localPosition : Vector3.zero;
            headBasePosition = head != null ? head.localPosition : Vector3.zero;
            leftArmBaseRotation = leftArm != null ? leftArm.localRotation : Quaternion.identity;
            rightArmBaseRotation = rightArm != null ? rightArm.localRotation : Quaternion.identity;
            leftLegBaseRotation = leftLeg != null ? leftLeg.localRotation : Quaternion.identity;
            rightLegBaseRotation = rightLeg != null ? rightLeg.localRotation : Quaternion.identity;
            randomState = unchecked((uint)(0x9E3779B9u + id * 747796405u));
            configured = true;
            ResetAgent();
        }

        public void Tick(float delta, DisasterSimulation simulation)
        {
            if (!configured || simulation == null || delta <= 0f)
            {
                return;
            }

            animationTime += delta;
            if (State == ResidentLifeState.Dead)
            {
                AnimateDead(delta);
                return;
            }

            var hazard = simulation.Sample(GetGameplayPosition());
            ApplyHazard(delta, hazard);
            if (health <= 0f)
            {
                SetState(ResidentLifeState.Dead);
                AnimateDead(delta);
                return;
            }

            if (hazard.Threat < 0.05f && health < 100f)
            {
                health = Mathf.Min(100f, health + delta * 0.42f);
            }

            var nextState = ResolveState(hazard);
            SetState(nextState);
            Move(delta, hazard);
            AnimateWalking(delta);
        }

        public void ResetAgent()
        {
            if (!configured)
            {
                return;
            }

            health = 100f;
            animationTime = agentId * 0.173f;
            wanderTimer = 0f;
            visualLean = 0f;
            randomState = InitialRandomState(agentId);
            state = ResidentLifeState.Calm;
            SetGameplayPosition(initialPosition);
            SetGameplayRotation(initialRotation);
            transform.localScale = initialScale;
            deadRotation = initialRotation;
            if (body != null) body.localPosition = bodyBasePosition;
            if (head != null) head.localPosition = headBasePosition;
            if (leftArm != null) leftArm.localRotation = leftArmBaseRotation;
            if (rightArm != null) rightArm.localRotation = rightArmBaseRotation;
            if (leftLeg != null) leftLeg.localRotation = leftLegBaseRotation;
            if (rightLeg != null) rightLeg.localRotation = rightLegBaseRotation;
            PickWanderTarget();
            ApplyStateTint();
        }

        private void ApplyHazard(float delta, HazardSample hazard)
        {
            var damagePerSecond = hazard.Fire * 22f
                + Mathf.Max(0f, hazard.Flood - 0.2f) * 10f
                + Mathf.Max(0f, hazard.Snow - 0.42f) * 3.2f
                + Mathf.Max(0f, hazard.Heat - 0.45f) * 7.5f
                + Mathf.Max(0f, hazard.Wind - 0.28f) * 14f
                + hazard.Electric * 34f;
            health = Mathf.Max(0f, health - damagePerSecond * delta);
        }

        private ResidentLifeState ResolveState(HazardSample hazard)
        {
            if (health <= 0f)
            {
                return ResidentLifeState.Dead;
            }

            if (health < 48f || hazard.Electric > 0.34f)
            {
                return ResidentLifeState.Injured;
            }

            return hazard.Threat > 0.14f ? ResidentLifeState.Fleeing : ResidentLifeState.Calm;
        }

        private void SetState(ResidentLifeState nextState)
        {
            if (State == nextState)
            {
                return;
            }

            state = nextState;
            if (State == ResidentLifeState.Dead)
            {
                var yaw = GetGameplayRotation().eulerAngles.y;
                var fallDirection = (agentId & 1) == 0 ? 88f : -88f;
                deadRotation = Quaternion.Euler(0f, yaw, fallDirection);
            }

            ApplyStateTint();
        }

        private void Move(float delta, HazardSample hazard)
        {
            var currentPosition = GetGameplayPosition();
            Vector3 direction;
            float speed;
            if (State == ResidentLifeState.Fleeing || (State == ResidentLifeState.Injured && hazard.Threat > 0.08f))
            {
                direction = hazard.EscapeDirection;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.01f)
                {
                    direction = Quaternion.Euler(0f, agentId * 47f, 0f) * Vector3.forward;
                }

                direction.Normalize();
                speed = State == ResidentLifeState.Injured ? 0.78f : 2.05f;
            }
            else
            {
                wanderTimer -= delta;
                var toTarget = wanderTarget - currentPosition;
                toTarget.y = 0f;
                if (wanderTimer <= 0f || toTarget.sqrMagnitude < 0.18f)
                {
                    PickWanderTarget();
                    toTarget = wanderTarget - currentPosition;
                    toTarget.y = 0f;
                }

                direction = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : Vector3.zero;
                speed = State == ResidentLifeState.Injured ? 0.44f : 0.66f;
            }

            var nextPosition = currentPosition + direction * (speed * delta);
            nextPosition.y = terrain != null ? terrain.SampleHeight(nextPosition) + 0.09f : initialPosition.y;
            nextPosition = ClampToIsland(nextPosition, 0.91f);
            SetGameplayPosition(nextPosition);
            if (direction.sqrMagnitude > 0.001f)
            {
                var lean = State == ResidentLifeState.Injured ? ((agentId & 1) == 0 ? 7f : -7f) : 0f;
                visualLean = Mathf.Lerp(visualLean, lean, 1f - Mathf.Exp(-delta * 6f));
                var targetRotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, 0f, visualLean);
                SetGameplayRotation(Quaternion.Slerp(
                    GetGameplayRotation(),
                    targetRotation,
                    1f - Mathf.Exp(-delta * 9f)));
            }
        }

        private void AnimateWalking(float delta)
        {
            var fleeing = State == ResidentLifeState.Fleeing;
            var injured = State == ResidentLifeState.Injured;
            var frequency = fleeing ? 12f : injured ? 5.2f : 6.5f;
            var amplitude = fleeing ? 52f : injured ? 18f : 26f;
            var stride = Mathf.Sin(animationTime * frequency) * amplitude;
            if (injured && stride < 0f) stride *= 0.35f;
            if (leftArm != null) leftArm.localRotation = leftArmBaseRotation * Quaternion.Euler(stride, 0f, 0f);
            if (rightArm != null) rightArm.localRotation = rightArmBaseRotation * Quaternion.Euler(-stride, 0f, 0f);
            if (leftLeg != null) leftLeg.localRotation = leftLegBaseRotation * Quaternion.Euler(-stride, 0f, 0f);
            if (rightLeg != null) rightLeg.localRotation = rightLegBaseRotation * Quaternion.Euler(stride, 0f, 0f);

            var bob = Mathf.Abs(Mathf.Sin(animationTime * frequency)) * (fleeing ? 0.035f : 0.018f);
            if (body != null)
            {
                body.localPosition = Vector3.Lerp(body.localPosition, bodyBasePosition + Vector3.up * bob, 1f - Mathf.Exp(-delta * 14f));
            }
            if (head != null)
            {
                head.localPosition = Vector3.Lerp(head.localPosition, headBasePosition + Vector3.up * bob, 1f - Mathf.Exp(-delta * 14f));
            }
        }

        private void AnimateDead(float delta)
        {
            SetGameplayRotation(Quaternion.Slerp(
                GetGameplayRotation(),
                deadRotation,
                1f - Mathf.Exp(-delta * 8f)));
            if (body != null)
            {
                body.localPosition = Vector3.Lerp(body.localPosition, bodyBasePosition, 1f - Mathf.Exp(-delta * 8f));
            }
            if (head != null)
            {
                head.localPosition = Vector3.Lerp(head.localPosition, headBasePosition, 1f - Mathf.Exp(-delta * 8f));
            }
            if (leftArm != null) leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation, leftArmBaseRotation, 1f - Mathf.Exp(-delta * 8f));
            if (rightArm != null) rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation, rightArmBaseRotation, 1f - Mathf.Exp(-delta * 8f));
            if (leftLeg != null) leftLeg.localRotation = Quaternion.Slerp(leftLeg.localRotation, leftLegBaseRotation, 1f - Mathf.Exp(-delta * 8f));
            if (rightLeg != null) rightLeg.localRotation = Quaternion.Slerp(rightLeg.localRotation, rightLegBaseRotation, 1f - Mathf.Exp(-delta * 8f));
        }

        private void PickWanderTarget()
        {
            var angle = Next01() * Mathf.PI * 2f;
            var distance = Mathf.Lerp(0.7f, 3.1f, Next01());
            wanderTarget = homePosition + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            wanderTarget.y = terrain != null ? terrain.SampleHeight(wanderTarget) + 0.09f : initialPosition.y;
            wanderTarget = ClampToIsland(wanderTarget, 0.84f);
            wanderTimer = Mathf.Lerp(2.2f, 6.5f, Next01());
        }

        private Vector3 ClampToIsland(Vector3 position, float margin)
        {
            var normalized = new Vector2(position.x / worldRadiusX, position.z / worldRadiusZ);
            if (normalized.sqrMagnitude > margin * margin)
            {
                normalized = normalized.normalized * margin;
                position.x = normalized.x * worldRadiusX;
                position.z = normalized.y * worldRadiusZ;
            }
            return position;
        }

        private float Next01()
        {
            randomState = randomState * 1664525u + 1013904223u;
            return (randomState & 0x00FFFFFFu) / 16777215f;
        }

        private void RecoverConfigurationFromHierarchy()
        {
            body ??= transform.Find("Body");
            head ??= transform.Find("Head");
            leftArm ??= transform.Find("Left Arm");
            rightArm ??= transform.Find("Right Arm");
            leftLeg ??= transform.Find("Left Leg");
            rightLeg ??= transform.Find("Right Leg");
            renderers = GetComponentsInChildren<Renderer>(true);
            if (body == null || head == null || renderers.Length == 0)
            {
                return;
            }

            if (agentId <= 0)
            {
                agentId = transform.GetSiblingIndex() + 1;
            }
            homePosition = GetGameplayPosition();
            initialPosition = homePosition;
            initialRotation = GetGameplayRotation();
            initialScale = transform.localScale;
            bodyBasePosition = body.localPosition;
            headBasePosition = head.localPosition;
            leftArmBaseRotation = leftArm != null ? leftArm.localRotation : Quaternion.identity;
            rightArmBaseRotation = rightArm != null ? rightArm.localRotation : Quaternion.identity;
            leftLegBaseRotation = leftLeg != null ? leftLeg.localRotation : Quaternion.identity;
            rightLegBaseRotation = rightLeg != null ? rightLeg.localRotation : Quaternion.identity;
            CaptureBaseColors();
            configured = true;
        }

        private void CaptureBaseColors()
        {
            renderers ??= Array.Empty<Renderer>();
            baseColors = new Color[renderers.Length];
            for (var index = 0; index < renderers.Length; index++)
            {
                baseColors[index] = ReadRendererColor(renderers[index]);
            }
        }

        private static uint InitialRandomState(int id)
        {
            return unchecked(0x9E3779B9u + (uint)Mathf.Max(1, id) * 747796405u);
        }

        private void ApplyStateTint()
        {
            EnsureVisualState();
            var tint = State switch
            {
                ResidentLifeState.Injured => new Color(0.92f, 0.64f, 0.62f),
                ResidentLifeState.Dead => new Color(0.38f, 0.39f, 0.4f),
                ResidentLifeState.Fleeing => new Color(1f, 0.94f, 0.86f),
                _ => Color.white
            };
            var count = Mathf.Min(renderers.Length, baseColors.Length);
            for (var index = 0; index < count; index++)
            {
                var renderer = renderers[index];
                if (renderer == null) continue;
                propertyBlock.Clear();
                var color = baseColors[index] * tint;
                color.a = baseColors[index].a;
                propertyBlock.SetColor(BaseColorId, color);
                propertyBlock.SetColor(ColorId, color);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void EnsureVisualState()
        {
            propertyBlock ??= new MaterialPropertyBlock();
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }
            if (baseColors == null || baseColors.Length != renderers.Length)
            {
                CaptureBaseColors();
            }
        }

        private WorldBuilder ResolveWorld()
        {
            if (world == null)
            {
                world = FindFirstObjectByType<WorldBuilder>(FindObjectsInactive.Include);
            }

            return world;
        }

        private Vector3 GetGameplayPosition()
        {
            var worldBuilder = ResolveWorld();
            return worldBuilder != null
                ? worldBuilder.GetGameplayPosition(transform)
                : transform.position;
        }

        private Quaternion GetGameplayRotation()
        {
            var worldBuilder = ResolveWorld();
            return worldBuilder != null
                ? worldBuilder.GetGameplayRotation(transform)
                : transform.rotation;
        }

        private void SetGameplayPosition(Vector3 position)
        {
            var worldBuilder = ResolveWorld();
            if (worldBuilder != null)
            {
                worldBuilder.SetGameplayPosition(transform, position);
                return;
            }

            transform.position = position;
        }

        private void SetGameplayRotation(Quaternion rotation)
        {
            var worldBuilder = ResolveWorld();
            if (worldBuilder != null)
            {
                worldBuilder.SetGameplayRotation(transform, rotation);
                return;
            }

            transform.rotation = rotation;
        }

        private static Color ReadRendererColor(Renderer renderer)
        {
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null) return Color.white;
            if (material.HasProperty(BaseColorId)) return material.GetColor(BaseColorId);
            if (material.HasProperty(ColorId)) return material.GetColor(ColorId);
            return Color.white;
        }
    }
}
