using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class NetworkChessGame
{
    private readonly struct CaptureTimerKey : IEquatable<CaptureTimerKey>
    {
        public readonly ushort ZoneIndex;
        public readonly ushort PieceId;

        public CaptureTimerKey(ushort zoneIndex, ushort pieceId)
        {
            ZoneIndex = zoneIndex;
            PieceId = pieceId;
        }

        public bool Equals(CaptureTimerKey other)
        {
            return ZoneIndex == other.ZoneIndex && PieceId == other.PieceId;
        }

        public override bool Equals(object obj)
        {
            return obj is CaptureTimerKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ZoneIndex, PieceId);
        }
    }

    private sealed class CaptureZoneVisual
    {
        public GameObject Root;
        public Mesh FillMesh;
        public Material FillMaterial;
        public Material OutlineMaterial;
    }

    private readonly Dictionary<CaptureTimerKey, float> _capturePieceTimers = new();
    private readonly HashSet<CaptureTimerKey> _activeCaptureTimers = new();
    private readonly HashSet<ushort> _liveCapturePieces = new();
    private readonly List<CaptureTimerKey> _captureTimersToRemove = new();
    private readonly List<CaptureZoneVisual> _captureZoneVisuals = new();
    private Transform _captureZoneVisualRoot;

    public bool IsCaptureModeEnabled =>
        gameMode != null && gameMode.CaptureMode.Enabled;
    public CaptureScoringRule ActiveCaptureScoringRule => gameMode != null
        ? gameMode.CaptureMode.ScoringRule
        : CaptureScoringRule.PeriodicPerPiece;

    private void InitializeCaptureZones()
    {
        _capturePieceTimers.Clear();
        _activeCaptureTimers.Clear();
        _liveCapturePieces.Clear();
        _captureZoneStates.Clear();

        if (!IsCaptureModeEnabled)
        {
            return;
        }

        IReadOnlyList<CaptureZoneSettings> zones = gameMode.CaptureMode.Zones;

        for (int index = 0; index < zones.Count; index++)
        {
            CaptureZoneSettings settings = zones[index];

            if (settings != null && settings.Enabled)
            {
                _captureZoneStates.Add(new CaptureZoneNetworkState((ushort)index));
            }
        }
    }

    private void UpdateCaptureZones(float deltaTime)
    {
        if (!IsCaptureModeEnabled || _captureZoneStates.Count == 0)
        {
            return;
        }

        CaptureModeSettings captureMode = gameMode.CaptureMode;
        bool periodicScoring =
            captureMode.ScoringRule == CaptureScoringRule.PeriodicPerPiece;
        _activeCaptureTimers.Clear();
        _liveCapturePieces.Clear();
        float whitePeriodicAward = 0f;
        float blackPeriodicAward = 0f;
        float whiteFinalValue = 0f;
        float blackFinalValue = 0f;

        for (int pieceIndex = 0; pieceIndex < _pieces.Count; pieceIndex++)
        {
            _liveCapturePieces.Add(_pieces[pieceIndex].Id);
        }

        for (int stateIndex = 0; stateIndex < _captureZoneStates.Count; stateIndex++)
        {
            CaptureZoneNetworkState previousState = _captureZoneStates[stateIndex];

            if (previousState.Index >= captureMode.Zones.Count)
            {
                continue;
            }

            CaptureZoneSettings zone = captureMode.Zones[previousState.Index];

            if (zone == null || !zone.Enabled)
            {
                continue;
            }

            int whiteCount = 0;
            int blackCount = 0;
            float zoneWhiteValue = 0f;
            float zoneBlackValue = 0f;

            for (int pieceIndex = 0; pieceIndex < _pieces.Count; pieceIndex++)
            {
                NetworkChessPieceState piece = _pieces[pieceIndex];

                if (!IsPieceInsideCaptureZone(piece, zone))
                {
                    continue;
                }

                PieceArchetypeSettings pieceSettings = GetPieceSettings(
                    piece.PieceType);

                if (piece.OwnerTeam == PlayerTeam.White)
                {
                    whiteCount++;
                    zoneWhiteValue += pieceSettings.FinalCaptureValue;
                }
                else if (piece.OwnerTeam == PlayerTeam.Black)
                {
                    blackCount++;
                    zoneBlackValue += pieceSettings.FinalCaptureValue;
                }

                if (!periodicScoring || pieceSettings.PeriodicCapturePoints <= 0f)
                {
                    continue;
                }

                CaptureTimerKey timerKey = new(previousState.Index, piece.Id);
                _activeCaptureTimers.Add(timerKey);
                _capturePieceTimers.TryGetValue(timerKey, out float elapsed);
                elapsed += Mathf.Max(0f, deltaTime);
                float interval = pieceSettings.PeriodicCaptureIntervalSeconds;
                int completedIntervals = Mathf.FloorToInt(elapsed / interval);

                if (completedIntervals > 0)
                {
                    float award = completedIntervals *
                        pieceSettings.PeriodicCapturePoints;
                    elapsed -= completedIntervals * interval;

                    if (piece.OwnerTeam == PlayerTeam.White)
                    {
                        whitePeriodicAward += award;
                    }
                    else if (piece.OwnerTeam == PlayerTeam.Black)
                    {
                        blackPeriodicAward += award;
                    }
                }

                _capturePieceTimers[timerKey] = elapsed;
            }

            CaptureZoneNetworkState newState = new(previousState.Index)
            {
                WhitePieceCount = (ushort)Mathf.Min(ushort.MaxValue, whiteCount),
                BlackPieceCount = (ushort)Mathf.Min(ushort.MaxValue, blackCount),
                WhiteOccupancyValue = zoneWhiteValue,
                BlackOccupancyValue = zoneBlackValue
            };

            if (!previousState.Equals(newState))
            {
                _captureZoneStates[stateIndex] = newState;
            }

            whiteFinalValue += zoneWhiteValue;
            blackFinalValue += zoneBlackValue;
        }

        if (periodicScoring)
        {
            if (whitePeriodicAward > 0f)
            {
                _whiteCaptureScore.Value += whitePeriodicAward;
            }

            if (blackPeriodicAward > 0f)
            {
                _blackCaptureScore.Value += blackPeriodicAward;
            }
        }
        else
        {
            SetCaptureScore(PlayerTeam.White, whiteFinalValue);
            SetCaptureScore(PlayerTeam.Black, blackFinalValue);
        }

        CleanupCapturePieceTimers(captureMode.ResetPeriodicTimerWhenLeaving);
    }

    private static bool IsPieceInsideCaptureZone(
        NetworkChessPieceState piece,
        CaptureZoneSettings zone)
    {
        Vector2 position = new(piece.BoardFile, piece.BoardRank);
        return (position - zone.BoardPosition).sqrMagnitude <=
               zone.RadiusInSquares * zone.RadiusInSquares;
    }

    private void CleanupCapturePieceTimers(bool resetWhenLeaving)
    {
        _captureTimersToRemove.Clear();

        foreach (KeyValuePair<CaptureTimerKey, float> pair in _capturePieceTimers)
        {
            bool shouldRemove = !_liveCapturePieces.Contains(pair.Key.PieceId) ||
                (resetWhenLeaving && !_activeCaptureTimers.Contains(pair.Key));

            if (shouldRemove)
            {
                _captureTimersToRemove.Add(pair.Key);
            }
        }

        foreach (CaptureTimerKey key in _captureTimersToRemove)
        {
            _capturePieceTimers.Remove(key);
        }
    }

    private void SetCaptureScore(PlayerTeam team, float value)
    {
        value = Mathf.Max(0f, value);

        if (team == PlayerTeam.White &&
            !Mathf.Approximately(_whiteCaptureScore.Value, value))
        {
            _whiteCaptureScore.Value = value;
        }
        else if (team == PlayerTeam.Black &&
                 !Mathf.Approximately(_blackCaptureScore.Value, value))
        {
            _blackCaptureScore.Value = value;
        }
    }

    private void RefreshFinalOccupancyScores()
    {
        if (IsCaptureModeEnabled &&
            gameMode.CaptureMode.ScoringRule ==
            CaptureScoringRule.FinalOccupancyValue)
        {
            UpdateCaptureZones(0f);
        }
    }

    private void RebuildCaptureZoneVisuals()
    {
        CleanupCaptureZoneVisuals();

        if (!IsCaptureModeEnabled || pieceSpawner == null)
        {
            return;
        }

        GameObject rootObject = new("Capture Zone Visuals");
        rootObject.transform.SetParent(transform, worldPositionStays: false);
        _captureZoneVisualRoot = rootObject.transform;
        IReadOnlyList<CaptureZoneSettings> zones = gameMode.CaptureMode.Zones;

        for (int index = 0; index < zones.Count; index++)
        {
            CaptureZoneSettings zone = zones[index];

            if (zone != null && zone.Enabled)
            {
                _captureZoneVisuals.Add(CreateCaptureZoneVisual(zone, index));
            }
        }
    }

    private CaptureZoneVisual CreateCaptureZoneVisual(
        CaptureZoneSettings zone,
        int index)
    {
        float squareSize = Mathf.Min(pieceSpawner.FileSpacing, pieceSpawner.RankSpacing);
        float radius = zone.RadiusInSquares * squareSize;
        GameObject root = new($"Capture Zone {index + 1:00} - {zone.DisplayName}");
        root.transform.SetParent(_captureZoneVisualRoot, worldPositionStays: true);
        root.transform.SetPositionAndRotation(
            pieceSpawner.GetBoardWorldPosition(
                zone.BoardPosition.x,
                zone.BoardPosition.y) +
            pieceSpawner.BoardUp * (zone.HeightOffsetInSquares * squareSize),
            Quaternion.LookRotation(pieceSpawner.BoardForward, pieceSpawner.BoardUp));

        CaptureZoneVisual visual = new() { Root = root };
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (zone.ShowFilledCircle && shader != null)
        {
            GameObject fill = new("Fill");
            fill.transform.SetParent(root.transform, worldPositionStays: false);
            MeshFilter meshFilter = fill.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fill.AddComponent<MeshRenderer>();
            visual.FillMesh = CreateCaptureDiscMesh(radius, zone.CircleSegments);
            visual.FillMaterial = new Material(shader)
            {
                name = $"{zone.DisplayName} Capture Fill",
                color = zone.FillColor
            };
            meshFilter.sharedMesh = visual.FillMesh;
            meshRenderer.sharedMaterial = visual.FillMaterial;
            meshRenderer.sortingOrder = -20;
        }

        GameObject outline = new("Outline");
        outline.transform.SetParent(root.transform, worldPositionStays: false);
        LineRenderer line = outline.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = zone.CircleSegments;
        line.startWidth = zone.OutlineWidthInSquares * squareSize;
        line.endWidth = line.startWidth;
        line.startColor = zone.OutlineColor;
        line.endColor = zone.OutlineColor;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;

        if (shader != null)
        {
            visual.OutlineMaterial = new Material(shader)
            {
                name = $"{zone.DisplayName} Capture Outline",
                color = zone.OutlineColor
            };
            line.sharedMaterial = visual.OutlineMaterial;
        }

        for (int segment = 0; segment < zone.CircleSegments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / zone.CircleSegments;
            line.SetPosition(
                segment,
                new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        return visual;
    }

    private static Mesh CreateCaptureDiscMesh(float radius, int segments)
    {
        Mesh mesh = new() { name = "Capture Zone Disc" };
        Vector3[] vertices = new Vector3[segments + 1];
        int[] triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;

        for (int segment = 0; segment < segments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / segments;
            vertices[segment + 1] = new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius);
            int triangle = segment * 3;
            triangles[triangle] = 0;
            triangles[triangle + 1] = (segment + 1) % segments + 1;
            triangles[triangle + 2] = segment + 1;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void CleanupCaptureZoneVisuals()
    {
        foreach (CaptureZoneVisual visual in _captureZoneVisuals)
        {
            if (visual.FillMesh != null)
            {
                Destroy(visual.FillMesh);
            }

            if (visual.FillMaterial != null)
            {
                Destroy(visual.FillMaterial);
            }

            if (visual.OutlineMaterial != null)
            {
                Destroy(visual.OutlineMaterial);
            }
        }

        _captureZoneVisuals.Clear();

        if (_captureZoneVisualRoot != null)
        {
            Destroy(_captureZoneVisualRoot.gameObject);
            _captureZoneVisualRoot = null;
        }
    }

    private void OnDrawGizmos()
    {
        if (!IsCaptureModeEnabled)
        {
            return;
        }

        ChessPieceSpawner spawner = pieceSpawner;

        if (spawner == null)
        {
            spawner = FindFirstObjectByType<ChessPieceSpawner>();
        }

        if (spawner == null)
        {
            return;
        }

        float squareSize = Mathf.Min(spawner.FileSpacing, spawner.RankSpacing);

        foreach (CaptureZoneSettings zone in gameMode.CaptureMode.Zones)
        {
            if (zone == null || !zone.Enabled)
            {
                continue;
            }

            Gizmos.color = zone.OutlineColor;
            Vector3 centre = spawner.GetBoardWorldPosition(
                zone.BoardPosition.x,
                zone.BoardPosition.y) +
                spawner.BoardUp * (zone.HeightOffsetInSquares * squareSize);
            Vector3 previous = centre +
                spawner.BoardRight * (zone.RadiusInSquares * squareSize);

            for (int segment = 1; segment <= zone.CircleSegments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / zone.CircleSegments;
                Vector3 next = centre +
                    spawner.BoardRight *
                    (Mathf.Cos(angle) * zone.RadiusInSquares * squareSize) +
                    spawner.BoardForward *
                    (Mathf.Sin(angle) * zone.RadiusInSquares * squareSize);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
