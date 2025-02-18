using static NSMB.Utils.NetworkUtils;
using NSMB.Utils;
using Photon.Deterministic;
using Photon.Realtime;
using Quantum;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using System.Text.RegularExpressions;
using NSMB.UI.MainMenu;

public class NetworkHandler : Singleton<NetworkHandler>, IMatchmakingCallbacks, IConnectionCallbacks {

    //---Events
    public static event Action<ClientState, ClientState> StateChanged;
    public static event Action<string> OnError;

    //---Constants
    public static readonly string RoomIdValidChars = "BCDFGHJKLMNPRQSTVWXYZ";
    private static readonly int RoomIdLength = 8;
    private static readonly List<DisconnectCause> NonErrorDisconnectCauses = new() {
        DisconnectCause.None, DisconnectCause.DisconnectByClientLogic, DisconnectCause.ApplicationQuit,
    };

    //---Static Variables
    public static RealtimeClient Client => Instance ? Instance.realtimeClient : null;
    public static long? Ping => Client?.RealtimePeer.Stats.RoundtripTime;
    public static QuantumRunner Runner { get; private set; }
    public static QuantumGame Game => Runner?.Game ?? QuantumRunner.DefaultGame;
    public static IEnumerable<Region> Regions => Client.RegionHandler.EnabledRegions.OrderBy(r => r.Code);
    public static string Region => Client?.CurrentRegion ?? Instance.lastRegion;
    public static bool IsReplay { get; private set; }
    public static int ReplayStart { get; private set; }
    public static int ReplayLength { get; private set; }
    public static int ReplayEnd => ReplayStart + ReplayLength;
    public static bool IsReplayFastForwarding { get; set; }
    public static string SavedRecordingPath { get; set; }
    public static List<byte[]> ReplayFrameCache => Instance.replayFrameCache;
    public static bool WasDisconnectedViaError { get; set; }

    //---Private Variables
    private RealtimeClient realtimeClient;
    private string lastRegion;
    private Coroutine pingUpdateCoroutine;
    private readonly List<byte[]> replayFrameCache = new();

    public void Awake() {
        Set(this);
        StateChanged += OnClientStateChanged;
        Settings.OnReplaysEnabledChanged += OnReplaysEnabledChanged;

        realtimeClient = new();
        realtimeClient.StateChanged += (ClientState oldState, ClientState newState) => {
            StateChanged?.Invoke(oldState, newState);
        };
        realtimeClient.AddCallbackTarget(this);

        QuantumCallback.Subscribe<CallbackSimulateFinished>(this, OnSimulateFinished);
        QuantumCallback.Subscribe<CallbackGameResynced>(this, OnGameResynced);
        QuantumCallback.Subscribe<CallbackGameDestroyed>(this, OnGameDestroyed);
        QuantumCallback.Subscribe<CallbackPluginDisconnect>(this, OnPluginDisconnect);
        QuantumEvent.Subscribe<EventGameStateChanged>(this, OnGameStateChanged);
        QuantumEvent.Subscribe<EventPlayerAdded>(this, OnPlayerAdded);
        QuantumEvent.Subscribe<EventRecordingStarted>(this, OnRecordingStarted);
        QuantumEvent.Subscribe<EventGameEnded>(this, OnGameEnded);
        QuantumEvent.Subscribe<EventRulesChanged>(this, OnRulesChanged);
    }

    public void Update() {
        if (Client != null && Client.IsConnectedAndReady) {
            Client.Service();
        }
    }

    public void OnDestroy() {
        StateChanged -= OnClientStateChanged;
        realtimeClient.RemoveCallbackTarget(this);
    }

    public void OnClientStateChanged(ClientState oldState, ClientState newState) {
        // Jesus christ
        GlobalController.Instance.connecting.SetActive(
            newState is ClientState.Authenticating
                or ClientState.ConnectWithFallbackProtocol
                or ClientState.ConnectingToNameServer
                or ClientState.ConnectingToMasterServer
                or ClientState.ConnectingToGameServer
                or ClientState.Disconnecting
                or ClientState.DisconnectingFromNameServer
                or ClientState.DisconnectingFromMasterServer
                or ClientState.DisconnectingFromGameServer
                or ClientState.ConnectedToMasterServer // We always join a lobby, so...
                or ClientState.Joining
                or ClientState.JoiningLobby
                or ClientState.Leaving
                or ClientState.ConnectedToNameServer // Include this since we can't do anything and will auto-disconnect anyway
                || (Client.State == ClientState.Joined && (!Runner || (Runner.Game == null) || Runner.Game.GetLocalPlayers().Count == 0))
        );
    }

    public IEnumerator PingUpdateCoroutine() {
        WaitForSeconds seconds = new(1);
        CommandUpdatePing pingCommand = new();
        while (true) {
            QuantumGame game;
            if (Runner && (game = Runner.Game) != null) {
                pingCommand.PingMs = (int) Ping.Value;
                foreach (int slot in game.GetLocalPlayerSlots()) {
                    game.SendCommand(slot, pingCommand);
                }
            }
            yield return seconds;
        }
    }

    public static Task Disconnect() {
        return Client.DisconnectAsync();
    }

    public static async Task<bool> ConnectToRegion(string region) {
        if (Client == null) {
            return false;
        }

        StateChanged?.Invoke(ClientState.Disconnected, ClientState.Authenticating);
        region ??= Instance.lastRegion;
        Instance.lastRegion = region;
        Client.AuthValues = await AuthenticationHandler.Authenticate();

        if (Client.AuthValues == null) {
            StateChanged?.Invoke(ClientState.ConnectingToMasterServer, ClientState.Disconnected);
            return false;
        }

        if (Client.IsConnected) {
            await Client.DisconnectAsync();
        }

        if (region == null) {
            Debug.Log("[Network] Connecting to the best available region");
        } else {
            Debug.Log($"[Network] Connecting to region {region}");
        }

        try {
            await Client.ConnectUsingSettingsAsync(new AppSettings {
                AppIdQuantum = "6b4b72d0-57c3-4991-96c1-f3f36f9548e5",
                AppVersion = Application.version[0..(Application.version.LastIndexOf('.') - 1)],
                EnableLobbyStatistics = true,
                AuthMode = AuthModeOption.Auth,
                FixedRegion = region,
            });
            await Client.JoinLobbyAsync(TypedLobby.Default);
            Instance.lastRegion = Client.CurrentRegion;
            return true;
        } catch {
            return false;
        }
    }

    public static async Task ConnectToRoomsRegion(string roomId) {
        int regionIndex = RoomIdValidChars.IndexOf(roomId.ToUpper()[0]);
        string targetRegion = Regions.ElementAt(regionIndex).Code;

        if (Client.CurrentRegion != targetRegion) {
            // await Client.DisconnectAsync();
            await ConnectToRegion(targetRegion);
        }
    }

    public static async Task<short> QuickPlay(EnterRoomArgs args = null) {
        args ??= new EnterRoomArgs();
        int maxAttempts = 3; // Maximum number of attempts to find and join a room
        int currentAttempt = 0;
        int retryDelayMs = 3000; // 3 second delay between attempts (room deletion takes a while i guess?)

        while (currentAttempt < maxAttempts) {
            if (currentAttempt > 0) {
                Debug.Log($"[Network] Waiting {retryDelayMs}ms before next quickplay join attempt");
                await Task.Delay(retryDelayMs);
            }

            // Try to find a suitable quickplay room
            var roomManager = GameObject.FindObjectOfType<RoomListManager>();
            var existingRoom = roomManager?.GetAvailableQuickplayRoom(args.RoomOptions.MaxPlayers);

            if (existingRoom != null) {
                args.RoomName = existingRoom.Name;
                short joinResult = await Client.JoinRoomAsync(args, false);

                if (joinResult == ErrorCode.GameDoesNotExist) {
                    Debug.Log($"[Network] Quickplay room {existingRoom.Name} was deleted, attempting to find another room (Attempt {currentAttempt + 1}/{maxAttempts})");
                    currentAttempt++;
                    continue; // Try finding another room
                } else if (joinResult != 0) { // 0 indicates success
                    Debug.LogError($"[Network] Failed to join quickplay room with error code: {joinResult}");
                    return joinResult; // Return the error code
                }

                return joinResult; // Success case
            } else {
                Debug.Log("[Network] No suitable quickplay rooms found, creating new room");
                break; // No rooms found, exit loop and create new room
            }
        }

        if (currentAttempt >= maxAttempts) {
            Debug.Log("[Network] Max attempts reached trying to join existing rooms, creating new room");
        }

        // Create a new room since we either found no rooms or failed to join after several attempts
        StringBuilder idBuilder = new();

        // First char should correspond to region
        int index = Regions.IndexOf(r => r.Code.Equals(Region, StringComparison.InvariantCultureIgnoreCase));
        idBuilder.Append(RoomIdValidChars[index >= 0 ? index : 0]);

        // Fill with random chars
        UnityEngine.Random.InitState(unchecked((int) DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds));
        for (int i = 1; i < RoomIdLength; i++) {
            idBuilder.Append(RoomIdValidChars[UnityEngine.Random.Range(0, RoomIdValidChars.Length)]);
        }

        args.RoomName = idBuilder.ToString();
        args.Lobby = TypedLobby.Default;
        args.RoomOptions.PublishUserId = true;
        args.RoomOptions.CustomRoomProperties = DefaultRoomProperties;
        args.RoomOptions.CustomRoomProperties[Enums.NetRoomProperties.HostName] = Settings.Instance.generalNickname;

        // Set quickplay and teams flags
        var boolProps = (BooleanProperties) (int) args.RoomOptions.CustomRoomProperties[Enums.NetRoomProperties.BoolProperties];
        boolProps.QuickPlay = true;
        boolProps.Teams = true;
        args.RoomOptions.CustomRoomProperties[Enums.NetRoomProperties.BoolProperties] = (int) boolProps;

        args.RoomOptions.CustomRoomPropertiesForLobby = new object[] {
        Enums.NetRoomProperties.HostName,
        Enums.NetRoomProperties.IntProperties,
        Enums.NetRoomProperties.BoolProperties,
        Enums.NetRoomProperties.StageGuid
    };

        return await Client.CreateAndJoinRoomAsync(args, false);
    }

    public static async Task<short> CreateRoom(EnterRoomArgs args) {
        // Create a random room id.
        StringBuilder idBuilder = new();

        // First char should correspond to region.
        int index = Regions.IndexOf(r => r.Code.Equals(Region, StringComparison.InvariantCultureIgnoreCase)); // Dirty linq hack
        idBuilder.Append(RoomIdValidChars[index >= 0 ? index : 0]);

        // Fill rest of the string with random chars
        UnityEngine.Random.InitState(unchecked((int) DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds));
        for (int i = 1; i < RoomIdLength; i++) {
            idBuilder.Append(RoomIdValidChars[UnityEngine.Random.Range(0, RoomIdValidChars.Length)]);
        }

        args.RoomName = idBuilder.ToString();
        args.Lobby = TypedLobby.Default;
        args.RoomOptions.PublishUserId = true;
        args.RoomOptions.CustomRoomProperties = DefaultRoomProperties;
        args.RoomOptions.CustomRoomProperties[Enums.NetRoomProperties.HostName] = Settings.Instance.generalNickname;
        args.RoomOptions.CustomRoomPropertiesForLobby = new object[] {
            Enums.NetRoomProperties.HostName,
            Enums.NetRoomProperties.IntProperties,
            Enums.NetRoomProperties.BoolProperties,
            Enums.NetRoomProperties.StageGuid,
        };

        Debug.Log($"[Network] Creating a game in {Region} with the ID {idBuilder}");
        return await Client.CreateAndJoinRoomAsync(args, false);
    }

    public static bool IsValidRoomId(string id, out int regionIndex) {
        if (id.Length <= 0) {
            regionIndex = -1;
            return false;
        }
        id = id.ToUpper();
        regionIndex = RoomIdValidChars.IndexOf(id[0]);
        return regionIndex >= 0 && regionIndex < Regions.Count() && Regex.IsMatch(id, $"[{RoomIdValidChars}]{{8}}");
    }

    public static bool IsQuickPlayRoom() {
        if (Client?.CurrentRoom == null) return false;

        return Client.CurrentRoom.CustomProperties.TryGetValue(
            Enums.NetRoomProperties.BoolProperties,
            out object props) && ((int) props & (1 << 4)) != 0; // Using a bit in BoolProperties for quickplay
    }

    public static async Task<short> JoinRoom(EnterRoomArgs args) {
        // Change to region if we need to
        args.RoomName = args.RoomName.ToUpper();
        await ConnectToRoomsRegion(args.RoomName);

        Debug.Log($"[Network] Attempting to join a game with the ID {args.RoomName}");
        return await Client.JoinRoomAsync(args, false);
    }

    public unsafe void SaveReplay(QuantumGame game, sbyte winner) {
#if UNITY_STANDALONE
        if (IsReplay || game.RecordInputStream == null) {
            SavedRecordingPath = null;
            return;
        }

        if (!Settings.Instance.GeneralReplaysEnabled) {
            // Disabled replays mid-game
            DisposeReplay();
            SavedRecordingPath = null;
            return;
        }

        // Make room for this replay - delete old ones.
        var manager = ReplayListManager.Instance;
        if (manager) {
            var deletions = manager.GetTemporaryReplaysToDelete();
            foreach (var replay in deletions) {
                Debug.Log($"[Replay] Automatically deleting temporary replay '{replay.ReplayFile.GetDisplayName()}' ({replay.FilePath}) to make room.");
                File.Delete(replay.FilePath);
                manager.RemoveReplay(replay);
            }
        }

        // JSON-friendly replay
        QuantumReplayFile jsonReplay = game.GetRecordedReplay();
        jsonReplay.InitialTick = initialFrame;
        jsonReplay.InitialFrameData = initialFrameData;
        initialFrame = 0;
        initialFrameData = null;

        // Create directories and open file
        string replayFolder = Path.Combine(ReplayListManager.ReplayDirectory, "temp");
        Directory.CreateDirectory(replayFolder);

        byte[] stars = new byte[10];
        Frame f = game.Frames.Verified;
        for (int i = 0; i < players; i++) {
            var filter = f.Filter<MarioPlayer>();
            while (filter.NextUnsafe(out _, out MarioPlayer* mario)) {
                if (mario->PlayerRef != playerrefs[i]) {
                    continue;
                }

                // Found him :)
                if (mario->Lives > 0 || !f.Global->Rules.IsLivesEnabled) {
                    stars[i] = mario->Stars;
                }
                break;
            }
        }

        // Write binary replay
        string now = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
        string finalFilePath = Path.Combine(replayFolder, $"Replay-{now}.mvlreplay");
        int attempts = 0;
        FileStream outputStream = null;
        do {
            try {
                outputStream = new FileStream(finalFilePath, FileMode.Create);
            } catch {
                // Failed to create file; maybe they have two copies of the game open?
                finalFilePath = Path.Combine(replayFolder, $"Replay-{now}-{++attempts}.mvlreplay");
            }
        } while (outputStream == null);

        BinaryReplayFile binaryReplay = BinaryReplayFile.FromReplayData(jsonReplay, f.Global->Rules, players, playernames, playerteams, stars, winner);
        long writtenBytes = binaryReplay.WriteToStream(outputStream);
        outputStream.Dispose();

        SavedRecordingPath = finalFilePath;

        // Complete
        Debug.Log($"[Replay] Saved new temporary replay '{finalFilePath}' ({Utils.BytesToString(writtenBytes)})");
        DisposeReplay();
#endif
    }

    private void DisposeReplay() {
        if (Game != null && Game.RecordInputStream != null) {
            Game.RecordInputStream.Dispose();
            Game.RecordInputStream = null;
        }
        playernames = null;
        playerteams = null;
        playerrefs = null;
    }

    private unsafe void UpdateRealtimeProperties() {
        Frame f = Game.Frames.Predicted;
        PlayerRef host = QuantumUtils.GetHostPlayer(f, out _);
        if (!Game.PlayerIsLocal(host)) {
            return;
        }

        ref GameRules rules = ref f.Global->Rules;
        IntegerProperties intProperties = new IntegerProperties {
            StarRequirement = rules.StarsToWin,
            CoinRequirement = rules.CoinsForPowerup,
            Lives = rules.Lives,
            Timer = rules.TimerSeconds,
        };

        // Get existing boolean properties to preserve quickplay flag
        BooleanProperties boolProperties = NetworkUtils.GetBooleanProperties(
            Client.CurrentRoom.CustomProperties,
            out NetworkUtils.BooleanProperties existingProps)
                ? existingProps
                : new BooleanProperties();

        // Update other boolean properties while preserving quickplay
        boolProperties.GameStarted = f.Global->GameState != GameState.PreGameRoom;
        boolProperties.CustomPowerups = rules.CustomPowerupsEnabled;
        boolProperties.Teams = rules.TeamsEnabled;
        boolProperties.DrawOnTimeUp = rules.DrawOnTimeUp;
        // Note: We don't modify boolProperties.QuickPlay here, preserving its existing value

        RuntimePlayer runtimePlayer = f.GetPlayerData(host);
        Client.CurrentRoom.SetCustomProperties(new Photon.Client.PhotonHashtable {
            [Enums.NetRoomProperties.IntProperties] = (int) intProperties,
            [Enums.NetRoomProperties.BoolProperties] = (int) boolProperties,
            [Enums.NetRoomProperties.HostName] = runtimePlayer.PlayerNickname,
            [Enums.NetRoomProperties.StageGuid] = rules.Stage.Id.ToString(),
        });
    }

    public void OnFriendListUpdate(List<FriendInfo> friendList) { }

    public void OnCreatedRoom() {
        if (pingUpdateCoroutine != null) {
            StopCoroutine(pingUpdateCoroutine);
        }
        pingUpdateCoroutine = StartCoroutine(PingUpdateCoroutine());
    }

    public void OnCreateRoomFailed(short returnCode, string message) { }

    public async void OnJoinedRoom() {
        if (pingUpdateCoroutine != null) {
            StopCoroutine(pingUpdateCoroutine);
        }
        pingUpdateCoroutine = StartCoroutine(PingUpdateCoroutine());

        var sessionRunnerArguments = new SessionRunner.Arguments {
            GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
            ClientId = Client.UserId,
            RuntimeConfig = new RuntimeConfig {
                SimulationConfig = GlobalController.Instance.config,
                Map = null,
                Seed = unchecked((int) DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                IsRealGame = true,
            },
            SessionConfig = QuantumDeterministicSessionConfigAsset.DefaultConfig,
            GameMode = DeterministicGameMode.Multiplayer,
            PlayerCount = 10,
            Communicator = new QuantumNetworkCommunicator(Client),
        };

        IsReplay = false;
        Runner = await QuantumRunner.StartGameAsync(sessionRunnerArguments);
        Runner.Game.AddPlayer(new RuntimePlayer {
            PlayerNickname = Settings.Instance.generalNickname ?? "noname",
            UserId = default(Guid).ToString(),
            UseColoredNickname = Settings.Instance.generalUseNicknameColor,
            Character = (byte) Settings.Instance.generalCharacter,
            Palette = (byte) Settings.Instance.generalPalette,
        });

        ChatManager.Instance.mutedPlayers.Clear();
        GlobalController.Instance.connecting.SetActive(false);
    }

    public void OnJoinRoomFailed(short returnCode, string message) { }

    public void OnJoinRandomFailed(short returnCode, string message) { }

    public void OnLeftRoom() {
        if (pingUpdateCoroutine != null) {
            StopCoroutine(pingUpdateCoroutine);
            pingUpdateCoroutine = null;
        }
    }

    private void OnPluginDisconnect(CallbackPluginDisconnect e) {
        Debug.Log($"[Network] Disconnected via server plugin: {e.Reason}");

        OnError?.Invoke(e.Reason);

        if (Runner) {
            Runner.Shutdown(ShutdownCause.SimulationStopped);
        }
    }

    private void OnGameDestroyed(CallbackGameDestroyed e) {
        SaveReplay(e.Game, -1);
    }

    private unsafe void OnRulesChanged(EventRulesChanged e) {
        UpdateRealtimeProperties();
    }

    private void OnGameEnded(EventGameEnded e) {
        SaveReplay(e.Game, (sbyte) e.WinningTeam);
    }

    private void OnPlayerAdded(EventPlayerAdded e) {
        RuntimePlayer runtimePlayer = e.Frame.GetPlayerData(e.Player);
        Debug.Log($"[Network] {runtimePlayer.PlayerNickname} ({runtimePlayer.UserId}) joined the game.");
    }

    private void OnPlayerRemoved(EventPlayerRemoved e) {
        RuntimePlayer runtimePlayer = e.Frame.GetPlayerData(e.Player);
        Debug.Log($"[Network] {runtimePlayer.PlayerNickname} ({runtimePlayer.UserId}) left the game.");
    }

    private void OnGameStateChanged(EventGameStateChanged e) {
        if (!Client.IsConnectedAndReady
            || !Client.LocalPlayer.IsMasterClient) {
            return;
        }

        BooleanProperties props = (int) Client.CurrentRoom.CustomProperties[Enums.NetRoomProperties.BoolProperties];
        props.GameStarted = e.NewState != GameState.PreGameRoom;

        Client.CurrentRoom.SetCustomProperties(new Photon.Client.PhotonHashtable {
            { Enums.NetRoomProperties.BoolProperties, (int) props }
        });
    }

    int initialFrame;
    byte[] initialFrameData;
    byte players;
    string[] playernames;
    byte[] playerteams;
    PlayerRef[] playerrefs;
    private void OnRecordingStarted(EventRecordingStarted e) {
        RecordReplay(e.Game, e.Frame);
    }

    public unsafe void RecordReplay(QuantumGame game, Frame f) {
        if (!Settings.Instance.GeneralReplaysEnabled) {
            return;
        }

        game.StartRecordingInput(f.Number);
        initialFrameData = f.Serialize(DeterministicFrameSerializeMode.Serialize);
        initialFrame = f.Number;

        players = f.Global->RealPlayers;
        playernames = new string[10];
        playerrefs = new PlayerRef[10];
        playerteams = new byte[10];

        int count = 0;
        for (PlayerRef player = 0; count < players && player < f.PlayerCount; player++) {
            var playerData = QuantumUtils.GetPlayerData(f, player);
            if (playerData == null || playerData->IsSpectator) {
                continue;
            }

            RuntimePlayer runtimePlayer = f.GetPlayerData(player);
            if (runtimePlayer == null) {
                continue;
            }

            playernames[count] = runtimePlayer.PlayerNickname.ToValidUsername(f, player);
            playerrefs[count] = player;
            playerteams[count] = playerData->RealTeam;
            count++;
        }

        Debug.Log("[Replay] Started recording a new replay.");
    }

    public async static void StartReplay(BinaryReplayFile replay) {
        if (Client.IsConnected) {
            await Client.DisconnectAsync();
        }
        if (Runner && Runner.IsRunning) {
            await Runner.ShutdownAsync();
        }

        IsReplay = true;
        ReplayStart = replay.InitialFrameNumber;
        ReplayLength = replay.ReplayLengthInFrames;

        var serializer = new QuantumUnityJsonSerializer();
        var runtimeConfig = serializer.ConfigFromByteArray<RuntimeConfig>(replay.DecompressedRuntimeConfigData, compressed: true);
        var deterministicConfig = DeterministicSessionConfig.FromByteArray(replay.DecompressedDeterministicConfigData);
        var inputStream = new Photon.Deterministic.BitStream(replay.DecompressedInputData);
        var replayInputProvider = new BitStreamReplayInputProvider(inputStream, ReplayEnd);

        // Disable checksums- they murder performance.
        deterministicConfig.ChecksumInterval = 0;

        var arguments = new SessionRunner.Arguments {
            GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
            RuntimeConfig = runtimeConfig,
            SessionConfig = deterministicConfig,
            ReplayProvider = replayInputProvider,
            GameMode = DeterministicGameMode.Replay,
            RunnerId = "LOCALREPLAY",
            PlayerCount = deterministicConfig.PlayerCount,
            InitialTick = ReplayStart,
            FrameData = replay.DecompressedInitialFrameData,
            DeltaTimeType = SimulationUpdateTime.EngineDeltaTime,
            GameFlags = QuantumGameFlags.EnableTaskProfiler,
        };

        GlobalController.Instance.loadingCanvas.Initialize(null);
        ReplayFrameCache.Clear();
        ReplayFrameCache.Add(arguments.FrameData);
        Runner = await QuantumRunner.StartGameAsync(arguments);
    }

    private unsafe void OnGameResynced(CallbackGameResynced e) {
        if (IsReplay) {
            return;
        }

        Frame f = e.Game.Frames.Verified;
        if (f.Global->GameState == GameState.Playing) {
            RecordReplay(e.Game, f);
        }
    }

    private void OnSimulateFinished(CallbackSimulateFinished e) {
        if (!IsReplay) {
            return;
        }

        Frame f = e.Frame;
        if ((f.Number - ReplayStart) % (5 * f.UpdateRate) == 0) {
            // Save this frame to the replay cache
            int index = (f.Number - ReplayStart) / (5 * f.UpdateRate);
            if (replayFrameCache.Count <= index) {
                byte[] serializedFrame = f.Serialize(DeterministicFrameSerializeMode.Serialize);
                /*
                byte[] copy = new byte[serializedFrame.Length];
                Array.Copy(serializedFrame, copy, serializedFrame.Length);
                replayFrameCache.Add(copy);
                */
                replayFrameCache.Add(serializedFrame);
            }
        }
    }

    public void OnConnected() { }

    public void OnConnectedToMaster() { }

    public void OnDisconnected(DisconnectCause cause) {
        Debug.Log($"[Network] Disconnected. Reason: {cause}");

        if (Runner) {
            Runner.Shutdown(ShutdownCause.SimulationStopped);
        }
    }

    public void OnRegionListReceived(RegionHandler regionHandler) { }

    public void OnCustomAuthenticationResponse(Dictionary<string, object> data) {
        PlayerPrefs.SetString("id", Client.AuthValues.UserId);

        if (data.TryGetValue("Token", out object token) && token is string tokenString) {
            PlayerPrefs.SetString("token", tokenString);
        }

        PlayerPrefs.Save();
    }

    public void OnCustomAuthenticationFailed(string debugMessage) { }

    public static bool FilterOutReplayFastForward(IDeterministicGame game) {
        return !IsReplayFastForwarding;
    }

    public static bool FilterOutReplay(IDeterministicGame game) {
        return !IsReplay;
    }

    private unsafe void OnReplaysEnabledChanged(bool enable) {
        if (Game == null) {
            return;
        }

        Frame f = Game.Frames.Predicted;
        if (enable) {
            if (f.Global->GameState >= GameState.Starting && f.Global->GameState < GameState.Ended) {
                RecordReplay(Game, f);
            }
        } else {
            // Disable
            DisposeReplay();
        }
    }
}
