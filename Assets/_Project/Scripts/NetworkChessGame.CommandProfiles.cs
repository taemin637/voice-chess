using UnityEngine;

public sealed partial class NetworkChessGame
{
    private int _localHoveredPieceId = -1;
    private int _localConfirmedPieceId = -1;
    private bool _localChargeAimValid;
    private Vector2 _localChargeAimBoardPosition;
    private Vector3 _localChargeAimWorldPosition;
    private GameObject _localChargeLaserObject;
    private LineRenderer _localChargeLaser;
    private Material _localChargeLaserMaterial;
    private float _localChargeLaserExpiresAt;

    public bool HasLocalConfirmedSelection =>
        _localConfirmedPieceId >= 0 &&
        FindPieceIndexById((ushort)_localConfirmedPieceId) >= 0;

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

        _localConfirmedPieceId = pieceId;
        _localVoiceTargetPieceId = pieceId;
        pieceSpawner?.SetVoiceSelectionTarget(null);
        pieceSpawner?.SetConfirmedVoiceSelectionTarget(pieceId);
        RecordLocalVoiceGazeSample();
        return true;
    }

    private void ClearLocalConfirmedSelection()
    {
        _localConfirmedPieceId = -1;
        pieceSpawner?.SetConfirmedVoiceSelectionTarget(null);

        if (ActiveVoiceCommandVersion == VoiceCommandVersion.ConfirmedSelectionCharge)
        {
            _localVoiceTargetPieceId = -1;
        }
    }

    private void ClearLocalConfirmedSelection(ushort commandedPieceId)
    {
        if (_localConfirmedPieceId == commandedPieceId)
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
    }
}
