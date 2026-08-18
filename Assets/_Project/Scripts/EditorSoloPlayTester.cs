#if UNITY_EDITOR
using System.Collections;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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

        if (!_networkManager.IsListening)
        {
            if (!TryConfigureAvailableSoloPort(out ushort soloPort))
            {
                Debug.LogError(
                    "[Editor Solo] 사용 가능한 로컬 UDP 포트를 준비하지 못했습니다.",
                    this);
                yield break;
            }

            if (!_networkManager.StartHost())
            {
                Debug.LogError(
                    $"[Editor Solo] 로컬 호스트 시작에 실패했습니다 (UDP {soloPort}).",
                    this);
                yield break;
            }

            Debug.Log(
                $"[Editor Solo] 로컬 호스트를 UDP {soloPort}에서 시작했습니다.",
                this);
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

    private bool TryConfigureAvailableSoloPort(out ushort port)
    {
        port = 0;
        UnityTransport transport = _networkManager.GetComponent<UnityTransport>();

        if (transport == null)
        {
            Debug.LogError(
                "[Editor Solo] NetworkManager에서 UnityTransport를 찾지 못했습니다.",
                this);
            return false;
        }

        try
        {
            using UdpClient portProbe = new(AddressFamily.InterNetwork);
            portProbe.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            port = (ushort)((IPEndPoint)portProbe.Client.LocalEndPoint).Port;
            transport.SetConnectionData(
                forceOverrideCommandLineArgs: true,
                ipv4Address: IPAddress.Loopback.ToString(),
                port: port,
                listenAddress: IPAddress.Loopback.ToString());
            return true;
        }
        catch (SocketException exception)
        {
            Debug.LogError(
                $"[Editor Solo] 로컬 UDP 포트 선택 실패: {exception.Message}",
                this);
            return false;
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

        if (keyboard.f10Key.wasPressedThisFrame)
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
        if (_networkManager != null && _networkManager.IsListening)
        {
            _networkManager.Shutdown();
        }

        IsActive = false;
        DummyPlayerTeam = PlayerTeam.Unassigned;
    }
}
#endif
