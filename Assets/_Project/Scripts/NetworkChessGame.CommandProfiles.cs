using System.Collections.Generic;
using UnityEngine;

public sealed partial class NetworkChessGame
{
    private int _localHoveredPieceId = -1;
    private int _localConfirmedPieceId = -1;
    private readonly List<ushort> _localConfirmedPieceIds = new();
    private bool _localChargeAimValid;
    private Vector2 _localChargeAimBoardPosition;
    private Vector3 _localChargeAimWorldPosition;
    private GameObject _localChargeLaserObject;
    private LineRenderer _localChargeLaser;
    private Material _localChargeLaserMaterial;
    private float _localChargeLaserExpiresAt;
    private GameObject _localVoiceChargeArrowObject;
    private LineRenderer _localVoiceChargeArrow;
    private Material _localVoiceChargeArrowMaterial;
    private float _localVoiceChargePreviewCost;
    private float _localVoiceChargePreviewPower;
    private float _localVoiceChargePreviewDistance;

    public float LocalVoiceChargePreviewCost => _localVoiceChargePreviewCost;
    public float LocalVoiceChargePreviewPower => _localVoiceChargePreviewPower;
    public float LocalVoiceChargePreviewDistance => _localVoiceChargePreviewDistance;

    public bool HasLocalConfirmedSelection =>
        _localConfirmedPieceIds.Count > 0;
    public int LocalConfirmedSelectionCount => _localConfirmedPieceIds.Count;

    public bool ConfirmLocalVoiceSelection(ushort pieceId, out string rejection)
    {
        rejection = string.Empty;

        if (ActiveVoiceCommandVersion != VoiceCommandVersion.ConfirmedSelectionCharge)
        {
            rejection = "확정 선택은 신규 명령 방식에서만 사용합니다.";
            return false;
        }

        if (!TryGetLocalPlayer(out NetworkPlayer localPlayer))
        {
            rejection = "로컬 플레이어를 찾지 못했습니다.";
            return false;
        }

        int pieceIndex = FindPieceIndexById(pieceId);

        if (pieceIndex < 0 || _pieces[pieceIndex].OwnerTeam != localPlayer.Team)
        {
            rejection = "자기 팀의 말만 확정 선택할 수 있습니다.";
            return false;
        }

        ClearLocalVoiceChargePreview();

        int existingIndex = _localConfirmedPieceIds.IndexOf(pieceId);

        if (existingIndex >= 0)
        {
            _localConfirmedPieceIds.RemoveAt(existingIndex);
        }
        else
        {
            int maximum = gameMode?.Commands.MaximumConfirmedSelections ?? 3;

            while (_localConfirmedPieceIds.Count >= maximum)
            {
                _localConfirmedPieceIds.RemoveAt(0);
            }

            _localConfirmedPieceIds.Add(pieceId);
        }

        UpdateConfirmedSelectionState();
        pieceSpawner?.SetVoiceSelectionTarget(null);
        RecordLocalVoiceGazeSample();
        return true;
    }

    private void UpdateConfirmedSelectionState()
    {
        _localConfirmedPieceId = _localConfirmedPieceIds.Count > 0
            ? _localConfirmedPieceIds[0]
            : -1;
        _localVoiceTargetPieceId = _localConfirmedPieceId;
        pieceSpawner?.SetConfirmedVoiceSelectionTargets(_localConfirmedPieceIds);
    }

    private void PruneLocalConfirmedSelections()
    {
        for (int index = _localConfirmedPieceIds.Count - 1; index >= 0; index--)
        {
            if (FindPieceIndexById(_localConfirmedPieceIds[index]) < 0)
            {
                _localConfirmedPieceIds.RemoveAt(index);
            }
        }

        UpdateConfirmedSelectionState();
    }

    private void ClearLocalConfirmedSelection()
    {
        ClearLocalVoiceChargePreview();
        _localConfirmedPieceIds.Clear();
        _localConfirmedPieceId = -1;
        pieceSpawner?.SetConfirmedVoiceSelectionTargets(_localConfirmedPieceIds);

        if (ActiveVoiceCommandVersion == VoiceCommandVersion.ConfirmedSelectionCharge)
        {
            _localVoiceTargetPieceId = -1;
        }
    }

    private void ClearLocalConfirmedSelection(ushort commandedPieceId)
    {
        if (_localConfirmedPieceIds.Contains(commandedPieceId))
        {
            ClearLocalConfirmedSelection();
        }
    }

    public void UpdateLocalChargeAim(Ray viewRay, PlayerTeam localTeam)
    {
        if (ActiveVoiceCommandVersion != VoiceCommandVersion.ConfirmedSelectionCharge ||
            !TryResolveLocalChargeAim(
                viewRay,
                localTeam,
                out _localChargeAimBoardPosition,
                out _localChargeAimWorldPosition))
        {
            ClearLocalChargeAim();
            return;
        }

        _localChargeAimValid = true;
    }

    private void ClearLocalChargeAim()
    {
        _localChargeAimValid = false;
        _localChargeAimBoardPosition = default;
        _localChargeAimWorldPosition = default;
    }

    private bool TryResolveLocalChargeAim(
        Ray viewRay,
        PlayerTeam localTeam,
        out Vector2 boardPosition,
        out Vector3 worldPosition)
    {
        boardPosition = default;
        worldPosition = default;

        if (pieceSpawner == null || localTeam == PlayerTeam.Unassigned)
        {
            return false;
        }

        float squareSize = Mathf.Min(pieceSpawner.FileSpacing, pieceSpawner.RankSpacing);
        float maximumDistance = (gameMode?.Commands.ChargeLaserRangeInSquares ?? 30f) *
            squareSize;
        float bestDistance = maximumDistance;
        bool found = false;

        for (int index = 0; index < _pieces.Count; index++)
        {
            if (!pieceSpawner.TryGetNetworkPieceWorldBounds(
                    _pieces[index].Id,
                    out Bounds bounds) ||
                !TryUseRayBoundsHit(viewRay, bounds, ref bestDistance, out Vector3 hit))
            {
                continue;
            }

            worldPosition = hit;
            found = true;
        }

        foreach (NetworkPlayer player in NetworkPlayer.Players)
        {
            if (player == null ||
                !player.IsSpawned ||
                player.IsEliminated ||
                player.Team == PlayerTeam.Unassigned ||
                player.Team == localTeam ||
                !player.TryGetAvatarWorldBounds(out Bounds bounds) ||
                bounds.Contains(viewRay.origin) ||
                !TryUseRayBoundsHit(viewRay, bounds, ref bestDistance, out Vector3 hit))
            {
                continue;
            }

            worldPosition = hit;
            found = true;
        }

        Plane boardPlane = new(
            pieceSpawner.BoardUp,
            pieceSpawner.GetBoardWorldPosition(0f, 0f));

        if (boardPlane.Raycast(viewRay, out float boardDistance) &&
            boardDistance > 0.001f &&
            boardDistance < bestDistance)
        {
            Vector3 hit = viewRay.GetPoint(boardDistance);
            GetUnclampedBoardCoordinates(hit, out float file, out float rank);

            if (IsInsideArena(file, rank))
            {
                bestDistance = boardDistance;
                worldPosition = hit;
                found = true;
            }
        }

        TryUseArenaWallHit(
            viewRay,
            pieceSpawner.GetBoardWorldPosition(pieceSpawner.GroundMinimumCoordinate, 3.5f),
            pieceSpawner.BoardRight,
            true,
            ref bestDistance,
            ref found,
            ref worldPosition);
        TryUseArenaWallHit(
            viewRay,
            pieceSpawner.GetBoardWorldPosition(pieceSpawner.GroundMaximumCoordinate, 3.5f),
            -pieceSpawner.BoardRight,
            true,
            ref bestDistance,
            ref found,
            ref worldPosition);
        TryUseArenaWallHit(
            viewRay,
            pieceSpawner.GetBoardWorldPosition(3.5f, pieceSpawner.GroundMinimumCoordinate),
            pieceSpawner.BoardForward,
            false,
            ref bestDistance,
            ref found,
            ref worldPosition);
        TryUseArenaWallHit(
            viewRay,
            pieceSpawner.GetBoardWorldPosition(3.5f, pieceSpawner.GroundMaximumCoordinate),
            -pieceSpawner.BoardForward,
            false,
            ref bestDistance,
            ref found,
            ref worldPosition);

        if (!found)
        {
            return false;
        }

        GetUnclampedBoardCoordinates(
            worldPosition,
            out float targetFile,
            out float targetRank);
        boardPosition = new Vector2(
            Mathf.Clamp(
                targetFile,
                pieceSpawner.GroundMinimumCoordinate,
                pieceSpawner.GroundMaximumCoordinate),
            Mathf.Clamp(
                targetRank,
                pieceSpawner.GroundMinimumCoordinate,
                pieceSpawner.GroundMaximumCoordinate));
        return true;
    }

    private static bool TryUseRayBoundsHit(
        Ray ray,
        Bounds bounds,
        ref float bestDistance,
        out Vector3 hit)
    {
        hit = default;

        if (!bounds.IntersectRay(ray, out float distance) ||
            distance <= 0.001f ||
            distance >= bestDistance)
        {
            return false;
        }

        bestDistance = distance;
        hit = ray.GetPoint(distance);
        return true;
    }

    private void TryUseArenaWallHit(
        Ray ray,
        Vector3 point,
        Vector3 normal,
        bool validateRank,
        ref float bestDistance,
        ref bool found,
        ref Vector3 bestWorldPosition)
    {
        Plane wall = new(normal, point);

        if (!wall.Raycast(ray, out float distance) ||
            distance <= 0.001f ||
            distance >= bestDistance)
        {
            return;
        }

        Vector3 hit = ray.GetPoint(distance);
        GetUnclampedBoardCoordinates(hit, out float file, out float rank);
        float otherCoordinate = validateRank ? rank : file;

        if (otherCoordinate < pieceSpawner.GroundMinimumCoordinate ||
            otherCoordinate > pieceSpawner.GroundMaximumCoordinate)
        {
            return;
        }

        bestDistance = distance;
        found = true;
        bestWorldPosition = hit;
    }

    private bool IsInsideArena(float file, float rank)
    {
        return file >= pieceSpawner.GroundMinimumCoordinate &&
            file <= pieceSpawner.GroundMaximumCoordinate &&
            rank >= pieceSpawner.GroundMinimumCoordinate &&
            rank <= pieceSpawner.GroundMaximumCoordinate;
    }

    private void GetUnclampedBoardCoordinates(
        Vector3 worldPosition,
        out float file,
        out float rank)
    {
        pieceSpawner.TryGetBoardCoordinates(worldPosition, out file, out rank);
    }

    public void UpdateLocalVoiceChargePreview(
        float voicedDurationSeconds,
        float normalizedLoudness,
        float pronunciationScore,
        bool useStoredAim = false,
        Vector2 storedAimBoardPosition = default)
    {
        CommandEconomySettings settings = gameMode?.Commands;

        if (settings == null ||
            !settings.UsesVoiceDurationCost ||
            ActiveVoiceCommandVersion != VoiceCommandVersion.ConfirmedSelectionCharge ||
            _localConfirmedPieceIds.Count == 0)
        {
            ClearLocalVoiceChargePreview();
            return;
        }

        int pieceIndex = FindPieceIndexById((ushort)_localConfirmedPieceId);

        if (pieceIndex < 0)
        {
            ClearLocalVoiceChargePreview();
            return;
        }

        NetworkChessPieceState piece = _pieces[pieceIndex];
        _localVoiceChargePreviewCost = settings.CostSystemEnabled
            ? GetVoiceChargeCost(
                piece.PieceType,
                voicedDurationSeconds,
                _localConfirmedPieceIds.Count)
            : 0f;
        _localVoiceChargePreviewPower = settings.GetVoiceChargePower(
            voicedDurationSeconds,
            normalizedLoudness,
            pronunciationScore);
        _localVoiceChargePreviewDistance = GetVoiceChargeDistance(
            piece.PieceType,
            _localVoiceChargePreviewPower);

        bool previewAimValid = useStoredAim || _localChargeAimValid;
        Vector2 previewAimBoardPosition = useStoredAim
            ? storedAimBoardPosition
            : _localChargeAimBoardPosition;

        if (voicedDurationSeconds <= 0f ||
            !previewAimValid ||
            pieceSpawner == null ||
            _localVoiceChargePreviewDistance <= 0f)
        {
            HideLocalVoiceChargeArrow();
            return;
        }

        Vector2 piecePosition = new(piece.BoardFile, piece.BoardRank);
        Vector2 boardDirection = previewAimBoardPosition - piecePosition;

        if (boardDirection.sqrMagnitude < 0.0001f)
        {
            HideLocalVoiceChargeArrow();
            return;
        }

        boardDirection.Normalize();
        Vector2 arrowEndBoard = piecePosition +
            boardDirection * _localVoiceChargePreviewDistance;
        float squareSize = Mathf.Min(pieceSpawner.FileSpacing, pieceSpawner.RankSpacing);
        float height = settings.VoiceChargeArrowHeightInSquares * squareSize;
        Vector3 start = pieceSpawner.GetBoardWorldPosition(
            piecePosition.x,
            piecePosition.y) + pieceSpawner.BoardUp * height;
        Vector3 end = pieceSpawner.GetBoardWorldPosition(
            arrowEndBoard.x,
            arrowEndBoard.y) + pieceSpawner.BoardUp * height;
        EnsureLocalVoiceChargeArrow();

        if (_localVoiceChargeArrow == null)
        {
            return;
        }

        Vector3 forward = end - start;
        float worldLength = forward.magnitude;
        forward = worldLength > 0.0001f ? forward / worldLength : pieceSpawner.BoardForward;
        Vector3 side = Vector3.Cross(pieceSpawner.BoardUp, forward).normalized;
        float headLength = Mathf.Min(
            worldLength * settings.VoiceChargeArrowHeadLengthRatio,
            squareSize * 0.9f);
        float headWidth = headLength * 0.55f;
        Vector3 headBase = end - forward * headLength;
        Color color = settings.VoiceChargeArrowColor;
        float width = settings.VoiceChargeArrowWidthInSquares * squareSize;
        _localVoiceChargeArrow.startWidth = width;
        _localVoiceChargeArrow.endWidth = width;
        _localVoiceChargeArrow.startColor = color;
        _localVoiceChargeArrow.endColor = color;

        if (_localVoiceChargeArrowMaterial != null)
        {
            _localVoiceChargeArrowMaterial.color = color;
        }

        _localVoiceChargeArrow.SetPosition(0, start);
        _localVoiceChargeArrow.SetPosition(1, end);
        _localVoiceChargeArrow.SetPosition(2, headBase + side * headWidth);
        _localVoiceChargeArrow.SetPosition(3, end);
        _localVoiceChargeArrow.SetPosition(4, headBase - side * headWidth);
        _localVoiceChargeArrowObject.SetActive(true);
    }

    public void ClearLocalVoiceChargePreview()
    {
        _localVoiceChargePreviewCost = 0f;
        _localVoiceChargePreviewPower = 0f;
        _localVoiceChargePreviewDistance = 0f;
        HideLocalVoiceChargeArrow();
    }

    private void EnsureLocalVoiceChargeArrow()
    {
        if (_localVoiceChargeArrowObject != null)
        {
            return;
        }

        _localVoiceChargeArrowObject = new GameObject("Local Voice Charge Arrow");
        _localVoiceChargeArrow = _localVoiceChargeArrowObject.AddComponent<LineRenderer>();
        _localVoiceChargeArrow.useWorldSpace = true;
        _localVoiceChargeArrow.positionCount = 5;
        _localVoiceChargeArrow.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        _localVoiceChargeArrow.receiveShadows = false;
        Shader shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            _localVoiceChargeArrowMaterial = new Material(shader);
            _localVoiceChargeArrow.sharedMaterial = _localVoiceChargeArrowMaterial;
        }
    }

    private void HideLocalVoiceChargeArrow()
    {
        if (_localVoiceChargeArrowObject != null)
        {
            _localVoiceChargeArrowObject.SetActive(false);
        }
    }

    private void ShowLocalChargeLaser(Vector2 targetBoardPosition)
    {
        if (pieceSpawner == null)
        {
            return;
        }

        if (_localChargeLaserObject == null)
        {
            _localChargeLaserObject = new GameObject("Local Charge Laser");
            _localChargeLaser = _localChargeLaserObject.AddComponent<LineRenderer>();
            _localChargeLaser.useWorldSpace = true;
            _localChargeLaser.positionCount = 2;
            _localChargeLaser.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            _localChargeLaser.receiveShadows = false;
            Shader shader = Shader.Find("Sprites/Default");

            if (shader != null)
            {
                _localChargeLaserMaterial = new Material(shader);
                _localChargeLaser.sharedMaterial = _localChargeLaserMaterial;
            }
        }

        CommandEconomySettings settings = gameMode?.Commands;
        Color color = settings?.ChargeLaserColor ??
            new Color(1f, 0.35f, 0.08f, 0.95f);
        float width = (settings?.ChargeLaserWidthInSquares ?? 0.025f) *
            Mathf.Min(pieceSpawner.FileSpacing, pieceSpawner.RankSpacing);
        Vector3 start = Camera.main != null
            ? Camera.main.transform.position
            : pieceSpawner.GetBoardWorldPosition(
                _localCommanderFile,
                _localCommanderRank);
        Vector3 end = _localChargeAimValid &&
            Vector2.Distance(targetBoardPosition, _localChargeAimBoardPosition) < 0.01f
            ? _localChargeAimWorldPosition
            : pieceSpawner.GetBoardWorldPosition(
                targetBoardPosition.x,
                targetBoardPosition.y);
        _localChargeLaser.startWidth = width;
        _localChargeLaser.endWidth = width;
        _localChargeLaser.startColor = color;
        _localChargeLaser.endColor = color;

        if (_localChargeLaserMaterial != null)
        {
            _localChargeLaserMaterial.color = color;
        }

        _localChargeLaser.SetPosition(0, start);
        _localChargeLaser.SetPosition(1, end);
        _localChargeLaserObject.SetActive(true);
        _localChargeLaserExpiresAt = Time.unscaledTime +
            (settings?.ChargeLaserVisibleSeconds ?? 0.2f);
    }

    private void UpdateLocalChargeLaserVisual()
    {
        if (_localChargeLaserObject != null &&
            _localChargeLaserObject.activeSelf &&
            Time.unscaledTime >= _localChargeLaserExpiresAt)
        {
            _localChargeLaserObject.SetActive(false);
        }
    }

    private void CleanupLocalChargeLaser()
    {
        ClearLocalVoiceChargePreview();

        if (_localChargeLaserObject != null)
        {
            Destroy(_localChargeLaserObject);
            _localChargeLaserObject = null;
            _localChargeLaser = null;
        }

        if (_localChargeLaserMaterial != null)
        {
            Destroy(_localChargeLaserMaterial);
            _localChargeLaserMaterial = null;
        }

        if (_localVoiceChargeArrowObject != null)
        {
            Destroy(_localVoiceChargeArrowObject);
            _localVoiceChargeArrowObject = null;
            _localVoiceChargeArrow = null;
        }

        if (_localVoiceChargeArrowMaterial != null)
        {
            Destroy(_localVoiceChargeArrowMaterial);
            _localVoiceChargeArrowMaterial = null;
        }
    }
}
