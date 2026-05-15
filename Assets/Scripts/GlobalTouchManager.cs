using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tüm sahnelerde dokunma partikülü. DDOL singleton; UI + dünya; sadece sürüklenebilir şekilde bastırılır.
/// Girdi: yeni Input System (eski Input.GetMouseButtonDown bu projede kullanılamaz).
/// </summary>
public sealed class GlobalTouchManager : MonoBehaviour
{
    public static GlobalTouchManager Instance { get; private set; }

    [Header("Partikül")]
    [SerializeField] ParticleSystem touchParticlePrefab;
    [SerializeField, Tooltip("Dünya spawn için; boşsa bir kez Camera.main cache'lenir.")]
    Camera worldCamera;
    [SerializeField, Tooltip("Screen Space - Camera: ScreenToWorldPoint için z (dünya spawn; UI için RectTransformUtility kullanılır).")]
    float screenToWorldPlaneZ = 5f;
    [SerializeField, Min(1)] int touchPoolPrewarm = 8;

    ParticleSystemPool _touchParticlePool;
    Camera _cachedFallbackCamera;

    GraphicRaycaster[] _graphicRaycasters = System.Array.Empty<GraphicRaycaster>();
    readonly List<Canvas> _uiRootCanvases = new();

    PointerEventData _pointerEventData;
    EventSystem _pointerEventDataOwner;
    readonly List<RaycastResult> _raycastCombined = new();
    readonly List<RaycastResult> _raycastChunk = new();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureTouchParticlePool();
        CacheFallbackCamera();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        RefreshUiRaycastCache();
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshUiRaycastCache();
        CacheFallbackCamera();
    }

    void CacheFallbackCamera()
    {
        _cachedFallbackCamera = worldCamera != null ? worldCamera : Camera.main;
    }

    Camera GetWorldCamera()
    {
        if (worldCamera != null)
            return worldCamera;

        if (_cachedFallbackCamera == null)
            CacheFallbackCamera();

        return _cachedFallbackCamera;
    }

    /// <summary>Sahnedeki GraphicRaycaster ve kök UI canvas listesini yeniler.</summary>
    void RefreshUiRaycastCache()
    {
        _graphicRaycasters = FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Exclude);

        _uiRootCanvases.Clear();
        var allCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude);
        for (int i = 0; i < allCanvases.Length; i++)
        {
            var c = allCanvases[i];
            if (c == null || !c.isRootCanvas || !c.enabled || !c.gameObject.activeInHierarchy)
                continue;
            if (c.renderMode != RenderMode.ScreenSpaceOverlay &&
                c.renderMode != RenderMode.ScreenSpaceCamera)
                continue;

            _uiRootCanvases.Add(c);
        }
    }

    void EnsureTouchParticlePool()
    {
        if (touchParticlePrefab == null)
            return;

        if (_touchParticlePool == null)
            _touchParticlePool = GetComponent<ParticleSystemPool>();

        if (_touchParticlePool == null)
            _touchParticlePool = gameObject.AddComponent<ParticleSystemPool>();

        _touchParticlePool.Initialize(touchParticlePrefab, touchPoolPrewarm, transform);
    }

    void Update()
    {
        if (touchParticlePrefab == null)
            return;

        if (!TryGetPointerDown(out var screen, out var pointerId))
            return;

        var wCam = GetWorldCamera();
        if (wCam != null && RayHitsDraggableShape(wCam, screen))
            return;

        var es = EventSystem.current;
        TryGraphicRaycastUi(screen, pointerId, out var rayCanvas);
        var overUi = es != null && IsPointerOverGameObjectSafe(es, pointerId);

        if (overUi || rayCanvas != null)
        {
            if (TrySpawnOnUiAtScreen(screen, rayCanvas))
                return;
        }

        if (wCam == null)
            return;

        SpawnWorldAtScreen(screen, wCam);
    }

    static bool IsPointerOverGameObjectSafe(EventSystem es, int pointerId)
    {
        try
        {
            if (pointerId >= 0)
                return es.IsPointerOverGameObject(pointerId);
            if (es.IsPointerOverGameObject())
                return true;
            return es.IsPointerOverGameObject(-1);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Fare: -1; dokunmatik: touchId (Input System).</summary>
    static bool TryGetPointerDown(out Vector2 screen, out int pointerId)
    {
        screen = default;
        pointerId = -1;

        var touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
        {
            screen = touchscreen.primaryTouch.position.ReadValue();
            pointerId = touchscreen.primaryTouch.touchId.ReadValue();
            return true;
        }

        var pen = Pen.current;
        if (pen != null && pen.tip.wasPressedThisFrame)
        {
            screen = pen.position.ReadValue();
            pointerId = pen.deviceId;
            return true;
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            screen = mouse.position.ReadValue();
            pointerId = -1;
            return true;
        }

        return false;
    }

    static bool RayHitsDraggableShape(Camera cam, Vector2 screen)
    {
        var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
        var hit = Physics2D.GetRayIntersection(ray, 500f);
        if (hit.collider == null)
            return false;
        return hit.collider.GetComponentInParent<ShapeDragController>() != null;
    }

    void EnsurePointerEventData(EventSystem es)
    {
        if (_pointerEventData == null || _pointerEventDataOwner != es)
        {
            _pointerEventData = new PointerEventData(es);
            _pointerEventDataOwner = es;
        }
    }

    bool TryGraphicRaycastUi(Vector2 screen, int pointerId, out Canvas rootCanvas)
    {
        rootCanvas = null;
        var es = EventSystem.current;
        if (es == null)
            return false;

        if (_graphicRaycasters == null || _graphicRaycasters.Length == 0)
            RefreshUiRaycastCache();

        EnsurePointerEventData(es);
        _pointerEventData.position = screen;
        _pointerEventData.pointerId = pointerId;
        _pointerEventData.displayIndex = 0;

        _raycastCombined.Clear();
        for (int i = 0; i < _graphicRaycasters.Length; i++)
        {
            var gr = _graphicRaycasters[i];
            if (gr == null || !gr.isActiveAndEnabled)
                continue;

            _raycastChunk.Clear();
            gr.Raycast(_pointerEventData, _raycastChunk);
            if (_raycastChunk.Count > 0)
                _raycastCombined.AddRange(_raycastChunk);
        }

        if (_raycastCombined.Count == 0)
            return false;

        var top = _raycastCombined[0];
        for (var i = 1; i < _raycastCombined.Count; i++)
        {
            if (IsRaycastMoreInFront(_raycastCombined[i], top))
                top = _raycastCombined[i];
        }

        rootCanvas = top.gameObject.GetComponentInParent<Canvas>()?.rootCanvas;
        return rootCanvas != null;
    }

    static bool IsRaycastMoreInFront(RaycastResult a, RaycastResult b)
    {
        if (a.sortingLayer != b.sortingLayer)
            return a.sortingLayer > b.sortingLayer;
        if (a.sortingOrder != b.sortingOrder)
            return a.sortingOrder > b.sortingOrder;
        return a.depth > b.depth;
    }

    void SpawnParticleAtWorldRoot(Vector3 spawnPos)
    {
        EnsureTouchParticlePool();
        _touchParticlePool?.PlayAt(spawnPos, Quaternion.identity);
    }

    bool TrySpawnOnUiAtScreen(Vector2 screen, Canvas hintCanvas)
    {
        var target = hintCanvas;
        if (target == null)
            TryGetFrontmostUiCanvas(out target);

        if (target == null)
            return false;

        var canvasRt = target.transform as RectTransform;
        if (canvasRt == null)
            return false;

        var eventCam = target.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : (target.worldCamera != null ? target.worldCamera : GetWorldCamera());

        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRt, screen, eventCam, out var spawnWorld))
            return false;

        SpawnParticleAtWorldRoot(spawnWorld);
        return true;
    }

    bool TryGetFrontmostUiCanvas(out Canvas canvas)
    {
        canvas = null;
        if (_uiRootCanvases.Count == 0)
            RefreshUiRaycastCache();

        var bestScore = int.MinValue;
        for (int i = 0; i < _uiRootCanvases.Count; i++)
        {
            var c = _uiRootCanvases[i];
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                continue;

            var score = c.sortingOrder + (c.renderMode == RenderMode.ScreenSpaceOverlay ? 1_000_000 : 0);
            if (score >= bestScore)
            {
                bestScore = score;
                canvas = c;
            }
        }

        return canvas != null;
    }

    void SpawnWorldAtScreen(Vector2 screen, Camera cam)
    {
        var mousePos = new Vector3(screen.x, screen.y, screenToWorldPlaneZ);
        var spawnPos = cam.ScreenToWorldPoint(mousePos);
        SpawnParticleAtWorldRoot(spawnPos);
    }

    public void SetPrefab(ParticleSystem prefab)
    {
        touchParticlePrefab = prefab;
        EnsureTouchParticlePool();
    }

    public void SetWorldCamera(Camera cam)
    {
        worldCamera = cam;
        CacheFallbackCamera();
    }

    public static GlobalTouchManager EnsureExists(ParticleSystem prefab, Camera worldCam = null)
    {
        if (prefab == null)
            return Instance;

        if (Instance != null)
        {
            Instance.SetPrefab(prefab);
            if (worldCam != null)
                Instance.SetWorldCamera(worldCam);
            return Instance;
        }

        var go = new GameObject("GlobalTouchManager");
        var m = go.AddComponent<GlobalTouchManager>();
        m.SetPrefab(prefab);
        if (worldCam != null)
            m.SetWorldCamera(worldCam);
        return m;
    }
}
