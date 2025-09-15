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

public class FirebaseController : MonoBehaviour
{
    private const string databaseUrl = "https://experimental-games-190e1-default-rtdb.firebaseio.com/";
    private const string webAppUrl = "https://brandoncoffey.com/game/play";

    [Header("UI References")]
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private Transform playerListContainer;
    [SerializeField] private Transform chatContainer;
    [SerializeField] private GameObject playerListItemPrefab;
    [SerializeField] private GameObject chatMessagePrefab;

    [SerializeField] private RawImage qrCodeImage;

    private string roomCode;
    private RoomData currentRoomData;

    private Dictionary<string, GameObject> playerUIElements = new Dictionary<string, GameObject>();
    private HashSet<string> displayedMessageIds = new HashSet<string>();

    private void Start()
    {
        CreateRoom();
    }

    private void CreateRoom()
    {
        StartCoroutine(CreateRoomCoroutine());
    }

    private IEnumerator CreateRoomCoroutine()
    {
        roomCode = GenerateRoomCode();
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

    private void DisplayQrCode()
    {
        if (qrCodeImage == null) return;

        string joinUrl = $"{webAppUrl}?code={roomCode}";
        qrCodeImage.texture = QRCode.GenerateQR(joinUrl, 256);
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
            yield return new WaitForSeconds(1.5f);
        }
    }

    private void ProcessRoomData()
    {
        if (currentRoomData == null) return;

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
    }

    private string GenerateRoomCode()
    {
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        string code = "";
        for (int i = 0; i < 4; i++) code += chars[Random.Range(0, chars.Length)];
        return code;
    }
}