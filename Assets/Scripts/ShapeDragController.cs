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
    [SerializeField] Vector3 heldScale = new Vector3(1f, 1f, 1f);
    [SerializeField] bool scaleToGridCellSize = true;
    [SerializeField, Tooltip("İstersen hedefi (cell size + spacing) kabul etsin. Genelde kapalı kalmalı.")]
    bool includeSpacingInHeldScale = false;

    [Header("Tween Timings")]
    [SerializeField, Min(0.01f)] float pickupScaleDuration = 0.15f;
    [SerializeField, Range(0f, 1f), Tooltip("0 = anında takip; yüksek = yumuşak Lerp.")]
    float dragFollowLerp = 0.35f;

    [Header("Grid Snapping")]
    [SerializeField] GridManager gridManager;
    [SerializeField] ShapeSpawner shapeSpawner;

    [Header("Sorting")]
    [SerializeField] int dragSortingOrder = 100;
    [SerializeField] string dragSortingLayerName = "Foreground";

    [Header("Depth (Z)")]
    [SerializeField] float dragZ = -1f;

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
        _cam = Camera.main;
        EnsureCollider2D();
        _shape = GetComponent<Shape>();
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (shapeSpawner == null)
            shapeSpawner = FindAnyObjectByType<ShapeSpawner>();
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

        var targetScale = _spawnLocalScale;
        if (scaleToGridCellSize && gridManager != null && _shape != null &&
            _shape.TryGetBlockPrefabWorldSize(out var blockPrefabSize) &&
            gridManager.CellWorldSize.x > 0f && gridManager.CellWorldSize.y > 0f)
        {
            var target = gridManager.CellWorldSize;
            if (includeSpacingInHeldScale)
                target += new Vector2(gridManager.Spacing, gridManager.Spacing);

            var sx = target.x / blockPrefabSize.x;
            var sy = target.y / blockPrefabSize.y;
            var uniform = Mathf.Max(sx, sy);

            // Izgara hücresi boyutu (tepsi 0.6 ile çarpma — yoksa bloklar küçük kalır)
            targetScale = new Vector3(uniform, uniform, 1f);
        }

        _shape?.RestoreBlockLayoutTransforms();

        transform.DOScale(targetScale, pickupScaleDuration)
            .SetEase(Ease.OutQuad)
            .OnUpdate(OnDragScaleTweenUpdate);

        ApplyDragMoveAndPreview(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsInputLocked())
            return;

        _shape?.RestoreBlockLayoutTransforms();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsInputLocked())
            return;

        ApplyDragMoveAndPreview(eventData);
    }

    void ApplyDragMoveAndPreview(PointerEventData eventData)
    {
        StopReturn();

        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

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
            gridManager.UpdatePlacementPreview(_shape);
    }

    void OnDragScaleTweenUpdate()
    {
        _shape?.RestoreBlockLayoutTransforms();
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
            return; // shape sahneden silindi

        if (gridManager != null)
            gridManager.ClearPreview();

        ReturnToStartPosition();
    }

    Vector2 ScreenToPointerWorld(PointerEventData eventData)
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return Vector2.zero;

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
        seq.OnComplete(() => _shape?.RestoreBlockLayoutTransforms());
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
    /// Tüm child SpriteRenderer'ları gezip bounds.Encapsulate ile toplam sınırları bulur,
    /// BoxCollider2D'nin size/offset değerlerini tüm şekli kapsayacak şekilde ayarlar.
    /// </summary>
    public void UpdateColliderBounds()
    {
        EnsureCollider2D();

        var renderers = GetComponentsInChildren<SpriteRenderer>();
        if (renderers == null || renderers.Length == 0)
        {
            _box.size = Vector2.one;
            _box.offset = Vector2.zero;
            return;
        }

        var combined = renderers[0].bounds; // world-space AABB
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);

        // World bounds -> local bounds (min/max köşelerini local'e çevirerek)
        var localMin3 = transform.InverseTransformPoint(combined.min);
        var localMax3 = transform.InverseTransformPoint(combined.max);

        var localMin = new Vector2(Mathf.Min(localMin3.x, localMax3.x), Mathf.Min(localMin3.y, localMax3.y));
        var localMax = new Vector2(Mathf.Max(localMin3.x, localMax3.x), Mathf.Max(localMin3.y, localMax3.y));

        _box.offset = (localMin + localMax) * 0.5f;
        _box.size = (localMax - localMin);
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

