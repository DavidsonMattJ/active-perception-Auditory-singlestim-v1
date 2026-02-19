using System.Collections;
using UnityEngine;

public class MakeAuditoryStimulus : MonoBehaviour
{
    /// <summary>
    /// Generates auditory stimuli for a duration discrimination task.
    ///
    /// Participants hear a fixed-duration "standard" tone (75ms, 1000 Hz pure sine)
    /// and judge whether comparison tones are shorter or longer.
    ///
    /// Replaces makeNavonStimulus in the stimulus pipeline:
    ///   - PlayStandardSequence()  → called at trial start (~1s before walking)
    ///   - PlayComparison()        → called by targetAppearance at each stimulus onset
    ///   - PrepareNextComparison() → called after response (analogous to GenerateNavon)
    ///   - StopAllAudio()          → called to silence playback (analogous to hideNavon)
    /// </summary>

    // ──────────────────────────────────────────────────────────────────
    //  Inspector-configurable parameters
    // ──────────────────────────────────────────────────────────────────

    [Header("Tone Properties")]
    public float toneFrequencyHz = 1000f;
    [Range(0f, 1f)]
    public float toneAmplitude = 0.8f;

    [Header("Standard Tone")]
    public float standardDurationMs = 100f;
    public int standardRepetitions;
    public float standardGapMs;

    [Header("Comparison Tone")]
    public float minComparisonMs = 10f;
    public float maxComparisonMs = 1000f;

    [Header("Audio")]
    [Tooltip("Cosine ramp duration in ms to prevent click artifacts")]
    public float rampDurationMs = 5f;

    // ──────────────────────────────────────────────────────────────────
    //  Public parameter struct (analogous to NavonParams)
    // ──────────────────────────────────────────────────────────────────

    public struct AuditoryParams
    {
        public float standardDurationMs;    // Fixed standard (75 ms)
        public float comparisonDurationMs;  // Current comparison duration
        public float toneFrequencyHz;       // Tone frequency (1000 Hz)
        public float toneAmplitude;         // Amplitude [0,1]
        public bool comparisonIsLonger;     // Ground truth: is comparison > standard?
        public float deltaMs;          // calilbrated difference in duration +- (staircase compatibility)
    }

    public AuditoryParams auditoryP;

    // ──────────────────────────────────────────────────────────────────
    //  Private state
    // ──────────────────────────────────────────────────────────────────

    private AudioSource audioSource;
    private AudioClip standardClip;
    private AudioClip comparisonClip;
    private const int sampleRate = 44100;

    // Reference to experiment parameters (for accessing staircase values)
    experimentParameters experimentParameters;

    [SerializeField]
    GameObject scriptHolder;

    // ──────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────────────────────────

    void Start()
    {
        experimentParameters = scriptHolder.GetComponent<experimentParameters>();

        // Ensure we have an AudioSource
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // 2D audio (non-spatialized, same in both ears)

        // Pre-generate the standard clip
        float standardSec = standardDurationMs / 1000f;
        standardClip = GenerateToneClip(standardSec, toneFrequencyHz, toneAmplitude);

        // Initialize params struct
        auditoryP.standardDurationMs = standardDurationMs;
        auditoryP.toneFrequencyHz = toneFrequencyHz;
        auditoryP.toneAmplitude = toneAmplitude;
        auditoryP.comparisonDurationMs = standardDurationMs; // start equal to standard
        auditoryP.comparisonIsLonger = true;
        auditoryP.deltaMs = 0.75f *standardDurationMs; // start large for easy detection.
        
        // set (adjustable)
        standardRepetitions= 3;
        standardGapMs = 500 ;
        // Prepare first comparison
        PrepareNextComparison();

        Debug.Log($"MakeAuditoryStimulus initialized: standard={standardDurationMs}ms, " +
                  $"freq={toneFrequencyHz}Hz, amp={toneAmplitude}, " +
                  $"ramp={rampDurationMs}ms, reps={standardRepetitions}");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Public methods (stimulus pipeline interface)
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the standard tone 3 times with gaps.
    /// Call as a coroutine at trial start, before walking begins.
    /// Total duration: ~(standardDuration + gap) * repetitions
    /// e.g. (75ms + 200ms) * 3 = ~825ms
    /// </summary>
    public IEnumerator PlayStandardSequence()
    {
        float gapSec = standardGapMs / 1000f;
        float stdSec = standardDurationMs / 1000f;

        for (int i = 0; i < standardRepetitions; i++)
        {
            audioSource.PlayOneShot(standardClip);
            // Wait for tone to finish + gap before next repetition
            yield return new WaitForSecondsRealtime(stdSec + gapSec);
        }

        Debug.Log($"Standard sequence complete: {standardRepetitions}x {standardDurationMs}ms tones");
    }

    /// <summary>
    /// Returns the total duration of the standard sequence in seconds.
    /// Useful for timing: the walk should begin after this duration.
    /// </summary>
    public float GetStandardSequenceDuration()
    {
        float stdSec = standardDurationMs / 1000f;
        float gapSec = standardGapMs / 1000f;
        return (stdSec + gapSec) * standardRepetitions;
    }

    /// <summary>
    /// Plays the current comparison tone once.
    /// Called by targetAppearance at each stimulus onset during walking.
    /// </summary>
    public void PlayComparison()
    {
        if (comparisonClip != null)
        {
            audioSource.PlayOneShot(comparisonClip);
            Debug.Log($"Comparison played: {auditoryP.comparisonDurationMs:F1}ms " +
                      $"(standard={standardDurationMs}ms, " +
                      $"isLonger={auditoryP.comparisonIsLonger})");
        }
        else
        {
            Debug.LogWarning("MakeAuditoryStimulus: comparisonClip is null, cannot play.");
        }
    }

    /// <summary>
    /// Generates a new comparison tone with the current staircase-controlled duration.
    /// Randomly assigns whether this comparison is shorter or longer than standard.
    /// Analogous to GenerateNavon() — call after each response.
    /// </summary>
    public void PrepareNextComparison()
    {
        // The staircase controls targDuration as the *delta* in seconds
        // (the absolute difference between comparison and standard).
        // We use this delta to construct the actual comparison duration.
        
        float deltaMs = auditoryP.deltaMs;

        // Randomly assign shorter or longer (50/50)
        auditoryP.comparisonIsLonger = Random.Range(0f, 1f) < 0.5f;

        if (auditoryP.comparisonIsLonger)
        {
            auditoryP.comparisonDurationMs = standardDurationMs + deltaMs;
        }
        else
        {
            auditoryP.comparisonDurationMs = standardDurationMs - deltaMs;
        }

        // Clamp to safe range
        auditoryP.comparisonDurationMs = Mathf.Clamp(
            auditoryP.comparisonDurationMs,
            minComparisonMs,
            maxComparisonMs
        );

        // Generate the audio clip
        float comparisonSec = auditoryP.comparisonDurationMs / 1000f;
        comparisonClip = GenerateToneClip(comparisonSec, toneFrequencyHz, toneAmplitude);

        Debug.Log($"Next comparison prepared: {auditoryP.comparisonDurationMs:F1}ms " +
                  $"(delta={deltaMs:F1}ms, isLonger={auditoryP.comparisonIsLonger})");
    }

    /// <summary>
    /// Stops any currently playing audio.
    /// Analogous to hideNavon(). Mostly a no-op for short tones,
    /// but useful as a safety stop at trial boundaries.
    /// </summary>
    public void StopAllAudio()
    {
        audioSource.Stop();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Tone generation (private)
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a pure sine tone AudioClip with cosine onset/offset ramps
    /// to prevent audible click artifacts.
    /// </summary>
    /// <param name="durationSec">Tone duration in seconds</param>
    /// <param name="frequencyHz">Frequency in Hz</param>
    /// <param name="amplitude">Peak amplitude [0, 1]</param>
    /// <returns>A mono AudioClip containing the generated tone</returns>
    private AudioClip GenerateToneClip(float durationSec, float frequencyHz, float amplitude)
    {
        int sampleCount = Mathf.CeilToInt(durationSec * sampleRate);
        if (sampleCount < 1) sampleCount = 1;

        float[] samples = new float[sampleCount];

        // Cosine ramp length in samples (5ms default)
        float rampSec = rampDurationMs / 1000f;
        int rampSamples = Mathf.CeilToInt(rampSec * sampleRate);
        // Ensure ramp doesn't exceed half the total duration
        rampSamples = Mathf.Min(rampSamples, sampleCount / 2);

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float sample = amplitude * Mathf.Sin(2f * Mathf.PI * frequencyHz * t);

            // Cosine onset ramp (raised cosine from 0 → 1)
            if (i < rampSamples)
            {
                float rampFraction = (float)i / rampSamples;
                sample *= 0.5f * (1f - Mathf.Cos(Mathf.PI * rampFraction));
            }
            // Cosine offset ramp (raised cosine from 1 → 0)
            else if (i >= sampleCount - rampSamples)
            {
                float rampFraction = (float)(i - (sampleCount - rampSamples)) / rampSamples;
                sample *= 0.5f * (1f + Mathf.Cos(Mathf.PI * rampFraction));
            }

            samples[i] = sample;
        }

        AudioClip clip = AudioClip.Create(
            $"tone_{durationSec * 1000f:F0}ms",
            sampleCount,
            1,          // mono
            sampleRate,
            false       // not streaming
        );
        clip.SetData(samples, 0);
        return clip;
    }
}
