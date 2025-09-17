using System;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class TerrainGenerator : MonoBehaviour
{
    [Header("Tilemap")]
    public Tilemap tilemap;

    [Header("Map size")]
    public int width = 128;
    public int height = 128;

    [Header("Noise (fBM)")]
    public float scale = 30f;
    public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f;
    public float lacunarity = 2f;
    public int seed = 0;
    public Vector2 offset = Vector2.zero;

    [Header("Tiles (assign in inspector)")]
    public TileBase waterTile;
    public TileBase sandTile;
    public TileBase grassTile;
    public TileBase hillTile;
    public TileBase mountainTile;

    [Header("Biome thresholds (increasing)")]
    [Range(0f, 1f)] public float waterThreshold = 0.30f;
    [Range(0f, 1f)] public float sandThreshold = 0.40f;
    [Range(0f, 1f)] public float grassThreshold = 0.60f;
    [Range(0f, 1f)] public float hillThreshold = 0.88f;
    // mountain: > hillThreshold

    // Public API used by the editor buttons
    public void GenerateTerrain()
    {
        if (tilemap == null)
        {
            Debug.LogError("[TerrainGenerator] Tilemap not assigned.");
            return;
        }

        CustomPerlin perlin = new CustomPerlin(seed);

        // Clear first (как было оговорено)
        ClearTerrain();

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++)
        {
            float offsetX = (float)(prng.Next(-100000, 100000)) + offset.x;
            float offsetY = (float)(prng.Next(-100000, 100000)) + offset.y;
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }

        if (scale <= 0) scale = 0.0001f;

        float[,] noiseMap = new float[width, height];

        float maxLocal = float.MinValue;
        float minLocal = float.MaxValue;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseHeight = 0f;

                for (int i = 0; i < octaves; i++)
                {
                    float sampleX = (x - width / 2f) / scale * frequency + octaveOffsets[i].x;
                    float sampleY = (y - height / 2f) / scale * frequency + octaveOffsets[i].y;

                    float perlinVal = perlin.Noise(sampleX, sampleY); // 0..1
                    float perlinValueCentered = perlinVal * 2f - 1f;   // -1..1, чтобы октавы могли быть отрицательными
                    noiseHeight += perlinValueCentered * amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                if (noiseHeight > maxLocal) maxLocal = noiseHeight;
                if (noiseHeight < minLocal) minLocal = noiseHeight;
                noiseMap[x, y] = noiseHeight;
            }
        }

        // Normalize to 0..1
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                noiseMap[x, y] = Mathf.InverseLerp(minLocal, maxLocal, noiseMap[x, y]);
            }
        }

        // Fill Tilemap
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float v = noiseMap[x, y];
                TileBase chosen = ChooseTileByValue(v);
                Vector3Int pos = new Vector3Int(x - width / 2, y - height / 2, 0);
                tilemap.SetTile(pos, chosen);
            }
        }

        Debug.Log("[TerrainGenerator] Terrain generated.");
    }

    public void ClearTerrain()
    {
        if (tilemap == null)
        {
            Debug.LogWarning("[TerrainGenerator] Tilemap not assigned; nothing to clear.");
            return;
        }

        tilemap.ClearAllTiles();
        Debug.Log("[TerrainGenerator] Tilemap cleared.");
    }

    private TileBase ChooseTileByValue(float v)
    {
        if (v <= waterThreshold) return waterTile;
        if (v <= sandThreshold) return sandTile;
        if (v <= grassThreshold) return grassTile;
        if (v <= hillThreshold) return hillTile;
        return mountainTile;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Поддерживаем порядок порогов в инспекторе
        sandThreshold = Mathf.Clamp(sandThreshold, waterThreshold + 0.001f, 1f);
        grassThreshold = Mathf.Clamp(grassThreshold, sandThreshold + 0.001f, 1f);
        hillThreshold = Mathf.Clamp(hillThreshold, grassThreshold + 0.001f, 1f);
    }
#endif
}

#if UNITY_EDITOR
[CanEditMultipleObjects]
[CustomEditor(typeof(TerrainGenerator))]
public class TerrainGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        TerrainGenerator gen = (TerrainGenerator)target;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Сгенерировать ландшафт"))
        {
            // поддержка undo и пометка сцены как изменённой
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Generate Terrain");
            gen.GenerateTerrain();
            EditorUtility.SetDirty(gen);
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        }

        if (GUILayout.Button("Очистить тайлы"))
        {
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Clear Terrain");
            gen.ClearTerrain();
            EditorUtility.SetDirty(gen);
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif
