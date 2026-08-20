using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherManager.Editor
{
    public static class VisualReviewCapture
    {
        private const int CaptureWidth = 2048;
        private const int CaptureHeight = 1280;
        private const string OutputDirectory = "Evidence/VisualReview/Current";

        private readonly struct ReviewView
        {
            public ReviewView(string fileName, Vector3 position, Vector3 target, float fieldOfView)
            {
                FileName = fileName;
                Position = position;
                Target = target;
                FieldOfView = fieldOfView;
            }

            public string FileName { get; }
            public Vector3 Position { get; }
            public Vector3 Target { get; }
            public float FieldOfView { get; }
        }

        [MenuItem("Weather Manager/Visual Review/Capture All Views %#&v")]
        public static void CaptureAllViews()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before capturing editable-scene review views.");
            }

            var sourceCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            if (sourceCamera == null)
            {
                throw new InvalidOperationException("The editable scene has no camera to copy render settings from.");
            }

            var outputPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, OutputDirectory);
            Directory.CreateDirectory(outputPath);

            var views = new[]
            {
                new ReviewView("01-island-oblique.png", new Vector3(75f, 69f, -82f), new Vector3(0f, 3.2f, 0f), 42f),
                new ReviewView("02-island-top.png", new Vector3(0f, 118f, -5f), new Vector3(0f, 1.5f, 0f), 45f),
                new ReviewView("03-mountain-range.png", new Vector3(27f, 30f, -23f), new Vector3(-31f, 8.2f, 18f), 37f),
                new ReviewView("04-lake-river.png", new Vector3(55f, 29f, -38f), new Vector3(20f, 1.2f, -4f), 37f),
                new ReviewView("05-town-terrace.png", new Vector3(30f, 23f, -43f), new Vector3(0f, 3.1f, -4f), 38f),
                new ReviewView("06-farmland-hills.png", new Vector3(-2f, 23f, -47f), new Vector3(-31f, 3.1f, -20f), 36f),
                new ReviewView("07-coast-cutaway.png", new Vector3(67f, 22f, -43f), new Vector3(24f, 0.8f, -17f), 36f),
                new ReviewView("08-forest-transition.png", new Vector3(37f, 27f, 31f), new Vector3(-14f, 4.8f, 14f), 38f)
            };

            var cameraObject = new GameObject("Visual Review Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var reviewCamera = cameraObject.AddComponent<Camera>();
            reviewCamera.CopyFrom(sourceCamera);
            reviewCamera.enabled = false;
            reviewCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
            reviewCamera.allowHDR = true;
            reviewCamera.allowMSAA = true;
            reviewCamera.nearClipPlane = 0.15f;
            reviewCamera.farClipPlane = 350f;

            try
            {
                foreach (var view in views)
                {
                    reviewCamera.transform.SetPositionAndRotation(
                        view.Position,
                        Quaternion.LookRotation(view.Target - view.Position, Vector3.up));
                    reviewCamera.fieldOfView = view.FieldOfView;
                    Capture(reviewCamera, Path.Combine(outputPath, view.FileName));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }

            Debug.Log($"WEATHER_MANAGER_VISUAL_REVIEW_CAPTURED:views={views.Length}:path={outputPath}");
        }

        private static void Capture(Camera camera, string path)
        {
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
                name = "Weather Manager Visual Review"
            };
            var image = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false, false);

            try
            {
                renderTexture.Create();
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                image.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0, false);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
