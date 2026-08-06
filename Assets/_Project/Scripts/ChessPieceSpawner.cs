using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines which prefab is used for each type of piece on one side.
/// One rook prefab is reused twice, one pawn prefab is reused eight times, etc.
/// </summary>
[Serializable]
public sealed class ChessPiecePrefabSet
{
    [SerializeField] private GameObject king;
    [SerializeField] private GameObject queen;
    [SerializeField] private GameObject rook;
    [SerializeField] private GameObject bishop;
    [SerializeField] private GameObject knight;
    [SerializeField] private GameObject pawn;

    public GameObject King => king;
    public GameObject Queen => queen;
    public GameObject Rook => rook;
    public GameObject Bishop => bishop;
    public GameObject Knight => knight;
    public GameObject Pawn => pawn;

    public bool HasAnyPrefab =>
        king != null || queen != null || rook != null ||
        bishop != null || knight != null || pawn != null;
}

public enum ChessBoardAnchor
{
    BoardCenter,
    A1Square
}

public enum ChessPlacementPlane
{
    WorldHorizontal,
    OriginLocal
}

/// <summary>
/// Spawns a standard 32-piece chess starting position from inspector-assigned prefabs.
/// The placement origin's right axis points from file a to h, and its forward axis
/// points from White toward Black.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChessPieceSpawner : MonoBehaviour
{
    private sealed class NetworkPieceVisual
    {
        public GameObject Instance;
        public PlayerTeam Team;
        public ChessPieceType PieceType;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public Renderer[] Renderers;
    }

    private static readonly string[] BackRankNames =
    {
        "Rook", "Knight", "Bishop", "Queen", "King", "Bishop", "Knight", "Rook"
    };

    private readonly GameObject[,] spawnedPieces = new GameObject[8, 8];
    private readonly Dictionary<ushort, NetworkPieceVisual> networkPieceVisuals = new();

    [Header("Piece Prefabs")]
    [SerializeField] private ChessPiecePrefabSet whitePieces = new();
    [SerializeField] private ChessPiecePrefabSet blackPieces = new();

    [Header("Placement Reference")]
    [Tooltip("Object used as the placement reference. If empty, this component's object is used.")]
    [SerializeField] private Transform placementOrigin;
    [Tooltip("Choose whether the reference object is at the board centre or at the centre of a1.")]
    [SerializeField] private ChessBoardAnchor anchor = ChessBoardAnchor.BoardCenter;
    [Tooltip("World Horizontal ignores the reference object's X/Z tilt and uses only its Y rotation.")]
    [SerializeField] private ChessPlacementPlane placementPlane = ChessPlacementPlane.WorldHorizontal;
    [Tooltip("Additional rotation of the entire layout around its up axis.")]
    [SerializeField] private float layoutYawOffset;
    [Tooltip("Optional parent for the generated-pieces container.")]
    [SerializeField] private Transform pieceParent;

    [Header("Spacing")]
    [Tooltip("Distance between adjacent files: a-b, b-c, etc.")]
    [SerializeField, Min(0.001f)] private float fileSpacing = 1f;
    [Tooltip("Distance between adjacent ranks: 1-2, 2-3, etc.")]
    [SerializeField, Min(0.001f)] private float rankSpacing = 1f;
    [Tooltip("Piece pivot height along the selected placement plane's up axis.")]
    [SerializeField] private float heightOffset;
    [Tooltip("Vertical distance between pieces in the same stack.")]
    [SerializeField, Min(0f)] private float stackHeight = 0.06f;

    [Header("Rotation")]
    [Tooltip("Rotation added to every White prefab, relative to the reference object.")]
    [SerializeField] private Vector3 whiteRotationOffset;
    [Tooltip("Rotation added to every Black prefab, relative to the reference object.")]
    [SerializeField] private Vector3 blackRotationOffset = new(0f, 180f, 0f);

    [Header("Generation")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField, HideInInspector] private Transform generatedRoot;

    [Header("Selection Marker")]
    [SerializeField] private Color selectionMarkerColor = new(1f, 0.8f, 0f, 1f);
    [SerializeField, Range(16, 96)] private int selectionMarkerSegments = 48;

    private GameObject selectionMarker;
    private Material selectionMarkerMaterial;
    private bool networkVisualMode;
    private GameObject voiceQuestionMark;
    private TextMesh voiceQuestionMarkText;
    private int voiceQuestionMarkPieceId = -1;
    private float voiceQuestionMarkExpiresAt;
#if UNITY_EDITOR
    private GameObject editorVoiceTargetMarker;
    private Material editorVoiceTargetMaterial;
    private int editorVoiceTargetPieceId = -1;
#endif

    public Transform PlacementOrigin => placementOrigin != null ? placementOrigin : transform;
    public Vector3 BoardRight => PlacementRotation * Vector3.right;
    public Vector3 BoardForward => PlacementRotation * Vector3.forward;
    public Vector3 BoardUp => PlacementRotation * Vector3.up;
    public float FileSpacing => fileSpacing;
    public float RankSpacing => rankSpacing;

    private Quaternion PlacementRotation
    {
        get
        {
            Transform origin = PlacementOrigin;

            if (placementPlane == ChessPlacementPlane.WorldHorizontal)
            {
                return Quaternion.Euler(0f, origin.eulerAngles.y + layoutYawOffset, 0f);
            }

            return origin.rotation * Quaternion.Euler(0f, layoutYawOffset, 0f);
        }
    }

    private void Start()
    {
        if (generateOnStart)
        {
            GenerateInitialPosition();
        }
    }

    private void LateUpdate()
    {
        if (!networkVisualMode)
        {
            return;
        }

        float blend = 1f - Mathf.Exp(-18f * Time.deltaTime);

        foreach (NetworkPieceVisual visual in networkPieceVisuals.Values)
        {
            if (visual.Instance == null)
            {
                continue;
            }

            Transform pieceTransform = visual.Instance.transform;
            pieceTransform.SetPositionAndRotation(
                Vector3.Lerp(pieceTransform.position, visual.TargetPosition, blend),
                Quaternion.Slerp(pieceTransform.rotation, visual.TargetRotation, blend));
        }

        UpdateVoiceQuestionMark();
#if UNITY_EDITOR
        UpdateEditorVoiceTargetMarker();
#endif
    }

    /// <summary>
    /// Clears the previous result and generates every assigned piece prefab.
    /// Empty prefab fields are skipped so temporary models can be tested individually.
    /// </summary>
    [ContextMenu("Generate Initial Position")]
    public void GenerateInitialPosition()
    {
        ClearGeneratedPieces();

        if (!whitePieces.HasAnyPrefab && !blackPieces.HasAnyPrefab)
        {
            Debug.LogWarning(
                $"{nameof(ChessPieceSpawner)} on '{name}' has no chess piece prefabs assigned.",
                this);
            return;
        }

        CreateGeneratedRoot();
        SpawnSide(whitePieces, "White", backRank: 0, pawnRank: 1, whiteRotationOffset);
        SpawnSide(blackPieces, "Black", backRank: 7, pawnRank: 6, blackRotationOffset);
    }

    /// <summary>
    /// Removes only the container previously created by this component.
    /// Other children of the reference or parent object are never touched.
    /// </summary>
    [ContextMenu("Clear Generated Pieces")]
    public void ClearGeneratedPieces()
    {
        Array.Clear(spawnedPieces, 0, spawnedPieces.Length);
        networkPieceVisuals.Clear();
        networkVisualMode = false;

        if (generatedRoot == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.Undo.DestroyObjectImmediate(generatedRoot.gameObject);
            generatedRoot = null;
            return;
        }
#endif

        Destroy(generatedRoot.gameObject);
        generatedRoot = null;
    }

    /// <summary>
    /// Returns the world-space placement point for zero-based chess coordinates.
    /// a1 is (0, 0), and h8 is (7, 7).
    /// </summary>
    public Vector3 GetSquareWorldPosition(int file, int rank)
    {
        if ((uint)file >= 8)
        {
            throw new ArgumentOutOfRangeException(nameof(file), file, "File must be between 0 and 7.");
        }

        if ((uint)rank >= 8)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "Rank must be between 0 and 7.");
        }

        return GetBoardWorldPosition(file, rank);
    }

    public Vector3 GetBoardWorldPosition(float file, float rank)
    {
        float fileOffset = file * fileSpacing;
        float rankOffset = rank * rankSpacing;

        if (anchor == ChessBoardAnchor.BoardCenter)
        {
            fileOffset -= 3.5f * fileSpacing;
            rankOffset -= 3.5f * rankSpacing;
        }

        Transform origin = PlacementOrigin;
        Quaternion placementRotation = PlacementRotation;
        return origin.position
            + placementRotation * Vector3.right * fileOffset
            + placementRotation * Vector3.forward * rankOffset
            + placementRotation * Vector3.up * heightOffset;
    }

    public bool TryGetBoardCoordinates(
        Vector3 worldPosition,
        out float file,
        out float rank)
    {
        Vector3 offset = worldPosition - PlacementOrigin.position;
        file = Vector3.Dot(offset, BoardRight) / fileSpacing;
        rank = Vector3.Dot(offset, BoardForward) / rankSpacing;

        if (anchor == ChessBoardAnchor.BoardCenter)
        {
            file += 3.5f;
            rank += 3.5f;
        }

        return file >= 0f && file <= 7f && rank >= 0f && rank <= 7f;
    }

    public bool TryGetGazeTarget(
        Camera viewCamera,
        PlayerTeam team,
        out ushort pieceId)
    {
        pieceId = 0;

        if (viewCamera == null || team == PlayerTeam.Unassigned)
        {
            return false;
        }

        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(viewCamera);
        float bestScreenDistance = float.PositiveInfinity;
        float bestDepth = float.PositiveInfinity;
        bool found = false;

        foreach (KeyValuePair<ushort, NetworkPieceVisual> pair in networkPieceVisuals)
        {
            NetworkPieceVisual visual = pair.Value;

            if (visual.Instance == null || visual.Team != team ||
                !TryGetVisualBounds(visual, out Bounds bounds) ||
                !GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
            {
                continue;
            }

            Vector3 viewport = viewCamera.WorldToViewportPoint(bounds.center);

            if (viewport.z <= viewCamera.nearClipPlane ||
                viewport.x < 0f || viewport.x > 1f ||
                viewport.y < 0f || viewport.y > 1f)
            {
                continue;
            }

            float screenDistance =
                (new Vector2(viewport.x, viewport.y) - new Vector2(0.5f, 0.5f)).sqrMagnitude;

            if (screenDistance > bestScreenDistance + 0.000001f ||
                (Mathf.Abs(screenDistance - bestScreenDistance) <= 0.000001f &&
                 viewport.z >= bestDepth))
            {
                continue;
            }

            bestScreenDistance = screenDistance;
            bestDepth = viewport.z;
            pieceId = pair.Key;
            found = true;
        }

        return found;
    }

    public void ShowVoiceQuestionMark(ushort pieceId, float duration = 1f)
    {
        if (!networkPieceVisuals.ContainsKey(pieceId))
        {
            return;
        }

        EnsureVoiceQuestionMark();
        voiceQuestionMarkPieceId = pieceId;
        voiceQuestionMarkExpiresAt = Time.unscaledTime + Mathf.Max(0.1f, duration);
        voiceQuestionMark.SetActive(true);
    }

    public void SetEditorVoiceTarget(ushort? pieceId)
    {
#if UNITY_EDITOR
        editorVoiceTargetPieceId = pieceId.HasValue ? pieceId.Value : -1;

        if (editorVoiceTargetPieceId < 0 && editorVoiceTargetMarker != null)
        {
            editorVoiceTargetMarker.SetActive(false);
        }
#endif
    }

    public bool TryGetSquareFromScreenPoint(
        Camera viewCamera,
        Vector2 screenPoint,
        out int file,
        out int rank)
    {
        file = -1;
        rank = -1;

        if (viewCamera == null)
        {
            return false;
        }

        Quaternion placementRotation = PlacementRotation;
        Vector3 right = placementRotation * Vector3.right;
        Vector3 forward = placementRotation * Vector3.forward;
        Vector3 up = placementRotation * Vector3.up;
        Vector3 planePoint = PlacementOrigin.position + up * heightOffset;
        Plane boardPlane = new(up, planePoint);
        Ray pointerRay = viewCamera.ScreenPointToRay(screenPoint);

        if (!boardPlane.Raycast(pointerRay, out float enter))
        {
            return false;
        }

        Vector3 boardPoint = pointerRay.GetPoint(enter);
        Vector3 offset = boardPoint - PlacementOrigin.position;
        float fileCoordinate = Vector3.Dot(offset, right) / fileSpacing;
        float rankCoordinate = Vector3.Dot(offset, forward) / rankSpacing;

        if (anchor == ChessBoardAnchor.BoardCenter)
        {
            fileCoordinate += 3.5f;
            rankCoordinate += 3.5f;
        }

        if (fileCoordinate < -0.5f || fileCoordinate > 7.5f ||
            rankCoordinate < -0.5f || rankCoordinate > 7.5f)
        {
            return false;
        }

        file = Mathf.RoundToInt(fileCoordinate);
        rank = Mathf.RoundToInt(rankCoordinate);
        return (uint)file < 8 && (uint)rank < 8;
    }

    public void ShowSelection(int file, int rank)
    {
        ShowSelection((float)file, rank);
    }

    public void ShowSelection(float file, float rank)
    {
        if (file < 0f || file > 7f || rank < 0f || rank > 7f)
        {
            HideSelection();
            return;
        }

        if (selectionMarker == null)
        {
            CreateSelectionMarker();
        }

        float squareSize = Mathf.Min(fileSpacing, rankSpacing);
        float radius = squareSize * 0.4f;
        float lineWidth = squareSize * 0.06f;
        Vector3 up = PlacementRotation * Vector3.up;
        Vector3 right = PlacementRotation * Vector3.right;
        Vector3 forward = PlacementRotation * Vector3.forward;
        Vector3 centre = GetBoardWorldPosition(file, rank) + up * (squareSize * 0.025f);
        LineRenderer lineRenderer = selectionMarker.GetComponent<LineRenderer>();

        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = selectionMarkerSegments;

        for (int index = 0; index < selectionMarkerSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / selectionMarkerSegments;
            Vector3 point = centre +
                right * (Mathf.Cos(angle) * radius) +
                forward * (Mathf.Sin(angle) * radius);
            lineRenderer.SetPosition(index, point);
        }

        selectionMarker.SetActive(true);
    }

    public void HideSelection()
    {
        if (selectionMarker != null)
        {
            selectionMarker.SetActive(false);
        }
    }

    public void RebuildFromNetworkState(
        IEnumerable<NetworkChessPieceState> pieceStates)
    {
        if (!networkVisualMode)
        {
            ClearGeneratedPieces();
            CreateGeneratedRoot();
            networkVisualMode = true;
        }
        else if (generatedRoot == null)
        {
            CreateGeneratedRoot();
        }

        HashSet<ushort> livePieceIds = new();

        foreach (NetworkChessPieceState pieceState in pieceStates)
        {
            livePieceIds.Add(pieceState.Id);
            GameObject prefab = GetPiecePrefab(
                pieceState.OwnerTeam,
                pieceState.PieceType);

            if (prefab == null)
            {
                continue;
            }

            if (!networkPieceVisuals.TryGetValue(
                    pieceState.Id,
                    out NetworkPieceVisual visual) ||
                visual.Instance == null ||
                visual.Team != pieceState.OwnerTeam ||
                visual.PieceType != pieceState.PieceType)
            {
                if (visual?.Instance != null)
                {
                    Destroy(visual.Instance);
                }

                visual = CreateNetworkPieceVisual(prefab, pieceState);
                networkPieceVisuals[pieceState.Id] = visual;
            }

            visual.Instance.name =
                $"{pieceState.OwnerTeam}_{pieceState.PieceType}_" +
                $"{GetSquareName(pieceState.File, pieceState.Rank)}_" +
                $"Depth{pieceState.StackDepth}_Id{pieceState.Id}";
            visual.Team = pieceState.OwnerTeam;
            visual.PieceType = pieceState.PieceType;
            visual.TargetPosition =
                GetBoardWorldPosition(pieceState.BoardFile, pieceState.BoardRank) +
                PlacementRotation * Vector3.up * (pieceState.StackDepth * stackHeight);
            visual.TargetRotation = GetNetworkPieceRotation(prefab, pieceState);
        }

        List<ushort> removedIds = new();

        foreach (KeyValuePair<ushort, NetworkPieceVisual> pair in networkPieceVisuals)
        {
            if (livePieceIds.Contains(pair.Key))
            {
                continue;
            }

            if (pair.Value.Instance != null)
            {
                Destroy(pair.Value.Instance);
            }

            removedIds.Add(pair.Key);
        }

        foreach (ushort removedId in removedIds)
        {
            networkPieceVisuals.Remove(removedId);
        }
    }

    private NetworkPieceVisual CreateNetworkPieceVisual(
        GameObject prefab,
        NetworkChessPieceState pieceState)
    {
        Vector3 position =
            GetBoardWorldPosition(pieceState.BoardFile, pieceState.BoardRank) +
            PlacementRotation * Vector3.up * (pieceState.StackDepth * stackHeight);
        Quaternion rotation = GetNetworkPieceRotation(prefab, pieceState);
        GameObject instance = Instantiate(prefab, position, rotation, generatedRoot);

        return new NetworkPieceVisual
        {
            Instance = instance,
            Team = pieceState.OwnerTeam,
            PieceType = pieceState.PieceType,
            TargetPosition = position,
            TargetRotation = rotation,
            Renderers = instance.GetComponentsInChildren<Renderer>(includeInactive: false)
        };
    }

    private static bool TryGetVisualBounds(
        NetworkPieceVisual visual,
        out Bounds bounds)
    {
        bounds = default;
        bool initialized = false;

        if (visual.Renderers == null)
        {
            return false;
        }

        foreach (Renderer pieceRenderer in visual.Renderers)
        {
            if (pieceRenderer == null || !pieceRenderer.enabled)
            {
                continue;
            }

            if (!initialized)
            {
                bounds = pieceRenderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(pieceRenderer.bounds);
            }
        }

        return initialized;
    }

    private void EnsureVoiceQuestionMark()
    {
        if (voiceQuestionMark != null)
        {
            return;
        }

        voiceQuestionMark = new GameObject("Voice Command Question Mark");
        voiceQuestionMarkText = voiceQuestionMark.AddComponent<TextMesh>();
        voiceQuestionMarkText.text = "?";
        voiceQuestionMarkText.anchor = TextAnchor.MiddleCenter;
        voiceQuestionMarkText.alignment = TextAlignment.Center;
        voiceQuestionMarkText.fontSize = 96;
        voiceQuestionMarkText.characterSize = Mathf.Min(fileSpacing, rankSpacing) * 0.035f;
        voiceQuestionMarkText.color = Color.white;
        voiceQuestionMark.SetActive(false);
    }

    private void UpdateVoiceQuestionMark()
    {
        if (voiceQuestionMark == null || !voiceQuestionMark.activeSelf)
        {
            return;
        }

        if (Time.unscaledTime >= voiceQuestionMarkExpiresAt ||
            !networkPieceVisuals.TryGetValue(
                (ushort)voiceQuestionMarkPieceId,
                out NetworkPieceVisual visual) ||
            visual.Instance == null)
        {
            voiceQuestionMark.SetActive(false);
            voiceQuestionMarkPieceId = -1;
            return;
        }

        Vector3 position = visual.TargetPosition + BoardUp * Mathf.Min(fileSpacing, rankSpacing);

        if (TryGetVisualBounds(visual, out Bounds bounds))
        {
            position = bounds.center + BoardUp * (bounds.extents.magnitude + 0.12f);
        }

        voiceQuestionMark.transform.position = position;
        Camera viewCamera = Camera.main;

        if (viewCamera != null)
        {
            voiceQuestionMark.transform.rotation = Quaternion.LookRotation(
                voiceQuestionMark.transform.position - viewCamera.transform.position,
                viewCamera.transform.up);
        }
    }

#if UNITY_EDITOR
    private void UpdateEditorVoiceTargetMarker()
    {
        if (editorVoiceTargetPieceId < 0 ||
            !networkPieceVisuals.TryGetValue(
                (ushort)editorVoiceTargetPieceId,
                out NetworkPieceVisual visual) ||
            visual.Instance == null)
        {
            if (editorVoiceTargetMarker != null)
            {
                editorVoiceTargetMarker.SetActive(false);
            }

            return;
        }

        if (editorVoiceTargetMarker == null)
        {
            editorVoiceTargetMarker = new GameObject("Editor Voice Gaze Target");
            LineRenderer lineRenderer = editorVoiceTargetMarker.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = true;
            lineRenderer.positionCount = 48;
            lineRenderer.startWidth = Mathf.Min(fileSpacing, rankSpacing) * 0.035f;
            lineRenderer.endWidth = lineRenderer.startWidth;
            lineRenderer.startColor = Color.cyan;
            lineRenderer.endColor = Color.cyan;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            Shader shader = Shader.Find("Sprites/Default");

            if (shader != null)
            {
                editorVoiceTargetMaterial = new Material(shader) { color = Color.cyan };
                lineRenderer.sharedMaterial = editorVoiceTargetMaterial;
            }
        }

        float radius = Mathf.Min(fileSpacing, rankSpacing) * 0.43f;
        Vector3 centre = visual.TargetPosition + BoardUp * 0.04f;
        LineRenderer marker = editorVoiceTargetMarker.GetComponent<LineRenderer>();

        for (int index = 0; index < marker.positionCount; index++)
        {
            float angle = index * Mathf.PI * 2f / marker.positionCount;
            marker.SetPosition(
                index,
                centre + BoardRight * (Mathf.Cos(angle) * radius) +
                BoardForward * (Mathf.Sin(angle) * radius));
        }

        editorVoiceTargetMarker.SetActive(true);
    }
#endif

    private Quaternion GetNetworkPieceRotation(
        GameObject prefab,
        NetworkChessPieceState pieceState)
    {
        Vector3 rotationOffset = pieceState.OwnerTeam == PlayerTeam.White
            ? whiteRotationOffset
            : blackRotationOffset;

        return PlacementRotation *
               Quaternion.Euler(0f, pieceState.VoiceHeading, 0f) *
               Quaternion.Euler(rotationOffset) *
               prefab.transform.rotation;
    }

    private void CreateGeneratedRoot()
    {
        GameObject rootObject = new("Generated Chess Pieces");
        Transform parent = pieceParent != null ? pieceParent : PlacementOrigin;
        generatedRoot = rootObject.transform;
        generatedRoot.SetParent(parent, false);

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.Undo.RegisterCreatedObjectUndo(rootObject, "Generate Chess Pieces");
        }
#endif
    }

    private void CreateSelectionMarker()
    {
        selectionMarker = new GameObject("Local Selection Marker");
        Transform parent = pieceParent != null ? pieceParent : PlacementOrigin;
        selectionMarker.transform.SetParent(parent, false);

        LineRenderer lineRenderer = selectionMarker.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = true;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.numCapVertices = 4;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.startColor = selectionMarkerColor;
        lineRenderer.endColor = selectionMarkerColor;

        Shader markerShader = Shader.Find("Sprites/Default");

        if (markerShader == null)
        {
            markerShader = Shader.Find("Unlit/Color");
        }

        if (markerShader != null)
        {
            selectionMarkerMaterial = new Material(markerShader)
            {
                color = selectionMarkerColor
            };
            lineRenderer.sharedMaterial = selectionMarkerMaterial;
        }
    }

    private void OnDestroy()
    {
        if (selectionMarkerMaterial != null)
        {
            Destroy(selectionMarkerMaterial);
        }

#if UNITY_EDITOR
        if (editorVoiceTargetMaterial != null)
        {
            Destroy(editorVoiceTargetMaterial);
        }
#endif
    }

    private void SpawnSide(
        ChessPiecePrefabSet prefabs,
        string sideName,
        int backRank,
        int pawnRank,
        Vector3 rotationOffset)
    {
        GameObject[] backRankPrefabs =
        {
            prefabs.Rook,
            prefabs.Knight,
            prefabs.Bishop,
            prefabs.Queen,
            prefabs.King,
            prefabs.Bishop,
            prefabs.Knight,
            prefabs.Rook
        };

        for (int file = 0; file < 8; file++)
        {
            SpawnPiece(
                backRankPrefabs[file],
                $"{sideName}_{BackRankNames[file]}_{GetSquareName(file, backRank)}",
                file,
                backRank,
                rotationOffset);

            SpawnPiece(
                prefabs.Pawn,
                $"{sideName}_Pawn_{GetSquareName(file, pawnRank)}",
                file,
                pawnRank,
                rotationOffset);
        }
    }

    private void SpawnPiece(
        GameObject prefab,
        string instanceName,
        int file,
        int rank,
        Vector3 rotationOffset,
        float verticalOffset = 0f,
        bool registerSingleSquare = true)
    {
        if (prefab == null)
        {
            return;
        }

        Quaternion rotation =
            PlacementRotation * Quaternion.Euler(rotationOffset) * prefab.transform.rotation;
        Vector3 position =
            GetSquareWorldPosition(file, rank) +
            PlacementRotation * Vector3.up * verticalOffset;
        GameObject instance;

#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
        {
            instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, generatedRoot);
            instance.transform.SetPositionAndRotation(position, rotation);
        }
        else
#endif
        {
            instance = Instantiate(
                prefab,
                position,
                rotation,
                generatedRoot);
        }

        instance.name = instanceName;

        if (registerSingleSquare)
        {
            spawnedPieces[file, rank] = instance;
        }
    }

    private GameObject GetPiecePrefab(
        PlayerTeam team,
        ChessPieceType pieceType)
    {
        ChessPiecePrefabSet pieceSet = team switch
        {
            PlayerTeam.White => whitePieces,
            PlayerTeam.Black => blackPieces,
            _ => null
        };

        if (pieceSet == null)
        {
            return null;
        }

        return pieceType switch
        {
            ChessPieceType.King => pieceSet.King,
            ChessPieceType.Queen => pieceSet.Queen,
            ChessPieceType.Rook => pieceSet.Rook,
            ChessPieceType.Bishop => pieceSet.Bishop,
            ChessPieceType.Knight => pieceSet.Knight,
            ChessPieceType.Pawn => pieceSet.Pawn,
            _ => null
        };
    }

    private static string GetSquareName(int file, int rank)
    {
        return $"{(char)('a' + file)}{rank + 1}";
    }

    private void OnValidate()
    {
        fileSpacing = Mathf.Max(0.001f, fileSpacing);
        rankSpacing = Mathf.Max(0.001f, rankSpacing);
        stackHeight = Mathf.Max(0f, stackHeight);
        selectionMarkerSegments = Mathf.Clamp(selectionMarkerSegments, 16, 96);
    }
}
