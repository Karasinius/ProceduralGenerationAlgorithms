using System;
using UnityEngine;

/// <summary>
///  ласс реализует классический 2D Perlin noise (Ken Perlin style).
/// ¬озвращает значение в диапазоне 0..1.
/// Ёкземпл€р создаЄтс€ с seed'ом (дл€ воспроизводимости).
/// </summary>
public class CustomPerlin
{
    private readonly int[] perm; // таблица перестановок длиной 512
    private static readonly float INV_SQRT2 = 0.70710678f;

    // предопределЄнные векторы градиентов (8 направлений)
    // диагонали нормализованы приблизительно
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

    /// <summary>
    /// —оздаЄт таблицу перестановок на основе seed'а.
    /// </summary>
    public CustomPerlin(int seed)
    {
        // базова€ таблица 0..255
        int[] p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;

        // перемешиваем p с помощью System.Random(seed)
        System.Random rng = new System.Random(seed);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int tmp = p[i];
            p[i] = p[j];
            p[j] = tmp;
        }

        // строим таблицу perm длиной 512: perm[i] = p[i % 256]
        perm = new int[512];
        for (int i = 0; i < 512; i++)
            perm[i] = p[i & 255];
    }

    /// <summary>
    /// —глаживающа€ функци€ (fade) Ч классический полином 6t^5 - 15t^4 + 10t^3
    /// </summary>
    private static float Fade(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    /// <summary>
    /// Ћинейна€ интерпол€ци€
    /// </summary>
    private static float Lerp(float a, float b, float t)
    {
        return a + t * (b - a);
    }

    /// <summary>
    /// ѕолучить градиент по хэшу и посчитать скал€рное произведение с вектором (x,y)
    /// hash берЄм и берЄм 3 младших бита -> индекс 0..7 в массив gradients
    /// </summary>
    private static float Grad(int hash, float x, float y)
    {
        int h = hash & 7; // 0..7
        Vector2 g = gradients[h];
        return g.x * x + g.y * y;
    }

    /// <summary>
    /// ¬озвращает PerlinNoise дл€ (x,y) в диапазоне 0..1.
    /// ѕодразумеваетс€, что x,y могут быть любыми (положительными/отрицательными/нецелыми).
    /// </summary>
    public float Noise(float x, float y)
    {
        // шахматные координаты целой клетки
        int xi = Mathf.FloorToInt(x) & 255;
        int yi = Mathf.FloorToInt(y) & 255;

        // дробна€ позици€ внутри клетки
        float xf = x - Mathf.Floor(x);
        float yf = y - Mathf.Floor(y);

        // fade (сглаживание)
        float u = Fade(xf);
        float v = Fade(yf);

        // хэши дл€ 4 углов
        int aa = perm[perm[xi] + yi];
        int ab = perm[perm[xi] + yi + 1];
        int ba = perm[perm[xi + 1] + yi];
        int bb = perm[perm[xi + 1] + yi + 1];

        // градиентные скал€рные произведени€
        float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u);
        float x2 = Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u);
        float result = Lerp(x1, x2, v);

        // result примерно в диапазоне [-sqrt(2)/2, sqrt(2)/2], но мы просто мапим -1..1 -> 0..1
        // ƒл€ стабильности нормируем через коэффициент Ч значение лежит в пределах [-1,1] практически.
        // ¬ернЄм значение в 0..1:
        return (result + 1f) * 0.5f;
    }
}
