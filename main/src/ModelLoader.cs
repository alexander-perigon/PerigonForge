using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Mathematics;

namespace PerigonForge
{
    /// <summary>
    /// Simple OBJ model loader for custom block shapes
    /// </summary>
    public static class ModelLoader
    {
        /// <summary>
        /// LOD (Level of Detail) data for simplified rendering at distance
        /// </summary>
        public class LODData
        {
            public ModelData Model { get; set; }
            public float DistanceThreshold { get; set; }
            public float TriangleRatio { get; set; }
        }

        public struct ModelData
        {
            public float[] Vertices;      // x, y, z, u, v per vertex (5 floats each)
            public Vector3[] Normals;     // Per-vertex normals computed from face data
            public uint[] Indices;        // Triangle indices
            public int VertexCount;
            public int IndexCount;
            public int TriangleCount => IndexCount / 3;
            public bool HasUVs;           // Whether UVs were extracted from OBJ
            public Vector3 BoundsMin;     // Model's bounding box minimum
            public Vector3 BoundsMax;     // Model's bounding box maximum
            
            // LOD data - stored as separate class to avoid struct cycle
            public LODData? LOD { get; set; }
        }

        // LOD distance thresholds (in world units)
        public const float LOD_DISTANCE_0 = 32f;   // Full detail within 32 units
        public const float LOD_DISTANCE_1 = 64f;   // Medium detail within 64 units
        public const float LOD_DISTANCE_2 = 128f;  // Low detail within 128 units

        private static Dictionary<string, ModelData> _loadedModels = new();

        /// <summary>
        /// Load an OBJ model from Resources/Blocks/models directory
        /// </summary>
        public static ModelData LoadModel(string modelName)
        {
            // Check cache first
            if (_loadedModels.TryGetValue(modelName, out var cached))
                return cached;

            string modelPath = Path.Combine("Resources", "Blocks", "models", $"{modelName}.obj");
            
            if (!File.Exists(modelPath))
            {
                Console.WriteLine($"[ModelLoader] Model not found: {modelPath}");
                return default;
            }

            var model = ParseOBJ(modelPath);
            
            // Generate LOD versions for high-poly models
            if (model.TriangleCount > 1000)
            {
                model.LOD = new LODData 
                { 
                    Model = GenerateLOD(model, 0.5f),
                    DistanceThreshold = LOD_DISTANCE_1,
                    TriangleRatio = 0.5f
                };
            }
            
            _loadedModels[modelName] = model;
            return model;
        }

        /// <summary>
        /// Generate a simplified LOD version of the model
        /// </summary>
        private static ModelData GenerateLOD(ModelData original, float ratio)
        {
            if (original.Indices == null || original.Indices.Length == 0)
                return default;

            int targetIndices = (int)(original.Indices.Length * ratio);
            if (targetIndices < 3) targetIndices = 3;
            
            // Simple LOD: just take every Nth triangle
            var newIndices = new List<uint>();
            int step = original.Indices.Length / targetIndices;
            if (step < 1) step = 1;
            
            for (int i = 0; i < original.Indices.Length && newIndices.Count < targetIndices; i += 3)
            {
                newIndices.Add(original.Indices[i]);
                newIndices.Add(original.Indices[i + 1]);
                newIndices.Add(original.Indices[i + 2]);
            }

            return new ModelData
            {
                Vertices = original.Vertices,
                Normals = original.Normals,
                Indices = newIndices.ToArray(),
                VertexCount = original.VertexCount,
                IndexCount = newIndices.Count,
                HasUVs = original.HasUVs,
                BoundsMin = original.BoundsMin,
                BoundsMax = original.BoundsMax
            };
        }

        /// <summary>
        /// Get the appropriate LOD version based on distance from camera
        /// </summary>
        public static ModelData GetLODModel(ModelData original, float distanceFromCamera)
        {
            // No LOD needed for small models or close viewing
            if (original.LOD == null || distanceFromCamera < LOD_DISTANCE_0)
                return original;
            
            // Use simplified version when far away
            return original.LOD.Model;
        }

        private static ModelData ParseOBJ(string filePath)
        {
            var positions = new List<Vector3>();
            var textureCoords = new List<Vector2>();
            var indices = new List<uint>();
            var vertexUVs = new Dictionary<(uint, uint), int>(); // Maps (posIdx, uvIdx) -> combined vertex index
            var combinedVertices = new List<Vector3>(); // Positions
            var combinedUVs = new List<Vector2>();     // UVs
            var vertexNormalIndices = new Dictionary<(uint, uint), int>(); // Maps (posIdx, faceNormalIdx) -> vertex index
            var faceNormals = new List<Vector3>(); // Store unique face normals

            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                            continue;

                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0)
                            continue;

                        // Parse vertex positions
                        if (parts[0] == "v" && parts.Length >= 4)
                        {
                            if (float.TryParse(parts[1], out float x) &&
                                float.TryParse(parts[2], out float y) &&
                                float.TryParse(parts[3], out float z))
                            {
                                positions.Add(new Vector3(x, y, z));
                            }
                        }

                        // Parse texture coordinates
                        if (parts[0] == "vt" && parts.Length >= 3)
                        {
                            if (float.TryParse(parts[1], out float u) &&
                                float.TryParse(parts[2], out float v))
                            {
                                // Flip V for OpenGL (OBJ uses bottom-left origin, OpenGL uses top-left)
                                textureCoords.Add(new Vector2(u, 1f - v));
                            }
                        }

                        // Parse faces
                        if (parts[0] == "f" && parts.Length >= 4)
                        {
                            // Handle triangles and quads
                            var faceVertices = new List<(uint posIdx, uint uvIdx)>();
                            for (int i = 1; i < parts.Length; i++)
                            {
                                var vertexStr = parts[i].Split('/');
                                if (uint.TryParse(vertexStr[0], out uint posIdx))
                                {
                                    posIdx--; // OBJ indices are 1-based
                                    uint uvIdx = 0;
                                    if (vertexStr.Length > 1 && uint.TryParse(vertexStr[1], out uint parsedUvIdx))
                                    {
                                        uvIdx = parsedUvIdx - 1;
                                    }
                                    faceVertices.Add((posIdx, uvIdx));
                                }
                            }

                            // Compute face normal for this face (flat shading)
                            if (faceVertices.Count >= 3)
                            {
                                Vector3 v0 = positions[(int)faceVertices[0].posIdx];
                                Vector3 v1 = positions[(int)faceVertices[1].posIdx];
                                Vector3 v2 = positions[(int)faceVertices[2].posIdx];
                                Vector3 edge1 = v1 - v0;
                                Vector3 edge2 = v2 - v0;
                                Vector3 faceNormal = Vector3.Cross(edge1, edge2);
                                if (faceNormal.LengthSquared > 0.0001f)
                                    faceNormal = faceNormal.Normalized();
                                else
                                    faceNormal = Vector3.UnitY;
                                int faceNormalIdx = faceNormals.Count;
                                faceNormals.Add(faceNormal);

                                // Create unique vertices for each face (flat shading = no shared vertices)
                                for (int fv = 0; fv < faceVertices.Count; fv++)
                                {
                                    var fvData = faceVertices[fv];
                                    int combinedIdx = combinedVertices.Count;
                                    vertexNormalIndices[(fvData.posIdx, (uint)faceNormalIdx)] = combinedIdx;

                                    // Add position
                                    if (fvData.posIdx < positions.Count)
                                        combinedVertices.Add(positions[(int)fvData.posIdx]);
                                    else
                                        combinedVertices.Add(Vector3.Zero);

                                    // Add UV
                                    if (fvData.uvIdx < textureCoords.Count)
                                        combinedUVs.Add(textureCoords[(int)fvData.uvIdx]);
                                    else
                                        combinedUVs.Add(new Vector2(0.5f, 0.5f));

                                    // Add face normal reference
                                    faceNormalIdx++;
                                }

                                // Triangulate if quad
                                if (faceVertices.Count == 3)
                                {
                                    indices.Add((uint)combinedVertices.Count - 3);
                                    indices.Add((uint)combinedVertices.Count - 2);
                                    indices.Add((uint)combinedVertices.Count - 1);
                                }
                                else if (faceVertices.Count == 4)
                                {
                                    indices.Add((uint)combinedVertices.Count - 4);
                                    indices.Add((uint)combinedVertices.Count - 3);
                                    indices.Add((uint)combinedVertices.Count - 2);
                                    indices.Add((uint)combinedVertices.Count - 4);
                                    indices.Add((uint)combinedVertices.Count - 2);
                                    indices.Add((uint)combinedVertices.Count - 1);
                                }
                            }
                        }
                    }
                }

                // Convert vertices to float array with UVs (5 floats per vertex: x, y, z, u, v)
                var vertArray = new float[combinedVertices.Count * 5];
                for (int i = 0; i < combinedVertices.Count; i++)
                {
                    vertArray[i * 5]     = combinedVertices[i].X;
                    vertArray[i * 5 + 1] = combinedVertices[i].Y;
                    vertArray[i * 5 + 2] = combinedVertices[i].Z;
                    if (i < combinedUVs.Count)
                    {
                        vertArray[i * 5 + 3] = combinedUVs[i].X;
                        vertArray[i * 5 + 4] = combinedUVs[i].Y;
                    }
                    else
                    {
                        vertArray[i * 5 + 3] = 0.5f;
                        vertArray[i * 5 + 4] = 0.5f;
                    }
                }
                var normals = new Vector3[combinedVertices.Count];
                
                // Recompute normals from face data (since we've created unique vertices per face)
                for (int i = 0; i < indices.Count; i += 3)
                {
                    uint i0 = indices[i];
                    uint i1 = indices[i + 1];
                    uint i2 = indices[i + 2];

                    Vector3 v0 = combinedVertices[(int)i0];
                    Vector3 v1 = combinedVertices[(int)i1];
                    Vector3 v2 = combinedVertices[(int)i2];

                    // Compute face normal using cross product
                    Vector3 edge1 = v1 - v0;
                    Vector3 edge2 = v2 - v0;
                    Vector3 faceNormal = Vector3.Cross(edge1, edge2);

                    if (faceNormal.LengthSquared > 0.0001f)
                    {
                        faceNormal = faceNormal.Normalized();
                    }
                    else
                    {
                        faceNormal = Vector3.UnitY;  // Default to up if no valid normal
                    }

                    // Assign the same face normal to all three vertices (flat shading)
                    normals[i0] = faceNormal;
                    normals[i1] = faceNormal;
                    normals[i2] = faceNormal;
                }

                // Compute bounding box
                Vector3 boundsMin = new Vector3(float.MaxValue);
                Vector3 boundsMax = new Vector3(float.MinValue);
                for (int i = 0; i < combinedVertices.Count; i++)
                {
                    boundsMin = Vector3.ComponentMin(boundsMin, combinedVertices[i]);
                    boundsMax = Vector3.ComponentMax(boundsMax, combinedVertices[i]);
                }

                // If no vertices, set bounds to origin
                if (combinedVertices.Count == 0)
                {
                    boundsMin = Vector3.Zero;
                    boundsMax = Vector3.Zero;
                }

                return new ModelData
                {
                    Vertices = vertArray,
                    Normals = normals,
                    Indices = indices.ToArray(),
                    VertexCount = combinedVertices.Count,
                    IndexCount = indices.Count,
                    HasUVs = textureCoords.Count > 0,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax
                };
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ModelLoader] Error loading {filePath}: {e.Message}");
                return default;
            }
        }

        public static void ClearCache()
        {
            _loadedModels.Clear();
        }
    }
}
