using System.Collections.Generic;
using UnityEngine;

public sealed class Shape : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] ShapeData shapeData;

    [Header("Visuals")]
    [SerializeField] GameObject blockPrefab;
    [SerializeField, Min(0f)] float spacing = 0f;

    readonly List<Transform> _spawnedBlocks = new();
    readonly List<BlockInstance> _blocks = new();
    readonly List<Vector3> _blockLayoutLocalPositions = new();
    readonly List<Vector3> _blockLayoutLocalScales = new();
    Transform _anchorBlock;

    [Header("Logic (Game Over / simülasyon)")]
    [Tooltip("Rebuild sırasında doldurulur: döndürülmüş 5x5 matrisindeki tam sayı (x,y) hücre indeksleri; blok sırası ile Blocks uyumludur.")]
    public List<Vector2Int> logicalBlocks = new List<Vector2Int>();

    public ShapeData Data => shapeData;

    public readonly struct BlockInstance
    {
        public readonly Transform Transform;
        public readonly Vector2Int Offset; // anchor (min) bazlı
        public readonly Color Color;

        public BlockInstance(Transform t, Vector2Int offset, Color color)
        {
            Transform = t;
            Offset = offset;
            Color = BlockColorUtils.WithOpaqueAlpha(color);
        }
    }

    public struct RolePalette
    {
        public Color Primary;
        public Color Secondary;
        public Color Tertiary;
    }

    RolePalette _palette;

    /// <summary>0..3 = 0°, 90°, 180°, 270° CCW around board center (şekil verisi, transform dönmüyor).</summary>
    int _rotationQuarters;

    /// <summary>Kayıt: mevcut mantıksal dönüş.</summary>
    public int RotationQuarters => _rotationQuarters;

    public RolePalette CurrentPalette => _palette;

    int? _forcedRotationQuarters;

    public void Init(ShapeData data, GameObject blockPrefabOverride, float spacingOverride, RolePalette palette, int? rotationQuartersOverride = null)
    {
        shapeData = data;
        blockPrefab = blockPrefabOverride != null ? blockPrefabOverride : blockPrefab;
        spacing = Mathf.Max(0f, spacingOverride);
        _palette = palette;
        _forcedRotationQuarters = rotationQuartersOverride;
        Rebuild();
        _forcedRotationQuarters = null;
    }

    public void SetData(ShapeData data)
    {
        shapeData = data;
        Rebuild();
    }

    public void Rebuild()
    {
        ClearBlocks();

        logicalBlocks ??= new List<Vector2Int>();

        if (shapeData == null)
        {
            Debug.LogError("[Shape] ShapeData is not assigned.", this);
            return;
        }

        if (blockPrefab == null)
        {
            Debug.LogError("[Shape] blockPrefab is not assigned.", this);
            return;
        }

        if (!shapeData.HasAnyBlocks())
            return;

        // Kök dönmesin; gölgeler bozulmasın. Mantıksal dönüş sadece hücre koordinatlarında.
        transform.localRotation = Quaternion.identity;

        if (_forcedRotationQuarters.HasValue)
            _rotationQuarters = (_forcedRotationQuarters.Value % 4 + 4) % 4;
        else if (Application.isPlaying)
            _rotationQuarters = Random.Range(0, 4);
        else
            _rotationQuarters = 0;

        const int pivotX = 2;
        const int pivotY = 2;

        var cellSize = GridManagerCellSizeFallback(blockPrefab);
        var step = new Vector2(cellSize.x + spacing, cellSize.y + spacing);

        var occupied = new List<(int rx, int ry, BlockRole role)>(16);
        for (int y = 0; y < ShapeData.BoardSize; y++)
        {
            for (int x = 0; x < ShapeData.BoardSize; x++)
            {
                var role = shapeData.GetCell(x, y);
                if (role == BlockRole.None) continue;
                var r = RotateBoardCellK(x, y, _rotationQuarters, pivotX, pivotY);
                occupied.Add((r.x, r.y, role));
            }
        }

        // Bounds (min/max) over occupied cells after rotation
        var min = new Vector2Int(int.MaxValue, int.MaxValue);
        var max = new Vector2Int(int.MinValue, int.MinValue);
        for (int i = 0; i < occupied.Count; i++)
        {
            var (rx, ry, _) = occupied[i];
            if (rx < min.x) min.x = rx;
            if (ry < min.y) min.y = ry;
            if (rx > max.x) max.x = rx;
            if (ry > max.y) max.y = ry;
        }

        var width = (max.x - min.x + 1) * cellSize.x + (max.x - min.x) * spacing;
        var height = (max.y - min.y + 1) * cellSize.y + (max.y - min.y) * spacing;

        // Parent pivot merkez olsun: her bloğu bu merkeze göre kaydır.
        var centerOffset = new Vector2(width * 0.5f - cellSize.x * 0.5f, height * 0.5f - cellSize.y * 0.5f);

        _anchorBlock = null;

        for (int i = 0; i < occupied.Count; i++)
        {
            var (rx, ry, role) = occupied[i];

            logicalBlocks.Add(new Vector2Int(rx, ry));

            var localPos = new Vector2((rx - min.x) * step.x, (ry - min.y) * step.y) - centerOffset;
            var go = Instantiate(blockPrefab, transform);
            go.name = $"Block_r{_rotationQuarters * 90}_{rx}_{ry}_{role}";
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // Blok prefab'ında yanlışlıkla kalmış drag script'i çocuğu parmağa kilitlemesin.
            if (go.TryGetComponent<ShapeDragController>(out var strayDrag))
                Destroy(strayDrag);

            var color = BlockColorUtils.WithOpaqueAlpha(ResolveRoleColor(role));
            if (go.TryGetComponent<SpriteRenderer>(out var sr))
                BlockColorUtils.EnsureOpaqueSprite(sr, color);

            _spawnedBlocks.Add(go.transform);
            var offset = new Vector2Int(rx - min.x, ry - min.y);
            _blocks.Add(new BlockInstance(go.transform, offset, color));
            _blockLayoutLocalPositions.Add(go.transform.localPosition);
            _blockLayoutLocalScales.Add(go.transform.localScale);
            if (offset == Vector2Int.zero)
                _anchorBlock = go.transform;
        }

        EnsureBlocksFullyOpaque();

        // Drag raycast/collider alanı şeklin yeni bounds'una göre güncellensin.
        if (TryGetComponent<ShapeDragController>(out var drag))
            drag.UpdateColliderBounds();
    }

    public bool TryGetAnchorWorldPosition(out Vector2 worldPos)
    {
        if (_anchorBlock == null)
        {
            worldPos = default;
            return false;
        }

        worldPos = _anchorBlock.position;
        return true;
    }

    public IReadOnlyList<BlockInstance> Blocks => _blocks;

    /// <summary>Tepsi / kayıt sonrası hayalet (soluk) blokları düzeltir.</summary>
    public void EnsureBlocksFullyOpaque()
    {
        BlockColorUtils.EnsureOpaqueHierarchy(transform);
    }

    /// <summary>Sürükleme / ölçek tween sonrası çocuk blokları Rebuild düzenine geri sarar.</summary>
    public void RestoreBlockLayoutTransforms()
    {
        var n = Mathf.Min(_blocks.Count, _blockLayoutLocalPositions.Count);
        for (int i = 0; i < n; i++)
        {
            var t = _blocks[i].Transform;
            if (t == null)
                continue;

            t.localPosition = _blockLayoutLocalPositions[i];
            t.localScale = i < _blockLayoutLocalScales.Count
                ? _blockLayoutLocalScales[i]
                : Vector3.one;
        }
    }

    /// <summary>Şekildeki kare (blok) sayısı — yerleştirme puanı için.</summary>
    public int GetTileCount() => _blocks.Count;

    public bool TryGetBlockPrefabWorldSize(out Vector2 size)
    {
        if (blockPrefab == null)
        {
            size = default;
            return false;
        }

        size = GridManagerCellSizeFallback(blockPrefab);
        return size.x > 0f && size.y > 0f;
    }

    public List<BlockInstance> DetachBlocksForPlacement(Transform newParent)
    {
        var detached = new List<BlockInstance>(_blocks.Count);
        for (int i = 0; i < _blocks.Count; i++)
        {
            var b = _blocks[i];
            if (b.Transform != null)
                b.Transform.SetParent(newParent, true);
            detached.Add(b);
        }

        _blocks.Clear();
        _spawnedBlocks.Clear();
        logicalBlocks.Clear();
        return detached;
    }

    Color ResolveRoleColor(BlockRole role)
    {
        return role switch
        {
            BlockRole.Primary => _palette.Primary,
            BlockRole.Secondary => _palette.Secondary,
            BlockRole.Tertiary => _palette.Tertiary,
            _ => Color.white
        };
    }

    void ClearBlocks()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
        _spawnedBlocks.Clear();
        _blocks.Clear();
        _blockLayoutLocalPositions.Clear();
        _blockLayoutLocalScales.Clear();
        _anchorBlock = null;
        logicalBlocks.Clear();
    }

    static Vector2 GridManagerCellSizeFallback(GameObject prefabOrInstance)
    {
        if (prefabOrInstance.TryGetComponent<SpriteRenderer>(out var sr) && sr.sprite != null)
            return sr.bounds.size;

        if (prefabOrInstance.TryGetComponent<Collider2D>(out var col2d))
            return col2d.bounds.size;

        if (prefabOrInstance.TryGetComponent<RectTransform>(out var rt))
        {
            var lossy = rt.lossyScale;
            return new Vector2(rt.rect.width * lossy.x, rt.rect.height * lossy.y);
        }

        return Vector2.one;
    }

    /// <summary>k adet 90° CCW dönüşü, (px,py) pivot etrafında; 5x5 grid indeksleri.</summary>
    static Vector2Int RotateBoardCellK(int x, int y, int k, int px, int py)
    {
        var c = new Vector2Int(x, y);
        k = ((k % 4) + 4) % 4;
        for (int i = 0; i < k; i++)
            c = RotateBoardCell90Ccw(c, px, py);
        return c;
    }

    static Vector2Int RotateBoardCell90Ccw(Vector2Int cell, int px, int py)
    {
        var dx = cell.x - px;
        var dy = cell.y - py;
        return new Vector2Int(-dy + px, dx + py);
    }
}

