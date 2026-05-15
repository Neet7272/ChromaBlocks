using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
    [SerializeField, Tooltip("Dünya spawn için; boşsa Camera.main.")]
    Camera worldCamera;
    [SerializeField, Tooltip("Screen Space - Camera: ScreenToWorldPoint için z (dünya spawn; UI için RectTransformUtility kullanılır).")]
    float screenToWorldPlaneZ = 5f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (touchParticlePrefab == null)
            return;

        if (!TryGetPointerDown(out var screen, out var pointerId))
            return;

        var wCam = worldCamera != null ? worldCamera : Camera.main;
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

    static bool TryGraphicRaycastUi(Vector2 screen, int pointerId, out Canvas rootCanvas)
    {
        rootCanvas = null;
        var es = EventSystem.current;
        if (es == null)
            return false;

        var ped = new PointerEventData(es)
        {
            position = screen,
            displayIndex = 0,
            pointerId = pointerId
        };

        var combined = new List<RaycastResult>();
        foreach (var gr in FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Exclude))
        {
            if (gr == null || !gr.isActiveAndEnabled)
                continue;
            var chunk = new List<RaycastResult>();
            gr.Raycast(ped, chunk);
            combined.AddRange(chunk);
        }

        if (combined.Count == 0)
            return false;

        var top = combined[0];
        for (var i = 1; i < combined.Count; i++)
        {
            if (IsRaycastMoreInFront(combined[i], top))
                top = combined[i];
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
        var newFeedback = Instantiate(touchParticlePrefab, spawnPos, Quaternion.identity);
        newFeedback.transform.SetParent(null);
        newFeedback.transform.localScale = Vector3.one;

        var main = newFeedback.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        newFeedback.Play();
        Destroy(newFeedback.gameObject, Mathf.Max(main.duration, 0.05f));
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
            : (target.worldCamera != null ? target.worldCamera : Camera.main);

        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRt, screen, eventCam, out var spawnWorld))
            return false;

        SpawnParticleAtWorldRoot(spawnWorld);
        return true;
    }

    static bool TryGetFrontmostUiCanvas(out Canvas canvas)
    {
        canvas = null;
        var bestScore = int.MinValue;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            if (!c.isRootCanvas || !c.enabled || !c.gameObject.activeInHierarchy)
                continue;
            if (c.renderMode != RenderMode.ScreenSpaceOverlay &&
                c.renderMode != RenderMode.ScreenSpaceCamera)
                continue;

            var score = c.sortingOrder + (c.renderMode == RenderMode.ScreenSpaceOverlay ? 1_000_000 : 0);
            if (score >= bestScore)
            {
                bestScore = score;
                canvas = c.rootCanvas;
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

    public void SetPrefab(ParticleSystem prefab) => touchParticlePrefab = prefab;

    public void SetWorldCamera(Camera cam) => worldCamera = cam;

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
