# PICO Unity Integration prerequisite

`manifest.json` intentionally resolves `com.unity.xr.picoxr` from the local
directory `Packages/com.unity.xr.picoxr`.

Before opening this project, install PICO Unity Integration **3.4.0** from the
official PICO developer distribution into that directory. The SDK itself is
not included in this repository because its vendor license prohibits
redistributing the local package copy.

After installation, open the project in Unity `6000.5.4f1` with Android Build
Support and open `Assets/WeatherManager/Scenes/WeatherManager.unity`.

## Optional Editor debugger configuration

If PICO 3.4.0 throws a NullReferenceException for a missing
`PXR_PicoDebuggerSO` before entering Play Mode, apply the project
[one-line null guard](../Docs/PICO-optional-debug-config.patch) to your local
SDK copy (`git apply Docs/PICO-optional-debug-config.patch`). The release APK
uses normal player build options and does not enable the PICO development
debugger. The SDK itself remains outside this repository.
