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

    [Header("Global dokunma partikül (bootstrap)")]
    [FormerlySerializedAs("touchFeedbackPrefabBootstrap")]
    [Tooltip("Boşsa GlobalTouchManager oluşturulmaz. Doluysa prefab DDOL manager’a verilir.")]
    public ParticleSystem touchFeedbackPrefab;
    [SerializeField, Tooltip("Boşsa Camera.main.")]
    Camera touchFeedbackCamera;

    Color _hudBaseColor = Color.white;

    [Header("Leaderboard (GameScene içi panel)")]
    [SerializeField] GameObject leaderboardPanel;
    [SerializeField] GameObject leaderboardPanelPrefab;

    [Header("Ayarlar (GameScene)")]
    [SerializeField] GameObject settingsPanel;

    void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (hudScoreText == null)
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                var skor = canvas.transform.Find("Skor");
                if (skor != null)
                    hudScoreText = skor.GetComponent<TextMeshProUGUI>();
            }
        }

        if (leaderboardPanel == null)
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                var t = canvas.transform.Find("LeaderboardPanel");
                if (t != null)
                    leaderboardPanel = t.gameObject;
            }
        }

        if (settingsPanel == null)
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                var t = canvas.transform.Find("SettingsPanel");
                if (t != null)
                    settingsPanel = t.gameObject;
            }
        }
    }

    void EnsureLeaderboardPanelExists()
    {
        if (leaderboardPanel != null)
            return;

        if (leaderboardPanelPrefab == null)
            return;

        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return;

        leaderboardPanel = Instantiate(leaderboardPanelPrefab, canvas.transform);
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

        var sm = ScoreManager.Instance != null ? ScoreManager.Instance : FindAnyObjectByType<ScoreManager>();
        if (sm != null)
        {
            if (sm.scoreText == null)
                sm.scoreText = hudScoreText;
            if (gridManager != null)
                sm.SyncDisplayedScore(gridManager.CurrentScore);
            sm.ApplyGhostLayout();
        }
        else if (hudScoreText != null && gridManager != null)
            hudScoreText.text = gridManager.CurrentScore.ToString();
    }

    void OnScoreChanged(int newTotal, int scoreAdded, System.Nullable<Vector3> scorePopupWorld)
    {
        if (hudScoreText == null || gridManager == null)
            return;

        var sm = ScoreManager.Instance != null ? ScoreManager.Instance : FindAnyObjectByType<ScoreManager>();
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
            Debug.LogError("[UIManager] LeaderboardPanel bulunamadı. ChromaBlocks → Install Leaderboard In Game Scene menüsünü çalıştırın.", this);
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
        if (settingsPanel == null)
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                var t = canvas.transform.Find("SettingsPanel");
                if (t != null)
                    settingsPanel = t.gameObject;
            }
        }

        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }

    /// <summary>Settings panel kapat (X butonu).</summary>
    public void CloseSettings()
    {
        PlayUiClick();
        if (settingsPanel == null)
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                var t = canvas.transform.Find("SettingsPanel");
                if (t != null)
                    settingsPanel = t.gameObject;
            }
        }

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    static void PlayUiClick()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(AudioManager.Instance.uiClickClip);
    }
}
