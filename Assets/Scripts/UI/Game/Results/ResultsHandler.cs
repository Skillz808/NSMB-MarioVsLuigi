using JimmysUnityUtilities;
using NSMB.Extensions;
using Quantum;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

public unsafe class ResultsHandler : MonoBehaviour {
    //---Serialized Variables
    [SerializeField] private GameObject parent;
    [SerializeField] private ResultsEntry[] entries;
    [SerializeField] private RectTransform header, ui;
    [SerializeField] private CanvasGroup fadeGroup;
    [SerializeField] private LoopingMusicData musicData;
    [SerializeField] private float delayUntilStart = 5.5f, delayPerEntry = 0.05f;
    [SerializeField] private string serverUrl = "http://localhost:3000/match-results";

    //---Private Variables
    private Coroutine endingCoroutine, moveUiCoroutine, moveHeaderCoroutine, fadeCoroutine;

    public void Start() {
        QuantumEvent.Subscribe<EventGameEnded>(this, OnGameEnded);
        QuantumCallback.Subscribe<CallbackGameResynced>(this, OnGameResynced);
        parent.SetActive(false);

        serverUrl = "http://localhost:3000/match-results";

        if (NetworkHandler.Game != null) {
            Frame f = NetworkHandler.Game.Frames.Predicted;
            if (f.Global->GameState == GameState.Ended) {
                endingCoroutine = StartCoroutine(RunEndingSequenceWrapper(f, 0));
            }
        }
    }

    private void OnGameEnded(EventGameEnded e) {
        endingCoroutine = StartCoroutine(RunEndingSequenceWrapper(e.Frame, delayUntilStart));
    }

    private bool CheckWinnerAndGetTeamRankings(Frame f, out Dictionary<int, int> teamRankings) {
        teamRankings = null;
        if (!f.Global->HasWinner) return false;

        if (IsTeamMode(f)) {
            teamRankings = CalculateTeamRankings(f);
        }
        return true;
    }

    private bool ShouldSendResults(Frame f) {
        PlayerRef host = QuantumUtils.GetHostPlayer(f, out _);
        return NetworkHandler.Game.PlayerIsLocal(host);
    }

    private IEnumerator RunEndingSequenceWrapper(Frame f, float delay) {
        yield return new WaitForSeconds(delay);

        parent.SetActive(true);
        FindObjectOfType<LoopingMusicPlayer>().Play(musicData);

        Dictionary<int, int> teamRankings;
        if (CheckWinnerAndGetTeamRankings(f, out teamRankings)) {
            if (ShouldSendResults(f)) {
                SendMatchResults(f, teamRankings);
            }
        }

        InitializeResultsEntries(f);
        moveHeaderCoroutine = StartCoroutine(MoveObjectToTarget(header, -500, 0, 1/3f));
        moveUiCoroutine = StartCoroutine(MoveObjectToTarget(ui, 500, 0, 1/3f));
        fadeCoroutine = StartCoroutine(OtherUIFade());
    }

    [System.Serializable]
    private class MatchPlayer {
        public int playerIndex;
        public string playerId;
        public string nickname;
        public int stars;
        public int team;
        public int rank;
    }

    [System.Serializable]
    private class TeamData {
        public int teamId;
        public int score;
        public int rank;
    }

    [System.Serializable]
    private class MatchResult {
        public string matchId;
        public string timestamp;
        public bool isTeamMode;
        public List<TeamData> teams = new List<TeamData>();
        public List<MatchPlayer> players = new List<MatchPlayer>();
    }

    private string ConvertToJson(Dictionary<string, object> results) {
        var matchResult = new MatchResult {
            matchId = (string)results["matchId"],
            timestamp = (string)results["timestamp"],
            isTeamMode = (bool)results["isTeamMode"]
        };

        if (results.ContainsKey("teams")) {
            var teamsDict = results["teams"] as Dictionary<int, object>;
            foreach (var team in teamsDict) {
                var teamData = team.Value as Dictionary<string, object>;
                matchResult.teams.Add(new TeamData {
                    teamId = team.Key,
                    score = (int)teamData["score"],
                    rank = (int)teamData["rank"]
                });
            }
        }

        var playersList = results["players"] as List<Dictionary<string, object>>;
        foreach (var player in playersList) {
            matchResult.players.Add(new MatchPlayer {
                playerIndex = (int)player["playerIndex"],
                playerId = (string)player["playerId"],
                nickname = (string)player["nickname"],
                stars = (int)player["stars"],
                team = player.ContainsKey("team") ? (int)player["team"] : -1,
                rank = player.ContainsKey("rank") ? (int)player["rank"] : -1
            });
        }

        return JsonUtility.ToJson(matchResult);
    }

    private void SendMatchResults(Frame f, Dictionary<int, int> teamRankings) {
        var matchResults = new Dictionary<string, object> {
            { "matchId", System.Guid.NewGuid().ToString() },
            { "timestamp", System.DateTime.UtcNow.ToString("o") },
            { "isTeamMode", teamRankings != null },
            { "players", new List<Dictionary<string, object>>() }
        };

        if (teamRankings != null) {
            matchResults["teams"] = new Dictionary<int, object>();
            byte[] teamScores = new byte[10];
            QuantumUtils.GetTeamStars(f, teamScores);

            for (int i = 0; i < teamScores.Length; i++) {
                if (teamScores[i] > 0) {
                    var teamData = new Dictionary<string, object> {
                        { "score", teamScores[i] },
                        { "rank", teamRankings[i] },
                        { "players", new List<Dictionary<string, object>>() }
                    };
                    (matchResults["teams"] as Dictionary<int, object>)[i] = teamData;
                }
            }
        }

        var playersList = matchResults["players"] as List<Dictionary<string, object>>;
        for (int i = 0; i < f.Global->RealPlayers; i++) {
            var playerInfo = f.Global->PlayerInfo[i];
            var runtimePlayer = f.GetPlayerData(i);
            var playerData = new Dictionary<string, object> {
                { "playerIndex", i },
                { "playerId", runtimePlayer.UserId },
                { "nickname", runtimePlayer.PlayerNickname },
                { "stars", playerInfo.GetStarCount(f) }
            };

            if (teamRankings == null) {
                playerData["rank"] = i + 1;
            } else {
                playerData["team"] = playerInfo.Team;
                var teams = matchResults["teams"] as Dictionary<int, object>;
                var teamData = teams[playerInfo.Team] as Dictionary<string, object>;
                (teamData["players"] as List<Dictionary<string, object>>).Add(playerData);
            }

            playersList.Add(playerData);
        }

        StartCoroutine(SendResultsToServer(matchResults));
    }

    private IEnumerator SendResultsToServer(Dictionary<string, object> results) {
        Debug.Log($"Attempting to send results to {serverUrl}");
        string jsonData;
        
        try {
            jsonData = ConvertToJson(results);
        } catch (System.Exception e) {
            Debug.LogError($"Error converting match results to JSON: {e}");
            yield break;
        }

        Debug.Log($"Sending data: {jsonData}");
    
        using (UnityWebRequest www = new UnityWebRequest(serverUrl, "POST")) {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            Debug.Log("Sending web request...");
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success) {
                Debug.Log($"Match results sent successfully! Response: {www.downloadHandler.text}");
            } else {
                Debug.LogError($"Error sending match results: {www.error}");
                Debug.LogError($"Response Code: {www.responseCode}");
                Debug.LogError($"Full Error: {www.error}");
                if (www.downloadHandler != null) {
                    Debug.LogError($"Response: {www.downloadHandler.text}");
                }
            }
        }
    }

    private void InitializeResultsEntries(Frame f) {
        int initializeCount = 0;
        List<PlayerInformation> infos = new();
        for (int i = 0; i < f.Global->RealPlayers; i++) {
            infos.Add(f.Global->PlayerInfo[i]);
        }
        
        foreach (var info in infos.OrderByDescending(x => x.GetStarCount(f))) {
            int rank = -1;
            if (IsTeamMode(f)) {
                Dictionary<int, int> teamRankings = CalculateTeamRankings(f);
                rank = teamRankings[info.Team];
            }
            entries[initializeCount].Initialize(f, info, rank, initializeCount * delayPerEntry, info.GetStarCount(f));
            initializeCount++;
        }

        for (int i = initializeCount; i < entries.Length; i++) {
            entries[i].Initialize(f, null, -1, i * delayPerEntry);
        }
    }

    private bool IsTeamMode(Frame f) {
        byte[] teamScores = new byte[10];
        QuantumUtils.GetTeamStars(f, teamScores);

        // Count how many teams have actual scores
        int teamsWithScores = 0;
        for (int i = 0; i < teamScores.Length; i++) {
            if (teamScores[i] > 0) {
                teamsWithScores++;
            }
        }
        
        return teamsWithScores > 1;  // If more than one team has scores, it's a team game
    }

    private Dictionary<int, int> CalculateTeamRankings(Frame f) {
        byte[] teamScores = new byte[10];
        QuantumUtils.GetTeamStars(f, teamScores);

        Dictionary<int, int> teamScoresDict = new();
        for (int i = 0; i < teamScores.Length; i++) {
            if (teamScores[i] > 0) {
                teamScoresDict[i] = teamScores[i];
            }
        }

        Dictionary<int, int> rankings = new();
        int previousScore = -1;
        int repeatedCount = 0;
        int currentRank = 1;

        foreach ((int teamIndex, int score) in teamScoresDict.OrderByDescending(x => x.Value)) {
            if (previousScore == score) {
                repeatedCount++;
                rankings[teamIndex] = currentRank - 1;
            } else {
                currentRank += repeatedCount;
                rankings[teamIndex] = currentRank;
                currentRank++;
                previousScore = score;
                repeatedCount = 0;
            }
        }

        return rankings;
    }

    private IEnumerator OtherUIFade() {
        float time = 0.333f;
        while (time > 0) {
            time -= Time.deltaTime;
            fadeGroup.alpha = Mathf.Lerp(0, 1, time / 0.333f);
            yield return null;
        }
    }

    public static IEnumerator MoveObjectToTarget(RectTransform obj, float start, float end, float moveTime, float delay = 0) {
        obj.SetAnchoredPositionX(start);
        if (delay > 0) {
            yield return new WaitForSeconds(delay);
        }

        float timer = moveTime;
        while (timer > 0) {
            timer -= Time.deltaTime;
            obj.SetAnchoredPositionX(Mathf.Lerp(end, start, timer / moveTime));
            yield return null;
        }
    }

    private void OnGameResynced(CallbackGameResynced e) {
        Frame f = e.Game.Frames.Predicted;
        fadeGroup.alpha = 1f;
        if (f.Global->GameState == GameState.Ended) {
            endingCoroutine = StartCoroutine(RunEndingSequenceWrapper(f, 0));
        } else {
            parent.SetActive(false);
            this.StopCoroutineNullable(ref endingCoroutine);
            this.StopCoroutineNullable(ref moveHeaderCoroutine);
            this.StopCoroutineNullable(ref moveUiCoroutine);
            this.StopCoroutineNullable(ref fadeCoroutine);
        }
    }
}