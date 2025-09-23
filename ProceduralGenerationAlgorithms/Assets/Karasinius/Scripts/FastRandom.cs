using System;

public class FastRandom
{
    // Не должно быть 0
    private uint state;

    public FastRandom(int seed)
    {
        state = (uint)seed;
        if (state == 0u)
            state = 0xDEADBEEF; 
    }

    // xorshift32: период ~2^32-1.
    // Возвращает uint в диапазоне 0..uint.MaxValue.
    private uint NextUInt()
    {
        uint x = state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        state = x;
        return x;
    }

    public int Next()
    {
        // берем 31 бит, чтобы результат был неотрицательным int
        return (int)(NextUInt() & 0x7FFFFFFF);
    }


    public int Next(int max)
    {
        if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max), "max must be > 0");
        return Next(0, max);
    }

    public int Next(int min, int max)
    {
        if (min >= max) throw new ArgumentOutOfRangeException(nameof(min), "min must be < max");

        ulong range = (ulong)max - (ulong)min;
        if (range <= uint.MaxValue)
        {
            uint r;
            // threshold — наибольшее число, кратное range, <= uint.MaxValue
            uint threshold = (uint)(uint.MaxValue / range) * (uint)range;
            do
            {
                r = NextUInt();
            } while (r >= threshold); // Не учитываем хвост для равномерно распредедения (10 % 6 не равномерно)
            return (int)(min + (r % (uint)range));
        }
        else
        {
            ulong r;
            ulong maxUL = ulong.MaxValue;
            ulong threshold = (maxUL / range) * range;
            do
            {
                ulong hi = NextUInt();
                ulong lo = NextUInt();
                r = (hi << 32) | lo;
            } while (r >= threshold);
            return (min + (int)(r % range));
        }
    }

    public double NextDouble()
    {
        return NextUInt() / (uint.MaxValue + 1.0);
    }

    public float NextFloat()
    {
        return (float)NextDouble();
    }

    public void NextBytes(byte[] buffer)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        int i = 0;
        while (i < buffer.Length)
        {
            uint v = NextUInt();
            // распакуем по 4 байта
            for (int b = 0; b < 4 && i < buffer.Length; b++)
            {
                buffer[i++] = (byte)(v & 0xFF);
                v >>= 8;
            }
        }
    }
}