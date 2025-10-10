using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class GrowingTreeGenerator : MonoBehaviour
{
    public enum SelectionMode { Last, Random, First, Probabilistic }

    [Header("Map")]
    public Tilemap targetTilemap;
    public TileBase wallTile;
    public TileBase floorTile;
    public int mapWidth = 81;   // рекомендуетс€ Ќ≈чЄтные
    public int mapHeight = 51;  // рекомендуетс€ Ќ≈чЄтные
    public Vector2Int mapOrigin = new Vector2Int(0, 0);

    [Header("Algorithm")]
    public SelectionMode selectionMode = SelectionMode.Last;
    [Tooltip("“олько если selectionMode == Probabilistic Ч веро€тность выбрать последний элемент (0..1)")]
    [Range(0f, 1f)]
    public float probPickLast = 0.7f;

    [Header("Random / Reproducibility")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Animation (Play Mode)")]
    public bool animateInPlay = false;
    public int stepsPerBatch = 200;      // сколько "итераций" алгоритма выполнить за один батч
    [Range(0f, 1f)]
    public float playBatchDelay = 0.01f; // задержка между батчами в корутине (сек)

    [Header("Editor Animation (Edit Mode)")]
    public int editorStepsPerBatch = 200;
    public float editorBatchDelay = 0.03f;

    // ¬нутренние
    private bool[,] isFloor; // размер mapWidth x mapHeight
    private SimpleRNG rng;

    // cell grid (логические клетки дл€ лабиринта)
    private int cellCols; // число клеток по X = (mapWidth-1)/2
    private int cellRows; // по Y = (mapHeight-1)/2

    // editor animation state
#if UNITY_EDITOR
    private bool editorAnimating = false;
    private List<Vector2Int> editorActiveList; // список клеток (cell coordinates) как в алгоритме
    private bool[,] editorVisited; // [cellCols,cellRows]
    private double editorLastBatchTime = 0.0;
#endif

    // Play-mode coroutine handle
    private Coroutine playCoroutine = null;

    // Ensure no auto-run in Start
    private void Start()
    {
        // intentionally empty: generator runs only from editor buttons or manually in Play mode.
    }

    #region Public entry points

    [ContextMenu("Generate GrowingTree (Sync)")]
    public void GenerateContext()
    {
        if (Application.isPlaying)
        {
            if (playCoroutine != null) StopCoroutine(playCoroutine);
            playCoroutine = StartCoroutine(GenerateRoutine());
        }
        else
        {
            GenerateSync();
        }
    }

    // —инхронна€ (немедленна€) генераци€ Ч используетс€ дл€ редактора
    public void GenerateSync()
    {
        if (!ValidateSetup()) return;
        PrepareRandom();
        PrepareMapArrays();
        RunGrowingTreeSync();
        PaintTilemap();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    //  орутина дл€ Play mode Ч с батчами и задержками
    public IEnumerator GenerateRoutine()
    {
        if (!ValidateSetup()) yield break;
        PrepareRandom();
        PrepareMapArrays();

        if (!animateInPlay)
        {
            RunGrowingTreeSync();
            PaintTilemap();
            yield break;
        }

        // Animated in Play-mode
        PaintAllWalls();
        // подготовим рабочие структуры
        List<Vector2Int> active = new List<Vector2Int>();
        bool[,] visited = new bool[cellCols, cellRows];

        // стартова€ клетка
        int sx = rng.Next(0, cellCols);
        int sy = rng.Next(0, cellRows);
        visited[sx, sy] = true;
        active.Add(new Vector2Int(sx, sy));
        // carve starting cell
        CarveCellCellcoords(sx, sy);

        while (active.Count > 0)
        {
            int batch = Math.Min(stepsPerBatch, 100000);
            for (int b = 0; b < batch; b++)
            {
                if (active.Count == 0) break;
                int idx = ChooseIndex(active.Count);
                Vector2Int cur = active[idx];
                var neighbors = GetUnvisitedNeighbors(cur, visited);
                if (neighbors.Count > 0)
                {
                    Vector2Int next = neighbors[rng.Next(0, neighbors.Count)];
                    // carve passage between cur and next
                    CarvePassageBetweenCells(cur, next);
                    visited[next.x, next.y] = true;
                    active.Add(new Vector2Int(next.x, next.y));
                }
                else
                {
                    // remove current cell
                    active.RemoveAt(idx);
                }
            }

            PaintTilemap();
            yield return new WaitForSeconds(playBatchDelay);
        }

        PaintTilemap();
    }

    #endregion

    #region Core algorithm (sync)

    private void RunGrowingTreeSync()
    {
        // paint walls initially in memory
        // (we will paint Tilemap at the end)
        // structures
        bool[,] visited = new bool[cellCols, cellRows];
        List<Vector2Int> active = new List<Vector2Int>();

        // pick starting cell
        int sx = rng.Next(0, cellCols);
        int sy = rng.Next(0, cellRows);
        visited[sx, sy] = true;
        active.Add(new Vector2Int(sx, sy));
        CarveCellCellcoords(sx, sy);

        while (active.Count > 0)
        {
            int idx = ChooseIndex(active.Count);
            Vector2Int cur = active[idx];
            var neighbors = GetUnvisitedNeighbors(cur, visited);
            if (neighbors.Count > 0)
            {
                Vector2Int next = neighbors[rng.Next(0, neighbors.Count)];
                CarvePassageBetweenCells(cur, next);
                visited[next.x, next.y] = true;
                active.Add(next);
            }
            else
            {
                active.RemoveAt(idx);
            }
        }
    }

    #endregion

    #region Helpers: initialization / mapping

    private bool ValidateSetup()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[GrowingTreeGenerator] targetTilemap is null. Assign Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[GrowingTreeGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        // ensure odd sizes (typical for maze generation where cells are separated by walls)
        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[GrowingTreeGenerator] mapWidth is even Ч decreasing by 1 to make it odd for maze layout.");
            mapWidth = Mathf.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[GrowingTreeGenerator] mapHeight is even Ч decreasing by 1 to make it odd for maze layout.");
            mapHeight = Mathf.Max(3, mapHeight - 1);
        }

        cellCols = (mapWidth - 1) / 2;
        cellRows = (mapHeight - 1) / 2;

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
        // initially everything false (walls)
    }

    // ¬спомогательна€ карвингова€ логика: cellCoords -> map coords
    // cell (cx, cy) maps to mapX = cx * 2 + 1, mapY = cy * 2 + 1 (относительно mapOrigin)
    private void CarveCellCellcoords(int cx, int cy)
    {
        int mapX = cx * 2 + 1;
        int mapY = cy * 2 + 1;
        SetFloorAtMapLocal(mapX, mapY);
    }

    // carve passage between two neighbouring cells (cell coords)
    private void CarvePassageBetweenCells(Vector2Int a, Vector2Int b)
    {
        // carve both cells and the wall between them
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

    // ¬озвращает список соседних клеток (cell coords) непосещЄнных
    private List<Vector2Int> GetUnvisitedNeighbors(Vector2Int cell, bool[,] visited)
    {
        var res = new List<Vector2Int>(4);
        int cx = cell.x;
        int cy = cell.y;
        // 4-way neighbors on cell grid
        if (cx > 0 && !visited[cx - 1, cy]) res.Add(new Vector2Int(cx - 1, cy));
        if (cx + 1 < cellCols && !visited[cx + 1, cy]) res.Add(new Vector2Int(cx + 1, cy));
        if (cy > 0 && !visited[cx, cy - 1]) res.Add(new Vector2Int(cx, cy - 1));
        if (cy + 1 < cellRows && !visited[cx, cy + 1]) res.Add(new Vector2Int(cx, cy + 1));
        return res;
    }

    // выбор индекса в active list по выбранной стратегии
    private int ChooseIndex(int listCount)
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
    // -------------------
    // Editor-mode animated generation
    // -------------------

    public void StartEditorAnimatedGeneration()
    {
        if (!ValidateSetup()) return;
        if (editorAnimating)
        {
            Debug.Log("[GrowingTreeGenerator] Editor animation already running.");
            return;
        }

        PrepareRandom();
        PrepareMapArrays();

        // init visited and active list
        editorVisited = new bool[cellCols, cellRows];
        editorActiveList = new List<Vector2Int>();

        int sx = rng.Next(0, cellCols);
        int sy = rng.Next(0, cellRows);
        editorVisited[sx, sy] = true;
        editorActiveList.Add(new Vector2Int(sx, sy));
        CarveCellCellcoords(sx, sy);

        // paint walls first
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

        int batch = editorStepsPerBatch;
        for (int i = 0; i < batch; i++)
        {
            if (editorActiveList.Count == 0)
            {
                StopEditorAnimatedGeneration();
                return;
            }

            int idx = ChooseIndex(editorActiveList.Count);
            Vector2Int cur = editorActiveList[idx];
            var neighbors = GetUnvisitedNeighbors(cur, editorVisited);
            if (neighbors.Count > 0)
            {
                Vector2Int next = neighbors[rng.Next(0, neighbors.Count)];
                CarvePassageBetweenCells(cur, next);
                editorVisited[next.x, next.y] = true;
                editorActiveList.Add(next);
            }
            else
            {
                editorActiveList.RemoveAt(idx);
            }
        }

        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }

    // Custom inspector buttons
    [UnityEditor.CustomEditor(typeof(GrowingTreeGenerator))]
    private class GrowingTreeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GrowingTreeGenerator script = (GrowingTreeGenerator)target;

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
                    if (script.playCoroutine != null) script.StopCoroutine(script.playCoroutine);
                    script.playCoroutine = script.StartCoroutine(script.GenerateRoutine());
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
