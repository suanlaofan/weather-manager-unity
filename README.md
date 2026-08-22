# Weather Manager PICO MR

Weather Manager is an editable Unity 6 URP low-poly weather-disaster sandbox,
now packaged as a PICO tabletop mixed-reality build.

## v0.3.0 PICO MR release

The release APK is an Android ARM64 IL2CPP build with:

- PICO XR loader, XR Origin, HMD session and right-controller ray/trigger casting.
- A world-locked tabletop island placed on a detected physical table on a real
  PICO headset, with passthrough enabled there.
- A head-locked world-space dashboard that preserves its authored aspect ratio.
- Mouse/touch casting only as an emulator fallback; it is not the real-headset
  interaction path.
- A reset fix for the scaled MR tabletop: buildings, trees and vehicles restore
  their local pose, so Reset no longer sends them outside the miniature island.

### Package

- APK: `WeatherManager-PicoMR-TrueMR.apk`
- Package id: `com.weathermanager.picomr`
- Launch activity: `com.unity3d.player.UnityPlayerActivity`
- SHA-256: `21d510c7dcdce55f923da0f589cc78e832c780b584bbec4e7ae15cb2c1b4a80c`
- Android: ARM64, minSdk 29, targetSdk 36

The APK was installed and launched on the PICO emulator without a fatal app
exception. The emulator does not expose real camera passthrough, so it uses a
stable opaque tabletop fallback; passthrough and physical-plane placement still
need confirmation on a real supported PICO headset.

## Interaction

1. Aim the right controller ray at the tabletop.
2. Select a weather type from the head-locked dashboard.
3. Hold the right trigger over a valid island point to charge and cast; release
   to finish the weather event.

In the PICO emulator, host mouse/touch can be used for the fallback path.

## Open the source in Unity

1. Install Unity `6000.5.4f1` with Android Build Support.
2. Download PICO Unity Integration 3.4.0 from PICO's official developer
   distribution and install it at `Packages/com.unity.xr.picoxr/`.
   It is deliberately not committed: the vendor license forbids redistributing
   the local SDK copy.
3. Open the repository in Unity Hub, then open
   `Assets/WeatherManager/Scenes/WeatherManager.unity`.

The scene, XR settings, generated materials and gameplay code are serialized
and versioned. Unity caches, local SDK binaries, evidence logs and build output
are intentionally excluded.
