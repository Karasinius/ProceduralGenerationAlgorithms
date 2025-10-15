// Well512Random.cs
// нцпнлмши оепхнд 2^512 - 1

using System;

public class Well512Random
{
    private uint[] state = new uint[16];
    private int index = 0;

    public Well512Random(int seed) { Init((ulong)seed); }
    public Well512Random() { Init((ulong)Environment.TickCount); }

    public void Init(ulong seed)
    {
        uint s = (uint)seed;
        if (s == 0) s = 0x9E3779B9u;
        for (int i = 0; i < 16; i++)
        {
            s = s * 1664525u + 1013904223u;
            state[i] = s;
        }
        index = 0;
    }

    public uint NextUInt()
    {
        uint a, b, c, d;
        a = state[index];
        c = state[(index + 13) & 15];
        b = a ^ c ^ (a << 16) ^ (c << 15);
        c = state[(index + 9) & 15];
        c ^= (c >> 11);
        state[index] = b ^ c;
        d = state[index] ^ ((state[index] << 5) & 0xDA442D24u);
        index = (index + 15) & 15;
        a = state[index];
        state[index] = a ^ b ^ d ^ (a << 2) ^ (b << 18) ^ (c << 28);
        return state[index];
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
