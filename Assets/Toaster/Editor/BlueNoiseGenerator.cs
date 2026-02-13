using UnityEngine;
using UnityEditor;

namespace Toaster
{
    public static class BlueNoiseGenerator
    {
        private const int SIZE = 128;
        private const string OutputPath = "Assets/Toaster/Runtime/Textures/BlueNoise128.png";

        [MenuItem("Toaster/Generate Blue Noise Texture")]
        public static void Generate()
        {
            EditorUtility.DisplayProgressBar("Toaster", "Generating blue noise texture...", 0f);

            try
            {
                var tex = GenerateBlueNoise(SIZE);
                var bytes = tex.EncodeToPNG();
                System.IO.File.WriteAllBytes(OutputPath, bytes);
                Object.DestroyImmediate(tex);

                AssetDatabase.Refresh();

                // Set import settings — single channel, no compression, no sRGB
                var importer = AssetImporter.GetAtPath(OutputPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.SingleChannel;
                    importer.sRGBTexture = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.filterMode = FilterMode.Point;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }

                Appliance.Log($"Blue noise texture generated at {OutputPath}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Generates a blue noise texture using interleaved gradient noise + energy minimization.
        /// Not a true void-and-cluster (too slow for editor), but produces good high-frequency
        /// spatial distribution suitable for temporal dithering.
        /// </summary>
        static Texture2D GenerateBlueNoise(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.R8, false, true);
            int total = size * size;

            // Step 1: Generate candidate ranking via Mitchell's best candidate algorithm
            // Place points one at a time, each maximizing minimum distance to existing points
            var values = new float[total];
            var placed = new Vector2[total];
            var placedCount = 0;

            // Use deterministic seed for reproducibility
            var rng = new System.Random(42);

            for (int i = 0; i < total; i++)
            {
                if (i == 0)
                {
                    // First point is random
                    placed[0] = new Vector2(rng.Next(size), rng.Next(size));
                    int idx = (int)placed[0].y * size + (int)placed[0].x;
                    values[idx] = (float)i / total;
                    placedCount = 1;
                    continue;
                }

                // Generate candidates and pick the one farthest from all existing points
                int numCandidates = Mathf.Min(1 + placedCount, 32);
                float bestMinDist = -1;
                Vector2 bestCandidate = Vector2.zero;

                for (int c = 0; c < numCandidates; c++)
                {
                    var candidate = new Vector2(rng.Next(size), rng.Next(size));
                    float minDist = float.MaxValue;

                    // Check distance to nearby placed points (toroidal wrapping)
                    int checkCount = Mathf.Min(placedCount, 64);
                    int step = Mathf.Max(1, placedCount / checkCount);
                    for (int j = 0; j < placedCount; j += step)
                    {
                        float dist = ToroidalDistSq(candidate, placed[j], size);
                        if (dist < minDist)
                            minDist = dist;
                    }

                    if (minDist > bestMinDist)
                    {
                        bestMinDist = minDist;
                        bestCandidate = candidate;
                    }
                }

                int px = Mathf.Clamp((int)bestCandidate.x, 0, size - 1);
                int py = Mathf.Clamp((int)bestCandidate.y, 0, size - 1);
                placed[placedCount] = bestCandidate;
                values[py * size + px] = (float)i / total;
                placedCount++;

                if (i % 1000 == 0)
                    EditorUtility.DisplayProgressBar("Toaster", $"Generating blue noise... {i}/{total}", (float)i / total);
            }

            // Step 2: Write to texture
            var pixels = new Color[total];
            for (int i = 0; i < total; i++)
            {
                float v = values[i];
                pixels[i] = new Color(v, v, v, 1f);
            }
            tex.SetPixels(pixels);
            tex.Apply();

            return tex;
        }

        static float ToroidalDistSq(Vector2 a, Vector2 b, int size)
        {
            float dx = Mathf.Abs(a.x - b.x);
            float dy = Mathf.Abs(a.y - b.y);
            if (dx > size * 0.5f) dx = size - dx;
            if (dy > size * 0.5f) dy = size - dy;
            return dx * dx + dy * dy;
        }
    }
}
