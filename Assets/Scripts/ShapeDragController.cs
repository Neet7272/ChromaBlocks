using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;

/// <summary>
/// Shape için temel Drag & Drop iskeleti.
/// - PointerDown: scale -> (1,1,1)
/// - Drag: pointer'ı takip + Y offset
/// - PointerUp: snap yok; doğduğu noktaya ve eski scale'e Lerp ile geri dön
/// </summary>
public sealed class ShapeDragController : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler
{
    [Header("Drag")]
    [SerializeField] float dragYOffset = 1.5f;

    [Header("Return Animation")]
    [SerializeField, Min(0.01f)] float returnDuration = 0.22f;

    [Header("Scale")]
    [SerializeField, Tooltip("Tahta boyutuna ek çarpan (1 = tahtadaki blokla birebir).")]
    Vector3 heldScale = Vector3.one;
    [SerializeField] bool scaleToGridCellSize = true;

    [Header("Tween Timings")]
    [SerializeField, Range(0f, 1f), Tooltip("0 = anında takip; yüksek = yumuşak Lerp.")]
    float dragFollowLerp = 0.35f;

    [Header("Grid Snapping")]
    [SerializeField] GridManager gridManager;
    [SerializeField] ShapeSpawner shapeSpawner;
    [SerializeField, Tooltip("Boşsa Awake'te Camera.main bir kez cache'lenir; tahta başka kameradaysa buraya sürükleyin.")]
    Camera worldCamera;

    [Header("Sorting")]
    [SerializeField] int dragSortingOrder = 100;
    [SerializeField] string dragSortingLayerName = "Foreground";

    [Header("Depth (Z)")]
    [SerializeField] float dragZ = -1f;

    [Header("Touch — sadece hitbox (görsel / snap / grid değişmez)")]
    [SerializeField, Min(0f), Tooltip("Sprite sınırına eklenen yerel tampon (komşu şekle taşmaz).")]
    float touchHitboxPaddingLocal = 0.42f;
    [SerializeField, Min(1f), Tooltip("Dış kenarlara (üst/alt/dış yan) ekstra genişlik çarpanı.")]
    float touchHitboxOuterPaddingMultiplier = 1.65f;
    [SerializeField, Min(0f), Tooltip("Komşu tepsi şekli arasında bırakılan dünya boşluğu.")]
    float touchHitboxNeighborGapWorld = 0.12f;
    [SerializeField, Min(1f), Tooltip("1x1 vb. tek blokta padding çarpanı (kalın parmak).")]
    float singleBlockHitboxPaddingMultiplier = 2.9f;

    Camera _cam;
    Vector3 _spawnWorldPos;
    Vector3 _spawnLocalScale;
    float _spawnZ;
    Coroutine _returnRoutine;

    BoxCollider2D _box;
    Shape _shape;

    SpriteRenderer[] _renderers;
    int[] _originalSortingOrders;
    int[] _originalSortingLayerIds;

    void Awake()
    {
        EnsureCollider2D();
        _shape = GetComponent<Shape>();
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (shapeSpawner == null)
            shapeSpawner = FindAnyObjectByType<ShapeSpawner>();
        CacheWorldCameraOnce();
    }

    void CacheWorldCameraOnce()
    {
        if (worldCamera != null)
            _cam = worldCamera;
        else if (_cam == null)
            _cam = Camera.main;
    }

    /// <summary>Tahtaya konan blokla aynı intrinsic ölçek (GridManager.NormalizePlacedBlockScale).</summary>
    Vector3 ComputeHeldDragScale()
    {
        if (scaleToGridCellSize && gridManager != null &&
            gridManager.TryGetHeldShapeLocalScale(transform, out var boardScale))
        {
            var hx = heldScale.x > 0f ? heldScale.x : 1f;
            var hy = heldScale.y > 0f ? heldScale.y : 1f;
            return new Vector3(boardScale.x * hx, boardScale.y * hy, 1f);
        }

        float trayU = Mathf.Max(_spawnLocalScale.x, _spawnLocalScale.y);
        float mult = Mathf.Max(heldScale.x, heldScale.y, 1f);
        return Vector3.one * (trayU * mult);
    }

    bool IsLockedByGameOver()
    {
        return shapeSpawner != null && shapeSpawner.IsGameOver;
    }

    bool IsLockedByBoardBusy()
    {
        return gridManager != null && gridManager.IsResolvingMatches;
    }

    bool IsInputLocked()
    {
        return IsLockedByGameOver() || IsLockedByBoardBusy();
    }

    void Start()
    {
        // Spawner Instantiate sonrası scale ayarlayacağı için, spawn değerlerini Start'ta yakalıyoruz.
        _spawnWorldPos = transform.position;
        _spawnLocalScale = shapeSpawner != null
            ? shapeSpawner.TrayRestingLocalScale
            : transform.localScale;
        _spawnZ = transform.position.z;

        // Spawn intro scale=0 iken bounds hesaplanırsa collider sıfır kalır; raycast çalışmaz.
        if (transform.localScale.sqrMagnitude > 0.0001f)
            UpdateColliderBounds();

        CacheRenderers();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsInputLocked())
            return;

        StopReturn();
        transform.DOKill();
        KillChildBlockTweens();

        SetDraggingSorting(true);

        var p = transform.position;
        p.z = dragZ;
        transform.position = p;

        var targetScale = ComputeHeldDragScale();

        _shape?.RestoreBlockLayoutTransforms();
        _shape?.EnsureBlocksFullyOpaque();

        transform.DOKill();
        transform.localScale = targetScale;
        _shape?.RestoreBlockLayoutTransforms();

        ApplyDragMoveAndPreview(eventData, forcePreview: true);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsInputLocked())
            return;

        transform.DOKill();
        transform.localScale = ComputeHeldDragScale();
        _shape?.RestoreBlockLayoutTransforms();
        _shape?.EnsureBlocksFullyOpaque();

        AudioManager.PlayPickUpSfxSafe();
        HapticManager.Instance?.PlayLightHaptic();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsInputLocked())
            return;

        ApplyDragMoveAndPreview(eventData, forcePreview: false);
    }

    void ApplyDragMoveAndPreview(PointerEventData eventData, bool forcePreview)
    {
        StopReturn();

        if (_cam == null)
            return;

        var z = transform.position.z - _cam.transform.position.z;
        var screen = new Vector3(eventData.position.x, eventData.position.y, z);
        var world = _cam.ScreenToWorldPoint(screen);

        world.y += dragYOffset;
        world.z = dragZ;

        if (dragFollowLerp <= 0f)
            transform.position = world;
        else
            transform.position = Vector3.Lerp(transform.position, world, dragFollowLerp);

        _shape?.RestoreBlockLayoutTransforms();

        if (gridManager != null && _shape != null)
            gridManager.UpdatePlacementPreview(_shape, forcePreview);
    }

    void KillChildBlockTweens()
    {
        if (_shape == null)
            return;

        var blocks = _shape.Blocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            var t = blocks[i].Transform;
            if (t != null)
                t.DOKill();
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (IsInputLocked())
            return;

        StopReturn();
        transform.DOKill();

        var pointerWorld = ScreenToPointerWorld(eventData);

        // Fare alt bölgedeyse bile şekil ızgarayla üst üste biniyorsa yerleştirmeyi dene.
        var tryGridPlacement = gridManager != null &&
            (gridManager.IsPointOverGrid(pointerWorld) ||
             (_shape != null && gridManager.IsShapeOverlappingGrid(_shape)));

        if (gridManager != null && !tryGridPlacement)
        {
            gridManager.ClearPreview();
            ReturnToStartPosition();
            return;
        }

        if (gridManager != null && _shape != null && gridManager.TryPlaceShape(_shape))
        {
            HapticManager.Instance?.PlayLightHaptic();
            return; // şekil sahneden silindi
        }

        if (gridManager != null)
            gridManager.ClearPreview();

        ReturnToStartPosition();
    }

    Vector2 ScreenToPointerWorld(PointerEventData eventData)
    {
        if (_cam == null)
            return Vector2.zero;

        var planeZ = gridManager != null ? gridManager.transform.position.z : dragZ;
        var z = planeZ - _cam.transform.position.z;
        var screen = new Vector3(eventData.position.x, eventData.position.y, z);
        var world = _cam.ScreenToWorldPoint(screen);
        return new Vector2(world.x, world.y);
    }

    void ReturnToStartPosition()
    {
        SetDraggingSorting(false);

        var target = _spawnWorldPos;
        target.z = _spawnZ;
        var mid = Vector3.Lerp(transform.position, target, 0.65f);
        mid.z = _spawnZ;

        var seq = DOTween.Sequence();
        seq.Append(transform.DOMove(mid, returnDuration * 0.45f).SetEase(Ease.InSine));
        seq.Append(transform.DOMove(target, returnDuration * 0.55f).SetEase(Ease.OutBounce));
        seq.Join(transform.DOScale(_spawnLocalScale, returnDuration).SetEase(Ease.OutBounce));
        seq.OnUpdate(() => _shape?.RestoreBlockLayoutTransforms());
        seq.OnComplete(() =>
        {
            _shape?.RestoreBlockLayoutTransforms();
            _shape?.EnsureBlocksFullyOpaque();
        });
    }

    void StopReturn()
    {
        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
        }
    }

    void EnsureCollider2D()
    {
        if (_box != null) return;
        if (!TryGetComponent(out _box))
        {
            // Raycast'lerin çalışması için Collider2D şart (Physics2DRaycaster + EventSystem gerekir).
            _box = gameObject.AddComponent<BoxCollider2D>();
        }
    }

    /// <summary>
    /// Child sprite AABB'sine göre BoxCollider2D; görseli değiştirmeden dokunma için yerel padding ekler.
    /// </summary>
    public void UpdateColliderBounds()
    {
        EnsureCollider2D();

        var renderers = GetComponentsInChildren<SpriteRenderer>();
        Vector2 localMin;
        Vector2 localMax;

        if (renderers == null || renderers.Length == 0)
        {
            localMin = new Vector2(-0.5f, -0.5f);
            localMax = new Vector2(0.5f, 0.5f);
        }
        else
        {
            var combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);

            var localMin3 = transform.InverseTransformPoint(combined.min);
            var localMax3 = transform.InverseTransformPoint(combined.max);

            localMin = new Vector2(Mathf.Min(localMin3.x, localMax3.x), Mathf.Min(localMin3.y, localMax3.y));
            localMax = new Vector2(Mathf.Max(localMin3.x, localMax3.x), Mathf.Max(localMin3.y, localMax3.y));
        }

        ApplyDirectionalTouchPadding(ref localMin, ref localMax);

        _box.offset = (localMin + localMax) * 0.5f;
        _box.size = localMax - localMin;
    }

    void ApplyDirectionalTouchPadding(ref Vector2 localMin, ref Vector2 localMax)
    {
        var padHalf = GetTouchPaddingHalfExtents();
        var extraOuter = padHalf * Mathf.Max(0f, touchHitboxOuterPaddingMultiplier - 1f);

        localMin -= padHalf;
        localMax += padHalf;

        localMin.y -= extraOuter.y;
        localMax.y += extraOuter.y;

        switch (ResolveTraySlot())
        {
            case TraySlot.Left:
                localMin.x -= extraOuter.x;
                ClampLocalMaxXToWorldX(ref localMax, GetNeighborMaxWorldX(TraySlot.Left));
                break;
            case TraySlot.Right:
                localMax.x += extraOuter.x;
                ClampLocalMinXToWorldX(ref localMin, GetNeighborMinWorldX(TraySlot.Right));
                break;
            default:
                ClampLocalMaxXToWorldX(ref localMax, GetNeighborMaxWorldX(TraySlot.Middle));
                ClampLocalMinXToWorldX(ref localMin, GetNeighborMinWorldX(TraySlot.Middle));
                break;
        }
    }

    enum TraySlot
    {
        Left,
        Middle,
        Right
    }

    TraySlot ResolveTraySlot()
    {
        if (shapeSpawner == null ||
            shapeSpawner.LeftSpawnPoint == null ||
            shapeSpawner.MiddleSpawnPoint == null ||
            shapeSpawner.RightSpawnPoint == null)
            return TraySlot.Middle;

        var x = transform.position.x;
        var lx = shapeSpawner.LeftSpawnPoint.position.x;
        var mx = shapeSpawner.MiddleSpawnPoint.position.x;
        var rx = shapeSpawner.RightSpawnPoint.position.x;

        if (Mathf.Abs(x - lx) <= Mathf.Abs(x - mx) && Mathf.Abs(x - lx) <= Mathf.Abs(x - rx))
            return TraySlot.Left;
        if (Mathf.Abs(x - rx) <= Mathf.Abs(x - mx))
            return TraySlot.Right;
        return TraySlot.Middle;
    }

    float GetNeighborMaxWorldX(TraySlot slot)
    {
        var gap = touchHitboxNeighborGapWorld;
        if (shapeSpawner == null || shapeSpawner.MiddleSpawnPoint == null)
            return float.PositiveInfinity;

        if (slot == TraySlot.Left)
        {
            var left = shapeSpawner.LeftSpawnPoint != null
                ? shapeSpawner.LeftSpawnPoint.position.x
                : transform.position.x;
            var mid = shapeSpawner.MiddleSpawnPoint.position.x;
            return (left + mid) * 0.5f - gap;
        }

        if (shapeSpawner.RightSpawnPoint == null)
            return float.PositiveInfinity;

        var center = shapeSpawner.MiddleSpawnPoint.position.x;
        var right = shapeSpawner.RightSpawnPoint.position.x;
        return (center + right) * 0.5f - gap;
    }

    float GetNeighborMinWorldX(TraySlot slot)
    {
        var gap = touchHitboxNeighborGapWorld;
        if (shapeSpawner == null || shapeSpawner.MiddleSpawnPoint == null)
            return float.NegativeInfinity;

        if (slot == TraySlot.Right)
        {
            var mid = shapeSpawner.MiddleSpawnPoint.position.x;
            var right = shapeSpawner.RightSpawnPoint != null
                ? shapeSpawner.RightSpawnPoint.position.x
                : transform.position.x;
            return (mid + right) * 0.5f + gap;
        }

        if (shapeSpawner.LeftSpawnPoint == null)
            return float.NegativeInfinity;

        var left = shapeSpawner.LeftSpawnPoint.position.x;
        var center = shapeSpawner.MiddleSpawnPoint.position.x;
        return (left + center) * 0.5f + gap;
    }

    void ClampLocalMaxXToWorldX(ref Vector2 localMax, float worldMaxX)
    {
        if (float.IsPositiveInfinity(worldMaxX))
            return;

        var localLimit = transform.InverseTransformPoint(new Vector3(worldMaxX, transform.position.y, transform.position.z)).x;
        localMax.x = Mathf.Min(localMax.x, localLimit);
    }

    void ClampLocalMinXToWorldX(ref Vector2 localMin, float worldMinX)
    {
        if (float.IsNegativeInfinity(worldMinX))
            return;

        var localLimit = transform.InverseTransformPoint(new Vector3(worldMinX, transform.position.y, transform.position.z)).x;
        localMin.x = Mathf.Max(localMin.x, localLimit);
    }

    /// <summary>Yerelde taban padding; tek bloklu şekillerde çarpan (görsel boyut aynı).</summary>
    Vector2 GetTouchPaddingHalfExtents()
    {
        float m = touchHitboxPaddingLocal;
        if (_shape != null && _shape.Blocks != null && _shape.Blocks.Count <= 1)
            m *= singleBlockHitboxPaddingMultiplier;
        return new Vector2(m, m);
    }

    void CacheRenderers()
    {
        _renderers = GetComponentsInChildren<SpriteRenderer>(true);
        if (_renderers == null) _renderers = System.Array.Empty<SpriteRenderer>();
        _originalSortingOrders = new int[_renderers.Length];
        _originalSortingLayerIds = new int[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null)
            {
                _originalSortingOrders[i] = 0;
                _originalSortingLayerIds[i] = 0;
                continue;
            }

            _originalSortingOrders[i] = _renderers[i].sortingOrder;
            _originalSortingLayerIds[i] = _renderers[i].sortingLayerID;
        }
    }

    void SetDraggingSorting(bool dragging)
    {
        if (_renderers == null || _originalSortingOrders == null || _originalSortingLayerIds == null || _renderers.Length == 0)
            CacheRenderers();

        var dragLayerId = SortingLayer.NameToID(dragSortingLayerName);
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;
            if (dragging)
            {
                r.sortingLayerID = dragLayerId;
                r.sortingOrder = dragSortingOrder;
            }
            else
            {
                r.sortingLayerID = _originalSortingLayerIds[i];
                r.sortingOrder = _originalSortingOrders[i];
            }
        }
    }
}

