// Простой детерминированный генератор на базе SplitMix64.
// Период 2^32

using System;

public class SimpleRNG
{
    private ulong state;

    public SimpleRNG(int seed)
    {
        Init((ulong)seed);
    }

    public SimpleRNG()
    {
        Init((ulong)Environment.TickCount);
    }

    public void Init(ulong seed)
    {
        // Фиксированный оффсет
        state = seed + 0x9E3779B97F4A7C15UL;
    }

    // Генерация 64-bit значения (SplitMix64)
    private ulong NextULong()
    {
        ulong z = (state += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    // 32-bit unsigned
    public uint NextUInt()
    {
        return (uint)(NextULong() & 0xFFFFFFFFUL);
    }

    // Next like System.Random: [min, max)
    public int Next(int min, int max)
    {
        if (min >= max) return min;
        uint range = (uint)(max - min);
        return (int)(NextUInt() % range) + min;
    }

    // Next(int max) -> [0, max)
    public int Next(int max)
    {
        return Next(0, max);
    }

    // NextDouble() in [0,1)
    public double NextDouble()
    {
        // берем 53 бита точности и нормируем
        ulong v = NextULong() >> 11; // 64-11 = 53 bits
        return v * (1.0 / (1UL << 53));
    }

    // логический с заданной вероятностью p
    public bool NextBool(double probability = 0.5)
    {
        return NextDouble() < probability;
    }

    // convenience: next direction 0..3
    public int NextDirection4()
    {
        return Next(0, 4);
    }

    // convenience: Next in range inclusive [min..max]
    public int NextInclusive(int min, int max)
    {
        if (min >= max) return min;
        return min + (int)(NextUInt() % (uint)(max - min + 1));
    }
}
