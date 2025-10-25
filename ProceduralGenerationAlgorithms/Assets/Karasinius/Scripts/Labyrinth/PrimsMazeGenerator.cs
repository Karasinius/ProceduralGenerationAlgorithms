using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class PrimsMazeGenerator : MonoBehaviour
{
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

    //[Header("Play-mode")]
    [HideInInspector] public bool animateInPlay = false;
    [HideInInspector] public int playStepsPerBatch = 200;
    [HideInInspector] public float playBatchDelay = 0.01f;

    private int cellCols; // = (mapWidth - 1) / 2
    private int cellRows; // = (mapHeight - 1) / 2

    private bool[,] isFloor; 

    private bool[,] visited;     
    private bool[,] inFrontier;  
    private List<Vector2Int> frontierList;

    private Xoshiro256StarStar rng;

#if UNITY_EDITOR
    private bool editorAnimating = false;
    private double editorLastBatchTime = 0.0;
#endif

    private Coroutine playCoroutine = null;

    private void Start()
    {
    }

    #region Public entry points

    [ContextMenu("Generate Prim (Editor Sync)")]
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
        PrepareArrays();
        RunPrimSync();
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
        PrepareArrays();

        if (!animateInPlay)
        {
            RunPrimSync();
            PaintTilemap();
            yield break;
        }

        PaintAllWalls();

        int sx = rng.Next(cellCols);
        int sy = rng.Next(cellRows);
        MarkCell(sx, sy);

        while (frontierList.Count > 0)
        {
            int batch = Math.Min(playStepsPerBatch, frontierList.Count);
            for (int b = 0; b < batch; b++)
            {
                int idx = rng.Next(frontierList.Count);
                Vector2Int f = frontierList[idx];
                int last = frontierList.Count - 1;
                frontierList[idx] = frontierList[last];
                frontierList.RemoveAt(last);
                inFrontier[f.x, f.y] = false;

                var ins = GetInNeighbors(f.x, f.y);
                if (ins.Count == 0) continue; 
                Vector2Int n = ins[rng.Next(ins.Count)];

                CarveBetweenCells(f.x, f.y, n.x, n.y);
                MarkCell(f.x, f.y);
            }

            PaintTilemap();
            yield return new WaitForSeconds(playBatchDelay);
        }

        PaintTilemap();
    }

    #endregion

    #region Setup & utils

    private bool ValidateSetup()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[PrimsMazeGenerator] targetTilemap is null. Assign Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[PrimsMazeGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[PrimsMazeGenerator] mapWidth is even — decreasing by 1 to make it odd for cell mapping.");
            mapWidth = Mathf.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[PrimsMazeGenerator] mapHeight is even — decreasing by 1 to make it odd for cell mapping.");
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
        rng = new Xoshiro256StarStar(seed);
    }

    private void PrepareArrays()
    {
        isFloor = new bool[mapWidth, mapHeight];
        visited = new bool[cellCols, cellRows];
        inFrontier = new bool[cellCols, cellRows];
        frontierList = new List<Vector2Int>();

        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                isFloor[x, y] = false;
    }

    #endregion

    #region Core Prim algorithm (sync)

    private void RunPrimSync()
    {
        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                isFloor[x, y] = false;
        for (int cx = 0; cx < cellCols; cx++)
            for (int cy = 0; cy < cellRows; cy++)
            {
                visited[cx, cy] = false;
                inFrontier[cx, cy] = false;
            }
        frontierList.Clear();

        int sx = rng.Next(cellCols);
        int sy = rng.Next(cellRows);
        MarkCell(sx, sy);

        while (frontierList.Count > 0)
        {
            int idx = rng.Next(frontierList.Count);
            Vector2Int f = frontierList[idx];
            int last = frontierList.Count - 1;
            frontierList[idx] = frontierList[last];
            frontierList.RemoveAt(last);
            inFrontier[f.x, f.y] = false;

            var ins = GetInNeighbors(f.x, f.y);
            if (ins.Count == 0) continue; 
            Vector2Int n = ins[rng.Next(ins.Count)];

            CarveBetweenCells(f.x, f.y, n.x, n.y);
            MarkCell(f.x, f.y);
        }
    }

    #endregion

    #region Frontier / carving helpers

    private void MarkCell(int cx, int cy)
    {
        if (cx < 0 || cy < 0 || cx >= cellCols || cy >= cellRows) return;
        if (visited[cx, cy]) return;

        visited[cx, cy] = true;
        int mx = cx * 2 + 1;
        int my = cy * 2 + 1;
        SetFloorAtMapLocal(mx, my);

        TryAddFrontier(cx - 1, cy);
        TryAddFrontier(cx + 1, cy);
        TryAddFrontier(cx, cy - 1);
        TryAddFrontier(cx, cy + 1);
    }

    private void TryAddFrontier(int cx, int cy)
    {
        if (cx < 0 || cy < 0 || cx >= cellCols || cy >= cellRows) return;
        if (visited[cx, cy]) return;
        if (inFrontier[cx, cy]) return;
        inFrontier[cx, cy] = true;
        frontierList.Add(new Vector2Int(cx, cy));
    }

    private List<Vector2Int> GetInNeighbors(int cx, int cy)
    {
        var res = new List<Vector2Int>(4);
        if (cx > 0 && visited[cx - 1, cy]) res.Add(new Vector2Int(cx - 1, cy));
        if (cx + 1 < cellCols && visited[cx + 1, cy]) res.Add(new Vector2Int(cx + 1, cy));
        if (cy > 0 && visited[cx, cy - 1]) res.Add(new Vector2Int(cx, cy - 1));
        if (cy + 1 < cellRows && visited[cx, cy + 1]) res.Add(new Vector2Int(cx, cy + 1));
        return res;
    }
    private void CarveBetweenCells(int cx1, int cy1, int cx2, int cy2)
    {
        int ax = cx1 * 2 + 1;
        int ay = cy1 * 2 + 1;
        int bx = cx2 * 2 + 1;
        int by = cy2 * 2 + 1;

        SetFloorAtMapLocal(ax, ay);
        SetFloorAtMapLocal(bx, by);

        int mx = (ax + bx) / 2;
        int my = (ay + by) / 2;
        SetFloorAtMapLocal(mx, my);
    }

    private void SetFloorAtMapLocal(int localX, int localY)
    {
        if (localX < 0 || localY < 0 || localX >= mapWidth || localY >= mapHeight) return;
        isFloor[localX, localY] = true;
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

    #endregion

#if UNITY_EDITOR

    public void StartEditorAnimatedGeneration()
    {
        if (!ValidateSetup()) return;
        if (editorAnimating)
        {
            Debug.Log("[PrimsMazeGenerator] Editor animation already running.");
            return;
        }

        PrepareRandom();
        PrepareArrays();

        PaintAllWalls();

        int sx = rng.Next(cellCols);
        int sy = rng.Next(cellRows);
        MarkCell(sx, sy);

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

        while (processed < batch && frontierList.Count > 0)
        {
            int idx = rng.Next(frontierList.Count);
            Vector2Int f = frontierList[idx];
            int last = frontierList.Count - 1;
            frontierList[idx] = frontierList[last];
            frontierList.RemoveAt(last);
            inFrontier[f.x, f.y] = false;

            var ins = GetInNeighbors(f.x, f.y);
            if (ins.Count > 0)
            {
                Vector2Int n = ins[rng.Next(ins.Count)];
                CarveBetweenCells(f.x, f.y, n.x, n.y);
                MarkCell(f.x, f.y);
            }

            processed++;
        }

        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        if (frontierList.Count == 0)
        {
            StopEditorAnimatedGeneration();
        }
    }

    [UnityEditor.CustomEditor(typeof(PrimsMazeGenerator))]
    private class PrimsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PrimsMazeGenerator script = (PrimsMazeGenerator)target;

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

