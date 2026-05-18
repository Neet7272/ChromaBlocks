using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

[DefaultExecutionOrder(200)]
public sealed class ShapeSpawner : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] GridManager gridManager;

    [Header("Spawn Points")]
    [SerializeField] Transform leftSpawnPoint;
    [SerializeField] Transform middleSpawnPoint;
    [SerializeField] Transform rightSpawnPoint;

    [Header("Pool")]
    [SerializeField] List<ShapeData> shapePool = new();

    [Header("Prefabs")]
    [SerializeField] Shape shapePrefab;
    [SerializeField] GameObject blockPrefab;

    public GameObject SharedBlockPrefab => blockPrefab;
    public Vector3 TrayRestingLocalScale => spawnLocalScale;

    public Transform LeftSpawnPoint => leftSpawnPoint;
    public Transform MiddleSpawnPoint => middleSpawnPoint;
    public Transform RightSpawnPoint => rightSpawnPoint;

    [Header("Layout")]
    [SerializeField, Min(0f)] float shapeSpacing = 0f;

    [Header("Colors")]
    [SerializeField] List<Color> customColorPalette = new();
    [SerializeField] bool debugModeAllRed = false;

    [Header("Spawn Scale (Bottom Area)")]
    [SerializeField] Vector3 spawnLocalScale = new Vector3(0.6f, 0.6f, 1f);

    [Header("Tepsi Giriş Animasyonu")]
    [SerializeField, Min(0f)] float traySpawnStagger = 0.1f;
    [SerializeField, Min(0.05f)] float traySpawnScaleDuration = 0.4f;

    [Header("Refill")]
    [SerializeField] bool autoRefillWhenEmpty = true;

    [Header("Game Over")]
    [SerializeField] GameOverManager gameOverManager;

    bool _isGameOver;
    public bool IsGameOver => _isGameOver;

    /// <summary>Oyun bittiğinde (UI / Game Over panel) tetiklenir.</summary>
    public event System.Action GameOverStarted;

    Shape _left;
    Shape _middle;
    Shape _right;

    Coroutine _deferredSettleCheck;
    float _suppressGameOverCheckUntil;

    void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (gameOverManager == null)
            gameOverManager = FindAnyObjectByType<GameOverManager>(FindObjectsInactive.Include);
    }

    void OnEnable()
    {
        if (gridManager != null)
            gridManager.OnBoardSettled += HandleBoardSettled;
    }

    void OnDisable()
    {
        if (gridManager != null)
            gridManager.OnBoardSettled -= HandleBoardSettled;

        if (_deferredSettleCheck != null)
        {
            StopCoroutine(_deferredSettleCheck);
            _deferredSettleCheck = null;
        }
    }

    void HandleBoardSettled()
    {
        if (_isGameOver)
            return;

        if (gridManager == null)
            return;

        if (_left == null && _middle == null && _right == null && autoRefillWhenEmpty)
            SpawnInitialThree();

        if (_deferredSettleCheck != null)
            StopCoroutine(_deferredSettleCheck);
        _deferredSettleCheck = StartCoroutine(DeferredCheckMovesAfterSettle());
    }

    IEnumerator DeferredCheckMovesAfterSettle()
    {
        if (gridManager != null)
        {
            while (gridManager.IsResolvingMatches)
                yield return null;
        }

        yield return null;

        _deferredSettleCheck = null;

        if (_isGameOver)
            yield break;

        if (Time.realtimeSinceStartup < _suppressGameOverCheckUntil)
            yield break;

        if (gridManager == null)
            yield break;

        var shapes = GetActiveShapes();
        if (!gridManager.CheckForAvailableMoves(shapes))
            EnterGameOver();
    }

    /// <summary>Yerleştirme veya yeni şekil spawn'ından sonra hamle kontrolü planla.</summary>
    public void ScheduleMoveAvailabilityCheck()
    {
        if (_isGameOver)
            return;

        if (_deferredSettleCheck != null)
            StopCoroutine(_deferredSettleCheck);
        _deferredSettleCheck = StartCoroutine(DeferredCheckMovesAfterSettle());
    }

    /// <summary>GridManager şekli yerleştirmeden önce spawner slotunu temizler.</summary>
    public void NotifyShapeConsumed(Shape shape)
    {
        if (shape == null)
            return;

        if (_left == shape) _left = null;
        else if (_middle == shape) _middle = null;
        else if (_right == shape) _right = null;
    }

    void EnterGameOver()
    {
        if (_isGameOver)
            return;
        _isGameOver = true;
        DevelopmentDiagnostics.Log("GAME OVER - YER KALMADI!");

        if (gameOverManager != null)
            gameOverManager.EnterGameOver();
        else
            DevelopmentDiagnostics.LogError("[ShapeSpawner] GameOverManager bulunamadı — panel açılamıyor.", this);

        GameOverStarted?.Invoke();
    }

    public void ClearGameOverState()
    {
        _isGameOver = false;
        _suppressGameOverCheckUntil = 0f;
    }

    public void DestroyCurrentShapes()
    {
        DespawnAll();
    }

    /// <summary>Revive: son tepsi hamlelerini geri sar, en az bir hamlesi olan 3 şekil ver.</summary>
    public void ReviveTray()
    {
        if (_deferredSettleCheck != null)
        {
            StopCoroutine(_deferredSettleCheck);
            _deferredSettleCheck = null;
        }

        if (gridManager != null)
            gridManager.RestoreTraySnapshot();

        _suppressGameOverCheckUntil = Time.realtimeSinceStartup
            + traySpawnScaleDuration + traySpawnStagger * 2f + 0.35f;

        SpawnThreeRandomShapesWithPlayableRetry();
        FinalizeInstantTrayVisuals(_left, _middle, _right);
        CaptureTraySnapshot();
        ScheduleMoveAvailabilityCheck();
    }

    void CaptureTraySnapshot()
    {
        if (gridManager != null)
            gridManager.CaptureTraySnapshot();
    }

    public void SpawnNewShapes()
    {
        if (shapePrefab == null || blockPrefab == null || shapePool == null || shapePool.Count == 0)
            return;

        if (leftSpawnPoint == null || middleSpawnPoint == null || rightSpawnPoint == null)
            return;

        DespawnAll();

        _left = SpawnAt(leftSpawnPoint, GetRandomShapeData());
        _middle = SpawnAt(middleSpawnPoint, GetRandomShapeData());
        _right = SpawnAt(rightSpawnPoint, GetRandomShapeData());

        PlayTraySpawnIntro(_left, _middle, _right);

        CaptureTraySnapshot();
        ScheduleMoveAvailabilityCheck();
    }

    /// <summary>0 = sol, 1 = orta, 2 = sağ — SaveManager için.</summary>
    public TraySlotSave ExportTraySlot(int index)
    {
        var s = index switch
        {
            0 => _left,
            1 => _middle,
            2 => _right,
            _ => null
        };

        return ExportTraySlotFromShape(s);
    }

    static TraySlotSave ExportTraySlotFromShape(Shape s)
    {
        if (s == null || s.Data == null)
            return new TraySlotSave { empty = true };

        var p = s.CurrentPalette;
        return new TraySlotSave
        {
            empty = false,
            shapeName = s.Data.ShapeName,
            rotationQuarters = s.RotationQuarters,
            primary = p.Primary,
            secondary = p.Secondary,
            tertiary = p.Tertiary
        };
    }

    /// <summary>Kayıt dosyasından tepsiyi yeniden kurar (animasyonsuz).</summary>
    public bool RestoreTrayFromSave(TraySlotSave[] slots)
    {
        if (slots == null || slots.Length != 3)
            return false;

        if (shapePrefab == null || blockPrefab == null || shapePool == null || shapePool.Count == 0)
            return false;

        if (leftSpawnPoint == null || middleSpawnPoint == null || rightSpawnPoint == null)
            return false;

        DespawnAll();

        _left = SpawnAtFromSaveSlot(leftSpawnPoint, slots[0]);
        _middle = SpawnAtFromSaveSlot(middleSpawnPoint, slots[1]);
        _right = SpawnAtFromSaveSlot(rightSpawnPoint, slots[2]);

        FinalizeInstantTrayVisuals(_left, _middle, _right);
        RefreshAllShapesOpacity();

        CaptureTraySnapshot();
        ScheduleMoveAvailabilityCheck();
        return true;
    }

    public ShapeData FindShapeDataByName(string shapeName)
    {
        if (string.IsNullOrEmpty(shapeName) || shapePool == null)
            return null;

        for (int i = 0; i < shapePool.Count; i++)
        {
            var d = shapePool[i];
            if (d != null && d.ShapeName == shapeName)
                return d;
        }

        return null;
    }

    public List<Shape> GetActiveShapes()
    {
        var list = new List<Shape>(3);
        if (_left != null) list.Add(_left);
        if (_middle != null) list.Add(_middle);
        if (_right != null) list.Add(_right);
        return list;
    }

    void Start()
    {
        if (SaveManager.ConsumeRestoredFromSaveFlag())
        {
            EnsureTrayShapesVisible();
            StartCoroutine(TrayStartupSafetyRoutine());
            return;
        }

        SpawnInitialThree();
        StartCoroutine(TrayStartupSafetyRoutine());
    }

    /// <summary>Intro tween takılırsa görünürlük düzeltir; yeni şekil spawn etmez.</summary>
    IEnumerator TrayStartupSafetyRoutine()
    {
        yield return null;
        yield return null;
        if (_isGameOver)
            yield break;
        EnsureTrayShapesVisible();
    }

    void Update()
    {
        if (_isGameOver)
            return;

        if (!autoRefillWhenEmpty)
            return;

        if (gridManager != null && gridManager.IsResolvingMatches)
            return;

        if (_left != null || _middle != null || _right != null)
            return;

        SpawnInitialThree();
    }

    public void SpawnInitialThree()
    {
        if (_isGameOver)
            return;

        if (shapePrefab == null)
        {
            DevelopmentDiagnostics.LogError("[ShapeSpawner] shapePrefab is not assigned.", this);
            return;
        }

        if (blockPrefab == null)
        {
            DevelopmentDiagnostics.LogError("[ShapeSpawner] blockPrefab is not assigned.", this);
            return;
        }

        if (shapePool == null || shapePool.Count == 0)
        {
            DevelopmentDiagnostics.LogError("[ShapeSpawner] shapePool is empty.", this);
            return;
        }

        if (leftSpawnPoint == null || middleSpawnPoint == null || rightSpawnPoint == null)
        {
            DevelopmentDiagnostics.LogError("[ShapeSpawner] Assign all three spawn points (left/middle/right).", this);
            return;
        }

        SpawnThreeRandomShapesWithPlayableRetry();
        FinalizeInstantTrayVisuals(_left, _middle, _right);

        CaptureTraySnapshot();
        ScheduleMoveAvailabilityCheck();
    }

    /// <summary>Yalnızca scale 0 kalan şekilleri düzeltir; boş slota yeni şekil koymaz.</summary>
    public void EnsureTrayShapesVisible()
    {
        if (_isGameOver)
            return;

        EnsureTrayShapeVisible(_left);
        EnsureTrayShapeVisible(_middle);
        EnsureTrayShapeVisible(_right);
    }

    void EnsureTrayShapeVisible(Shape slot)
    {
        if (slot == null)
            return;

        var t = slot.transform;
        if (t == null)
            return;

        t.DOKill();
        if (t.localScale.sqrMagnitude < spawnLocalScale.x * spawnLocalScale.x * 0.04f)
            t.localScale = spawnLocalScale;
    }

    /// <summary>Rastgele 3 şekil; en az biri tahtaya sığana kadar sınırsız dene (donma önlemi: güvenlik tavanı).</summary>
    void SpawnThreeRandomShapesWithPlayableRetry()
    {
        if (leftSpawnPoint == null || middleSpawnPoint == null || rightSpawnPoint == null)
            return;

        DespawnAll();

        const int safetyCap = 100;
        for (int attempt = 0; attempt < safetyCap; attempt++)
        {
            if (attempt > 0)
                DespawnAll();

            _left = SpawnAt(leftSpawnPoint, GetRandomShapeData());
            _middle = SpawnAt(middleSpawnPoint, GetRandomShapeData());
            _right = SpawnAt(rightSpawnPoint, GetRandomShapeData());

            if (gridManager == null)
                return;

            if (gridManager.CheckForAvailableMoves(GetActiveShapes()))
                return;
        }

        DevelopmentDiagnostics.LogWarning(
            "[ShapeSpawner] Oynanabilir tepsi bulunamadı (tahta dolu olabilir); son set bırakıldı.", this);
    }

    Shape SpawnAt(Transform point, ShapeData data)
    {
        return SpawnAtInternal(point, data, false, default, null, playIntro: true);
    }

    Shape SpawnAtFromSaveSlot(Transform point, TraySlotSave slot)
    {
        if (slot.empty)
            return null;

        var data = FindShapeDataByName(slot.shapeName);
        if (data == null || !data.HasAnyBlocks())
            return null;

        var palette = new Shape.RolePalette
        {
            Primary = BlockColorUtils.WithOpaqueAlpha(slot.primary),
            Secondary = BlockColorUtils.WithOpaqueAlpha(slot.secondary),
            Tertiary = BlockColorUtils.WithOpaqueAlpha(slot.tertiary)
        };

        int rot = (slot.rotationQuarters % 4 + 4) % 4;
        return SpawnAtInternal(point, data, true, palette, rot, playIntro: false);
    }

    /// <summary>forcedPalette yalnızca useForcedPalette true iken kullanılır.</summary>
    Shape SpawnAtInternal(Transform point, ShapeData data, bool useForcedPalette, Shape.RolePalette forcedPalette, int? rotationQuarters, bool playIntro)
    {
        if (data == null || !data.HasAnyBlocks())
        {
            DevelopmentDiagnostics.LogError("[ShapeSpawner] Geçersiz ShapeData (null veya boş). Inspector'daki shape pool'u kontrol et.", this);
            return null;
        }

        var shape = Instantiate(shapePrefab, point.position, point.rotation, point);
        shape.name = $"Shape_{data.ShapeName}";

        shape.transform.localScale = spawnLocalScale;

        var palette = useForcedPalette ? forcedPalette : CreateRandomPalette();
        shape.Init(data, blockPrefab, shapeSpacing, palette, rotationQuarters);

        if (playIntro)
            shape.transform.localScale = Vector3.zero;
        else
        {
            shape.transform.localScale = spawnLocalScale;
            if (shape.TryGetComponent<ShapeDragController>(out var drag))
                drag.UpdateColliderBounds();
        }

        return shape;
    }

    void FinalizeInstantTrayVisuals(Shape a, Shape b, Shape c)
    {
        FinalizeOne(a, spawnLocalScale);
        FinalizeOne(b, spawnLocalScale);
        FinalizeOne(c, spawnLocalScale);
    }

    static void FinalizeOne(Shape shape, Vector3 restingScale)
    {
        if (shape == null)
            return;

        var t = shape.transform;
        t.DOKill();
        if (t.localScale.sqrMagnitude < restingScale.x * restingScale.x * 0.04f)
            t.localScale = restingScale;
        shape.EnsureBlocksFullyOpaque();
        if (shape.TryGetComponent<ShapeDragController>(out var drag))
            drag.UpdateColliderBounds();
    }

    /// <summary>Pause / kombo sonrası tepsi şekillerinin opaklığını düzelt.</summary>
    public void RefreshAllShapesOpacity()
    {
        RefreshShapeOpacity(_left);
        RefreshShapeOpacity(_middle);
        RefreshShapeOpacity(_right);
    }

    static void RefreshShapeOpacity(Shape shape)
    {
        if (shape != null)
            shape.EnsureBlocksFullyOpaque();
    }

    void PlayTraySpawnIntro(params Shape[] shapes)
    {
        for (int i = 0; i < shapes.Length; i++)
        {
            var shape = shapes[i];
            if (shape == null)
                continue;

            var t = shape.transform;
            t.DOKill();
            t.localScale = Vector3.zero;
            t.DOScale(spawnLocalScale, traySpawnScaleDuration)
                .SetEase(Ease.OutBack)
                .SetDelay(i * traySpawnStagger)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (shape == null)
                        return;
                    shape.EnsureBlocksFullyOpaque();
                    if (shape.TryGetComponent<ShapeDragController>(out var drag))
                        drag.UpdateColliderBounds();
                });
        }
    }

    Shape.RolePalette CreateRandomPalette()
    {
        if (debugModeAllRed)
        {
            return new Shape.RolePalette
            {
                Primary = Color.red,
                Secondary = Color.red,
                Tertiary = Color.red
            };
        }

        // Inspector'daki palette'den tekrarsız seçmeye çalışır.
        // En az 2 renk: Primary+Secondary garanti. Tertiary yoksa Secondary ile aynı yapılır.
        if (customColorPalette == null || customColorPalette.Count == 0)
        {
            // Güvenli fallback
            return new Shape.RolePalette
            {
                Primary = Color.red,
                Secondary = Color.yellow,
                Tertiary = Color.blue
            };
        }

        var a = customColorPalette[Random.Range(0, customColorPalette.Count)];
        var b = a;
        if (customColorPalette.Count >= 2)
        {
            int guard = 0;
            while (b == a && guard++ < 20)
                b = customColorPalette[Random.Range(0, customColorPalette.Count)];
        }

        var c = a;
        if (customColorPalette.Count >= 3)
        {
            int guard = 0;
            while ((c == a || c == b) && guard++ < 30)
                c = customColorPalette[Random.Range(0, customColorPalette.Count)];
        }
        else
        {
            c = b;
        }

        return new Shape.RolePalette
        {
            Primary = BlockColorUtils.WithOpaqueAlpha(a),
            Secondary = BlockColorUtils.WithOpaqueAlpha(b),
            Tertiary = BlockColorUtils.WithOpaqueAlpha(c)
        };
    }

    ShapeData GetRandomShapeData()
    {
        if (shapePool == null || shapePool.Count == 0)
            return null;

        for (int g = 0; g < 48; g++)
        {
            var d = shapePool[Random.Range(0, shapePool.Count)];
            if (d != null && d.HasAnyBlocks())
                return d;
        }

        return null;
    }

    void DespawnAll()
    {
        KillShapeTween(_left);
        KillShapeTween(_middle);
        KillShapeTween(_right);

        if (_left != null) Destroy(_left.gameObject);
        if (_middle != null) Destroy(_middle.gameObject);
        if (_right != null) Destroy(_right.gameObject);
        _left = _middle = _right = null;
    }

    static void KillShapeTween(Shape shape)
    {
        if (shape != null)
            shape.transform.DOKill();
    }
}

