#if UNITY_EDITOR
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the real Netcode host flow locally so editor play mode can enter a
/// one-player match without Unity Services, Relay, a lobby, or an MPPM clone.
/// </summary>
[DisallowMultipleComponent]
public sealed class EditorSoloPlayTester : MonoBehaviour
{
    private NetworkManager _networkManager;
    private NetworkChessGame _game;
    private EditorSoloTestSettings _settings;

    public static bool IsActive { get; private set; }
    public static PlayerTeam DummyPlayerTeam { get; private set; } =
        PlayerTeam.Unassigned;

    public static bool HasLivingDummyPlayer(PlayerTeam team)
    {
        return IsActive && team == DummyPlayerTeam;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != "SampleScene" ||
            FindFirstObjectByType<EditorSoloPlayTester>() != null)
        {
            return;
        }

        NetworkChessGame game = FindFirstObjectByType<NetworkChessGame>();
        EditorSoloTestSettings settings = game?.GameMode?.EditorSoloTest;

        if (settings == null || !settings.Enabled)
        {
            return;
        }

        new GameObject("Editor Solo Play Tester")
            .AddComponent<EditorSoloPlayTester>();
    }

    private void Awake()
    {
        IsActive = true;
        NetworkChessGame game = FindFirstObjectByType<NetworkChessGame>();
        PlayerTeam playerTeam = game?.GameMode?.EditorSoloTest.PlayerTeam ??
            PlayerTeam.White;
        DummyPlayerTeam = playerTeam == PlayerTeam.Black
            ? PlayerTeam.White
            : PlayerTeam.Black;
    }

    private IEnumerator Start()
    {
        _game = FindFirstObjectByType<NetworkChessGame>();
        _settings = _game?.GameMode?.EditorSoloTest;
        _networkManager = NetworkManager.Singleton != null
            ? NetworkManager.Singleton
            : FindFirstObjectByType<NetworkManager>();

        if (_settings == null || !_settings.Enabled || _networkManager == null)
        {
            yield break;
        }

        if (!_networkManager.IsListening && !_networkManager.StartHost())
        {
            yield break;
        }

        if (!_networkManager.IsHost)
        {
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + _settings.StartupTimeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)
        {
            _game ??= FindFirstObjectByType<NetworkChessGame>();

            if (_game != null &&
                _game.IsSpawned &&
                NetworkPlayer.LocalPlayer != null &&
                NetworkPlayer.LocalPlayer.IsSpawned)
            {
                break;
            }

            yield return null;
        }

        NetworkPlayer localPlayer = NetworkPlayer.LocalPlayer;

        if (_game == null || !_game.IsSpawned || localPlayer == null)
        {
            yield break;
        }

        if (localPlayer.Team != _settings.PlayerTeam)
        {
            localPlayer.SelectTeam(_settings.PlayerTeam);

            while (Time.realtimeSinceStartup < deadline &&
                   localPlayer.Team != _settings.PlayerTeam)
            {
                yield return null;
            }
        }

        if (localPlayer.Team == _settings.PlayerTeam &&
            !NetworkPlayer.MatchStarted)
        {
            localPlayer.StartMatch();
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null ||
            _game == null ||
            !NetworkPlayer.MatchStarted)
        {
            return;
        }

        if (keyboard.backquoteKey.wasPressedThisFrame)
        {
            _game.EditorForceFinishMatchAtTimeLimit();
            return;
        }

        if (_game.CommandMode == CommandIssuingMode.AlternatingTurns &&
            WasForceNextTurnPressed(keyboard))
        {
            _game.EditorForceAdvanceTurn();
        }
    }

    private static bool WasForceNextTurnPressed(Keyboard keyboard)
    {
        bool shiftedEquals = keyboard.equalsKey.wasPressedThisFrame &&
            (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        return keyboard.numpadPlusKey.wasPressedThisFrame || shiftedEquals;
    }

    private void OnDestroy()
    {
        IsActive = false;
        DummyPlayerTeam = PlayerTeam.Unassigned;
    }
}
#endif
