using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using TMPro;

/// <summary>
/// 8x8 (veya inspector'dan verilen) grid'i üretir ve yönetir.
/// Oluşturulan hücreleri dünya orijinine (0,0) merkezler ve referanslarını 2D dizide saklar.
/// </summary>
public sealed class GridManager : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField, Min(1)] int rows = 8;
    [SerializeField, Min(1)] int columns = 8;

    [Header("Prefab")]
    [SerializeField] GridCell cellPrefab;

    [Header("Layout")]
    [SerializeField, Min(0f)] float spacing = 0f;

    [Header("Generated (Read Only)")]
    [SerializeField] bool generateOnStart = true;

    [Header("Placement Preview")]
    [SerializeField] bool enablePreview = true;
    [SerializeField, Min(0f), Tooltip("Bloğun hücre merkezine ne kadar yaklaşması gerekir (world units). 0 = mesafe kontrolü kapalı.")]
    float maxSnapDistance = 0f;

    [Header("Juice (Game Feel)")]
    [SerializeField] CameraManager cameraManager;
    [Tooltip("3D TextMeshPro içeren prefab; +puan yazısı.")]
    [SerializeField] GameObject floatingScoreWorldPrefab;
    [SerializeField, Min(0.05f)] float floatingRiseY = 1f;
    [SerializeField, Min(0.01f)] float floatingDuration = 0.6f;

    [Header("Yerleştirme / Patlama Juice")]
    [SerializeField, Min(0.05f)] float clearShrinkDuration = 0.25f;
    public ParticleSystem blastParticlePrefab;

    public GridCell[,] gridArray;

    /// <summary>Tahta güncellemesi (2x2 / cascade) bittikten sonra; hamle kontrolü burada yapılmalı.</summary>
    public event System.Action OnBoardSettled;

    public int Rows => rows;
    public int Columns => columns;
    public float Spacing => spacing;
    public Vector2 CellWorldSize => _cellWorldSize;

    /// <summary>2x2 patlama zinciri çalışırken tepsi refill'i vb. bekletilmeli (tahta durumu henüz kesin değil).</summary>
    public bool IsResolvingMatches => _isResolvingMatches;

    ShapeSpawner _shapeSpawner;

    const int MaxShapeBlocks = 25;

    /// <summary>
    /// Monokrom 2x2 sol-alt köşesi (x,y) iken temizlenecek <b>sabit</b> 12 hücre ofsetleri.
    /// Döngü / özyineleme / şablon dışı komşu yok; sadece bu liste + sınır + occupied.
    /// </summary>
    static readonly Vector2Int[] TwoByTwoBombTemplateOffsets =
    {
        // çekirdek (4)
        new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1),
        // sol (2)
        new Vector2Int(-1, 0), new Vector2Int(-1, 1),
        // sağ (2)
        new Vector2Int(2, 0), new Vector2Int(2, 1),
        // alt (2)
        new Vector2Int(0, -1), new Vector2Int(1, -1),
        // üst (2)
        new Vector2Int(0, 2), new Vector2Int(1, 2),
    };

    readonly List<GridCell> _placementTargetCells = new();
    readonly Dictionary<Vector2Int, int> _anchorVotes = new();
    readonly HashSet<GridCell> _placementUsedCells = new();
    readonly HashSet<GridCell> _highlightedCells = new();
    readonly HashSet<GridCell> _previewClearCells = new();
    readonly HashSet<GridCell> _cascadeClearCells = new();
    readonly List<(int x, int y)> _matchCorners = new();

    GridCell[] _nearestCellsBuffer;
    bool[,] _simOccupied;
    Color[,] _simColors;

    /// <summary>Kayıt JSON üretimi için GC azaltma; yalnızca ToJson anında okunur.</summary>
    bool[] _flatSaveOccupied;
    Color[] _flatSaveColors;

    ParticleSystemPool _blastParticlePool;

    Transform _placedBlocksRoot;
    int _score;

    /// <summary>Skor her arttığı toplam değer (2x2 / lazer puanları).</summary>
    public int CurrentScore => _score;

    /// <summary>Kaydet/yükle: tahta sakin iken genelde 0; zincir ortasında anlık değer.</summary>
    public int CurrentComboMultiplier => _comboMultiplier;

    /// <summary>Toplam skor değiştiğinde (yeni toplam parametre olarak).</summary>
    /// <summary>(yeniToplam, bu olayda eklenen puan). Eklenen 0 ise senkron/revive — hayalet efekt yok.</summary>
    /// <summary>(yeniToplam, eklenen, hayalet +X için dünya konumu; null = HUD varsayılanı).</summary>
    public event System.Action<int, int, Vector3?> OnScoreChanged;

    int _comboMultiplier;
    bool _isResolvingMatches;
    Coroutine _cascadeRoutine;

    sealed class TraySnapshot
    {
        public int score;
        public bool[,] occupied;
        public Color[,] colors;
    }

    TraySnapshot _traySnapshot;

    Vector2 _cellWorldSize;
    float _stepX;
    float _stepY;

    /// <summary>Izgaranın yerel XY AABB köşeleri (GridManager transform'ına göre; pivot = transform).</summary>
    Vector2 _gridLocalMin;
    Vector2 _gridLocalMax;
    bool _gridBoundsValid;

    int _previewAnchorX = int.MinValue;
    int _previewAnchorY = int.MinValue;

    void Awake()
    {
        GamePerformanceSettings.Apply();

        if (cameraManager == null)
            cameraManager = FindAnyObjectByType<CameraManager>();
        if (_shapeSpawner == null)
            _shapeSpawner = FindAnyObjectByType<ShapeSpawner>();

        EnsurePlacementScratchBuffers();
        EnsureBlastParticlePool();
    }

    void Start()
    {
        if (generateOnStart)
            GenerateGrid();
        else
            EnsurePlacementScratchBuffers();
    }

    void EnsurePlacementScratchBuffers()
    {
        if (columns < 1 || rows < 1)
            return;

        if (_nearestCellsBuffer == null || _nearestCellsBuffer.Length < MaxShapeBlocks)
            _nearestCellsBuffer = new GridCell[MaxShapeBlocks];

        if (_simOccupied == null || _simOccupied.GetLength(0) != columns || _simOccupied.GetLength(1) != rows)
        {
            _simOccupied = new bool[columns, rows];
            _simColors = new Color[columns, rows];
        }
    }

    void EnsureBlastParticlePool()
    {
        if (blastParticlePrefab == null)
            return;

        if (_blastParticlePool == null)
        {
            var poolGo = new GameObject("BlastParticlePool");
            poolGo.transform.SetParent(transform);
            _blastParticlePool = poolGo.AddComponent<ParticleSystemPool>();
        }

        _blastParticlePool.Initialize(blastParticlePrefab, 12, _blastParticlePool.transform);
    }

    public void GenerateGrid()
    {
        if (cellPrefab == null)
        {
            Debug.LogError("[GridManager] cellPrefab is not assigned.", this);
            return;
        }

        ClearChildren();

        gridArray = new GridCell[columns, rows];

        var origin = transform.position;
        var cellSize = GetCellWorldSize(cellPrefab.gameObject);
        _cellWorldSize = cellSize;
        _stepX = cellSize.x + spacing;
        _stepY = cellSize.y + spacing;

        var totalWidth = (columns * cellSize.x) + ((columns - 1) * spacing);
        var totalHeight = (rows * cellSize.y) + ((rows - 1) * spacing);

        // Grid'i (0,0)'a merkezlemek için başlangıç noktası:
        // En soldaki hücrenin merkezi: -totalWidth/2 + cellWidth/2
        // En alttaki hücrenin merkezi: -totalHeight/2 + cellHeight/2
        var startX = (-totalWidth * 0.5f) + (cellSize.x * 0.5f);
        var startY = (-totalHeight * 0.5f) + (cellSize.y * 0.5f);

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var pos = new Vector3(
                    origin.x + startX + (x * _stepX),
                    origin.y + startY + (y * _stepY),
                    origin.z
                );
                var cell = Instantiate(cellPrefab, pos, Quaternion.identity, transform);
                cell.name = $"Cell_{x}_{y}";
                cell.Init(x, y);
                gridArray[x, y] = cell;
            }
        }

        // Yerleştirilen hücrelerin kapladığı alan (hücre merkezi ± yarım boyut), yerel uzayda.
        _gridLocalMin = new Vector2(
            startX - cellSize.x * 0.5f,
            startY - cellSize.y * 0.5f
        );
        _gridLocalMax = new Vector2(
            startX + (columns - 1) * _stepX + cellSize.x * 0.5f,
            startY + (rows - 1) * _stepY + cellSize.y * 0.5f
        );
        _gridBoundsValid = true;
        EnsurePlacementScratchBuffers();
    }

    /// <summary>
    /// Parmağın dünya pozisyonu ızgara dolgu alanı içinde mi? Spawner / alt alan dışında bırakınca false.
    /// </summary>
    public bool IsPointOverGrid(Vector2 worldPos)
    {
        if (!_gridBoundsValid || gridArray == null)
            return false;

        var local = transform.InverseTransformPoint(new Vector3(worldPos.x, worldPos.y, transform.position.z));
        return local.x >= _gridLocalMin.x && local.x <= _gridLocalMax.x &&
               local.y >= _gridLocalMin.y && local.y <= _gridLocalMax.y;
    }

    /// <summary>
    /// Şeklin herhangi bir blok görselinin (sprite / collider AABB, XY) ızgara alanıyla kesişiyor mu?
    /// Fare hâlâ alt bölgedeyken parça üstte kalınca yerleştirmeyi denemek için.
    /// </summary>
    public bool IsShapeOverlappingGrid(Shape shape)
    {
        if (shape == null || !_gridBoundsValid || gridArray == null)
            return false;

        var blocks = shape.Blocks;
        if (blocks == null || blocks.Count == 0)
            return false;

        GetGridWorldXYBounds(out var gMin, out var gMax);

        for (int i = 0; i < blocks.Count; i++)
        {
            var t = blocks[i].Transform;
            if (t == null) continue;

            if (t.TryGetComponent<SpriteRenderer>(out var sr) && sr.sprite != null)
            {
                var b = sr.bounds;
                if (RectsOverlap2D(new Vector2(b.min.x, b.min.y), new Vector2(b.max.x, b.max.y), gMin, gMax))
                    return true;
            }
            else if (t.TryGetComponent<Collider2D>(out var col))
            {
                var b = col.bounds;
                if (RectsOverlap2D(new Vector2(b.min.x, b.min.y), new Vector2(b.max.x, b.max.y), gMin, gMax))
                    return true;
            }
            else if (IsPointOverGrid(t.position))
                return true;
        }

        return false;
    }

    void GetGridWorldXYBounds(out Vector2 min, out Vector2 max)
    {
        Vector3 p00 = transform.TransformPoint(new Vector3(_gridLocalMin.x, _gridLocalMin.y, 0f));
        Vector3 p10 = transform.TransformPoint(new Vector3(_gridLocalMax.x, _gridLocalMin.y, 0f));
        Vector3 p01 = transform.TransformPoint(new Vector3(_gridLocalMin.x, _gridLocalMax.y, 0f));
        Vector3 p11 = transform.TransformPoint(new Vector3(_gridLocalMax.x, _gridLocalMax.y, 0f));

        float minX = Mathf.Min(p00.x, p10.x, p01.x, p11.x);
        float maxX = Mathf.Max(p00.x, p10.x, p01.x, p11.x);
        float minY = Mathf.Min(p00.y, p10.y, p01.y, p11.y);
        float maxY = Mathf.Max(p00.y, p10.y, p01.y, p11.y);
        min = new Vector2(minX, minY);
        max = new Vector2(maxX, maxY);
    }

    static bool RectsOverlap2D(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
    {
        return aMin.x <= bMax.x && aMax.x >= bMin.x && aMin.y <= bMax.y && aMax.y >= bMin.y;
    }

    public GridCell GetNearestCell(Vector2 worldPos)
    {
        if (gridArray == null) return null;

        GridCell nearest = null;
        var best = float.PositiveInfinity;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var c = gridArray[x, y];
                if (c == null) continue;
                var d = ((Vector2)c.transform.position - worldPos).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = c;
                }
            }
        }

        return nearest;
    }

    public bool CanPlaceShape(Shape shape, out List<GridCell> targetCells, out GridCell anchorCell)
    {
        _placementTargetCells.Clear();
        targetCells = _placementTargetCells;
        anchorCell = null;

        if (shape == null || gridArray == null) return false;

        var blocks = shape.Blocks;
        if (blocks == null || blocks.Count == 0) return false;
        if (blocks.Count > MaxShapeBlocks)
            return false;

        // Rigid yerleşim: Her blok için "en yakın hücre"yi bul,
        // sonra offset'lerine göre olası anchor hücreyi türet ve çoğunluğun anchor'unu seç.
        // Böylece tek bir blok şaşırsa bile şekil bütün olarak doğru yere oturur.
        _anchorVotes.Clear();

        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (b.Transform == null) return false;

            var near = GetNearestCell(b.Transform.position);
            if (near == null) return false;

            // Opsiyonel mesafe kontrolü
            if (maxSnapDistance > 0f)
            {
                var d = Vector2.Distance(near.transform.position, b.Transform.position);
                if (d > maxSnapDistance)
                    return false;
            }

            _nearestCellsBuffer[i] = near;
            var impliedAnchor = new Vector2Int(near.X - b.Offset.x, near.Y - b.Offset.y);
            _anchorVotes.TryGetValue(impliedAnchor, out var cnt);
            _anchorVotes[impliedAnchor] = cnt + 1;
        }

        // En çok oy alan anchor'u seç; eşitlikte toplam hata (distance) küçük olan kazansın.
        Vector2Int bestAnchor = default;
        var bestVotes = -1;
        var bestError = float.PositiveInfinity;

        foreach (var kv in _anchorVotes)
        {
            var a = kv.Key;
            var votes = kv.Value;

            float error = 0f;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                var gx = a.x + b.Offset.x;
                var gy = a.y + b.Offset.y;

                if (gx < 0 || gx >= columns || gy < 0 || gy >= rows)
                {
                    error = float.PositiveInfinity;
                    break;
                }

                var cell = gridArray[gx, gy];
                if (cell == null)
                {
                    error = float.PositiveInfinity;
                    break;
                }

                error += ((Vector2)cell.transform.position - (Vector2)b.Transform.position).sqrMagnitude;
            }

            if (votes > bestVotes || (votes == bestVotes && error < bestError))
            {
                bestVotes = votes;
                bestError = error;
                bestAnchor = a;
            }
        }

        // Seçilen anchor'a göre kesin doğrulama + target hücre listesi (blok sırası ile)
        _placementUsedCells.Clear();
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var gx = bestAnchor.x + b.Offset.x;
            var gy = bestAnchor.y + b.Offset.y;

            if (gx < 0 || gx >= columns || gy < 0 || gy >= rows)
                return false;

            var cell = gridArray[gx, gy];
            if (cell == null) return false;
            if (cell.IsOccupied) return false;
            if (!_placementUsedCells.Add(cell)) return false;

            _placementTargetCells.Add(cell);
        }

        anchorCell = gridArray[bestAnchor.x, bestAnchor.y];
        return anchorCell != null;
    }

    /// <summary>
    /// Spawner şekillerinden biri en az bir boş yere sığabiliyorsa true.
    /// Ofsetler önce Block.Offset (tam sayı); yedek olarak logicalBlocks.
    /// </summary>
    public bool CheckForAvailableMoves(List<Shape> currentShapes)
    {
        if (gridArray == null)
            return true;

        if (currentShapes == null || currentShapes.Count == 0)
            return HasNoOccupiedCells();

        if (HasNoOccupiedCells())
            return true;

        var anyOffsets = false;
        for (int s = 0; s < currentShapes.Count; s++)
        {
            var shape = currentShapes[s];
            if (shape == null) continue;
            if (!TryGetNormalizedLogicalOffsets(shape, out var relOffsets))
                continue;
            anyOffsets = true;
            if (ShapeFitsSomewhereWithOffsets(relOffsets))
                return true;
        }

        if (!anyOffsets)
        {
            Debug.LogWarning("[GridManager] CheckForAvailableMoves: aktif şekillerde blok ofseti yok (ShapeData/prefab?). Yanlış GAME OVER engellendi.", this);
            return true;
        }

        return false;
    }

    bool HasNoOccupiedCells()
    {
        if (gridArray == null)
            return true;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var c = gridArray[x, y];
                if (c != null && c.IsOccupied)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Önce runtime <see cref="Blocks"/> ofsetleri (Rebuild ile aynı kaynak); logicalBlocks yalnızca yedek.
    /// </summary>
    bool TryGetNormalizedLogicalOffsets(Shape shape, out List<Vector2Int> relativeOffsets)
    {
        relativeOffsets = null;
        var blocks = shape.Blocks;
        if (blocks != null && blocks.Count > 0)
        {
            relativeOffsets = new List<Vector2Int>(blocks.Count);
            for (int i = 0; i < blocks.Count; i++)
                relativeOffsets.Add(blocks[i].Offset);
            return true;
        }

        var bits = shape.logicalBlocks;
        if (bits == null || bits.Count == 0)
            return false;

        int minOx = int.MaxValue, minOy = int.MaxValue;
        for (int i = 0; i < bits.Count; i++)
        {
            var p = bits[i];
            if (p.x < minOx) minOx = p.x;
            if (p.y < minOy) minOy = p.y;
        }

        relativeOffsets = new List<Vector2Int>(bits.Count);
        for (int i = 0; i < bits.Count; i++)
        {
            var p = bits[i];
            relativeOffsets.Add(new Vector2Int(p.x - minOx, p.y - minOy));
        }

        return true;
    }

    bool ShapeFitsSomewhereWithOffsets(List<Vector2Int> relativeOffsets)
    {
        for (int ay = 0; ay < rows; ay++)
        {
            for (int ax = 0; ax < columns; ax++)
            {
                if (FitsVirtualPlacementAt(relativeOffsets, ax, ay))
                    return true;
            }
        }

        return false;
    }

    bool FitsVirtualPlacementAt(List<Vector2Int> relativeOffsets, int anchorX, int anchorY)
    {
        var usedCells = new HashSet<Vector2Int>();
        for (int i = 0; i < relativeOffsets.Count; i++)
        {
            var o = relativeOffsets[i];
            int gx = anchorX + o.x;
            int gy = anchorY + o.y;

            if (gx < 0 || gx >= columns || gy < 0 || gy >= rows)
                return false;

            var key = new Vector2Int(gx, gy);
            if (!usedCells.Add(key))
                return false;

            var cell = gridArray[gx, gy];
            if (cell == null || cell.IsOccupied)
                return false;
        }

        return true;
    }

    /// <summary>Şeklin anchor bloğunun altındaki en yakın ızgara hücresi (tek tarama).</summary>
    public bool TryGetShapePreviewAnchorCell(Shape shape, out int anchorX, out int anchorY)
    {
        anchorX = int.MinValue;
        anchorY = int.MinValue;

        if (shape == null || gridArray == null)
            return false;

        if (!shape.TryGetAnchorWorldPosition(out var world))
        {
            var blocks = shape.Blocks;
            if (blocks == null || blocks.Count == 0 || blocks[0].Transform == null)
                return false;
            world = blocks[0].Transform.position;
        }

        var cell = GetNearestCell(world);
        if (cell == null)
            return false;

        anchorX = cell.X;
        anchorY = cell.Y;
        return true;
    }

    public void UpdatePlacementPreview(Shape shape, bool forceRefresh = false)
    {
        if (!enablePreview)
            return;

        if (shape == null)
        {
            ClearPreview();
            return;
        }

        shape.RestoreBlockLayoutTransforms();

        if (!TryGetShapePreviewAnchorCell(shape, out var anchorX, out var anchorY))
        {
            if (_previewAnchorX != int.MinValue)
                ClearPreview();
            return;
        }

        if (!forceRefresh && anchorX == _previewAnchorX && anchorY == _previewAnchorY)
            return;

        _previewAnchorX = anchorX;
        _previewAnchorY = anchorY;

        _previewClearCells.Clear();
        ResetHighlights();

        if (!CanPlaceShape(shape, out var cells, out _))
            return;

        if (TrySimulateClearAfterPlacement(shape, cells) && _previewClearCells.Count > 0)
        {
            // Altın kural: mavi = yalnızca 12'li şablonda VE simülasyonda dolu olan hücreler (_previewClearCells).
            // Yerleşen şeklin şablon dışındaki parçaları asla "patlayacak" mavisiyle boyanmaz.
            foreach (var cell in _previewClearCells)
                HighlightPredictiveClear(cell);

            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c == null || _previewClearCells.Contains(c))
                    continue;
                HighlightPlacementHover(c);
            }

            return;
        }

        for (int i = 0; i < cells.Count; i++)
            HighlightPlacementHover(cells[i]);
    }

    public void ClearPreview()
    {
        _previewAnchorX = int.MinValue;
        _previewAnchorY = int.MinValue;
        _previewClearCells.Clear();
        ResetHighlights();
    }

    public void ResetHighlights()
    {
        foreach (var c in _highlightedCells)
        {
            if (c != null)
                c.ResetHighlight();
        }

        _highlightedCells.Clear();
    }

    void HighlightPlacementHover(GridCell cell)
    {
        if (cell == null)
            return;

        cell.SetPlacementHoverHighlight();
        _highlightedCells.Add(cell);
    }

    void HighlightPredictiveClear(GridCell cell)
    {
        if (cell == null)
            return;

        cell.SetPredictiveClearHighlight();
        _highlightedCells.Add(cell);
    }

    bool TrySimulateClearAfterPlacement(Shape shape, List<GridCell> targetCells)
    {
        _previewClearCells.Clear();
        if (gridArray == null || shape == null || targetCells == null || targetCells.Count == 0)
            return false;

        if (_simOccupied == null || _simColors == null)
            EnsurePlacementScratchBuffers();

        CaptureGridState(_simOccupied, _simColors);

        var blocks = shape.Blocks;
        if (blocks != null && blocks.Count > 0)
        {
            int n = Mathf.Min(blocks.Count, targetCells.Count);
            for (int i = 0; i < n; i++)
            {
                var cell = targetCells[i];
                if (cell == null)
                    continue;

                var b = blocks[i];
                _simOccupied[cell.X, cell.Y] = true;
                _simColors[cell.X, cell.Y] = GetSimulatedBlockColor(b);
            }
        }

        return TryBuild2x2ClearSetFromState(_simOccupied, _simColors, _previewClearCells, out _);
    }

    /// <summary>Önizleme / 2x2 kontrolü: görsel = SpriteRenderer, gerisi BlockInstance.Color.</summary>
    static Color GetSimulatedBlockColor(Shape.BlockInstance b)
    {
        if (b.Transform != null &&
            b.Transform.TryGetComponent<SpriteRenderer>(out var sr) &&
            sr != null)
            return BlockColorUtils.WithOpaqueAlpha(sr.color);

        return BlockColorUtils.WithOpaqueAlpha(b.Color);
    }

    void CaptureGridState(bool[,] occupied, Color[,] colors)
    {
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cell = gridArray[x, y];
                if (cell != null && cell.IsOccupied)
                {
                    occupied[x, y] = true;
                    colors[x, y] = BlockColorUtils.WithOpaqueAlpha(cell.PlacedColor);
                }
                else
                {
                    occupied[x, y] = false;
                    colors[x, y] = default;
                }
            }
        }
    }

    static Vector3 AverageWorldPosition(HashSet<GridCell> cells)
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (var cell in cells)
        {
            if (cell == null) continue;
            sum += cell.transform.position;
            n++;
        }
        return n > 0 ? sum / n : Vector3.zero;
    }

    void SpawnFloatingWorldScore(Vector3 worldPos, int points)
    {
        if (floatingScoreWorldPrefab == null)
            return;

        var go = Instantiate(floatingScoreWorldPrefab, worldPos, Quaternion.identity);
        var tmp = go.GetComponent<TextMeshPro>();
        if (tmp == null)
        {
            Destroy(go);
            return;
        }

        tmp.text = $"+{points}";
        var col = tmp.color;
        tmp.color = new Color(col.r, col.g, col.b, 1f);

        float endY = worldPos.y + floatingRiseY;
        var t = go.transform;
        t.DOKill();

        var seq = DOTween.Sequence().SetUpdate(true);
        seq.Join(t.DOMoveY(endY, floatingDuration).SetEase(Ease.OutQuad));
        seq.Join(DOTween.To(() => tmp.color, c => tmp.color = c, new Color(col.r, col.g, col.b, 0f), floatingDuration));
        seq.OnComplete(() =>
        {
            if (go != null)
                Destroy(go);
        });
    }

    public bool TryPlaceShape(Shape shape)
    {
        if (_isResolvingMatches)
            return false;

        ClearPreview();
        if (!CanPlaceShape(shape, out var cells, out var anchorCell))
            return false;

        var tileCount = shape.GetTileCount();
        Vector3? placementPopupWorld = null;
        if (cells != null && cells.Count > 0)
        {
            Vector3 sum = Vector3.zero;
            var n = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] == null)
                    continue;
                sum += cells[i].transform.position;
                n++;
            }

            if (n > 0)
                placementPopupWorld = sum / n;
        }

        EnsurePlacedBlocksRoot();

        // Blokları koparıp hücrelere taşı
        shape.transform.DOKill();
        var blocks = shape.DetachBlocksForPlacement(_placedBlocksRoot);
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var cell = cells[i];

            if (b.Transform != null && cell != null)
            {
                b.Transform.DOKill();
                NormalizePlacedBlockScale(b.Transform);
                PerfectMagnetSnap(b.Transform, cell.transform);
                BlockColorUtils.EnsureOpaqueBlock(b.Transform, b.Color);
                cell.SetPlacedBlock(b.Transform, BlockColorUtils.WithOpaqueAlpha(b.Color));
                cell.SetPreview(false);
            }
        }

        _shapeSpawner?.NotifyShapeConsumed(shape);
        Destroy(shape.gameObject);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayPlaceSfx();

        if (tileCount > 0)
            AddScore(tileCount, placementPopupWorld);

        StartResolve2x2Cascade();
        return true;
    }

    void NormalizePlacedBlockScale(Transform block)
    {
        if (block == null || _cellWorldSize.x <= 0f || _cellWorldSize.y <= 0f)
            return;

        if (!block.TryGetComponent<SpriteRenderer>(out var sr) || sr.sprite == null)
            return;

        BlockColorUtils.EnsureOpaqueSpriteKeepRgb(sr);

        var sprite = sr.sprite;
        var intrinsic = new Vector2(
            sprite.rect.width / sprite.pixelsPerUnit,
            sprite.rect.height / sprite.pixelsPerUnit);
        if (intrinsic.x <= 0f || intrinsic.y <= 0f)
            return;

        var ps = block.parent != null ? block.parent.lossyScale : Vector3.one;
        var px = Mathf.Max(Mathf.Abs(ps.x), 1e-6f);
        var py = Mathf.Max(Mathf.Abs(ps.y), 1e-6f);

        block.localScale = new Vector3(
            _cellWorldSize.x / (intrinsic.x * px),
            _cellWorldSize.y / (intrinsic.y * py),
            1f);
    }

    static void PerfectMagnetSnap(Transform block, Transform cell)
    {
        // Hedef: blok görsel merkezini hücre görsel merkezine hizala.
        // (Sprite pivotu/parent offseti farklıysa transform.position eşitlemek yetmeyebilir.)
        var cellCenter = GetVisualCenter(cell);
        var blockCenter = GetVisualCenter(block);

        var delta = cellCenter - blockCenter;
        var p = block.position + delta;
        p.z = cell.position.z; // Z'yi de garantiye al
        block.position = p;
    }

    static Vector3 GetVisualCenter(Transform t)
    {
        if (t == null) return Vector3.zero;
        if (t.TryGetComponent<SpriteRenderer>(out var sr) && sr != null && sr.sprite != null)
        {
            // bounds.center bazı projelerde (pivot/PPU/scale) küçük kaymalar üretebiliyor.
            // Pivot'tan "sprite görsel merkezi"ni hesaplamak daha deterministik.
            var sprite = sr.sprite;
            var rect = sprite.rect;
            var pivot = sprite.pivot; // pixels
            var ppu = sprite.pixelsPerUnit;

            // local uzayda: pivot -> rect center ofseti (units)
            var rectCenterPx = new Vector2(rect.width * 0.5f, rect.height * 0.5f);
            var offsetLocal = (rectCenterPx - pivot) / ppu;

            return sr.transform.position + sr.transform.TransformVector(offsetLocal);
        }
        if (t.TryGetComponent<Collider2D>(out var col) && col != null)
            return col.bounds.center;
        return t.position;
    }

    void EnsurePlacedBlocksRoot()
    {
        if (_placedBlocksRoot != null) return;
        var t = transform.Find("PlacedBlocks");
        if (t != null) _placedBlocksRoot = t;
        else
        {
            var go = new GameObject("PlacedBlocks");
            go.transform.SetParent(transform, false);
            _placedBlocksRoot = go.transform;
        }
    }

    void AddScore(int amount, Vector3? scorePopupWorld = null)
    {
        if (amount == 0) return;
        _score += amount;
        OnScoreChanged?.Invoke(_score, amount, scorePopupWorld);
    }

    /// <summary>Alt tepsiye 3 şekil geldiği anda tahta + skor kaydı (revive geri sarma).</summary>
    public void CaptureTraySnapshot()
    {
        if (gridArray == null)
            return;

        var occ = new bool[columns, rows];
        var cols = new Color[columns, rows];

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cell = gridArray[x, y];
                if (cell == null || !cell.IsOccupied)
                    continue;

                occ[x, y] = true;
                cols[x, y] = cell.PlacedColor;
            }
        }

        _traySnapshot = new TraySnapshot
        {
            score = _score,
            occupied = occ,
            colors = cols
        };
    }

    /// <summary>Revive: son tepsi spawn'ından beri yapılan yerleştirmeleri geri al.</summary>
    public void RestoreTraySnapshot()
    {
        if (_traySnapshot == null || gridArray == null)
            return;

        if (_cascadeRoutine != null)
        {
            StopCoroutine(_cascadeRoutine);
            _cascadeRoutine = null;
        }

        _isResolvingMatches = false;
        _comboMultiplier = 0;
        ClearPreview();

        var blockPrefab = ResolveBlockPrefabForRestore();
        EnsurePlacedBlocksRoot();

        if (_placedBlocksRoot != null)
        {
            for (int i = _placedBlocksRoot.childCount - 1; i >= 0; i--)
            {
                var child = _placedBlocksRoot.GetChild(i);
                if (child != null)
                {
                    child.DOKill();
                    Destroy(child.gameObject);
                }
            }
        }

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cell = gridArray[x, y];
                if (cell == null)
                    continue;

                cell.ClearPlacedBlock();

                if (!_traySnapshot.occupied[x, y] || blockPrefab == null)
                    continue;

                var go = Instantiate(blockPrefab, _placedBlocksRoot);
                go.name = $"ReviveBlock_{x}_{y}";

                var placedColor = BlockColorUtils.WithOpaqueAlpha(_traySnapshot.colors[x, y]);
                if (go.TryGetComponent<SpriteRenderer>(out var sr))
                    BlockColorUtils.EnsureOpaqueSprite(sr, placedColor);

                NormalizePlacedBlockScale(go.transform);
                PerfectMagnetSnap(go.transform, cell.transform);
                cell.SetPlacedBlock(go.transform, placedColor);
            }
        }

        _score = _traySnapshot.score;
        OnScoreChanged?.Invoke(_score, 0, null);
        RefreshAllPlacedBlockOpacity();
    }

    /// <summary>Kayıt: düz dizi (sıra: y=0..rows-1, x=0..columns-1 → idx = y * columns + x). Aynı tamponlar tekrar kullanılır.</summary>
    public void BuildFlatGridStateForSave(out bool[] occupied, out Color[] colors)
    {
        int n = columns * rows;
        if (_flatSaveOccupied == null || _flatSaveOccupied.Length != n)
        {
            _flatSaveOccupied = new bool[n];
            _flatSaveColors = new Color[n];
        }

        occupied = _flatSaveOccupied;
        colors = _flatSaveColors;

        if (gridArray == null)
            return;

        int idx = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cell = gridArray[x, y];
                if (cell != null && cell.IsOccupied)
                {
                    occupied[idx] = true;
                    colors[idx] = cell.PlacedColor;
                }
                else
                {
                    occupied[idx] = false;
                    colors[idx] = default;
                }

                idx++;
            }
        }
    }

    /// <summary>Kayıt yükleme: mevcut ızgarayı temizleyip düz diziden blokları yeniden kurar.</summary>
    public bool ApplyGridStateFromSave(int saveColumns, int saveRows, bool[] occupied, Color[] colors)
    {
        if (gridArray == null)
            return false;

        int n = columns * rows;
        if (saveColumns != columns || saveRows != rows || occupied == null || colors == null)
            return false;
        if (occupied.Length != n || colors.Length != n)
            return false;

        if (_cascadeRoutine != null)
        {
            StopCoroutine(_cascadeRoutine);
            _cascadeRoutine = null;
        }

        _isResolvingMatches = false;
        ClearPreview();

        EnsurePlacedBlocksRoot();

        if (_placedBlocksRoot != null)
        {
            for (int i = _placedBlocksRoot.childCount - 1; i >= 0; i--)
            {
                var child = _placedBlocksRoot.GetChild(i);
                if (child != null)
                {
                    child.DOKill();
                    Destroy(child.gameObject);
                }
            }
        }

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cell = gridArray[x, y];
                if (cell != null)
                    cell.ClearPlacedBlock();
            }
        }

        var blockPrefab = ResolveBlockPrefabForRestore();
        if (blockPrefab == null)
            return false;

        int idx = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (!occupied[idx])
                {
                    idx++;
                    continue;
                }

                var cell = gridArray[x, y];
                if (cell == null)
                {
                    idx++;
                    continue;
                }

                var c = BlockColorUtils.WithOpaqueAlpha(colors[idx]);
                var go = Instantiate(blockPrefab, _placedBlocksRoot);
                go.name = $"SaveBlock_{x}_{y}";

                if (go.TryGetComponent<SpriteRenderer>(out var sr))
                    BlockColorUtils.EnsureOpaqueSprite(sr, c);

                NormalizePlacedBlockScale(go.transform);
                PerfectMagnetSnap(go.transform, cell.transform);
                cell.SetPlacedBlock(go.transform, c);
                idx++;
            }
        }

        RefreshAllPlacedBlockOpacity();
        return true;
    }

    /// <summary>Patlama / kombo / pause sonrası ızgaradaki tüm blokları tam opak yap.</summary>
    public void RefreshAllPlacedBlockOpacity()
    {
        if (gridArray == null)
            return;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cell = gridArray[x, y];
                if (cell == null || !cell.IsOccupied)
                    continue;

                var block = cell.PlacedBlock;
                if (block == null)
                    continue;

                var opaque = BlockColorUtils.WithOpaqueAlpha(cell.PlacedColor);
                BlockColorUtils.EnsureOpaqueBlock(block, opaque);
            }
        }
    }

    /// <summary>Kayıt sonrası skor + kombo (HUD senkronu için OnScoreChanged, ek puan 0).</summary>
    public void SetScoreAndComboForLoad(int score, int comboMultiplier)
    {
        _score = Mathf.Max(0, score);
        _comboMultiplier = Mathf.Max(0, comboMultiplier);
        OnScoreChanged?.Invoke(_score, 0, null);
    }

    GameObject ResolveBlockPrefabForRestore()
    {
        if (_shapeSpawner == null)
            _shapeSpawner = FindAnyObjectByType<ShapeSpawner>();
        return _shapeSpawner != null ? _shapeSpawner.SharedBlockPrefab : null;
    }

    void StartResolve2x2Cascade()
    {
        if (_isResolvingMatches)
            return;

        _comboMultiplier = 1;
        _isResolvingMatches = true;
        _cascadeRoutine = StartCoroutine(Resolve2x2CascadeRoutine());
    }

    System.Collections.IEnumerator Resolve2x2CascadeRoutine()
    {
        // Patlama kalmayana kadar devam et
        while (true)
        {
            if (!TryBuild2x2ClearSet(out var isCombo))
                break;

            var fxCenter = AverageWorldPosition(_cascadeClearCells);

            var clearedCount = ClearCellsWithTween(_cascadeClearCells);
            if (clearedCount <= 0)
                break;

            var basePoints = clearedCount * 10;
            var points = basePoints * _comboMultiplier * (isCombo ? 3 : 1);
            AddScore(points, fxCenter);

            if (cameraManager == null)
                cameraManager = FindAnyObjectByType<CameraManager>();
            cameraManager?.ShakeStrong();
            HapticManager.Instance?.PlayHeavyHaptic();
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayClearSfx();

            var sm = ScoreManager.Instance != null ? ScoreManager.Instance : FindAnyObjectByType<ScoreManager>();
            if (sm == null || !sm.HasGhostConfigured)
                SpawnFloatingWorldScore(fxCenter, points);

            _comboMultiplier++;

            // Animasyonların görünmesi için kısa bekleme (scale tween 0.2s)
            yield return new WaitForSeconds(clearShrinkDuration + 0.02f);
        }

        _comboMultiplier = 0;
        _isResolvingMatches = false;
        _cascadeRoutine = null;
        RefreshAllPlacedBlockOpacity();
        ClearPreview();
        OnBoardSettled?.Invoke();
    }

    bool TryBuild2x2ClearSet(out bool isCombo)
    {
        _cascadeClearCells.Clear();
        isCombo = false;

        if (gridArray == null)
            return false;

        if (_simOccupied == null || _simColors == null)
            EnsurePlacementScratchBuffers();

        CaptureGridState(_simOccupied, _simColors);
        return TryBuild2x2ClearSetFromState(_simOccupied, _simColors, _cascadeClearCells, out isCombo);
    }

    /// <summary>
    /// 2x2 yalnızca monokrom tespit edilir. Patlama: <see cref="TwoByTwoBombTemplateOffsets"/> ile
    /// sabit 12 hücre (sınır içi ve occupied). Combo skor çarpanı kalır; ekstra satır/sütun temizliği yok.
    /// </summary>
    bool TryBuild2x2ClearSetFromState(bool[,] occupied, Color[,] colors, HashSet<GridCell> toClear, out bool isCombo)
    {
        toClear.Clear();
        isCombo = false;

        if (gridArray == null || occupied == null || colors == null)
            return false;

        _matchCorners.Clear();
        for (int y = 0; y < rows - 1; y++)
        {
            for (int x = 0; x < columns - 1; x++)
            {
                if (!IsStrictMonochromeTwoByTwo(occupied, colors, x, y))
                    continue;

                _matchCorners.Add((x, y));
            }
        }

        if (_matchCorners.Count == 0)
            return false;

        for (int i = 0; i < _matchCorners.Count; i++)
        {
            var (mx, my) = _matchCorners[i];
            if (!IsStrictMonochromeTwoByTwo(occupied, colors, mx, my))
                continue;

            AddTwoByTwoBombShockwaveOccupied(occupied, toClear, mx, my);
        }

        isCombo = _matchCorners.Count >= 2;
        return toClear.Count > 0;
    }

    /// <summary>
    /// Sabit 12 şablon: sol-alt (mx,my) çekirdek; yalnızca ızgarada ve occupied olanlar eklenir.
    /// </summary>
    void AddTwoByTwoBombShockwaveOccupied(bool[,] occupied, HashSet<GridCell> toClear, int mx, int my)
    {
        for (int i = 0; i < TwoByTwoBombTemplateOffsets.Length; i++)
        {
            var o = TwoByTwoBombTemplateOffsets[i];
            int xx = mx + o.x;
            int yy = my + o.y;

            if (xx < 0 || xx >= columns || yy < 0 || yy >= rows)
                continue;
            if (!occupied[xx, yy])
                continue;

            var cell = gridArray[xx, yy];
            if (cell != null)
                toClear.Add(cell);
        }
    }

    int ClearCellsWithTween(HashSet<GridCell> cells)
    {
        var cleared = 0;
        foreach (var cell in cells)
        {
            if (cell == null) continue;

            var blockedColor = cell.PlacedColor;

            var block = cell.ClearPlacedBlock();
            if (block == null) continue;

            cleared++;

            block.DOKill();
            if (block.TryGetComponent<SpriteRenderer>(out var clearSr))
                BlockColorUtils.EnsureOpaqueSpriteKeepRgb(clearSr);

            block.DOScale(Vector3.zero, clearShrinkDuration)
                .SetEase(Ease.InBack)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (block == null)
                        return;

                    if (_blastParticlePool != null)
                    {
                        var blastColor = blockedColor;
                        _blastParticlePool.PlayAt(block.position, Quaternion.identity, ps =>
                        {
                            var mainModule = ps.main;
                            mainModule.startColor = blastColor;
                        });
                    }

                    Destroy(block.gameObject);
                });
        }

        return cleared;
    }

    static bool IsStrictMonochromeTwoByTwo(bool[,] occupied, Color[,] colors, int x, int y)
    {
        if (occupied == null || colors == null)
            return false;

        int w = occupied.GetLength(0);
        int h = occupied.GetLength(1);
        if (x < 0 || y < 0 || x + 1 >= w || y + 1 >= h)
            return false;

        if (!occupied[x, y] || !occupied[x + 1, y] || !occupied[x, y + 1] || !occupied[x + 1, y + 1])
            return false;

        var c = colors[x, y];
        return SameColor(c, colors[x + 1, y]) &&
               SameColor(c, colors[x, y + 1]) &&
               SameColor(c, colors[x + 1, y + 1]);
    }

    static bool SameColor(Color a, Color b)
    {
        // SpriteRenderer.color float olduğu için Color32 ile birebir kıyas daha stabil.
        var ca = (Color32)a;
        var cb = (Color32)b;
        return ca.r == cb.r && ca.g == cb.g && ca.b == cb.b && ca.a == cb.a;
    }

    static Vector2 GetCellWorldSize(GameObject prefabOrInstance)
    {
        // 2D Sprite için en doğru boyut: SpriteRenderer.bounds.size (world units)
        if (prefabOrInstance.TryGetComponent<SpriteRenderer>(out var sr) && sr.sprite != null)
            return sr.bounds.size;

        // Collider2D varsa, genelde hücre alanını temsil eder.
        if (prefabOrInstance.TryGetComponent<Collider2D>(out var col2d))
            return col2d.bounds.size;

        // UI (RectTransform) ile world-space canvas kullanılıyorsa fallback.
        if (prefabOrInstance.TryGetComponent<RectTransform>(out var rt))
        {
            // RectTransform sizeDelta local ölçüdür; scale'i world'a taşır.
            var lossy = rt.lossyScale;
            return new Vector2(rt.rect.width * lossy.x, rt.rect.height * lossy.y);
        }

        // Son çare: 1x1 kabul et.
        return Vector2.one;
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }
}

