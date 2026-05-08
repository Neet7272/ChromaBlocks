using UnityEngine;

public enum BlockRole
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Tertiary = 3
}

[CreateAssetMenu(menuName = "ChromaBlocks/Shape Data", fileName = "ShapeData_")]
public sealed class ShapeData : ScriptableObject
{
    public const int BoardSize = 5;

    [Header("Identification")]
    [SerializeField] string shapeName = "NewShape";

    [Header("Role Board (5x5)")]
    [Tooltip("5x5 şekil ızgarası. None = boş hücre. Primary/Secondary/Tertiary = rol.")]
    [SerializeField] BlockRole[] roleBoard = new BlockRole[BoardSize * BoardSize];

    public string ShapeName => shapeName;

    public BlockRole GetCell(int x, int y)
    {
        if ((uint)x >= BoardSize || (uint)y >= BoardSize) return BlockRole.None;
        return roleBoard[(y * BoardSize) + x];
    }

    public void SetCell(int x, int y, BlockRole value)
    {
        if ((uint)x >= BoardSize || (uint)y >= BoardSize) return;
        roleBoard[(y * BoardSize) + x] = value;
    }

    public bool HasAnyBlocks()
    {
        for (int i = 0; i < roleBoard.Length; i++)
        {
            if (roleBoard[i] != BlockRole.None)
                return true;
        }
        return false;
    }
}

