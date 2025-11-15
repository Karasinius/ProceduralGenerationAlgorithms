// WfcTileType.cs
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
    ROCK = 15
    // Если нужны дополнительные — добавь, чтобы соответствовать твоему Config.py
}

[CreateAssetMenu(menuName = "WFC/TileType", fileName = "WfcTileType")]
public class WfcTileType : ScriptableObject
{
    [Tooltip("Unique id (for your reference). Match it to your existing tile set indices if needed")]
    public int id;

    [Tooltip("Tile asset to paint on Tilemap")]
    public TileBase tile;

    [Tooltip("Weight used when randomly collapsing among possibilities")]
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
