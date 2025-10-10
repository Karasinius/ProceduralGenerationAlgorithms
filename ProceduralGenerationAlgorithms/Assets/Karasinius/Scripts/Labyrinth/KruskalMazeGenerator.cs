// KruskalMazeGenerator.cs
// Generates a perfect maze using randomized Kruskal's algorithm.
// Editor-only generation buttons + editor-mode animated build with batching.
// Requires SimpleRNG.cs in project.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class KruskalMazeGenerator : MonoBehaviour
{
    // Direction flags (like typical maze representations)
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
    public int mapWidth = 81;    // рекомендую нечЄтные размеры
    public int mapHeight = 51;
    public Vector2Int mapOrigin = new Vector2Int(0, 0);

    [Header("Random / Reproducibility")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Editor Animation (only editor)")]
    public int editorStepsPerBatch = 200;   // сколько ребЄр обрабатывать за батч
    public float editorBatchDelay = 0.03f;  // задержка между батчами (сек)

    [Header("Play-mode (optional)")]
    public bool animateInPlay = false;
    public int playStepsPerBatch = 200;
    public float playBatchDelay = 0.01f;

    // internal state
    private int cellCols;   // (mapWidth - 1) / 2
    private int cellRows;   // (mapHeight - 1) / 2

    private int[,] cellFlags; // cellCols x cellRows: store N/S/E/W bits per cell (for constructing passages)
    private bool[,] isFloor;  // mapWidth x mapHeight full map floor mask

    private SimpleRNG rng;

    // edges array used in animation
    private Edge[] edges;
    private int edgesCount;

    // editor animation state
#if UNITY_EDITOR
    private bool editorAnimating = false;
    private int editorEdgeIndex = 0;
    private double editorLastBatchTime = 0.0;
#endif

    // play-mode coroutine handle (if you use Play-mode animation)
    private Coroutine playCoroutine = null;

    // Edge struct
    private struct Edge
    {
        public int cx;
        public int cy;
        public int dir;
        public Edge(int cx, int cy, int dir)
        {
            this.cx = cx; this.cy = cy; this.dir = dir;
        }
    }

    // Disjoint set (union-find)
    private int[] dsuParent;
    private int[] dsuRank;

    private void Start()
    {
        // intentionally empty: generation runs only from editor buttons (or manual StartCoroutine in Play)
    }

    #region Public entry points

    [ContextMenu("Generate Kruskal (Editor Sync)")]
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

    // Synchronous generation (editor)
    public void GenerateSync()
    {
        if (!ValidateSetup()) return;
        PrepareRandom();
        PrepareGridStructures();
        BuildEdgesList();
        ShuffleEdges();
        RunKruskalSync();
        PaintTilemap();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    // Play-mode coroutine (optional)
    public System.Collections.IEnumerator GenerateRoutine()
    {
        if (!ValidateSetup()) yield break;
        PrepareRandom();
        PrepareGridStructures();
        BuildEdgesList();
        ShuffleEdges();

        if (!animateInPlay)
        {
            RunKruskalSync();
            PaintTilemap();
            yield break;
        }

        // animated in play: process edges in batches
        PaintAllWalls();
        InitializeDSU();

        int idx = 0;
        while (idx < edgesCount)
        {
            int batch = Math.Min(playStepsPerBatch, edgesCount - idx);
            for (int b = 0; b < batch; b++)
            {
                var e = edges[idx++];
                int aIndex = CellIndex(e.cx, e.cy);
                int nx = e.cx + DX[e.dir];
                int ny = e.cy + DY[e.dir];
                int bIndex = CellIndex(nx, ny);

                if (!Connected(aIndex, bIndex))
                {
                    Union(aIndex, bIndex);
                    CarveEdge(e);
                }
            }

            PaintTilemap();
            yield return new WaitForSeconds(playBatchDelay);
        }

        PaintTilemap();
    }

    #endregion

    #region Setup & helpers

    private bool ValidateSetup()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[KruskalMazeGenerator] targetTilemap is null. Assign a Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[KruskalMazeGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[KruskalMazeGenerator] mapWidth is even Ч decreasing by 1 to make it odd.");
            mapWidth = Mathf.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[KruskalMazeGenerator] mapHeight is even Ч decreasing by 1 to make it odd.");
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

    private void PrepareGridStructures()
    {
        cellFlags = new int[cellCols, cellRows];
        isFloor = new bool[mapWidth, mapHeight];
        // initially nothing carved; we will carve when joining sets
    }

    // Build a list of all possible internal edges (edges connecting adjacent cells).
    private void BuildEdgesList()
    {
        // maximum edges roughly = (cellCols*(cellRows-1) + (cellCols-1)*cellRows)
        List<Edge> tmp = new List<Edge>();
        for (int y = 0; y < cellRows; y++)
        {
            for (int x = 0; x < cellCols; x++)
            {
                if (y > 0) tmp.Add(new Edge(x, y, N));
                if (x > 0) tmp.Add(new Edge(x, y, W));
            }
        }
        edges = tmp.ToArray();
        edgesCount = edges.Length;
    }

    // Fisher-Yates shuffle using SimpleRNG
    private void ShuffleEdges()
    {
        for (int i = edgesCount - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1); // [0..i]
            // swap i and j
            Edge t = edges[i];
            edges[i] = edges[j];
            edges[j] = t;
        }
    }

    // Initialize DSU arrays
    private void InitializeDSU()
    {
        int n = cellCols * cellRows;
        dsuParent = new int[n];
        dsuRank = new int[n];
        for (int i = 0; i < n; i++)
        {
            dsuParent[i] = i;
            dsuRank[i] = 0;
        }
    }

    private int CellIndex(int cx, int cy)
    {
        return cx + cy * cellCols;
    }

    private int FindRoot(int i)
    {
        int p = dsuParent[i];
        if (p == i) return i;
        // path compression
        dsuParent[i] = FindRoot(p);
        return dsuParent[i];
    }

    private bool Connected(int a, int b)
    {
        if (a < 0 || b < 0 || a >= dsuParent.Length || b >= dsuParent.Length) return false;
        return FindRoot(a) == FindRoot(b);
    }

    private void Union(int a, int b)
    {
        int ra = FindRoot(a), rb = FindRoot(b);
        if (ra == rb) return;
        // union by rank
        if (dsuRank[ra] < dsuRank[rb])
            dsuParent[ra] = rb;
        else if (dsuRank[rb] < dsuRank[ra])
            dsuParent[rb] = ra;
        else
        {
            dsuParent[rb] = ra;
            dsuRank[ra]++;
        }
    }

    // carve edge into cellFlags + isFloor
    private void CarveEdge(Edge e)
    {
        // set both cells and the connecting wall between them
        int ax = e.cx * 2 + 1;
        int ay = e.cy * 2 + 1;
        int nx = e.cx + DX[e.dir];
        int ny = e.cy + DY[e.dir];
        int bx = nx * 2 + 1;
        int by = ny * 2 + 1;

        // mark cell centers
        SetFloorAtMapLocal(ax, ay);
        SetFloorAtMapLocal(bx, by);

        // mark passage between centers
        int mx = (ax + bx) / 2;
        int my = (ay + by) / 2;
        SetFloorAtMapLocal(mx, my);

        // also update logical cell flags (if needed later)
        cellFlags[e.cx, e.cy] |= e.dir;
        if (nx >= 0 && ny >= 0 && nx < cellCols && ny < cellRows)
            cellFlags[nx, ny] |= OPPOSITE[e.dir];
    }

    private void SetFloorAtMapLocal(int localX, int localY)
    {
        if (localX < 0 || localY < 0 || localX >= mapWidth || localY >= mapHeight) return;
        isFloor[localX, localY] = true;
    }

    #endregion

    #region Synchronous Kruskal run

    private void RunKruskalSync()
    {
        // clear arrays
        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                isFloor[x, y] = false;

        InitializeDSU();

        for (int i = 0; i < edgesCount; i++)
        {
            Edge e = edges[i];
            int aIndex = CellIndex(e.cx, e.cy);
            int nx = e.cx + DX[e.dir];
            int ny = e.cy + DY[e.dir];
            if (nx < 0 || ny < 0 || nx >= cellCols || ny >= cellRows) continue;
            int bIndex = CellIndex(nx, ny);

            if (!Connected(aIndex, bIndex))
            {
                Union(aIndex, bIndex);
                CarveEdge(e);
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
    // -------------------
    // Editor-mode animated generation
    // -------------------

    public void StartEditorAnimatedGeneration()
    {
        if (!ValidateSetup()) return;
        if (editorAnimating)
        {
            Debug.Log("[KruskalMazeGenerator] Editor animation already running.");
            return;
        }

        PrepareRandom();
        PrepareGridStructures();
        BuildEdgesList();
        ShuffleEdges();

        InitializeDSU();

        // paint walls initially
        PaintAllWalls();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        editorEdgeIndex = 0;
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
        while (processed < editorStepsPerBatch && editorEdgeIndex < edgesCount)
        {
            Edge e = edges[editorEdgeIndex++];
            int aIndex = CellIndex(e.cx, e.cy);
            int nx = e.cx + DX[e.dir];
            int ny = e.cy + DY[e.dir];
            if (nx < 0 || ny < 0 || nx >= cellCols || ny >= cellRows) { processed++; continue; }
            int bIndex = CellIndex(nx, ny);

            if (!Connected(aIndex, bIndex))
            {
                Union(aIndex, bIndex);
                CarveEdge(e);
            }
            processed++;
        }

        // Update visual after batch
        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        // finish if done
        if (editorEdgeIndex >= edgesCount)
        {
            StopEditorAnimatedGeneration();
        }
    }

    // Custom inspector buttons
    [UnityEditor.CustomEditor(typeof(KruskalMazeGenerator))]
    private class KruskalEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            KruskalMazeGenerator script = (KruskalMazeGenerator)target;

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
