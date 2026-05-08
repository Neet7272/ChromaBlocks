using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    [Header("Layout")]
    [SerializeField, Min(0f)] float shapeSpacing = 0f;

    [Header("Colors")]
    [SerializeField] List<Color> customColorPalette = new();
    [SerializeField] bool debugModeAllRed = false;

    [Header("Spawn Scale (Bottom Area)")]
    [SerializeField] Vector3 spawnLocalScale = new Vector3(0.6f, 0.6f, 1f);

    [Header("Refill")]
    [SerializeField] bool autoRefillWhenEmpty = true;

    bool _isGameOver;
    public bool IsGameOver => _isGameOver;

    /// <summary>Oyun bittiğinde (UI / Game Over panel) tetiklenir.</summary>
    public event System.Action GameOverStarted;

    Shape _left;
    Shape _middle;
    Shape _right;

    Coroutine _deferredSettleCheck;

    void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
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
        yield return null;

        _deferredSettleCheck = null;

        if (_isGameOver)
            yield break;

        if (gridManager == null)
            yield break;

        var shapes = GetActiveShapes();
        if (!gridManager.CheckForAvailableMoves(shapes))
            EnterGameOver();
    }

    void EnterGameOver()
    {
        if (_isGameOver)
            return;
        _isGameOver = true;
        Debug.Log("GAME OVER - YER KALMADI!");
        GameOverStarted?.Invoke();
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
        SpawnInitialThree();
    }

    void Update()
    {
        if (_isGameOver)
            return;

        if (!autoRefillWhenEmpty)
            return;

        if (gridManager != null && gridManager.IsResolvingMatches)
            return;

        // Unity'de Destroy edilen objeler == null olur.
        if (_left == null && _middle == null && _right == null)
            SpawnInitialThree();
    }

    public void SpawnInitialThree()
    {
        if (_isGameOver)
            return;

        if (shapePrefab == null)
        {
            Debug.LogError("[ShapeSpawner] shapePrefab is not assigned.", this);
            return;
        }

        if (blockPrefab == null)
        {
            Debug.LogError("[ShapeSpawner] blockPrefab is not assigned.", this);
            return;
        }

        if (shapePool == null || shapePool.Count == 0)
        {
            Debug.LogError("[ShapeSpawner] shapePool is empty.", this);
            return;
        }

        if (leftSpawnPoint == null || middleSpawnPoint == null || rightSpawnPoint == null)
        {
            Debug.LogError("[ShapeSpawner] Assign all three spawn points (left/middle/right).", this);
            return;
        }

        DespawnAll();

        _left = SpawnAt(leftSpawnPoint, GetRandomShapeData());
        _middle = SpawnAt(middleSpawnPoint, GetRandomShapeData());
        _right = SpawnAt(rightSpawnPoint, GetRandomShapeData());
    }

    Shape SpawnAt(Transform point, ShapeData data)
    {
        if (data == null || !data.HasAnyBlocks())
        {
            Debug.LogError("[ShapeSpawner] Geçersiz ShapeData (null veya boş). Inspector'daki shape pool'u kontrol et.", this);
            return null;
        }

        var shape = Instantiate(shapePrefab, point.position, point.rotation, point);
        shape.name = $"Shape_{(data != null ? data.ShapeName : "Unknown")}";

        // Alt alana sığması için küçük doğsun.
        shape.transform.localScale = spawnLocalScale;

        // Her spawn öncesi rastgele bir "rol paleti" üret.
        var palette = CreateRandomPalette();

        // Spawner tek yerden yönetebilsin diye blockPrefab + spacing + palette'i buradan geçiriyoruz.
        shape.Init(data, blockPrefab, shapeSpacing, palette);

        // Şekil parent'ında ortalı olduğu için ek bir offset gerekmiyor.
        // İleride şekiller arası mesafe istersen spawnPoint'leri ayırman yeterli.
        return shape;
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

        return new Shape.RolePalette { Primary = a, Secondary = b, Tertiary = c };
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
        if (_left != null) Destroy(_left.gameObject);
        if (_middle != null) Destroy(_middle.gameObject);
        if (_right != null) Destroy(_right.gameObject);
        _left = _middle = _right = null;
    }
}

