# Weather Manager PICO Smoke Build

Weather Manager is an editable Unity 6 URP low-poly weather-disaster sandbox.

## PICO emulator build

The release APK is an Android ARM64 IL2CPP **flat-screen fallback smoke build**:

- Package: `com.weathermanager.picomr`
- Launcher: `com.unity3d.player.UnityPlayerGameActivity`
- Interaction: weather buttons, touch/mouse casting, and an Input System Gamepad fallback (right-stick virtual cursor; right trigger or A to cast).

It has been installed and launched on the shared PICO emulator with no fatal startup crash reported.

## Scope boundary

This release does **not** claim full mixed reality support. It does not include a PICO/OpenXR loader, HMD session, tracked-controller ray interaction, hand tracking, passthrough, or spatial UI. The UI is a screen-space Unity Canvas.

## Open in Unity

1. Install Unity `6000.5.4f1` with Android Build Support.
2. Open this repository in Unity Hub.
3. Open `Assets/WeatherManager/Scenes/WeatherManager.unity`.

The scene is serialized and editable; generated cache/output directories are intentionally excluded from version control.
