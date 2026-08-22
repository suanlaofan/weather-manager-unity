using UnityEngine;

namespace WeatherManager
{
    public enum AngeEnvironmentalMotion
    {
        WindRotor,
        WaterWheel
    }

    [DisallowMultipleComponent]
    public sealed class AngeEnvironmentalAnimator : MonoBehaviour
    {
        [SerializeField] private AngeEnvironmentalMotion motion;
        [SerializeField] private DisasterSimulation simulation;
        [SerializeField] private Transform rotor;
        [SerializeField] private Vector3 localAxis = Vector3.forward;
        [SerializeField] private float idleDegreesPerSecond = 8f;
        [SerializeField] private float maximumDegreesPerSecond = 260f;
        private WorldBuilder world;

        public void Configure(
            AngeEnvironmentalMotion motionMode,
            DisasterSimulation disasterSimulation,
            Transform drivenRotor,
            Vector3 axis,
            float idleSpeed,
            float maximumSpeed)
        {
            motion = motionMode;
            simulation = disasterSimulation;
            rotor = drivenRotor;
            localAxis = axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.forward;
            idleDegreesPerSecond = idleSpeed;
            maximumDegreesPerSecond = maximumSpeed;
        }

        private void Update()
        {
            if (simulation == null || rotor == null || Time.deltaTime <= 0f)
            {
                return;
            }

            // DisasterSimulation is authored in Weather Tabletop Island local
            // coordinates. In MR this landmark is physically translated and
            // scaled with that island, so its scene-space position is meters,
            // not a valid simulation sample coordinate.
            var hazard = simulation.Sample(GetGameplayPosition());
            var intensity = motion == AngeEnvironmentalMotion.WindRotor
                ? Mathf.Clamp01(hazard.Wind)
                : Mathf.Clamp01(hazard.Flood * 1.35f + hazard.Moisture * 0.3f);
            var speed = Mathf.Lerp(idleDegreesPerSecond, maximumDegreesPerSecond, intensity);
            rotor.Rotate(localAxis, speed * Time.deltaTime, Space.Self);
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
