using UnityEngine;
using DG.Tweening;

public enum BlockColor
{
    None = 0,
    Red = 1,
    Blue = 2,
    Yellow = 3,
    Green = 4,
    Purple = 5
}

public static class BlockColorUtils
{
    public static Color ToUnityColor(this BlockColor c)
    {
        return c switch
        {
            BlockColor.Red => Color.red,
            BlockColor.Blue => Color.blue,
            BlockColor.Yellow => Color.yellow,
            BlockColor.Green => Color.green,
            BlockColor.Purple => new Color(0.6f, 0.2f, 0.9f, 1f),
            _ => Color.white
        };
    }

    /// <summary>Hayalet blok önleme: alpha kesin 1.</summary>
    public static Color WithOpaqueAlpha(Color color)
    {
        color.a = 1f;
        return color;
    }

    public static void EnsureOpaqueSprite(SpriteRenderer sr, Color color)
    {
        if (sr == null)
            return;

        sr.DOKill();
        sr.color = WithOpaqueAlpha(color);
    }

    public static void EnsureOpaqueSpriteKeepRgb(SpriteRenderer sr)
    {
        if (sr == null)
            return;

        sr.DOKill();
        var c = sr.color;
        c.a = 1f;
        sr.color = c;
    }

    /// <summary>Transform + tüm child SpriteRenderer — yarım kalan fade tween'leri iptal.</summary>
    public static void EnsureOpaqueHierarchy(Transform root)
    {
        if (root == null)
            return;

        root.DOKill();
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            EnsureOpaqueSpriteKeepRgb(renderers[i]);
    }

    public static void EnsureOpaqueBlock(Transform block, Color color)
    {
        if (block == null)
            return;

        block.DOKill();
        if (block.TryGetComponent<SpriteRenderer>(out var sr))
            EnsureOpaqueSprite(sr, color);
        else
            EnsureOpaqueHierarchy(block);
    }
}

