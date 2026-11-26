using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using UnityEngine;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

public class RealtimeAPIWrapper : MonoBehaviour
{
    private ClientWebSocket ws;
    [SerializeField] string apiKey = "YOUR_API_KEY";

    // Control Variables from the Inspector
    [Header("AI Agent Configurations")]
    [TextArea(5, 15)] 
    [SerializeField] private string systemInstructions = "You are a helpful, empathetic, and concise embodied AI assistant. Please respond with brief and natural conversational sentences.";

    [Tooltip("alloy, ash, ballad, cedar, coral, echo, marin, sage, shimmer, verse")]
    [SerializeField] private string voice = "shimmer";

    public AudioPlayer audioPlayer;
    public AudioRecorder audioRecorder;
    private StringBuilder messageBuffer = new StringBuilder();
    private StringBuilder transcriptBuffer = new StringBuilder();
    private bool isResponseInProgress = false;

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

    // Newly added for auto-animation, change for multi-animation
    public static event Action<List<string>> OnAnimationSequenceReceived;

    private void Start() => AudioRecorder.OnAudioRecorded += SendAudioToAPI;
    private void OnApplicationQuit() => DisposeWebSocket();


    /// <summary>
    /// connects or disconnects websocket when button is pressed
    /// </summary>
    public async void ConnectWebSocketButton()
    {
        if (ws != null) DisposeWebSocket();
        else
        {
            ws = new ClientWebSocket();
            await ConnectWebSocket();
        }
    }

    /// <summary>
    /// establishes websocket connection to the api
    /// </summary>
    private async Task ConnectWebSocket()
    {
        try
        {
            var uri = new Uri("wss://api.openai.com/v1/realtime?model=gpt-realtime-2.1-mini");
            ws.Options.SetRequestHeader("Authorization", "Bearer " + ApiKeyFallback.Resolve(apiKey));
            
            await ws.ConnectAsync(uri, CancellationToken.None);
            OnWebSocketConnected?.Invoke();
            
            // Set up the prompt and voice once successfully connected
            await SetupSession();

            _ = ReceiveMessages();
        }
        catch (Exception e)
        {
            Debug.LogError("websocket connection failed: " + e.Message);
        }
    }

    /// <summary>
    /// sends a cancel event to api if response is in progress
    /// </summary>
    private async void SendCancelEvent()
    {
        if (ws.State == WebSocketState.Open && isResponseInProgress)
        {
            var cancelMessage = new
            {
                type = "response.cancel"
            };
            string jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(cancelMessage);
            byte[] messageBytes = Encoding.UTF8.GetBytes(jsonString);
            await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            OnResponseCancelled?.Invoke();
            isResponseInProgress = false;
        }
    }

    /// <summary>
    /// sends recorded audio to the api
    /// </summary>
    private async void SendAudioToAPI(string base64AudioData)
    {
        if (isResponseInProgress)
            SendCancelEvent();

        if (ws != null && ws.State == WebSocketState.Open)
        {
            var eventMessage = new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "message",
                    role = "user",
                    content = new[]
                    {
                        new { type = "input_audio", audio = base64AudioData }
                    }
                }
            };

            string jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(eventMessage);
            byte[] messageBytes = Encoding.UTF8.GetBytes(jsonString);
            await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);

            var responseMessage = new
            {
                type = "response.create",
                response = new
                {
                    output_modalities = new[] { "audio" }
                }
            };
            string responseJson = Newtonsoft.Json.JsonConvert.SerializeObject(responseMessage);
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);
            await ws.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

    /// <summary>
    /// receives messages from websocket and handles them
    /// </summary>
    private async Task ReceiveMessages()
    {
        var buffer = new byte[1024 * 128];
        var messageHandlers = GetMessageHandlers();

        while (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (ws.State == WebSocketState.CloseReceived)
            {
                Debug.Log("websocket close received, disposing current ws instance.");
                DisposeWebSocket();
                return;
            }

            if (result.EndOfMessage)
            {
                string fullMessage = messageBuffer.ToString();

                messageBuffer.Clear();

                if (!string.IsNullOrEmpty(fullMessage.Trim()))
                {
                    try
                    {
                        JObject eventMessage = JObject.Parse(fullMessage);
                        string messageType = eventMessage["type"]?.ToString();

                        if (messageHandlers.TryGetValue(messageType, out var handler)) handler(eventMessage);

                        else Debug.Log("unhandled message type: " + messageType);

                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("error parsing json: " + ex.Message);
                    }
                }
            }
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
            { "conversation.item.created", _ => OnConversationItemCreated?.Invoke() },
            
            { "response.done", HandleResponseDone },
            { "response.created", HandleResponseCreated },
            //{ "response.function_call_arguments.delta", _ => { } },
            //{ "conversation.item.added", _ => { } },
            //{ "conversation.item.done", _ => { } },
            //{ "response.created", _ => { } },
            //{ "response.done", _ => { } },
            
            { "session.created", _ => OnSessionCreated?.Invoke() },
            { "response.output_audio.done", _ => OnResponseAudioDone?.Invoke() },
            { "response.output_audio_transcript.done", _ => OnResponseAudioTranscriptDone?.Invoke() },
            { "response.content_part.done", _ => OnResponseContentPartDone?.Invoke() },
            { "response.output_item.done", _ => OnResponseOutputItemDone?.Invoke() },
            { "response.output_item.added", _ => OnResponseOutputItemAdded?.Invoke() },
            { "response.content_part.added", _ => OnResponseContentPartAdded?.Invoke() },
            { "rate_limits.updated", _ => OnRateLimitsUpdated?.Invoke() },
            // Newly added - monitor the tool calling function
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
        if (!string.IsNullOrEmpty(base64AudioData))
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
    /// handles response.done message - checks if audio is still playing
    /// </summary>
    private void HandleResponseDone(JObject eventMessage)
    {
        if (!audioPlayer.IsAudioPlaying())
        {
            isResponseInProgress = false;
        }
        OnResponseDone?.Invoke();
    }

    /// <summary>
    /// handles response.created message - resets transcript buffer
    /// </summary>
    private void HandleResponseCreated(JObject eventMessage)
    {
        transcriptBuffer.Clear();
        isResponseInProgress = true;
        OnResponseCreated?.Invoke();
    }

    /// <summary>
    /// handles error messages from api
    /// </summary>
    private void HandleError(JObject eventMessage)
    {
        string errorMsg = eventMessage["error"]?["message"]?.ToString();
        
        // 【核心修改】：精准拦截并屏蔽无害的并发时序警告
        if (!string.IsNullOrEmpty(errorMsg) && errorMsg.Contains("Cancellation failed: no active response found"))
        {
            // 这是 OpenAI 端的机制提示，不影响运行，直接 return 丢弃，不再打印红字
            return; 
        }

        // 如果是其他的严重错误，正常打印红字
        Debug.LogError($"openai error: {errorMsg}");
    }

    // send session.update to OpenAI 
    private async Task SetupSession()
    {
        if (ws != null && ws.State == WebSocketState.Open)
        {
            var sessionUpdate = new
            {
                type = "session.update",
                session = new
                {
                    type = "realtime",
                    instructions = systemInstructions,
                    audio = new   //compatible to the new GA system
                    {
                        output = new
                        {
                            voice = voice
                        }
                    },

                    tools = new[]
                    {
                        new {
                            type = "function",
                            name = "trigger_animation",
                            // Require calling for every response
                            description = "CRITICAL: You MUST call this tool EVERY SINGLE TIME you speak. It provides an array of sequential animations to choreograph your 3D avatar's body language from the beginning to the end of your current sentence.",
                            parameters = new {
                                type = "object",
                                properties = new {
                                    animation_sequence = new { 
                                        type = "array", 
                                        items = new {
                                            type = "string",
                                            
                                            
                                            @enum = new[] { 
                                                "Brief Talking (1)", "Brief Talking (2)", "Cocky Head Turn", 
                                                "Disagreeing Talking", "Head Nod Yes", "Standing Arguing", 
                                                "Standing Thumbs Up", "Talking (0)", "Talking (1)", "Talking (2)", 
                                                "Talking (3)", "Talking (4)", "Talking (5)", "Talking (6)", 
                                                "Talking (7)", "Talking (8)", "Talking (9)", "Talking (10)", 
                                                "Talking (11)", "Talking Ideas (1)", "Talking Ideas (2)", 
                                                "Thoughtful Head Nod", "Thoughtful Head Shake", "Waving" 
                                            }
                                            
                                            
                                            /*
                                            @enum = new[] { 
                                                "Brief Talking (1)", "Brief Talking (2)", "Cocky Head Turn", 
                                                "Disagreeing Talking", "Head Nod Yes", 
                                                "Standing Thumbs Up",  "Talking Ideas (1)", "Talking Ideas (2)", 
                                                "Thoughtful Head Nod", "Thoughtful Head Shake", "Waving", 
                                                "Sad Idle", "Disappointed (1)", "Defeat", "Happy Idle",
                                                "Shaking Hands", "Annoyed Head Shake", "Quick Open-Palm Gesture",
                                                "Relieved Sigh"
                                            }
                                            */

                                        },
                                        // Explain this is the animation for the current response
                                        description = "An array of exact animation names to play sequentially alongside your current spoken response. You must select strictly from the provided enum list and match the response length and emotion based on the context." 
                                    }
                                },
                                required = new[] { "animation_sequence" }
                            }
                        }
                    }
                }
            };

            string jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(sessionUpdate);
            byte[] messageBytes = Encoding.UTF8.GetBytes(jsonString);
            await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            Debug.Log($"[Session] Prompt and voice ({voice}) successfully updated to OpenAI");
        }
    }

    /// <summary>
    /// disposes the websocket connection
    /// </summary>
    private async void DisposeWebSocket()
    {
        if (ws != null && (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived))
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None);
            ws.Dispose();
            ws = null;
            OnWebSocketClosed?.Invoke();
        }
    }

    // Newly added method: Parse the JSON returned by the LLM and trigger the animation
    private void HandleFunctionCall(JObject eventMessage)
    {
        string functionName = eventMessage["name"]?.ToString();
        if (functionName == "trigger_animation")
        {
            try
            {
                string arguments = eventMessage["arguments"]?.ToString();
                JObject argsJson = JObject.Parse(arguments);
                
                // Change - Analyse JSON array as List<string>
                JArray animArray = (JArray)argsJson["animation_sequence"];
                List<string> animList = animArray.ToObject<List<string>>();
                if (animList != null && animList.Count > 0)
                {
                    OnAnimationSequenceReceived?.Invoke(animList); 
                }
                string callId = eventMessage["call_id"]?.ToString();
                SendFunctionCallOutput(callId);
            }
            catch (Exception ex)
            {
                Debug.LogError("Error parsing animation function: " + ex.Message);
            }
        }
    }

    // Newly added method: Send a tool execution completion confirmation to the LLM
    private async void SendFunctionCallOutput(string callId)
    {
        if (ws != null && ws.State == WebSocketState.Open)
        {
            // 1. 提交函数执行结果（告诉 GPT 动作收到了）
            var outputMsg = new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = "{\"status\": \"success\"}"
                }
            };
            string jsonString = Newtonsoft.Json.JsonConvert.SerializeObject(outputMsg);
            byte[] messageBytes = Encoding.UTF8.GetBytes(jsonString);
            await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);

            // 2. 【极其关键：必须加回来】命令 GPT 根据结果继续生成并发送音频！
            var responseMsg = new { type = "response.create" };
            string respJson = Newtonsoft.Json.JsonConvert.SerializeObject(responseMsg);
            byte[] respBytes = Encoding.UTF8.GetBytes(respJson);
            await ws.SendAsync(new ArraySegment<byte>(respBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}






