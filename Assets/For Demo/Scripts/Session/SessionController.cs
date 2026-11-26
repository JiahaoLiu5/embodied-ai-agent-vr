using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Optional scripted session: avatar selection, narration, gestures played by the agent, a timed conversation with
/// the agent and a closing message. Panels, clips, durations and gestures are set in the Inspector. The two time
/// reminders appear 180 and 60 seconds before the end.
/// </summary>
public class SessionController : MonoBehaviour
{
    [Header("Core References")]
    public RealtimeAPIWrapper apiWrapper;
    public Animator agentAnimatorCore;
    public Animator agentAnimatorCoreTorso;
    public AudioSource backgroundAudioSource;

    [Header("Human Avatar Configuration")]
    public GameObject targetHumanAvatar;
    public bool forceMale = false;
    public Avatar maleAvatarRig;
    public Avatar femaleAvatarRig;

    [Header("Avatar Selection Prefabs (6 options)")]
    public GameObject avatarPrefab1_LeftX;
    public GameObject avatarPrefab2_LeftY;
    public GameObject avatarPrefab3_LeftTrigger;
    public GameObject avatarPrefab4_RightA;
    public GameObject avatarPrefab5_RightB;
    public GameObject avatarPrefab6_RightTrigger;

    [Header("Input Triggers (VR controller buttons)")]
    public InputActionReference btnLeftX;
    public InputActionReference btnLeftY;
    public InputActionReference btnLeftTrigger;
    public InputActionReference btnRightA;
    public InputActionReference btnRightB;
    public InputActionReference btnRightTrigger;

    [Header("Debug Control")]
    public bool enableKeyboardNumbersToStart = true;

    [Header("UI Panels")]
    public GameObject panel1;
    public GameObject panel2;
    public TextMeshProUGUI countdownText;

    [Header("Warning Panels (time reminders)")]
    [Tooltip("Shown when 3 minutes are left")]
    public GameObject panelWarning3Min;
    [Tooltip("Shown when 1 minute is left")]
    public GameObject panelWarning1Min;

    [Header("Background Audio")]
    [Tooltip("Optional narration 1: welcome")]
    public AudioClip audio1_Welcome;
    [Tooltip("Optional narration 2: introduction, with panel 1")]
    public AudioClip audio2_FamiliarizationIntro;
    [Tooltip("Optional narration 3: announcement of the actions")]
    public AudioClip audio3_StartActions;
    [Tooltip("Optional narration 4: start of the preparation time, with panel 2")]
    public AudioClip audio4_PrepConversation;
    [Tooltip("Optional narration 5: start of the conversation")]
    public AudioClip audio5_StartConversation;
    [Tooltip("Optional narration 6: end of the session")]
    public AudioClip audio6_EndSession;

    [Header("Timeline (seconds)")]
    [Tooltip("Wait after the avatar selection")]
    public float time0_AvatarLoadDelay = 3f;
    [Tooltip("Time for background audio 1")]
    public float time1 = 5f;
    [Tooltip("Time for background audio 2 and panel 1")]
    public float time2 = 10f;
    [Tooltip("Pause after the actions of the agent")]
    public float time3 = 3f;
    [Tooltip("Preparation time with panel 2")]
    public float time4_PrepDuration = 30f;
    [Tooltip("Length of the conversation with the agent")]
    public float time5_ConversationDuration = 180f;

    [Header("Agent Actions")]
    public string[] familiarizationActions = new string[] { "Action1", "Action2", "Action3", "Action4" };

    // Prevents a second selection or start
    private bool hasAvatarBeenSelected = false;

    private void OnEnable()
    {
        BindAction(btnLeftX, OnPressedLeftX);
        BindAction(btnLeftY, OnPressedLeftY);
        BindAction(btnLeftTrigger, OnPressedLeftTrigger);
        BindAction(btnRightA, OnPressedRightA);
        BindAction(btnRightB, OnPressedRightB);
        BindAction(btnRightTrigger, OnPressedRightTrigger);
    }

    private void OnDisable()
    {
        UnbindAction(btnLeftX, OnPressedLeftX);
        UnbindAction(btnLeftY, OnPressedLeftY);
        UnbindAction(btnLeftTrigger, OnPressedLeftTrigger);
        UnbindAction(btnRightA, OnPressedRightA);
        UnbindAction(btnRightB, OnPressedRightB);
        UnbindAction(btnRightTrigger, OnPressedRightTrigger);
    }

    private void Start()
    {
        // Hide all panels at the start
        if (panel1 != null) panel1.SetActive(false);
        if (panel2 != null) panel2.SetActive(false);
        if (panelWarning3Min != null) panelWarning3Min.SetActive(false);
        if (panelWarning1Min != null) panelWarning1Min.SetActive(false);
    }

    private void Update()
    {
        if (enableKeyboardNumbersToStart && Keyboard.current != null && !hasAvatarBeenSelected)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) TriggerAvatarSelection(avatarPrefab1_LeftX, "Keyboard 1");
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) TriggerAvatarSelection(avatarPrefab2_LeftY, "Keyboard 2");
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) TriggerAvatarSelection(avatarPrefab3_LeftTrigger, "Keyboard 3");
            else if (Keyboard.current.digit4Key.wasPressedThisFrame) TriggerAvatarSelection(avatarPrefab4_RightA, "Keyboard 4");
            else if (Keyboard.current.digit5Key.wasPressedThisFrame) TriggerAvatarSelection(avatarPrefab5_RightB, "Keyboard 5");
            else if (Keyboard.current.digit6Key.wasPressedThisFrame) TriggerAvatarSelection(avatarPrefab6_RightTrigger, "Keyboard 6");
        }
    }

    #region Input Callbacks
    private void OnPressedLeftX(InputAction.CallbackContext ctx) => TriggerAvatarSelection(avatarPrefab1_LeftX, "Left X");
    private void OnPressedLeftY(InputAction.CallbackContext ctx) => TriggerAvatarSelection(avatarPrefab2_LeftY, "Left Y");
    private void OnPressedLeftTrigger(InputAction.CallbackContext ctx) => TriggerAvatarSelection(avatarPrefab3_LeftTrigger, "Left Trigger");
    private void OnPressedRightA(InputAction.CallbackContext ctx) => TriggerAvatarSelection(avatarPrefab4_RightA, "Right A");
    private void OnPressedRightB(InputAction.CallbackContext ctx) => TriggerAvatarSelection(avatarPrefab5_RightB, "Right B");
    private void OnPressedRightTrigger(InputAction.CallbackContext ctx) => TriggerAvatarSelection(avatarPrefab6_RightTrigger, "Right Trigger");
    #endregion

    private void TriggerAvatarSelection(GameObject selectedPrefab, string sourceButton)
    {
        if (hasAvatarBeenSelected) return;
        hasAvatarBeenSelected = true;

        Debug.Log($"[Session Controller] Input received: {sourceButton}. Loading the selected avatar...");

        LoadAndRetargetAvatar(selectedPrefab);
        StartCoroutine(StartSessionAfterDelayRoutine());
    }

    private IEnumerator StartSessionAfterDelayRoutine()
    {
        Debug.Log($"[Session Controller] The session starts in {time0_AvatarLoadDelay} s...");
        yield return new WaitForSeconds(time0_AvatarLoadDelay);

        Debug.Log("[Session Controller] Session started.");
        StartCoroutine(SessionTimelineRoutine());
    }

    #region Avatar Loading & Retargeting Logic

    private void LoadAndRetargetAvatar(GameObject sourcePrefab)
    {
        if (sourcePrefab == null)
        {
            Debug.LogError("[Avatar Loader] Error: no source prefab is assigned to this button!");
            return;
        }
        if (targetHumanAvatar == null)
        {
            Debug.LogError("[Avatar Loader] Error: no target avatar is assigned!");
            return;
        }

        DetermineAndSwapAvatar(sourcePrefab.name);

        GameObject tempAvatar = Instantiate(sourcePrefab);
        tempAvatar.name = "Temp_Source_Avatar";
        tempAvatar.SetActive(true);

        Debug.Log("[Avatar Loader] [1/2] Physically aligning bones...");
        AlignSkeletonToSource(tempAvatar, targetHumanAvatar);

        Debug.Log("[Avatar Loader] [2/2] Transferring meshes...");
        RetargetMesh(tempAvatar, targetHumanAvatar);

        Destroy(tempAvatar);
        Debug.Log($"[Avatar Loader] Successfully loaded and retargeted Avatar from prefab: {sourcePrefab.name}");
    }

    private void DetermineAndSwapAvatar(string nameString)
    {
        Animator targetAnim = targetHumanAvatar.GetComponent<Animator>();
        if (targetAnim == null) return;

        bool isMale = forceMale;
        string fileName = nameString.ToLower();

        if (fileName.Contains("feminine") || fileName.Contains("female"))
        {
            isMale = false;
        }
        else if (fileName.Contains("masculine") || fileName.Contains("male"))
        {
            isMale = true;
        }

        if (isMale)
        {
            if (maleAvatarRig != null) targetAnim.avatar = maleAvatarRig;
            else Debug.LogError("[Avatar Loader] Missing Male Avatar Rig!");
        }
        else
        {
            if (femaleAvatarRig != null) targetAnim.avatar = femaleAvatarRig;
            else Debug.LogError("[Avatar Loader] Missing Female Avatar Rig!");
        }
    }

    private void AlignSkeletonToSource(GameObject sourceRoot, GameObject targetRoot)
    {
        var sourceBones = sourceRoot.GetComponentsInChildren<Transform>();
        var targetBones = targetRoot.GetComponentsInChildren<Transform>();

        Dictionary<string, Transform> targetMap = new Dictionary<string, Transform>();
        foreach (var t in targetBones) targetMap[t.name] = t;

        foreach (var sourceBone in sourceBones)
        {
            if (sourceBone == sourceRoot.transform) continue;

            Transform targetBone = null;
            string boneName = sourceBone.name;

            if (targetMap.ContainsKey(boneName))
            {
                targetBone = targetMap[boneName];
            }
            else
            {
                string alias = GetBoneAlias(boneName);
                if (!string.IsNullOrEmpty(alias) && targetMap.ContainsKey(alias))
                {
                    targetBone = targetMap[alias];
                }
            }

            if (targetBone != null)
            {
                targetBone.localPosition = sourceBone.localPosition;
                targetBone.localScale = sourceBone.localScale;
            }
        }
    }

    private string GetBoneAlias(string name)
    {
        if (name == "LeftEye") return "EyeLeft";
        if (name == "RightEye") return "EyeRight";
        if (name == "EyeLeft") return "LeftEye";
        if (name == "EyeRight") return "RightEye";
        return null;
    }

    private void RetargetMesh(GameObject sourceRoot, GameObject targetRoot)
    {
        Dictionary<string, Transform> targetBoneMap = new Dictionary<string, Transform>();
        foreach (var bone in targetRoot.GetComponentsInChildren<Transform>(true))
            if (!targetBoneMap.ContainsKey(bone.name)) targetBoneMap.Add(bone.name, bone);

        var sourceRenderers = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var sourceRenderer in sourceRenderers)
        {
            Transform existingChild = targetRoot.transform.Find(sourceRenderer.gameObject.name);
            GameObject newSubMesh = existingChild != null ? existingChild.gameObject : new GameObject(sourceRenderer.gameObject.name);
            if (existingChild == null) newSubMesh.transform.SetParent(targetRoot.transform);

            newSubMesh.transform.localPosition = Vector3.zero;
            newSubMesh.transform.localRotation = Quaternion.identity;
            newSubMesh.transform.localScale = Vector3.one;

            var targetRenderer = newSubMesh.GetComponent<SkinnedMeshRenderer>();
            if (targetRenderer == null) targetRenderer = newSubMesh.AddComponent<SkinnedMeshRenderer>();

            targetRenderer.sharedMesh = sourceRenderer.sharedMesh;
            targetRenderer.materials = sourceRenderer.materials;
            targetRenderer.updateWhenOffscreen = true;

            Transform[] newBones = new Transform[sourceRenderer.bones.Length];
            for (int i = 0; i < sourceRenderer.bones.Length; i++)
            {
                Transform oldBone = sourceRenderer.bones[i];
                Transform foundBone = null;

                if (oldBone != null)
                {
                    if (targetBoneMap.ContainsKey(oldBone.name)) foundBone = targetBoneMap[oldBone.name];
                    else
                    {
                        string alias = GetBoneAlias(oldBone.name);
                        if (alias != null && targetBoneMap.ContainsKey(alias)) foundBone = targetBoneMap[alias];
                    }
                }
                newBones[i] = foundBone != null ? foundBone : targetRoot.transform;
            }
            targetRenderer.bones = newBones;

            if (sourceRenderer.rootBone != null)
            {
                if (targetBoneMap.ContainsKey(sourceRenderer.rootBone.name))
                    targetRenderer.rootBone = targetBoneMap[sourceRenderer.rootBone.name];
            }
        }
    }
    #endregion

    #region Session Timeline

    private IEnumerator SessionTimelineRoutine()
    {
        // Stage 1: welcome
        Debug.Log("[Timeline] Playing background audio 1: Welcome");
        PlayAudio(audio1_Welcome);
        yield return new WaitForSeconds(time1);

        // Stage 2: introduction and panel 1
        Debug.Log("[Timeline] Playing background audio 2 and showing panel 1");
        PlayAudio(audio2_FamiliarizationIntro);
        if (panel1 != null) panel1.SetActive(true);
        yield return new WaitForSeconds(time2);

        // Stage 3: announcement of the actions
        Debug.Log("[Timeline] Hiding panel 1 and playing background audio 3");
        if (panel1 != null) panel1.SetActive(false);
        PlayAudio(audio3_StartActions);

        float audio3Length = audio3_StartActions != null ? audio3_StartActions.length : 0f;
        yield return new WaitForSeconds(audio3Length);

        // Stage 4: the agent plays the actions on both Animators at the same time
        Debug.Log("[Timeline] The agent starts the actions on Core and Torso...");
        foreach (string animName in familiarizationActions)
        {
            if (agentAnimatorCore != null) agentAnimatorCore.CrossFade(animName, 0.2f);
            if (agentAnimatorCoreTorso != null) agentAnimatorCoreTorso.CrossFade(animName, 0.2f);

            yield return null;

            float animLength = 0f;
            if (agentAnimatorCore != null)
            {
                if (agentAnimatorCore.IsInTransition(0))
                    animLength = agentAnimatorCore.GetNextAnimatorStateInfo(0).length;
                else
                    animLength = agentAnimatorCore.GetCurrentAnimatorStateInfo(0).length;
            }

            yield return new WaitForSeconds(animLength > 0 ? Mathf.Max(0, animLength - 0.2f) : 2f);
        }

        Debug.Log($"[Timeline] Actions finished, waiting for time 3 ({time3}s)...");
        yield return new WaitForSeconds(time3);

        // Stage 5: preparation time
        Debug.Log("[Timeline] Playing background audio 4, showing panel 2 and starting the preparation countdown...");
        PlayAudio(audio4_PrepConversation);
        if (panel2 != null) panel2.SetActive(true);

        float prepTimer = time4_PrepDuration;
        while (prepTimer > 0)
        {
            UpdateCountdownUI(prepTimer, "Prep Time");
            yield return new WaitForSeconds(1f);
            prepTimer -= 1f;
        }

        // Stage 6: conversation with the agent, including the time reminders
        Debug.Log("[Timeline] Playing background audio 5, hiding panel 2 and connecting to the Realtime API");
        PlayAudio(audio5_StartConversation);
        if (panel2 != null) panel2.SetActive(false);

        if (apiWrapper != null) apiWrapper.Connect();

        float conversationTimer = time5_ConversationDuration;
        while (conversationTimer > 0)
        {
            UpdateCountdownUI(conversationTimer, "Time Remaining");

            // Reminder when 3 minutes are left
            if (Mathf.Approximately(conversationTimer, 180f) && panelWarning3Min != null)
            {
                StartCoroutine(ShowPanelTemporarily(panelWarning3Min, 3f));
            }
            // Reminder when 1 minute is left
            else if (Mathf.Approximately(conversationTimer, 60f) && panelWarning1Min != null)
            {
                StartCoroutine(ShowPanelTemporarily(panelWarning1Min, 3f));
            }

            yield return new WaitForSeconds(1f);
            conversationTimer -= 1f;
        }

        // Stage 7: end
        Debug.Log("[Timeline] Time is up. Playing background audio 6 and disconnecting from the Realtime API.");
        PlayAudio(audio6_EndSession);
        UpdateCountdownUI(0, "Session Completed");

        if (apiWrapper != null) apiWrapper.Disconnect();

        Debug.Log("[Session Controller] Session finished.");
    }

    /// <summary>
    /// Shows a panel and hides it after the given number of seconds, without blocking the timeline
    /// </summary>
    private IEnumerator ShowPanelTemporarily(GameObject panel, float duration)
    {
        if (panel != null)
        {
            panel.SetActive(true);
            yield return new WaitForSeconds(duration);
            panel.SetActive(false);
        }
    }

    #endregion

    #region Utility Methods
    private void BindAction(InputActionReference actionRef, System.Action<InputAction.CallbackContext> callback)
    {
        if (actionRef != null && actionRef.action != null)
        {
            actionRef.action.Enable();
            actionRef.action.performed += callback;
        }
    }

    private void UnbindAction(InputActionReference actionRef, System.Action<InputAction.CallbackContext> callback)
    {
        if (actionRef != null && actionRef.action != null)
        {
            actionRef.action.performed -= callback;
        }
    }

    private void PlayAudio(AudioClip clip)
    {
        if (backgroundAudioSource != null && clip != null)
        {
            backgroundAudioSource.clip = clip;
            backgroundAudioSource.Play();
        }
    }

    private void UpdateCountdownUI(float timeLeft, string prefix)
    {
        if (countdownText != null)
        {
            int minutes = Mathf.FloorToInt(timeLeft / 60f);
            int seconds = Mathf.FloorToInt(timeLeft % 60f);
            countdownText.text = $"{prefix}: {minutes:00}:{seconds:00}";
        }
    }
    #endregion
}
