using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public abstract class FirebaseController : MonoBehaviour
{
    [Tooltip("Optional: If left blank, a random 4-letter room code will be generated.")]
    [SerializeField] private string _roomCode;
    [SerializeField] private float _pollDatabaseInterval = 1.0f;

    private WaitForSeconds _pollDelay;

    private void Start()
    {
        StartCoroutine(CreateRoomRoutine());
    }

    private void OnDestroy()
    {
        CloseRoom();
    }

    // Start function, runs once room is created. Gives the room code to display.
    protected abstract void OnRoomCreated(string roomCode);

    // Set the initial JSON data when creating the room.
    protected abstract string GetInitialJsonData();

    // Update function. On poll interval, data is sent here to be deserialized and update the game visuals.
    protected abstract void ProcessJsonData(string jsonData);

    // If the unity app needs to send data to the database, this function can be used to set specific parts of the json data.
    protected void SendJsonData(string jsonPath, string jsonData) => StartCoroutine(SendJsonDataRoutine(jsonPath, jsonData));

    // Displays a QR code to the given RawImage that links to the web app with the room code already supplied.
    protected void DisplayQrCode(RawImage image) => image.texture = QRCode.GenerateQR($"{WebAppUrl}?code={_roomCode}", 256);

    private IEnumerator CreateRoomRoutine()
    {
        _roomCode = string.IsNullOrEmpty(_roomCode) ? GenerateRoomCode() : _roomCode.Trim().ToUpper();

        string initialJsonData = GetInitialJsonData();
        string url = $"{DatabaseUrl}/rooms/{_roomCode}.json";
        
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
                OnRoomCreated(_roomCode);
                _pollDelay = new WaitForSeconds(_pollDatabaseInterval);
                StartCoroutine(ListenForChangesRoutine());
            }
            else
            {
                Debug.LogError($"Error creating room: {request.error}");
            }
        }
    }

    private IEnumerator ListenForChangesRoutine()
    {
        string url = $"{DatabaseUrl}/rooms/{_roomCode}.json";
        while (true)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    string jsonData = request.downloadHandler.text;
                    ProcessJsonData(jsonData);
                }
            }
            yield return _pollDelay;
        }
    }

    private IEnumerator SendJsonDataRoutine(string jsonPath, string jsonData)
    {
        string url = $"{DatabaseUrl}/rooms/{_roomCode}/{jsonPath}.json";

        using (var request = new UnityWebRequest(url, "PUT"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error setting json data ({jsonData}): {request.error}");
            }
            else
            {
                Debug.Log($"Json data set {jsonData}");
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

    private void CloseRoom()
    {
        if (!string.IsNullOrEmpty(_roomCode))
        {
            Debug.Log($"Deleting room {_roomCode}...");
            string url = $"{DatabaseUrl}/rooms/{_roomCode}.json";
            using (var request = UnityWebRequest.Delete(url))
            {
                request.SendWebRequest();
            }
            _roomCode = "";
        }
    }

    private const string DatabaseUrl = "https://experimental-games-190e1-default-rtdb.firebaseio.com";
    private const string WebAppUrl = "https://brandoncoffey.com/game/play";
}