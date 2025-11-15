// WfcGenerator.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using static UnityEngine.GraphicsBuffer;

[DisallowMultipleComponent]
public class WfcGenerator : MonoBehaviour
{
    [Header("Grid")]
    public int width = 60;
    public int height = 34;
    public Vector2Int origin = Vector2Int.zero; // в случае, если хочешь смещать отрисовку на Tilemap

    [Header("Tilemap target")]
    public Tilemap targetTilemap;

    [Header("Tile types (assign all WfcTileType assets here)")]
    public List<WfcTileType> tileTypes = new List<WfcTileType>();

    [Header("Generation options")]
    [Tooltip("Макс. количество попыток (restart-on-conflict)")]
    public int maxRestarts = 100;
    [Tooltip("Использовать Шенноновую энтропию при выборе клетки")]
    public bool useShannonEntropy = true;
    [Tooltip("Если -1 — используется случайный seed")]
    public int seed = -1;
    [Tooltip("Использовать случайный сид (перезаписывает seed)")]
    public bool useRandomSeed = true;

    [Header("Async / Play-mode animation settings")]
    [Tooltip("После какого числа коллапсов делать частичную отрисовку")]
    public int batchSize = 8;
    [Tooltip("Задержка между батчами (секунды) в Play mode")]
    public float stepDelay = 0.03f;

    [Header("Editor animation settings (Edit mode)")]
    [Tooltip("Сколько шагов выполнять за один editor-батч")]
    public int editorStepsPerBatch = 64;
    [Tooltip("Задержка (секунды) между editor-батчами")]
    public float editorBatchDelay = 0.03f;

    [Header("Runtime state (read-only)")]
    public bool isGenerating = false;
    public bool isAsync = false;
    public int currentAttempt = 0;

    // internal RNG and coroutine
    private System.Random rng;
    private Coroutine runningCoroutine = null;
    private bool cancelRequested = false;

    // direction constants same as Python (0=N,1=E,2=S,3=W)
    private const int NORTH = 0;
    private const int EAST = 1;
    private const int SOUTH = 2;
    private const int WEST = 3;

    // --- PUBLIC API (callable from Inspector / Editor buttons) ---

    [ContextMenu("Generate (Sync)")]
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
        isAsync = false;
        cancelRequested = false;

        rng = CreateRng();

        bool success = false;
        for (int attempt = 0; attempt < maxRestarts; attempt++)
        {
            currentAttempt = attempt + 1;
            if (TryGenerateOnce(out var finalGrid))
            {
                ApplyToTilemap(finalGrid);
                Debug.Log($"WFC: Sync generation succeeded on attempt {attempt + 1}");
                success = true;
                break;
            }
        }

        if (!success)
        {
            Debug.LogError($"WFC: Sync generation failed after {maxRestarts} attempts.");
        }

        isGenerating = false;
        currentAttempt = 0;
    }

    /// <summary>
    /// Запуск анимированной генерации в Play mode (корутина).
    /// Если вы вызываете в Edit mode, используйте StartEditorAnimatedGeneration (кнопка в инспекторе).
    /// </summary>
    public void GenerateAsync()
    {
        if (!ValidateSetup()) return;
        if (isGenerating)
        {
            Debug.LogWarning("WFC: Generation already running.");
            return;
        }

        PrepareForNewRun();

        isGenerating = true;
        isAsync = true;
        cancelRequested = false;

        rng = CreateRng();

        runningCoroutine = StartCoroutine(GenerateAsyncCoroutine());
    }

    /// <summary>
    /// Запрос отмены текущей асинхронной генерации (корутина) или остановка editor-anim.
    /// </summary>
    public void CancelGeneration()
    {
        if (!isGenerating)
        {
            Debug.Log("WFC: Nothing to cancel.");
            return;
        }

        cancelRequested = true;

        // stop play-mode coroutine if running
        if (runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
            runningCoroutine = null;
        }

#if UNITY_EDITOR
        // stop editor animation if any
        StopEditorAnimatedGeneration_Internal();
#endif

        isGenerating = false;
        isAsync = false;
        currentAttempt = 0;
        Debug.Log("WFC: Generation cancelled.");
    }

    public void ClearTiles()
    {
        if (isGenerating)
        {
            Debug.LogWarning("WFC: Can't clear while generating. Cancel first.");
            return;
        }

        if (targetTilemap != null)
        {
            // Clear area where generator would draw to be safe
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), null);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(targetTilemap);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
        }
    }

    // --- Internal helpers ---

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

    private void PrepareForNewRun()
    {
        // cancel any running process
        cancelRequested = false;
        if (runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
            runningCoroutine = null;
        }
#if UNITY_EDITOR
        StopEditorAnimatedGeneration_Internal();
#endif
        // clear area
        if (targetTilemap != null)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), null);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(targetTilemap);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
        }
    }

    private System.Random CreateRng()
    {
        if (!useRandomSeed && seed >= 0)
            return new System.Random(seed);
        return new System.Random(Guid.NewGuid().GetHashCode());
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
                if (cell.possibilities.Count == 0) return cell; // conflict cell

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

                double noise = (rng != null) ? rng.NextDouble() * 1e-6 : UnityEngine.Random.value * 1e-6;
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

    private bool TryGenerateOnce(out WfcCell[,] finalGrid)
    {
        finalGrid = null;
        WfcCell[,] grid = CreateEmptyGrid();

        int cellsTotal = width * height;
        int collapsedCount = 0;

        while (collapsedCount < cellsTotal)
        {
            var cell = PickCellWithLowestEntropy(grid);
            if (cell == null) break;

            cell.CollapseWeighted(rng);
            if (cell.IsCollapsed) collapsedCount++;

            Stack<WfcCell> stack = new Stack<WfcCell>();
            stack.Push(cell);

            bool conflict = false;
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
                        if (res == ConstrainResult.Conflict) { conflict = true; break; }
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
                        if (res == ConstrainResult.Conflict) { conflict = true; break; }
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
                        if (res == ConstrainResult.Conflict) { conflict = true; break; }
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
                        if (res == ConstrainResult.Conflict) { conflict = true; break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
            }

            if (conflict) return false;
            collapsedCount = CountCollapsed(grid);
        }

        // final validation
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (grid[x, y].possibilities.Count == 0) return false;

        finalGrid = grid;
        return true;
    }

    // --- Apply / Partial Apply (использует только tileType.tile) ---

    private void ApplyToTilemap(WfcCell[,] grid)
    {
        if (targetTilemap == null || grid == null) return;

        // clear area first
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), null);

        for (int x = 0; x < width; x++)
        {
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
        {
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
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    // --- Play-mode async coroutine (animated generation in Play mode) ---

    private IEnumerator GenerateAsyncCoroutine()
    {
        bool success = false;

        for (int attempt = 0; attempt < maxRestarts; attempt++)
        {
            if (cancelRequested) break;
            currentAttempt = attempt + 1;

            WfcCell[,] grid = CreateEmptyGrid();
            bool[,] painted = new bool[width, height];
            int cellsTotal = width * height;
            int collapsedCount = 0;
            int collapsedSinceLastPaint = 0;

            while (collapsedCount < cellsTotal)
            {
                if (cancelRequested) break;

                var cell = PickCellWithLowestEntropy(grid);
                if (cell == null) break;

                cell.CollapseWeighted(rng);
                if (cell.IsCollapsed)
                {
                    collapsedCount++;
                    collapsedSinceLastPaint++;
                }

                Stack<WfcCell> stack = new Stack<WfcCell>();
                stack.Push(cell);

                bool conflict = false;
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
                            if (res == ConstrainResult.Conflict) { conflict = true; break; }
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
                            if (res == ConstrainResult.Conflict) { conflict = true; break; }
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
                            if (res == ConstrainResult.Conflict) { conflict = true; break; }
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
                            if (res == ConstrainResult.Conflict) { conflict = true; break; }
                            if (res == ConstrainResult.Reduced) stack.Push(n);
                        }
                    }
                } // end propagation

                if (conflict) break;

                if (collapsedSinceLastPaint >= Math.Max(1, batchSize))
                {
                    ApplyPartialToTilemap(grid, painted);
                    collapsedSinceLastPaint = 0;

                    // delay
                    float elapsed = 0f;
                    while (elapsed < stepDelay)
                    {
                        if (cancelRequested) break;
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                }

                collapsedCount = CountCollapsed(grid);
            } // end while collapsed

            if (cancelRequested) break;

            bool hasEmpty = false;
            for (int x = 0; x < width && !hasEmpty; x++)
                for (int y = 0; y < height; y++)
                    if (grid[x, y].possibilities.Count == 0) { hasEmpty = true; break; }

            if (!hasEmpty && CountCollapsed(grid) == cellsTotal)
            {
                ApplyToTilemap(grid);
                Debug.Log($"WFC: Async generation succeeded on attempt {attempt + 1}");
                success = true;
                break;
            }
            else
            {
                // restart attempt: clear and continue
                if (!cancelRequested)
                {
                    for (int x = 0; x < width; x++)
                        for (int y = 0; y < height; y++)
                            targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), null);
#if UNITY_EDITOR
                    UnityEditor.EditorUtility.SetDirty(targetTilemap);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
                    yield return null;
                }
            }
        } // end attempts

        if (!success && !cancelRequested)
        {
            Debug.LogError($"WFC: Async generation failed after {maxRestarts} attempts.");
        }

        isGenerating = false;
        isAsync = false;
        currentAttempt = 0;
        runningCoroutine = null;
    }

    // --- Editor-mode animated generation (uses EditorApplication.update) ---
#if UNITY_EDITOR
    private bool editorAnimating = false;
    private double editorLastBatchTime = 0.0;
    private WfcCell[,] editorGrid;
    private bool[,] editorPainted;
    private int editorCollapsedCount = 0;
    private int editorCurrentAttempt = 0;

    public void StartEditorAnimatedGeneration()
    {
        if (!ValidateSetup()) return;
        if (editorAnimating)
        {
            Debug.Log("[WFC] Editor animation already running.");
            return;
        }

        PrepareForNewRun();
        isGenerating = true;
        isAsync = true;
        cancelRequested = false;
        rng = CreateRng();

        editorCurrentAttempt = 0;
        BeginEditorAttempt();
        editorLastBatchTime = UnityEditor.EditorApplication.timeSinceStartup;
        editorAnimating = true;
        UnityEditor.EditorApplication.update += EditorUpdateStep;
    }

    private void BeginEditorAttempt()
    {
        editorCurrentAttempt++;
        currentAttempt = editorCurrentAttempt;
        // create new grid for attempt
        editorGrid = CreateEmptyGrid();
        editorPainted = new bool[width, height];
        editorCollapsedCount = 0;

        // ensure area cleared
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                targetTilemap.SetTile(new Vector3Int(origin.x + x, origin.y + y, 0), null);

        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }

    private void EditorUpdateStep()
    {
        if (!editorAnimating) return;
        if (cancelRequested)
        {
            StopEditorAnimatedGeneration_Internal();
            return;
        }

        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now - editorLastBatchTime < editorBatchDelay) return;
        editorLastBatchTime = now;

        int processed = 0;
        int batch = Math.Max(1, editorStepsPerBatch);
        int cellsTotal = width * height;

        bool conflict = false;

        while (processed < batch && editorCollapsedCount < cellsTotal && !conflict)
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
                        if (res == ConstrainResult.Conflict) { conflict = true; break; }
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
                        if (res == ConstrainResult.Conflict) { conflict = true; break; }
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
                        if (res == ConstrainResult.Conflict) { conflict = true; break; }
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
                        if (res == ConstrainResult.Conflict) { conflict = true; break; }
                        if (res == ConstrainResult.Reduced) stack.Push(n);
                    }
                }
            } // end propagation

            processed++;
        } // end while processed

        // after batch — paint partial
        ApplyPartialToTilemap(editorGrid, editorPainted);

        if (conflict)
        {
            // restart attempt if attempts remain
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

        // finished?
        if (editorCollapsedCount >= cellsTotal)
        {
            // final paint (ensure any remaining)
            ApplyToTilemap(editorGrid);
            Debug.Log($"WFC: Editor animated generation succeeded on attempt {editorCurrentAttempt}");
            StopEditorAnimatedGeneration_Internal();
            return;
        }

        // continue next update
    }

    private void StopEditorAnimatedGeneration_Internal()
    {
        if (!editorAnimating) return;
        editorAnimating = false;
        UnityEditor.EditorApplication.update -= EditorUpdateStep;
        // ensure final dirty mark
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        isGenerating = false;
        isAsync = false;
        currentAttempt = 0;
    }

    public void StopEditorAnimatedGeneration()
    {
        if (!editorAnimating) return;
        StopEditorAnimatedGeneration_Internal();
        Debug.Log("WFC: Editor animation stopped by user.");
    }
#endif
    // end #if UNITY_EDITOR
}

// Custom inspector with buttons in the same file (wrapped for editor)
#if UNITY_EDITOR

[CustomEditor(typeof(WfcGenerator))]
public class WfcGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        WfcGenerator gen = (WfcGenerator)target;

        GUILayout.Space(8);
        GUILayout.Label("WFC Controls", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate (Editor Sync)"))
        {
            if (!EditorApplication.isPlaying)
            {
                gen.GenerateSync();
            }
            else
            {
                if (EditorUtility.DisplayDialog("Generate in Play Mode?",
                    "You are in Play mode. Sync generation will run in Play mode. Continue?",
                    "Yes", "No"))
                {
                    gen.GenerateSync();
                }
            }
        }

        if (GUILayout.Button("Generate Animated (Editor)"))
        {
            if (!EditorApplication.isPlaying)
            {
                gen.StartEditorAnimatedGeneration();
            }
            else
            {
                if (EditorUtility.DisplayDialog("Animated in Play Mode?",
                    "You are in Play mode. For animated generation run 'Generate Async' in play mode (use Inspector or call method). Do you want to run Play-mode async now?",
                    "Yes (Play Async)", "No"))
                {
                    gen.GenerateAsync();
                }
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Stop Animated"))
        {
            gen.CancelGeneration();
        }
        if (GUILayout.Button("Clear Area"))
        {
            gen.ClearTiles();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUI.enabled = false;
        EditorGUILayout.Toggle("Is Generating", gen.isGenerating);
        EditorGUILayout.Toggle("Is Async", gen.isAsync);
        EditorGUILayout.IntField("Current Attempt", gen.currentAttempt);
        GUI.enabled = true;
    }
}
#endif

