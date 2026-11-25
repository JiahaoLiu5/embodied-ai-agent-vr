using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Client for the OpenAI Realtime API (speech in, speech out) over a WebSocket.
///
/// Based on OpenAI-Realtime-API-for-Unity by Martin Pluisch (MIT licence, see LICENSE.md in this package).
/// This modified version uses the generally available protocol, offers the model a gesture tool,
/// reads the API key from outside the scene and can reconnect after a failed or dropped connection.
/// </summary>
public class RealtimeAPIWrapper : MonoBehaviour
{
    private const string ApiKeyPlaceholder = "YOUR_API_KEY";
    private const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    private const string ApiKeyFileName = "openai_api_key.txt";
    private const string AnimationToolName = "trigger_animation";

    private const int ReceiveBufferBytes = 1024 * 128;
    private const int CloseTimeoutMilliseconds = 2000;
    private const int QuitCloseTimeoutMilliseconds = 250;

    private ClientWebSocket ws;
    private CancellationTokenSource connectionCancellation;
    private bool hasConnected = false;

    // ClientWebSocket allows only one outstanding send at a time.
    private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

    [Header("Connection")]
    [Tooltip("Optional, for quick local tests only. The key is saved in the scene and in every build. " +
             "Leave this empty and use the OPENAI_API_KEY environment variable or an openai_api_key.txt file instead.")]
    [SerializeField] string apiKey = ApiKeyPlaceholder;

    [Tooltip("Name of the Realtime model.")]
    [SerializeField] private string model = "gpt-realtime-2.1-mini";

    [Tooltip("WebSocket address of the Realtime API, without the model parameter.")]
    [SerializeField] private string endpoint = "wss://api.openai.com/v1/realtime";

    // Control Variables from the Inspector
    [Header("AI Agent Configurations")]
    [TextArea(5, 15)]
    [SerializeField] private string systemInstructions = "You are a helpful, empathetic, and concise embodied AI assistant. Please respond with brief and natural conversational sentences.";

    [Tooltip("alloy, ash, ballad, cedar, coral, echo, marin, sage, shimmer, verse")]
    [SerializeField] private string voice = "shimmer";

    [Tooltip("Gestures the model may request through the trigger_animation tool. " +
             "Each name must be the name of a state on the first layer of the agent's Animator Controller.")]
    [SerializeField] private string[] gestureNames =
    {
        "Brief Talking (1)", "Brief Talking (2)", "Cocky Head Turn",
        "Disagreeing Talking", "Head Nod Yes", "Standing Arguing",
        "Standing Thumbs Up", "Talking (0)", "Talking (1)", "Talking (2)",
        "Talking (3)", "Talking (4)", "Talking (5)", "Talking (6)",
        "Talking (7)", "Talking (8)", "Talking (9)", "Talking (10)",
        "Talking (11)", "Talking Ideas (1)", "Talking Ideas (2)",
        "Thoughtful Head Nod", "Thoughtful Head Shake", "Waving"
    };

    public AudioPlayer audioPlayer;
    public AudioRecorder audioRecorder;
    private StringBuilder transcriptBuffer = new StringBuilder();

    // True from response.created until the server has finished the response and its audio has been played.
    private bool isResponseInProgress = false;

    // True once the server has sent response.done for the latest response.
    private bool isResponseDoneOnServer = true;

    private string lastConversationItemId;

    public static event Action OnWebSocketConnected;
    public static event Action OnWebSocketClosed;
    public static event Action OnSessionCreated;
    public static event Action OnConversationItemCreated;
    public static event Action OnResponseDone;
    public static event Action<string> OnTranscriptReceived;
    public static event Action OnResponseCreated;
    public static event Action OnResponseAudioDone;
    public static event Action OnResponseAudioTranscriptDone;
    public static event Action OnResponseContentPartDone;
    public static event Action OnResponseOutputItemDone;
    public static event Action OnRateLimitsUpdated;
    public static event Action OnResponseOutputItemAdded;
    public static event Action OnResponseContentPartAdded;
    public static event Action OnResponseCancelled;

    // Raised with the gesture names the model requested, in playing order.
    public static event Action<List<string>> OnAnimationSequenceReceived;

    /// <summary>
    /// true while the websocket is open
    /// </summary>
    public bool IsConnected => ws != null && ws.State == WebSocketState.Open;

    /// <summary>
    /// the gesture names offered to the model
    /// </summary>
    public IReadOnlyList<string> GestureNames => gestureNames ?? Array.Empty<string>();

    private void Start()
    {
        AudioRecorder.OnAudioRecorded += SendAudioToAPI;
        if (audioPlayer != null) audioPlayer.OnPlaybackFinished += ClearResponseInProgressWhenFinished;
    }

    private void OnApplicationQuit() => CloseImmediately();

    private void OnDestroy()
    {
        AudioRecorder.OnAudioRecorded -= SendAudioToAPI;
        if (audioPlayer != null) audioPlayer.OnPlaybackFinished -= ClearResponseInProgressWhenFinished;
        CloseImmediately();
    }

    /// <summary>
    /// connects or disconnects websocket when button is pressed
    /// </summary>
    public async void ConnectWebSocketButton()
    {
        if (ws != null) await DisposeWebSocket();
        else await ConnectWebSocket();
    }

    /// <summary>
    /// opens the connection; does nothing if a connection is open or being opened
    /// </summary>
    public async void Connect()
    {
        if (ws == null) await ConnectWebSocket();
    }

    /// <summary>
    /// closes the connection; does nothing if there is none
    /// </summary>
    public async void Disconnect()
    {
        if (ws != null) await DisposeWebSocket();
    }

    /// <summary>
    /// establishes websocket connection to the api and receives messages until it is closed
    /// </summary>
    private async Task ConnectWebSocket()
    {
        string key = ResolveApiKey();
        if (key == null)
        {
            Debug.LogError(
                "[Realtime] No API key found. Set the " + ApiKeyEnvironmentVariable + " environment variable, or save the key in a file named " +
                ApiKeyFileName + " in Application.persistentDataPath (in the Editor also next to the Assets folder), " +
                "or enter it in the Api Key field of RealtimeAPIWrapper. Never commit a key.", this);
            return;
        }

        var socket = new ClientWebSocket();
        var cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        ws = socket;
        connectionCancellation = cancellation;

        try
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + key);
            await socket.ConnectAsync(BuildUri(), token);
            if (ws != socket) return; // closed while connecting

            hasConnected = true;
            ResetResponseState();
            Raise(OnWebSocketConnected);

            // Set up the prompt, the voice and the gesture tool once connected
            await SetupSession();
        }
        catch (Exception e)
        {
            // If ws has changed, the connection was closed on purpose and has already been cleaned up.
            if (ws == socket)
            {
                Debug.LogError("websocket connection failed: " + e.Message, this);
                await DisposeWebSocket();
            }
            return;
        }

        if (ws == socket) await ReceiveMessages(socket, token);
    }

    /// <summary>
    /// returns the address of the api including the model parameter
    /// </summary>
    private Uri BuildUri()
    {
        string address = (endpoint ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(model))
        {
            string separator = address.Contains("?") ? "&" : "?";
            address += separator + "model=" + Uri.EscapeDataString(model.Trim());
        }
        return new Uri(address);
    }

    /// <summary>
    /// looks for the api key in the inspector, the environment and a local file; returns null if there is none
    /// </summary>
    private string ResolveApiKey()
    {
        string key = CleanKey(apiKey);
        if (key != null) return key;

        key = CleanKey(Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable));
        if (key != null) return key;

        key = ReadKeyFile(Application.persistentDataPath);
        if (key != null) return key;

#if UNITY_EDITOR
        // Project root, next to the Assets folder
        key = ReadKeyFile(Path.Combine(Application.dataPath, ".."));
#endif
        return key;
    }

    private static string ReadKeyFile(string folder)
    {
        try
        {
            string path = Path.Combine(folder, ApiKeyFileName);
            return File.Exists(path) ? CleanKey(File.ReadAllText(path)) : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Realtime] Could not read " + ApiKeyFileName + ": " + e.Message);
            return null;
        }
    }

    private static string CleanKey(string value)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) || value == ApiKeyPlaceholder ? null : value;
    }

    /// <summary>
    /// sends a cancel event to api if a response is in progress
    /// </summary>
    private async Task SendCancelEvent()
    {
        if (!isResponseInProgress) return;

        // Once the server has finished generating, there is nothing left to cancel on its side.
        bool isServerGenerating = !isResponseDoneOnServer;
        isResponseInProgress = false;

        if (isServerGenerating && !await SendMessages(new JObject { ["type"] = "response.cancel" })) return;
        OnResponseCancelled?.Invoke();
    }

    /// <summary>
    /// sends recorded audio to the api
    /// </summary>
    private async void SendAudioToAPI(string base64AudioData)
    {
        if (!IsConnected) return;

        await SendCancelEvent();

        var userMessage = new JObject
        {
            ["type"] = "conversation.item.create",
            ["item"] = new JObject
            {
                ["type"] = "message",
                ["role"] = "user",
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "input_audio", ["audio"] = base64AudioData }
                }
            }
        };

        var responseRequest = new JObject
        {
            ["type"] = "response.create",
            ["response"] = new JObject { ["output_modalities"] = new JArray { "audio" } }
        };

        await SendMessages(userMessage, responseRequest);
    }

    /// <summary>
    /// sends messages one after the other, without messages from other callers in between
    /// </summary>
    private async Task<bool> SendMessages(params JObject[] messages)
    {
        ClientWebSocket socket = ws;
        if (socket == null || connectionCancellation == null) return false;
        CancellationToken token = connectionCancellation.Token;

        bool hasLock = false;
        try
        {
            await sendLock.WaitAsync(token);
            hasLock = true;

            foreach (JObject message in messages)
            {
                if (socket.State != WebSocketState.Open) return false;
                byte[] messageBytes = Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
                await socket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, token);
            }
            return true;
        }
        catch (Exception e)
        {
            // Errors of a connection that was closed on purpose are expected.
            if (ws == socket) Debug.LogError("[Realtime] Sending failed: " + e.Message, this);
            return false;
        }
        finally
        {
            if (hasLock) sendLock.Release();
        }
    }

    /// <summary>
    /// receives messages from websocket and handles them until the connection ends
    /// </summary>
    private async Task ReceiveMessages(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[ReceiveBufferBytes];
        var messageHandlers = GetMessageHandlers();

        try
        {
            using (var messageBuffer = new MemoryStream())
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[Realtime] The server closed the connection: " + result.CloseStatusDescription);
                        break;
                    }

                    messageBuffer.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;

                    // Decode only complete messages, so that a character split across frames stays intact.
                    string fullMessage = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                    messageBuffer.SetLength(0);
                    HandleMessage(fullMessage, messageHandlers);
                }
            }
        }
        catch (Exception e)
        {
            // Errors of a connection that was closed on purpose are expected.
            if (ws == socket)
            {
                Debug.LogError("[Realtime] The connection was lost: " + e, this);
            }
        }

        if (ws == socket) await DisposeWebSocket();
    }

    /// <summary>
    /// parses one message and passes it to its handler
    /// </summary>
    private void HandleMessage(string json, Dictionary<string, Action<JObject>> messageHandlers)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        JObject eventMessage;
        try
        {
            eventMessage = JObject.Parse(json);
        }
        catch (JsonException e)
        {
            Debug.LogError("error parsing json: " + e.Message);
            return;
        }

        string messageType = eventMessage["type"]?.ToString() ?? "";
        if (!messageHandlers.TryGetValue(messageType, out var handler))
        {
            Debug.Log("unhandled message type: " + messageType);
            return;
        }

        try
        {
            handler(eventMessage);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    /// <summary>
    /// returns dictionary of message handlers for different message types
    /// </summary>
    private Dictionary<string, Action<JObject>> GetMessageHandlers()
    {
        return new Dictionary<string, Action<JObject>>
        {
            { "response.output_audio.delta", HandleAudioDelta },
            { "response.output_audio_transcript.delta", HandleTranscriptDelta },

            // conversation.item.added is the current name, conversation.item.created the earlier one.
            { "conversation.item.added", HandleConversationItem },
            { "conversation.item.created", HandleConversationItem },
            { "conversation.item.done", _ => { } },

            { "response.done", HandleResponseDone },
            { "response.created", HandleResponseCreated },

            { "session.created", _ => OnSessionCreated?.Invoke() },
            { "session.updated", _ => { } },
            { "response.output_audio.done", _ => OnResponseAudioDone?.Invoke() },
            { "response.output_audio_transcript.done", _ => OnResponseAudioTranscriptDone?.Invoke() },
            { "response.content_part.done", _ => OnResponseContentPartDone?.Invoke() },
            { "response.output_item.done", _ => OnResponseOutputItemDone?.Invoke() },
            { "response.output_item.added", _ => OnResponseOutputItemAdded?.Invoke() },
            { "response.content_part.added", _ => OnResponseContentPartAdded?.Invoke() },
            { "rate_limits.updated", _ => OnRateLimitsUpdated?.Invoke() },

            // Tool calls; the argument deltas are ignored because the done event carries the full arguments.
            { "response.function_call_arguments.delta", _ => { } },
            { "response.function_call_arguments.done", HandleFunctionCall },
            { "error", HandleError }
        };
    }

    /// <summary>
    /// handles incoming audio delta messages from api
    /// </summary>
    private void HandleAudioDelta(JObject eventMessage)
    {
        string base64AudioData = eventMessage["delta"]?.ToString();
        if (!string.IsNullOrEmpty(base64AudioData) && audioPlayer != null)
        {
            byte[] pcmAudioData = Convert.FromBase64String(base64AudioData);
            audioPlayer.EnqueueAudioData(pcmAudioData);
        }
    }

    /// <summary>
    /// handles incoming transcript delta messages from api
    /// </summary>
    private void HandleTranscriptDelta(JObject eventMessage)
    {
        string transcriptPart = eventMessage["delta"]?.ToString();
        if (!string.IsNullOrEmpty(transcriptPart))
        {
            transcriptBuffer.Append(transcriptPart);
            OnTranscriptReceived?.Invoke(transcriptPart);
        }
    }

    /// <summary>
    /// handles new conversation items; raises the event once per item
    /// </summary>
    private void HandleConversationItem(JObject eventMessage)
    {
        string itemId = eventMessage["item"]?["id"]?.ToString();
        if (!string.IsNullOrEmpty(itemId) && itemId == lastConversationItemId) return;

        lastConversationItemId = itemId;
        OnConversationItemCreated?.Invoke();
    }

    /// <summary>
    /// handles response.done message - the response ends once its audio has been played as well
    /// </summary>
    private void HandleResponseDone(JObject eventMessage)
    {
        isResponseDoneOnServer = true;
        ClearResponseInProgressWhenFinished();
        OnResponseDone?.Invoke();
    }

    /// <summary>
    /// handles response.created message - resets transcript buffer
    /// </summary>
    private void HandleResponseCreated(JObject eventMessage)
    {
        transcriptBuffer.Clear();
        isResponseInProgress = true;
        isResponseDoneOnServer = false;
        OnResponseCreated?.Invoke();
    }

    /// <summary>
    /// called on response.done and when the player runs out of audio; the later of the two ends the response
    /// </summary>
    private void ClearResponseInProgressWhenFinished()
    {
        if (!isResponseDoneOnServer) return;
        if (audioPlayer != null && audioPlayer.IsAudioPlaying()) return;
        isResponseInProgress = false;
    }

    private void ResetResponseState()
    {
        isResponseInProgress = false;
        isResponseDoneOnServer = true;
        lastConversationItemId = null;
    }

    /// <summary>
    /// handles error messages from api
    /// </summary>
    private void HandleError(JObject eventMessage)
    {
        string errorMsg = eventMessage["error"]?["message"]?.ToString();

        // Harmless timing notice: the response had already ended when response.cancel reached the server.
        if (!string.IsNullOrEmpty(errorMsg) && errorMsg.Contains("Cancellation failed: no active response found"))
        {
            return;
        }

        Debug.LogError($"openai error: {errorMsg}");
    }

    /// <summary>
    /// sends the prompt, the voice and the gesture tool to the api
    /// </summary>
    private async Task SetupSession()
    {
        var session = new JObject
        {
            ["type"] = "realtime",
            ["instructions"] = systemInstructions,
            ["audio"] = new JObject
            {
                ["output"] = new JObject { ["voice"] = voice }
            }
        };

        JArray tools = BuildTools();
        if (tools.Count > 0) session["tools"] = tools;

        bool isSent = await SendMessages(new JObject
        {
            ["type"] = "session.update",
            ["session"] = session
        });

        if (isSent) Debug.Log($"[Session] Prompt, voice ({voice}) and {tools.Count} tool(s) sent to the Realtime API");
    }

    /// <summary>
    /// describes the gesture tool; the list is empty if there are no gesture names
    /// </summary>
    private JArray BuildTools()
    {
        var names = new JArray();
        foreach (string gestureName in GestureNames)
        {
            if (!string.IsNullOrWhiteSpace(gestureName)) names.Add(gestureName);
        }

        var tools = new JArray();
        if (names.Count == 0) return tools;

        tools.Add(new JObject
        {
            ["type"] = "function",
            ["name"] = AnimationToolName,
            // Ask for a call in every response
            ["description"] = "CRITICAL: You MUST call this tool EVERY SINGLE TIME you speak. It provides an array of sequential animations to choreograph your 3D avatar's body language from the beginning to the end of your current sentence.",
            ["parameters"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["animation_sequence"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "string",
                            ["enum"] = names
                        },
                        // Explain that the gestures belong to the current response
                        ["description"] = "An array of exact animation names to play sequentially alongside your current spoken response. You must select strictly from the provided enum list and match the response length and emotion based on the context."
                    }
                },
                ["required"] = new JArray { "animation_sequence" }
            }
        });
        return tools;
    }

    /// <summary>
    /// closes and disposes the current connection, whatever state it is in, so that a new one can be opened
    /// </summary>
    private async Task DisposeWebSocket()
    {
        ClientWebSocket socket = ws;
        CancellationTokenSource cancellation = connectionCancellation;
        bool wasConnected = hasConnected;
        if (socket == null) return;

        ClearConnection();

        bool hasLock = false;
        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                using (var timeout = new CancellationTokenSource(CloseTimeoutMilliseconds))
                {
                    await sendLock.WaitAsync(timeout.Token);
                    hasLock = true;
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", timeout.Token);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Realtime] The connection was not closed cleanly: " + e.Message);
        }
        finally
        {
            if (hasLock) sendLock.Release();
            Release(socket, cancellation);
        }

        if (wasConnected) Raise(OnWebSocketClosed);
    }

    /// <summary>
    /// closes the connection without waiting for other tasks; used when the object or the application goes away.
    /// OnWebSocketClosed is not raised because its listeners may already have been destroyed
    /// </summary>
    private void CloseImmediately()
    {
        ClientWebSocket socket = ws;
        CancellationTokenSource cancellation = connectionCancellation;
        if (socket == null) return;

        ClearConnection();

        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                // Runs on the thread pool, so that this short wait cannot block its own continuation.
                Task.Run(() => socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None))
                    .Wait(QuitCloseTimeoutMilliseconds);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Realtime] The connection was not closed cleanly: " + e.Message);
        }
        finally
        {
            Release(socket, cancellation);
        }
    }

    private void ClearConnection()
    {
        ws = null;
        connectionCancellation = null;
        hasConnected = false;
        ResetResponseState();
    }

    private static void Release(ClientWebSocket socket, CancellationTokenSource cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Realtime] " + e.Message);
        }

        cancellation?.Dispose();
        socket.Dispose();
    }

    /// <summary>
    /// raises an event so that one failing listener does not stop the others or the connection
    /// </summary>
    private static void Raise(Action listeners)
    {
        if (listeners == null) return;

        foreach (Delegate listener in listeners.GetInvocationList())
        {
            try
            {
                ((Action)listener)();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    /// <summary>
    /// reads the gestures requested by the model, starts them, and always answers the call
    /// </summary>
    private void HandleFunctionCall(JObject eventMessage)
    {
        string callId = eventMessage["call_id"]?.ToString();
        string functionName = eventMessage["name"]?.ToString();
        string arguments = eventMessage["arguments"]?.ToString();

        string error = null;
        List<string> gestures = null;

        if (!string.IsNullOrEmpty(functionName) && functionName != AnimationToolName)
        {
            error = "Unknown function: " + functionName;
        }
        else if (!TryReadGestures(arguments, out gestures))
        {
            error = "The arguments must be a JSON object with an array of gesture names called animation_sequence.";
        }

        if (error != null)
        {
            Debug.LogError("[Realtime] Function call failed. " + error);
        }
        else if (gestures.Count > 0)
        {
            try
            {
                OnAnimationSequenceReceived?.Invoke(gestures);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        SendFunctionCallOutput(callId, error);
    }

    private static bool TryReadGestures(string arguments, out List<string> gestures)
    {
        gestures = null;
        try
        {
            JObject argsJson = JObject.Parse(arguments ?? "");
            if (argsJson["animation_sequence"] is JArray animArray) gestures = animArray.ToObject<List<string>>();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Realtime] Could not read the function arguments: " + e.Message);
        }
        return gestures != null;
    }

    /// <summary>
    /// tells the model whether the tool call worked and asks it to continue its response
    /// </summary>
    private async void SendFunctionCallOutput(string callId, string error)
    {
        if (string.IsNullOrEmpty(callId))
        {
            Debug.LogWarning("[Realtime] A function call without a call_id cannot be answered.");
            return;
        }

        var status = new JObject { ["status"] = error == null ? "success" : "error" };
        if (error != null) status["message"] = error;

        var functionOutput = new JObject
        {
            ["type"] = "conversation.item.create",
            ["item"] = new JObject
            {
                ["type"] = "function_call_output",
                ["call_id"] = callId,
                ["output"] = status.ToString(Formatting.None)
            }
        };

        // Without response.create the model would not continue after the tool result, and the agent would stay silent.
        await SendMessages(functionOutput, new JObject { ["type"] = "response.create" });
    }
}
