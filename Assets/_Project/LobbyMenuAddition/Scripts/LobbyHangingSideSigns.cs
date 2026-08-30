using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LobbyHangingSideSigns : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private GameObject menuRoot;
    [SerializeField] private GameObject whiteSignMeshPrefab;
    [SerializeField] private GameObject blackSignMeshPrefab;
    [SerializeField] private Material whiteMarbleMaterial;
    [SerializeField] private Material blackMarbleMaterial;
    [SerializeField] private LobbyCreateServerCameraTransition cameraTransition;
    [SerializeField] private LobbyCreateServerDepthOfField depthOfFieldController;
    [SerializeField] private LobbyCreateServerKingDrop kingDropController;
    [SerializeField] private Vector3 whiteSignPosition = new(0.34f, 1.04f, 0.4f);
    [SerializeField] private Vector3 blackSignPosition = new(0.34f, 1.04f, -0.4f);
    [SerializeField] private Vector3 signSize = new(0.045f, 0.14f, 0.44f);
    [SerializeField, Min(0.05f)] private float ropeLength = 0.38f;
    [SerializeField, Min(0.1f)] private float clickImpulse = 3.6f;

    private readonly HashSet<Rigidbody> signBodies = new();
    private readonly Dictionary<Rigidbody, bool> whiteSignByBody = new();
    private readonly Dictionary<Transform, Vector3> kingRestPositions = new();
    private readonly List<RopeVisual> ropes = new();
    private readonly List<Material> sceneSignMaterials = new();
    private GameObject runtimeRoot;
    private Material ropeMaterial;
    private bool signsAreVisible;
    private bool isReturningToMainMenu;
    private bool whiteSelectionLocked;
    private Transform selectedKing;
    private Transform lockedWhiteKing;
    private Coroutine kingSelectionRoutine;
    private Coroutine revealSignsRoutine;
    private Coroutine joinedServerSelectionRoutine;
    private int selectionRequestId;
    private RectTransform backButtonRect;

    private sealed class RopeVisual
    {
        public LineRenderer Line;
        public Transform Anchor;
        public Transform Sign;
        public Rigidbody SignBody;
        public float RestLength;
        public Vector3 SignAttachment;
        public Quaternion RestRotation;
    }

    private void Awake()
    {
        CreateSigns();
        signsAreVisible = false;
        runtimeRoot.SetActive(false);

        if (menuRoot == null || !menuRoot.activeInHierarchy)
        {
            SetKingsActive(true);
            revealSignsRoutine = StartCoroutine(RevealSignsAfterKingsLand());
        }
    }

    private void Update()
    {
        if (isReturningToMainMenu)
        {
            if (menuRoot != null && menuRoot.activeInHierarchy)
            {
                isReturningToMainMenu = false;
            }

            return;
        }

        if (!signsAreVisible && revealSignsRoutine == null &&
            menuRoot != null && !menuRoot.activeInHierarchy)
        {
            SetKingsActive(true);
            revealSignsRoutine = StartCoroutine(RevealSignsAfterKingsLand());
        }

        if (!signsAreVisible || targetCamera == null || Mouse.current == null ||
            !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        Vector2 pointerPosition = Mouse.current.position.ReadValue();
        if (backButtonRect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(
                backButtonRect,
                pointerPosition))
        {
            ReturnToMainMenu();
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        Rigidbody hitBody = hit.rigidbody;
        if (hitBody == null || !signBodies.Contains(hitBody))
        {
            return;
        }

        hitBody.WakeUp();
        hitBody.AddForceAtPosition(
            ray.direction.normalized * clickImpulse,
            hit.point,
            ForceMode.Impulse);

        if (whiteSignByBody.TryGetValue(hitBody, out bool isWhiteSign))
        {
            if (isWhiteSign && whiteSelectionLocked)
            {
                return;
            }

            BeginKingSelection(isWhiteSign);
        }
    }

    public void ConfigureJoinedServerWithWhiteHost()
    {
        whiteSelectionLocked = true;
        if (joinedServerSelectionRoutine != null)
        {
            StopCoroutine(joinedServerSelectionRoutine);
        }

        joinedServerSelectionRoutine = StartCoroutine(LockWhiteWhenReady());
    }

    private void BeginKingSelection(bool selectWhiteKing)
    {
        selectionRequestId++;
        if (kingSelectionRoutine != null)
        {
            StopCoroutine(kingSelectionRoutine);
        }

        kingSelectionRoutine = StartCoroutine(
            SelectKingWhenReady(selectWhiteKing, selectionRequestId));
    }

    private void LateUpdate()
    {
        if (!signsAreVisible)
        {
            return;
        }

        foreach (RopeVisual rope in ropes)
        {
            if (rope.Line == null || rope.Anchor == null || rope.Sign == null)
            {
                continue;
            }

            Vector3 start = rope.Anchor.position;
            Vector3 end = rope.Sign.TransformPoint(rope.SignAttachment);
            float slack = Mathf.Max(0f, rope.RestLength - Vector3.Distance(start, end));
            Vector3 control = (start + end) * 0.5f +
                Vector3.down * Mathf.Clamp(0.008f + slack * 0.7f, 0.008f, 0.09f);
            if (rope.SignBody != null)
            {
                control -= rope.SignBody.linearVelocity * 0.012f;
            }

            for (int index = 0; index < rope.Line.positionCount; index++)
            {
                float progress = index / (float)(rope.Line.positionCount - 1);
                float inverse = 1f - progress;
                Vector3 point = inverse * inverse * start +
                    2f * inverse * progress * control +
                    progress * progress * end;
                rope.Line.SetPosition(index, point);
            }
        }
    }

    private void CreateSigns()
    {
        runtimeRoot = new GameObject("Lobby Hanging Side Signs (Scene Only)");
        runtimeRoot.transform.SetParent(transform, false);
        CreateBackButton();

        Shader ropeShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (ropeShader == null)
        {
            ropeShader = Shader.Find("Sprites/Default");
        }

        ropeMaterial = new Material(ropeShader)
        {
            name = "Lobby Hanging Sign Rope (Runtime)",
            color = new Color(0.12f, 0.075f, 0.035f, 1f),
        };

        Material whiteToonMaterial = CreateToonMarbleMaterial(
            whiteMarbleMaterial,
            "White");
        Material blackToonMaterial = CreateToonMarbleMaterial(
            blackMarbleMaterial,
            "Black");

        CreateSign("White", "WHITE", whiteSignPosition, whiteSignMeshPrefab,
            whiteToonMaterial, new Color(0.09f, 0.08f, 0.065f, 1f), -9f, 0.08f);
        CreateSign("Black", "BLACK", blackSignPosition, blackSignMeshPrefab,
            blackToonMaterial, new Color(0.92f, 0.9f, 0.82f, 1f), 9f, -0.08f);
    }

    private void CreateBackButton()
    {
        GameObject canvasObject = new("Lobby Server Back Button Canvas (Scene Only)");
        canvasObject.transform.SetParent(runtimeRoot.transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject buttonObject = new("Main Menu Back Button");
        buttonObject.transform.SetParent(canvasObject.transform, false);
        backButtonRect = buttonObject.AddComponent<RectTransform>();
        backButtonRect.anchorMin = new Vector2(0f, 1f);
        backButtonRect.anchorMax = new Vector2(0f, 1f);
        backButtonRect.pivot = new Vector2(0f, 1f);
        backButtonRect.anchoredPosition = new Vector2(26f, -24f);
        backButtonRect.sizeDelta = new Vector2(300f, 92f);

        Image background = buttonObject.AddComponent<Image>();
        background.color = new Color(0.16f, 0.18f, 0.2f, 0.9f);
        background.raycastTarget = false;

        Outline border = buttonObject.AddComponent<Outline>();
        border.effectColor = new Color(0.72f, 0.73f, 0.72f, 0.9f);
        border.effectDistance = new Vector2(3f, -3f);

        GameObject textObject = new("Main Menu Back Button Label");
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.06f, 0.1f);
        textRect.anchorMax = new Vector2(0.94f, 0.9f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text label = textObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = "MAIN MENU";
        label.alignment = TextAnchor.MiddleCenter;
        label.fontStyle = FontStyle.Bold;
        label.fontSize = 42;
        label.color = new Color(0.76f, 0.76f, 0.73f, 1f);
        label.raycastTarget = false;

        Shadow insetShadow = textObject.AddComponent<Shadow>();
        insetShadow.effectColor = new Color(0.02f, 0.02f, 0.025f, 0.95f);
        insetShadow.effectDistance = new Vector2(-2f, 2f);
    }

    private void ReturnToMainMenu()
    {
        if (cameraTransition == null || !cameraTransition.ReturnToMainMenu())
        {
            return;
        }

        selectionRequestId++;
        if (kingSelectionRoutine != null)
        {
            StopCoroutine(kingSelectionRoutine);
            kingSelectionRoutine = null;
        }

        if (revealSignsRoutine != null)
        {
            StopCoroutine(revealSignsRoutine);
            revealSignsRoutine = null;
        }

        if (joinedServerSelectionRoutine != null)
        {
            StopCoroutine(joinedServerSelectionRoutine);
            joinedServerSelectionRoutine = null;
        }

        whiteSelectionLocked = false;
        if (lockedWhiteKing != null)
        {
            if (kingRestPositions.TryGetValue(
                    lockedWhiteKing,
                    out Vector3 lockedRestPosition))
            {
                lockedWhiteKing.position = lockedRestPosition;
            }

            SetKingOutline(lockedWhiteKing, false);
            lockedWhiteKing = null;
        }

        if (selectedKing != null)
        {
            if (kingRestPositions.TryGetValue(selectedKing, out Vector3 restPosition))
            {
                selectedKing.position = restPosition;
            }

            SetKingOutline(selectedKing, false);
            selectedKing = null;
        }

        if (kingDropController != null)
        {
            kingDropController.ResetKingsForMainMenu();
        }
        else
        {
            SetKingsActive(false);
        }
        depthOfFieldController?.DeactivateBackgroundBlur();
        isReturningToMainMenu = true;
        signsAreVisible = false;
        runtimeRoot.SetActive(false);
    }

    private IEnumerator LockWhiteWhenReady()
    {
        float timeoutAt = Time.unscaledTime + 3f;
        Transform whiteKing = null;

        while (Time.unscaledTime < timeoutAt)
        {
            whiteKing = FindSceneObject("Lobby White King Drop");
            if (whiteKing != null && kingDropController != null &&
                kingDropController.KingsHaveSettled)
            {
                break;
            }

            yield return null;
        }

        if (whiteKing == null || !whiteSelectionLocked)
        {
            joinedServerSelectionRoutine = null;
            yield break;
        }

        if (!kingRestPositions.TryGetValue(whiteKing, out Vector3 whiteRestPosition))
        {
            whiteRestPosition = new Vector3(
                whiteKing.position.x,
                0.5f,
                whiteKing.position.z);
            kingRestPositions[whiteKing] = whiteRestPosition;
        }

        lockedWhiteKing = whiteKing;
        lockedWhiteKing.position = whiteRestPosition + Vector3.up * 0.13f;
        SetKingOutline(
            lockedWhiteKing,
            true,
            new Color(0.72f, 0.25f, 0.2f, 1f),
            0.0045f);

        joinedServerSelectionRoutine = null;
    }

    private IEnumerator RevealSignsAfterKingsLand()
    {
        while (true)
        {
            if (isReturningToMainMenu ||
                (menuRoot != null && menuRoot.activeInHierarchy))
            {
                revealSignsRoutine = null;
                yield break;
            }

            if (kingDropController != null && kingDropController.KingsHaveSettled)
            {
                break;
            }

            if (kingDropController == null)
            {
                Transform whiteKing = FindSceneObject("Lobby White King Drop");
                Transform blackKing = FindSceneObject("Lobby Black King Drop");
                if (whiteKing != null && blackKing != null &&
                    whiteKing.position.y <= 0.53f &&
                    blackKing.position.y <= 0.53f)
                {
                    break;
                }
            }

            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.16f);

        if (isReturningToMainMenu ||
            (menuRoot != null && menuRoot.activeInHierarchy))
        {
            revealSignsRoutine = null;
            yield break;
        }

        foreach (RopeVisual rope in ropes)
        {
            if (rope.SignBody == null || rope.Anchor == null)
            {
                continue;
            }

            Vector3 dropPosition = rope.Anchor.position + Vector3.up * 0.26f;
            rope.Sign.SetPositionAndRotation(dropPosition, rope.RestRotation);
            rope.SignBody.position = dropPosition;
            rope.SignBody.rotation = rope.RestRotation;
            rope.SignBody.linearVelocity = Vector3.zero;
            rope.SignBody.angularVelocity = Vector3.zero;
        }

        runtimeRoot.SetActive(true);
        foreach (RopeVisual rope in ropes)
        {
            rope.SignBody?.WakeUp();
        }

        signsAreVisible = true;
        revealSignsRoutine = null;
        StartGentleSway();
    }

    private void SetKingsActive(bool isActive)
    {
        Transform whiteKing = FindSceneObject("Lobby White King Drop");
        Transform blackKing = FindSceneObject("Lobby Black King Drop");
        if (whiteKing != null)
        {
            whiteKing.gameObject.SetActive(isActive);
        }

        if (blackKing != null)
        {
            blackKing.gameObject.SetActive(isActive);
        }
    }

    private void CreateSign(
        string signName,
        string labelText,
        Vector3 signPosition,
        GameObject signMeshPrefab,
        Material marbleMaterial,
        Color labelColor,
        float inwardYaw,
        float initialTorque)
    {
        GameObject anchorObject = new($"Lobby {signName} Sign Rope Anchor");
        anchorObject.transform.SetParent(runtimeRoot.transform, false);
        anchorObject.transform.position = signPosition +
            Vector3.up * (signSize.y * 0.5f + ropeLength);

        Rigidbody anchorBody = anchorObject.AddComponent<Rigidbody>();
        anchorBody.isKinematic = true;
        anchorBody.useGravity = false;

        GameObject signObject = new($"Lobby {signName} Hanging Sign");
        signObject.transform.SetParent(runtimeRoot.transform, false);
        signObject.transform.position = signPosition;
        signObject.transform.rotation = Quaternion.Euler(0f, inwardYaw, 0f);

        Rigidbody signBody = signObject.AddComponent<Rigidbody>();
        signBody.mass = 1.35f;
        signBody.linearDamping = 0.22f;
        signBody.angularDamping = 0.65f;
        signBody.interpolation = RigidbodyInterpolation.Interpolate;
        signBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        signBodies.Add(signBody);
        whiteSignByBody[signBody] = signName == "White";

        BoxCollider signCollider = signObject.AddComponent<BoxCollider>();
        signCollider.size = signSize;

        GameObject visual = signMeshPrefab != null
            ? Instantiate(signMeshPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = $"{signName} Lobby Stadium Marble Sign Visual";
        visual.transform.SetParent(signObject.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        visual.transform.localScale = Vector3.one;

        FitVisualInsideSign(visual);

        foreach (Collider visualCollider in visual.GetComponentsInChildren<Collider>(true))
        {
            Destroy(visualCollider);
        }

        foreach (MeshRenderer visualRenderer in
                 visual.GetComponentsInChildren<MeshRenderer>(true))
        {
            visualRenderer.sharedMaterial = marbleMaterial;
        }

        CreateLabel(signObject.transform, labelText, labelColor);

        ConfigurableJoint joint = signObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = anchorBody;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = new Vector3(0f, signSize.y * 0.5f, 0f);
        joint.connectedAnchor = Vector3.zero;
        joint.xMotion = ConfigurableJointMotion.Limited;
        joint.yMotion = ConfigurableJointMotion.Limited;
        joint.zMotion = ConfigurableJointMotion.Limited;
        joint.linearLimit = new SoftJointLimit
        {
            limit = ropeLength,
            contactDistance = 0.003f,
        };
        joint.linearLimitSpring = new SoftJointLimitSpring
        {
            spring = 520f,
            damper = 34f,
        };
        joint.angularXMotion = ConfigurableJointMotion.Free;
        joint.angularYMotion = ConfigurableJointMotion.Free;
        joint.angularZMotion = ConfigurableJointMotion.Free;
        joint.projectionMode = JointProjectionMode.None;
        joint.projectionDistance = 0.025f;

        GameObject ropeObject = new($"Lobby {signName} Sign Rope");
        ropeObject.transform.SetParent(runtimeRoot.transform, false);
        LineRenderer line = ropeObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 8;
        line.startWidth = 0.018f;
        line.endWidth = 0.018f;
        line.numCapVertices = 5;
        line.sharedMaterial = ropeMaterial;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        ropes.Add(new RopeVisual
        {
            Line = line,
            Anchor = anchorObject.transform,
            Sign = signObject.transform,
            SignBody = signBody,
            RestLength = ropeLength,
            SignAttachment = new Vector3(0f, signSize.y * 0.5f, 0f),
            RestRotation = signObject.transform.rotation,
        });

        signBody.AddTorque(new Vector3(0f, initialTorque * 0.35f, initialTorque),
            ForceMode.Impulse);
    }

    private Material CreateToonMarbleMaterial(Material sourceMaterial, string signName)
    {
        if (sourceMaterial == null)
        {
            return null;
        }

        Shader toonShader = Shader.Find("Toon/Toon");
        if (toonShader == null)
        {
            return sourceMaterial;
        }

        Material toonMaterial = new(toonShader)
        {
            name = $"Lobby {signName} Marble Toon (Runtime)",
        };

        Texture marbleTexture = sourceMaterial.HasProperty("_BaseMap")
            ? sourceMaterial.GetTexture("_BaseMap")
            : sourceMaterial.mainTexture;
        Color baseColor = sourceMaterial.HasProperty("_BaseColor")
            ? sourceMaterial.GetColor("_BaseColor")
            : sourceMaterial.color;

        if (toonMaterial.HasProperty("_MainTex"))
        {
            toonMaterial.SetTexture("_MainTex", marbleTexture);
        }

        if (toonMaterial.HasProperty("_BaseMap"))
        {
            toonMaterial.SetTexture("_BaseMap", marbleTexture);
        }

        if (toonMaterial.HasProperty("_BaseColor"))
        {
            toonMaterial.SetColor("_BaseColor", baseColor);
        }

        bool usePureBlackShade = signName == "Black";
        Color firstShadeColor = usePureBlackShade
            ? Color.black
            : baseColor * 0.68f;
        Color secondShadeColor = usePureBlackShade
            ? Color.black
            : baseColor * 0.42f;

        if (toonMaterial.HasProperty("_1st_ShadeColor"))
        {
            toonMaterial.SetColor("_1st_ShadeColor", firstShadeColor);
        }

        if (toonMaterial.HasProperty("_2nd_ShadeColor"))
        {
            toonMaterial.SetColor("_2nd_ShadeColor", secondShadeColor);
        }

        if (toonMaterial.HasProperty("_Outline_Width"))
        {
            toonMaterial.SetFloat("_Outline_Width", 0.003f);
        }

        toonMaterial.EnableKeyword("_IS_CLIPPING_OFF");
        toonMaterial.EnableKeyword("_OUTLINE_NML");
        sceneSignMaterials.Add(toonMaterial);
        return toonMaterial;
    }

    private IEnumerator SelectKingWhenReady(bool selectWhiteKing, int requestId)
    {
        string kingName = selectWhiteKing
            ? "Lobby White King Drop"
            : "Lobby Black King Drop";
        Transform targetKing = null;
        float timeoutAt = Time.unscaledTime + 3f;

        while (Time.unscaledTime < timeoutAt && requestId == selectionRequestId)
        {
            targetKing = FindSceneObject(kingName);
            bool kingsAreReady = kingDropController == null ||
                kingDropController.KingsHaveSettled;
            if (targetKing != null && kingsAreReady &&
                targetKing.position.y <= 0.53f)
            {
                break;
            }

            yield return null;
        }

        if (targetKing == null || requestId != selectionRequestId)
        {
            kingSelectionRoutine = null;
            yield break;
        }

        if (!kingRestPositions.TryGetValue(targetKing, out Vector3 targetRestPosition))
        {
            targetRestPosition = new Vector3(
                targetKing.position.x,
                0.5f,
                targetKing.position.z);
            kingRestPositions[targetKing] = targetRestPosition;
        }

        Transform previousKing = selectedKing;
        Vector3 previousStart = Vector3.zero;
        Vector3 previousRest = Vector3.zero;
        if (previousKing != null && previousKing != targetKing)
        {
            previousStart = previousKing.position;
            if (!kingRestPositions.TryGetValue(previousKing, out previousRest))
            {
                previousRest = new Vector3(
                    previousKing.position.x,
                    0.5f,
                    previousKing.position.z);
                kingRestPositions[previousKing] = previousRest;
            }

            SetKingOutline(previousKing, false);
        }

        selectedKing = targetKing;
        SetKingOutline(targetKing, true);

        Vector3 targetStart = targetKing.position;
        Vector3 targetRaisedPosition = targetRestPosition + Vector3.up * 0.13f;
        const float raiseDuration = 0.34f;
        float elapsed = 0f;

        while (elapsed < raiseDuration && requestId == selectionRequestId)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / raiseDuration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            targetKing.position = Vector3.LerpUnclamped(
                targetStart,
                targetRaisedPosition,
                eased);

            if (previousKing != null && previousKing != targetKing)
            {
                previousKing.position = Vector3.LerpUnclamped(
                    previousStart,
                    previousRest,
                    eased);
            }

            yield return null;
        }

        if (requestId == selectionRequestId)
        {
            targetKing.position = targetRaisedPosition;
            if (previousKing != null && previousKing != targetKing)
            {
                previousKing.position = previousRest;
            }
        }

        kingSelectionRoutine = null;
    }

    private Transform FindSceneObject(string objectName)
    {
        foreach (GameObject rootObject in gameObject.scene.GetRootGameObjects())
        {
            foreach (Transform candidate in
                     rootObject.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == objectName)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void SetKingOutline(
        Transform king,
        bool isHighlighted,
        Color? outlineColor = null,
        float outlineWidth = 0.006f)
    {
        Color highlightColor = outlineColor ??
            new Color(1f, 0.72f, 0.12f, 1f);

        foreach (Renderer targetRenderer in
                 king.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in targetRenderer.sharedMaterials)
            {
                if (material == null ||
                    !material.HasProperty("_Outline_Width"))
                {
                    continue;
                }

                bool isKingTestMaterial = material.name.Contains("KingTest");

                if (!isKingTestMaterial)
                {
                    material.SetShaderPassEnabled("Outline", isHighlighted);
                    material.SetShaderPassEnabled(
                        "SRPDefaultUnlit",
                        isHighlighted);
                }

                material.SetFloat(
                    "_Outline_Width",
                    isHighlighted ? outlineWidth : 0f);

                if (material.HasProperty("_OutlineVisible"))
                {
                    material.SetFloat("_OutlineVisible", isHighlighted ? 1f : 0f);
                }

                if (!isKingTestMaterial &&
                    material.HasProperty("_SPRDefaultUnlitColorMask"))
                {
                    material.SetFloat(
                        "_SPRDefaultUnlitColorMask",
                        isHighlighted ? 15f : 0f);
                }

                if (material.HasProperty("_Outline_Color"))
                {
                    material.SetColor("_Outline_Color", highlightColor);
                }

                if (isHighlighted)
                {
                    material.EnableKeyword("_OUTLINE_NML");
                }
                else
                {
                    material.DisableKeyword("_OUTLINE_NML");
                    material.DisableKeyword("_OUTLINE_POS");
                }
            }
        }
    }

    private void FitVisualInsideSign(GameObject visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            visual.transform.localScale = signSize;
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        Vector3 boundsSize = bounds.size;
        visual.transform.localScale = new Vector3(
            signSize.z / Mathf.Max(boundsSize.z, 0.0001f),
            signSize.y / Mathf.Max(boundsSize.y, 0.0001f),
            signSize.x / Mathf.Max(boundsSize.x, 0.0001f));

        bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        visual.transform.position -= bounds.center - visual.transform.parent.position;
    }

    private void CreateLabel(Transform signTransform, string text, Color color)
    {
        GameObject labelObject = new($"{text} Label");
        labelObject.transform.SetParent(signTransform, false);
        labelObject.transform.localPosition = new Vector3(
            -signSize.x * 0.5f - 0.004f,
            0f,
            0f);
        labelObject.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = text;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 64;
        label.characterSize = 0.016f;
        label.fontStyle = FontStyle.Bold;
        label.color = color;

        MeshRenderer labelRenderer = labelObject.GetComponent<MeshRenderer>();
        labelRenderer.shadowCastingMode = ShadowCastingMode.Off;
        labelRenderer.receiveShadows = false;
    }

    private void StartGentleSway()
    {
        float direction = 1f;
        foreach (Rigidbody body in signBodies)
        {
            body.WakeUp();
            body.AddTorque(new Vector3(0.015f, 0.01f, 0.055f * direction),
                ForceMode.Impulse);
            direction = -direction;
        }
    }

    private void OnDestroy()
    {
        if (ropeMaterial != null)
        {
            Destroy(ropeMaterial);
        }

        foreach (Material material in sceneSignMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        if (runtimeRoot != null)
        {
            Destroy(runtimeRoot);
        }
    }
}
