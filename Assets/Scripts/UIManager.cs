using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using DG.Tweening;

/// <summary>
/// Oyun içi HUD skoru + sahne geçişleri (replay, ana menü, leaderboard).
/// </summary>
public sealed class UIManager : MonoBehaviour
{
    [Header("HUD Skor")]
    [SerializeField] TextMeshProUGUI hudScoreText;
    [SerializeField] GridManager gridManager;

    [Header("UI Kök (boşsa Awake'te bir kez Canvas aranır)")]
    [SerializeField] Canvas hudRootCanvas;

    [Header("Global dokunma partikül (bootstrap)")]
    [FormerlySerializedAs("touchFeedbackPrefabBootstrap")]
    [Tooltip("Boşsa GlobalTouchManager oluşturulmaz. Doluysa prefab DDOL manager’a verilir.")]
    public ParticleSystem touchFeedbackPrefab;
    [SerializeField, Tooltip("Boşsa Awake'te Camera.main bir kez cache.")]
    Camera touchFeedbackCamera;

    Color _hudBaseColor = Color.white;

    Canvas _hudCanvas;
    ScoreManager _scoreManager;

    [Header("Leaderboard (GameScene içi panel)")]
    [SerializeField] GameObject leaderboardPanel;
    [SerializeField] GameObject leaderboardPanelPrefab;

    [Header("Ayarlar (GameScene)")]
    [SerializeField] GameObject settingsPanel;

    void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        _hudCanvas = hudRootCanvas != null ? hudRootCanvas : FindAnyObjectByType<Canvas>();

        if (hudScoreText == null && _hudCanvas != null)
        {
            var skor = _hudCanvas.transform.Find("Skor");
            if (skor != null)
                hudScoreText = skor.GetComponent<TextMeshProUGUI>();
        }

        if (leaderboardPanel == null && _hudCanvas != null)
        {
            var t = _hudCanvas.transform.Find("LeaderboardPanel");
            if (t != null)
                leaderboardPanel = t.gameObject;
        }

        if (settingsPanel == null && _hudCanvas != null)
        {
            var t = _hudCanvas.transform.Find("SettingsPanel");
            if (t != null)
                settingsPanel = t.gameObject;
        }
    }

    void EnsureLeaderboardPanelExists()
    {
        if (leaderboardPanel != null)
            return;

        if (leaderboardPanelPrefab == null)
            return;

        if (_hudCanvas == null)
            return;

        leaderboardPanel = Instantiate(leaderboardPanelPrefab, _hudCanvas.transform);
        leaderboardPanel.name = "LeaderboardPanel";
        leaderboardPanel.SetActive(false);
    }

    void OnEnable()
    {
        if (gridManager != null)
            gridManager.OnScoreChanged += OnScoreChanged;
    }

    void OnDisable()
    {
        if (gridManager != null)
            gridManager.OnScoreChanged -= OnScoreChanged;
    }

    void Start()
    {
        GlobalTouchManager.EnsureExists(touchFeedbackPrefab, touchFeedbackCamera);

        if (hudScoreText != null)
            _hudBaseColor = hudScoreText.color;

        _scoreManager = ScoreManager.Instance != null ? ScoreManager.Instance : FindAnyObjectByType<ScoreManager>();
        if (_scoreManager != null)
        {
            if (_scoreManager.scoreText == null)
                _scoreManager.scoreText = hudScoreText;
            if (gridManager != null)
                _scoreManager.SyncDisplayedScore(gridManager.CurrentScore);
            _scoreManager.ApplyGhostLayout();
        }
        else if (hudScoreText != null && gridManager != null)
            hudScoreText.text = gridManager.CurrentScore.ToString();
    }

    void OnScoreChanged(int newTotal, int scoreAdded, System.Nullable<Vector3> scorePopupWorld)
    {
        if (hudScoreText == null || gridManager == null)
            return;

        var sm = _scoreManager != null ? _scoreManager : ScoreManager.Instance;
        var ghostHud = sm != null && sm.HasGhostConfigured && sm.scoreText == hudScoreText;

        if (scoreAdded > 0 && ghostHud)
        {
            sm.AddScoreWithEffect(scoreAdded, newTotal, scorePopupWorld);
            return;
        }

        if (sm != null && sm.scoreText == hudScoreText)
            sm.SyncDisplayedScore(newTotal);
        else
            hudScoreText.text = newTotal.ToString();

        if (scoreAdded > 0)
            ScoreVisualJuice.PlayScoreIncreaseJuice(hudScoreText, scoreAdded, _hudBaseColor);
    }

    /// <summary>Game Over → Tekrar Oyna: mevcut oyun sahnesini yeniden yükler.</summary>
    public void RestartGame()
    {
        DOTween.KillAll();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>Game Over → Ana menü.</summary>
    public void GoToMainMenu()
    {
        DOTween.KillAll();
        SceneManager.LoadScene(0);
    }

    /// <summary>Game Over → Leaderboard: aynı sahnede paneli aç (kapatınca Game Over altta kalır).</summary>
    public void OpenLeaderboard()
    {
        EnsureLeaderboardPanelExists();

        if (leaderboardPanel == null)
        {
            DevelopmentDiagnostics.LogError("[UIManager] LeaderboardPanel bulunamadı. ChromaBlocks → Install Leaderboard In Game Scene menüsünü çalıştırın.", this);
            return;
        }

        leaderboardPanel.transform.SetAsLastSibling();
        leaderboardPanel.SetActive(true);
    }

    public void CloseLeaderboard()
    {
        if (leaderboardPanel != null)
            leaderboardPanel.SetActive(false);
    }

    /// <summary>Oyun içi dişli butonu — UI tıklama sesi + panel aç.</summary>
    public void OpenSettings()
    {
        PlayUiClick();
        ResolveSettingsPanelIfNeeded();

        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }

    /// <summary>Settings panel kapat (X butonu).</summary>
    public void CloseSettings()
    {
        PlayUiClick();
        ResolveSettingsPanelIfNeeded();

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    void ResolveSettingsPanelIfNeeded()
    {
        if (settingsPanel != null || _hudCanvas == null)
            return;

        var t = _hudCanvas.transform.Find("SettingsPanel");
        if (t != null)
            settingsPanel = t.gameObject;
    }

    static void PlayUiClick()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
    }
}
