using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class OurEllerGenerator : MonoBehaviour
{
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
    public int mapWidth = 81;
    public int mapHeight = 51;
    public Vector2Int mapOrigin = new Vector2Int(0, 0);

    [Header("Algorithm")]
    [Tooltip("Chance denominator for ending a run. Smaller -> runs end more often. Value >= 1.")]
    public int runEndWeight = 2;

    [Header("Cycles (post-process)")]
    [Tooltip("Если true — после построения лабиринта будут добавлены случайные дополнительные проходы (циклы).")]
    public bool addCycles = false;
    [Tooltip("Вероятность для каждой клетки (узла) пробовать добавить проход в одном случайно выбранном направлении (0..1).")]
    [Range(0f, 1f)]
    public float cycleProbability = 0.05f;

    [Header("Random / Reproducibility")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Editor Animation (only editor)")]
    public int editorStepsPerBatch = 200;
    public float editorBatchDelay = 0.03f;

    private int cellCols; // (mapWidth - 1) / 2
    private int cellRows; // (mapHeight - 1) / 2

    private int[,] cellFlags; 
    private bool[,] isFloor; 

    private Well512Random rng;

#if UNITY_EDITOR
    private bool editorAnimating = false;
    private int editorCx = 0;
    private int editorCy = 0;
    private int editorRunStart = 0;
    private double editorLastBatchTime = 0.0;
#endif

    private void Start()
    {
    }

    #region Public entry points

    [ContextMenu("Generate (Sync)")]
    public void GenerateContext()
    {
        GenerateSync();
    }

    public void GenerateSync()
    {
        if (!ValidateSetup()) return;
        PrepareRandom();
        PrepareMapArrays();
        RunOurEllerSync();
        FinalizeMapCenters();

        if (addCycles)
            AddCyclesPostProcess();

        PaintTilemap();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    #endregion

    #region Core algorithm (sync)

    private void RunOurEllerSync()
    {
        for (int cy = 0; cy < cellRows; cy++)
        {
            int runStart = 0;
            for (int cx = 0; cx < cellCols; cx++)
            {
                bool endRun = (cy > 0) && (cx + 1 == cellCols || rng.Next(Math.Max(1, runEndWeight)) == 0);
                if (endRun)
                {
                    int span = cx - runStart + 1;
                    int chosen = runStart + rng.Next(span);
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
        cellFlags[cx, cy] |= E;
        if (cx + 1 < cellCols) cellFlags[cx + 1, cy] |= W;

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
        if (cy <= 0) return;
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
            Debug.LogWarning("[OurEllerGenerator] targetTilemap is null. Assign a Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[OurEllerGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[OurEllerGenerator] mapWidth is even — decreasing by 1 to make it odd for cell mapping.");
            mapWidth = Mathf.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[OurEllerGenerator] mapHeight is even — decreasing by 1 to make it odd for cell mapping.");
            mapHeight = Mathf.Max(3, mapHeight - 1);
        }

        cellCols = (mapWidth - 1) / 2;
        cellRows = (mapHeight - 1) / 2;

        if (runEndWeight < 1) runEndWeight = 1;

        cycleProbability = Mathf.Clamp01(cycleProbability);

        return true;
    }

    private void PrepareRandom()
    {
        if (useRandomSeed)
            seed = Environment.TickCount;
        rng = new Well512Random(seed);
    }

    private void PrepareMapArrays()
    {
        cellFlags = new int[cellCols, cellRows];
        isFloor = new bool[mapWidth, mapHeight];
    }

    #endregion

    #region Post-process: add cycles (per-node single-direction test)
    private void AddCyclesPostProcess()
    {
        if (!addCycles) return;

        int trialsScale = 1000000;
        int threshold = (int)(cycleProbability * trialsScale);

        for (int mx = 1; mx <= mapWidth - 2; mx += 2)
        {
            for (int my = 1; my <= mapHeight - 2; my += 2)
            {
                // 0=N,1=S,2=E,3=W
                var dirs = new List<int>(4);
                if (my - 2 >= 1) dirs.Add(0); // North
                if (my + 2 <= mapHeight - 2) dirs.Add(1); // South
                if (mx + 2 <= mapWidth - 2) dirs.Add(2); // East
                if (mx - 2 >= 1) dirs.Add(3); // West

                if (dirs.Count == 0) continue;

                int pickIndex = rng.Next(dirs.Count);
                int dir = dirs[pickIndex];

                int r = rng.Next(trialsScale);
                if (r >= threshold) continue;

                switch (dir)
                {
                    case 0: // North
                        SetFloorAtMapLocal(mx, my - 1);
                        SetFloorAtMapLocal(mx, my - 2);
                        break;
                    case 1: // South
                        SetFloorAtMapLocal(mx, my + 1);
                        SetFloorAtMapLocal(mx, my + 2);
                        break;
                    case 2: // East
                        SetFloorAtMapLocal(mx + 1, my);
                        SetFloorAtMapLocal(mx + 2, my);
                        break;
                    case 3: // West
                        SetFloorAtMapLocal(mx - 1, my);
                        SetFloorAtMapLocal(mx - 2, my);
                        break;
                }
            }
        }
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
    public void StartEditorAnimatedGeneration()
    {
        if (!ValidateSetup()) return;
        if (editorAnimating)
        {
            Debug.Log("[OurEllerGenerator] Editor animation already running.");
            return;
        }

        PrepareRandom();
        PrepareMapArrays();

        PaintAllWalls();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

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
                FinalizeMapCenters();
                if (addCycles) AddCyclesPostProcess();
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

            editorCx++;
            if (editorCx >= cellCols)
            {
                editorCy++;
                editorCx = 0;
                editorRunStart = 0;
            }

            processed++;
        }

        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

    }

    [UnityEditor.CustomEditor(typeof(OurEllerGenerator))]
    private class OurEllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            OurEllerGenerator script = (OurEllerGenerator)target;

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
