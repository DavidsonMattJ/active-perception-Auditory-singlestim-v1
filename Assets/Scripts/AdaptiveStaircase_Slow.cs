// DISABLED: Replaced by AdaptiveStaircase.cs (single script, multiple conditions).
// Remove the AdaptiveStaircaseSlow component from the GameObject in the Inspector.
#if false
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class AdaptiveStaircaseSlow : MonoBehaviour
{
    /// <summary>
    /// This script initialises and handles the update of staircase values based on participant reponses.
    /// mainly using  ProcessResponse(bool correct); returns new float intensity
    /// </summary>

    [System.Serializable]
    public enum StaircaseType
    {
        SimpleUpDown,           // 1-up, 1-down (50% threshold)
        TwoUpOneDown,          // 2-up, 1-down (70.7% threshold)
        ThreeUpOneDown,        // 3-up, 1-down (79.4% threshold)
        OneUpTwoDown,          // 1-up, 2-down (70.7% threshold)
        OneUpThreeDown         // 1-up, 3-down (79.4% threshold)
    }
    
    //StairCase is configured to work with different durations (sec)

    [Header("Staircase Configuration")]
    public StaircaseType staircaseType = StaircaseType.TwoUpOneDown;
    public float initialDuration = 0.6f; // 600 ms
    public float minDuration = 0.01f;
    public float maxDuration = 1.0f;
    
    [Header("Step Sizes")] // initial step sizes we adjust by.
    public float initialStepSize = 0.05f; //50 ms
    public float finalStepSize = 0.011f; // single frame
    public int trialsToReduceStep = 4; // After how many reversals to reduce step size

    [Header("Stopping Criteria (Read Only)")]
    [SerializeField, ReadOnly] private int maxTrials; // now set below in Start() to avoid override.
    [SerializeField, ReadOnly] private int minReversals; 
    [SerializeField, ReadOnly] private int trialsAfterMinReversals;  // Continue for this many trials after min reversals
    
    // Current state
    [Header("Current State Staircase (Read Only)")]
    [SerializeField, ReadOnly] private float currentIntensity;
    [SerializeField, ReadOnly] private float currentStepSize;
    [SerializeField, ReadOnly] private int trialCount = 0;
    [SerializeField, ReadOnly] private int reversalCount = 0;
    [SerializeField, ReadOnly] private int consecutiveCorrect = 0;
    [SerializeField, ReadOnly] private int consecutiveIncorrect = 0;
    [SerializeField, ReadOnly] private bool isComplete = false;
    
    // History tracking
    private List<float> intensityHistory = new List<float>();
    private List<bool> responseHistory = new List<bool>();
    private List<float> reversalIntensities = new List<float>();
    private List<int> reversalTrials = new List<int>();
    private bool lastDirectionWasUp = false;
    private bool hasHadFirstReversal = false;
    
    // Properties for external access
    public float CurrentIntensity => currentIntensity;
    public bool IsComplete => isComplete;
    public int TrialCount => trialCount;
    public int ReversalCount => reversalCount;
    public float EstimatedThreshold => CalculateThreshold();
    
    void Start()
    {
        InitializeStaircase();
    }
    
    public void InitializeStaircase()
    {
        currentIntensity = initialDuration;
        currentStepSize = initialStepSize;
        trialCount = 0;
        reversalCount = 0;
        consecutiveCorrect = 0;
        consecutiveIncorrect = 0;
        isComplete = false;
        hasHadFirstReversal = false;


        // stopping criteria (adjust as needed), e.g.. 60, 4, 20.
        maxTrials=9999; // now set below in Start() to avoid override.
        minReversals = 9999;  
        trialsAfterMinReversals= 9999;

        intensityHistory.Clear();
        responseHistory.Clear();
        reversalIntensities.Clear();
        reversalTrials.Clear();
        
        Debug.Log($"Staircase initialized: Type={staircaseType}, Initial={initialDuration:F3}");
    }
    
    /// <summary>
    /// Main method to call after each trial response
    /// </summary>
    /// <param name="correct">True if response was correct, false if incorrect</param>
    /// <returns>The new intensity value for the next trial</returns>
    public float ProcessResponse(bool correct)
    {
        if (isComplete)
        {
            Debug.LogWarning("Staircase is already complete!");
            return currentIntensity;
        }
        
        // Record the response
        trialCount++;
        responseHistory.Add(correct);
        intensityHistory.Add(currentIntensity);
        
        Debug.Log($"Trial {trialCount}: Response={correct}, Intensity={currentIntensity:F3}");
        
        // Update consecutive counters
        if (correct)
        {
            consecutiveCorrect++;
            consecutiveIncorrect = 0;
        }
        else
        {
            consecutiveIncorrect++;
            consecutiveCorrect = 0;
        }
        
        // Determine if we should change intensity
        bool shouldGoUp = ShouldIncreaseIntensity();
        bool shouldGoDown = ShouldDecreaseIntensity();
        
        // Check for reversal before updating intensity
        bool isReversal = CheckForReversal(shouldGoUp, shouldGoDown);
        
        // Update intensity
        if (shouldGoUp)
        {
            IncreaseIntensity();
            lastDirectionWasUp = true;
            ResetConsecutiveCounters();
        }
        else if (shouldGoDown)
        {
            DecreaseIntensity();
            lastDirectionWasUp = false;
            ResetConsecutiveCounters();
        }
        
        // Record reversal if it occurred
        if (isReversal)
        {
            reversalCount++;
            reversalIntensities.Add(currentIntensity);
            reversalTrials.Add(trialCount);
            hasHadFirstReversal = true;
            
            Debug.Log($"Reversal #{reversalCount} at trial {trialCount}, intensity {currentIntensity:F3}");
            
            // Reduce step size after certain number of reversals
            if (reversalCount > 0 && reversalCount % trialsToReduceStep == 0)
            {
                ReduceStepSize();
            }
        }
        
        // Check completion criteria
        CheckCompletionCriteria();
        
        // Ensure intensity stays within bounds
        currentIntensity = Mathf.Clamp(currentIntensity, minDuration, maxDuration);
        
        Debug.Log($"New intensity: {currentIntensity:F3}, Reversals: {reversalCount}");



        // set immediately for the next Gabor
        

        return currentIntensity; // return for storing in trialData
    }
    
    private bool ShouldIncreaseIntensity()
    {
        switch (staircaseType)
        {
            case StaircaseType.SimpleUpDown:
                return consecutiveIncorrect >= 1;
                
            case StaircaseType.TwoUpOneDown:
                return consecutiveIncorrect >= 1;
                
            case StaircaseType.ThreeUpOneDown:
                return consecutiveIncorrect >= 1;
                
            case StaircaseType.OneUpTwoDown:
                return consecutiveIncorrect >= 2;
                
            case StaircaseType.OneUpThreeDown:
                return consecutiveIncorrect >= 3;
                
            default:
                return false;
        }
    }
    
    private bool ShouldDecreaseIntensity()
    {
        switch (staircaseType)
        {
            case StaircaseType.SimpleUpDown:
                return consecutiveCorrect >= 1;
                
            case StaircaseType.TwoUpOneDown:
                return consecutiveCorrect >= 2;
                
            case StaircaseType.ThreeUpOneDown:
                return consecutiveCorrect >= 3;
                
            case StaircaseType.OneUpTwoDown:
                return consecutiveCorrect >= 1;
                
            case StaircaseType.OneUpThreeDown:
                return consecutiveCorrect >= 1;
                
            default:
                return false;
        }
    }
    
    private bool CheckForReversal(bool shouldGoUp, bool shouldGoDown)
    {
        if (!hasHadFirstReversal)
        {
            // First direction change
            if (shouldGoUp || shouldGoDown)
            {
                return true;
            }
        }
        else
        {
            // Subsequent reversals
            if ((shouldGoUp && !lastDirectionWasUp) || (shouldGoDown && lastDirectionWasUp))
            {
                return true;
            }
        }
        return false;
    }
    
    private void IncreaseIntensity()
    {
        currentIntensity += currentStepSize;
        Debug.Log($"Increasing intensity by {currentStepSize:F3} to {currentIntensity:F3}");
    }
    
    private void DecreaseIntensity()
    {
        currentIntensity -= currentStepSize;
        Debug.Log($"Decreasing intensity by {currentStepSize:F3} to {currentIntensity:F3}");
    }
    
    private void ResetConsecutiveCounters()
    {
        consecutiveCorrect = 0;
        consecutiveIncorrect = 0;
    }
    
    private void ReduceStepSize()
    {
        float oldStepSize = currentStepSize;
        currentStepSize = Mathf.Max(finalStepSize, currentStepSize * 0.5f);
        Debug.Log($"Reduced step size from {oldStepSize:F3} to {currentStepSize:F3}");
    }
    
    private void CheckCompletionCriteria()
    {
        bool maxTrialsReached = trialCount >= maxTrials;
        bool minReversalsReached = reversalCount >= minReversals;
        bool additionalTrialsCompleted = minReversalsReached && 
            (trialCount - reversalTrials[minReversals - 1]) >= trialsAfterMinReversals;
        
        if (maxTrialsReached || additionalTrialsCompleted)
        {
            isComplete = true;
            Debug.Log($"Staircase complete! Trials: {trialCount}, Reversals: {reversalCount}, Threshold: {EstimatedThreshold:F3}");
        }
    }
    
    private float CalculateThreshold()
    {
        if (reversalIntensities.Count < 4)
        {
            return currentIntensity; // Not enough data
        }
        
        // Use last 6 reversals or all if fewer than 6
        int reversalsToUse = Mathf.Min(6, reversalIntensities.Count);
        int startIndex = reversalIntensities.Count - reversalsToUse;
        
        float sum = 0f;
        for (int i = startIndex; i < reversalIntensities.Count; i++)
        {
            sum += reversalIntensities[i];
        }
        
        return sum / reversalsToUse;
    }
    
    /// <summary>
    /// Get summary statistics of the staircase
    /// </summary>
    public void PrintSummary()
    {
        Debug.Log("=== STAIRCASE SUMMARY ===");
        Debug.Log($"Type: {staircaseType}");
        Debug.Log($"Trials completed: {trialCount}");
        Debug.Log($"Reversals: {reversalCount}");
        Debug.Log($"Final intensity: {currentIntensity:F3}");
        Debug.Log($"Estimated threshold: {EstimatedThreshold:F3}");
        
        if (responseHistory.Count > 0)
        {
            float accuracy = responseHistory.Count(r => r) / (float)responseHistory.Count * 100f;
            Debug.Log($"Overall accuracy: {accuracy:F1}%");
        }
        
        Debug.Log("Reversal intensities: " + string.Join(", ", reversalIntensities.Select(r => r.ToString("F3"))));
    }
    
    /// <summary>
    /// Reset the staircase for a new session
    /// </summary>
    public void Reset()
    {
        InitializeStaircase();
    }
    
    /// <summary>
    /// Get the intensity history for plotting or analysis
    /// </summary>
    public List<float> GetIntensityHistory()
    {
        return new List<float>(intensityHistory);
    }
    
    /// <summary>
    /// Get the response history
    /// </summary>
    public List<bool> GetResponseHistory()
    {
        return new List<bool>(responseHistory);
    }
}
#endif