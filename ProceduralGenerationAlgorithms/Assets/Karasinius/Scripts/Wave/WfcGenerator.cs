using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
public class WfcGenerator : MonoBehaviour
{
    [Header("Grid")]
    public int width = 60;
    public int height = 34;
    public Vector2Int origin = Vector2Int.zero;

    [Header("Tilemap target")]
    public Tilemap targetTilemap;

    [Header("Tile types ")]
    public List<WfcTileType> tileTypes = new List<WfcTileType>();

    [Header("Generation options")]
    public int maxRestarts = 100;
    [Tooltip("Use Shannon entropy (true) or simple count (false) when picking cell")]
    public bool useShannonEntropy = true;
    [Tooltip("If true - seed is randomized every run")]
    public bool useRandomSeed = true;
    [Tooltip("When useRandomSeed==false and seed >= 0 -> deterministic")]
    public int seed = -1;

    [Header("Conflict handling")]
    [Tooltip("If true -> will retry generation up to maxRestarts (old behavior). If false -> stop at first conflict and paint conflictTile.")]
    public bool retryOnConflict = true;
    [Tooltip("Tile used to mark a conflict cell when retryOnConflict == false")]
    public TileBase conflictTile;

    [Header("Editor animation settings (Edit mode)")]
    [Tooltip("How many cell-collapse steps to perform per editor batch")]
    public int editorStepsPerBatch = 64;
    [Tooltip("Delay (seconds) between editor batches")]
    public float editorBatchDelay = 0.03f;


    public bool isGenerating = false;
    public int currentAttempt = 0;

    private System.Random rng;

    private const int NORTH = 0;
    private const int EAST = 1;
    private const int SOUTH = 2;
    private const int WEST = 3;

    #region Public inspector entry points

    [ContextMenu("Generate (Editor Sync)")]
    public void GenerateSync()
    {
        if (!ValidateSetup()) return;
        if (isGenerating)
        {
            Debug.LogWarning("WFC: Generation already running.");
            return;
        }

        PrepareForNewRun();

        isGenerating = true;
        rng = CreateRng();

        if (retryOnConflict)
        {
            bool succeeded = false;
            for (int attempt = 0; attempt < maxRestarts; attempt++)
            {
                currentAttempt = attempt + 1;
                if (TryGenerateOnce(out var finalGrid, out var _conflictPos))
                {
                    ApplyToTilemap(finalGrid);
                    Debug.Log($"WFC: Sync generation succeeded on attempt {attempt + 1}");
                    succeeded = true;
                    break;
                }

            }

            if (!succeeded)
                Debug.LogError($"WFC: Sync generation failed after {maxRestarts} attempts.");
        }
        else
        {
            currentAttempt = 1;
            if (TryGenerateOnce(out var finalGrid, out var conflictPos))
            {
                ApplyToTilemap(finalGrid);
                Debug.Log($"WFC: Sync generation succeeded (no-retry mode).");
            }
            else
            {
                if (conflictTile != null)
                {
                    PaintConflict(conflictPos);
                    Debug.LogError($"WFC: Sync generation conflict at {conflictPos}. Painted conflict tile and stopped (retryOnConflict=false).");
                }
                else
                {
                    Debug.LogError($"WFC: Sync generation conflict at {conflictPos}. No conflictTile assigned (retryOnConflict=false).");
                }
            }
        }

        isGenerating = false;
        currentAttempt = 0;
    }

    public void StartEditorAnimatedGeneration()
    {
#if UNITY_EDITOR
        if (!ValidateSetup()) return;
        if (isGenerating)
        {
            Debug.LogWarning("WFC: Generation already running.");
            return;
        }

        PrepareForNewRun();
        isGenerating = true;
        rng = CreateRng();
        editorCurrentAttempt = 0;
        BeginEditorAttempt();
        editorLastBatchTime = UnityEditor.EditorApplication.timeSinceStartup;
        editorAnimating = true;
        UnityEditor.EditorApplication.update += EditorUpdateStep;
#else
        Debug.LogWarning("StartEditorAnimatedGeneration is only available in Editor.");
#endif
    }

    public void StopEditorAnimatedGeneration()
    {
#if UNITY_EDITOR
        if (!editorAnimating) return;
        StopEditorAnimatedGeneration_Internal();
        Debug.Log("WFC: Editor animation stopped by user.");
#else
        Debug.LogWarning("StopEditorAnimatedGeneration is only available in Editor.");
#endif
    }

    public void ClearTiles()
    {
        if (isGenerating)
        {
            Debug.LogWarning("WFC: Can't clear while generating. Stop animation first.");
            return;
        }
        ClearAreaInternal();
    }

    #endregion

    #region Core WFC logic (shared by sync and editor animation)

    private bool ValidateSetup()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("WFC: targetTilemap is null. Assign Tilemap in inspector.");
            return false;
        }
        if (tileTypes == null || tileTypes.Count == 0)
        {
            Debug.LogWarning("WFC: No tile types assigned.");
            return false;
        }
        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning("WFC: Invalid width/height.");
            return false;
        }
        return true;
    }

    private void ClearAreaInternal()
    {
        if (targetTilemap == null) return;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), null);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    private void PrepareForNewRun()
    {
        ClearAreaInternal();
    }

    private System.Random CreateRng()
    {
        if (!useRandomSeed && seed >= 0)
            return new XorShift64StarRandom(seed);
        int s = Guid.NewGuid().GetHashCode();
        return new XorShift64StarRandom(s);
    }

    private WfcCell[,] CreateEmptyGrid()
    {
        WfcCell[,] grid = new WfcCell[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = new WfcCell(x, y, tileTypes);
        return grid;
    }

    private int CountCollapsed(WfcCell[,] grid)
    {
        int c = 0;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (grid[x, y].IsCollapsed) c++;
        return c;
    }

    private WfcCell PickCellWithLowestEntropy(WfcCell[,] grid)
    {
        WfcCell best = null;
        double bestVal = double.MaxValue;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = grid[x, y];
                if (cell.IsCollapsed) continue;
                if (cell.possibilities.Count == 0) return cell; // conflict

                double val;
                if (useShannonEntropy)
                {
                    double total = cell.possibilities.Sum(t => Math.Max(1, t.weight));
                    double H = 0.0;
                    foreach (var t in cell.possibilities)
                    {
                        double p = Math.Max(1, t.weight) / total;
                        H -= (p > 0) ? p * Math.Log(p) : 0.0;
                    }
                    val = H;
                }
                else
                {
                    val = cell.possibilities.Count;
                }

                double noise = rng != null ? rng.NextDouble() * 1e-6 : UnityEngine.Random.value * 1e-6;
                double score = val + noise;
                if (score < bestVal)
                {
                    bestVal = score;
                    best = cell;
                }
            }
        }

        return best;
    }

    private bool TryGenerateOnce(out WfcCell[,] finalGrid, out Vector2Int conflictPos)
    {
        finalGrid = null;
        conflictPos = new Vector2Int(-1, -1);

        WfcCell[,] grid = CreateEmptyGrid();

        int cellsTotal = width * height;
        int collapsedCount = 0;

        while (collapsedCount < cellsTotal)
        {
            var cell = PickCellWithLowestEntropy(grid);
            if (cell == null) break;

            if (cell.possibilities.Count == 0)
            {
                conflictPos = new Vector2Int(cell.x, cell.y);
                return false;
            }

            cell.CollapseWeighted(rng);
            if (cell.IsCollapsed) collapsedCount++;

            Stack<WfcCell> stack = new Stack<WfcCell>();
            stack.Push(cell);

            bool conflict = false;
            Vector2Int localConflict = new Vector2Int(-1, -1);

            while (stack.Count > 0 && !conflict)
            {
                var t = stack.Pop();
                var tPoss = t.possibilities;
                int tx = t.x;
                int ty = t.y;

                // NORTH
                if (ty + 1 < height)
                {
                    var n = grid[tx, ty + 1];
                    if (n.possibilities.Count > 0)
                    {
                        var res = n.Constrain(tPoss, NORTH);
                        if (res == ConstrainResult.Conflict) { conflict = true; localConflict = new Vector2Int(n.x, n.y); break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
                // EAST
                if (tx + 1 < width)
                {
                    var n = grid[tx + 1, ty];
                    if (n.possibilities.Count > 0)
                    {
                        var res = n.Constrain(tPoss, EAST);
                        if (res == ConstrainResult.Conflict) { conflict = true; localConflict = new Vector2Int(n.x, n.y); break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
                // SOUTH
                if (ty - 1 >= 0)
                {
                    var n = grid[tx, ty - 1];
                    if (n.possibilities.Count > 0)
                    {
                        var res = n.Constrain(tPoss, SOUTH);
                        if (res == ConstrainResult.Conflict) { conflict = true; localConflict = new Vector2Int(n.x, n.y); break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
                // WEST
                if (tx - 1 >= 0)
                {
                    var n = grid[tx - 1, ty];
                    if (n.possibilities.Count > 0)
                    {
                        var res = n.Constrain(tPoss, WEST);
                        if (res == ConstrainResult.Conflict) { conflict = true; localConflict = new Vector2Int(n.x, n.y); break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
            }

            if (conflict)
            {
                conflictPos = localConflict;
                return false;
            }

            collapsedCount = CountCollapsed(grid);
        }

        // final validation
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (grid[x, y].possibilities.Count == 0)
                {
                    conflictPos = new Vector2Int(x, y);
                    return false;
                }

        finalGrid = grid;
        return true;
    }

    private void PaintConflict(Vector2Int globalGridPos)
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("PaintConflict: targetTilemap is null - cannot paint conflict.");
            isGenerating = false;
            currentAttempt = 0;
            return;
        }

        if (globalGridPos.x < 0 || globalGridPos.x >= width || globalGridPos.y < 0 || globalGridPos.y >= height)
        {
            Debug.LogWarning($"PaintConflict: position {globalGridPos} out of bounds.");
            isGenerating = false;
            currentAttempt = 0;
            return;
        }

        if (conflictTile == null)
        {
            Debug.LogWarning("PaintConflict: conflictTile not assigned - cannot paint conflict.");
            isGenerating = false;
            currentAttempt = 0;
            return;
        }

        Vector3Int tilemapPos = new Vector3Int(origin.x + globalGridPos.x, origin.y + globalGridPos.y, 0);
        targetTilemap.SetTile(tilemapPos, conflictTile);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        StopEditorAnimatedGeneration_Internal();
#endif

        isGenerating = false;
        currentAttempt = 0;

        Debug.Log($"WFC: Painted conflict tile at grid {globalGridPos} (tilemap pos {tilemapPos}). Generation stopped.");
    }

    #endregion

    #region Apply / Partial apply (tileTypes use field 'tile')

    private void ApplyToTilemap(WfcCell[,] grid)
    {
        if (targetTilemap == null || grid == null) return;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), null);

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var cell = grid[x, y];
                if (cell == null) continue;
                if (cell.possibilities.Count >= 1)
                {
                    var tileType = cell.possibilities[0];
                    if (tileType != null && tileType.tile != null)
                        targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), tileType.tile);
                }
            }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    private void ApplyPartialToTilemap(WfcCell[,] grid, bool[,] paintedFlags)
    {
        if (targetTilemap == null || grid == null || paintedFlags == null) return;

        int gx = grid.GetLength(0);
        int gy = grid.GetLength(1);
        if (paintedFlags.GetLength(0) != gx || paintedFlags.GetLength(1) != gy)
        {
            Debug.LogWarning("ApplyPartialToTilemap: paintedFlags size mismatch, aborting partial apply.");
            return;
        }

        for (int x = 0; x < gx; x++)
            for (int y = 0; y < gy; y++)
            {
                if (paintedFlags[x, y]) continue;
                var cell = grid[x, y];
                if (cell == null) continue;
                if (cell.IsCollapsed && cell.possibilities.Count >= 1)
                {
                    var t = cell.possibilities[0];
                    if (t != null && t.tile != null)
                        targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), t.tile);

                    paintedFlags[x, y] = true;
                }
            }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    #endregion

    #region Editor-only animated generation (EditorApplication.update)

#if UNITY_EDITOR
    private bool editorAnimating = false;
    private double editorLastBatchTime = 0.0;
    private WfcCell[,] editorGrid;
    private bool[,] editorPainted;
    private int editorCollapsedCount = 0;
    private int editorCurrentAttempt = 0;

    private void BeginEditorAttempt()
    {
        editorCurrentAttempt++;
        currentAttempt = editorCurrentAttempt;
        editorGrid = CreateEmptyGrid();
        editorPainted = new bool[width, height];
        editorCollapsedCount = 0;

        // clear area
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), null);

        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }

    private void EditorUpdateStep()
    {
        if (!editorAnimating) return;

        // batching timing
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now - editorLastBatchTime < editorBatchDelay) return;
        editorLastBatchTime = now;

        int processed = 0;
        int batch = Math.Max(1, editorStepsPerBatch);
        int totalCells = width * height;
        bool conflict = false;
        Vector2Int conflictPos = new Vector2Int(-1, -1);

        while (processed < batch && editorCollapsedCount < totalCells && !conflict)
        {
            var cell = PickCellWithLowestEntropy(editorGrid);
            if (cell == null) break;

            cell.CollapseWeighted(rng);
            if (cell.IsCollapsed) editorCollapsedCount++;

            Stack<WfcCell> stack = new Stack<WfcCell>();
            stack.Push(cell);

            while (stack.Count > 0 && !conflict)
            {
                var t = stack.Pop();
                var tPoss = t.possibilities;
                int tx = t.x;
                int ty = t.y;

                // NORTH
                if (ty + 1 < height)
                {
                    var n = editorGrid[tx, ty + 1];
                    if (n.possibilities.Count > 0)
                    {
                        var res = n.Constrain(tPoss, NORTH);
                        if (res == ConstrainResult.Conflict) { conflict = true; conflictPos = new Vector2Int(n.x, n.y); break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
                // EAST
                if (tx + 1 < width)
                {
                    var n = editorGrid[tx + 1, ty];
                    if (n.possibilities.Count > 0)
                    {
                        var res = n.Constrain(tPoss, EAST);
                        if (res == ConstrainResult.Conflict) { conflict = true; conflictPos = new Vector2Int(n.x, n.y); break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
                // SOUTH
                if (ty - 1 >= 0)
                {
                    var n = editorGrid[tx, ty - 1];
                    if (n.possibilities.Count > 0)
                    {
                        var res = n.Constrain(tPoss, SOUTH);
                        if (res == ConstrainResult.Conflict) { conflict = true; conflictPos = new Vector2Int(n.x, n.y); break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
                // WEST
                if (tx - 1 >= 0)
                {
                    var n = editorGrid[tx - 1, ty];
                    if (n.possibilities.Count > 0)
                    {
                        var res = n.Constrain(tPoss, WEST);
                        if (res == ConstrainResult.Conflict) { conflict = true; conflictPos = new Vector2Int(n.x, n.y); break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
            } // end propagation

            processed++;
        } // end processed loop

        // paint partial results
        ApplyPartialToTilemap(editorGrid, editorPainted);

        editorCollapsedCount = CountCollapsed(editorGrid);

        if (conflict)
        {
            if (retryOnConflict)
            {
                if (editorCurrentAttempt < maxRestarts)
                {
                    Debug.Log($"WFC: Editor attempt {editorCurrentAttempt} conflicted — restarting (attempt {editorCurrentAttempt + 1}/{maxRestarts})");
                    BeginEditorAttempt();
                    return;
                }
                else
                {
                    Debug.LogError($"WFC: Editor generation failed after {maxRestarts} attempts.");
                    StopEditorAnimatedGeneration_Internal();
                    return;
                }
            }
            else
            {
                if (conflictTile != null)
                {
                    PaintConflict(conflictPos);
                    Debug.LogError($"WFC: Editor generation conflict at {conflictPos}. Painted conflict tile and stopped (retryOnConflict=false).");
                }
                else
                {
                    Debug.LogError($"WFC: Editor generation conflict at {conflictPos}. No conflictTile assigned (retryOnConflict=false).");
                    StopEditorAnimatedGeneration_Internal();
                }
                return;
            }
        }

        // finished?
        if (editorCollapsedCount >= totalCells)
        {
            ApplyToTilemap(editorGrid);
            Debug.Log($"WFC: Editor animated generation succeeded on attempt {editorCurrentAttempt}");
            StopEditorAnimatedGeneration_Internal();
            return;
        }
    }

    private void StopEditorAnimatedGeneration_Internal()
    {
        if (!editorAnimating) return;
        editorAnimating = false;
        UnityEditor.EditorApplication.update -= EditorUpdateStep;

        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        isGenerating = false;
        currentAttempt = 0;
    }

    public void StopEditorAnimatedGeneration_Request()
    {
        StopEditorAnimatedGeneration();
    }
#endif
    #endregion
}
