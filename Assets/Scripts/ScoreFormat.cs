using UnityEngine;

/// <summary>HUD ve Game Over skor metinleri için ortak sıfır doldurma.</summary>
public static class ScoreFormat
{
    public const int DefaultDigits = 5;

    public static string Pad(int score, int digits = DefaultDigits)
    {
        digits = Mathf.Max(1, digits);
        score = Mathf.Max(0, score);

        var max = 1;
        for (int i = 0; i < digits; i++)
            max *= 10;
        score = Mathf.Min(score, max - 1);

        return score.ToString($"D{digits}");
    }
}
