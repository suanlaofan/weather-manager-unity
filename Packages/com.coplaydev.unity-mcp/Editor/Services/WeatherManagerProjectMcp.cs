using System;
using System.IO;
using MCPForUnity.Editor.Services.Transport;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    // Project-local transport isolation: never rewrites host clients or global endpoint preferences.
    [InitializeOnLoad]
    public static class WeatherManagerProjectMcp
    {
        private static readonly string ConfigPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/com.coplaydev.unity-mcp/WeatherManagerLocalMcp.json"));
        private static readonly JObject Config = LoadConfig();
        public static bool Enabled => Config != null;
        public static string BaseUrl => (string)Config?["baseUrl"] ?? "http://127.0.0.1:8091";
        private static bool connecting;
        private static double nextAttempt;

        private static JObject LoadConfig()
        {
            try { return File.Exists(ConfigPath) ? JObject.Parse(File.ReadAllText(ConfigPath)) : null; }
            catch (Exception e) { Debug.LogError("Weather Manager MCP config: " + e.Message); return null; }
        }

        static WeatherManagerProjectMcp()
        {
            if (!Enabled || Application.isBatchMode || AssetDatabase.IsAssetImportWorkerProcess()) return;
            if ((bool?)Config["autoConnect"] == true) EditorApplication.update += EnsureConnected;
        }

        private static async void EnsureConnected()
        {
            if (connecting || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextAttempt) return;
            nextAttempt = EditorApplication.timeSinceStartup + 5;
            if (MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http)) return;
            connecting = true;
            try { await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http); }
            catch (Exception e) { Debug.LogWarning("Weather Manager MCP connection: " + e.Message); }
            finally { connecting = false; }
        }
    }
}
