using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class LobbyCreateServerCameraSetup
{
    private const string TargetScenePath =
        "Assets/_Project/LobbyMenuAddition/Scenes/Lobby_Menu_Addition_NoOverlap.unity";

    private const string MenuRootName = "Lobby Menu Addition - White Marble Buttons";
    private const string LabelsRootName = "Lobby Menu Addition - Screen Space Labels";
    private const string CreateButtonName = "01 White Marble - Create server";
    private const string TargetName = "Create Server Camera Target (Scene View Capture)";
    private const string ControllerName = "Lobby Create Server Camera Transition";

    static LobbyCreateServerCameraSetup()
    {
        EditorApplication.delayCall += TryAutomaticSetup;
    }

    [MenuItem("Tools/Codex/Capture Create Server Camera Target")]
    private static void SetupFromMenu()
    {
        SetupCurrentScene(true);
    }

    private static void TryAutomaticSetup()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutomaticSetup;
            return;
        }

        SetupCurrentScene(false);
    }

    private static void SetupCurrentScene(bool showDialogs)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.path != TargetScenePath)
        {
            return;
        }

        if (FindComponentInScene<LobbyCreateServerCameraTransition>(activeScene) != null)
        {
            if (showDialogs)
            {
                EditorUtility.DisplayDialog(
                    "Create Server camera",
                    "The camera transition is already configured in this scene.",
                    "OK");
            }

            return;
        }

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null || sceneView.camera == null)
        {
            Debug.LogWarning(
                "Create Server camera setup was not applied because no active Scene view camera was found. " +
                "Use Tools > Codex > Capture Create Server Camera Target while the target view is visible.");
            return;
        }

        Camera mainCamera = FindNamedCamera(activeScene, "Main Camera");
        if (mainCamera == null)
        {
            Debug.LogError("Create Server camera setup could not find Main Camera in the active lobby scene.");
            return;
        }

        Transform menuRoot = mainCamera.transform.Find(MenuRootName);
        Transform createButton = menuRoot != null ? menuRoot.Find(CreateButtonName) : null;
        GameObject labelsRoot = FindRootObject(activeScene, LabelsRootName);

        if (menuRoot == null || createButton == null)
        {
            Debug.LogError("Create Server camera setup could not find the added white-marble menu objects.");
            return;
        }

        PreserveFlippedMarbleDirection(menuRoot);

        GameObject targetObject = new GameObject(TargetName);
        SceneManager.MoveGameObjectToScene(targetObject, activeScene);
        targetObject.transform.SetPositionAndRotation(
            sceneView.camera.transform.position,
            sceneView.camera.transform.rotation);

        GameObject controllerObject = new GameObject(ControllerName);
        SceneManager.MoveGameObjectToScene(controllerObject, activeScene);
        LobbyCreateServerCameraTransition controller =
            controllerObject.AddComponent<LobbyCreateServerCameraTransition>();

        List<GameObject> objectsToHide = new List<GameObject> { menuRoot.gameObject };
        if (labelsRoot != null)
        {
            objectsToHide.Add(labelsRoot);
        }

        controller.Configure(
            mainCamera,
            createButton,
            targetObject.transform,
            objectsToHide.ToArray(),
            sceneView.camera.fieldOfView,
            1.2f);

        EditorUtility.SetDirty(targetObject);
        EditorUtility.SetDirty(controllerObject);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene, TargetScenePath);

        Debug.Log(
            "Create Server camera transition configured from the current Scene view. " +
            "The server-creation UnityEvent is ready for the next connection step.");

        if (showDialogs)
        {
            EditorUtility.DisplayDialog(
                "Create Server camera",
                "Captured the current Scene view and connected the Create server marble.",
                "OK");
        }
    }

    private static void PreserveFlippedMarbleDirection(Transform menuRoot)
    {
        for (int index = 0; index < menuRoot.childCount; index++)
        {
            Transform slot = menuRoot.GetChild(index);
            Transform visual = null;

            foreach (Transform child in slot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith("Lobby_Stadium_Marble_White"))
                {
                    visual = child;
                    break;
                }
            }

            if (visual == null)
            {
                continue;
            }

            visual.localRotation = Quaternion.Euler(0f, 180f, 0f);
            EditorUtility.SetDirty(visual);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
        }
    }

    private static T FindComponentInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
            {
                return component;
            }
        }

        return null;
    }

    private static Camera FindNamedCamera(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera.name == objectName)
                {
                    return camera;
                }
            }
        }

        return null;
    }

    private static GameObject FindRootObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == objectName)
            {
                return root;
            }
        }

        return null;
    }
}
