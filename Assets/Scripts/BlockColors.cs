using UnityEngine;
using UnityEngine.UI;
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

    /// <summary>Hayalet blok: alpha 1; düşük alpha ile kaydedilmiş RGB varsa unpremultiply.</summary>
    public static Color WithOpaqueAlpha(Color color)
    {
        if (color.a > 0.001f && color.a < 0.999f)
        {
            var inv = 1f / color.a;
            color.r = Mathf.Clamp01(color.r * inv);
            color.g = Mathf.Clamp01(color.g * inv);
            color.b = Mathf.Clamp01(color.b * inv);
        }

        color.a = 1f;
        return color;
    }

    public static void EnsureOpaqueSprite(SpriteRenderer sr, Color color)
    {
        if (sr == null)
            return;

        sr.DOKill();
        ClearSpriteMaterialOverrides(sr);
        sr.color = WithOpaqueAlpha(color);
    }

    public static void EnsureOpaqueSpriteKeepRgb(SpriteRenderer sr)
    {
        if (sr == null)
            return;

        sr.DOKill();
        ClearSpriteMaterialOverrides(sr);
        sr.color = WithOpaqueAlpha(sr.color);
    }

    /// <summary>Pause sonrası paylaşılan materyal / MPB solukluğunu sıfırla.</summary>
    public static void ClearSpriteMaterialOverrides(SpriteRenderer sr)
    {
        if (sr == null)
            return;

        sr.SetPropertyBlock(null);
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

    /// <summary>Kök + tüm child SpriteRenderer: alpha 1 ve verilen renk (hayalet önleme).</summary>
    public static void EnsureOpaqueBlock(Transform block, Color color)
    {
        if (block == null)
            return;

        block.DOKill();
        var opaque = WithOpaqueAlpha(color);
        var renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers.Length > 0)
        {
            for (int i = 0; i < renderers.Length; i++)
                EnsureOpaqueSprite(renderers[i], opaque);
        }
        else
        {
            EnsureOpaqueHierarchy(block);
        }

        ForceOpaqueUiGraphics(block);
    }

    /// <summary>
    /// Kayıt / pause / revive: kök + hiyerarşideki tüm görseller — tween iptal, alpha kesin 1.
    /// </summary>
    public static void ForceOpaqueVisualHierarchy(Transform root, Color? tint = null)
    {
        if (root == null)
            return;

        root.DOKill();

        var opaqueTint = tint.HasValue ? WithOpaqueAlpha(tint.Value) : (Color?)null;

        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var sr = renderers[i];
            if (sr == null)
                continue;

            if (opaqueTint.HasValue)
                EnsureOpaqueSprite(sr, opaqueTint.Value);
            else
                EnsureOpaqueSpriteKeepRgb(sr);
        }

        ForceOpaqueUiGraphics(root);
    }

    static void ForceOpaqueUiGraphics(Transform root)
    {
        var images = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            var img = images[i];
            if (img == null)
                continue;

            img.DOKill();
            var c = img.color;
            c.a = 1f;
            img.color = c;
        }

        var groups = root.GetComponentsInChildren<CanvasGroup>(true);
        for (int i = 0; i < groups.Length; i++)
        {
            var cg = groups[i];
            if (cg == null)
                continue;

            cg.DOKill();
            cg.alpha = 1f;
        }
    }

}

