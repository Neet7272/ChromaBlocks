using UnityEngine;

/// <summary>
/// Grid üzerindeki tek bir hücreyi temsil eder.
/// Bu script, her bir hücre prefab'ının üzerinde bulunmalıdır.
/// </summary>
public sealed class GridCell : MonoBehaviour
{
    [Header("Coordinates")]
    [SerializeField] int x;
    [SerializeField] int y;

    [Header("State")]
    [SerializeField] bool isOccupied;

    Transform _placedBlock;
    Color _placedColor;

    [Header("Visuals")]
    [SerializeField] Color previewColor = new Color(1f, 1f, 1f, 0.30f);
    [SerializeField] Color occupiedTint = new Color(1f, 1f, 1f, 1f);
    [SerializeField] Color predictiveClearColor = new Color(0.55f, 0.95f, 1f, 0.62f);

    SpriteRenderer _sr;
    Color _baseColor;
    bool _hasSprite;
    bool _highlightActive;
    HighlightKind _highlightKind;

    enum HighlightKind
    {
        None,
        PlacementHover,
        PredictiveClear
    }

    public int X => x;
    public int Y => y;
    public bool IsOccupied
    {
        get => isOccupied;
        set => isOccupied = value;
    }

    public Transform PlacedBlock => _placedBlock;
    public Color PlacedColor => _placedColor;

    /// <summary>Hücreyi grid koordinatlarıyla başlatır.</summary>
    public void Init(int gridX, int gridY)
    {
        x = gridX;
        y = gridY;
        isOccupied = false;
        _placedBlock = null;
        _placedColor = default;

        if (_sr == null)
            _hasSprite = TryGetComponent(out _sr);
        if (_hasSprite)
            _baseColor = _sr.color;
    }

    public void SetPlacedBlock(Transform block, Color color)
    {
        _placedBlock = block;
        _placedColor = color;
        isOccupied = block != null;
    }

    public Transform ClearPlacedBlock()
    {
        var b = _placedBlock;
        _placedBlock = null;
        _placedColor = default;
        isOccupied = false;
        return b;
    }

    public void SetPreview(bool value)
    {
        if (value)
            SetPlacementHoverHighlight();
        else
            ResetHighlight();
    }

    public void SetPlacementHoverHighlight()
    {
        if (isOccupied)
            return;

        _highlightActive = true;
        _highlightKind = HighlightKind.PlacementHover;
        ApplyHighlightVisual();
    }

    public void SetPredictiveClearHighlight()
    {
        _highlightActive = true;
        _highlightKind = HighlightKind.PredictiveClear;
        ApplyHighlightVisual();
    }

    public void ResetHighlight()
    {
        _highlightActive = false;
        _highlightKind = HighlightKind.None;
        ApplyHighlightVisual();
    }

    void ApplyHighlightVisual()
    {
        if (!_hasSprite || _sr == null)
            return;

        if (!_highlightActive)
        {
            _sr.color = isOccupied ? occupiedTint : _baseColor;
            return;
        }

        _sr.color = _highlightKind == HighlightKind.PredictiveClear
            ? predictiveClearColor
            : previewColor;
    }
}

