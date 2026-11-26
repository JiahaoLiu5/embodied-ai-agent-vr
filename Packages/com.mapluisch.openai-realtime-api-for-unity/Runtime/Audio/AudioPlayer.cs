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

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
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
                playbackClip = AudioClip.Create("PlaybackClip", samplesToPlay, 1, sampleRate, false);
                playbackClip.SetData(audioChunk, 0);
                audioSource.clip = playbackClip;
                audioSource.Play();
                // 数学时间计算：精准等待这段音频播完
                yield return new WaitForSeconds((float)samplesToPlay / sampleRate);
            }
            else if (audioBuffer.Count > 0)
            {
                float[] audioChunk = audioBuffer.ToArray();
                audioBuffer.Clear();
                playbackClip = AudioClip.Create("PlaybackClip", audioChunk.Length, 1, sampleRate, false);
                playbackClip.SetData(audioChunk, 0);
                audioSource.clip = playbackClip;
                audioSource.Play();
                // 数学时间计算：精准等待这段音频播完
                yield return new WaitForSeconds((float)audioChunk.Length / sampleRate);
            }
            // 【核心修复】：删除了 && !audioSource.isPlaying 的不可靠判断
            // 只要缓冲区空了，上面的等待也结束了，我们就认定播放逻辑结束
            else if (audioBuffer.Count == 0) 
            {
                // 预留 0.1 秒的微小缓冲，防止网络波动导致下个数据包还在路上
                yield return new WaitForSeconds(0.1f);
                if (audioBuffer.Count == 0) 
                {
                    isPlayingAudio = false; // 完美结束协程状态
                }
            }
            else
            {
                yield return null;
            }
        }
        ClearAudioBuffer();
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
        audioBuffer.Clear();
        audioSource.Stop();
        isPlayingAudio = false;
        aiFrequencyData = null;
    }

    /// <summary>
    /// checks if audio is playing
    /// </summary>
    public bool IsAudioPlaying()
    {
        // return audioSource.isPlaying || audioBuffer.Count > 0;
        // 核心修复：彻底抛弃 audioSource.isPlaying
        // 只依靠处理 GPT 缓冲区的协程状态 (isPlayingAudio) 来判断是否真正在说话
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


