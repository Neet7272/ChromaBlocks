using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TabController : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler
{
    [Header("Refs (TabBackground üstünde durur)")]
    [SerializeField] private RectTransform tabBackground;
    [SerializeField] private RectTransform tabIndicator;
    [SerializeField] private TMP_Text weeklyText;
    [SerializeField] private TMP_Text globalText;

    [Header("Config")]
    [SerializeField, Min(0.05f)] private float snapDuration = 0.18f;
    [SerializeField] private Color selectedColor = new Color32(0x1A, 0x1A, 0x2E, 0xFF);
    [SerializeField] private Color unselectedColor = Color.white;

    public event Action<int> OnTabChanged;

    // 0: Weekly, 1: Global
    public int CurrentTabIndex { get; private set; } = 0;

    private float _leftX;
    private float _rightX;

    private bool _dragging;
    private Vector2 _pointerDownLocal;
    private float _indicatorStartX;

    private Coroutine _snapCo;

    private void Reset()
    {
        tabBackground = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (tabBackground == null) tabBackground = GetComponent<RectTransform>();
        RecalculateTargets();
        SetIndicatorX(CurrentTabIndex == 0 ? _leftX : _rightX, applyVisuals: true);
        ApplyColorsFromIndicator();
    }

    private void OnRectTransformDimensionsChange()
    {
        // Ekran yönü / çözünürlük deðiþince hedefleri güncelle
        RecalculateTargets();
        SetIndicatorX(CurrentTabIndex == 0 ? _leftX : _rightX, applyVisuals: true);
        ApplyColorsFromIndicator();
    }

    private void RecalculateTargets()
    {
        if (tabBackground == null || tabIndicator == null) return;

        // Indicator pivotu 0.5 ise en temiz sonuç
        // Hedefler: arka planýn sol/sað yarýsýnýn merkezi
        float bgW = tabBackground.rect.width;
        float half = bgW * 0.25f; // (-bgW/4) ve (+bgW/4)
        _leftX = -half;
        _rightX = +half;

        // Clamp sýnýrlarý: hedeflerin dýþýna çýkmasýn (tam hedeflere kadar izin)
        // (Ýstersen burada indicator width’e göre ekstra güvenlik payý ekleyebilirsin.)
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!TryLocalPoint(eventData, out var local)) return;

        _dragging = true;
        _pointerDownLocal = local;
        _indicatorStartX = tabIndicator.anchoredPosition.x;

        // “Týklama” davranýþý: basýlan yer orta çizginin saðýnda/solunda ise oraya kay
        int targetTab = local.x >= 0f ? 1 : 0;
        GoToTab(targetTab, animated: true);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging) return;
        if (!TryLocalPoint(eventData, out var local)) return;

        // Drag: indicator sadece X’te parmaðý takip etsin
        float deltaX = local.x - _pointerDownLocal.x;
        float nextX = _indicatorStartX + deltaX;

        nextX = Mathf.Clamp(nextX, _leftX, _rightX);
        SetIndicatorX(nextX, applyVisuals: true);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging) return;
        _dragging = false;

        // Býrakýnca: hangi taraftaysa oraya snap
        float x = tabIndicator.anchoredPosition.x;
        int targetTab = x >= 0f ? 1 : 0;
        GoToTab(targetTab, animated: true);
    }

    public void GoToTab(int tabIndex, bool animated)
    {
        tabIndex = Mathf.Clamp(tabIndex, 0, 1);

        // zaten seçiliyse (drag ile ayný yerdeyse) sadece görseli güncelle
        if (tabIndex == CurrentTabIndex && _snapCo == null && !_dragging)
        {
            ApplyColorsFromIndicator();
            return;
        }

        if (_snapCo != null)
        {
            StopCoroutine(_snapCo);
            _snapCo = null;
        }

        float targetX = tabIndex == 0 ? _leftX : _rightX;

        if (!animated)
        {
            SetIndicatorX(targetX, applyVisuals: true);
            CommitTab(tabIndex);
            return;
        }

        _snapCo = StartCoroutine(SnapTo(targetX, tabIndex));
    }

    private IEnumerator SnapTo(float targetX, int targetTab)
    {
        float startX = tabIndicator.anchoredPosition.x;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, snapDuration);
            float eased = EaseOutCubic(Mathf.Clamp01(t));

            float x = Mathf.Lerp(startX, targetX, eased);
            SetIndicatorX(x, applyVisuals: true);

            yield return null;
        }

        SetIndicatorX(targetX, applyVisuals: true);
        _snapCo = null;

        CommitTab(targetTab);
    }

    private void CommitTab(int tabIndex)
    {
        tabIndex = Mathf.Clamp(tabIndex, 0, 1);

        if (CurrentTabIndex != tabIndex)
        {
            CurrentTabIndex = tabIndex;
            OnTabChanged?.Invoke(CurrentTabIndex);
        }

        ApplyColorsFromIndicator();
    }

    private void SetIndicatorX(float x, bool applyVisuals)
    {
        var pos = tabIndicator.anchoredPosition;
        pos.x = x;
        tabIndicator.anchoredPosition = pos;

        if (applyVisuals)
            ApplyColorsFromIndicator();
    }

    private void ApplyColorsFromIndicator()
    {
        // Indicator pozisyonundan 0..1 arasý lerp deðeri çýkar
        float x = tabIndicator.anchoredPosition.x;
        float u = Mathf.InverseLerp(_leftX, _rightX, x); // 0=Weekly, 1=Global

        // Weekly seçili -> selectedColor, Global -> unselectedColor
        // u arttýkça Weekly beyaza, Global laciverte kayar
        if (weeklyText != null)
            weeklyText.color = Color.Lerp(selectedColor, unselectedColor, u);
        if (globalText != null)
            globalText.color = Color.Lerp(unselectedColor, selectedColor, u);
    }

    private bool TryLocalPoint(PointerEventData e, out Vector2 localPoint)
    {
        localPoint = default;
        if (tabBackground == null) return false;

        // Screen Space Overlay’de camera null olmalý; diðer modlarda eventCamera kullan
        var cam = e.pressEventCamera;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(tabBackground, e.position, cam, out localPoint);
    }

    private static float EaseOutCubic(float x)
    {
        float inv = 1f - x;
        return 1f - inv * inv * inv;
    }
}