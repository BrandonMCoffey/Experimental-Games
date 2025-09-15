using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using Newtonsoft.Json;
using UnityEngine.UI;
using System.Linq;

[System.Serializable]
public class PlayerData
{
    public string name;
    public int score;
    public bool isReady;
    public Dictionary<string, bool> inputs;
}

[System.Serializable]
public class ChatMessage
{
    public string sender;
    public string message;
}

[System.Serializable]
public class RoomData
{
    public string gameState;
    public string prompt;
    public Dictionary<string, PlayerData> players;
    public Dictionary<string, ChatMessage> chatMessages;
}

public enum GameState
{
    Lobby,
    InGame,
    PostGame
}

public class FirebaseController : MonoBehaviour
{
    private const string databaseUrl = "https://experimental-games-190e1-default-rtdb.firebaseio.com/";
    private const string webAppUrl = "https://brandoncoffey.com/game/play";

    [Header("UI References")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private Transform playerListContainer;
    [SerializeField] private Transform chatContainer;
    [SerializeField] private GameObject playerListItemPrefab;
    [SerializeField] private GameObject chatMessagePrefab;
    [SerializeField] private RawImage qrCodeImage;

    [Header("Game Settings")]
    [SerializeField] private string forceRoomCode = "";
    [SerializeField] private float countdownDuration = 5.0f;

    private string roomCode;
    private RoomData currentRoomData;

    private Dictionary<string, GameObject> playerUIElements = new Dictionary<string, GameObject>();
    private HashSet<string> displayedMessageIds = new HashSet<string>();

    private Coroutine gameStartTimer;
    private bool isTimerRunning = false;
    private GameState currentGameState = GameState.Lobby;

    private void Start()
    {
        CreateRoom();
    }

    private void OnDestroy()
    {
        CloseRoom();
    }

    private void CreateRoom()
    {
        StartCoroutine(CreateRoomCoroutine());
    }

    private IEnumerator CreateRoomCoroutine()
    {
        roomCode = string.IsNullOrEmpty(forceRoomCode) ? GenerateRoomCode() : forceRoomCode.Trim().ToUpper();
        roomCodeText.text = roomCode;

        string initialJsonData = "{\"gameState\":\"lobby\",\"prompt\":\"Waiting for players...\"}";
        string url = $"{databaseUrl}rooms/{roomCode}.json";
        
        using (var request = new UnityWebRequest(url, "PUT"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(initialJsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Room created successfully!");
                DisplayQrCode();
                StartCoroutine(ListenForChangesCoroutine());
            }
            else
            {
                Debug.LogError($"Error creating room: {request.error}");
            }
        }
    }

    private IEnumerator ListenForChangesCoroutine()
    {
        string url = $"{databaseUrl}rooms/{roomCode}.json";
        while (true)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    string jsonData = request.downloadHandler.text;
                    currentRoomData = JsonConvert.DeserializeObject<RoomData>(jsonData);
                    ProcessRoomData();
                }
            }
            yield return new WaitForSeconds(1.0f);
        }
    }

    private void ProcessRoomData()
    {
        if (currentRoomData == null)
        {
            Debug.Log("Room data is null");
            return;
        }

        switch (currentRoomData.gameState)
        {
            case "lobby":
                if (currentGameState != GameState.Lobby)
                {
                    Debug.Log("Entered Lobby state");
                    lobbyPanel.SetActive(true);
                    gamePanel.SetActive(false);
                }
                currentGameState = GameState.Lobby;
                CheckReadyState();
                break;
            case "in-game":
                Debug.Log("Entered In-Game state");
                if (currentGameState != GameState.InGame)
                {
                    lobbyPanel.SetActive(false);
                    gamePanel.SetActive(true);
                }
                currentGameState = GameState.InGame;
                break;
            case "post-game":
                Debug.Log("Entered Post-Game state");
                break;
            default:
                Debug.Log("Invalid Game State");
                break;
        }

        var playersFromDb = currentRoomData.players ?? new Dictionary<string, PlayerData>();

        List<string> currentDisplayedIds = playerUIElements.Keys.ToList();

        foreach (string displayedId in currentDisplayedIds)
        {
            if (!playersFromDb.ContainsKey(displayedId))
            {
                Destroy(playerUIElements[displayedId]);
                playerUIElements.Remove(displayedId);
            }
        }

        foreach (var playerEntry in playersFromDb)
        {
            string playerId = playerEntry.Key;
            PlayerData playerData = playerEntry.Value;

            if (!playerUIElements.ContainsKey(playerId))
            {
                GameObject newPlayerUI = Instantiate(playerListItemPrefab, playerListContainer);
                newPlayerUI.GetComponentInChildren<TMP_Text>().text = playerData.name;
                playerUIElements.Add(playerId, newPlayerUI);
            }
            else
            {
                playerUIElements[playerId].GetComponentInChildren<TMP_Text>().text = playerData.name;
            }
        }

        if (currentRoomData.chatMessages != null)
        {
            foreach (var msgEntry in currentRoomData.chatMessages)
            {
                string msgId = msgEntry.Key;
                ChatMessage msgData = msgEntry.Value;

                if (!displayedMessageIds.Contains(msgId))
                {
                    GameObject newChatUI = Instantiate(chatMessagePrefab, chatContainer);
                    newChatUI.GetComponentInChildren<TMP_Text>().text = $"{msgData.sender}: {msgData.message}";
                    displayedMessageIds.Add(msgId);
                }
            }
        }

        if (currentGameState == GameState.InGame)
        {
            foreach (var playerEntry in playersFromDb)
            {
                if (playerEntry.Value.inputs != null)
                {
                    bool isUp = playerEntry.Value.inputs.GetValueOrDefault("up", false);
                    bool isDown = playerEntry.Value.inputs.GetValueOrDefault("down", false);
                    bool isLeft = playerEntry.Value.inputs.GetValueOrDefault("left", false);
                    bool isRight = playerEntry.Value.inputs.GetValueOrDefault("right", false);
                    Debug.Log($"Player {playerEntry.Value.name} Inputs - Up: {isUp}, Down: {isDown}, Left: {isLeft}, Right: {isRight}");
                }
            }
        }
    }

    private void CheckReadyState()
    {
        var players = currentRoomData.players;

        if (players == null || players.Count == 0)
        {
            StopGameTimer();
            return;
        }

        bool allPlayersReady = true;
        foreach (var player in players.Values)
        {
            if (!player.isReady)
            {
                allPlayersReady = false;
                break;
            }
        }

        if (allPlayersReady && !isTimerRunning)
        {
            gameStartTimer = StartCoroutine(GameStartCountdown());
        }
        else if (!allPlayersReady && isTimerRunning)
        {
            StopGameTimer();
        }
    }

    private IEnumerator GameStartCountdown()
    {
        isTimerRunning = true;
        float timer = countdownDuration;

        while (timer > 0)
        {
            timerText.text = $"Game starting in {Mathf.CeilToInt(timer)}...";
            timer -= Time.deltaTime;
            yield return null;
        }

        timerText.text = "Starting!";
        isTimerRunning = false;

        StartCoroutine(SetGameState("in-game"));
    }

    private void StopGameTimer()
    {
        if (gameStartTimer != null)
        {
            StopCoroutine(gameStartTimer);
            gameStartTimer = null;
        }
        isTimerRunning = false;
        timerText.text = "";
    }

    private IEnumerator SetGameState(string newState)
    {
        string url = $"{databaseUrl}rooms/{roomCode}/gameState.json";
        string jsonData = $"\"{newState}\"";

        using (var request = new UnityWebRequest(url, "PUT"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error setting game state: {request.error}");
            }
            else
            {
                Debug.Log($"Game state set to {newState}");
            }
        }
    }

    private string GenerateRoomCode()
    {
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        string code = "";
        for (int i = 0; i < 4; i++) code += chars[Random.Range(0, chars.Length)];
        return code;
    }

    private void DisplayQrCode()
    {
        if (qrCodeImage == null) return;

        string joinUrl = $"{webAppUrl}?code={roomCode}";
        qrCodeImage.texture = QRCode.GenerateQR(joinUrl, 256);
    }

    private void CloseRoom()
    {
        if (!string.IsNullOrEmpty(roomCode))
        {
            Debug.Log($"Deleting room {roomCode}...");
            string url = $"{databaseUrl}rooms/{roomCode}.json";
            using (var request = UnityWebRequest.Delete(url))
            {
                request.SendWebRequest();
            }
            roomCode = "";
        }
    }
}