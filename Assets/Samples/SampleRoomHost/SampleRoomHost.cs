using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SampleRoomHost : FirebaseController
{
    [Header("Sample Room Settings")]
    [SerializeField] private Transform playerListContainer;
    [SerializeField] private GameObject playerListItemPrefab;
    [SerializeField] private Transform chatContainer;
    [SerializeField] private GameObject chatMessagePrefab;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private RawImage qrCodeImage;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private float countdownDuration = 5.0f;

    private RoomData currentRoomData;
    private GameState currentGameState = GameState.Lobby;

    private Dictionary<string, GameObject> playerUIElements = new Dictionary<string, GameObject>();
    private HashSet<string> displayedMessageIds = new HashSet<string>();

    private Coroutine gameStartTimer;
    private bool isTimerRunning = false;

    [System.Serializable]
    private class PlayerData
    {
        public string name;
        public int score;
        public bool isReady;
        public Dictionary<string, bool> inputs;
    }

    [System.Serializable]
    private class ChatMessage
    {
        public string sender;
        public string message;
    }

    [System.Serializable]
    private class RoomData
    {
        public string gameState;
        public string prompt;
        public Dictionary<string, PlayerData> players;
        public Dictionary<string, ChatMessage> chatMessages;
    }

    private enum GameState
    {
        Lobby,
        InGame,
        PostGame
    }

    protected override string GetInitialJsonData()
    {
        return "{\"gameState\":\"lobby\",\"prompt\":\"Waiting for players...\"}";
    }

    protected override void OnRoomCreated(string roomCode)
    {
        if (roomCodeText != null) roomCodeText.text = roomCode;
        if (qrCodeImage != null) DisplayQrCode(qrCodeImage);
    }

    protected override void ProcessJsonData(string jsonData)
    {
        currentRoomData = JsonConvert.DeserializeObject<RoomData>(jsonData);
        if (currentRoomData == null)
        {
            Debug.Log("Room data is null");
            return;
        }

        // Update Game State
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

        // Update Player List
        var playersFromDb = currentRoomData.players ?? new Dictionary<string, PlayerData>();
        List<string> currentDisplayedIds = playerUIElements.Keys.ToList();

        foreach (string displayedId in currentDisplayedIds)
        {
            // Check remove players
            if (!playersFromDb.ContainsKey(displayedId))
            {
                Destroy(playerUIElements[displayedId]);
                playerUIElements.Remove(displayedId);
            }
        }

        foreach (var playerEntry in playersFromDb)
        {
            // Check add or update players
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

        // Update Chat
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

        // Input Handling Example
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

    private void SetGameState(string newState)
    {
        string jsonData = $"\"{newState}\"";
        SendJsonData("gameState", jsonData);
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

        SetGameState("in-game");
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
}
