using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class DrunkardsWalkCarver : MonoBehaviour
{
    [Header("Map")]
    public Tilemap targetTilemap;        
    public TileBase wallTile;           
    public TileBase floorTile;          
    public int mapWidth = 80;
    public int mapHeight = 60;
    public Vector2Int mapOrigin = new Vector2Int(0, 0); 

    [Header("Walkers")]
    public int numWalkers = 4;
    public int maxStepsPerWalker = 10000;
    [Range(0f, 1f)]
    public float targetFloorPercent = 0.35f; 
    public int carveRadius = 1; 

    [Header("Random / Reproducibility")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Behavior")]
    public bool wrapEdges = false; 
    public bool reflectEdges = false; 

    [Header("Animation (Play Mode)")]
    public bool animateGeneration = false; 
    [Range(0f, 0.5f)]
    public float stepDelay = 0.0005f; 
    public int stepsPerBatch = 50; 

    [Header("Editor Animation (Edit Mode)")]
    public float editorStepDelay = 0.05f; 
    public int editorStepsPerBatch = 100; 

    private bool[,] isFloor;
    private System.Random rng;

#if UNITY_EDITOR
    private bool editorAnimating = false;
    private List<Vector2Int> editorStarts;
    private int editorCurrentWalkerIndex;
    private Vector2Int editorCurrentPos;
    private int editorCurrentStepsForWalker;
    private int editorCarved;
    private int editorGoal;
    private double editorLastBatchTime = 0.0;
#endif
    private int totalTiles => mapWidth * mapHeight;
    private int targetFloorCount => Mathf.Clamp((int)(targetFloorPercent * totalTiles), 1, totalTiles);
    private void Start()
    {

    }

    #region Public generation entry points

    [ContextMenu("Generate Drunkard's Walk (Play Mode / Sync)")]
    public void GenerateContext()
    {
        if (Application.isPlaying)
        {
            StartCoroutine(GenerateRoutine());
        }
        else
        {
            GenerateSync();
        }
    }

    public void GenerateSync()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[DrunkardsWalkCarver] targetTilemap is null. Assign a Tilemap in inspector.");
            return;
        }

        PrepareRandom();
        InitMap();
        RunCarversSync();
        PaintTilemap();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    public IEnumerator GenerateRoutine()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[DrunkardsWalkCarver] targetTilemap is null.");
            yield break;
        }

        PrepareRandom();
        InitMap();

        if (!animateGeneration)
        {
            RunCarversSync();
            PaintTilemap();
            yield break;
        }

        int carved = 0;
        int goal = targetFloorCount;
        List<Vector2Int> starts = ChooseStarts();

        PaintAllWalls();

        foreach (var start in starts)
        {
            Vector2Int pos = start;
            int steps = 0;
            while (steps < maxStepsPerWalker && carved < goal)
            {
                int batch = Mathf.Min(stepsPerBatch, maxStepsPerWalker - steps);
                for (int i = 0; i < batch && carved < goal; i++)
                {
                    carved += CarveAt(pos);
                    pos = StepFrom(pos);
                    steps++;
                }

                PaintTilemap();
                yield return new WaitForSeconds(stepDelay);
            }

            if (carved >= goal) break;
        }

        PaintTilemap();
    }

    #endregion

    private void PrepareRandom()
    {
        if (useRandomSeed)
        {
            seed = Environment.TickCount;
        }
        rng = new System.Random(seed);
    }

    private void InitMap()
    {
        isFloor = new bool[mapWidth, mapHeight];
    }

    private List<Vector2Int> ChooseStarts()
    {
        var starts = new List<Vector2Int>(numWalkers);
        for (int i = 0; i < numWalkers; i++)
        {
            int sx = rng.Next(0, mapWidth);
            int sy = rng.Next(0, mapHeight);
            starts.Add(new Vector2Int(sx, sy));
        }
        return starts;
    }

    private void RunCarversSync()
    {
        int carved = 0;
        int goal = targetFloorCount;
        List<Vector2Int> starts = ChooseStarts();

        foreach (var start in starts)
        {
            Vector2Int pos = start;
            int steps = 0;
            while (steps < maxStepsPerWalker && carved < goal)
            {
                carved += CarveAt(pos);
                pos = StepFrom(pos);
                steps++;
            }
            if (carved >= goal) break;
        }
    }

    private int CarveAt(Vector2Int mapPos)
    {
        int newCarved = 0;
        int r = Mathf.Max(0, carveRadius);
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                if (dx * dx + dy * dy > r * r) continue; 
                int x = mapPos.x + dx;
                int y = mapPos.y + dy;
                if (!IsInsideMap(x, y)) continue;
                if (!isFloor[x, y])
                {
                    isFloor[x, y] = true;
                    newCarved++;
                }
            }
        }
        return newCarved;
    }

    private Vector2Int StepFrom(Vector2Int pos)
    {
        int dir = rng.Next(0, 4);
        Vector2Int next = pos;
        switch (dir)
        {
            case 0: next += Vector2Int.up; break;
            case 1: next += Vector2Int.down; break;
            case 2: next += Vector2Int.left; break;
            case 3: next += Vector2Int.right; break;
        }

        if (IsInsideMap(next.x, next.y))
        {
            return next;
        }
        else
        {
            if (wrapEdges)
            {
                int nx = (next.x % mapWidth + mapWidth) % mapWidth;
                int ny = (next.y % mapHeight + mapHeight) % mapHeight;
                return new Vector2Int(nx, ny);
            }
            else if (reflectEdges)
            {
                int rx = Mathf.Clamp(next.x, 0, mapWidth - 1);
                int ry = Mathf.Clamp(next.y, 0, mapHeight - 1);
                return new Vector2Int(rx, ry);
            }
            else
            {
                int cx = Mathf.Clamp(next.x, 0, mapWidth - 1);
                int cy = Mathf.Clamp(next.y, 0, mapHeight - 1);
                return new Vector2Int(cx, cy);
            }
        }
    }

    private bool IsInsideMap(int x, int y)
    {
        return x >= 0 && y >= 0 && x < mapWidth && y < mapHeight;
    }

    #region Tilemap Painting Helpers

    private void PaintTilemap()
    {
        if (targetTilemap == null) return;

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                Vector3Int tilePos = new Vector3Int(mapOrigin.x + x, mapOrigin.y + y, 0);
                if (isFloor[x, y])
                    targetTilemap.SetTile(tilePos, floorTile);
                else
                    targetTilemap.SetTile(tilePos, wallTile);
            }
        }
    }

    private void PaintAllWalls()
    {
        if (targetTilemap == null) return;

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                Vector3Int tilePos = new Vector3Int(mapOrigin.x + x, mapOrigin.y + y, 0);
                targetTilemap.SetTile(tilePos, wallTile);
            }
        }
    }

    #endregion

#if UNITY_EDITOR
    public void StartEditorAnimatedGeneration()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[DrunkardsWalkCarver] targetTilemap is null.");
            return;
        }

        if (editorAnimating)
        {
            Debug.Log("[DrunkardsWalkCarver] Editor animation already running.");
            return;
        }

        PrepareRandom();
        InitMap();

        editorStarts = ChooseStarts();
        editorCurrentWalkerIndex = 0;
        if (editorStarts.Count > 0)
            editorCurrentPos = editorStarts[0];
        else
            editorCurrentPos = new Vector2Int(0, 0);

        editorCurrentStepsForWalker = 0;
        editorCarved = 0;
        editorGoal = targetFloorCount;
        editorLastBatchTime = UnityEditor.EditorApplication.timeSinceStartup;

        PaintAllWalls();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        editorAnimating = true;
        UnityEditor.EditorApplication.update += EditorUpdateStep;
    }

    public void StopEditorAnimatedGeneration()
    {
        if (!editorAnimating) return;
        editorAnimating = false;
        UnityEditor.EditorApplication.update -= EditorUpdateStep;
        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }

    private void EditorUpdateStep()
    {
        if (!editorAnimating) return;

        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now - editorLastBatchTime < editorStepDelay)
            return; 

        editorLastBatchTime = now;

        if (editorCarved >= editorGoal || editorCurrentWalkerIndex >= editorStarts.Count)
        {
            StopEditorAnimatedGeneration();
            return;
        }

        int batch = Mathf.Min(editorStepsPerBatch, maxStepsPerWalker - editorCurrentStepsForWalker);
        for (int i = 0; i < batch && editorCarved < editorGoal; i++)
        {
            editorCarved += CarveAt(editorCurrentPos);
            editorCurrentPos = StepFrom(editorCurrentPos);
            editorCurrentStepsForWalker++;

            if (editorCurrentStepsForWalker >= maxStepsPerWalker)
            {
                editorCurrentWalkerIndex++;
                if (editorCurrentWalkerIndex < editorStarts.Count)
                {
                    editorCurrentPos = editorStarts[editorCurrentWalkerIndex];
                    editorCurrentStepsForWalker = 0;
                }
                break;
            }
        }

        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        if (editorCarved >= editorGoal || editorCurrentWalkerIndex >= editorStarts.Count)
        {
            StopEditorAnimatedGeneration();
        }
    }
#endif

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(DrunkardsWalkCarver))]
    private class DrunkardEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            DrunkardsWalkCarver script = (DrunkardsWalkCarver)target;

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate (Editor Mode)"))
            {
                script.GenerateSync();
            }

            if (GUILayout.Button("Generate Animated (Editor Mode)"))
            {
                script.StartEditorAnimatedGeneration();
            }

            if (GUILayout.Button("Stop Animated"))
            {
                script.StopEditorAnimatedGeneration();
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Generate (Play Mode)"))
            {
                if (Application.isPlaying)
                    script.StartCoroutine(script.GenerateRoutine());
                else
                    Debug.LogWarning("Start Play mode to run Play-mode coroutine.");
            }

            if (GUILayout.Button("Clear Area (remove tiles in area)"))
            {
                if (script.targetTilemap != null)
                {
                    for (int x = 0; x < script.mapWidth; x++)
                        for (int y = 0; y < script.mapHeight; y++)
                            script.targetTilemap.SetTile(new Vector3Int(script.mapOrigin.x + x, script.mapOrigin.y + y, 0), null);

                    UnityEditor.EditorUtility.SetDirty(script.targetTilemap);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                }
            }
        }
    }
#endif
}
