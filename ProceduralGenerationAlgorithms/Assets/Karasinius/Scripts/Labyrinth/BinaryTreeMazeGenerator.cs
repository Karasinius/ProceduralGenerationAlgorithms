using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class BinaryTreeMazeGenerator : MonoBehaviour
{
    [Header("Map / Tiles")]
    public Tilemap targetTilemap;
    public TileBase wallTile;
    public TileBase floorTile;
    public int mapWidth = 81;
    public int mapHeight = 51;
    public Vector2Int mapOrigin = new Vector2Int(0, 0);

    [Header("Random")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Behavior")]
    public MaxNeighborsMode maxNeighborsMode = MaxNeighborsMode.One;
    [Tooltip("When MaxNeighborsMode == Two: independent chance to carve each available direction. If none chosen, we enforce carving one direction.")]
    [Range(0f, 1f)]
    public float twoDirChance = 0.5f;

    public enum MaxNeighborsMode { One, Two }

    [Header("Editor Animation (only editor)")]
    [Tooltip("How many cell steps to perform per editor batch")]
    public int editorStepsPerBatch = 64;
    [Tooltip("Delay (seconds) between editor batches")]
    public float editorBatchDelay = 0.03f;

    private int cellCols; // (mapWidth - 1) / 2
    private int cellRows; // (mapHeight - 1) / 2
    private bool[,] isFloor; // mapWidth x mapHeight

    private SimpleRNG rng;

#if UNITY_EDITOR
    private bool editorAnimating = false;
    private int editorLinearIndex = 0;
    private double editorLastBatchTime = 0.0;
#endif

    private void Start()
    {
    }

    #region Public editor entry points

    [ContextMenu("Generate BinaryTree (Editor Sync)")]
    public void GenerateContext()
    {
        GenerateSync();
    }

    public void GenerateSync()
    {
        if (!ValidateSetup()) return;
        PrepareRandom();
        PrepareMapArrays();
        RunBinaryTreeSync();
        PaintTilemap();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

#if UNITY_EDITOR
    public void StartEditorAnimatedGeneration()
    {
        if (!ValidateSetup()) return;
        if (editorAnimating)
        {
            Debug.Log("[BinaryTreeMazeGenerator] Editor animation already running.");
            return;
        }

        PrepareRandom();
        PrepareMapArrays();
        PaintAllWalls();

        editorLinearIndex = 0;
        editorLastBatchTime = UnityEditor.EditorApplication.timeSinceStartup;
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
        if (now - editorLastBatchTime < editorBatchDelay) return;
        editorLastBatchTime = now;

        int processed = 0;
        int batch = Math.Max(1, editorStepsPerBatch);
        int totalCells = cellCols * cellRows;

        while (processed < batch && editorLinearIndex < totalCells)
        {
            int cx = editorLinearIndex % cellCols;
            int cy = editorLinearIndex / cellCols;
            ProcessCell(cx, cy);
            editorLinearIndex++;
            processed++;
        }

        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        if (editorLinearIndex >= totalCells)
        {
            StopEditorAnimatedGeneration();
        }
    }
#endif

    #endregion

    #region Setup & helpers

    private bool ValidateSetup()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[BinaryTreeMazeGenerator] targetTilemap is null. Assign Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[BinaryTreeMazeGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[BinaryTreeMazeGenerator] mapWidth is even — decreasing by 1 to make it odd for cell mapping.");
            mapWidth = Math.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[BinaryTreeMazeGenerator] mapHeight is even — decreasing by 1 to make it odd for cell mapping.");
            mapHeight = Math.Max(3, mapHeight - 1);
        }

        cellCols = (mapWidth - 1) / 2;
        cellRows = (mapHeight - 1) / 2;

        twoDirChance = Mathf.Clamp01(twoDirChance);

        return true;
    }

    private void PrepareRandom()
    {
        if (useRandomSeed)
            seed = Environment.TickCount;
        rng = new SimpleRNG(seed);
    }

    private void PrepareMapArrays()
    {
        isFloor = new bool[mapWidth, mapHeight];
        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                isFloor[x, y] = false;
    }

    #endregion

    #region Binary Tree core

    private void RunBinaryTreeSync()
    {
        int total = cellCols * cellRows;
        for (int idx = 0; idx < total; idx++)
        {
            int cx = idx % cellCols;
            int cy = idx / cellCols;
            ProcessCell(cx, cy);
        }
    }

    private void ProcessCell(int cx, int cy)
    {
        int mx = cx * 2 + 1;
        int my = cy * 2 + 1;
        SetFloorAtMapLocal(mx, my);

        List<Dir> options = new List<Dir>(2);
        if (cy > 0) options.Add(Dir.North);
        if (cx > 0) options.Add(Dir.West);

        if (options.Count == 0) return;

        if (maxNeighborsMode == MaxNeighborsMode.One)
        {
            int choice = rng.Next(options.Count);
            Dir dir = options[choice];
            CarveDirection(dir, mx, my);
        }
        else 
        {
            bool carvedNorth = false;
            bool carvedWest = false;

            foreach (var o in options)
            {
                bool doCarve = rng.NextDouble() < twoDirChance;
                if (o == Dir.North && doCarve)
                {
                    CarveDirection(Dir.North, mx, my);
                    carvedNorth = true;
                }
                if (o == Dir.West && doCarve)
                {
                    CarveDirection(Dir.West, mx, my);
                    carvedWest = true;
                }
            }

            if (!carvedNorth && !carvedWest)
            {
                int choice = rng.Next(options.Count);
                CarveDirection(options[choice], mx, my);
            }
        }
    }

    private void CarveDirection(Dir dir, int mx, int my)
    {
        if (dir == Dir.North)
        {
            SetFloorAtMapLocal(mx, my - 1);
            SetFloorAtMapLocal(mx, my - 2);
        }
        else // West
        {
            SetFloorAtMapLocal(mx - 1, my);
            SetFloorAtMapLocal(mx - 2, my);
        }
    }

    private enum Dir { North, West }

    #endregion

    #region Tilemap painting and utilities

    private void PaintTilemap()
    {
        if (targetTilemap == null) return;
        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                Vector3Int pos = new Vector3Int(mapOrigin.x + x, mapOrigin.y + y, 0);
                if (isFloor[x, y]) targetTilemap.SetTile(pos, floorTile);
                else targetTilemap.SetTile(pos, wallTile);
            }
        }
    }

    private void PaintAllWalls()
    {
        if (targetTilemap == null) return;
        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                targetTilemap.SetTile(new Vector3Int(mapOrigin.x + x, mapOrigin.y + y, 0), wallTile);
    }

    private void SetFloorAtMapLocal(int localX, int localY)
    {
        if (localX < 0 || localY < 0 || localX >= mapWidth || localY >= mapHeight) return;
        isFloor[localX, localY] = true;
    }

    #endregion

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(BinaryTreeMazeGenerator))]
    private class BinaryTreeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            BinaryTreeMazeGenerator script = (BinaryTreeMazeGenerator)target;

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate (Editor Sync)"))
            {
                script.GenerateSync();
            }
            if (GUILayout.Button("Generate Animated (Editor)"))
            {
                script.StartEditorAnimatedGeneration();
            }
            if (GUILayout.Button("Stop Animated"))
            {
                script.StopEditorAnimatedGeneration();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Area"))
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
            GUILayout.EndHorizontal();
        }
    }
#endif
}
