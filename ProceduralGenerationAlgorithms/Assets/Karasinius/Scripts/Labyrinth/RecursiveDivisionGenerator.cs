using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class RecursiveDivisionGenerator : MonoBehaviour
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

    [Header("Play-mode (optional)")]
    public bool animateInPlay = false;
    public int playStepsPerBatch = 16;
    public float playBatchDelay = 0.01f;

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

    [ContextMenu("Generate RecursiveDivision (Editor Sync)")]
    public void GenerateContext()
    {
        if (Application.isPlaying)
        {
            if (playStepsPerBatch <= 0) playStepsPerBatch = 16;
            StartCoroutine(GenerateRoutine());
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
        PrepareMapArrays();
        RunRecursiveDivisionSync();
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
        PrepareMapArrays();

        if (!animateInPlay)
        {
            RunRecursiveDivisionSync();
            PaintTilemap();
            yield break;
        }

        PaintAllFloors(); 
        BuildInitialStack();

        while (stackTop > 0)
        {
            int steps = Math.Min(playStepsPerBatch, stackTop);
            for (int s = 0; s < steps; s++)
            {
                Region r = PopRegion();
                ProcessDivideAndPushChildren(r);
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
            Debug.LogWarning("[RecursiveDivisionGenerator] targetTilemap is null. Assign Tilemap in inspector.");
            return false;
        }
        if (floorTile == null || wallTile == null)
        {
            Debug.LogWarning("[RecursiveDivisionGenerator] floorTile or wallTile not assigned.");
            return false;
        }

        if (mapWidth < 3) mapWidth = 3;
        if (mapHeight < 3) mapHeight = 3;

        if (mapWidth % 2 == 0)
        {
            Debug.Log("[RecursiveDivisionGenerator] mapWidth is even — decreasing by 1 to make it odd for cell mapping.");
            mapWidth = Math.Max(3, mapWidth - 1);
        }
        if (mapHeight % 2 == 0)
        {
            Debug.Log("[RecursiveDivisionGenerator] mapHeight is even — decreasing by 1 to make it odd for cell mapping.");
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

    private void RunRecursiveDivisionSync()
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
            // choose a row index for the wall: wy in [y .. y+h-2]
            int wy = y + rng.Next(Math.Max(1, h - 1)); // value in [y .. y+h-2]
            // choose a column for passage: px in [x .. x+w-1]
            int px = x + rng.Next(w); // cell coordinate of gap

            // mapY is between wy and wy+1:
            int mapY = wy * 2 + 2;

            // draw continuous horizontal wall across the region: mapX from x*2 to x*2 + 2*w inclusive
            int startMapX = x * 2;
            int endMapX = x * 2 + 2 * w;
            int gapMapX = px * 2 + 1;

            for (int mapX = startMapX; mapX <= endMapX; mapX++)
            {
                // leave gap at center of chosen cell
                if (mapX == gapMapX) continue;
                if (mapX >= 0 && mapX < mapWidth && mapY >= 0 && mapY < mapHeight)
                    isFloor[mapX, mapY] = false;
            }

            // subregions: top and bottom
            int topW = w;
            int topH = wy - y + 1;
            int botW = w;
            int botH = y + h - (wy + 1);

            if (botW >= 1 && botH >= 1) PushRegion(new Region(x, wy + 1, botW, botH));
            if (topW >= 1 && topH >= 1) PushRegion(new Region(x, y, topW, topH));
        }
        else // vertical
        {
            // choose column for wall wx in [x .. x+w-2]
            int wx = x + rng.Next(Math.Max(1, w - 1)); // wx in [x .. x+w-2]
            // choose row for gap py in [y .. y+h-1]
            int py = y + rng.Next(h);

            // mapX is between wx and wx+1:
            int mapX = wx * 2 + 2;

            // draw continuous vertical wall: mapY from y*2 to y*2 + 2*h inclusive
            int startMapY = y * 2;
            int endMapY = y * 2 + 2 * h;
            int gapMapY = py * 2 + 1;

            for (int mapY = startMapY; mapY <= endMapY; mapY++)
            {
                if (mapY == gapMapY) continue;
                if (mapX >= 0 && mapX < mapWidth && mapY >= 0 && mapY < mapHeight)
                    isFloor[mapX, mapY] = false;
            }

            // left and right subregions
            int leftW = wx - x + 1;
            int leftH = h;
            int rightW = x + w - (wx + 1);
            int rightH = h;

            if (rightW >= 1 && rightH >= 1) PushRegion(new Region(wx + 1, y, rightW, rightH));
            if (leftW >= 1 && leftH >= 1) PushRegion(new Region(x, y, leftW, leftH));
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

    #endregion

#if UNITY_EDITOR

    public void StartEditorAnimatedGeneration()
    {
        if (!ValidateSetup()) return;
        if (editorAnimating)
        {
            Debug.Log("[RecursiveDivisionGenerator] Editor animation already running.");
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
            StopEditorAnimatedGeneration();
        }
    }

    [UnityEditor.CustomEditor(typeof(RecursiveDivisionGenerator))]
    private class RecursiveDivisionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            RecursiveDivisionGenerator script = (RecursiveDivisionGenerator)target;

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
                    if (script != null)
                        script.StartCoroutine(script.GenerateRoutine());
                }
                else
                {
                    UnityEngine.Debug.LogWarning("Enter Play mode to run Play-mode coroutine.");
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
