using System;
using UnityEngine;

public class CustomPerlin
{
    private readonly int[] perm; // таблица перестановок длиной 512
    private static readonly float INV_SQRT2 = 0.70710678f;

    private static readonly Vector2[] gradients = new Vector2[]
    {
        new Vector2( 1f,  0f),
        new Vector2(-1f,  0f),
        new Vector2( 0f,  1f),
        new Vector2( 0f, -1f),
        new Vector2( INV_SQRT2,  INV_SQRT2),
        new Vector2(-INV_SQRT2,  INV_SQRT2),
        new Vector2( INV_SQRT2, -INV_SQRT2),
        new Vector2(-INV_SQRT2, -INV_SQRT2),
    };

    public CustomPerlin(int seed)
    {
        int[] p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;

        FastRandom rng = new FastRandom(seed);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int tmp = p[i];
            p[i] = p[j];
            p[j] = tmp;
        }

        perm = new int[512];
        for (int i = 0; i < 512; i++)
            perm[i] = p[i & 255];
    }

    // —глаживающа€ функци€ (fade) Ч классический полином 6t^5 - 15t^4 + 10t^3
    private static float Fade(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + t * (b - a);
    }

    private static float Grad(int hash, float x, float y)
    {
        int h = hash & 7; // 0..7
        Vector2 g = gradients[h];
        return g.x * x + g.y * y;
    }

    public float Noise(float x, float y)
    {
        int xi = Mathf.FloorToInt(x) & 255;
        int yi = Mathf.FloorToInt(y) & 255;

        float xf = x - Mathf.Floor(x);
        float yf = y - Mathf.Floor(y);

        float u = Fade(xf);
        float v = Fade(yf);

        int aa = perm[perm[xi] + yi];
        int ab = perm[perm[xi] + yi + 1];
        int ba = perm[perm[xi + 1] + yi];
        int bb = perm[perm[xi + 1] + yi + 1];

        float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u);
        float x2 = Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u);
        float result = Lerp(x1, x2, v);

        //  мапим -1..1 -> 0..1
        return (result + 1f) * 0.5f;
    }
}
