using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.Composites;

public class targetAppearance : MonoBehaviour
{
    /// <summary>
    /// Handles the co-routine to precisely time changes to target appearance during walk trajectory.
    /// 
    /// Main method called from runExperiment.


    public bool processNoResponse;
    private float waitTime;
    private float trialDuration;
    private float[]  trialOnsets;
    
    runExperiment runExperiment;

    MakeAuditoryStimulus makeAuditoryStimulus;
    experimentParameters expParams;
    CalculateStimTimes calcStimTimes;

    [SerializeField]
    GameObject scriptHolder;

    private void Start()
    {
        runExperiment = scriptHolder.GetComponent<runExperiment>();
        expParams = scriptHolder.GetComponent<experimentParameters>();
        calcStimTimes = scriptHolder.GetComponent<CalculateStimTimes>();

        // methods:
        makeAuditoryStimulus = GetComponent<MakeAuditoryStimulus>();
        processNoResponse = false;
    }

    public void startSequence()
    {
        // some params for this trial:
        //// note that trial duration changes with walk speed.
        trialDuration = runExperiment.thisTrialDuration;

        // note that onsets are now precalculated:
        trialOnsets = calcStimTimes.allOnsets[runExperiment.trialCount];

        StartCoroutine("trialProgress");
    }

    /// <summary>
    /// Coroutine controlling target appearance with precise timing.
    /// </summary>
    /// 

    IEnumerator trialProgress()
    {
        while (runExperiment.trialinProgress) // this creates a never-ending loop for the co-routine.
        {
            // trial progress:
            // The timing of trial elements is determined on the fly.
            // Boundaries set in trialParameters.
            runExperiment.detectIndex = 0; // listener, to assign correct responses per stimulus

            yield return new WaitForSecondsRealtime(expParams.preTrialsec);

            // Present comparison tones at precalculated onsets
            for (int itargindx = 0; itargindx < trialOnsets.Length; itargindx++)
            {
                // first target has no ISI adjustment
                if (itargindx == 0)
                {
                    waitTime = trialOnsets[0];
                }
                else
                {
                    // adjust for time elapsed.
                    waitTime = trialOnsets[itargindx] - runExperiment.trialTime;
                }

                // wait before presenting stimulus:
                yield return new WaitForSecondsRealtime(waitTime);

                // To increase difficulty and remove expectancy, only present on 95% of slots.
                if (Random.value <= .95f)
                {
                    // Play comparison tone
                    makeAuditoryStimulus.PlayComparison();
                    runExperiment.targState = 1; // stimulus is active
                    runExperiment.detectIndex = itargindx + 1; // responses in this window are valid
                    runExperiment.hasResponded = false;

                    // Freeze all stimulus properties into an immutable event at this
                    // exact moment. Once created, this snapshot cannot be changed —
                    // so even when PrepareNextComparison() later overwrites auditoryP
                    // for the next stimulus, the response handler and data recorder
                    // will still see the correct values for *this* presentation.
                    runExperiment.currentEvent = new experimentParameters.StimulusEvent(
                        standardDurationMs:   makeAuditoryStimulus.auditoryP.standardDurationMs,
                        comparisonDurationMs: makeAuditoryStimulus.auditoryP.comparisonDurationMs,
                        comparisonType:       makeAuditoryStimulus.auditoryP.comparisonIsLonger
                                              ? experimentParameters.ComparisonType.Longer
                                              : experimentParameters.ComparisonType.Shorter,
                        toneFrequencyHz:      makeAuditoryStimulus.auditoryP.toneFrequencyHz,
                        onsetTime:            runExperiment.trialTime
                    );

                    // The comparison tone duration is very short (50-200ms),
                    // so we just wait for the response window after onset.
                    float comparisonDurationSec = makeAuditoryStimulus.auditoryP.comparisonDurationMs / 1000f;
                    yield return new WaitForSecondsRealtime(comparisonDurationSec);

                    runExperiment.targState = 0; // stimulus has ended
                    yield return new WaitForSecondsRealtime(expParams.responseWindow);

                    // if no click in time, count as a miss.
                    if (!runExperiment.hasResponded)
                    {
                        processNoResponse = true; // handled in runExperiment.
                    }
                    runExperiment.detectIndex = 0; // clicks from now are too slow
                }
                else // skip this stimulus slot (catch trial)
                {
                    Debug.Log("Catch trial — no comparison played");
                    yield return new WaitForSecondsRealtime(expParams.targDurationsec);
                    yield return new WaitForSecondsRealtime(expParams.responseWindow);
                    processNoResponse = false; // don't count as a miss
                }
            } // for each stimulus

            // after for loop, wait for trial end:
            while (runExperiment.trialTime < runExperiment.thisTrialDuration)
            {
                yield return null; // wait until next frame.
            }

            break; // Trial Complete, exit the while loop.

        } // while trial in progress

    } // IEnumerator


}
