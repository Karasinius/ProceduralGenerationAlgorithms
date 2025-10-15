// Mulberry32Random.cs
// Период 2^32

using System;

public class Mulberry32Random
{
    private uint state;

    public Mulberry32Random(int seed) { Init((ulong)seed); }
    public Mulberry32Random() { Init((ulong)Environment.TickCount); }

    public void Init(ulong seed)
    {
        state = (uint)(seed + 0x9E3779B9u);
        if (state == 0) state = 0x1u;
    }

    public uint NextUInt()
    {
        uint z = (state += 0x6D2B79F5u);
        z = (z ^ (z >> 15)) * (z | 1u);
        z ^= z + (z ^ (z >> 7)) * (z | 61u);
        return z ^ (z >> 14);
    }

    public int Next(int min, int max)
    {
        if (min >= max) return min;
        uint range = (uint)(max - min);
        return (int)(NextUInt() % range) + min;
    }

    public int Next(int max) => Next(0, max);

    public double NextDouble()
    {
        uint a = NextUInt() >> 5; // 27 bits
        uint b = NextUInt() >> 6; // 26 bits
        ulong v = ((ulong)a << 26) | b;
        return v * (1.0 / (1UL << 53));
    }

    public bool NextBool(double probability = 0.5) => NextDouble() < probability;

    public int NextDirection4() => Next(0, 4);

    public int NextInclusive(int min, int max)
    {
        if (min >= max) return min;
        return min + (int)(NextUInt() % (uint)(max - min + 1));
    }
}
