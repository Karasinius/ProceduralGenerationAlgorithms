using UnityEngine;
using UnityEngine.Tilemaps;

public enum EdgeType
{
    GRASS = 0,
    WATER = 1,
    FOREST = 2,
    COAST_N = 3,
    COAST_E = 4,
    COAST_S = 5,
    COAST_W = 6,
    FOREST_N = 7,
    FOREST_E = 8,
    FOREST_S = 9,
    FOREST_W = 10,
    ROCK_N = 11,
    ROCK_E = 12,
    ROCK_S = 13,
    ROCK_W = 14,
    ROCK = 15,


    ROAD_N = 16,
    ROAD_E = 17,
    ROAD_S = 18,
    ROAD_W = 19,
    ROAD = 20,

    COLUMN_N = 21,
    COLUMN_E = 22,
    COLUMN_S = 23,
    COLUMN_W = 24,
    COLUMN = 25,
    COLUMN_L = 26,
    COLUMN_R = 27,
    COLUMN_GRASS = 28,
    COLUMN_C = 29,

    COLUMN_BROKEN_1 = 30,
    COLUMN_BROKEN_2 = 31,
    COLUMN_BROKEN_3 = 32,
    COFFIN = 33,
}

[CreateAssetMenu(menuName = "WFC/TileType", fileName = "WfcTileType")]
public class WfcTileType : ScriptableObject
{
    public int id;

    public TileBase tile;

    public int weight = 1;

    [Tooltip("Edges order: [0]=North, [1]=East, [2]=South, [3]=West")]
    public EdgeType[] edges = new EdgeType[4];

    private void OnValidate()
    {
        if (edges == null || edges.Length != 4)
            edges = new EdgeType[4] { EdgeType.GRASS, EdgeType.GRASS, EdgeType.GRASS, EdgeType.GRASS };
        if (weight < 1) weight = 1;
    }
}
