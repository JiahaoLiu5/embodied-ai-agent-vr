using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// Shows what the agent says, sentence by sentence. The text arrives faster than the voice, so the sentence is chosen
    /// by how much of the audio has been played. A sentence that is still arriving grows on screen.
    /// </summary>
    public sealed class SpeechCaptions : MonoBehaviour
    {
        private const int LongestLine = 96;
        private const float HoldSeconds = 1f;

        [SerializeField] private RealtimeAPIWrapper api;
        [SerializeField] private AudioPlayer player;

        private readonly StringBuilder text = new StringBuilder();
        private readonly List<int> ends = new List<int>();
        private int shown = -1, shownEnd;
        private bool isShowing;
        private float silence;

        public CaptionService Captions { get; set; }

        private void OnEnable()
        {
            RealtimeAPIWrapper.OnResponseCreated += Begin;
            RealtimeAPIWrapper.OnTranscriptReceived += Add;
        }

        private void OnDisable()
        {
            RealtimeAPIWrapper.OnResponseCreated -= Begin;
            RealtimeAPIWrapper.OnTranscriptReceived -= Add;
        }

        private void Update()
        {
            if (Captions == null) return;

            if (!player.IsAudioPlaying())
            {
                silence += Time.deltaTime;
                if (isShowing && silence > HoldSeconds) Hide();
                return;
            }

            silence = 0f;
            if (ends.Count == 0) return;
            int line = LineAt(Position());
            if (line == shown && ends[line] == shownEnd) return;
            shown = line;
            shownEnd = ends[line];
            isShowing = true;
            int start = line == 0 ? 0 : ends[line - 1];
            Captions.Say(text.ToString(start, ends[line] - start).Trim(), AgentDemoTexts.AgentName);
        }

        /// <summary>Removes the caption, for example when the conversation ends.</summary>
        public void Stop()
        {
            Hide();
            Begin();
        }

        /// <summary>The text stays, because the next answer may have begun to arrive.</summary>
        private void Hide()
        {
            if (isShowing) Captions?.EndSpeech();
            isShowing = false;
            shown = -1;
        }

        private void Begin()
        {
            text.Clear();
            ends.Clear();
            shown = -1;
        }

        private void Add(string part)
        {
            text.Append(part);
            Split();
        }

        /// <summary>The character that is being spoken, estimated from the share of the audio that was played.</summary>
        private int Position()
        {
            int received = api.ReceivedMilliseconds;
            float share = received > 0 ? Mathf.Clamp01(player.PlayedMilliseconds / (float)received) : 0f;
            return (int)(share * text.Length);
        }

        private int LineAt(int position)
        {
            for (int i = 0; i < ends.Count; i++)
                if (position < ends[i]) return i;
            return ends.Count - 1;
        }

        /// <summary>Ends lines after sentences, or at the last space of a long one.</summary>
        private void Split()
        {
            ends.Clear();
            int start = 0, space = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ') space = i + 1;
                bool isSentenceEnd = c == '.' || c == '!' || c == '?';
                if (isSentenceEnd && i + 1 < text.Length && text[i + 1] == ' ' && i - start > LongestLine / 4) End(i + 1);
                else if (i - start >= LongestLine && space > start) End(space);
            }
            if (start < text.Length) ends.Add(text.Length);

            void End(int at)
            {
                ends.Add(at);
                start = at;
            }
        }
    }
}