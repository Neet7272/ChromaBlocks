using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// HUD için hayalet puan akışı + skor metnine juice.
/// </summary>
public sealed class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("HUD")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI floatingScoreText;

    [Header("Hayalet animasyon")]
    [SerializeField, Tooltip("Boşsa Camera.main; 2D tahta başka kameradaysa buraya o kamerayı ver (WorldToScreen yanlışsa hayalet sol alta kaçar).")]
    Camera worldToUiGameCamera;
    [SerializeField, Tooltip("HUD akışında: Inspector konumundan aşağı kaydırma (ekran pikseli).")]
    float hudGhostPopDownPixels = 40f;
    [SerializeField, Tooltip("Pop sırasında yukarı (ekran pikseli).")]
    float hudGhostPopUpPixels = 44f;
    [SerializeField, Tooltip("Kapalı (önerilen): hayalet her zaman Inspector’daki HUD konumundan (ör. Pos X=0, Y=-203) çıkar. Açık: grid/patlamadan gelen dünya noktasından başlar.")]
    bool ghostStartFromWorldEvent;

    Color _baseColor = Color.white;
    readonly Queue<(int added, int newTotal, Vector3? worldSpawn)> _queue = new();
    bool _sequenceRunning;
    Sequence _ghostSequence;

    Vector3 _ghostDesignWorldPos;
    Quaternion _ghostDesignWorldRot;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        KillGhostTweens();
    }

    void Start() => ApplyGhostLayout();

    /// <summary>
    /// Inspector'daki parent / anchor / pozisyonu değiştirmez; sadece rengi ve görünürlüğü hazırlar.
    /// </summary>
    public void ApplyGhostLayout()
    {
        if (scoreText != null)
            _baseColor = scoreText.color;

        if (floatingScoreText == null)
            return;

        var c = floatingScoreText.color;
        c.a = 0f;
        floatingScoreText.color = c;
    }

    public bool HasGhostConfigured =>
        floatingScoreText != null && scoreText != null;

    /// <summary>
    /// Hayalet + akış; ardışık puanlar kuyrukta işlenir.
    /// <paramref name="worldSpawn"/> yalnızca <see cref="ghostStartFromWorldEvent"/> açıksa kullanılır; kapalıysa her zaman HUD’daki tasarım konumundan başlar.
    /// </summary>
    public void AddScoreWithEffect(int addedValue, int newTotal, Vector3? worldSpawn = null)
    {
        if (!HasGhostConfigured)
            return;

        _queue.Enqueue((addedValue, newTotal, worldSpawn));
        if (!_sequenceRunning)
            ProcessQueue();
    }

    void ProcessQueue()
    {
        if (_queue.Count == 0)
        {
            _sequenceRunning = false;
            return;
        }

        _sequenceRunning = true;
        var (added, newTotal, worldSpawn) = _queue.Dequeue();
        if (ghostStartFromWorldEvent && worldSpawn.HasValue)
            RunGhostInfluxFromWorld(added, newTotal, worldSpawn.Value, ProcessQueue);
        else
            RunGhostInfluxHudRelative(added, newTotal, ProcessQueue);
    }

    RectTransform GetUiPlaneRect()
    {
        var ghostRt = floatingScoreText.rectTransform;
        var canvas = ghostRt.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.rootCanvas != null)
        {
            var rootRt = canvas.rootCanvas.transform as RectTransform;
            if (rootRt != null)
                return rootRt;
        }

        if (ghostRt.parent is RectTransform prt)
            return prt;
        return ghostRt;
    }

    Camera GetUiCameraForScreenRay()
    {
        var canvas = floatingScoreText != null ? floatingScoreText.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
            return null;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    Camera GetWorldCamera()
    {
        if (Camera.main != null)
            return Camera.main;
        return FindAnyObjectByType<Camera>();
    }

    Camera GetUiEventCameraForProjection()
    {
        return GetUiCameraForScreenRay();
    }

    /// <summary>Oyun dünyası (3D) noktasını, hayaletin parent UI düzlemine projeler — Overlay için şart.</summary>
    bool TryGameWorldToUiPlaneWorld(Vector3 gameWorld, out Vector3 uiWorld)
    {
        uiWorld = gameWorld;
        var planeRt = GetUiPlaneRect();
        var wCam = worldToUiGameCamera != null ? worldToUiGameCamera : GetWorldCamera();
        if (planeRt == null || wCam == null)
            return false;

        var sp = RectTransformUtility.WorldToScreenPoint(wCam, gameWorld);
        var uiCam = GetUiEventCameraForProjection();
        return RectTransformUtility.ScreenPointToWorldPointInRectangle(planeRt, sp, uiCam, out uiWorld);
    }

    /// <summary>UI elemanının dünya pozisyonundan ekranda kaydırıp tekrar UI düzlemine yansıtır.</summary>
    Vector3 OffsetUiWorldByScreenPixels(Vector3 uiWorld, Vector2 screenPixelDelta)
    {
        var planeRt = GetUiPlaneRect();
        var uiCam = GetUiCameraForScreenRay();
        var sp = RectTransformUtility.WorldToScreenPoint(uiCam, uiWorld);
        sp += screenPixelDelta;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(planeRt, sp, uiCam, out var w))
            return w;
        return uiWorld;
    }

    /// <summary>Patlama/yerleşme noktasından hayalet; bittiğinde Inspector'daki HUD konumuna döner.</summary>
    void RunGhostInfluxFromWorld(int addedValue, int newTotal, Vector3 worldStart, System.Action onDone)
    {
        KillGhostTweens();

        var ghost = floatingScoreText;
        var main = scoreText;

        _ghostDesignWorldPos = ghost.transform.position;
        _ghostDesignWorldRot = ghost.transform.rotation;

        if (!TryGameWorldToUiPlaneWorld(worldStart, out var uiAtGrid))
            uiAtGrid = _ghostDesignWorldPos;

        ghost.transform.position = uiAtGrid;

        var peak = OffsetUiWorldByScreenPixels(uiAtGrid, new Vector2(0f, hudGhostPopUpPixels));

        RunGhostWorldSequence(
            addedValue,
            newTotal,
            peakWorld: peak,
            sinkWorld: main.transform.position,
            worldRestorePos: _ghostDesignWorldPos,
            worldRestoreRot: _ghostDesignWorldRot,
            onDone);
    }

    /// <summary>Inspector'daki HUD konumundan hayalet (worldSpawn yok).</summary>
    void RunGhostInfluxHudRelative(int addedValue, int newTotal, System.Action onDone)
    {
        KillGhostTweens();

        var ghost = floatingScoreText;
        var main = scoreText;

        _ghostDesignWorldPos = ghost.transform.position;
        _ghostDesignWorldRot = ghost.transform.rotation;

        var start = OffsetUiWorldByScreenPixels(_ghostDesignWorldPos, new Vector2(0f, -hudGhostPopDownPixels));
        ghost.transform.position = start;

        var peak = OffsetUiWorldByScreenPixels(_ghostDesignWorldPos, new Vector2(0f, hudGhostPopUpPixels));

        RunGhostWorldSequence(
            addedValue,
            newTotal,
            peakWorld: peak,
            sinkWorld: main.transform.position,
            worldRestorePos: _ghostDesignWorldPos,
            worldRestoreRot: _ghostDesignWorldRot,
            onDone);
    }

    void RunGhostWorldSequence(
        int addedValue,
        int newTotal,
        Vector3 peakWorld,
        Vector3 sinkWorld,
        Vector3 worldRestorePos,
        Quaternion worldRestoreRot,
        System.Action onDone)
    {
        var ghost = floatingScoreText;
        var main = scoreText;

        ghost.text = "+" + addedValue;
        var turq = ScoreVisualJuice.ScoreFlashTurquoise;
        turq.a = 0f;
        ghost.color = turq;

        _ghostSequence = DOTween.Sequence().SetUpdate(true);

        _ghostSequence.Append(
            DOTween.To(() => ghost.color.a, a =>
            {
                var col = ghost.color;
                col.a = a;
                ghost.color = col;
            }, 1f, 0.2f).SetEase(Ease.OutQuad));

        _ghostSequence.Join(ghost.transform.DOMove(peakWorld, 0.2f).SetEase(Ease.OutBack));

        _ghostSequence.AppendInterval(0.2f);

        _ghostSequence.Append(
            DOTween.To(() => ghost.color.a, a =>
            {
                var col = ghost.color;
                col.a = a;
                ghost.color = col;
            }, 0f, 0.2f).SetEase(Ease.InQuad));

        _ghostSequence.Join(ghost.transform.DOMove(sinkWorld, 0.2f).SetEase(Ease.InQuad));

        _ghostSequence.OnComplete(() =>
        {
            if (main != null)
                main.text = newTotal.ToString();

            ScoreVisualJuice.PlayScoreIncreaseJuice(main, addedValue, _baseColor);

            var c = ghost.color;
            c.a = 0f;
            ghost.color = c;
            ghost.transform.SetPositionAndRotation(worldRestorePos, worldRestoreRot);

            _ghostSequence = null;
            onDone?.Invoke();
        });
    }

    void KillGhostTweens()
    {
        _ghostSequence?.Kill();
        _ghostSequence = null;

        if (floatingScoreText != null)
        {
            floatingScoreText.DOKill();
            floatingScoreText.transform.DOKill(true);
        }
    }

    /// <summary>Ana skoru anında yaz (revive / senkron); hayalet tween'lerini kesmez.</summary>
    public void SyncDisplayedScore(int total)
    {
        if (scoreText != null)
            scoreText.text = total.ToString();
    }
}

/// <summary>DOTween punch / renk / shake — HUD ve Game Over ortak.</summary>
public static class ScoreVisualJuice
{
    public static readonly Color ScoreFlashTurquoise = new Color(45f / 255f, 212f / 255f, 191f / 255f, 1f);

    public static void PlayScoreIncreaseJuice(TMP_Text scoreText, int pointsGained, Color restoreColor)
    {
        if (scoreText == null)
            return;

        scoreText.transform.DOKill(true);
        scoreText.DOKill();

        scoreText.transform.DOPunchScale(new Vector3(0.2f, 0.2f, 0f), 0.2f, 5, 0.5f).SetUpdate(true);

        var flash = ScoreFlashTurquoise;
        flash.a = restoreColor.a;
        scoreText.color = flash;
        DOTween.To(() => scoreText.color, c => scoreText.color = c, restoreColor, 0.3f).SetUpdate(true);

        if (pointsGained > 50)
        {
            if (scoreText.transform is RectTransform rt)
                rt.DOShakeAnchorPos(0.25f, new Vector2(5f, 5f), 12, 90f, false, true).SetUpdate(true);
            else
                scoreText.transform.DOShakePosition(0.25f, 0.06f, 10, 90f, false, true).SetUpdate(true);
        }
    }

    public static void PlayScoreCounterCompleteJuice(TMP_Text scoreText, Color restoreColor)
    {
        if (scoreText == null)
            return;

        scoreText.transform.DOKill(true);
        scoreText.DOKill();

        scoreText.transform.DOPunchScale(new Vector3(0.12f, 0.12f, 0f), 0.18f, 4, 0.45f).SetUpdate(true);

        var flash = ScoreFlashTurquoise;
        flash.a = restoreColor.a;
        scoreText.color = flash;
        DOTween.To(() => scoreText.color, c => scoreText.color = c, restoreColor, 0.25f).SetUpdate(true);
    }
}
