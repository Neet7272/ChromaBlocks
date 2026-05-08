using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using DG.Tweening;

/// <summary>
/// Skor metni, Game Over paneli (fade) ve sahne yeniden yükleme.
/// </summary>
public sealed class UIManager : MonoBehaviour
{
    [Header("Skor")]
    [SerializeField] TextMeshProUGUI scoreText;
    [SerializeField] GridManager gridManager;
    [SerializeField, Min(0.05f)] float scorePunchDuration = 0.25f;
    [SerializeField] Vector3 scorePunchScale = new Vector3(0.18f, 0.18f, 0f);
    [SerializeField, Min(1)] int scorePunchVibrato = 8;
    [SerializeField, Range(0f, 1f)] float scorePunchElasticity = 0.5f;

    [Header("Game Over")]
    [SerializeField] CanvasGroup gameOverPanel;
    [SerializeField] ShapeSpawner shapeSpawner;
    [SerializeField, Min(0.01f)] float gameOverFadeSeconds = 0.5f;

    [Header("İsteğe bağlı: dünya uzayında sürüklemeyi kesin kapat")]
    [SerializeField] Physics2DRaycaster[] disablePhysicsRaycastersOnGameOver;

    void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (shapeSpawner == null)
            shapeSpawner = FindAnyObjectByType<ShapeSpawner>();

        if (gameOverPanel != null)
        {
            gameOverPanel.alpha = 0f;
            gameOverPanel.blocksRaycasts = false;
            gameOverPanel.interactable = false;
        }
    }

    void OnEnable()
    {
        if (gridManager != null)
            gridManager.OnScoreChanged += OnScoreChanged;

        if (shapeSpawner != null)
            shapeSpawner.GameOverStarted += OnGameOverStarted;
    }

    void OnDisable()
    {
        if (gridManager != null)
            gridManager.OnScoreChanged -= OnScoreChanged;

        if (shapeSpawner != null)
            shapeSpawner.GameOverStarted -= OnGameOverStarted;
    }

    void Start()
    {
        RefreshScoreDisplay(true);
    }

    void OnScoreChanged(int newTotal)
    {
        RefreshScoreDisplay(false);
    }

    void RefreshScoreDisplay(bool instant)
    {
        if (scoreText == null || gridManager == null)
            return;

        scoreText.text = gridManager.CurrentScore.ToString();

        if (instant)
            return;

        var t = scoreText.transform;
        t.DOKill();
        t.DOPunchScale(scorePunchScale, scorePunchDuration, scorePunchVibrato, scorePunchElasticity)
            .SetUpdate(true);
    }

    void OnGameOverStarted()
    {
        if (gameOverPanel == null)
            return;

        foreach (var r in disablePhysicsRaycastersOnGameOver)
        {
            if (r != null)
                r.enabled = false;
        }

        gameOverPanel.blocksRaycasts = true;
        gameOverPanel.interactable = true;
        gameOverPanel.DOKill();
        gameOverPanel.alpha = 0f;
        gameOverPanel.DOFade(1f, gameOverFadeSeconds).SetUpdate(true);
    }

    /// <summary>Game Over panelindeki Yeniden Başla butonundan bağlayın.</summary>
    public void RestartGame()
    {
        DOTween.KillAll();
        SceneManager.LoadScene(0);
    }
}
