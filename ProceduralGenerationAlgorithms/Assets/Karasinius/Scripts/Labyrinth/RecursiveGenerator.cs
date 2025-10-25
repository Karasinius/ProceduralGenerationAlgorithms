using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class RecursiveGenerator : MonoBehaviour
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
    public int editorStepsPerBatch = 8;
    public float editorBatchDelay = 0.03f;

    [Header("Cycles (post-process)")]
    [Tooltip("Если true — после построения лабиринта будет выполнен пост-процесс добавления дополнительных проходов.")]
    public bool addCycles = false;
    [Tooltip("Вероятность для каждой клетки (узла) добавить проход в одном случайно выбранном направлении (0..1).")]
    [Range(0f, 1f)]
    public float cycleProbability = 0.05f;

    private int cellCols;    // (mapWidth - 1) / 2
    private int cellRows;    // (mapHeight - 1) / 2

    private bool[,] isFloor;

    private PCGRandom rng;

    private struct Region { public int x, y, w, h; public Region(int x, int y, int w, int h) { this.x = x; this.y = y; this.w = w; this.h = h; } }
    private Region[] regionStack;
    private int stackTop;

#if UNITY_EDITOR
    private bool editorAnimating = false;
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
        RunRecursiveSync();

        if (addCycles)
            AddCyclesPostProcess();

        PaintTilemap();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    #endregion

    #region Setup & helpers

    private bool ValidateSetup()
    {
        if (targetTilemap == null)
        {
            Debug.LogWarning("[RecursiveGenerator] targetTilemap is null. Assign Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[RecursiveGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[RecursiveGenerator] mapWidth is even — decreasing by 1 to make it odd for cell mapping.");
            mapWidth = Math.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[RecursiveGenerator] mapHeight is even — decreasing by 1 to make it odd for cell mapping.");
            mapHeight = Math.Max(3, mapHeight - 1);
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
        rng = new PCGRandom(seed);
    }

    private void PrepareMapArrays()
    {
        isFloor = new bool[mapWidth, mapHeight];
        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                isFloor[x, y] = true;

        for (int x = 0; x < mapWidth; x++)
        {
            isFloor[x, 0] = false;
            isFloor[x, mapHeight - 1] = false;
        }
        for (int y = 0; y < mapHeight; y++)
        {
            isFloor[0, y] = false;
            isFloor[mapWidth - 1, y] = false;
        }

        int worst = cellCols * cellRows * 2 + 16;
        regionStack = new Region[worst];
        stackTop = 0;
    }

    private void PushRegion(Region r)
    {
        if (stackTop >= regionStack.Length)
        {
            Array.Resize(ref regionStack, regionStack.Length * 2);
        }
        regionStack[stackTop++] = r;
    }
    private Region PopRegion()
    {
        if (stackTop <= 0) return new Region(0, 0, 0, 0);
        return regionStack[--stackTop];
    }

    private void BuildInitialStack()
    {
        stackTop = 0;
        PushRegion(new Region(0, 0, cellCols, cellRows));
    }

    #endregion

    #region Core division logic

    private void RunRecursiveSync()
    {
        for (int x = 0; x < mapWidth; x++)
            for (int y = 0; y < mapHeight; y++)
                isFloor[x, y] = true;
        for (int x = 0; x < mapWidth; x++)
        {
            isFloor[x, 0] = false;
            isFloor[x, mapHeight - 1] = false;
        }
        for (int y = 0; y < mapHeight; y++)
        {
            isFloor[0, y] = false;
            isFloor[mapWidth - 1, y] = false;
        }

        BuildInitialStack();

        while (stackTop > 0)
        {
            Region r = PopRegion();
            ProcessDivideAndPushChildren(r);
        }
    }

    private void ProcessDivideAndPushChildren(Region r)
    {
        int x = r.x, y = r.y, w = r.w, h = r.h;
        if (w < 2 || h < 2) return;

        bool horizontal;
        if (w < h) horizontal = true;
        else if (h < w) horizontal = false;
        else horizontal = rng.Next(2) == 0;

        if (horizontal)
        {
            int wy = y + rng.Next(Math.Max(1, h - 1)); 
            int px = x + rng.Next(w);

            int mapY = wy * 2 + 2;

            int startMapX = x * 2;
            int endMapX = x * 2 + 2 * w;
            int gapMapX = px * 2 + 1;

            for (int mapX = startMapX; mapX <= endMapX; mapX++)
            {
                if (mapX == gapMapX) continue;
                if (mapX >= 0 && mapX < mapWidth && mapY >= 0 && mapY < mapHeight)
                    isFloor[mapX, mapY] = false;
            }

            int topW = w;
            int topH = wy - y + 1;
            int botW = w;
            int botH = y + h - (wy + 1);

            if (botW >= 1 && botH >= 1) PushRegion(new Region(x, wy + 1, botW, botH));
            if (topW >= 1 && topH >= 1) PushRegion(new Region(x, y, topW, topH));
        }
        else 
        {
            int wx = x + rng.Next(Math.Max(1, w - 1)); 
            int py = y + rng.Next(h);

            int mapX = wx * 2 + 2;

            int startMapY = y * 2;
            int endMapY = y * 2 + 2 * h;
            int gapMapY = py * 2 + 1;

            for (int mapY = startMapY; mapY <= endMapY; mapY++)
            {
                if (mapY == gapMapY) continue;
                if (mapX >= 0 && mapX < mapWidth && mapY >= 0 && mapY < mapHeight)
                    isFloor[mapX, mapY] = false;
            }

            int leftW = wx - x + 1;
            int leftH = h;
            int rightW = x + w - (wx + 1);
            int rightH = h;

            if (rightW >= 1 && rightH >= 1) PushRegion(new Region(wx + 1, y, rightW, rightH));
            if (leftW >= 1 && leftH >= 1) PushRegion(new Region(x, y, leftW, leftH));
        }
    }

    #endregion

    #region Post-process: add cycles (new behavior)
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
                if (my - 2 >= 1) dirs.Add(0); // North: 
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

    #region Painting helpers

    private void PaintTilemap()
    {
        if (targetTilemap == null) return;
        for (int mx = 0; mx < mapWidth; mx++)
        {
            for (int my = 0; my < mapHeight; my++)
            {
                Vector3Int pos = new Vector3Int(mapOrigin.x + mx, mapOrigin.y + my, 0);
                if (isFloor[mx, my])
                    targetTilemap.SetTile(pos, floorTile);
                else
                    targetTilemap.SetTile(pos, wallTile);
            }
        }
    }

    private void PaintAllFloors()
    {
        if (targetTilemap == null) return;
        for (int mx = 0; mx < mapWidth; mx++)
        {
            for (int my = 0; my < mapHeight; my++)
            {
                targetTilemap.SetTile(new Vector3Int(mapOrigin.x + mx, mapOrigin.y + my, 0), floorTile);
            }
        }
    }

    private void SetFloorAtMapLocal(int localX, int localY)
    {
        if (localX < 0 || localY < 0 || localX >= mapWidth || localY >= mapHeight)
            return;
        isFloor[localX, localY] = true;
    }

    #endregion

#if UNITY_EDITOR

    public void StartEditorAnimatedGeneration()
    {
        if (!ValidateSetup()) return;
        if (editorAnimating)
        {
            Debug.Log("[RecursiveGenerator] Editor animation already running.");
            return;
        }

        PrepareRandom();
        PrepareMapArrays();
        PaintAllFloors();

        BuildInitialStack();

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

        int processed = 0;
        int batch = Math.Max(1, editorStepsPerBatch);

        while (processed < batch && stackTop > 0)
        {
            Region r = PopRegion();
            ProcessDivideAndPushChildren(r);
            processed++;
        }

        PaintTilemap();
        UnityEditor.EditorUtility.SetDirty(targetTilemap);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        if (stackTop <= 0)
        {
            if (addCycles)
                AddCyclesPostProcess();

            StopEditorAnimatedGeneration();
        }
    }

    [UnityEditor.CustomEditor(typeof(RecursiveGenerator))]
    private class RecursiveEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            RecursiveGenerator script = (RecursiveGenerator)target;

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
