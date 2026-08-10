using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// Owns the complete multiplayer front-end: home, server browser and team lobby.
/// The UI intentionally stays runtime-generated so the scene requires no extra setup.
/// </summary>
public sealed class SessionManager : MonoBehaviour
{
    private static SessionManager _instance;

    private enum ScreenView
    {
        Home,
        Browser,
        Lobby
    }

    private const float DesignWidth = 1600f;
    private const float DesignHeight = 900f;
    private const int MaxPlayers = 4;
    private const int SessionListPollingIntervalSeconds = 5;

    private readonly List<Texture2D> _generatedTextures = new();

    private ISession _session;
    private QuerySessionsResults _queryResults;
    private NetworkChessGame _chessGame;
    private ScreenView _screen = ScreenView.Home;
    private Vector2 _sessionListScroll;

    private bool _servicesReady;
    private bool _requestInProgress;
    private bool _statusIsError;
    private bool _showLobbyOverMatch;
    private string _status = "Connecting to online services...";
    private string _joinCode = string.Empty;
    private string _searchText = string.Empty;

    private Texture2D _backgroundTexture;
    private GUIStyle _panel;
    private GUIStyle _panelSoft;
    private GUIStyle _row;
    private GUIStyle _accentButton;
    private GUIStyle _secondaryButton;
    private GUIStyle _ghostButton;
    private GUIStyle _lightButton;
    private GUIStyle _dangerButton;
    private GUIStyle _input;
    private GUIStyle _title;
    private GUIStyle _heroTitle;
    private GUIStyle _subtitle;
    private GUIStyle _body;
    private GUIStyle _muted;
    private GUIStyle _small;
    private GUIStyle _badge;
    private GUIStyle _teamWhitePanel;
    private GUIStyle _teamBlackPanel;
    private GUIStyle _whiteTeamTitle;
    private GUIStyle _blackTeamTitle;

    public static bool IsFrontEndVisible =>
        _instance != null &&
        (!NetworkPlayer.MatchStarted ||
         _instance._showLobbyOverMatch ||
         _instance.IsGameOver());

    private void Awake()
    {
        _instance = this;
    }

    private async void Start()
    {
        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            _servicesReady = true;
            SetStatus("Online services are ready.");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not connect: {exception.Message}", true);
            Debug.LogException(exception);
        }
    }

    private void OnGUI()
    {
        EnsureStyles();
        GUI.depth = -1000;

        if (NetworkPlayer.MatchStarted && !_showLobbyOverMatch)
        {
            NetworkChessGame chessGame = ResolveChessGame();

            if (chessGame != null &&
                chessGame.IsGameOver &&
                chessGame.IsGameOverPresentationReady)
            {
                DrawGameOverOverlay(chessGame);
            }
            else if (chessGame != null && !chessGame.IsGameOver)
            {
                DrawMatchTimer(chessGame.RemainingTime);
            }

            return;
        }

        DrawFullScreenBackground();

        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
        float offsetX = (Screen.width - DesignWidth * scale) * 0.5f;
        float offsetY = (Screen.height - DesignHeight * scale) * 0.5f;

        GUI.matrix = Matrix4x4.TRS(
            new Vector3(offsetX, offsetY, 0f),
            Quaternion.identity,
            new Vector3(scale, scale, 1f));

        HandleKeyboardNavigation();

        switch (_screen)
        {
            case ScreenView.Home:
                DrawHome();
                break;
            case ScreenView.Browser:
                DrawBrowser();
                break;
            case ScreenView.Lobby:
                DrawLobby();
                break;
        }

        GUI.matrix = previousMatrix;
    }

    private void DrawMatchTimer(float remainingTime)
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
        float offsetX = (Screen.width - DesignWidth * scale) * 0.5f;
        float offsetY = (Screen.height - DesignHeight * scale) * 0.5f;

        GUI.matrix = Matrix4x4.TRS(
            new Vector3(offsetX, offsetY, 0f),
            Quaternion.identity,
            new Vector3(scale, scale, 1f));

        int seconds = Mathf.CeilToInt(remainingTime);
        Rect timerPanel = new(675f, 30f, 250f, 78f);
        DrawShadowedPanel(timerPanel, _panel);
        GUI.Label(
            timerPanel,
            $"{seconds / 60:00}:{seconds % 60:00}",
            _heroTitle);

        GUI.matrix = previousMatrix;
    }

    private void DrawGameOverOverlay(NetworkChessGame chessGame)
    {
        DrawFullScreenBackground();

        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
        float offsetX = (Screen.width - DesignWidth * scale) * 0.5f;
        float offsetY = (Screen.height - DesignHeight * scale) * 0.5f;

        GUI.matrix = Matrix4x4.TRS(
            new Vector3(offsetX, offsetY, 0f),
            Quaternion.identity,
            new Vector3(scale, scale, 1f));

        Rect panel = new(420f, 165f, 760f, 570f);
        DrawShadowedPanel(panel, _panel);
        PlayerTeam winner = chessGame.Winner;
        GUI.Label(
            new Rect(480f, 235f, 640f, 70f),
            winner == PlayerTeam.Unassigned
                ? "DRAW"
                : $"{winner.ToString().ToUpperInvariant()} TEAM WINS",
            _heroTitle);

        int whiteRemaining = chessGame.GetRemainingPieceCount(PlayerTeam.White);
        int blackRemaining = chessGame.GetRemainingPieceCount(PlayerTeam.Black);
        int whiteKills = chessGame.GetKilledPieceCount(PlayerTeam.White);
        int blackKills = chessGame.GetKilledPieceCount(PlayerTeam.Black);
        GUI.Label(
            new Rect(480f, 315f, 640f, 38f),
            $"WHITE  |  REMAINING {whiteRemaining}  |  KILLED {whiteKills}",
            _subtitle);
        GUI.Label(
            new Rect(480f, 355f, 640f, 38f),
            $"BLACK  |  REMAINING {blackRemaining}  |  KILLED {blackKills}",
            _subtitle);

        NetworkPlayer localPlayer = NetworkPlayer.LocalPlayer;
        bool isHost = localPlayer != null && localPlayer.IsServer;

        if (isHost)
        {
            if (DrawButton(
                    new Rect(500f, 445f, 600f, 76f),
                    "PLAY AGAIN",
                    _accentButton))
            {
                localPlayer.StartMatch();
            }

            if (DrawButton(
                    new Rect(500f, 545f, 600f, 76f),
                    "RETURN TO LOBBY",
                    _secondaryButton))
            {
                _screen = ScreenView.Lobby;
                _showLobbyOverMatch = false;
                localPlayer.ReturnToLobby();
            }
        }
        else
        {
            GUI.Label(
                new Rect(480f, 475f, 640f, 80f),
                "Waiting for the host to choose the next round.",
                _subtitle);
        }

        GUI.matrix = previousMatrix;
    }

    private bool IsGameOver()
    {
        NetworkChessGame chessGame = ResolveChessGame();
        return chessGame != null &&
               chessGame.IsGameOver;
    }

    private NetworkChessGame ResolveChessGame()
    {
        if (_chessGame == null)
        {
            _chessGame = FindFirstObjectByType<NetworkChessGame>();
        }

        return _chessGame;
    }

    private void DrawHome()
    {
        bool canRequest = _servicesReady && !_requestInProgress && _session == null;

        if (DrawButton(
                new Rect(400f, 320f, 800f, 112f),
                "CREATE SERVER",
                _accentButton,
                canRequest))
        {
            _ = CreateRelaySessionAsync();
        }

        if (DrawButton(
                new Rect(400f, 468f, 800f, 112f),
                "JOIN SERVER",
                _secondaryButton,
                !_requestInProgress))
        {
            OpenBrowser();
        }
    }

    private void DrawBrowser()
    {
        if (DrawButton(new Rect(90f, 72f, 170f, 56f), "BACK", _ghostButton))
        {
            CloseBrowser();
            return;
        }

        Rect browser = new(250f, 160f, 1100f, 590f);
        DrawShadowedPanel(browser, _panel);

        GUI.Label(new Rect(300f, 205f, 700f, 58f), "PUBLIC SERVERS", _title);

        if (DrawButton(
                new Rect(1130f, 205f, 170f, 58f),
                "REFRESH",
                _secondaryButton,
                _servicesReady && !_requestInProgress))
        {
            _ = RefreshSessionListAsync();
        }

        DrawServerList(new Rect(300f, 295f, 1000f, 400f));
    }

    private void DrawServerList(Rect rect)
    {
        if (_queryResults == null || _queryResults.Sessions.Count == 0)
        {
            return;
        }

        const float rowHeight = 84f;
        Rect content = new(
            0f,
            0f,
            rect.width - 20f,
            _queryResults.Sessions.Count * (rowHeight + 12f));
        _sessionListScroll = GUI.BeginScrollView(rect, _sessionListScroll, content);

        for (int index = 0; index < _queryResults.Sessions.Count; index++)
        {
            var sessionInfo = _queryResults.Sessions[index];
            float y = index * (rowHeight + 12f);
            Rect rowRect = new(0f, y, content.width, rowHeight);
            GUI.Box(rowRect, GUIContent.none, _row);

            GUI.Label(
                new Rect(30f, y + 25f, 600f, 34f),
                $"Room {index + 1}",
                _body);

            int playerCount =
                sessionInfo.MaxPlayers - sessionInfo.AvailableSlots;
            GUI.Label(
                new Rect(content.width - 350f, y + 25f, 150f, 34f),
                $"{playerCount} / {sessionInfo.MaxPlayers}",
                _badge);

            bool canJoin =
                _servicesReady &&
                !_requestInProgress &&
                _session == null &&
                !sessionInfo.IsLocked &&
                sessionInfo.AvailableSlots > 0;

            if (DrawButton(
                    new Rect(content.width - 150f, y + 16f, 126f, 52f),
                    sessionInfo.AvailableSlots > 0 ? "JOIN" : "FULL",
                    _accentButton,
                    canJoin))
            {
                _ = JoinRelaySessionByIdAsync(sessionInfo.Id, sessionInfo.Name);
            }
        }

        GUI.EndScrollView();
    }

    private void DrawLobby()
    {
        if (_session == null)
        {
            _screen = ScreenView.Home;
            return;
        }

        if (DrawButton(
                new Rect(90f, 72f, 190f, 56f),
                _requestInProgress ? "LEAVING..." : "LEAVE ROOM",
                _dangerButton,
                !_requestInProgress))
        {
            _ = LeaveSessionAsync();
            return;
        }

        int whiteCount = CountPlayersOnTeam(PlayerTeam.White);
        int blackCount = CountPlayersOnTeam(PlayerTeam.Black);
        NetworkPlayer localPlayer = NetworkPlayer.LocalPlayer;
        bool canStart =
            _session.IsHost &&
            localPlayer != null &&
            whiteCount > 0 &&
            blackCount > 0 &&
            !_requestInProgress;

        string startText;
        if (NetworkPlayer.MatchStarted)
        {
            startText = "RETURN TO MATCH";
        }
        else if (_session.IsHost)
        {
            startText = canStart ? "START MATCH" : "FILL BOTH TEAMS TO START";
        }
        else
        {
            startText = "WAITING FOR HOST";
        }

        bool startEnabled = NetworkPlayer.MatchStarted || canStart;
        if (DrawButton(
                new Rect(540f, 130f, 520f, 72f),
                startText,
                _accentButton,
                startEnabled))
        {
            if (NetworkPlayer.MatchStarted)
            {
                _showLobbyOverMatch = false;
            }
            else
            {
                localPlayer.StartMatch();
            }
        }

        DrawTeamPanel(
            new Rect(120f, 260f, 650f, 440f),
            PlayerTeam.White,
            whiteCount);
        DrawTeamPanel(
            new Rect(830f, 260f, 650f, 440f),
            PlayerTeam.Black,
            blackCount);
    }

    private void DrawTeamPanel(Rect rect, PlayerTeam team, int teamCount)
    {
        bool isWhite = team == PlayerTeam.White;
        GUIStyle panelStyle = isWhite ? _teamWhitePanel : _teamBlackPanel;
        GUIStyle titleStyle = isWhite ? _whiteTeamTitle : _blackTeamTitle;
        GUIStyle playerTextStyle = isWhite ? _whiteTeamTitle : _blackTeamTitle;
        GUIStyle playerMetaStyle = _muted;
        NetworkPlayer localPlayer = NetworkPlayer.LocalPlayer;

        DrawShadowedPanel(rect, panelStyle);
        GUI.Label(
            new Rect(rect.x + 42f, rect.y + 55f, rect.width - 84f, 50f),
            isWhite ? "WHITE TEAM" : "BLACK TEAM",
            titleStyle);

        bool isSelected = localPlayer != null && localPlayer.Team == team;
        bool canSelect =
            localPlayer != null &&
            !NetworkPlayer.MatchStarted &&
            (isSelected || teamCount < 2);

        GUIStyle buttonStyle = isWhite ? _secondaryButton : _lightButton;

        if (DrawButton(
                new Rect(rect.x + 42f, rect.y + 145f, rect.width - 84f, 72f),
                "JOIN TEAM",
                buttonStyle,
                canSelect))
        {
            localPlayer.SelectTeam(team);
        }

        float playerY = rect.y + 250f;
        int slot = 0;

        foreach (NetworkPlayer player in NetworkPlayer.Players)
        {
            if (player == null || !player.IsSpawned || player.Team != team)
            {
                continue;
            }

            Rect playerRect = new(
                rect.x + 42f,
                playerY + slot * 82f,
                rect.width - 84f,
                66f);
            DrawSolid(
                playerRect,
                isWhite
                    ? new Color32(215, 215, 215, 255)
                    : new Color32(34, 34, 34, 255));
            DrawSolid(
                new Rect(playerRect.x, playerRect.y, 6f, playerRect.height),
                isWhite
                    ? new Color32(20, 20, 20, 255)
                    : new Color32(235, 235, 235, 255));
            GUI.Label(
                new Rect(playerRect.x + 24f, playerRect.y + 7f, 360f, 27f),
                player.DisplayName,
                playerTextStyle);
            GUI.Label(
                new Rect(playerRect.x + 24f, playerRect.y + 35f, 360f, 22f),
                player.IsOwnedByMe ? "YOU" : "CONNECTED",
                playerMetaStyle);
            slot++;
        }
    }

    private void DrawMatchLobbyButton()
    {
        GUIStyle previousSkinButton = GUI.skin.button;
        float width = 180f;
        Rect rect = new(Screen.width - width - 24f, 24f, width, 48f);

        if (GUI.Button(rect, "TEAM LOBBY", _secondaryButton))
        {
            _screen = ScreenView.Lobby;
            _showLobbyOverMatch = true;
        }

        GUI.skin.button = previousSkinButton;
    }

    private void DrawBrandHeader(string section)
    {
        GUI.Label(new Rect(90f, 38f, 320f, 48f), "VOICE CHESS", _title);
        GUI.Label(new Rect(1180f, 40f, 330f, 40f), section, _badge);
        DrawSolid(new Rect(90f, 92f, 1420f, 2f), new Color32(65, 65, 65, 255));
    }

    private void DrawEmptyState(Rect rect, string heading, string message)
    {
        GUI.Box(rect, GUIContent.none, _panelSoft);
        GUI.Label(new Rect(rect.x + 30f, rect.y + 105f, rect.width - 60f, 34f), heading, _title);
        GUI.Label(new Rect(rect.x + 30f, rect.y + 150f, rect.width - 60f, 34f), message, _muted);
    }

    private void DrawStatus(Rect rect)
    {
        Color color = _statusIsError
            ? new Color32(255, 255, 255, 255)
            : _servicesReady
                ? new Color32(205, 205, 205, 255)
                : new Color32(135, 135, 135, 255);

        DrawSolid(new Rect(rect.x, rect.y + 12f, 10f, 10f), color);
        GUI.Label(new Rect(rect.x + 24f, rect.y, rect.width - 24f, rect.height), _status, _muted);
    }

    private void DrawShadowedPanel(Rect rect, GUIStyle style)
    {
        GUI.Box(new Rect(rect.x + 10f, rect.y + 12f, rect.width, rect.height), GUIContent.none, _panelSoft);
        GUI.Box(rect, GUIContent.none, style);
    }

    private void DrawFullScreenBackground()
    {
        GUI.matrix = Matrix4x4.identity;
        GUI.DrawTexture(
            new Rect(0f, 0f, Screen.width, Screen.height),
            _backgroundTexture,
            ScaleMode.StretchToFill);

        DrawSolid(
            new Rect(0f, Screen.height * 0.72f, Screen.width, Screen.height * 0.28f),
            new Color32(92, 92, 92, 255));
    }

    private static void DrawSolid(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.color = previousColor;
    }

    private static void DrawOutline(Rect rect, Color color, float thickness)
    {
        DrawSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
        DrawSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        DrawSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
        DrawSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private bool DrawButton(Rect rect, string label, GUIStyle style, bool enabled = true)
    {
        bool previousEnabled = GUI.enabled;
        GUI.enabled = enabled;
        bool clicked = GUI.Button(rect, label, style);
        GUI.enabled = previousEnabled;
        return clicked;
    }

    private void HandleKeyboardNavigation()
    {
        Event current = Event.current;

        if (current.type != EventType.KeyDown || current.keyCode != KeyCode.Escape)
        {
            return;
        }

        if (_screen == ScreenView.Browser)
        {
            CloseBrowser();
        }
        else if (_screen == ScreenView.Lobby && NetworkPlayer.MatchStarted)
        {
            _showLobbyOverMatch = false;
        }

        current.Use();
    }

    private void OpenBrowser()
    {
        _screen = ScreenView.Browser;
        SetStatus(_servicesReady
            ? "Searching for public servers..."
            : "Waiting for online services...");

        if (_servicesReady && !_requestInProgress)
        {
            _ = RefreshSessionListAsync();
        }
    }

    private void CloseBrowser()
    {
        _queryResults?.StopPolling();
        _screen = ScreenView.Home;
        SetStatus(_servicesReady ? "Online services are ready." : "Connecting to online services...");
    }

    private bool SessionMatchesSearch(string sessionName)
    {
        return string.IsNullOrWhiteSpace(_searchText) ||
               sessionName.IndexOf(_searchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private int CountVisibleSessions()
    {
        if (_queryResults == null)
        {
            return 0;
        }

        int count = 0;

        foreach (var sessionInfo in _queryResults.Sessions)
        {
            if (SessionMatchesSearch(sessionInfo.Name))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountPlayersOnTeam(PlayerTeam team)
    {
        int count = 0;

        foreach (NetworkPlayer player in NetworkPlayer.Players)
        {
            if (player != null && player.IsSpawned && player.Team == team)
            {
                count++;
            }
        }

        return count;
    }

    private async Task CreateRelaySessionAsync()
    {
        _requestInProgress = true;
        SetStatus("Creating a public server...");

        try
        {
            string playerId = AuthenticationService.Instance.PlayerId;
            string shortId = playerId.Substring(0, Mathf.Min(6, playerId.Length)).ToUpperInvariant();
            var options = new SessionOptions
            {
                MaxPlayers = MaxPlayers,
                Name = $"Voice Chess - {shortId}",
                IsPrivate = false
            }.WithRelayNetwork();

            _queryResults?.StopPolling();
            _session = await MultiplayerService.Instance.CreateSessionAsync(options);
            _screen = ScreenView.Lobby;
            SetStatus($"Server created. Share code {_session.Code} with your team.");
            Debug.Log($"Session created: {_session.Id} / {_session.Code}");
        }
        catch (Exception exception)
        {
            SetStatus($"Server creation failed: {exception.Message}", true);
            Debug.LogException(exception);
        }
        finally
        {
            _requestInProgress = false;
        }
    }

    private async Task JoinRelaySessionByCodeAsync()
    {
        _requestInProgress = true;
        SetStatus($"Joining room {_joinCode}...");

        try
        {
            _queryResults?.StopPolling();
            _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(_joinCode);
            _screen = ScreenView.Lobby;
            SetStatus($"Joined {_session.Name}.");
            Debug.Log($"Session joined by code: {_session.Id} / {_session.Code}");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not join that room: {exception.Message}", true);
            Debug.LogException(exception);
        }
        finally
        {
            _requestInProgress = false;
        }
    }

    private async Task JoinRelaySessionByIdAsync(string sessionId, string sessionName)
    {
        _requestInProgress = true;
        SetStatus($"Joining {sessionName}...");

        try
        {
            _queryResults?.StopPolling();
            _session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId);
            _screen = ScreenView.Lobby;
            SetStatus($"Joined {sessionName}.");
            Debug.Log($"Session joined from browser: {_session.Id} / {_session.Code}");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not join {sessionName}: {exception.Message}", true);
            Debug.LogException(exception);
        }
        finally
        {
            _requestInProgress = false;
        }
    }

    private async Task RefreshSessionListAsync()
    {
        _requestInProgress = true;
        SetStatus("Refreshing public servers...");

        try
        {
            _queryResults?.StopPolling();
            _queryResults = await MultiplayerService.Instance.QuerySessionsAsync(
                new QuerySessionsOptions { Count = 20 });
            _queryResults.StartPolling(SessionListPollingIntervalSeconds);
            SetStatus(
                _queryResults.Sessions.Count == 0
                    ? "No public servers found. Try again in a moment."
                    : $"Found {_queryResults.Sessions.Count} public server(s). Auto-refresh is on.");
        }
        catch (Exception exception)
        {
            SetStatus($"Server search failed: {exception.Message}", true);
            Debug.LogException(exception);
        }
        finally
        {
            _requestInProgress = false;
        }
    }

    private async Task LeaveSessionAsync()
    {
        if (_session == null)
        {
            return;
        }

        _requestInProgress = true;
        SetStatus("Leaving the room...");

        try
        {
            ISession sessionToLeave = _session;
            await sessionToLeave.LeaveAsync();
            _session = null;
            _screen = ScreenView.Home;
            _showLobbyOverMatch = false;
            _joinCode = string.Empty;
            SetStatus("You left the room.");
        }
        catch (Exception exception)
        {
            SetStatus($"Could not leave the room: {exception.Message}", true);
            Debug.LogException(exception);
        }
        finally
        {
            _requestInProgress = false;
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        _status = message;
        _statusIsError = isError;
    }

    private void EnsureStyles()
    {
        if (_panel != null)
        {
            return;
        }

        _backgroundTexture = MakeTexture(new Color32(112, 112, 112, 255));

        _panel = MakeBoxStyle(new Color32(22, 22, 22, 255), 18);
        _panelSoft = MakeBoxStyle(new Color32(13, 13, 13, 255), 16);
        _row = MakeBoxStyle(new Color32(31, 31, 31, 255), 12);
        _teamWhitePanel = MakeBoxStyle(new Color32(238, 238, 238, 255), 18);
        _teamBlackPanel = MakeBoxStyle(new Color32(14, 14, 14, 255), 18);

        _accentButton = MakeButtonStyle(
            new Color32(238, 238, 238, 255),
            Color.white,
            new Color32(12, 12, 12, 255),
            22,
            14);
        _secondaryButton = MakeButtonStyle(
            new Color32(42, 42, 42, 255),
            new Color32(58, 58, 58, 255),
            Color.white,
            20,
            12);
        _ghostButton = MakeButtonStyle(
            new Color32(25, 25, 25, 255),
            new Color32(48, 48, 48, 255),
            new Color32(215, 215, 215, 255),
            16,
            10);
        _lightButton = MakeButtonStyle(
            new Color32(235, 235, 235, 255),
            Color.white,
            new Color32(18, 18, 18, 255),
            18,
            12);
        _dangerButton = MakeButtonStyle(
            new Color32(38, 38, 38, 255),
            new Color32(60, 60, 60, 255),
            new Color32(225, 225, 225, 255),
            16,
            10);

        _input = new GUIStyle(GUI.skin.textField)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(22, 18, 0, 0),
            normal =
            {
                background = MakeRoundedTexture(new Color32(8, 8, 8, 255), 14),
                textColor = Color.white
            },
            focused =
            {
                background = MakeRoundedTexture(new Color32(20, 20, 20, 255), 14),
                textColor = Color.white
            }
        };
        _input.border = new RectOffset(18, 18, 18, 18);

        _heroTitle = MakeLabelStyle(42, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        _title = MakeLabelStyle(25, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
        _subtitle = MakeLabelStyle(19, new Color32(180, 180, 180, 255), FontStyle.Normal, TextAnchor.MiddleCenter);
        _body = MakeLabelStyle(19, new Color32(238, 238, 238, 255), FontStyle.Bold, TextAnchor.MiddleLeft);
        _muted = MakeLabelStyle(16, new Color32(155, 155, 155, 255), FontStyle.Normal, TextAnchor.MiddleLeft);
        _small = MakeLabelStyle(13, new Color32(130, 130, 130, 255), FontStyle.Bold, TextAnchor.MiddleCenter);
        _badge = MakeLabelStyle(15, new Color32(220, 220, 220, 255), FontStyle.Bold, TextAnchor.MiddleCenter);
        _whiteTeamTitle = MakeLabelStyle(25, new Color32(20, 20, 20, 255), FontStyle.Bold, TextAnchor.MiddleLeft);
        _blackTeamTitle = MakeLabelStyle(25, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
    }

    private GUIStyle MakeBoxStyle(Color color, int radius)
    {
        Texture2D background = MakeRoundedTexture(color, radius);
        GUIStyle style = new(GUI.skin.box)
        {
            border = new RectOffset(20, 20, 20, 20)
        };
        style.normal.background = background;
        style.hover.background = background;
        style.active.background = background;
        style.focused.background = background;
        return style;
    }

    private GUIStyle MakeButtonStyle(
        Color normalColor,
        Color hoverColor,
        Color textColor,
        int fontSize,
        int radius)
    {
        GUIStyle style = new(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            border = new RectOffset(18, 18, 18, 18),
            normal =
            {
                background = MakeRoundedTexture(normalColor, radius),
                textColor = textColor
            },
            hover =
            {
                background = MakeRoundedTexture(hoverColor, radius),
                textColor = textColor
            },
            active =
            {
                background = MakeRoundedTexture(normalColor, radius),
                textColor = textColor
            },
            focused =
            {
                background = MakeRoundedTexture(hoverColor, radius),
                textColor = textColor
            }
        };
        return style;
    }

    private static GUIStyle MakeLabelStyle(
        int fontSize,
        Color textColor,
        FontStyle fontStyle,
        TextAnchor alignment)
    {
        GUIStyle style = new(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = fontStyle,
            alignment = alignment,
            wordWrap = true
        };

        style.normal.textColor = textColor;
        style.hover.textColor = textColor;
        style.active.textColor = textColor;
        style.focused.textColor = textColor;
        style.onNormal.textColor = textColor;
        style.onHover.textColor = textColor;
        style.onActive.textColor = textColor;
        style.onFocused.textColor = textColor;
        return style;
    }

    private Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        _generatedTextures.Add(texture);
        return texture;
    }

    private Texture2D MakeRoundedTexture(Color color, int radius)
    {
        const int size = 64;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color clear = new(color.r, color.g, color.b, 0f);
        float corner = radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nearestX = Mathf.Clamp(x, corner, size - 1f - corner);
                float nearestY = Mathf.Clamp(y, corner, size - 1f - corner);
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(nearestX, nearestY));
                texture.SetPixel(x, y, distance <= corner ? color : clear);
            }
        }

        texture.Apply();
        _generatedTextures.Add(texture);
        return texture;
    }

    private void OnDestroy()
    {
        _queryResults?.StopPolling();

        if (_instance == this)
        {
            _instance = null;
        }

        foreach (Texture2D texture in _generatedTextures)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
    }
}
