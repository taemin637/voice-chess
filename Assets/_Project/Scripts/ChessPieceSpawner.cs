using System;
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

    [Header("Rotation")]
    [Tooltip("Rotation added to every White prefab, relative to the reference object.")]
    [SerializeField] private Vector3 whiteRotationOffset;
    [Tooltip("Rotation added to every Black prefab, relative to the reference object.")]
    [SerializeField] private Vector3 blackRotationOffset = new(0f, 180f, 0f);

    [Header("Generation")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField, HideInInspector] private Transform generatedRoot;

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
        Vector3 rotationOffset)
    {
        if (prefab == null)
        {
            return;
        }

        Quaternion rotation =
            PlacementRotation * Quaternion.Euler(rotationOffset) * prefab.transform.rotation;
        GameObject instance;

#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
        {
            instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, generatedRoot);
            instance.transform.SetPositionAndRotation(GetSquareWorldPosition(file, rank), rotation);
        }
        else
#endif
        {
            instance = Instantiate(
                prefab,
                GetSquareWorldPosition(file, rank),
                rotation,
                generatedRoot);
        }

        instance.name = instanceName;
    }

    private static string GetSquareName(int file, int rank)
    {
        return $"{(char)('a' + file)}{rank + 1}";
    }

    private void OnValidate()
    {
        fileSpacing = Mathf.Max(0.001f, fileSpacing);
        rankSpacing = Mathf.Max(0.001f, rankSpacing);
    }
}
