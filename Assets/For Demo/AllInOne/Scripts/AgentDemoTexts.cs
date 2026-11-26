using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>All texts of the integrated demo.</summary>
    public static class AgentDemoTexts
    {
        public const string AgentName = "Amy";

        // Start panel
        public const string AvatarTitle = "Choose your avatar";
        public const string AvatarSubtitle = "This will be your body. The figure next to this panel moves with you.";
        public const string RecentreHint = "Stand upright, look straight ahead, then press Recentre.";
        public const string MovementTitle = "Choose a movement transformation";
        public const string MovementSubtitle = "Your view never changes. Only the avatars do.";
        public const string IndependentText = "No transformation. You and Amy move on your own.";
        public const string LeadingText = "Amy leads. Your avatar carries a share of her movement.";
        public const string MimicryText = "Amy mirrors you. Her body carries a share of your movement.";
        public const string NoiseText = "Your avatar carries a share of recorded movement that is unrelated to the conversation.";
        public const string StrengthNote = "30 % is the default.";
        public const string MovementHint = "Try it: raise a hand and watch Amy, the mirrors, and the figure.";
        public const string ExperienceTitle = "Choose an experience";
        public const string FreeText = "Talk with Amy about anything, as long as you like.";
        public const string FreeInfoTitle = "How it works";
        public const string FreeInfoVoice = "Amy greets you first. Then just speak, no button is needed.";
        public const string FreeInfoInterrupt = "You can interrupt her at any time.";
        public const string FreeInfoMovement = "Change the movement and its strength while you talk.";
        public const string SessionText = "A guided session with a warm-up, a preparation, and a timed debate on one topic.";
        public const string StudyPaceInfo = "About 8 minutes.";
        public const string QuickPaceInfo = "About 3.5 minutes. The spoken instructions still name the study times.";
        public const string ReadyTitle = "Ready";
        public const string KeyNote = "When you start talking, your voice is sent to OpenAI. This app does not save audio, text, or movement. " +
                                      "Wear earphones so that Amy does not hear herself.";
        public const string NoKeyNote = "No API key was found. Amy can move, but she cannot talk. Everything else works.";

        // Key help
        public const string KeyHelpTitle = "Add an OpenAI API key";
        public const string KeyWarning = "Keep the key to yourself. Never put it into a scene, a build, or a repository.";

        // Conversation
        public const string HandsFreeHint = "Just speak. Amy notices when you start and stop. You can interrupt her.";
        public const string HandsFreeNoInterruptHint = "Just speak. Amy notices when you start and stop.";
        public const string NoKeyHint = "Amy gestures on her own until a key is added.";
        public const string PushToTalkHeadset = "Hold A on the right controller while you speak.";
        public const string PushToTalkDesktop = "Hold the space bar while you speak.";
        public const string InterruptLabel = "Let me interrupt Amy";
        public const string KeyHelpButton = "How to add a key";
        public const string GreetingNote = "Greet the user in one short, friendly sentence and wait for the answer.";
        public const string Reconnecting = "The connection was lost. Reconnecting.";
        public const string NoAnswer = "No answer yet. Try to speak again.";
        public const string StoppedListening = "Amy stopped listening after five minutes of silence.";
        public const string ForgotConversation = "Amy does not remember what was said before the break.";
        public const string NoRecordings = "The recorded movement could not be loaded.";
        public const string PanelHint = "The control panel is on your left.";

        // Session
        public const string FinishedTitle = "Session finished";
        public const string FinishedText = "Thank you for taking part.";
        public const string LeaveTitle = "Leave the session?";
        public const string LeaveText = "The session is paused. Leaving ends it.";
        public const string Paused = "Paused";

        public static string ModeTitle(MovementMode mode) => mode.ToString();

        public static string ModeId(MovementMode mode) => mode.ToString().ToLowerInvariant();

        /// <summary>For example "Leading, 30 %". Independent has no strength.</summary>
        public static string ModeSummary(MovementMode mode, float strength) =>
            mode == MovementMode.Independent ? ModeTitle(mode) : ModeTitle(mode) + ", " + Mathf.RoundToInt(strength * 100f) + " %";

        public static string PushToTalkHint(bool isHeadset) => isHeadset ? PushToTalkHeadset : PushToTalkDesktop;

        public static string PaceTitle(SessionPace pace) => pace == SessionPace.Study ? "Study timing" : "Quick preview";

        public static string StateText(ConversationState state)
        {
            switch (state)
            {
                case ConversationState.NoKey: return "Cannot talk without a key";
                case ConversationState.Connecting: return "Connecting";
                case ConversationState.Listening: return "Listening";
                case ConversationState.UserSpeaking: return "You are speaking";
                case ConversationState.Thinking: return "Thinking";
                case ConversationState.AgentSpeaking: return "Speaking";
                case ConversationState.Suspended: return "Paused";
                case ConversationState.Problem: return "Not available";
                default: return "Not connected";
            }
        }

        public static string KeyHelp()
        {
            if (Application.isEditor)
                return "Set the environment variable OPENAI_API_KEY and restart Unity Hub and the Editor, or save the key in a file named " +
                       "openai_api_key.txt next to the Assets folder.";
            if (Application.platform == RuntimePlatform.Android)
                return "1. Save the key in a text file named openai_api_key.txt.\n2. Connect the headset to a computer.\n" +
                       "3. Copy the file to Android/data/" + Application.identifier + "/files (start this app once before).\n4. Press Check again.";
            return "Set the environment variable OPENAI_API_KEY, or save the key in a file named openai_api_key.txt next to this program.";
        }

        public static string KeyHelpDetail() =>
            Application.platform == RuntimePlatform.Android
                ? "With adb: adb push openai_api_key.txt /sdcard/Android/data/" + Application.identifier + "/files/"
                : "";

        public static string ProblemTitle(RealtimeProblem problem)
        {
            switch (problem)
            {
                case RealtimeProblem.NoKey: return "No API key";
                case RealtimeProblem.KeyRejected: return "The API key was not accepted";
                case RealtimeProblem.NoAccess: return "This key cannot use the model";
                case RealtimeProblem.NoCredit: return "No credit left";
                case RealtimeProblem.RateLimit: return "Too many requests";
                case RealtimeProblem.NoInternet: return "No internet connection";
                case RealtimeProblem.ConnectFailed: return "The connection could not be opened";
                case RealtimeProblem.ConnectionLost: return "The connection was lost";
                case RealtimeProblem.SessionEnded: return "The conversation reached its time limit";
                case RealtimeProblem.MicDenied: return "The microphone is not allowed";
                case RealtimeProblem.MicMissing: return "No microphone found";
                case RealtimeProblem.MicRate: return "The microphone does not record at 24 kHz";
                default: return "The service reported a problem";
            }
        }

        /// <summary>Title and text in one line, for a short message.</summary>
        public static string ProblemLine(RealtimeProblem problem, string model, string detail) =>
            (ProblemTitle(problem) + ". " + ProblemText(problem, model, detail)).Trim();

        public static string ProblemText(RealtimeProblem problem, string model, string detail)
        {
            switch (problem)
            {
                case RealtimeProblem.NoKey: return "Amy can move, but she cannot talk. The menu explains how to add a key.";
                case RealtimeProblem.KeyRejected: return "Check the key, copy the file again, then press Try again.";
                case RealtimeProblem.NoAccess: return "The key needs access to " + model + ".";
                case RealtimeProblem.NoCredit: return "The OpenAI account has no credit or has reached its limit.";
                case RealtimeProblem.RateLimit: return "Wait a moment, then try again.";
                case RealtimeProblem.NoInternet: return "Check the network, then try again.";
                case RealtimeProblem.ConnectFailed: return "Check the API key and the network, then try again.";
                case RealtimeProblem.ConnectionLost: return "Amy will not remember what was said before.";
                case RealtimeProblem.SessionEnded: return "Press Try again to begin a new one.";
                case RealtimeProblem.MicDenied: return "Allow the microphone for this app in the headset settings.";
                case RealtimeProblem.MicMissing: return "Connect a microphone, then try again.";
                case RealtimeProblem.MicRate: return "Choose another microphone. Amy may misunderstand you.";
                default: return detail;
            }
        }
    }
}