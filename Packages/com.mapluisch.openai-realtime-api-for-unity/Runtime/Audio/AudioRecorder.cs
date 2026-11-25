using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Drop-in replacement for the original AudioRecorder.
///
/// VAD design:
/// 1. Measures short-window RMS level in dBFS.
/// 2. Maintains an adaptive background-noise floor while idle.
/// 3. Uses separate start and continue thresholds (hysteresis).
/// 4. Ends an utterance only after continuous low-level audio for a configured duration.
/// 5. Keeps pre-roll audio so short first syllables are not clipped.
/// 6. Stores the active utterance separately, so speech longer than the microphone ring buffer
///    is not truncated to the last 10 seconds.
///
/// This remains an energy-based VAD. No energy-only detector can perfectly distinguish
/// very quiet speech from noise; use a model-based VAD such as Silero/WebRTC when that
/// distinction must be substantially more robust.
/// </summary>
public class AudioRecorder : MonoBehaviour
{
    [Header("General")]
    public ListeningMode listeningMode = ListeningMode.PushToTalk;
    public int sampleRate = 24000;

    [Tooltip("Name of the microphone as Unity lists it in Microphone.devices. " +
             "Empty uses the first device.")]
    [SerializeField]
    private string microphoneDeviceName = "";

    [SerializeField]
    private bool interruptResponseOnNewRecording = false;

    [Tooltip("Length of Unity's looping microphone AudioClip. This is only a transport ring buffer; " +
             "active VAD speech is copied into a separate list and may be much longer.")]
    [SerializeField]
    private int microphoneRingBufferSeconds = 10;

    [Tooltip("Maximum Push-to-Talk recording length.")]
    [SerializeField]
    private int pushToTalkMaxSeconds = 120;

    [Header("Robust VAD: level thresholds")]
    [Tooltip("Absolute minimum level required to START speech. dBFS is negative; -40 is louder than -55.")]
    [SerializeField]
    private float minimumStartDbFs = -50f;

    [Tooltip("Absolute minimum level considered speech AFTER recording has started. " +
             "This should be lower (more permissive) than minimumStartDbFs.")]
    [SerializeField]
    private float minimumContinueDbFs = -56f;

    [Tooltip("Start threshold is also kept this many dB above the learned noise floor.")]
    [SerializeField]
    private float startMarginAboveNoiseDb = 12f;

    [Tooltip("Continue threshold is kept this many dB above the learned noise floor. " +
             "Use a smaller margin than startMarginAboveNoiseDb to preserve quiet words.")]
    [SerializeField]
    private float continueMarginAboveNoiseDb = 6f;

    [Tooltip("Stateful first-order high-pass cutoff used only for VAD measurement. " +
             "The recorded audio remains unfiltered. Set to 0 to disable.")]
    [SerializeField]
    private float vadHighPassHz = 80f;

    [Tooltip("Time constant for smoothing the measured RMS power. Larger values are steadier but slower.")]
    [SerializeField]
    private float levelSmoothingSeconds = 0.08f;

    [Header("Robust VAD: timing")]
    [Tooltip("Audio must remain above the start threshold for this long before recording starts.")]
    [SerializeField]
    private float speechStartHoldSeconds = 0.08f;

    [Tooltip("After speech starts, audio must remain continuously below the continue threshold " +
             "for this long before the utterance ends.")]
    [SerializeField]
    private float endSilenceSeconds = 1.6f;

    [Tooltip("Audio retained before speech is confirmed, preventing the first phoneme from being clipped.")]
    [SerializeField]
    private float preRollSeconds = 0.30f;

    [Tooltip("Safety limit for one VAD utterance. Unlike the original implementation, this can exceed 10 seconds.")]
    [SerializeField]
    private float maximumUtteranceSeconds = 120f;

    [Header("Adaptive noise floor")]
    [Tooltip("Initial assumed room-noise level before enough idle audio has been observed.")]
    [SerializeField]
    private float initialNoiseFloorDbFs = -60f;

    [Tooltip("How quickly the idle noise estimate follows environmental changes.")]
    [SerializeField]
    private float noiseAdaptationSeconds = 1.5f;

    [Tooltip("Do not update the noise floor when the current level is this close to the start threshold; " +
             "this reduces the chance of learning quiet speech as noise.")]
    [SerializeField]
    private float noiseUpdateGuardDb = 3f;

    [Header("FFT / visualization")]
    public float[] frequencyData { get; private set; }
    public int fftSampleSize = 1024;

    [Header("Runtime VAD diagnostics")]
    [SerializeField] private float currentInputDbFs = -100f;
    [SerializeField] private float currentNoiseFloorDbFs = -60f;
    [SerializeField] private float currentStartThresholdDbFs = -48f;
    [SerializeField] private float currentContinueThresholdDbFs = -54f;
    [SerializeField] private float currentContinuousSilenceSeconds = 0f;
    [SerializeField] private bool isVADRecording = false;

    public float CurrentInputDbFs => currentInputDbFs;
    public float CurrentNoiseFloorDbFs => currentNoiseFloorDbFs;
    public float CurrentStartThresholdDbFs => currentStartThresholdDbFs;
    public float CurrentContinueThresholdDbFs => currentContinueThresholdDbFs;
    public bool IsVADRecording => isVADRecording;

    public static event Action<string> OnAudioRecorded;
    public static event Action OnVADRecordingStarted;
    public static event Action OnVADRecordingEnded;

    private const float MicrophoneRetrySeconds = 1f;

    private AudioPlayer audioPlayer;
    private AudioClip microphoneClip;
    private string microphoneDevice;
    private bool missingDeviceNameLogged;
    private int lastMicrophoneFramePosition;

    // Only a short idle ring is retained for pre-roll.
    private readonly Queue<float> preRollBuffer = new Queue<float>();

    // Active speech is kept independently, so it is not truncated by the 10-second microphone clip.
    private readonly List<float> activeUtteranceBuffer = new List<float>();

    private float speechCandidateSeconds;
    private float activeUtteranceSeconds;

    // Smoothed RMS power.
    private bool smoothedPowerInitialized;
    private float smoothedPower;

    // Stateful one-pole high-pass filter state, used only while measuring VAD level.
    private bool highPassInitialized;
    private float highPassPreviousInput;
    private float highPassPreviousOutput;

    private void Start()
    {
        audioPlayer = GetComponent<AudioPlayer>();
        ResetVadState(resetNoiseFloor: true);
        RequestMicrophonePermission();
        StartCoroutine(StartMicrophoneWhenAvailable());
    }

    /// <summary>
    /// Selects the microphone and, in VAD mode, starts it. If there is no device yet, for example
    /// because the user has not answered the permission request, it tries again every second.
    /// </summary>
    private IEnumerator StartMicrophoneWhenAvailable()
    {
        bool waitingLogged = false;

        while (true)
        {
            if (TrySelectMicrophone())
            {
                if (listeningMode != ListeningMode.VAD ||
                    Microphone.IsRecording(microphoneDevice))
                {
                    yield break;
                }

                StartMicrophone();

                if (microphoneClip != null)
                {
                    yield break;
                }
            }

            if (!waitingLogged)
            {
                Debug.LogWarning(
                    "[AudioRecorder] No microphone is available yet. " +
                    "Waiting for a device or for the microphone permission.");
                waitingLogged = true;
            }

            yield return new WaitForSecondsRealtime(MicrophoneRetrySeconds);
        }
    }

    /// <summary>
    /// Sets microphoneDevice. Returns false while there is no permission or no device.
    /// </summary>
    private bool TrySelectMicrophone()
    {
        if (!HasMicrophonePermission())
        {
            return false;
        }

        string[] devices = Microphone.devices;
        if (devices.Length == 0)
        {
            return false;
        }

        microphoneDevice = devices[0];

        if (string.IsNullOrEmpty(microphoneDeviceName))
        {
            return true;
        }

        foreach (string device in devices)
        {
            if (string.Equals(device, microphoneDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                microphoneDevice = device;
                return true;
            }
        }

        if (!missingDeviceNameLogged)
        {
            Debug.LogWarning(
                $"[AudioRecorder] Microphone '{microphoneDeviceName}' not found. " +
                $"Using '{microphoneDevice}'.");
            missingDeviceNameLogged = true;
        }

        return true;
    }

    private static bool HasMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
        return true;
#endif
    }

    private static void RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!HasMicrophonePermission())
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }
#endif
    }

    private void WarnIfSampleRateDiffers()
    {
        if (microphoneClip != null && microphoneClip.frequency != sampleRate)
        {
            Debug.LogWarning(
                $"[AudioRecorder] The microphone records at {microphoneClip.frequency} Hz " +
                $"instead of {sampleRate} Hz. The recorded speech will be sent at the wrong speed.");
        }
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(microphoneDevice) ||
            !Microphone.IsRecording(microphoneDevice))
        {
            frequencyData = null;
            return;
        }

        if (!TryReadNewMicrophoneSamples(out float[] monoSamples))
        {
            return;
        }

        UpdateFrequencyVisualization(monoSamples);

        if (listeningMode == ListeningMode.VAD)
        {
            ProcessVadChunk(monoSamples);
        }
    }

    private void OnDisable()
    {
        if (!string.IsNullOrEmpty(microphoneDevice) &&
            Microphone.IsRecording(microphoneDevice))
        {
            Microphone.End(microphoneDevice);
        }
    }

    /// <summary>
    /// Starts Push-to-Talk recording.
    /// </summary>
    public void StartRecording()
    {
        if (!TrySelectMicrophone())
        {
            Debug.LogError("[AudioRecorder] No microphone is available.");
            return;
        }

        if (interruptResponseOnNewRecording && audioPlayer != null)
        {
            audioPlayer.CancelAudioPlayback();
        }

        if (audioPlayer != null)
        {
            audioPlayer.ResetCancelPending();
        }

        if (Microphone.IsRecording(microphoneDevice))
        {
            Microphone.End(microphoneDevice);
        }

        microphoneClip = Microphone.Start(
            microphoneDevice,
            false,
            Mathf.Max(1, pushToTalkMaxSeconds),
            sampleRate);

        WarnIfSampleRateDiffers();
        lastMicrophoneFramePosition = 0;
    }

    /// <summary>
    /// Stops Push-to-Talk recording and emits mono PCM16 base64.
    /// </summary>
    public void StopRecording()
    {
        if (microphoneClip == null || string.IsNullOrEmpty(microphoneDevice))
        {
            return;
        }

        int framePosition = Microphone.GetPosition(microphoneDevice);
        bool wasRecording = Microphone.IsRecording(microphoneDevice);

        if (wasRecording)
        {
            Microphone.End(microphoneDevice);
        }

        if (framePosition > 0)
        {
            float[] monoAudio = ReadClipRangeAsMono(microphoneClip, 0, framePosition);
            if (monoAudio.Length > 0)
            {
                string base64AudioData =
                    AudioProcessingUtils.ConvertFloatToPCM16AndBase64(monoAudio);
                OnAudioRecorded?.Invoke(base64AudioData);
            }
        }

        frequencyData = null;
        microphoneClip = null;
    }

    /// <summary>
    /// Starts the microphone in loop mode for VAD.
    /// </summary>
    public void StartMicrophone()
    {
        if (!TrySelectMicrophone())
        {
            Debug.LogWarning("[AudioRecorder] No microphone is available.");
            return;
        }

        if (Microphone.IsRecording(microphoneDevice))
        {
            Microphone.End(microphoneDevice);
        }

        microphoneClip = Microphone.Start(
            microphoneDevice,
            true,
            Mathf.Max(1, microphoneRingBufferSeconds),
            sampleRate);

        WarnIfSampleRateDiffers();
        lastMicrophoneFramePosition = 0;
        ResetVadState(resetNoiseFloor: true);
    }

    public void StopMicrophone()
    {
        if (!string.IsNullOrEmpty(microphoneDevice) &&
            Microphone.IsRecording(microphoneDevice))
        {
            Microphone.End(microphoneDevice);
        }

        if (isVADRecording)
        {
            AbortVADRecording();
        }

        frequencyData = null;
        microphoneClip = null;
    }

    /// <summary>
    /// Reads only the microphone frames that arrived since the previous Update,
    /// handling wrap-around of Unity's looping AudioClip and downmixing to mono.
    /// </summary>
    private bool TryReadNewMicrophoneSamples(out float[] monoSamples)
    {
        monoSamples = null;

        if (microphoneClip == null)
        {
            return false;
        }

        int currentFramePosition = Microphone.GetPosition(microphoneDevice);
        if (currentFramePosition < 0)
        {
            return false;
        }

        int newFrameCount = currentFramePosition - lastMicrophoneFramePosition;
        if (newFrameCount < 0)
        {
            newFrameCount += microphoneClip.samples;
        }

        if (newFrameCount <= 0)
        {
            return false;
        }

        int channels = Mathf.Max(1, microphoneClip.channels);
        float[] interleaved = new float[newFrameCount * channels];

        int framesUntilClipEnd = microphoneClip.samples - lastMicrophoneFramePosition;

        if (newFrameCount <= framesUntilClipEnd)
        {
            microphoneClip.GetData(interleaved, lastMicrophoneFramePosition);
        }
        else
        {
            int firstPartFrames = framesUntilClipEnd;
            int secondPartFrames = newFrameCount - firstPartFrames;

            float[] firstPart = new float[firstPartFrames * channels];
            float[] secondPart = new float[secondPartFrames * channels];

            microphoneClip.GetData(firstPart, lastMicrophoneFramePosition);
            microphoneClip.GetData(secondPart, 0);

            Array.Copy(firstPart, 0, interleaved, 0, firstPart.Length);
            Array.Copy(secondPart, 0, interleaved, firstPart.Length, secondPart.Length);
        }

        lastMicrophoneFramePosition = currentFramePosition;
        monoSamples = DownmixToMono(interleaved, channels);
        return monoSamples.Length > 0;
    }

    private static float[] DownmixToMono(float[] interleaved, int channels)
    {
        if (channels <= 1)
        {
            return interleaved;
        }

        int frames = interleaved.Length / channels;
        float[] mono = new float[frames];

        for (int frame = 0; frame < frames; frame++)
        {
            double sum = 0.0;
            int baseIndex = frame * channels;

            for (int channel = 0; channel < channels; channel++)
            {
                sum += interleaved[baseIndex + channel];
            }

            mono[frame] = (float)(sum / channels);
        }

        return mono;
    }

    private static float[] ReadClipRangeAsMono(
        AudioClip clip,
        int startFrame,
        int frameCount)
    {
        if (clip == null || frameCount <= 0)
        {
            return Array.Empty<float>();
        }

        int channels = Mathf.Max(1, clip.channels);
        float[] interleaved = new float[frameCount * channels];
        clip.GetData(interleaved, startFrame);
        return DownmixToMono(interleaved, channels);
    }

    private void UpdateFrequencyVisualization(float[] samples)
    {
        int fftSize = Mathf.Max(2, fftSampleSize);
        float[] fftSamples = new float[fftSize];

        int copyLength = Mathf.Min(samples.Length, fftSize);
        int sourceStart = samples.Length - copyLength;
        Array.Copy(samples, sourceStart, fftSamples, 0, copyLength);

        frequencyData = new float[fftSize];
        AudioProcessingUtils.FFT(fftSamples, frequencyData);
    }

    private void ProcessVadChunk(float[] rawSamples)
    {
        if (rawSamples == null || rawSamples.Length == 0)
        {
            return;
        }

        // When interruption is disabled, do not learn the Agent's playback as room noise
        // and do not accidentally submit half of an utterance captured during playback.
        if (!interruptResponseOnNewRecording &&
            audioPlayer != null &&
            audioPlayer.IsAudioPlaying())
        {
            if (isVADRecording)
            {
                AbortVADRecording();
            }

            preRollBuffer.Clear();
            speechCandidateSeconds = 0f;
            currentContinuousSilenceSeconds = 0f;
            return;
        }

        float chunkSeconds = rawSamples.Length / (float)Mathf.Max(1, sampleRate);
        currentInputDbFs = MeasureSmoothedVadDbFs(rawSamples, chunkSeconds);
        RefreshThresholds();

        if (!isVADRecording)
        {
            AddToPreRoll(rawSamples);

            bool aboveStartThreshold =
                currentInputDbFs >= currentStartThresholdDbFs;

            // Learn the room only when the chunk is safely below the speech-start boundary.
            if (!aboveStartThreshold &&
                currentInputDbFs <= currentStartThresholdDbFs - noiseUpdateGuardDb)
            {
                UpdateNoiseFloor(currentInputDbFs, chunkSeconds);
                RefreshThresholds();
                aboveStartThreshold =
                    currentInputDbFs >= currentStartThresholdDbFs;
            }

            if (aboveStartThreshold)
            {
                speechCandidateSeconds += chunkSeconds;
            }
            else
            {
                speechCandidateSeconds = 0f;
            }

            if (speechCandidateSeconds >= speechStartHoldSeconds)
            {
                StartVADRecording();
            }

            return;
        }

        // Once active, always retain every raw sample, including quiet words and pauses.
        activeUtteranceBuffer.AddRange(rawSamples);
        activeUtteranceSeconds += chunkSeconds;

        bool aboveContinueThreshold =
            currentInputDbFs >= currentContinueThresholdDbFs;

        if (aboveContinueThreshold)
        {
            currentContinuousSilenceSeconds = 0f;
        }
        else
        {
            // Endpoint rule:
            // level below threshold -> begin/continue timing;
            // only continuous low level exceeding endSilenceSeconds ends the utterance.
            currentContinuousSilenceSeconds += chunkSeconds;
        }

        if (activeUtteranceSeconds >= maximumUtteranceSeconds)
        {
            Debug.LogWarning(
                $"[AudioRecorder] Maximum utterance duration " +
                $"({maximumUtteranceSeconds:F1}s) reached.");
            StopVADRecording();
        }
        else if (currentContinuousSilenceSeconds >= endSilenceSeconds)
        {
            StopVADRecording();
        }
    }

    private void AddToPreRoll(float[] samples)
    {
        int capacity = Mathf.Max(0, Mathf.RoundToInt(preRollSeconds * sampleRate));
        if (capacity == 0)
        {
            preRollBuffer.Clear();
            return;
        }

        for (int i = 0; i < samples.Length; i++)
        {
            preRollBuffer.Enqueue(samples[i]);
        }

        while (preRollBuffer.Count > capacity)
        {
            preRollBuffer.Dequeue();
        }
    }

    private void StartVADRecording()
    {
        if (isVADRecording)
        {
            return;
        }

        if (interruptResponseOnNewRecording && audioPlayer != null)
        {
            audioPlayer.CancelAudioPlayback();
        }

        if (audioPlayer != null)
        {
            audioPlayer.ResetCancelPending();
        }

        activeUtteranceBuffer.Clear();

        // preRollBuffer already contains the chunks that triggered speech,
        // so the first word is not lost.
        if (preRollBuffer.Count > 0)
        {
            activeUtteranceBuffer.AddRange(preRollBuffer);
        }

        preRollBuffer.Clear();

        isVADRecording = true;
        speechCandidateSeconds = 0f;
        currentContinuousSilenceSeconds = 0f;
        activeUtteranceSeconds =
            activeUtteranceBuffer.Count / (float)Mathf.Max(1, sampleRate);

        OnVADRecordingStarted?.Invoke();
    }

    private void StopVADRecording()
    {
        if (!isVADRecording)
        {
            return;
        }

        if (activeUtteranceBuffer.Count > 0)
        {
            float[] audioData = activeUtteranceBuffer.ToArray();
            string base64AudioData =
                AudioProcessingUtils.ConvertFloatToPCM16AndBase64(audioData);
            OnAudioRecorded?.Invoke(base64AudioData);
        }

        isVADRecording = false;
        activeUtteranceBuffer.Clear();
        preRollBuffer.Clear();
        speechCandidateSeconds = 0f;
        currentContinuousSilenceSeconds = 0f;
        activeUtteranceSeconds = 0f;

        OnVADRecordingEnded?.Invoke();
    }

    private void AbortVADRecording()
    {
        bool wasRecording = isVADRecording;

        isVADRecording = false;
        activeUtteranceBuffer.Clear();
        preRollBuffer.Clear();
        speechCandidateSeconds = 0f;
        currentContinuousSilenceSeconds = 0f;
        activeUtteranceSeconds = 0f;

        if (wasRecording)
        {
            OnVADRecordingEnded?.Invoke();
        }

        Debug.Log(
            "[AudioRecorder] Agent is speaking. Human input ignored and " +
            "active VAD recording aborted.");
    }

    /// <summary>
    /// Calculates filtered RMS power for the new chunk and then smooths power over time.
    /// dBFS = 10 * log10(mean-square), equivalent to 20 * log10(RMS).
    /// </summary>
    private float MeasureSmoothedVadDbFs(
        float[] samples,
        float chunkSeconds)
    {
        double sumSquares = 0.0;

        if (vadHighPassHz > 0f)
        {
            float dt = 1f / Mathf.Max(1, sampleRate);
            float rc = 1f / (2f * Mathf.PI * vadHighPassHz);

            // Standard first-order RC high-pass coefficient.
            float alpha = rc / (rc + dt);

            for (int i = 0; i < samples.Length; i++)
            {
                float input = samples[i];

                if (!highPassInitialized)
                {
                    highPassPreviousInput = input;
                    highPassPreviousOutput = 0f;
                    highPassInitialized = true;
                }

                float output = alpha *
                    (highPassPreviousOutput +
                     input -
                     highPassPreviousInput);

                highPassPreviousInput = input;
                highPassPreviousOutput = output;
                sumSquares += output * output;
            }
        }
        else
        {
            for (int i = 0; i < samples.Length; i++)
            {
                double sample = samples[i];
                sumSquares += sample * sample;
            }
        }

        float chunkPower =
            (float)(sumSquares / Math.Max(1, samples.Length));

        if (!smoothedPowerInitialized)
        {
            smoothedPower = chunkPower;
            smoothedPowerInitialized = true;
        }
        else
        {
            float timeConstant = Mathf.Max(0.001f, levelSmoothingSeconds);
            float blend =
                1f - Mathf.Exp(-chunkSeconds / timeConstant);
            smoothedPower =
                Mathf.Lerp(smoothedPower, chunkPower, blend);
        }

        return 10f * Mathf.Log10(Mathf.Max(smoothedPower, 1e-10f));
    }

    private void UpdateNoiseFloor(float observedDbFs, float chunkSeconds)
    {
        float timeConstant = Mathf.Max(0.05f, noiseAdaptationSeconds);
        float blend = 1f - Mathf.Exp(-chunkSeconds / timeConstant);

        currentNoiseFloorDbFs = Mathf.Lerp(
            currentNoiseFloorDbFs,
            Mathf.Clamp(observedDbFs, -100f, -5f),
            blend);
    }

    private void RefreshThresholds()
    {
        currentStartThresholdDbFs = Mathf.Max(
            minimumStartDbFs,
            currentNoiseFloorDbFs + startMarginAboveNoiseDb);

        currentContinueThresholdDbFs = Mathf.Max(
            minimumContinueDbFs,
            currentNoiseFloorDbFs + continueMarginAboveNoiseDb);

        // Continue threshold must remain easier to satisfy than start threshold.
        if (currentContinueThresholdDbFs >= currentStartThresholdDbFs)
        {
            currentContinueThresholdDbFs =
                currentStartThresholdDbFs - 3f;
        }
    }

    /// <summary>
    /// Call this after the microphone/environment changes substantially.
    /// </summary>
    public void ResetNoiseCalibration()
    {
        currentNoiseFloorDbFs = initialNoiseFloorDbFs;
        smoothedPowerInitialized = false;
        highPassInitialized = false;
        RefreshThresholds();
    }

    private void ResetVadState(bool resetNoiseFloor)
    {
        isVADRecording = false;
        speechCandidateSeconds = 0f;
        currentContinuousSilenceSeconds = 0f;
        activeUtteranceSeconds = 0f;

        preRollBuffer.Clear();
        activeUtteranceBuffer.Clear();

        smoothedPowerInitialized = false;
        smoothedPower = 0f;
        highPassInitialized = false;
        highPassPreviousInput = 0f;
        highPassPreviousOutput = 0f;

        if (resetNoiseFloor)
        {
            currentNoiseFloorDbFs = initialNoiseFloorDbFs;
        }

        currentInputDbFs = -100f;
        RefreshThresholds();
    }
}
