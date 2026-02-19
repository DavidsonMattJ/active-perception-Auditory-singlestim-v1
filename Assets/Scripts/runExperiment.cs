using UnityEngine;
using System;
// using UnityEditor;
// using UnityEditorInternal;
using UnityEngine.UIElements.Experimental;
using UnityEngine.XR.Interaction.Toolkit.Utilities.Tweenables.Primitives;
using Unity.VisualScripting;
using TMPro;
using JetBrains.Annotations;
using System.Collections;

public class runExperiment : MonoBehaviour
{
    // This is the launch script for the experiment, useful for toggling certain input states. 

    //Navon v1  -UTS 


    [Header("User Input")]
    public bool playinVR;
    public string participant;
    public bool skipWalkCalibration;


    [Header("Experiment State")]

    public string responseMapping = "L:shorter R:longer"; // show for experimenter (default)
    public int trialCount;
    public float trialTime;
    public float thisTrialDuration;
    public bool trialinProgress;
    [SerializeField] private int responseMap; // for assigning left/right to detect/reject [-1, 1];


    [HideInInspector]
    public int detectIndex, targState, blockType; // 

    
    [HideInInspector]
    public bool isStationary, collectTrialSummary, collectEventSummary, hasResponded;

    // Immutable snapshot of the current stimulus, created by targetAppearance
    // at the moment the stimulus is played. Because StimulusEvent is a readonly
    // struct, it cannot be mutated after creation — so even if PrepareNextComparison()
    // overwrites auditoryP for the next trial, this event remains intact for
    // response scoring and data recording.
    [HideInInspector]
    public experimentParameters.StimulusEvent currentEvent;
    private bool updateNextStimulus;
    

    [HideInInspector]
    public string[] responseforShorterLonger; // grabbed by showText.
    
    bool SetUpSession;

    //todo
    //public bool forceheightCalibration;
    //public bool forceEyecalibration;
    //public bool recordEEG;
    //public bool isEyetracked;


    CollectPlayerInput playerInput;
    experimentParameters expParams;
    controlWalkingGuide controlWalkingGuide;
    WalkSpeedCalibrator walkCalibrator;
    ShowText ShowText;
    FeedbackText FeedbackText;
    targetAppearance targetAppearance;
    RecordData RecordData;
    AdaptiveStaircase adaptiveStaircase;
    
    MakeAuditoryStimulus makeAuditoryStimulus;

    //use  serialize field to require drag-drop in inspector. less expensive than GameObject.Find() .
    [SerializeField] GameObject TextScreen;
    [SerializeField] GameObject TextFeedback;
    [SerializeField] GameObject StimulusScreen;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {


        adaptiveStaircase = GetComponent<AdaptiveStaircase>();
        playerInput = GetComponent<CollectPlayerInput>();
        expParams = GetComponent<experimentParameters>();
        controlWalkingGuide = GetComponent<controlWalkingGuide>();
        walkCalibrator = GetComponent<WalkSpeedCalibrator>();
        RecordData = GetComponent<RecordData>();

        ShowText = TextScreen.GetComponent<ShowText>();
        FeedbackText = TextFeedback.GetComponent<FeedbackText>();

        targetAppearance = StimulusScreen.GetComponent<targetAppearance>();
        makeAuditoryStimulus = StimulusScreen.GetComponent<MakeAuditoryStimulus>();
        // hide player camera if not in VR (useful for debugging).
        togglePlayers();

        // flip coin for responsemapping:
        assignResponses(); // assign Left/RIght clicks to above/below average(random)
        
        trialCount = 0;    
        trialinProgress = false;

        trialTime = 0f;
        collectEventSummary = false; // send info after each target to csv file.
        
        hasResponded = false;
        
        updateNextStimulus=false;
        
        SetUpSession = true;

    }

    // Update is called once per frame
    void Update()
    {
        if (SetUpSession && ShowText.isInitialized)
        {
            if (skipWalkCalibration)
            {
                // show welcome 
                ShowText.UpdateText(ShowText.TextType.CalibrationComplete);                
            }
            else
            {
                // show welcome 
                ShowText.UpdateText(ShowText.TextType.Welcome);
            }
            SetUpSession = false;
        }


        //pseudo code: 
        // listen for trial start (input)/
        // if input. 1) start the walking guide movement
        //           2) start the within trial co-routine
        //           3) start the data recording.

        if (!trialinProgress && playerInput.botharePressed)
        {

            // if we have not yet calibrated walk speed, simply move the wlaking guide to start loc:
            if (playinVR)
            {
                if (walkCalibrator.isCalibrationComplete())
                {
                    //start trial sequence, including:
                    // movement, co-routine, datarecording.

                    Debug.Log("Starting Trial in VR mode");
                    startTrial();
                }
                else
                {
                    Debug.Log("button pressed but walk calibration still in progress");
                    // lets hide the walking guide temporarily. 
                    controlWalkingGuide.setGuidetoHidden();
                }
            }
            else // not in VR, skip calibration:
            {
                // Non-VR mode: skip calibration check and start trial directly
                Debug.Log("Starting Trial (Non-VR mode)");
                startTrial();
            }

        }

        // increment trial time.
        if (trialinProgress)
        {
            trialTime += Time.deltaTime; // increment timer.

            if (trialTime > thisTrialDuration)
            {
                trialPackDown(); // includes trial incrementer
                trialCount++;
            }

            if (trialTime < 0.5f || hasResponded)
            {
                return; // do nothing if early, or if already processed a reponse for current event
            }

            if (playerInput.anyarePressed)
            {
                processPlayerResponse(); // determines if a 'Detect' or 'Reject' based on controller mappings.
            }



        }


        // // process no response (TO DO):
        // if (targetAppearance.processNoResponse) // i.e. no reponse was recorded ,this value is set in the targetAppearance coroutine.
        // {
        //     Debug.Log("No Response, and No update to staircase, regenerating...");
        //     //flip if present/absent on next trial:
        //     makeGaborTexture.gaborP.signalPresent = UnityEngine.Random.Range(0f, 1f) < 0.5f ? true : false;   // Changed from 0.66 as we have changed lower asymptote to 0 (pThreshold now 0.5, not 0.75)            
        //     makeGaborTexture.GenerateGabor(makeGaborTexture.gaborP.sAmp); // using the current intensity            
        //     updateNextGabor = false; // perform once only   
        //      targetAppearance.processNoResponse = false;
        // }

    } //end Update()

    
        
        
    

    void togglePlayers()
    {
        if (playinVR)
        {
            GameObject.Find("VR_Player").SetActive(true);
            GameObject.Find("Kb_Player").SetActive(false);
        }
        else
        {
            GameObject.Find("VR_Player").SetActive(false);
            GameObject.Find("Kb_Player").SetActive(true);

        }
    }
    void processPlayerResponse()
    {

        // first place the click into our array for subsequent recording
        expParams.trialD.clickOnsetTime = trialTime;
        

        if (hasResponded || detectIndex <= 0) // if response already processed, eject
        {
            return;
        }

        // Read the immutable stimulus event — this was frozen at stimulus onset
        // by targetAppearance, so it is safe to read even if PrepareNextComparison()
        // has already prepared the next stimulus in the background.
        var evt = currentEvent;

        // Determine what the participant responded: "longer" or "shorter"
        // responseMap == 1 means R=longer, L=shorter
        // responseMap == -1 means L=longer, R=shorter
        bool respondedLonger = false;
        if ((responseMap == 1 && playerInput.rightisPressed) || (responseMap == -1 && playerInput.leftisPressed))
        {
            respondedLonger = true;
        }

        // Score: was the response correct?
        bool comparisonWasLonger = (evt.comparisonType == experimentParameters.ComparisonType.Longer);

        if (respondedLonger == comparisonWasLonger)
        {
            Debug.Log("Correct! " + (respondedLonger ? "Longer" : "Shorter"));
            expParams.trialD.targCorrect = 1;
        }
        else
        {
            Debug.Log("Incorrect! Responded " + (respondedLonger ? "Longer" : "Shorter") +
                       " but was " + (comparisonWasLonger ? "Longer" : "Shorter"));
            expParams.trialD.targCorrect = 0;
        }
        expParams.trialD.targResponse = respondedLonger ? 1f : 0f; // 1 = longer, 0 = shorter

        // Pass the immutable event to the data recorder. RecordData reads
        // trial context (blockID, trialID, etc.) from trialD, and stimulus
        // data (durations, comparison type, etc.) from the event.
        RecordData.extractEventSummary(currentEvent);

        hasResponded = true; // passed to coroutine, avoids processing omitted responses.

        // Now update stimulus after each response
        // send the information to AdaptiveStaircase and prepare next comparison.

        if (trialCount >= expParams.nstandingStilltrials)
        {
            float nextDuration = makeAuditoryStimulus.auditoryP.standardDurationMs; //default

            bool wasCorrect = expParams.trialD.targCorrect == 1;

            // Map blockType to condition label for the staircase
            string condition = GetConditionLabel(expParams.trialD.blockType);

            if (condition != null)
            {
                nextDuration = adaptiveStaircase.ProcessResponse(condition, wasCorrect);
            }

            Debug.Log($"[Staircase:{condition}] {(wasCorrect ? "✓" : "✗")} → Next delta: {nextDuration:F3}s");

            // Apply new delta and prepare next comparison tone
            makeAuditoryStimulus.auditoryP.comparisonDurationMs = nextDuration;
            makeAuditoryStimulus.PrepareNextComparison();
        }
        else
        {
            // Practice trials: regenerate without updating staircase, provide feedback
            Debug.Log("Still in practice trials, regenerating... ");
            makeAuditoryStimulus.PrepareNextComparison();

            if (expParams.trialD.targCorrect == 1)
            {
                FeedbackText.UpdateText(FeedbackText.TextType.Correct);
                Invoke(nameof(HideFeedbackText), 0.2f);
            }
            else
            {
                FeedbackText.UpdateText(FeedbackText.TextType.Incorrect);
                Invoke(nameof(HideFeedbackText), 0.2f);
            }
        }
                
    }
    private void HideFeedbackText()
    {
        FeedbackText.UpdateText(FeedbackText.TextType.Hide);
    }

    void startTrial()
    {
        // This method handles the trial sequence.
        // First play the standard tone sequence (~1s), then start walking + comparisons.

        //recalibrate screen height to participants HMD
        controlWalkingGuide.updateScreenHeight();
        //remove text
        ShowText.UpdateText(ShowText.TextType.Hide);
        FeedbackText.UpdateText(FeedbackText.TextType.Hide);

        //establish trial parameters:
        if (expParams.maxTargsbySpeed == null)
        {
            // expParams.CalculateMaxTargetsBySpeed();
        }

        trialinProgress = true; // for coroutine (handled in targetAppearance.cs).
        ShowText.UpdateText(ShowText.TextType.Hide);
        trialTime = 0;
        targState = 0; //target is hidden.

        //Establish (this trial) specific parameters:
        blockType = expParams.blockTypeArray[trialCount, 2]; //third column [0,1,2].

        thisTrialDuration = expParams.walkDuration; // all trials the same duration now, distance varies instead.

        //query if stationary (restricts movement guide)
        isStationary = blockType == 0;

        //populate public trialD structure for extraction in recordData.cs
        expParams.trialD.trialNumber = trialCount;
        expParams.trialD.blockID = expParams.blockTypeArray[trialCount, 0];
        expParams.trialD.trialID = expParams.blockTypeArray[trialCount, 1]; // count within block
        expParams.trialD.isStationary = isStationary;
        expParams.trialD.blockType = blockType; // 0,1,2

        //updated phases for flow managers:
        RecordData.recordPhase = RecordData.phase.collectResponse;

        //start coroutine to control target onset and target behaviour:
        print("Starting Trial " + (trialCount + 1) + " of " + expParams.nTrialsperBlock);

        // Play standard tone sequence first (~1s), then start walking + comparisons
        StartCoroutine(StandardThenWalkSequence());
    }

    /// <summary>
    /// Coroutine: plays the standard tone sequence, then starts the walking guide
    /// and comparison stimulus sequence.
    /// </summary>
    IEnumerator StandardThenWalkSequence()
    {
        // 1. Play standard tone sequence ( standard tones with  gaps )
        yield return StartCoroutine(makeAuditoryStimulus.PlayStandardSequence());

        // 2. Now start movement guide (if not stationary)
        if (!isStationary)
        {
            controlWalkingGuide.moveGuideatWalkSpeed();
        }

        // 3. Start the comparison stimulus coroutine
        targetAppearance.startSequence();
    }

    void trialPackDown()
    {
        // This method handles the end of a trial, including data recording and cleanup.
        Debug.Log("End of Trial " + (trialCount + 1));

        // For safety
        RecordData.recordPhase = RecordData.phase.stop;
        //determine next start position for walking guide.

        controlWalkingGuide.SetGuideForNextTrial(); //uses current trialcount +1 to determine next position.

        // Reset trial state
        trialinProgress = false;
        trialTime = 0f;

        // Stop any playing audio
        makeAuditoryStimulus.StopAllAudio();


        // Update text screen to show next steps or end of experiment
        ShowText.UpdateText(ShowText.TextType.TrialStart); //using the previous trial count to show next trial info.


    }

    /// <summary>
    /// Maps a blockType integer to a condition label for the adaptive staircase.
    /// Returns null for stationary trials (blockType 0) which don't use the staircase.
    /// Add new entries here if new walking conditions are added.
    /// </summary>
    string GetConditionLabel(int blockType)
    {
        switch (blockType)
        {
            case 1: return "slow";
            case 2: return "natural";
            default: return null; // stationary — no staircase
        }
    }

    void assignResponses()
    {
        bool switchmapping = UnityEngine.Random.Range(0f, 1f) < 0.5f;

        ////Hack
        //// To force L:Longer R:Shorter
        //bool switchmapping = true;
        //// To force L:Shorter R:Longer
        //bool switchmapping = false;

        responseforShorterLonger = new string[2];

        if (switchmapping)
        {
            responseMap = -1;
            responseMapping = "L:Longer R:Shorter";
            responseforShorterLonger[0] = "Left click"; //longer
            responseforShorterLonger[1] = "Right click"; //shorter
        }
        else
        {
            responseMap = 1;
            responseforShorterLonger[0] = "Right click"; //longer
            responseforShorterLonger[1] = "Left click"; //shorter
            responseMapping = "L:Shorter R:Longer";
        }
    }



}
