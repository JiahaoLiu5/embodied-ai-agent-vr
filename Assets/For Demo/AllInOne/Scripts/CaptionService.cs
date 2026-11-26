using System;
using DemoKit;

namespace EmbodiedAgent.AllInOne
{
    /// <summary>
    /// Subtitles for the narration and for the agent, which the user can switch off. The end of the agent's speech never
    /// removes a narration that has started meanwhile.
    /// </summary>
    public sealed class CaptionService
    {
        // More unknown characters than this belong to a language that the font does not cover.
        private const int MostUnknownCharacters = 2;

        private readonly DemoCaptionBar bar;
        private bool isOn;
        private string text;
        private float seconds;
        private Func<float> clock;

        public CaptionService(DemoCaptionBar bar, bool isOn)
        {
            this.bar = bar;
            this.isOn = isOn;
        }

        public DemoCaptionBar Bar => bar;

        public bool On
        {
            get { return isOn; }
            set
            {
                isOn = value;
                if (isOn) Display();
                else bar.Stop();
            }
        }

        /// <summary>Shows a transcript in step with a narration. The clock returns the seconds since it started.</summary>
        public void Narrate(string transcript, float length, Func<float> narrationClock) => Set(transcript, length, narrationClock);

        /// <summary>Shows one line of the agent until the next call. A language that the font does not cover is left out.</summary>
        public void Say(string line, string speaker)
        {
            if (CanShow(line)) Set(speaker + ": " + line, 0f, null);
            else EndSpeech();
        }

        /// <summary>Removes the line of the agent.</summary>
        public void EndSpeech()
        {
            if (clock == null) Clear();
        }

        public void Clear()
        {
            Set(null, 0f, null);
            bar.Clock = null;
            bar.Stop();
        }

        private static bool CanShow(string line)
        {
            DemoUi.Theme.Regular.HasCharacters(line, out uint[] unknown, true, true);
            return unknown == null || unknown.Length <= MostUnknownCharacters;
        }

        private void Set(string newText, float newSeconds, Func<float> newClock)
        {
            text = newText;
            seconds = newSeconds;
            clock = newClock;
            if (isOn) Display();
        }

        private void Display()
        {
            if (text == null) return;
            bar.Clock = clock;
            if (clock == null) bar.Show(text);
            else if (clock() < seconds) bar.Play(text, seconds, null, clock());
        }
    }
}
