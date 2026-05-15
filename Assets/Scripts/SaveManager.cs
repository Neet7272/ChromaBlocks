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

/// <summary>Kök kayıt paketi (tek JsonUtility kökü).</summary>
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

    public static SaveManager Instance { get; private set; }

    static bool _restoredFromSaveThisLoad;

    readonly object _writeLock = new();
    bool _diskWriteInProgress;
    string _queuedJson;
    string _queuedPath;

    [SerializeField] GridManager gridManager;
    [SerializeField] ShapeSpawner shapeSpawner;

    SaveGameEnvelope _pendingEnvelope;

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

        _pendingEnvelope = TryReadEnvelopeFromDisk();
    }

    void OnDestroy()
    {
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
            SaveToDisk();
            return;
        }

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
        var path = Path.Combine(Application.persistentDataPath, FileName);
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

    SaveGameEnvelope TryReadEnvelopeFromDisk()
    {
        var path = Path.Combine(Application.persistentDataPath, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
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
    /// Ana thread: Unity verisini topla + JSON üret.
    /// Arka plan: File.WriteAllText (pause / hamle sonrası).
    /// </summary>
    void SaveToDisk(bool waitForCompletion = false)
    {
        if (!TryBuildSaveJson(out var json, out var path))
            return;

        if (waitForCompletion)
        {
            WriteJsonToDisk(path, json);
            return;
        }

        ScheduleAsyncDiskWrite(path, json);
    }

    /// <summary>Unity API — yalnızca ana thread.</summary>
    bool TryBuildSaveJson(out string json, out string path)
    {
        json = null;
        path = null;

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
            path = Path.Combine(Application.persistentDataPath, FileName);
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

        var self = this;
        Task.Run(() =>
        {
            WriteJsonToDisk(path, json);
            if (self != null)
                self.FinishAsyncWriteCycle();
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

    void FinishAsyncWriteCycle()
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
            return;

        Task.Run(() =>
        {
            WriteJsonToDisk(nextPath, nextJson);
            FinishAsyncWriteCycle();
        });
    }
}
