// Xoshiro256StarStar
// Xor shift rotate
// Период 2^256 - 1

using System;

public class Xoshiro256StarStar
{
    private ulong s0, s1, s2, s3;

    public Xoshiro256StarStar(int seed) { Init((ulong)seed); }
    public Xoshiro256StarStar() { Init((ulong)Environment.TickCount); }

    public void Init(ulong seed)
    {
        ulong z = seed + 0x9E3779B97F4A7C15UL;
        s0 = SplitMix64(ref z);
        s1 = SplitMix64(ref z);
        s2 = SplitMix64(ref z);
        s3 = SplitMix64(ref z);
        if ((s0 | s1 | s2 | s3) == 0)
        {
            s0 = 0x0123_4567_89AB_CDEFUL;
            s1 = 0xFEDC_BA98_7654_3210UL;
            s2 = 0xF00D_BEEF_DEAD_BEEFUL;
            s3 = 0xC0FF_EE00_CAFE_BABEUL;
        }
    }

    private static ulong SplitMix64(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        ulong result = z;
        result = (result ^ (result >> 30)) * 0xBF58476D1CE4E5B9UL;
        result = (result ^ (result >> 27)) * 0x94D049BB133111EBUL;
        return result ^ (result >> 31);
    }

    // left-rotate
    private static ulong Rol(ulong x, int k) => (x << k) | (x >> (64 - k));

    private ulong NextULong()
    {
        ulong result = Rol(s1 * 5, 7) * 9;

        ulong t = s1 << 17;

        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;

        s2 ^= t;
        s3 = Rol(s3, 45);

        return result;
    }

    public uint NextUInt()
    {
        return (uint)(NextULong() & 0xFFFFFFFFUL);
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
        ulong v = NextULong() >> 11; 
        return v * (1.0 / (1UL << 53));
    }

    public bool NextBool(double probability = 0.5) => NextDouble() < probability;

    public int NextDirection4() => Next(0, 4);

    public int NextInclusive(int min, int max)
    {
        if (min >= max) return min;
        uint span = (uint)(max - min + 1);
        return min + (int)(NextUInt() % span);
    }
}
