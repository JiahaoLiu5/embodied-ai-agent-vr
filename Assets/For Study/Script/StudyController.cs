using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class StudyController : MonoBehaviour
{
    [Header("Core References (核心引用)")]
    public RealtimeAPIWrapper apiWrapper;
    public Animator agentAnimatorCore;
    public Animator agentAnimatorCoreTorso;
    public AudioSource backgroundAudioSource;

    [Header("Human Avatar Configuration (人类化身加载设置)")]
    public GameObject targetHumanAvatar;
    public bool forceMale = false;
    public Avatar maleAvatarRig;
    public Avatar femaleAvatarRig;

    [Header("Avatar Selection Prefabs (6个选项)")]
    public GameObject avatarPrefab1_LeftX;
    public GameObject avatarPrefab2_LeftY;
    public GameObject avatarPrefab3_LeftTrigger;
    public GameObject avatarPrefab4_RightA;
    public GameObject avatarPrefab5_RightB;
    public GameObject avatarPrefab6_RightTrigger;

    [Header("Input Triggers (VR 手柄按键绑定)")]
    public InputActionReference btnLeftX;
    public InputActionReference btnLeftY;
    public InputActionReference btnLeftTrigger;
    public InputActionReference btnRightA;
    public InputActionReference btnRightB;
    public InputActionReference btnRightTrigger;

    [Header("Debug Control (调试控制)")]
    public bool enableKeyboardNumbersToStart = true;

    [Header("UI Panels (UI 面板)")]
    public GameObject panel1;
    public GameObject panel2;
    public TextMeshProUGUI countdownText;

    [Header("Warning Panels (时间提醒面板)")]
    [Tooltip("剩余3分钟时的提示面板")]
    public GameObject panelWarning3Min;
    [Tooltip("剩余1分钟时的提示面板")]
    public GameObject panelWarning1Min;

    [Header("Background Audios (背景音)")]
    public AudioClip audio1_Welcome;
    public AudioClip audio2_FamiliarizationIntro;
    public AudioClip audio3_StartActions;
    public AudioClip audio4_PrepDebate;
    public AudioClip audio5_StartDebate;
    public AudioClip audio6_EndStudy;

    [Header("Timelines (时间控制 - 秒)")]
    public float time0_AvatarLoadDelay = 3f;
    public float time1 = 5f;
    public float time2 = 10f;
    public float time3 = 3f;
    public float time4_PrepDuration = 30f;
    public float time5_DebateDuration = 180f;

    [Header("Agent Actions (动作列表)")]
    public string[] familiarizationActions = new string[] { "Action1", "Action2", "Action3", "Action4" };

    // 防止多次选择和触发的安全锁
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
        // 确保所有的初始状态面板都是隐藏的
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

        Debug.Log($"[Study Controller] 收到输入: {sourceButton}. 开始加载对应的 Avatar...");

        LoadAndRetargetAvatar(selectedPrefab);
        StartCoroutine(StartStudyAfterDelayRoutine());
    }

    private IEnumerator StartStudyAfterDelayRoutine()
    {
        Debug.Log($"[Study Controller] Avatar 加载完成，等待 Time 0 ({time0_AvatarLoadDelay}s) 后正式启动流程...");
        yield return new WaitForSeconds(time0_AvatarLoadDelay);
        
        Debug.Log("[Study Controller] 实验主流程正式启动！");
        StartCoroutine(StudyTimelineRoutine());
    }

    #region Avatar Loading & Retargeting Logic
    
    private void LoadAndRetargetAvatar(GameObject sourcePrefab)
    {
        if (sourcePrefab == null)
        {
            Debug.LogError("[Avatar Loader] Error: 所选按键未配置对应的 Source Prefab!");
            return;
        }
        if (targetHumanAvatar == null)
        {
            Debug.LogError("[Avatar Loader] Error: 未绑定 Target Human Avatar!");
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

    #region Study Timeline
    
    private IEnumerator StudyTimelineRoutine()
    {
        // 阶段 1：欢迎被试
        Debug.Log("[Timeline] 播放背景音 1: Welcome");
        PlayAudio(audio1_Welcome);
        yield return new WaitForSeconds(time1);

        // 阶段 2：环境熟悉介绍与面板 1
        Debug.Log("[Timeline] 播放背景音 2 且显示面板 1");
        PlayAudio(audio2_FamiliarizationIntro);
        if (panel1 != null) panel1.SetActive(true);
        yield return new WaitForSeconds(time2);

        // 阶段 3：引导做动作
        Debug.Log("[Timeline] 隐藏面板 1，播放背景音 3");
        if (panel1 != null) panel1.SetActive(false);
        PlayAudio(audio3_StartActions);
        
        float audio3Length = audio3_StartActions != null ? audio3_StartActions.length : 0f;
        yield return new WaitForSeconds(audio3Length);

        // 阶段 4：Agent 同步驱动双 Animator
        Debug.Log("[Timeline] Agent 开始在 Core 和 Torso 上同步执行熟悉动作...");
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

        Debug.Log($"[Timeline] 动作执行完毕，进入时间 3 ({time3}s) 的间隔缓冲...");
        yield return new WaitForSeconds(time3);

        // 阶段 5：辩论准备期
        Debug.Log("[Timeline] 播放背景音 4，显示面板 2，准备辩论倒计时开始...");
        PlayAudio(audio4_PrepDebate);
        if (panel2 != null) panel2.SetActive(true);
        
        float prepTimer = time4_PrepDuration;
        while (prepTimer > 0)
        {
            UpdateCountdownUI(prepTimer, "Prep Time");
            yield return new WaitForSeconds(1f);
            prepTimer -= 1f;
        }

        // 阶段 6：辩论迭代阶段 (包含时间提醒模块)
        Debug.Log("[Timeline] 播放背景音 5，隐藏面板 2，连接 GPT 开始辩论！");
        PlayAudio(audio5_StartDebate);
        if (panel2 != null) panel2.SetActive(false);
        
        if (apiWrapper != null) apiWrapper.ConnectWebSocketButton();

        float debateTimer = time5_DebateDuration;
        while (debateTimer > 0)
        {
            UpdateCountdownUI(debateTimer, "Debate Time Remaining");

            // 【新增】：3分钟提醒检测
            if (Mathf.Approximately(debateTimer, 180f) && panelWarning3Min != null)
            {
                StartCoroutine(ShowPanelTemporarily(panelWarning3Min, 3f));
            }
            // 【新增】：1分钟提醒检测
            else if (Mathf.Approximately(debateTimer, 60f) && panelWarning1Min != null)
            {
                StartCoroutine(ShowPanelTemporarily(panelWarning1Min, 3f));
            }

            yield return new WaitForSeconds(1f);
            debateTimer -= 1f;
        }

        // 阶段 7：实验结束
        Debug.Log("[Timeline] 辩论时间到！播放背景音 6，断开 GPT 连接。");
        PlayAudio(audio6_EndStudy);
        UpdateCountdownUI(0, "Study Completed");
        
        if (apiWrapper != null) apiWrapper.ConnectWebSocketButton();
        
        Debug.Log("[Study Controller] 实验主流程圆满结束！");
    }

    /// <summary>
    /// 【新增】辅助协程：显示目标面板，并在经过 duration 秒后自动隐藏，不阻塞主线程
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