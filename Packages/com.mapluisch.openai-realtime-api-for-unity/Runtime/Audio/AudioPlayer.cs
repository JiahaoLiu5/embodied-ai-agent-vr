using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioPlayer : MonoBehaviour
{
    private AudioSource audioSource;
    private bool isPlayingAudio = false;
    private bool cancelPending = false;
    public float[] aiFrequencyData { get; private set; }
    private List<float> audioBuffer = new List<float>();
    private AudioClip playbackClip;
    private const int BUFFER_SIZE = 48000;
    private const float MIN_BUFFER_TIME = 0.1f;
    public int sampleRate = 24000;
    public int fftSampleSize = 1024;

    /// <summary>
    /// raised when all buffered audio has been played or when playback is cancelled
    /// </summary>
    public event Action OnPlaybackFinished;

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
    }

    private void OnDestroy()
    {
        ReleasePlaybackClip();
    }

    /// <summary>
    /// enqueues audio data for playback
    /// </summary>
    public void EnqueueAudioData(byte[] pcmAudioData)
    {
        if (cancelPending) return;
        float[] floatData = AudioProcessingUtils.ConvertPCM16ToFloat(pcmAudioData);
        audioBuffer.AddRange(floatData);
        if (!isPlayingAudio)
        {
            StartCoroutine(PlayAudioCoroutine());
        }
    }

    private IEnumerator PlayAudioCoroutine()
    {
        isPlayingAudio = true;
        while (isPlayingAudio)
        {
            if (audioBuffer.Count >= sampleRate * MIN_BUFFER_TIME)
            {
                int samplesToPlay = Mathf.Min(BUFFER_SIZE, audioBuffer.Count);
                float[] audioChunk = new float[samplesToPlay];
                audioBuffer.CopyTo(0, audioChunk, 0, samplesToPlay);
                audioBuffer.RemoveRange(0, samplesToPlay);
                PlayChunk(audioChunk);
                // Wait for the duration of this chunk, computed from its length
                yield return new WaitForSeconds((float)samplesToPlay / sampleRate);
            }
            else if (audioBuffer.Count > 0)
            {
                float[] audioChunk = audioBuffer.ToArray();
                audioBuffer.Clear();
                PlayChunk(audioChunk);
                // Wait for the duration of this chunk, computed from its length
                yield return new WaitForSeconds((float)audioChunk.Length / sampleRate);
            }
            // AudioSource.isPlaying is not checked here because it proved unreliable.
            // Once the buffer is empty and the wait above is over, the audio counts as played.
            else
            {
                // Leave 0.1 s for a late network packet
                yield return new WaitForSeconds(0.1f);
                if (audioBuffer.Count == 0) break;
            }
        }
        ClearAudioBuffer();
    }

    /// <summary>
    /// plays one chunk and destroys the clip of the previous chunk
    /// </summary>
    private void PlayChunk(float[] audioChunk)
    {
        AudioClip previousClip = playbackClip;
        playbackClip = AudioClip.Create("PlaybackClip", audioChunk.Length, 1, sampleRate, false);
        playbackClip.SetData(audioChunk, 0);
        audioSource.clip = playbackClip;
        audioSource.Play();
        if (previousClip != null) Destroy(previousClip);
    }

    /// <summary>
    /// destroys the current clip; AudioClips created at runtime are not garbage collected
    /// </summary>
    private void ReleasePlaybackClip()
    {
        if (playbackClip == null) return;
        if (audioSource != null && audioSource.clip == playbackClip) audioSource.clip = null;
        Destroy(playbackClip);
        playbackClip = null;
    }

    private void Update()
    {
        UpdateAIFrequencyData();
    }

    private void UpdateAIFrequencyData()
    {
        if (!audioSource.isPlaying)
        {
            aiFrequencyData = null;
            return;
        }
        int fftSize = fftSampleSize;
        aiFrequencyData = new float[fftSize];
        audioSource.GetSpectrumData(aiFrequencyData, 0, FFTWindow.BlackmanHarris);
    }

    /// <summary>
    /// cancels audio playback
    /// </summary>
    public void CancelAudioPlayback()
    {
        cancelPending = true;
        StopAllCoroutines();
        ClearAudioBuffer();
    }

    /// <summary>
    /// clears audio buffer
    /// </summary>
    public void ClearAudioBuffer()
    {
        bool wasPlaying = IsAudioPlaying();
        audioBuffer.Clear();
        audioSource.Stop();
        ReleasePlaybackClip();
        isPlayingAudio = false;
        aiFrequencyData = null;
        if (wasPlaying) OnPlaybackFinished?.Invoke();
    }

    /// <summary>
    /// checks if audio is playing
    /// </summary>
    public bool IsAudioPlaying()
    {
        // AudioSource.isPlaying is deliberately not used: it is false between two chunks.
        // The playback coroutine and the buffer together tell whether the agent is still speaking.
        return isPlayingAudio || audioBuffer.Count > 0;
    }

    /// <summary>
    /// resets cancel pending flag
    /// </summary>
    public void ResetCancelPending()
    {
        cancelPending = false;
    }
}
