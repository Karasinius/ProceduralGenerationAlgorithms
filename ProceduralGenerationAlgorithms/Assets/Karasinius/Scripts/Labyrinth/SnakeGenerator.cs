using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class SnakeGenerator : MonoBehaviour
{
    public enum SelectionMode { Last, Random, First, Probabilistic }

    [Header("Map")]
    public Tilemap targetTilemap;
    public TileBase wallTile;
    public TileBase floorTile;
    public int mapWidth = 81;   // нужны Ќ≈чЄтные
    public int mapHeight = 51;  // нужны Ќ≈чЄтные
    public Vector2Int mapOrigin = new Vector2Int(0, 0);

    [Header("Algorithm")]
    public SelectionMode selectionMode = SelectionMode.Last;
    [Tooltip("“олько если selectionMode == Probabilistic Ч веро€тность выбрать последний элемент (0..1)")]
    [Range(0f, 1f)]
    public float probPickLast = 0.7f;

    [Header("Cycles (post-process)")]
    [Tooltip("≈сли true Ч после построени€ лабиринта будет выполнен пост-процесс добавлени€ дополнительных проходов.")]
    public bool addCycles = false;
    [Tooltip("¬еро€тность дл€ каждой клетки (узла) добавить проход в одном случайно выбранном направлении (0..1).")]
    [Range(0f, 1f)]
    public float cycleProbability = 0.05f;

    [Header("Random / Reproducibility")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Editor Animation (Edit Mode)")]
    public int editorStepsPerBatch = 200;
    public float editorBatchDelay = 0.03f;

    private bool[,] isFloor;
    private SimpleRNG rng;

    private int cellCols;
    private int cellRows;

#if UNITY_EDITOR
    private bool editorAnimating = false;
    private List<Vector2Int> editorActiveList;
    private bool[,] editorVisited;
    private int editorCurrentIndex = -1;
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
        RunSnakeSync();

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

    private void RunSnakeSync()
    {
        bool[,] visited = new bool[cellCols, cellRows];
        List<Vector2Int> active = new List<Vector2Int>();

        int sx = rng.Next(0, cellCols);
        int sy = rng.Next(0, cellRows);
        visited[sx, sy] = true;
        active.Add(new Vector2Int(sx, sy));
        CarveCellCellcoords(sx, sy);

        int currentIndex = 0;

        while (active.Count > 0)
        {
            if (currentIndex < 0 || currentIndex >= active.Count)
                currentIndex = ChooseIndexOnNeed(active.Count);

            Vector2Int cur = active[currentIndex];
            var neighbors = GetUnvisitedNeighbors(cur, visited);
            if (neighbors.Count > 0)
            {
                Vector2Int next = neighbors[rng.Next(0, neighbors.Count)];
                CarvePassageBetweenCells(cur, next);
                visited[next.x, next.y] = true;
                active.Add(next);

                currentIndex = active.Count - 1;
            }
            else
            {
                active.RemoveAt(currentIndex);

                if (active.Count > 0)
                {
                    currentIndex = ChooseIndexOnNeed(active.Count);
                }
                else
                {
                    currentIndex = -1;
                }
            }
        }
    }

    #endregion

    #region Helpers: initialization / mapping

    private bool ValidateSetup()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[SnakeGenerator] targetTilemap is null. Assign Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[SnakeGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[SnakeGenerator] mapWidth is even Ч decreasing by 1 to make it odd for maze layout.");
            mapWidth = Mathf.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[SnakeGenerator] mapHeight is even Ч decreasing by 1 to make it odd for maze layout.");
            mapHeight = Mathf.Max(3, mapHeight - 1);
        }

        cellCols = (mapWidth - 1) / 2;
        cellRows = (mapHeight - 1) / 2;

        cycleProbability = Mathf.Clamp01(cycleProbability);

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

    private void CarveCellCellcoords(int cx, int cy)
    {
        int mapX = cx * 2 + 1;
        int mapY = cy * 2 + 1;
        SetFloorAtMapLocal(mapX, mapY);
    }

    private void CarvePassageBetweenCells(Vector2Int a, Vector2Int b)
    {
        CarveCellCellcoords(a.x, a.y);
        CarveCellCellcoords(b.x, b.y);

        int ax = a.x * 2 + 1;
        int ay = a.y * 2 + 1;
        int bx = b.x * 2 + 1;
        int by = b.y * 2 + 1;

        int mx = (ax + bx) / 2;
        int my = (ay + by) / 2;

        SetFloorAtMapLocal(mx, my);
    }

    private void SetFloorAtMapLocal(int localX, int localY)
    {
        int x = localX;
        int y = localY;
        if (x < 0 || y < 0 || x >= mapWidth || y >= mapHeight) return;
        isFloor[x, y] = true;
    }

    private List<Vector2Int> GetUnvisitedNeighbors(Vector2Int cell, bool[,] visited)
    {
        var res = new List<Vector2Int>(4);
        int cx = cell.x;
        int cy = cell.y;
        if (cx > 0 && !visited[cx - 1, cy]) res.Add(new Vector2Int(cx - 1, cy));
        if (cx + 1 < cellCols && !visited[cx + 1, cy]) res.Add(new Vector2Int(cx + 1, cy));
        if (cy > 0 && !visited[cx, cy - 1]) res.Add(new Vector2Int(cx, cy - 1));
        if (cy + 1 < cellRows && !visited[cx, cy + 1]) res.Add(new Vector2Int(cx, cy + 1));
        return res;
    }
    private int ChooseIndexOnNeed(int listCount)
    {
        if (listCount <= 0) return -1;
        switch (selectionMode)
        {
            case SelectionMode.Last:
                return listCount - 1;
            case SelectionMode.First:
                return 0;
            case SelectionMode.Random:
                return rng.Next(0, listCount);
            case SelectionMode.Probabilistic:
                double p = rng.NextDouble();
                if (p < probPickLast)
                    return listCount - 1;
                else
                    return rng.Next(0, listCount);
            default:
                return listCount - 1;
        }
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
                var dirs = new List<int>(4); // 0=N,1=S,2=E,3=W
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
            Debug.Log("[SnakeGenerator] Editor animation already running.");
            return;
        }

        PrepareRandom();
        PrepareMapArrays();

        editorVisited = new bool[cellCols, cellRows];
        editorActiveList = new List<Vector2Int>();

        int sx = rng.Next(0, cellCols);
        int sy = rng.Next(0, cellRows);
        editorVisited[sx, sy] = true;
        editorActiveList.Add(new Vector2Int(sx, sy));
        CarveCellCellcoords(sx, sy);

        editorCurrentIndex = 0;

        PaintAllWalls();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        editorLastBatchTime = UnityEditor.EditorApplication.timeSinceStartup;
        editorAnimating = true;
        UnityEditor.EditorApplication.update += EditorUpdateStep;
    }

    public void StopEditorAnimatedGeneration()
    {
        if (!editorAnimating) return;
        editorAnimating = false;
        UnityEditor.EditorApplication.update -= EditorUpdateStep;

        if (addCycles)
            AddCyclesPostProcess();

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

        int batch = Math.Max(1, editorStepsPerBatch);
        for (int i = 0; i < batch; i++)
        {
            if (editorActiveList.Count == 0)
            {
                if (addCycles)
                    AddCyclesPostProcess();

                StopEditorAnimatedGeneration();
                return;
            }

            if (editorCurrentIndex < 0 || editorCurrentIndex >= editorActiveList.Count)
                editorCurrentIndex = ChooseIndexOnNeed(editorActiveList.Count);

            Vector2Int cur = editorActiveList[editorCurrentIndex];
            var neighbors = GetUnvisitedNeighbors(cur, editorVisited);
            if (neighbors.Count > 0)
            {
                Vector2Int next = neighbors[rng.Next(0, neighbors.Count)];
                CarvePassageBetweenCells(cur, next);
                editorVisited[next.x, next.y] = true;
                editorActiveList.Add(next);

                editorCurrentIndex = editorActiveList.Count - 1;
            }
            else
            {
                editorActiveList.RemoveAt(editorCurrentIndex);
                if (editorActiveList.Count > 0)
                    editorCurrentIndex = ChooseIndexOnNeed(editorActiveList.Count);
                else
                    editorCurrentIndex = -1;
            }
        }

        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }

    [UnityEditor.CustomEditor(typeof(SnakeGenerator))]
    private class SnakeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SnakeGenerator script = (SnakeGenerator)target;

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
