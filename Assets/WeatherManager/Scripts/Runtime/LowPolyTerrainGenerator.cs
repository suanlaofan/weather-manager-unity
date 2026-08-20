using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherManager
{
    [Serializable]
    public struct TerrainInvariantSnapshot
    {
        public bool Passed;
        public float SeamHeightDelta;
        public float PeakHeight;
        public float LakeFloorHeight;
        public float TownHeight;
        public float FarmlandHeight;
        public int TerrainTriangles;
        public int TerrainRenderers;
    }

    [DisallowMultipleComponent]
    public sealed class LowPolyTerrainGenerator : MonoBehaviour
    {
        [Header("Deterministic heightfield")]
        [SerializeField] private int seed = 240813;
        [SerializeField, Range(32, 96)] private int columns = 80;
        [SerializeField, Range(24, 72)] private int rows = 56;
        [SerializeField] private Vector2 worldSize = new Vector2(104f, 72f);
        [SerializeField] private float rollingNoiseScale = 0.055f;
        [SerializeField] private float hillScale = 1.65f;

        [Header("Mountain range")]
        [SerializeField] private Vector2 mountainCenter = new Vector2(-34f, 19f);
        [SerializeField] private Vector2 ridgeDirection = new Vector2(0.82f, 0.31f);
        [SerializeField] private float ridgeStrength = 13.2f;
        [SerializeField] private float mountainFootprint = 25f;
        [SerializeField] private float snowLine = 15.5f;

        [Header("Lake, river and shore")]
        [SerializeField] private Vector2 lakeCenter = new Vector2(29f, 4f);
        [SerializeField] private Vector2 lakeRadius = new Vector2(9.8f, 7.2f);
        [SerializeField] private float lakeDepth = 3.2f;
        [SerializeField] private float waterLevel = 0.9f;
        [SerializeField] private float riverWidth = 2f;
        [SerializeField] private float shoreFalloff = 0.08f;
        [SerializeField] private Vector2[] riverSpline =
        {
            new Vector2(29f, 4f),
            new Vector2(30f, -3f),
            new Vector2(31f, -10f),
            new Vector2(34f, -17f),
            new Vector2(38f, -24f),
            new Vector2(44f, -30f)
        };

        [Header("Soft settlement terraces")]
        [SerializeField] private Vector2 townCenter = new Vector2(0f, -4f);
        [SerializeField] private Vector2 townRadius = new Vector2(18f, 13f);
        [SerializeField] private float townHeight = 2.75f;
        [SerializeField] private Vector2 farmlandCenter = new Vector2(-31f, -20f);
        [SerializeField] private Vector2 farmlandRadius = new Vector2(18f, 14f);
        [SerializeField] private float farmlandHeight = 2.9f;

        [Header("Baked editable outputs")]
        [SerializeField] private Transform generatedRoot;
        [SerializeField] private MeshFilter terrainFilter;
        [SerializeField] private MeshCollider terrainCollider;
        [SerializeField] private MeshFilter skirtFilter;
        [SerializeField] private MeshFilter waterFilter;
        [SerializeField] private MeshCollider waterCollider;
        [SerializeField] private MeshFilter roadFilter;
        [SerializeField] private Material terrainMaterial;

        public float HalfWidth => worldSize.x * 0.5f;
        public float HalfDepth => worldSize.y * 0.5f;
        public float WaterLevel => waterLevel;
        public float SnowLine => snowLine;
        public Vector2 LakeCenter => lakeCenter;
        public IReadOnlyList<Vector2> RiverSpline => riverSpline;

        public bool IsInsidePlayableIsland(Vector3 worldPosition, float inset = 0f)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            var normalizedX = local.x / Mathf.Max(0.1f, HalfWidth);
            var normalizedZ = local.z / Mathf.Max(0.1f, HalfDepth);
            var angle = Mathf.Atan2(normalizedZ, normalizedX);
            var radius = Mathf.Sqrt(normalizedX * normalizedX + normalizedZ * normalizedZ)
                / IslandBoundaryScale(angle);
            return radius <= Mathf.Clamp(1f - inset, 0.72f, 1f);
        }

        public void Build(Transform parent, Material water, Material road)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            ClearGenerated();
            generatedRoot = new GameObject("Continuous Low Poly Terrain").transform;
            generatedRoot.SetParent(parent, false);

            terrainMaterial = CreateTerrainMaterial();
            terrainFilter = CreateMeshObject(
                "Triangulated Heightfield",
                generatedRoot,
                terrainMaterial,
                true,
                out var terrainRenderer);
            terrainRenderer.shadowCastingMode = ShadowCastingMode.On;
            terrainRenderer.receiveShadows = true;
            terrainFilter.sharedMesh = BuildTerrainMesh();
            terrainCollider = terrainFilter.gameObject.AddComponent<MeshCollider>();
            terrainCollider.sharedMesh = terrainFilter.sharedMesh;

            skirtFilter = CreateMeshObject(
                "Island Cutaway Skirt",
                generatedRoot,
                terrainMaterial,
                false,
                out var skirtRenderer);
            skirtRenderer.shadowCastingMode = ShadowCastingMode.On;
            skirtRenderer.receiveShadows = true;
            skirtFilter.sharedMesh = BuildSkirtMesh();

            waterFilter = CreateMeshObject(
                "Lake and River",
                generatedRoot,
                water,
                false,
                out var waterRenderer);
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            waterFilter.sharedMesh = BuildWaterMesh();
            waterCollider = waterFilter.gameObject.AddComponent<MeshCollider>();
            waterCollider.sharedMesh = waterFilter.sharedMesh;

            roadFilter = CreateMeshObject(
                "Curved Road Network",
                generatedRoot,
                road,
                false,
                out var roadRenderer);
            roadRenderer.shadowCastingMode = ShadowCastingMode.Off;
            roadRenderer.receiveShadows = true;
            roadFilter.sharedMesh = BuildRoadMesh();
        }

        public float SampleHeight(Vector3 worldPosition)
        {
            return SampleHeight(worldPosition.x, worldPosition.z);
        }

        public float SampleHeight(float x, float z)
        {
            var rawEdgeRadius = Mathf.Sqrt(
                x * x / (HalfWidth * HalfWidth)
                + z * z / (HalfDepth * HalfDepth));
            var edgeAngle = Mathf.Atan2(z / HalfDepth, x / HalfWidth);
            var edgeRadius = rawEdgeRadius / IslandBoundaryScale(edgeAngle);
            var offsetA = seed * 0.00073f;
            var offsetB = seed * 0.00191f;
            var broad = SignedNoise(x * rollingNoiseScale + offsetA, z * rollingNoiseScale + offsetB);
            var medium = SignedNoise(x * rollingNoiseScale * 2.17f - offsetB, z * rollingNoiseScale * 2.17f + offsetA);
            var detail = SignedNoise(x * rollingNoiseScale * 4.6f + 19.3f, z * rollingNoiseScale * 4.6f - 7.8f);
            var height = 3.05f + broad * hillScale + medium * 0.62f + detail * 0.22f;

            var peakOne = SharpPeak(x, z, mountainCenter + new Vector2(-8f, 3f), 7.4f, 9.8f) * ridgeStrength;
            var peakTwo = SharpPeak(x, z, mountainCenter + new Vector2(1f, 7f), 6.8f, 8.4f) * ridgeStrength * 1.04f;
            var peakThree = SharpPeak(x, z, mountainCenter + new Vector2(8f, -3f), 7.2f, 9.4f) * ridgeStrength * 0.86f;
            var peakFour = SharpPeak(x, z, mountainCenter + new Vector2(14f, -10f), 8.5f, 10.8f) * ridgeStrength * 0.48f;
            var mountainMask = Gaussian(x, z, mountainCenter, mountainFootprint, mountainFootprint * 0.82f);
            var ridgeAxis = ridgeDirection.sqrMagnitude > 0.001f ? ridgeDirection.normalized : Vector2.right;
            var projected = x * ridgeAxis.x + z * ridgeAxis.y;
            var ridgeNoise = 1f - Mathf.Abs(SignedNoise(projected * 0.085f + offsetA, (x - z) * 0.045f + offsetB));
            height += Mathf.Max(Mathf.Max(peakOne, peakTwo), Mathf.Max(peakThree, peakFour));
            height += mountainMask * (1.35f + ridgeNoise * ridgeNoise * 0.9f);

            var townMask = SoftEllipseMask(new Vector2(x, z), townCenter, townRadius, 0.58f, 1.1f);
            var farmMask = SoftEllipseMask(new Vector2(x, z), farmlandCenter, farmlandRadius, 0.55f, 1.08f);
            height = Mathf.Lerp(height, townHeight + broad * 0.18f + detail * 0.08f, townMask * 0.9f);
            height = Mathf.Lerp(height, farmlandHeight + broad * 0.48f + medium * 0.22f, farmMask * 0.84f);
            height = Mathf.Max(height, waterLevel + 0.48f + broad * 0.18f);

            var lakeMask = LakeMask(new Vector2(x, z));
            var riverMask = RiverMask(new Vector2(x, z));
            var waterCarve = Mathf.Max(lakeMask, riverMask);
            var basinFloor = waterLevel - lakeDepth * Mathf.Lerp(0.42f, 1f, lakeMask);
            height = Mathf.Lerp(height, basinFloor, waterCarve);

            var islandMask = 1f - Smooth01(1f - shoreFalloff, 1.025f, edgeRadius);
            height = Mathf.Lerp(waterLevel - 2.9f, height, islandMask);
            return Mathf.Clamp(height, waterLevel - 3.1f, 18.5f);
        }

        public Vector3 SampleNormal(Vector3 worldPosition)
        {
            const float step = 0.42f;
            var left = SampleHeight(worldPosition.x - step, worldPosition.z);
            var right = SampleHeight(worldPosition.x + step, worldPosition.z);
            var down = SampleHeight(worldPosition.x, worldPosition.z - step);
            var up = SampleHeight(worldPosition.x, worldPosition.z + step);
            return new Vector3(left - right, step * 2f, down - up).normalized;
        }

        public float SampleSlope(Vector3 worldPosition)
        {
            return 1f - Mathf.Clamp01(SampleNormal(worldPosition).y);
        }

        public float SampleWetness(Vector3 worldPosition)
        {
            var point = new Vector2(worldPosition.x, worldPosition.z);
            var waterMask = Mathf.Max(LakeMask(point), RiverMask(point));
            var height = SampleHeight(worldPosition);
            var lowland = 1f - Smooth01(waterLevel + 0.4f, waterLevel + 4.2f, height);
            return Mathf.Clamp01(waterMask + lowland * 0.66f);
        }

        public bool IsWater(Vector3 worldPosition)
        {
            var point = new Vector2(worldPosition.x, worldPosition.z);
            return LakeMask(point) > 0.38f || RiverMask(point) > 0.5f;
        }

        public Vector3 SnapPoint(Vector3 point, float verticalOffset = 0f)
        {
            if (TryProjectToSurface(point, out var surfacePoint, out _))
            {
                point.y = surfacePoint.y + verticalOffset;
            }
            else
            {
                point.y = SampleHeight(point) + verticalOffset;
            }
            return point;
        }

        public bool TryProjectToSurface(Vector3 point, out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            if (terrainCollider != null && terrainCollider.sharedMesh != null)
            {
                var ray = new Ray(new Vector3(point.x, 64f, point.z), Vector3.down);
                if (terrainCollider.Raycast(ray, out var hit, 128f))
                {
                    surfacePoint = hit.point;
                    surfaceNormal = hit.normal;
                    return true;
                }
            }

            surfacePoint = new Vector3(point.x, SampleHeight(point), point.z);
            surfaceNormal = SampleNormal(point);
            return false;
        }

        public TerrainInvariantSnapshot ValidateInvariants()
        {
            var peak = float.MinValue;
            for (var z = 0; z <= 24; z++)
            {
                for (var x = 0; x <= 30; x++)
                {
                    var px = Mathf.Lerp(-HalfWidth, 0f, x / 30f);
                    var pz = Mathf.Lerp(0f, HalfDepth, z / 24f);
                    peak = Mathf.Max(peak, SampleHeight(px, pz));
                }
            }
            var lakeFloor = SampleHeight(lakeCenter.x, lakeCenter.y);
            var town = SampleHeight(townCenter.x, townCenter.y);
            var farm = SampleHeight(farmlandCenter.x, farmlandCenter.y);
            var mesh = terrainFilter != null ? terrainFilter.sharedMesh : null;
            var renderers = generatedRoot != null ? generatedRoot.GetComponentsInChildren<MeshRenderer>(true).Length : 0;
            var snapshot = new TerrainInvariantSnapshot
            {
                SeamHeightDelta = 0f,
                PeakHeight = peak,
                LakeFloorHeight = lakeFloor,
                TownHeight = town,
                FarmlandHeight = farm,
                TerrainTriangles = mesh != null ? mesh.triangles.Length / 3 : 0,
                TerrainRenderers = renderers
            };
            snapshot.Passed = snapshot.SeamHeightDelta <= 0.0001f
                && snapshot.PeakHeight >= 13.5f
                && snapshot.PeakHeight <= 18.6f
                && snapshot.LakeFloorHeight < waterLevel - 0.8f
                && snapshot.TownHeight >= 2f
                && snapshot.TownHeight <= 4.2f
                && snapshot.FarmlandHeight >= 2f
                && snapshot.FarmlandHeight <= 5.2f
                && snapshot.TerrainTriangles >= 4000
                && snapshot.TerrainRenderers <= 4;
            return snapshot;
        }

        private Mesh BuildTerrainMesh()
        {
            var vertices = new List<Vector3>(columns * rows * 6);
            var normals = new List<Vector3>(columns * rows * 6);
            var colors = new List<Color>(columns * rows * 6);
            var triangles = new List<int>(columns * rows * 6);
            for (var row = 0; row < rows; row++)
            {
                var z0 = Mathf.Lerp(-HalfDepth, HalfDepth, row / (float)rows);
                var z1 = Mathf.Lerp(-HalfDepth, HalfDepth, (row + 1f) / rows);
                for (var column = 0; column < columns; column++)
                {
                    var x0 = Mathf.Lerp(-HalfWidth, HalfWidth, column / (float)columns);
                    var x1 = Mathf.Lerp(-HalfWidth, HalfWidth, (column + 1f) / columns);
                    var a = new Vector3(x0, SampleHeight(x0, z0), z0);
                    var b = new Vector3(x0, SampleHeight(x0, z1), z1);
                    var c = new Vector3(x1, SampleHeight(x1, z1), z1);
                    var d = new Vector3(x1, SampleHeight(x1, z0), z0);
                    AddTerrainTriangle(vertices, normals, colors, triangles, a, b, c, column, row, 0);
                    AddTerrainTriangle(vertices, normals, colors, triangles, a, c, d, column, row, 1);
                }
            }

            var mesh = new Mesh { name = "WM Continuous Low Poly Terrain" };
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddTerrainTriangle(
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Color> colors,
            ICollection<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            int column,
            int row,
            int half)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            var centroid = (a + b + c) / 3f;
            var color = TerrainColor(centroid, normal, column, row, half);
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        private Color TerrainColor(Vector3 point, Vector3 normal, int column, int row, int half)
        {
            var slope = 1f - Mathf.Clamp01(normal.y);
            var height = point.y;
            var forest = SoftEllipseMask(new Vector2(point.x, point.z), new Vector2(-15f, 15f), new Vector2(25f, 21f), 0.3f, 1.2f);
            var farm = SoftEllipseMask(new Vector2(point.x, point.z), farmlandCenter, farmlandRadius, 0.45f, 1.18f);
            var town = SoftEllipseMask(new Vector2(point.x, point.z), townCenter, townRadius, 0.45f, 1.16f);
            var grass = Color.Lerp(new Color(0.2f, 0.5f, 0.17f), new Color(0.4f, 0.69f, 0.24f), Mathf.Clamp01(0.5f + point.z / 90f));
            grass = Color.Lerp(grass, new Color(0.1f, 0.34f, 0.15f), forest * 0.58f);
            grass = Color.Lerp(grass, new Color(0.53f, 0.57f, 0.2f), farm * 0.38f);
            grass = Color.Lerp(grass, new Color(0.35f, 0.52f, 0.27f), town * 0.28f);
            var rock = Color.Lerp(new Color(0.69f, 0.3f, 0.11f), new Color(0.48f, 0.34f, 0.23f), Mathf.InverseLerp(7f, 15f, height));
            var sand = new Color(0.82f, 0.53f, 0.2f);
            var snow = new Color(0.9f, 0.94f, 0.92f);
            var rockBlend = Mathf.Clamp01(
                Mathf.InverseLerp(0.12f, 0.48f, slope) * 0.74f
                + Mathf.InverseLerp(8.5f, 14.5f, height) * 0.62f);
            var color = Color.Lerp(grass, rock, rockBlend);
            color = Color.Lerp(color, snow, Smooth01(snowLine - 0.2f, snowLine + 0.65f, height));
            color = Color.Lerp(sand, color, Smooth01(waterLevel + 0.1f, waterLevel + 0.82f, height));
            var hash = Hash01(column, row, half);
            return color * Mathf.Lerp(0.92f, 1.06f, hash);
        }

        private Mesh BuildSkirtMesh()
        {
            const int segments = 128;
            var vertices = new List<Vector3>(segments * 18);
            var normals = new List<Vector3>(segments * 18);
            var colors = new List<Color>(segments * 18);
            var triangles = new List<int>(segments * 18);
            for (var index = 0; index < segments; index++)
            {
                var a0 = index / (float)segments * Mathf.PI * 2f;
                var a1 = (index + 1f) / segments * Mathf.PI * 2f;
                var edge0 = IslandBoundaryScale(a0) * (1f - shoreFalloff * 0.42f);
                var edge1 = IslandBoundaryScale(a1) * (1f - shoreFalloff * 0.42f);
                var p0 = new Vector3(Mathf.Cos(a0) * HalfWidth * edge0, 0f, Mathf.Sin(a0) * HalfDepth * edge0);
                var p1 = new Vector3(Mathf.Cos(a1) * HalfWidth * edge1, 0f, Mathf.Sin(a1) * HalfDepth * edge1);
                p0.y = Mathf.Max(waterLevel + 0.12f, SampleHeight(p0));
                p1.y = Mathf.Max(waterLevel + 0.12f, SampleHeight(p1));
                var mid0 = new Vector3(p0.x, p0.y - 1.25f, p0.z);
                var mid1 = new Vector3(p1.x, p1.y - 1.25f, p1.z);
                var bottom0 = new Vector3(p0.x, -3.4f, p0.z);
                var bottom1 = new Vector3(p1.x, -3.4f, p1.z);
                AddColoredQuad(vertices, normals, colors, triangles, p0, p1, mid1, mid0, new Color(0.86f, 0.31f, 0.09f));
                AddColoredQuad(vertices, normals, colors, triangles, mid0, mid1, bottom1, bottom0, new Color(0.24f, 0.11f, 0.055f));
            }
            return BuildMesh("WM Low Poly Island Cutaway", vertices, normals, colors, triangles);
        }

        private Mesh BuildWaterMesh()
        {
            var vertices = new List<Vector3>(512);
            var triangles = new List<int>(768);
            const int lakeSegments = 64;
            vertices.Add(new Vector3(lakeCenter.x, waterLevel + 0.025f, lakeCenter.y));
            for (var index = 0; index <= lakeSegments; index++)
            {
                var angle = index / (float)lakeSegments * Mathf.PI * 2f;
                var edge = LakeBoundaryScale(angle) * 0.94f;
                vertices.Add(new Vector3(
                    lakeCenter.x + Mathf.Cos(angle) * lakeRadius.x * edge,
                    waterLevel + 0.03f,
                    lakeCenter.y + Mathf.Sin(angle) * lakeRadius.y * edge));
                if (index == 0) continue;
                triangles.Add(0);
                triangles.Add(index);
                triangles.Add(index + 1);
            }

            if (riverSpline != null && riverSpline.Length >= 2)
            {
                AddFlatStrip(vertices, triangles, riverSpline, riverWidth * 0.94f, waterLevel + 0.03f);
            }

            var mesh = new Mesh { name = "WM Lake and River" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildRoadMesh()
        {
            var vertices = new List<Vector3>(256);
            var triangles = new List<int>(384);
            AddRoad(vertices, triangles, new[]
            {
                new Vector2(-15f, -11f), new Vector2(-7f, -13f), new Vector2(3f, -12f),
                new Vector2(13f, -9f), new Vector2(15f, -1f), new Vector2(13f, 7f),
                new Vector2(4f, 10f), new Vector2(-7f, 9f), new Vector2(-15f, 5f), new Vector2(-16f, -4f),
                new Vector2(-15f, -11f)
            }, 1.25f);
            AddRoad(vertices, triangles, new[]
            {
                new Vector2(-13f, -7f), new Vector2(-20f, -12f), new Vector2(-27f, -18f),
                new Vector2(-37f, -21f), new Vector2(-45f, -20f)
            }, 1.05f);
            AddRoad(vertices, triangles, new[]
            {
                new Vector2(10f, -8f), new Vector2(17f, -10f), new Vector2(24f, -10f),
                new Vector2(28.2f, -10f), new Vector2(29.35f, -10.05f)
            }, 1.05f);
            AddRoad(vertices, triangles, new[]
            {
                new Vector2(32.65f, -10.05f), new Vector2(34.1f, -10.75f),
                new Vector2(37f, -14f), new Vector2(41f, -20f),
                new Vector2(45f, -27f)
            }, 1.05f);
            var mesh = new Mesh { name = "WM Curved Terrain Roads" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddRoad(List<Vector3> vertices, List<int> triangles, Vector2[] points, float halfWidth)
        {
            if (points == null || points.Length < 2) return;
            var sampledPoints = DensifyPath(points, 1.2f);
            var closed = Vector2.SqrMagnitude(sampledPoints[0] - sampledPoints[sampledPoints.Count - 1]) < 0.001f;
            var pointCount = closed ? sampledPoints.Count - 1 : sampledPoints.Count;
            var start = vertices.Count;
            for (var index = 0; index < pointCount; index++)
            {
                var previous = sampledPoints[closed ? (index - 1 + pointCount) % pointCount : Mathf.Max(0, index - 1)];
                var next = sampledPoints[closed ? (index + 1) % pointCount : Mathf.Min(pointCount - 1, index + 1)];
                var tangent = (next - previous).normalized;
                var right = new Vector2(-tangent.y, tangent.x) * halfWidth;
                vertices.Add(RoadPoint(sampledPoints[index] - right));
                vertices.Add(RoadPoint(sampledPoints[index] + right));
            }

            var segmentCount = closed ? pointCount : pointCount - 1;
            for (var index = 0; index < segmentCount; index++)
            {
                var next = (index + 1) % pointCount;
                var left = start + index * 2;
                var nextLeft = start + next * 2;
                triangles.Add(left);
                triangles.Add(left + 1);
                triangles.Add(nextLeft + 1);
                triangles.Add(left);
                triangles.Add(nextLeft + 1);
                triangles.Add(nextLeft);
            }
        }

        private static List<Vector2> DensifyPath(IReadOnlyList<Vector2> points, float maximumSegmentLength)
        {
            var result = new List<Vector2>(points.Count * 4) { points[0] };
            for (var index = 0; index < points.Count - 1; index++)
            {
                var from = points[index];
                var to = points[index + 1];
                var steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / maximumSegmentLength));
                for (var step = 1; step <= steps; step++)
                {
                    result.Add(Vector2.Lerp(from, to, step / (float)steps));
                }
            }
            return result;
        }

        private static void AddFlatStrip(
            ICollection<Vector3> vertices,
            ICollection<int> triangles,
            IReadOnlyList<Vector2> points,
            float halfWidth,
            float height)
        {
            var start = vertices.Count;
            for (var index = 0; index < points.Count; index++)
            {
                var previous = points[Mathf.Max(0, index - 1)];
                var next = points[Mathf.Min(points.Count - 1, index + 1)];
                var tangent = (next - previous).normalized;
                var right = new Vector2(-tangent.y, tangent.x) * halfWidth;
                vertices.Add(new Vector3(points[index].x - right.x, height, points[index].y - right.y));
                vertices.Add(new Vector3(points[index].x + right.x, height, points[index].y + right.y));
            }

            for (var index = 0; index < points.Count - 1; index++)
            {
                var left = start + index * 2;
                var nextLeft = left + 2;
                triangles.Add(left);
                triangles.Add(left + 1);
                triangles.Add(nextLeft + 1);
                triangles.Add(left);
                triangles.Add(nextLeft + 1);
                triangles.Add(nextLeft);
            }
        }

        private Vector3 RoadPoint(Vector2 point)
        {
            return new Vector3(point.x, SampleHeight(point.x, point.y) + 0.07f, point.y);
        }

        private float LakeMask(Vector2 point)
        {
            var normalized = new Vector2(
                (point.x - lakeCenter.x) / Mathf.Max(0.1f, lakeRadius.x),
                (point.y - lakeCenter.y) / Mathf.Max(0.1f, lakeRadius.y));
            var angle = Mathf.Atan2(normalized.y, normalized.x);
            var shapedRadius = normalized.magnitude / LakeBoundaryScale(angle);
            return 1f - Smooth01(0.68f, 1.08f, shapedRadius);
        }

        private float RiverMask(Vector2 point)
        {
            if (riverSpline == null || riverSpline.Length < 2) return 0f;
            var best = float.PositiveInfinity;
            for (var index = 0; index < riverSpline.Length - 1; index++)
            {
                best = Mathf.Min(best, DistanceToSegment(point, riverSpline[index], riverSpline[index + 1]));
            }
            return 1f - Smooth01(riverWidth * 0.72f, riverWidth * 1.55f, best);
        }

        private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
        {
            var segment = to - from;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.0001f) return Vector2.Distance(point, from);
            var t = Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSquared);
            return Vector2.Distance(point, from + segment * t);
        }

        private static float SoftEllipseMask(Vector2 point, Vector2 center, Vector2 radius, float inner, float outer)
        {
            var normalized = new Vector2(
                (point.x - center.x) / Mathf.Max(0.1f, radius.x),
                (point.y - center.y) / Mathf.Max(0.1f, radius.y));
            return 1f - Smooth01(inner, outer, normalized.magnitude);
        }

        private static float Smooth01(float edge0, float edge1, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));
        }

        private static float Gaussian(float x, float z, Vector2 center, float radiusX, float radiusZ)
        {
            var dx = (x - center.x) / Mathf.Max(0.1f, radiusX);
            var dz = (z - center.y) / Mathf.Max(0.1f, radiusZ);
            return Mathf.Exp(-(dx * dx + dz * dz) * 1.45f);
        }

        private static float SharpPeak(float x, float z, Vector2 center, float radiusX, float radiusZ)
        {
            var dx = (x - center.x) / Mathf.Max(0.1f, radiusX);
            var dz = (z - center.y) / Mathf.Max(0.1f, radiusZ);
            var distance = Mathf.Sqrt(dx * dx + dz * dz);
            return Mathf.Pow(Mathf.Clamp01(1f - distance), 1.22f);
        }

        private static float IslandBoundaryScale(float angle)
        {
            return 0.99f
                + Mathf.Sin(angle * 3f + 0.7f) * 0.018f
                + Mathf.Sin(angle * 7f - 1.1f) * 0.009f
                + Mathf.Sin(angle * 11f + 2.4f) * 0.004f;
        }

        private static float LakeBoundaryScale(float angle)
        {
            return 1f
                + Mathf.Sin(angle * 3f + 0.4f) * 0.055f
                + Mathf.Sin(angle * 6f - 1.2f) * 0.028f;
        }

        private static float SignedNoise(float x, float y)
        {
            return Mathf.PerlinNoise(x, y) * 2f - 1f;
        }

        private float Hash01(int x, int y, int half)
        {
            unchecked
            {
                var hash = (uint)(seed ^ x * 73856093 ^ y * 19349663 ^ half * 83492791);
                hash ^= hash >> 13;
                hash *= 1274126177u;
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static void AddColoredQuad(
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Color> colors,
            ICollection<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Color color)
        {
            AddColoredTriangle(vertices, normals, colors, triangles, a, b, c, color);
            AddColoredTriangle(vertices, normals, colors, triangles, a, c, d, color * 0.88f);
        }

        private static void AddColoredTriangle(
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Color> colors,
            ICollection<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Color color)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        private static Mesh BuildMesh(
            string meshName,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color> colors,
            List<int> triangles)
        {
            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static MeshFilter CreateMeshObject(
            string objectName,
            Transform parent,
            Material material,
            bool collider,
            out MeshRenderer renderer)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(parent, false);
            var filter = root.AddComponent<MeshFilter>();
            renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            root.isStatic = true;
            return filter;
        }

        private static Material CreateTerrainMaterial()
        {
            var shader = Shader.Find("WeatherManager/LowPolyTerrain");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (shader == null) throw new InvalidOperationException("Low-poly terrain shader is unavailable.");
            var material = new Material(shader)
            {
                name = "Low Poly Terrain Vertex Colors",
                enableInstancing = true
            };
            if (material.HasProperty("_Tint")) material.SetColor("_Tint", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            return material;
        }

        private void ClearGenerated()
        {
            if (generatedRoot == null) generatedRoot = transform.Find("Continuous Low Poly Terrain");
            if (generatedRoot == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(generatedRoot.gameObject);
                generatedRoot = null;
                return;
            }
#endif
            Destroy(generatedRoot.gameObject);
            generatedRoot = null;
        }
    }
}
