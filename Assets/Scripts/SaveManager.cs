using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Tepsi slotu — JsonUtility uyumlu düz alanlar.</summary>
[System.Serializable]
public sealed class TraySlotSave
{
    public bool empty;
    public string shapeName;
    public int rotationQuarters;
    public Color primary;
    public Color secondary;
    public Color tertiary;
}

/// <summary>Kök kayıt paketi (tek JsonUtility kökü — düz alanlar, derin hiyerarşi yok).</summary>
[System.Serializable]
public sealed class SaveGameEnvelope
{
    public int version;
    public int columns;
    public int rows;
    public int score;
    public int comboMultiplier;
    public bool[] gridOccupied;
    public Color[] gridColors;
    public TraySlotSave tray0;
    public TraySlotSave tray1;
    public TraySlotSave tray2;
}

/// <summary>Oyun durumunu persistentDataPath altına JSON yazar; açılışta yükler.</summary>
[DefaultExecutionOrder(100)]
public sealed class SaveManager : MonoBehaviour
{
    public const int CurrentSaveVersion = 1;
    const string FileName = "chromablocks_save.json";

    /// <summary>Ekran görüntüsü / pause flapping: gereksiz ana iş parçacığı yükü.</summary>
    const float MinSecondsBetweenSaveRequests = 2f;

    public static SaveManager Instance { get; private set; }

    static bool _restoredFromSaveThisLoad;

    /// <summary>Awake'te bir kez; OnApplicationPause içinde asla <see cref="Application.persistentDataPath"/> yok.</summary>
    static string s_cachedFullSavePath;

    readonly object _writeLock = new();
    bool _diskWriteInProgress;
    string _queuedJson;
    string _queuedPath;

    /// <summary>Örnek başına gizli yol; <see cref="s_cachedFullSavePath"/> ile aynı içerik.</summary>
    string _saveFileFullPath;

    float _lastSuccessfulSaveRealtime = -9999f;

    [SerializeField] GridManager gridManager;
    [SerializeField] ShapeSpawner shapeSpawner;

    SaveGameEnvelope _pendingEnvelope;

    Coroutine _deferredOpacityRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (shapeSpawner == null)
            shapeSpawner = FindAnyObjectByType<ShapeSpawner>();

        _saveFileFullPath = Path.Combine(Application.persistentDataPath, FileName);
        s_cachedFullSavePath = _saveFileFullPath;

        _pendingEnvelope = TryReadEnvelopeFromDisk(_saveFileFullPath);
    }

    void OnDestroy()
    {
        if (_deferredOpacityRoutine != null)
        {
            StopCoroutine(_deferredOpacityRoutine);
            _deferredOpacityRoutine = null;
        }

        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        if (_pendingEnvelope == null)
            return;

        if (gridManager == null || shapeSpawner == null)
        {
            _pendingEnvelope = null;
            return;
        }

        if (!ValidateEnvelopeAgainstScene(_pendingEnvelope, gridManager))
        {
            _pendingEnvelope = null;
            return;
        }

        if (!gridManager.ApplyGridStateFromSave(
                _pendingEnvelope.columns,
                _pendingEnvelope.rows,
                _pendingEnvelope.gridOccupied,
                _pendingEnvelope.gridColors))
        {
            _pendingEnvelope = null;
            return;
        }

        gridManager.SetScoreAndComboForLoad(_pendingEnvelope.score, _pendingEnvelope.comboMultiplier);

        var tray = new[] { _pendingEnvelope.tray0, _pendingEnvelope.tray1, _pendingEnvelope.tray2 };
        if (!shapeSpawner.RestoreTrayFromSave(tray))
            Debug.LogWarning("[SaveManager] Tepsi kayıttan tam yüklenemedi (pool / prefab kontrolü).", this);

        RefreshGameplayVisualOpacity();

        _pendingEnvelope = null;
        _restoredFromSaveThisLoad = true;
    }

    void OnEnable()
    {
        if (gridManager != null)
            gridManager.OnBoardSettled += HandleBoardSettledSave;
    }

    void OnDisable()
    {
        if (gridManager != null)
            gridManager.OnBoardSettled -= HandleBoardSettledSave;
    }

    void HandleBoardSettledSave() => SaveToDisk();

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            // Kısa aralıklı pause (SS, odak kaybı): ana iş parçacığında JSON üretimini tekrarlamayı kes.
            if (Time.realtimeSinceStartup - _lastSuccessfulSaveRealtime < MinSecondsBetweenSaveRequests)
                return;

            SaveToDisk();
            return;
        }

        ScheduleDeferredRefreshGameplayVisualOpacity();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            ScheduleDeferredRefreshGameplayVisualOpacity();
    }

    void ScheduleDeferredRefreshGameplayVisualOpacity()
    {
        if (_deferredOpacityRoutine != null)
            StopCoroutine(_deferredOpacityRoutine);

        _deferredOpacityRoutine = StartCoroutine(DeferredRefreshGameplayVisualOpacityRoutine());
    }

    /// <summary>Uyanma odağından sonra 2 frame bekle; Main Thread spike'ı azaltır.</summary>
    IEnumerator DeferredRefreshGameplayVisualOpacityRoutine()
    {
        yield return null;
        yield return null;
        _deferredOpacityRoutine = null;
        RefreshGameplayVisualOpacity();
    }

    void RefreshGameplayVisualOpacity()
    {
        if (gridManager != null)
            gridManager.RefreshAllPlacedBlockOpacity();
        if (shapeSpawner != null)
            shapeSpawner.RefreshAllShapesOpacity();
    }

    void OnApplicationQuit()
    {
        SaveToDisk(waitForCompletion: true);
    }

    /// <summary>ShapeSpawner.Start içinde: kayıt uygulandıysa ilk spawn atlanır.</summary>
    public static bool ConsumeRestoredFromSaveFlag()
    {
        if (!_restoredFromSaveThisLoad)
            return false;
        _restoredFromSaveThisLoad = false;
        return true;
    }

    /// <summary>Harici (yeni oyun vb.) çağrılar için kayıt dosyasını siler.</summary>
    public static void DeleteSaveFile()
    {
        var path = s_cachedFullSavePath;
        if (string.IsNullOrEmpty(path))
            path = Path.Combine(Application.persistentDataPath, FileName);

        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch (IOException) { /* yoksay */ }
        }
    }

    /// <summary>Oyun kaydı (JSON) tamamen silinir. Izgara durumu PlayerPrefs'te tutulmuyor.</summary>
    public void ClearSaveData()
    {
        DeleteSaveFile();
    }

    SaveGameEnvelope TryReadEnvelopeFromDisk(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return null;

        try
        {
            var json = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var env = JsonUtility.FromJson<SaveGameEnvelope>(json);
            if (env == null || env.version != CurrentSaveVersion)
                return null;

            env.tray0 ??= new TraySlotSave { empty = true };
            env.tray1 ??= new TraySlotSave { empty = true };
            env.tray2 ??= new TraySlotSave { empty = true };
            return env;
        }
        catch (IOException)
        {
            return null;
        }
    }

    static bool ValidateEnvelopeAgainstScene(SaveGameEnvelope env, GridManager grid)
    {
        if (env == null || grid == null)
            return false;

        int n = env.columns * env.rows;
        if (env.columns < 1 || env.rows < 1 || env.gridOccupied == null || env.gridColors == null)
            return false;
        if (env.gridOccupied.Length != n || env.gridColors.Length != n)
            return false;
        if (env.columns != grid.Columns || env.rows != grid.Rows)
            return false;
        return true;
    }

    /// <summary>
    /// Ana thread: Unity verisini topla + JSON üret (küçük düz paket; arka planda yaz).
    /// </summary>
    void SaveToDisk(bool waitForCompletion = false)
    {
        if (string.IsNullOrEmpty(_saveFileFullPath))
            return;

        if (!TryBuildSaveJson(out var json, out var path))
            return;

        _lastSuccessfulSaveRealtime = Time.realtimeSinceStartup;

        if (waitForCompletion)
        {
            WriteJsonToDisk(path, json);
            return;
        }

        ScheduleAsyncDiskWrite(path, json);
    }

    /// <summary>Unity API — yalnızca ana thread. path = önbelleklenmiş tam dosya yolu.</summary>
    bool TryBuildSaveJson(out string json, out string path)
    {
        json = null;
        path = _saveFileFullPath;

        if (gridManager == null || shapeSpawner == null)
            return false;

        if (shapeSpawner.IsGameOver)
            return false;

        if (gridManager.IsResolvingMatches)
            return false;

        if (gridManager.gridArray == null)
            return false;

        gridManager.BuildFlatGridStateForSave(out var occ, out var cols);

        var env = new SaveGameEnvelope
        {
            version = CurrentSaveVersion,
            columns = gridManager.Columns,
            rows = gridManager.Rows,
            score = gridManager.CurrentScore,
            comboMultiplier = gridManager.CurrentComboMultiplier,
            gridOccupied = occ,
            gridColors = cols,
            tray0 = shapeSpawner.ExportTraySlot(0),
            tray1 = shapeSpawner.ExportTraySlot(1),
            tray2 = shapeSpawner.ExportTraySlot(2)
        };

        try
        {
            json = JsonUtility.ToJson(env);
            return !string.IsNullOrEmpty(json) && !string.IsNullOrEmpty(path);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[SaveManager] JSON oluşturulamadı: " + ex.Message, this);
            return false;
        }
    }

    void ScheduleAsyncDiskWrite(string path, string json)
    {
        lock (_writeLock)
        {
            if (_diskWriteInProgress)
            {
                _queuedPath = path;
                _queuedJson = json;
                return;
            }

            _diskWriteInProgress = true;
        }

        RunBackgroundWriteChain(path, json);
    }

    /// <summary>Worker ipliklerinde yalnızca dosya I/O; Unity API / MonoBehaviour erişimi yok.</summary>
    void RunBackgroundWriteChain(string path, string json)
    {
        Task.Run(() =>
        {
            WriteJsonToDisk(path, json);

            while (true)
            {
                string nextPath = null;
                string nextJson = null;

                lock (_writeLock)
                {
                    _diskWriteInProgress = false;

                    if (!string.IsNullOrEmpty(_queuedJson) && !string.IsNullOrEmpty(_queuedPath))
                    {
                        nextPath = _queuedPath;
                        nextJson = _queuedJson;
                        _queuedPath = null;
                        _queuedJson = null;
                        _diskWriteInProgress = true;
                    }
                }

                if (nextJson == null)
                    break;

                WriteJsonToDisk(nextPath, nextJson);
            }
        });
    }

    static void WriteJsonToDisk(string path, string json)
    {
        try
        {
            File.WriteAllText(path, json);
        }
        catch (IOException ex)
        {
            Debug.LogWarning("[SaveManager] Arka plan kayıt yazılamadı: " + ex.Message);
        }
    }
}
