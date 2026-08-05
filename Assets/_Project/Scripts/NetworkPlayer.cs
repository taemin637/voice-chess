using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum PlayerTeam
{
    Unassigned,
    White,
    Black
}

public sealed class NetworkPlayer : NetworkBehaviour
{
    private const int MaxPlayersPerTeam = 2;

    private static readonly List<NetworkPlayer> SpawnedPlayers = new();

    private readonly NetworkVariable<PlayerTeam> _team = new(
        PlayerTeam.Unassigned,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _matchStarted = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private string _selectionStatus = "Choose a team.";

    public PlayerTeam Team => _team.Value;
    public string SelectionStatus => _selectionStatus;
    public bool IsOwnedByMe => IsOwner;
    public string DisplayName => $"Player {OwnerClientId + 1:00}";
    public static IReadOnlyList<NetworkPlayer> Players => SpawnedPlayers;

    public static bool MatchStarted
    {
        get
        {
            foreach (var player in SpawnedPlayers)
            {
                if (player != null && player.IsSpawned && player._matchStarted.Value)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static NetworkPlayer LocalPlayer
    {
        get
        {
            foreach (var player in SpawnedPlayers)
            {
                if (player != null && player.IsSpawned && player.IsOwner)
                {
                    return player;
                }
            }

            return null;
        }
    }

    public static bool TryGetByClientId(
        ulong clientId,
        out NetworkPlayer networkPlayer)
    {
        foreach (var player in SpawnedPlayers)
        {
            if (player != null &&
                player.IsSpawned &&
                player.OwnerClientId == clientId)
            {
                networkPlayer = player;
                return true;
            }
        }

        networkPlayer = null;
        return false;
    }

    public override void OnNetworkSpawn()
    {
        SpawnedPlayers.Add(this);
        SpawnedPlayers.Sort(
            (left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));

        if (IsServer && _team.Value == PlayerTeam.Unassigned)
        {
            AssignTeamAutomatically();
        }

        Debug.Log(
            $"NetworkPlayer spawned. " +
            $"Owner Client ID: {OwnerClientId}, " +
            $"Team: {_team.Value}, " +
            $"Is Owner: {IsOwner}, " +
            $"Is Server: {IsServer}");
    }

    public override void OnNetworkDespawn()
    {
        SpawnedPlayers.Remove(this);
    }

    public void SelectTeam(PlayerTeam requestedTeam)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        RequestTeamRpc(requestedTeam);
    }

    public void StartMatch()
    {
        if (!IsSpawned || !IsOwner || !IsServer)
        {
            return;
        }

        RequestStartMatchRpc();
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner)]
    private void RequestTeamRpc(PlayerTeam requestedTeam)
    {
        bool isValidTeam =
            requestedTeam == PlayerTeam.Unassigned ||
            requestedTeam == PlayerTeam.White ||
            requestedTeam == PlayerTeam.Black;

        if (!isValidTeam)
        {
            TeamSelectionResultRpc(false, requestedTeam);
            return;
        }

        if (requestedTeam != PlayerTeam.Unassigned &&
            CountOtherPlayersOnTeam(requestedTeam) >= MaxPlayersPerTeam)
        {
            TeamSelectionResultRpc(false, requestedTeam);
            return;
        }

        _team.Value = requestedTeam;
        TeamSelectionResultRpc(true, requestedTeam);
    }

    [Rpc(
        SendTo.Owner,
        InvokePermission = RpcInvokePermission.Server)]
    private void TeamSelectionResultRpc(
        bool accepted,
        PlayerTeam requestedTeam)
    {
        _selectionStatus = accepted
            ? $"Selected {requestedTeam}."
            : $"{requestedTeam} team is full.";
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner)]
    private void RequestStartMatchRpc()
    {
        if (IsServer)
        {
            _matchStarted.Value = true;
        }
    }

    private int CountOtherPlayersOnTeam(PlayerTeam team)
    {
        int count = 0;

        foreach (var player in SpawnedPlayers)
        {
            if (player != null &&
                player != this &&
                player.IsSpawned &&
                player.Team == team)
            {
                count++;
            }
        }

        return count;
    }

    private void AssignTeamAutomatically()
    {
        int whiteCount = CountOtherPlayersOnTeam(PlayerTeam.White);
        int blackCount = CountOtherPlayersOnTeam(PlayerTeam.Black);

        PlayerTeam assignedTeam;

        if (whiteCount >= MaxPlayersPerTeam && blackCount >= MaxPlayersPerTeam)
        {
            Debug.LogWarning(
                $"Could not automatically assign Player {OwnerClientId + 1:00}: " +
                "both teams are full.");
            return;
        }

        if (whiteCount >= MaxPlayersPerTeam)
        {
            assignedTeam = PlayerTeam.Black;
        }
        else if (blackCount >= MaxPlayersPerTeam)
        {
            assignedTeam = PlayerTeam.White;
        }
        else
        {
            // White wins an empty or tied lobby. Otherwise choose the smaller team.
            assignedTeam = whiteCount <= blackCount
                ? PlayerTeam.White
                : PlayerTeam.Black;
        }

        _team.Value = assignedTeam;

        Debug.Log(
            $"Automatically assigned Player {OwnerClientId + 1:00} to " +
            $"{assignedTeam} (White: {whiteCount}, Black: {blackCount}).");
    }
}
