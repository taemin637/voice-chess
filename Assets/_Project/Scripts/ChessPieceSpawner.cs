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
    private static readonly string[] BackRankNames =
    {
        "Rook", "Knight", "Bishop", "Queen", "King", "Bishop", "Knight", "Rook"
    };

    private readonly GameObject[,] spawnedPieces = new GameObject[8, 8];

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

    public Transform PlacementOrigin => placementOrigin != null ? placementOrigin : transform;

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
        if ((uint)file >= 8 || (uint)rank >= 8)
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
        Vector3 centre = GetSquareWorldPosition(file, rank) + up * (squareSize * 0.025f);
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
        ClearGeneratedPieces();
        CreateGeneratedRoot();

        foreach (NetworkChessPieceState pieceState in pieceStates)
        {
            GameObject prefab = GetPiecePrefab(
                pieceState.OwnerTeam,
                pieceState.PieceType);

            if (prefab == null)
            {
                continue;
            }

            Vector3 rotationOffset = pieceState.OwnerTeam == PlayerTeam.White
                ? whiteRotationOffset
                : blackRotationOffset;

            SpawnPiece(
                prefab,
                $"{pieceState.OwnerTeam}_{pieceState.PieceType}_" +
                $"{GetSquareName(pieceState.File, pieceState.Rank)}_" +
                $"Depth{pieceState.StackDepth}_Id{pieceState.Id}",
                pieceState.File,
                pieceState.Rank,
                rotationOffset,
                pieceState.StackDepth * stackHeight,
                registerSingleSquare: false);
        }
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
