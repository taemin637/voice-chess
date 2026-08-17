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
    public const float DefaultBoardBorderWidthInSquares = 1.25f;

    private sealed class NetworkPieceVisual
    {
        public GameObject Instance;
        public PlayerTeam Team;
        public ChessPieceType PieceType;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public float CurrentHeading;
        public float TargetHeading;
        public GameObject HeadingArrow;
        public MeshRenderer HeadingArrowRenderer;
        public GameObject MovementCooldownVisual;
        public LineRenderer MovementCooldownBackground;
        public LineRenderer MovementCooldownArc;
        public double MovementCooldownEndServerTime;
        public Renderer[] Renderers;
        public Collider[] SelectionColliders;
    }

    private static readonly string[] BackRankNames =
    {
        "Rook", "Knight", "Bishop", "Queen", "King", "Bishop", "Knight", "Rook"
    };

    private readonly GameObject[,] spawnedPieces = new GameObject[8, 8];
    private readonly Dictionary<ushort, NetworkPieceVisual> networkPieceVisuals = new();
    private readonly RaycastHit[] gazeRaycastHits = new RaycastHit[128];

    [Header("기물 프리팹")]
    [SerializeField] private ChessPiecePrefabSet whitePieces = new();
    [SerializeField] private ChessPiecePrefabSet blackPieces = new();

    [Header("에디터 시작 배치 미리보기")]
    [Tooltip("When assigned, Generate Initial Position previews the same roster used by NetworkChessGame.")]
    [SerializeField] private GameModeConfiguration previewGameMode;

    [Header("배치 기준")]
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

    [Header("기물 간격")]
    [Tooltip("Distance between adjacent files: a-b, b-c, etc.")]
    [SerializeField, Min(0.001f)] private float fileSpacing = 1f;
    [Tooltip("Distance between adjacent ranks: 1-2, 2-3, etc.")]
    [SerializeField, Min(0.001f)] private float rankSpacing = 1f;
    [Tooltip("Piece pivot height along the selected placement plane's up axis.")]
    [SerializeField] private float heightOffset;

    [Header("체스판 바닥")]
    [Tooltip("Walkable border outside the 8x8 squares, measured in chess-square units.")]
    [SerializeField, Min(0f)]
    private float boardBorderWidthInSquares = DefaultBoardBorderWidthInSquares;

    [Header("장외 연출")]
    [Tooltip("Distance beyond the board edge over which the falling animation plays.")]
    [SerializeField, Min(0.1f)] private float ringOutVisualDistance = 0.8f;
    [Tooltip("Downward fall distance, measured in chess-square units.")]
    [SerializeField, Min(0.1f)] private float ringOutDropDistance = 2.5f;
    [SerializeField, Range(0f, 120f)] private float ringOutTiltAngle = 82f;

    [Header("회전")]
    [Tooltip("Rotation added to every White prefab, relative to the reference object.")]
    [SerializeField] private Vector3 whiteRotationOffset;
    [Tooltip("Rotation added to every Black prefab, relative to the reference object.")]
    [SerializeField] private Vector3 blackRotationOffset = new(0f, 180f, 0f);

    [Header("생성")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField, HideInInspector] private Transform generatedRoot;

    [Header("선택 표시")]
    [SerializeField] private Color selectionMarkerColor = new(1f, 0.8f, 0f, 1f);
    [SerializeField, Range(16, 96)] private int selectionMarkerSegments = 48;
    [SerializeField] private Color voiceHoverMarkerColor =
        new(0.1f, 0.45f, 1f, 1f);
    [SerializeField] private Color confirmedVoiceMarkerColor =
        new(1f, 0.38f, 0.05f, 1f);

    [Header("기물 방향 화살표")]
    [InspectorName("아군 방향 화살표 색")]
    [SerializeField] private Color whiteHeadingArrowColor = new(0.1f, 0.85f, 1f, 0.95f);
    [InspectorName("적군 방향 화살표 색")]
    [SerializeField] private Color blackHeadingArrowColor = new(1f, 0.3f, 0.15f, 0.95f);
    [SerializeField, Range(0.3f, 1f)] private float headingArrowLengthInSquares = 0.72f;
    [SerializeField, Range(0.02f, 0.15f)] private float headingArrowWidthInSquares = 0.065f;
    [SerializeField, Range(0.005f, 0.1f)] private float headingArrowHeightInSquares = 0.025f;

    private GameObject selectionMarker;
    private Material selectionMarkerMaterial;
    private Material whiteHeadingArrowMaterial;
    private Material blackHeadingArrowMaterial;
    private Material movementCooldownMaterial;
    private Material movementCooldownBackgroundMaterial;
    private Mesh headingArrowMesh;
    private bool networkVisualMode;
    private GameObject voiceQuestionMark;
    private TextMesh voiceQuestionMarkText;
    private int voiceQuestionMarkPieceId = -1;
    private float voiceQuestionMarkExpiresAt;
    private GameObject voiceSelectionMarker;
    private Material voiceSelectionMaterial;
    private int voiceSelectionPieceId = -1;
    private readonly List<GameObject> confirmedVoiceSelectionMarkers = new();
    private readonly List<Material> confirmedVoiceSelectionMaterials = new();
    private readonly List<int> confirmedVoiceSelectionPieceIds = new();
    private GameObject voiceCommandMarker;
    private Material voiceCommandMaterial;
    private int voiceCommandPieceId = -1;
    private float voiceCommandMarkerExpiresAt = float.PositiveInfinity;

    public Transform PlacementOrigin => placementOrigin != null ? placementOrigin : transform;
    public Vector3 BoardRight => PlacementRotation * Vector3.right;
    public Vector3 BoardForward => PlacementRotation * Vector3.forward;
    public Vector3 BoardUp => PlacementRotation * Vector3.up;
    public float FileSpacing => fileSpacing;
    public float RankSpacing => rankSpacing;
    public float GroundMinimumCoordinate => -0.5f - boardBorderWidthInSquares;
    public float GroundMaximumCoordinate => 7.5f + boardBorderWidthInSquares;

    public GameObject GetKingPrefab(PlayerTeam team)
    {
        return GetPiecePrefab(team, ChessPieceType.King);
    }

    public Quaternion GetPieceWorldRotation(
        PlayerTeam team,
        GameObject prefab,
        float heading)
    {
        if (prefab == null)
        {
            return PlacementRotation;
        }

        Vector3 rotationOffset = team == PlayerTeam.White
            ? whiteRotationOffset
            : blackRotationOffset;
        return PlacementRotation *
               Quaternion.Euler(0f, heading, 0f) *
               Quaternion.Euler(rotationOffset) *
               prefab.transform.rotation;
    }

    private void Awake()
    {
        ApplyCentralConfiguration();
    }

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
            visual.CurrentHeading = Mathf.LerpAngle(
                visual.CurrentHeading,
                visual.TargetHeading,
                blend);
            UpdateHeadingArrow(visual, pieceTransform.position);
        }

        UpdateVoiceQuestionMark();
        UpdateVoiceTargetMarkers();
    }

    /// <summary>
    /// Clears the previous result and generates every assigned piece prefab.
    /// Empty prefab fields are skipped so temporary models can be tested individually.
    /// </summary>
    [ContextMenu("Generate Initial Position")]
    public void GenerateInitialPosition()
    {
        GameModeConfiguration configuration = ResolveCentralConfiguration();
        ApplyCentralConfiguration(configuration);
        ClearGeneratedPieces();

        if (!whitePieces.HasAnyPrefab && !blackPieces.HasAnyPrefab)
        {
            // Debug.LogWarning(
            //     $"{nameof(ChessPieceSpawner)} on '{name}' has no chess piece prefabs assigned.",
            //     this);
            return;
        }

        CreateGeneratedRoot();

        if (configuration != null)
        {
            // In Player Commander mode kings are controlled by players and must
            // not exist as board pieces at runtime.  Still show them in edit-mode
            // previews so the exact player avatar prefab, scale and material can
            // be adjusted on the board before entering Play Mode.
            SpawnConfiguredPosition(
                configuration,
                includePlayerCommanderKings: !Application.isPlaying);
            return;
        }

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

        Ray gazeRay = viewCamera.ScreenPointToRay(viewCamera.pixelRect.center);
        int hitCount = Physics.RaycastNonAlloc(
            gazeRay,
            gazeRaycastHits,
            viewCamera.farClipPlane,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        float nearestPieceDistance = float.PositiveInfinity;
        NetworkPieceVisual nearestVisual = null;

        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            RaycastHit hit = gazeRaycastHits[hitIndex];

            if (hit.collider == null || hit.distance >= nearestPieceDistance ||
                !TryGetPieceVisual(hit.collider.transform, out ushort hitPieceId,
                    out NetworkPieceVisual hitVisual))
            {
                continue;
            }

            nearestPieceDistance = hit.distance;
            pieceId = hitPieceId;
            nearestVisual = hitVisual;
        }

        // The closest piece blocks pieces behind it. An enemy under the reticle
        // therefore clears the local selection instead of selecting an ally
        // whose projected bounds happen to overlap the screen centre.
        return nearestVisual != null && nearestVisual.Team == team;
    }

    private bool TryGetPieceVisual(
        Transform hitTransform,
        out ushort pieceId,
        out NetworkPieceVisual visual)
    {
        foreach (KeyValuePair<ushort, NetworkPieceVisual> pair in
                 networkPieceVisuals)
        {
            Transform instanceTransform = pair.Value.Instance != null
                ? pair.Value.Instance.transform
                : null;

            if (instanceTransform == null ||
                (hitTransform != instanceTransform &&
                 !hitTransform.IsChildOf(instanceTransform)))
            {
                continue;
            }

            pieceId = pair.Key;
            visual = pair.Value;
            return true;
        }

        pieceId = 0;
        visual = null;
        return false;
    }

    public bool TryGetRepresentativePieceHeight(out float height)
    {
        height = 0f;
        Dictionary<ChessPieceType, float> heightByType = new();
        Vector3 up = BoardUp;

        foreach (NetworkPieceVisual visual in networkPieceVisuals.Values)
        {
            if (!TryGetVisualBounds(visual, out Bounds bounds))
            {
                continue;
            }

            Vector3 extents = bounds.extents;
            float projectedHeight = 2f * (
                Mathf.Abs(up.x) * extents.x +
                Mathf.Abs(up.y) * extents.y +
                Mathf.Abs(up.z) * extents.z);

            if (projectedHeight <= 0.001f)
            {
                continue;
            }

            if (!heightByType.TryGetValue(visual.PieceType, out float existingHeight) ||
                projectedHeight > existingHeight)
            {
                heightByType[visual.PieceType] = projectedHeight;
            }
        }

        if (heightByType.Count == 0)
        {
            return false;
        }

        foreach (float pieceHeight in heightByType.Values)
        {
            height += pieceHeight;
        }

        height /= heightByType.Count;
        return true;
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

    public void SetVoiceSelectionTarget(ushort? pieceId)
    {
        voiceSelectionPieceId = pieceId.HasValue ? pieceId.Value : -1;

        if (voiceSelectionPieceId < 0 && voiceSelectionMarker != null)
        {
            voiceSelectionMarker.SetActive(false);
        }
    }

    public void SetConfirmedVoiceSelectionTarget(ushort? pieceId)
    {
        if (pieceId.HasValue)
        {
            SetConfirmedVoiceSelectionTargets(new[] { pieceId.Value });
        }
        else
        {
            SetConfirmedVoiceSelectionTargets(Array.Empty<ushort>());
        }
    }

    public void SetConfirmedVoiceSelectionTargets(
        IReadOnlyList<ushort> pieceIds)
    {
        int requestedCount = pieceIds?.Count ?? 0;

        while (confirmedVoiceSelectionPieceIds.Count < requestedCount)
        {
            confirmedVoiceSelectionPieceIds.Add(-1);
            confirmedVoiceSelectionMarkers.Add(null);
            confirmedVoiceSelectionMaterials.Add(null);
        }

        for (int index = 0; index < confirmedVoiceSelectionPieceIds.Count; index++)
        {
            confirmedVoiceSelectionPieceIds[index] =
                index < requestedCount
                    ? pieceIds[index]
                    : -1;

            if (confirmedVoiceSelectionPieceIds[index] < 0 &&
                confirmedVoiceSelectionMarkers[index] != null)
            {
                confirmedVoiceSelectionMarkers[index].SetActive(false);
            }
        }
    }

    public bool TryGetNetworkPieceWorldBounds(ushort pieceId, out Bounds bounds)
    {
        bounds = default;
        return networkPieceVisuals.TryGetValue(pieceId, out NetworkPieceVisual visual) &&
            visual.Instance != null &&
            TryGetVisualBounds(visual, out bounds);
    }

    public void SetVoiceCommandTarget(ushort? pieceId, float duration = -1f)
    {
        voiceCommandPieceId = pieceId.HasValue ? pieceId.Value : -1;
        voiceCommandMarkerExpiresAt = duration > 0f
            ? Time.unscaledTime + duration
            : float.PositiveInfinity;

        if (voiceCommandPieceId < 0 && voiceCommandMarker != null)
        {
            voiceCommandMarker.SetActive(false);
        }
    }

    public void HoldVoiceCommandTarget(float duration = 1f)
    {
        if (voiceCommandPieceId >= 0)
        {
            voiceCommandMarkerExpiresAt = Time.unscaledTime + Mathf.Max(0.1f, duration);
        }
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
        ApplyCentralConfiguration();

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

                if (visual?.HeadingArrow != null)
                {
                    Destroy(visual.HeadingArrow);
                }

                if (visual?.MovementCooldownVisual != null)
                {
                    Destroy(visual.MovementCooldownVisual);
                }

                visual = CreateNetworkPieceVisual(prefab, pieceState);
                networkPieceVisuals[pieceState.Id] = visual;
            }

            visual.Instance.name =
                $"{pieceState.OwnerTeam}_{pieceState.PieceType}_" +
                $"Id{pieceState.Id}";
            visual.Team = pieceState.OwnerTeam;
            visual.PieceType = pieceState.PieceType;
            visual.TargetPosition = GetNetworkPiecePosition(pieceState);
            visual.TargetRotation = GetNetworkPieceRotation(prefab, pieceState);
            visual.TargetHeading = pieceState.VoiceHeading;
            visual.MovementCooldownEndServerTime =
                pieceState.MovementCooldownEndServerTime;
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

            if (pair.Value.HeadingArrow != null)
            {
                Destroy(pair.Value.HeadingArrow);
            }

            if (pair.Value.MovementCooldownVisual != null)
            {
                Destroy(pair.Value.MovementCooldownVisual);
            }

            removedIds.Add(pair.Key);
        }

        foreach (ushort removedId in removedIds)
        {
            networkPieceVisuals.Remove(removedId);
        }
    }

    /// <summary>
    /// Detaches the king visual from normal network-state rebuilding so a local
    /// death cinematic can continue after the authoritative piece is removed.
    /// </summary>
    public GameObject DetachKingForDeathCinematic(
        NetworkChessPieceState kingState)
    {
        GameObject prefab = GetPiecePrefab(
            kingState.OwnerTeam,
            ChessPieceType.King);
        GameObject instance = null;

        if (networkPieceVisuals.TryGetValue(
                kingState.Id,
                out NetworkPieceVisual visual))
        {
            instance = visual.Instance;

            if (visual.HeadingArrow != null)
            {
                Destroy(visual.HeadingArrow);
                visual.HeadingArrow = null;
            }


            if (visual.MovementCooldownVisual != null)
            {
                Destroy(visual.MovementCooldownVisual);
                visual.MovementCooldownVisual = null;
                visual.MovementCooldownBackground = null;
                visual.MovementCooldownArc = null;
            }
            networkPieceVisuals.Remove(kingState.Id);
        }

        NetworkChessPieceState edgeState = kingState;
        edgeState.BoardFile = Mathf.Clamp(
            edgeState.BoardFile,
            GroundMinimumCoordinate,
            GroundMaximumCoordinate);
        edgeState.BoardRank = Mathf.Clamp(
            edgeState.BoardRank,
            GroundMinimumCoordinate,
            GroundMaximumCoordinate);

        if (instance == null && prefab != null)
        {
            instance = Instantiate(prefab);
        }

        if (instance == null)
        {
            return null;
        }

        Quaternion rotation = prefab != null
            ? GetNetworkPieceRotation(prefab, edgeState)
            : instance.transform.rotation;
        instance.name = $"{kingState.OwnerTeam}_King_Death_Cinematic";
        instance.transform.SetParent(null, worldPositionStays: true);
        instance.transform.SetPositionAndRotation(
            GetNetworkPiecePosition(edgeState),
            rotation);

        foreach (Collider pieceCollider in instance.GetComponentsInChildren<Collider>())
        {
            pieceCollider.enabled = false;
        }

        foreach (Rigidbody rigidbody in instance.GetComponentsInChildren<Rigidbody>())
        {
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
        }

        if (voiceSelectionPieceId == kingState.Id)
        {
            SetVoiceSelectionTarget(null);
        }

        if (voiceCommandPieceId == kingState.Id)
        {
            SetVoiceCommandTarget(null);
        }

        return instance;
    }

    private NetworkPieceVisual CreateNetworkPieceVisual(
        GameObject prefab,
        NetworkChessPieceState pieceState)
    {
        Vector3 position = GetNetworkPiecePosition(pieceState);
        Quaternion rotation = GetNetworkPieceRotation(prefab, pieceState);
        GameObject instance = Instantiate(prefab, position, rotation, generatedRoot);
        NetworkPieceVisual visual = new()
        {
            Instance = instance,
            Team = pieceState.OwnerTeam,
            PieceType = pieceState.PieceType,
            TargetPosition = position,
            TargetRotation = rotation,
            CurrentHeading = pieceState.VoiceHeading,
            TargetHeading = pieceState.VoiceHeading,
            Renderers = instance.GetComponentsInChildren<Renderer>(includeInactive: false)
        };

        visual.SelectionColliders = EnsurePieceSelectionColliders(visual);
        visual.HeadingArrow = CreateHeadingArrow(visual);
        visual.HeadingArrowRenderer = visual.HeadingArrow != null
            ? visual.HeadingArrow.GetComponent<MeshRenderer>()
            : null;
        visual.MovementCooldownVisual = CreateMovementCooldownVisual(
            out visual.MovementCooldownBackground,
            out visual.MovementCooldownArc);
        visual.MovementCooldownEndServerTime =
            pieceState.MovementCooldownEndServerTime;
        UpdateHeadingArrow(visual, position);
        return visual;
    }

    private static Collider[] EnsurePieceSelectionColliders(
        NetworkPieceVisual visual)
    {
        if (visual.Instance == null)
        {
            return Array.Empty<Collider>();
        }

        Collider[] existingColliders = visual.Instance
            .GetComponentsInChildren<Collider>(includeInactive: false);

        if (existingColliders.Length > 0)
        {
            return existingColliders;
        }

        List<Collider> selectionColliders = new();

        foreach (Renderer pieceRenderer in visual.Renderers)
        {
            if (pieceRenderer == null)
            {
                continue;
            }

            Bounds localBounds = pieceRenderer.localBounds;

            if (localBounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            BoxCollider selectionCollider =
                pieceRenderer.gameObject.AddComponent<BoxCollider>();
            selectionCollider.center = localBounds.center;
            selectionCollider.size = localBounds.size;
            selectionCollider.isTrigger = true;
            selectionColliders.Add(selectionCollider);
        }

        return selectionColliders.ToArray();
    }

    private GameObject CreateMovementCooldownVisual(
        out LineRenderer background,
        out LineRenderer progressArc)
    {
        const int segments = 48;
        GameObject root = new("Movement Cooldown");
        root.transform.SetParent(generatedRoot, true);

        GameObject backgroundObject = new("Background");
        backgroundObject.transform.SetParent(root.transform, false);
        background = ConfigureCooldownLineRenderer(
            backgroundObject,
            GetMovementCooldownBackgroundMaterial());
        background.loop = true;
        background.sortingOrder = 20;
        background.positionCount = segments;

        for (int index = 0; index < segments; index++)
        {
            float angle = Mathf.PI * 0.5f - index * Mathf.PI * 2f / segments;
            background.SetPosition(
                index,
                new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
        }

        GameObject progressObject = new("Remaining Time");
        progressObject.transform.SetParent(root.transform, false);
        progressArc = ConfigureCooldownLineRenderer(
            progressObject,
            GetMovementCooldownMaterial());
        progressArc.loop = false;
        progressArc.sortingOrder = 21;
        root.SetActive(false);
        return root;
    }

    private static LineRenderer ConfigureCooldownLineRenderer(
        GameObject lineObject,
        Material material)
    {
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.alignment = LineAlignment.TransformZ;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
        return line;
    }

    public void UpdateMovementCooldownVisuals(
        double serverTime,
        bool systemEnabled,
        Func<ChessPieceType, float> cooldownDurationResolver)
    {
        const int segments = 48;
        float squareSize = Mathf.Min(fileSpacing, rankSpacing);
        float radius = squareSize * 0.3f;
        float lineWidth = squareSize * 0.045f;
        Camera viewCamera = Camera.main;

        foreach (NetworkPieceVisual visual in networkPieceVisuals.Values)
        {
            if (visual.Instance == null ||
                visual.MovementCooldownVisual == null ||
                visual.MovementCooldownBackground == null ||
                visual.MovementCooldownArc == null)
            {
                continue;
            }

            float remaining = systemEnabled
                ? Mathf.Max(
                    0f,
                    (float)(visual.MovementCooldownEndServerTime - serverTime))
                : 0f;

            if (remaining <= 0.0001f)
            {
                visual.MovementCooldownVisual.SetActive(false);
                continue;
            }

            Transform root = visual.MovementCooldownVisual.transform;
            Vector3 centre = GetMovementCooldownWorldPosition(
                visual,
                visual.Instance.transform.position,
                squareSize);
            Vector3 cameraDirection = viewCamera != null
                ? viewCamera.transform.position - centre
                : BoardForward;

            if (cameraDirection.sqrMagnitude < 0.0001f)
            {
                cameraDirection = BoardForward;
            }

            root.SetPositionAndRotation(
                centre,
                Quaternion.LookRotation(cameraDirection.normalized, BoardUp));
            root.localScale = Vector3.one;

            LineRenderer background = visual.MovementCooldownBackground;

            background.startWidth = lineWidth;
            background.endWidth = lineWidth;

            for (int index = 0; index < background.positionCount; index++)
            {
                background.SetPosition(
                    index,
                    background.GetPosition(index).normalized * radius);
            }

            float cooldownDuration = cooldownDurationResolver != null
                ? cooldownDurationResolver(visual.PieceType)
                : 0.01f;
            float progress = Mathf.Clamp01(
                remaining / Mathf.Max(0.01f, cooldownDuration));
            int pointCount = Mathf.Max(2, Mathf.CeilToInt(progress * segments) + 1);
            visual.MovementCooldownArc.positionCount = pointCount;
            visual.MovementCooldownArc.startWidth = lineWidth;
            visual.MovementCooldownArc.endWidth = lineWidth;

            for (int index = 0; index < pointCount; index++)
            {
                float normalized = Mathf.Min(
                    progress,
                    index / (float)segments);
                float angle = Mathf.PI * 0.5f - normalized * Mathf.PI * 2f;
                visual.MovementCooldownArc.SetPosition(
                    index,
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        squareSize * 0.002f));
            }

            visual.MovementCooldownVisual.SetActive(true);
        }
    }

    private Vector3 GetMovementCooldownWorldPosition(
        NetworkPieceVisual visual,
        Vector3 piecePosition,
        float squareSize)
    {
        if (!TryGetVisualBounds(visual, out Bounds bounds))
        {
            return piecePosition + BoardUp * squareSize;
        }

        Vector3 up = BoardUp.normalized;
        float topProjection = Vector3.Dot(bounds.center, up) +
            Mathf.Abs(up.x) * bounds.extents.x +
            Mathf.Abs(up.y) * bounds.extents.y +
            Mathf.Abs(up.z) * bounds.extents.z;
        float pieceProjection = Vector3.Dot(piecePosition, up);
        return piecePosition + up *
            (topProjection - pieceProjection + squareSize * 0.14f);
    }

    private Material GetMovementCooldownMaterial()
    {
        if (movementCooldownMaterial == null)
        {
            movementCooldownMaterial = CreateCooldownMaterial(
                "Movement Cooldown Material",
                new Color(1f, 0.48f, 0.04f, 0.98f));
        }

        return movementCooldownMaterial;
    }

    private Material GetMovementCooldownBackgroundMaterial()
    {
        if (movementCooldownBackgroundMaterial == null)
        {
            movementCooldownBackgroundMaterial = CreateCooldownMaterial(
                "Movement Cooldown Background Material",
                new Color(0.03f, 0.03f, 0.03f, 0.58f));
        }

        return movementCooldownBackgroundMaterial;
    }

    private static Material CreateCooldownMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color");

        return shader != null
            ? new Material(shader) { name = materialName, color = color }
            : null;
    }

    private GameObject CreateHeadingArrow(NetworkPieceVisual visual)
    {
        GameObject arrowObject = new("Heading Arrow");
        arrowObject.transform.SetParent(generatedRoot, true);

        MeshFilter meshFilter = arrowObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = GetHeadingArrowMesh();

        MeshRenderer meshRenderer = arrowObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = GetHeadingArrowMaterial(visual.Team);
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        return arrowObject;
    }

    private void UpdateHeadingArrow(
        NetworkPieceVisual visual,
        Vector3 piecePosition)
    {
        if (visual.HeadingArrow == null)
        {
            return;
        }

        float squareSize = Mathf.Min(fileSpacing, rankSpacing);
        float length = squareSize * headingArrowLengthInSquares;
        float width = squareSize * headingArrowWidthInSquares;
        Vector3 direction = GetWorldHeadingDirection(
            visual.Team,
            visual.CurrentHeading);
        Vector3 centre = piecePosition +
            BoardUp * (squareSize * headingArrowHeightInSquares);
        Transform arrowTransform = visual.HeadingArrow.transform;
        MeshRenderer arrowRenderer = visual.HeadingArrowRenderer;

        if (arrowRenderer != null)
        {
            Material expectedMaterial = GetHeadingArrowMaterial(visual.Team);

            if (arrowRenderer.sharedMaterial != expectedMaterial)
            {
                arrowRenderer.sharedMaterial = expectedMaterial;
            }
        }

        arrowTransform.SetPositionAndRotation(
            centre,
            Quaternion.LookRotation(direction, BoardUp));
        arrowTransform.localScale = new Vector3(width, 1f, length);
    }

    private Mesh GetHeadingArrowMesh()
    {
        if (headingArrowMesh != null)
        {
            return headingArrowMesh;
        }

        headingArrowMesh = new Mesh
        {
            name = "Piece Heading Arrow Mesh",
            vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0.58f),
                new Vector3(2.2f, 0f, 0.58f),
                new Vector3(0f, 0f, 1f),
                new Vector3(-2.2f, 0f, 0.58f),
                new Vector3(-0.5f, 0f, 0.58f)
            },
            triangles = new[]
            {
                0, 6, 1,
                1, 6, 2,
                5, 4, 3
            }
        };
        headingArrowMesh.RecalculateNormals();
        headingArrowMesh.RecalculateBounds();
        return headingArrowMesh;
    }

    private Vector3 GetWorldHeadingDirection(PlayerTeam team, float heading)
    {
        Vector3 teamForward = team == PlayerTeam.Black
            ? -BoardForward
            : BoardForward;
        return (Quaternion.AngleAxis(heading, BoardUp) * teamForward).normalized;
    }

    private Material GetHeadingArrowMaterial(PlayerTeam team)
    {
        bool isFriendly = NetworkPlayer.IsTeamFriendlyToLocalPlayer(team);
        Material material = isFriendly
            ? whiteHeadingArrowMaterial
            : blackHeadingArrowMaterial;

        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Sprites/Default") ??
                        Shader.Find("Unlit/Color");

        if (shader == null)
        {
            return null;
        }

        Color color = isFriendly
            ? whiteHeadingArrowColor
            : blackHeadingArrowColor;
        material = new Material(shader)
        {
            name = isFriendly
                ? "Friendly Heading Arrow Material"
                : "Enemy Heading Arrow Material",
            color = color
        };

        if (isFriendly)
        {
            whiteHeadingArrowMaterial = material;
        }
        else
        {
            blackHeadingArrowMaterial = material;
        }

        return material;
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

    private void UpdateVoiceTargetMarkers()
    {
        if (voiceCommandPieceId >= 0 &&
            Time.unscaledTime >= voiceCommandMarkerExpiresAt)
        {
            voiceCommandPieceId = -1;
        }

        UpdateVoiceTargetMarker(
            ref voiceSelectionMarker,
            ref voiceSelectionMaterial,
            voiceSelectionPieceId,
            "Voice Gaze Selection",
            voiceHoverMarkerColor,
            0.46f,
            0.025f);
        for (int index = 0; index < confirmedVoiceSelectionPieceIds.Count; index++)
        {
            GameObject marker = confirmedVoiceSelectionMarkers[index];
            Material material = confirmedVoiceSelectionMaterials[index];
            UpdateVoiceTargetMarker(
                ref marker,
                ref material,
                confirmedVoiceSelectionPieceIds[index],
                $"Confirmed Voice Selection {index + 1}",
                confirmedVoiceMarkerColor,
                0.5f,
                0.035f);
            confirmedVoiceSelectionMarkers[index] = marker;
            confirmedVoiceSelectionMaterials[index] = material;
        }
        UpdateVoiceTargetMarker(
            ref voiceCommandMarker,
            ref voiceCommandMaterial,
            voiceCommandPieceId,
            "Voice Command Receiver",
            new Color(0.1f, 1f, 0.25f, 1f),
            0.34f,
            0.04f);
    }

    private void UpdateVoiceTargetMarker(
        ref GameObject markerObject,
        ref Material markerMaterial,
        int pieceId,
        string markerName,
        Color color,
        float radiusScale,
        float heightScale)
    {
        if (pieceId < 0 ||
            !networkPieceVisuals.TryGetValue((ushort)pieceId, out NetworkPieceVisual visual) ||
            visual.Instance == null)
        {
            if (markerObject != null)
            {
                markerObject.SetActive(false);
            }

            return;
        }

        if (markerObject == null)
        {
            markerObject = new GameObject(markerName);
            LineRenderer lineRenderer = markerObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = true;
            lineRenderer.positionCount = 48;
            lineRenderer.startWidth = Mathf.Min(fileSpacing, rankSpacing) * 0.035f;
            lineRenderer.endWidth = lineRenderer.startWidth;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            Shader shader = Shader.Find("Sprites/Default");

            if (shader != null)
            {
                markerMaterial = new Material(shader) { color = color };
                lineRenderer.sharedMaterial = markerMaterial;
            }
        }

        float squareSize = Mathf.Min(fileSpacing, rankSpacing);
        float radius = squareSize * radiusScale;
        Vector3 centre = visual.TargetPosition + BoardUp * (squareSize * heightScale);
        LineRenderer marker = markerObject.GetComponent<LineRenderer>();

        for (int index = 0; index < marker.positionCount; index++)
        {
            float angle = index * Mathf.PI * 2f / marker.positionCount;
            marker.SetPosition(
                index,
                centre + BoardRight * (Mathf.Cos(angle) * radius) +
                BoardForward * (Mathf.Sin(angle) * radius));
        }

        markerObject.SetActive(true);
    }

    private Quaternion GetNetworkPieceRotation(
        GameObject prefab,
        NetworkChessPieceState pieceState)
    {
        Quaternion rotation = GetPieceWorldRotation(
            pieceState.OwnerTeam,
            prefab,
            pieceState.VoiceHeading);
        float fallProgress = GetRingOutProgress(pieceState, out Vector2 outwardDirection);

        if (fallProgress <= 0f)
        {
            return rotation;
        }

        Vector3 worldOutward =
            BoardRight * outwardDirection.x + BoardForward * outwardDirection.y;
        Vector3 tiltAxis = Vector3.Cross(worldOutward, BoardUp).normalized;
        return Quaternion.AngleAxis(ringOutTiltAngle * fallProgress, tiltAxis) * rotation;
    }

    private Vector3 GetNetworkPiecePosition(NetworkChessPieceState pieceState)
    {
        Vector3 position =
            GetBoardWorldPosition(pieceState.BoardFile, pieceState.BoardRank);
        float fallProgress = GetRingOutProgress(pieceState, out _);
        float squareSize = Mathf.Min(fileSpacing, rankSpacing);
        return position - BoardUp *
            (fallProgress * fallProgress * ringOutDropDistance * squareSize);
    }

    private float GetRingOutProgress(
        NetworkChessPieceState pieceState,
        out Vector2 outwardDirection)
    {
        Vector2 position = new(pieceState.BoardFile, pieceState.BoardRank);
        Vector2 closestPoint = new(
            Mathf.Clamp(position.x, GroundMinimumCoordinate, GroundMaximumCoordinate),
            Mathf.Clamp(position.y, GroundMinimumCoordinate, GroundMaximumCoordinate));
        Vector2 outsideOffset = position - closestPoint;
        float outsideDistance = outsideOffset.magnitude;
        outwardDirection = outsideDistance > 0.0001f
            ? outsideOffset / outsideDistance
            : Vector2.zero;
        return Mathf.Clamp01(outsideDistance / ringOutVisualDistance);
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

        if (voiceSelectionMaterial != null)
        {
            Destroy(voiceSelectionMaterial);
        }

        foreach (Material material in confirmedVoiceSelectionMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        if (voiceCommandMaterial != null)
        {
            Destroy(voiceCommandMaterial);
        }

        if (whiteHeadingArrowMaterial != null)
        {
            Destroy(whiteHeadingArrowMaterial);
        }

        if (blackHeadingArrowMaterial != null)
        {
            Destroy(blackHeadingArrowMaterial);
        }

        if (movementCooldownMaterial != null)
        {
            Destroy(movementCooldownMaterial);
        }

        if (movementCooldownBackgroundMaterial != null)
        {
            Destroy(movementCooldownBackgroundMaterial);
        }

        if (headingArrowMesh != null)
        {
            Destroy(headingArrowMesh);
        }
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

    private void SpawnConfiguredPosition(
        GameModeConfiguration configuration,
        bool includePlayerCommanderKings)
    {
        int index = 0;

        foreach (InitialPiecePlacement placement in configuration.InitialPlacements)
        {
            bool isPlayerCommanderPreview =
                includePlayerCommanderKings &&
                configuration.Victory.UsesPlayerCommander &&
                placement?.PieceType == ChessPieceType.King;

            if (placement == null ||
                !placement.Enabled ||
                (!configuration.ShouldSpawnBoardPiece(placement) &&
                 !isPlayerCommanderPreview) ||
                placement.PieceType == ChessPieceType.None ||
                (placement.Team != PlayerTeam.White &&
                 placement.Team != PlayerTeam.Black))
            {
                continue;
            }

            GameObject prefab = GetPiecePrefab(placement.Team, placement.PieceType);
            Vector3 rotationOffset = placement.Team == PlayerTeam.White
                ? whiteRotationOffset
                : blackRotationOffset;
            Vector2 boardPosition = placement.BoardPosition;
            string roleName = isPlayerCommanderPreview
                ? "PlayerKingPreview"
                : placement.PieceType.ToString();
            SpawnPiece(
                prefab,
                $"{placement.Team}_{roleName}_{index++:00}_" +
                $"({boardPosition.x:F1},{boardPosition.y:F1})",
                boardPosition.x,
                boardPosition.y,
                rotationOffset,
                registerSingleSquare: false);
        }
    }

    private void SpawnPiece(
        GameObject prefab,
        string instanceName,
        float file,
        float rank,
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
            GetBoardWorldPosition(file, rank) +
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
            int squareFile = Mathf.RoundToInt(file);
            int squareRank = Mathf.RoundToInt(rank);

            if ((uint)squareFile < 8 &&
                (uint)squareRank < 8 &&
                Mathf.Approximately(file, squareFile) &&
                Mathf.Approximately(rank, squareRank))
            {
                spawnedPieces[squareFile, squareRank] = instance;
            }
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

    private GameModeConfiguration ResolveCentralConfiguration()
    {
        NetworkChessGame game = FindFirstObjectByType<NetworkChessGame>();

        if (game != null && game.GameMode != null)
        {
            return game.GameMode;
        }

        return previewGameMode;
    }

    private void ApplyCentralConfiguration()
    {
        ApplyCentralConfiguration(ResolveCentralConfiguration());
    }

    private void ApplyCentralConfiguration(GameModeConfiguration configuration)
    {
        BoardPresentationSettings settings = configuration?.BoardPresentation;

        if (settings == null)
        {
            return;
        }

        whitePieces = settings.WhitePieces ?? whitePieces;
        blackPieces = settings.BlackPieces ?? blackPieces;
        anchor = settings.Anchor;
        placementPlane = settings.PlacementPlane;
        layoutYawOffset = settings.LayoutYawOffset;
        fileSpacing = settings.FileSpacing;
        rankSpacing = settings.RankSpacing;
        heightOffset = settings.HeightOffset;
        boardBorderWidthInSquares = settings.BoardBorderWidthInSquares;
        whiteRotationOffset = settings.WhiteRotationOffset;
        blackRotationOffset = settings.BlackRotationOffset;
        ringOutVisualDistance = settings.RingOutVisualDistance;
        ringOutDropDistance = settings.RingOutDropDistance;
        ringOutTiltAngle = settings.RingOutTiltAngle;
        selectionMarkerColor = settings.SelectionMarkerColor;
        selectionMarkerSegments = settings.SelectionMarkerSegments;
        voiceHoverMarkerColor = settings.VoiceHoverMarkerColor;
        confirmedVoiceMarkerColor = settings.ConfirmedVoiceMarkerColor;
        whiteHeadingArrowColor = settings.FriendlyHeadingArrowColor;
        blackHeadingArrowColor = settings.EnemyHeadingArrowColor;
        headingArrowLengthInSquares = settings.HeadingArrowLengthInSquares;
        headingArrowWidthInSquares = settings.HeadingArrowWidthInSquares;
        headingArrowHeightInSquares = settings.HeadingArrowHeightInSquares;
        generateOnStart = settings.GenerateOnStart;
    }

    private void OnValidate()
    {
        fileSpacing = Mathf.Max(0.001f, fileSpacing);
        rankSpacing = Mathf.Max(0.001f, rankSpacing);
        boardBorderWidthInSquares = Mathf.Max(0f, boardBorderWidthInSquares);
        ringOutVisualDistance = Mathf.Max(0.1f, ringOutVisualDistance);
        ringOutDropDistance = Mathf.Max(0.1f, ringOutDropDistance);
        ringOutTiltAngle = Mathf.Clamp(ringOutTiltAngle, 0f, 120f);
        selectionMarkerSegments = Mathf.Clamp(selectionMarkerSegments, 16, 96);
        headingArrowLengthInSquares = Mathf.Clamp(
            headingArrowLengthInSquares,
            0.3f,
            1f);
        headingArrowWidthInSquares = Mathf.Clamp(
            headingArrowWidthInSquares,
            0.02f,
            0.15f);
        headingArrowHeightInSquares = Mathf.Clamp(
            headingArrowHeightInSquares,
            0.005f,
            0.1f);
    }
}
