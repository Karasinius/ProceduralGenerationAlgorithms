
public sealed class XorShift64StarRandom : System.Random
{
    private ulong state;

    public XorShift64StarRandom(int seed) : this((ulong)unchecked((uint)seed))
    { }


    public XorShift64StarRandom(ulong seed)
    {

        state = SplitMix64(seed ^ 0x9E3779B97F4A7C15UL);
        if (state == 0) state = 0xF39A2F9B3F7C1A07UL; 
    }

    private static ulong SplitMix64(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private ulong NextUInt64()
    {
        ulong x = state;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        state = x;
        return x * 2685821657736338717UL;
    }


    protected override double Sample()
    {
        ulong r = NextUInt64();
        const double scale = 1.0 / (1UL << 53); 
        ulong v = r >> 11;
        return (double)v * scale;
    }


    public ulong NextULong()
    {
        return NextUInt64();
    }
}
