// SidewinderGenerator.cs
// Sidewinder maze algorithm Ч Editor-only generation buttons + Editor-mode animated build with batching.
// Uses SimpleRNG for deterministic randomness (must be present in project).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class SidewinderGenerator : MonoBehaviour
{
    // Directions (bit flags)
    private const int N = 1;
    private const int S = 2;
    private const int E = 4;
    private const int W = 8;

    private static readonly Dictionary<int, int> DX = new Dictionary<int, int> { { E, 1 }, { W, -1 }, { N, 0 }, { S, 0 } };
    private static readonly Dictionary<int, int> DY = new Dictionary<int, int> { { E, 0 }, { W, 0 }, { N, -1 }, { S, 1 } };
    private static readonly Dictionary<int, int> OPPOSITE = new Dictionary<int, int> { { E, W }, { W, E }, { N, S }, { S, N } };

    [Header("Map")]
    public Tilemap targetTilemap;
    public TileBase wallTile;
    public TileBase floorTile;
    public int mapWidth = 81;   // рекомендуетс€ нечЄтные
    public int mapHeight = 51;
    public Vector2Int mapOrigin = new Vector2Int(0, 0);

    [Header("Algorithm")]
    [Tooltip("Chance denominator for ending a run. Smaller -> runs end more often. Value >= 1.")]
    public int runEndWeight = 2;

    [Header("Random / Reproducibility")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Editor Animation (only editor)")]
    public int editorStepsPerBatch = 200;   // сколько клеток обрабатывать за батч
    public float editorBatchDelay = 0.03f;  // задержка между батчами (сек)

    [Header("Play-mode (optional)")]
    public bool animateInPlay = false;
    public int playStepsPerBatch = 200;
    public float playBatchDelay = 0.01f;

    // Internal
    private int cellCols; // (mapWidth - 1) / 2
    private int cellRows; // (mapHeight - 1) / 2

    private int[,] cellFlags; // cellCols x cellRows bitflags (N/S/E/W)
    private bool[,] isFloor; // mapWidth x mapHeight (tile-level)

    private SimpleRNG rng;

#if UNITY_EDITOR
    // Editor animation state
    private bool editorAnimating = false;
    private int editorCx = 0;
    private int editorCy = 0;
    private int editorRunStart = 0;
    private double editorLastBatchTime = 0.0;
#endif

    // No auto-run
    private void Start()
    {
        // intentionally empty: generation only via Editor buttons or manual Play coroutine.
    }

    #region Public entry points

    [ContextMenu("Generate Sidewinder (Editor Sync)")]
    public void GenerateContext()
    {
        if (Application.isPlaying)
        {
            if (playStepsPerBatch <= 0) playStepsPerBatch = 200;
            StartCoroutine(GenerateRoutine());
        }
        else
        {
            GenerateSync();
        }
    }

    // synchronous (fast) generation used in editor
    public void GenerateSync()
    {
        if (!ValidateSetup()) return;
        PrepareRandom();
        PrepareMapArrays();
        RunSidewinderSync();
        FinalizeMapCenters(); // ensure all cell centers are floors
        PaintTilemap();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    // Optional Play-mode coroutine (animated in Play)
    public System.Collections.IEnumerator GenerateRoutine()
    {
        if (!ValidateSetup()) yield break;
        PrepareRandom();
        PrepareMapArrays();

        if (!animateInPlay)
        {
            RunSidewinderSync();
            FinalizeMapCenters();
            PaintTilemap();
            yield break;
        }

        // Animated in Play-mode
        PaintAllWalls();

        int cx = 0, cy = 0, runStart = 0;
        int steps = 0;
        while (cy < cellRows)
        {
            int batch = Math.Min(playStepsPerBatch, 100000);
            for (int b = 0; b < batch; b++)
            {
                // process single cell (cx,cy)
                bool endRun = (cy > 0) && (cx + 1 == cellCols || rng.Next(Math.Max(1, runEndWeight)) == 0);
                if (endRun)
                {
                    int span = cx - runStart + 1;
                    int chosen = runStart + rng.Next(span); // [0..span-1]
                    CarveNorth(chosen, cy);
                    runStart = cx + 1;
                }
                else if (cx + 1 < cellCols)
                {
                    CarveEast(cx, cy);
                }

                // advance to next cell
                cx++;
                if (cx >= cellCols)
                {
                    cy++;
                    cx = 0;
                    runStart = 0;
                    if (cy >= cellRows) break;
                }
            }

            PaintTilemap();
            yield return new WaitForSeconds(playBatchDelay);
        }

        FinalizeMapCenters();
        PaintTilemap();
    }

    #endregion

    #region Core algorithm (sync)

    private void RunSidewinderSync()
    {
        // Iterate over cell-grid row-wise
        for (int cy = 0; cy < cellRows; cy++)
        {
            int runStart = 0;
            for (int cx = 0; cx < cellCols; cx++)
            {
                bool endRun = (cy > 0) && (cx + 1 == cellCols || rng.Next(Math.Max(1, runEndWeight)) == 0);
                if (endRun)
                {
                    int span = cx - runStart + 1;
                    int chosen = runStart + rng.Next(span); // rng.Next(span) gives [0..span-1]
                    CarveNorth(chosen, cy);
                    runStart = cx + 1;
                }
                else if (cx + 1 < cellCols)
                {
                    CarveEast(cx, cy);
                }
            }
        }
    }

    #endregion

    #region Carving helpers (cell -> map)

    private void CarveEast(int cx, int cy)
    {
        // set flags
        cellFlags[cx, cy] |= E;
        if (cx + 1 < cellCols) cellFlags[cx + 1, cy] |= W;

        // mark floor at centers and middle
        int ax = cx * 2 + 1;
        int ay = cy * 2 + 1;
        int bx = (cx + 1) * 2 + 1;
        int by = ay;

        SetFloorAtMapLocal(ax, ay);
        SetFloorAtMapLocal(bx, by);
        SetFloorAtMapLocal((ax + bx) / 2, ay);
    }

    private void CarveNorth(int cx, int cy)
    {
        if (cy <= 0) return; // cannot carve north from top row
        cellFlags[cx, cy] |= N;
        cellFlags[cx, cy - 1] |= S;

        int ax = cx * 2 + 1;
        int ay = cy * 2 + 1;
        int bx = ax;
        int by = (cy - 1) * 2 + 1;

        SetFloorAtMapLocal(ax, ay);
        SetFloorAtMapLocal(bx, by);
        SetFloorAtMapLocal(ax, (ay + by) / 2);
    }

    // Ensure every cell center is floor (useful for cells that never had edges carved)
    private void FinalizeMapCenters()
    {
        for (int cy = 0; cy < cellRows; cy++)
            for (int cx = 0; cx < cellCols; cx++)
            {
                int mx = cx * 2 + 1;
                int my = cy * 2 + 1;
                SetFloorAtMapLocal(mx, my);
            }
    }

    private void SetFloorAtMapLocal(int localX, int localY)
    {
        if (localX < 0 || localY < 0 || localX >= mapWidth || localY >= mapHeight) return;
        isFloor[localX, localY] = true;
    }

    #endregion

    #region Setup & validation

    private bool ValidateSetup()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[SidewinderGenerator] targetTilemap is null. Assign a Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[SidewinderGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[SidewinderGenerator] mapWidth is even Ч decreasing by 1 to make it odd for cell mapping.");
            mapWidth = Mathf.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[SidewinderGenerator] mapHeight is even Ч decreasing by 1 to make it odd for cell mapping.");
            mapHeight = Mathf.Max(3, mapHeight - 1);
        }

        cellCols = (mapWidth - 1) / 2;
        cellRows = (mapHeight - 1) / 2;

        if (runEndWeight < 1) runEndWeight = 1;

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
        cellFlags = new int[cellCols, cellRows];
        isFloor = new bool[mapWidth, mapHeight];
    }

    #endregion

    #region Tilemap painting

    private void PaintTilemap()
    {
        if (targetTilemap == null) return;
        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                Vector3Int pos = new Vector3Int(mapOrigin.x + x, mapOrigin.y + y, 0);
                if (isFloor[x, y])
                    targetTilemap.SetTile(pos, floorTile);
                else
                    targetTilemap.SetTile(pos, wallTile);
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

    #endregion

#if UNITY_EDITOR
    // -------------------
    // Editor-mode animated generation
    // -------------------

    public void StartEditorAnimatedGeneration()
    {
        if (!ValidateSetup()) return;
        if (editorAnimating)
        {
            Debug.Log("[SidewinderGenerator] Editor animation already running.");
            return;
        }

        PrepareRandom();
        PrepareMapArrays();

        // initial paint: walls
        PaintAllWalls();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        // init editor-step state
        editorCx = 0;
        editorCy = 0;
        editorRunStart = 0;
        editorLastBatchTime = UnityEditor.EditorApplication.timeSinceStartup;
        editorAnimating = true;
        UnityEditor.EditorApplication.update += EditorUpdateStep;
    }

    public void StopEditorAnimatedGeneration()
    {
        if (!editorAnimating) return;
        editorAnimating = false;
        UnityEditor.EditorApplication.update -= EditorUpdateStep;
        FinalizeMapCenters();
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

        while (processed < batch)
        {
            if (editorCy >= cellRows)
            {
                StopEditorAnimatedGeneration();
                return;
            }

            int cx = editorCx;
            int cy = editorCy;

            bool endRun = (cy > 0) && (cx + 1 == cellCols || rng.Next(Math.Max(1, runEndWeight)) == 0);
            if (endRun)
            {
                int span = cx - editorRunStart + 1;
                int chosen = editorRunStart + rng.Next(span);
                CarveNorth(chosen, cy);
                editorRunStart = cx + 1;
            }
            else if (cx + 1 < cellCols)
            {
                CarveEast(cx, cy);
            }

            // advance
            editorCx++;
            if (editorCx >= cellCols)
            {
                editorCy++;
                editorCx = 0;
                editorRunStart = 0;
            }

            processed++;
        }

        // update visual after batch
        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        // if finished, will be stopped at next loop check
    }

    // inspector buttons
    [UnityEditor.CustomEditor(typeof(SidewinderGenerator))]
    private class SidewinderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SidewinderGenerator script = (SidewinderGenerator)target;

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
            if (GUILayout.Button("Generate (Play Mode)"))
            {
                if (Application.isPlaying)
                {
                    if (script != null)
                    {
                        script.StartCoroutine(script.GenerateRoutine());
                    }
                }
                else
                {
                    Debug.LogWarning("Enter Play mode to run Play-mode coroutine.");
                }
            }
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
