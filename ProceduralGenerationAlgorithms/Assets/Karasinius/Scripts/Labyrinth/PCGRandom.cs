// LCG (линейный конгруэнтный генератор)

//old = state

//state = old * 6364136223846793005 + inc(64 - бит LCG)

//x = ((old >> 18) ^ old) >> 27(сжатие старших бит)

//rot = old >> 59

//out = (x >> rot) | (x << ((-rot) & 31))(ротация)

// При определенных значениях период 2^64

using System;

public class PCGRandom
{
    private ulong state;
    private ulong inc;

    public PCGRandom(int seed) { Init((ulong)seed); }
    public PCGRandom() { Init((ulong)Environment.TickCount); }

    public void Init(ulong seed)
    {
        inc = (seed << 1) | 1UL;
        state = seed + 0x5851F42D4C957F2DUL;
        NextUInt();
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong oldstate = state;
        state = oldstate * 6364136223846793005UL + inc;
        uint xorshifted = (uint)(((oldstate >> 18) ^ oldstate) >> 27);
        int rot = (int)(oldstate >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
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
        uint a = NextUInt() >> 5; 
        uint b = NextUInt() >> 6; 
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
