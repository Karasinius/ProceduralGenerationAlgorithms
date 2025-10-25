using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class KruskalMazeGenerator : MonoBehaviour
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

    [Header("Random / Reproducibility")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Header("Editor Animation (only editor)")]
    public int editorStepsPerBatch = 200;   
    public float editorBatchDelay = 0.03f;

    //[Header("Play-mode (optional)")]
    [HideInInspector] public bool animateInPlay = false;
    [HideInInspector] public int playStepsPerBatch = 200;
    [HideInInspector] public float playBatchDelay = 0.01f;

    private int cellCols;   // (mapWidth - 1) / 2
    private int cellRows;   // (mapHeight - 1) / 2

    private int[,] cellFlags; 
    private bool[,] isFloor;  

    private Mulberry32Random rng;

    private Edge[] edges;
    private int edgesCount;

#if UNITY_EDITOR
    private bool editorAnimating = false;
    private int editorEdgeIndex = 0;
    private double editorLastBatchTime = 0.0;
#endif

    private Coroutine playCoroutine = null;

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

    private int[] dsuParent;
    private int[] dsuRank;

    private void Start()
    {
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
            Debug.Log("[KruskalMazeGenerator] mapWidth is even — decreasing by 1 to make it odd.");
            mapWidth = Mathf.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[KruskalMazeGenerator] mapHeight is even — decreasing by 1 to make it odd.");
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
        rng = new Mulberry32Random(seed);
    }

    private void PrepareGridStructures()
    {
        cellFlags = new int[cellCols, cellRows];
        isFloor = new bool[mapWidth, mapHeight];
    }

    private void BuildEdgesList()
    {
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

    private void ShuffleEdges()
    {
        for (int i = edgesCount - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1); 
            Edge t = edges[i];
            edges[i] = edges[j];
            edges[j] = t;
        }
    }

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

    private void CarveEdge(Edge e)
    {
        int ax = e.cx * 2 + 1;
        int ay = e.cy * 2 + 1;
        int nx = e.cx + DX[e.dir];
        int ny = e.cy + DY[e.dir];
        int bx = nx * 2 + 1;
        int by = ny * 2 + 1;

        SetFloorAtMapLocal(ax, ay);
        SetFloorAtMapLocal(bx, by);

        int mx = (ax + bx) / 2;
        int my = (ay + by) / 2;
        SetFloorAtMapLocal(mx, my);

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

        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        if (editorEdgeIndex >= edgesCount)
        {
            StopEditorAnimatedGeneration();
        }
    }

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
            //if (GUILayout.Button("Generate (Play Mode)"))
            //{
            //    if (Application.isPlaying)
            //    {
            //        if (script.playCoroutine != null) script.StopCoroutine(script.playCoroutine);
            //        script.playCoroutine = script.StartCoroutine(script.GenerateRoutine());
            //    }
            //    else
            //    {
            //        Debug.LogWarning("Enter Play mode to run Play-mode coroutine.");
            //    }
            //}
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
