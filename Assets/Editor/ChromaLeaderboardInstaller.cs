#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Ana menüdeki LeaderboardPanel'i prefab yapar ve GameScene Canvas altına ekler.
/// Unity: ChromaBlocks → Install Leaderboard In Game Scene
/// </summary>
public static class ChromaLeaderboardInstaller
{
    const string MainMenuScene = "Assets/Scenes/main menu.unity";
    const string GameScene = "Assets/Scenes/GameScene.unity";
    const string PrefabPath = "Assets/Prefabs/LeaderboardPanel.prefab";

    [MenuItem("ChromaBlocks/Install Leaderboard In Game Scene")]
    public static void Install()
    {
        if (!System.IO.File.Exists(MainMenuScene) || !System.IO.File.Exists(GameScene))
        {
            Debug.LogError("[ChromaLeaderboardInstaller] Sahne dosyaları bulunamadı.");
            return;
        }

        EditorSceneManager.OpenScene(MainMenuScene, OpenSceneMode.Single);

        var panel = GameObject.Find("LeaderboardPanel");
        if (panel == null)
        {
            Debug.LogError("[ChromaLeaderboardInstaller] main menu'de LeaderboardPanel yok.");
            return;
        }

        var managerGo = GameObject.Find("LeaderboardManager");
        if (managerGo != null)
        {
            var src = managerGo.GetComponent<LeaderboardManager>();
            if (src != null)
            {
                var dst = panel.GetComponent<LeaderboardManager>();
                if (dst == null)
                    dst = panel.AddComponent<LeaderboardManager>();
                EditorUtility.CopySerialized(src, dst);
                Object.DestroyImmediate(managerGo);
            }
        }

        if (panel.GetComponent<LeaderboardPanelController>() == null)
            panel.AddComponent<LeaderboardPanelController>();

        WireCloseButton(panel);

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
            AssetDatabase.DeleteAsset(PrefabPath);

        var prefab = PrefabUtility.SaveAsPrefabAsset(panel, PrefabPath);
        Debug.Log("[ChromaLeaderboardInstaller] Prefab kaydedildi: " + PrefabPath);

        EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);

        var canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[ChromaLeaderboardInstaller] GameScene'de Canvas yok.");
            return;
        }

        var old = canvas.transform.Find("LeaderboardPanel");
        if (old != null)
            Object.DestroyImmediate(old.gameObject);

        var instance = PrefabUtility.InstantiatePrefab(prefab, canvas.transform) as GameObject;
        if (instance == null)
        {
            Debug.LogError("[ChromaLeaderboardInstaller] Prefab instance oluşturulamadı.");
            return;
        }

        instance.name = "LeaderboardPanel";
        instance.SetActive(false);
        instance.transform.SetAsLastSibling();

        var ui = Object.FindAnyObjectByType<UIManager>();
        if (ui != null)
        {
            var so = new SerializedObject(ui);
            so.FindProperty("leaderboardPanel").objectReferenceValue = instance;
            so.FindProperty("leaderboardPanelPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log("[ChromaLeaderboardInstaller] GameScene hazır — LeaderboardPanel Canvas altında (inactive).");
    }

    static void WireCloseButton(GameObject panelRoot)
    {
        var close = panelRoot.transform.Find("CloseButton");
        if (close == null)
        {
            Debug.LogWarning("[ChromaLeaderboardInstaller] CloseButton bulunamadı.");
            return;
        }

        if (!close.TryGetComponent<Button>(out var btn))
            return;

        var controller = panelRoot.GetComponent<LeaderboardPanelController>();
        if (controller == null)
            return;

        while (btn.onClick.GetPersistentEventCount() > 0)
            UnityEventTools.RemovePersistentListener(btn.onClick, 0);

        UnityEventTools.AddPersistentListener(btn.onClick, controller.CloseLeaderboardPanel);
    }
}
#endif
