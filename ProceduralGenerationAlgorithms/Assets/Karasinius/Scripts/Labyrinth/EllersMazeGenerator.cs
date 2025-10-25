using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class EllersMazeGenerator : MonoBehaviour
{
    [Header("Map / Tiles")]
    public Tilemap targetTilemap;
    public TileBase wallTile;
    public TileBase floorTile;
    public int mapWidth = 81;    
    public int mapHeight = 51;   
    public Vector2Int mapOrigin = new Vector2Int(0, 0);

    [Header("Random / Reproducibility")]
    public bool useRandomSeed = true;
    public int seed = 12345;

    [Tooltip("Chance of joining cells (0..1).")]
    [Range(0f, 1f)]
    public float horizontalJoinChance = 0.57f;

    [Header("Editor Animation (only editor)")]
    [Tooltip("How many rows to process per editor batch")]
    public int editorRowsPerBatch = 1;
    [Tooltip("Delay (seconds) between editor batches")]
    public float editorBatchDelay = 0.05f;

    private int cellCols; 
    private int cellRows; 

    private bool[,] isFloor; 

    private int[] currentRowSets; 
    private int[] nextRowSets;   
    private int nextSetId;

    private SimpleRNG rng;

#if UNITY_EDITOR
    private bool editorAnimating = false;
    private int editorRowIndex = 0; 
    private double editorLastBatchTime = 0.0;
#endif

    private void Start()
    {
    }

    #region Public editor entry points

    [ContextMenu("Generate Eller (Editor Sync)")]
    public void GenerateContext()
    {
        GenerateSync();
    }

    public void GenerateSync()
    {
        if (!ValidateSetup()) return;
        PrepareRandom();
        PrepareMapArrays();
        InitializeEller();
        for (int r = 0; r < cellRows; r++)
        {
            ProcessRow(r, r == cellRows - 1);
        }
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
            Debug.Log("[EllersMazeGenerator] Editor animation already running.");
            return;
        }

        PrepareRandom();
        PrepareMapArrays();
        InitializeEller();

        PaintAllWalls();

        editorRowIndex = 0;
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
        int batch = Math.Max(1, editorRowsPerBatch);

        while (processed < batch && editorRowIndex < cellRows)
        {
            bool isLast = (editorRowIndex == cellRows - 1);
            ProcessRow(editorRowIndex, isLast);
            editorRowIndex++;
            processed++;
        }

        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        if (editorRowIndex >= cellRows)
        {
            StopEditorAnimatedGeneration();
        }
    }
#endif

    #endregion

    #region Setup & validation

    private bool ValidateSetup()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[EllersMazeGenerator] targetTilemap is null. Assign a Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[EllersMazeGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[EllersMazeGenerator] mapWidth is even — decreasing by 1 to make it odd for cell mapping.");
            mapWidth = Math.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[EllersMazeGenerator] mapHeight is even — decreasing by 1 to make it odd for cell mapping.");
            mapHeight = Math.Max(3, mapHeight - 1);
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
        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                isFloor[x, y] = false;
    }

    private void InitializeEller()
    {
        currentRowSets = new int[cellCols];
        nextRowSets = new int[cellCols];
        nextSetId = 1;

        for (int c = 0; c < cellCols; c++)
        {
            currentRowSets[c] = nextSetId++;
        }
    }

    #endregion

    #region Core Eller's algorithm (row processing)
    private void ProcessRow(int rowIndex, bool isLast)
    {
        for (int cx = 0; cx < cellCols; cx++)
        {
            CarveCellCenter(cx, rowIndex);
        }

        for (int cx = 0; cx < cellCols - 1; cx++)
        {
            int setA = currentRowSets[cx];
            int setB = currentRowSets[cx + 1];

            if (setA == setB)
            {
                continue;
            }

            bool join;
            if (isLast)
            {
                join = true;
            }
            else
            {
                join = rng.NextDouble() < horizontalJoinChance;
            }

            if (join)
            {
                MergeSetsInArray(currentRowSets, setB, setA);
                CarveEast(cx, rowIndex);
            }
        }

        if (isLast)
        {
            return;
        }

        Dictionary<int, List<int>> setsMap = new Dictionary<int, List<int>>();
        for (int cx = 0; cx < cellCols; cx++)
        {
            int sid = currentRowSets[cx];
            if (!setsMap.TryGetValue(sid, out var list))
            {
                list = new List<int>();
                setsMap[sid] = list;
            }
            list.Add(cx);
        }

        for (int i = 0; i < cellCols; i++) nextRowSets[i] = 0;

        foreach (var kv in setsMap)
        {
            List<int> indices = kv.Value;
            Shuffle(indices, rng);

            int chooseCount = 1;
            if (indices.Count > 1) chooseCount = 1 + rng.Next(indices.Count); 
            for (int i = 0; i < chooseCount; i++)
            {
                int cx = indices[i];
                CarveSouth(cx, rowIndex);
                nextRowSets[cx] = kv.Key;
                CarveCellCenter(cx, rowIndex + 1);
            }
        }
        for (int cx = 0; cx < cellCols; cx++)
        {
            if (nextRowSets[cx] == 0)
            {
                nextRowSets[cx] = nextSetId++;
            }
        }

        Array.Copy(nextRowSets, currentRowSets, cellCols);
    }

    private void MergeSetsInArray(int[] arr, int targetId, int sinkId)
    {
        if (targetId == sinkId) return;
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] == targetId) arr[i] = sinkId;
        }
    }

    #endregion

    #region Carving helpers (cell -> map coordinates)

    private void CarveCellCenter(int cx, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= cellRows) return;
        int mx = cx * 2 + 1;
        int my = rowIndex * 2 + 1;
        SetFloorAtMapLocal(mx, my);
    }

    private void CarveEast(int cx, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= cellRows) return;
        if (cx < 0 || cx >= cellCols - 1) return;
        int ax = cx * 2 + 1;
        int ay = rowIndex * 2 + 1;
        int bx = ax + 2;
        SetFloorAtMapLocal(ax, ay);
        SetFloorAtMapLocal(bx, ay);
        SetFloorAtMapLocal(ax + 1, ay);
    }

    private void CarveSouth(int cx, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= cellRows - 1) return; 
        int ax = cx * 2 + 1;
        int ay = rowIndex * 2 + 1;
        int by = ay + 2;
        SetFloorAtMapLocal(ax, ay);
        SetFloorAtMapLocal(ax, by);
        SetFloorAtMapLocal(ax, ay + 1);
    }

    private void SetFloorAtMapLocal(int localX, int localY)
    {
        if (localX < 0 || localY < 0 || localX >= mapWidth || localY >= mapHeight) return;
        isFloor[localX, localY] = true;
    }

    #endregion

    #region Painting / utility

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

    private void Shuffle(List<int> list, SimpleRNG r)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = r.Next(i + 1);
            int tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    #endregion

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(EllersMazeGenerator))]
    private class EllersEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EllersMazeGenerator script = (EllersMazeGenerator)target;

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
