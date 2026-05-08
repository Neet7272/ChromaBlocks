using UnityEngine;

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
}

