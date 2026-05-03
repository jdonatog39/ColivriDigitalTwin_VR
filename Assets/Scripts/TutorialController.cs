using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;

public class TutorialController : MonoBehaviour
{
    public static TutorialController Instance { get; private set; }
    public enum Stage { Saludo, Observacion, Controladores, Teleport, Agarre, Cierre }

    [System.Serializable]
    public class StageAnimation
    {
        [Header("Etapa y referencias")]
        public Stage stage;
        [Tooltip("Panel principal de instrucciones de la etapa")]
        public GameObject panel;
        [Tooltip("Animator principal de la etapa (e.g., para la guía del panel)")]
        public Animator animator;


        [Header("Triggers opcionales")]
        public string onEnterTrigger = "Enter";
        public string onCompleteTrigger = "Complete";

        [Header("Auto show/hide")]
        public bool autoShowOnEnter = true;
        public bool autoHideOnComplete = true;
        [Tooltip("Retraso para ocultar tras Complete")]
        public float hideDelay = 0.2f;
    }

    [Header("Animaciones por etapa")]
    [SerializeField] private List<StageAnimation> stageAnimations = new List<StageAnimation>();
    [SerializeField, Tooltip("Al iniciar la escena, apaga todos los paneles de etapas")]
    private bool startPanelsInactive = true;

    private HashSet<Stage> completedStages = new HashSet<Stage>();

    private StageAnimation GetSA(Stage st) => stageAnimations.Find(x => x.stage == st);

    private void PlayStageEnter(Stage st)
    {
        var sa = GetSA(st);
        if (sa == null) return;
        if (sa.autoShowOnEnter && sa.panel && !sa.panel.activeSelf) sa.panel.SetActive(true);
        if (sa.animator && !string.IsNullOrEmpty(sa.onEnterTrigger)) sa.animator.SetTrigger(sa.onEnterTrigger);
    }

    private void PlayStageComplete(Stage st)
    {
        var sa = GetSA(st);
        if (sa == null) return;
        if (sa.animator && !string.IsNullOrEmpty(sa.onCompleteTrigger)) sa.animator.SetTrigger(sa.onCompleteTrigger);
        if (sa.autoHideOnComplete && sa.panel) StartCoroutine(HidePanelAfter(sa.panel, sa.hideDelay));
    }

    private IEnumerator HidePanelAfter(GameObject go, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (go) go.SetActive(false);
    }

    private void HideAllStagePanels()
    {
        foreach (var sa in stageAnimations)
            if (sa != null && sa.panel) sa.panel.SetActive(false);
    }

    [Header("Orden de etapas")]
    [SerializeField] private Stage startStage = Stage.Saludo;

    [Header("UI Principal")]
    [SerializeField] private TMP_Text instructionText;

    [Header("Animación de Aura (Robot)")]
    [SerializeField] private Animator auraAnimator;
    [SerializeField] private string talkingBoolName = "talking"; 
    [SerializeField] private string clappingTriggerName = "clapping";
    [SerializeField] private string greetingName = "greeting";

    [Header("Progreso de Etapas (Animators)")]
    [Tooltip("Lista de Animators que representan los íconos de progreso (Observación, Controladores, Teleport, Agarre) en orden.")]
    [SerializeField] private Animator[] progressAnimators;
    [Tooltip("Trigger a activar en el Animator de progreso cuando una etapa se completa (e.g., 'Activate' o 'Check').")]
    [SerializeField] private string progressTrigger = "Activate";

    [Header("UI Extra")]
    [SerializeField] private GameObject welcomePanel;
    [SerializeField] private GameObject instructionsPanel;
    [SerializeField] private float welcomeDuration = 5f;

    [Header("Audio General")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioClip musicClip;
    [SerializeField] private AudioSource voiceSource;
    [SerializeField] private AudioClip completionCheckClip;
    [SerializeField] private AudioClip saludoClip, obsClip, ctrlClip, agarreClip, cierreClip;
    [Header("Audio Feedback")]
    [SerializeField] private AudioSource feedbackSource;
    [SerializeField] private AudioClip completionCelebrationClip;
    [SerializeField] private AudioSource celebrationSource;

    [Header("Audio Teleport Secuencia")]
    [SerializeField] private AudioClip teleportIntroClip;
    [SerializeField] private AudioClip teleportClip;
    [SerializeField] private AudioClip teleportTargetClip;



    [Header("Portal / Salida")]
    [SerializeField] private GameObject portalRoot;
    [SerializeField] private string sceneToLoad = "RegistrationScene";
    [SerializeField] private Collider portalTrigger;

    [Header("Refs del Rig / Jugador")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftController;
    [SerializeField] private Transform rightController;

    [Header("Teleport Settings")]
    [SerializeField] private GameObject locomotionRoot;
    [Tooltip("El objeto Target (e.g., cilindro, círculo) que se activa y completa el paso al ser tocado por el jugador (usando un Box Collider con Is Trigger).")]
    [SerializeField] private GameObject teleportTargetTrigger;
    private bool teleportTargetReached = false; 
    private bool teleportEnabled = false;
    private readonly List<Behaviour> cachedTeleportBehaviours = new List<Behaviour>();
    private bool teleportCacheBuilt = false;

    [Header("Observación 360°")]
    [SerializeField] private float requiredYaw = 300f;
    [SerializeField] private float minAngularSpeed = 5f;
    private float obsAccumYaw;
    private Vector3 lastForwardFlat;

    [Header("Agarre de objetos (Interaction SDK)")]
    [SerializeField] private List<GameObject> grabbables = new List<GameObject>();
    private bool anyGrabRegistered;

    [Header("Controladores detección")]
    [SerializeField] private bool useTriggersForFace = true;
    [SerializeField] private float moveDelta = 0.15f;
    [SerializeField] private float faceRadius = 0.28f;
    private bool leftMoved, rightMoved;
    private bool leftNearFace, rightNearFace;
    private Vector3 l0, r0;

    [Header("Teleport (joystick adelante)")]
    [SerializeField] private float thumbstickForwardThreshold = 0.6f;
    [SerializeField] private float holdTimeToConfirm = 0.5f;
    [SerializeField] private float teleportTransitionTime = 0.35f;


    private float thumbHoldTimer;
    private int teleportStep;
    //private bool teleportDone;
    private bool hasPlayedTargetAudio;

    private Stage current;
    private bool tutorialFinished;
    private bool _advancing = false;
    private Coroutine currentVoiceCoroutine = null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (!head && Camera.main) head = Camera.main.transform;

        if (musicSource && musicClip)
        {
            musicSource.clip = musicClip;
            musicSource.loop = true;
            musicSource.Play();
        }

        if (startPanelsInactive) HideAllStagePanels();

        if (welcomePanel) welcomePanel.SetActive(true);
        GoToStage(startStage);
        StartCoroutine(HideWelcomeThenShowChecklist());


        EnablePortal(false);

        SetTeleportActive(false);


        completedStages.Clear();

       
    }

    private IEnumerator HideWelcomeThenShowChecklist()
    {
        yield return new WaitForSeconds(welcomeDuration);
        if (welcomePanel) welcomePanel.SetActive(false);
    }

    private void Update()
    {
        if (!tutorialFinished)
        {
            switch (current)
            {
                case Stage.Saludo:
                    if (voiceSource == null || !voiceSource.isPlaying) StartCoroutine(AdvanceAfter(1.0f));
                    break;

                case Stage.Observacion:
                    if (CheckObservation360()) CompleteCurrentStage();
                    break;

                case Stage.Controladores:
                    if (CheckControllersMoved()) CompleteCurrentStage();
                    break;

                case Stage.Teleport:
                    UpdateTeleportLogic();
                    break; 

                case Stage.Agarre:
                    if (CheckGrabbedOnce()) CompleteCurrentStage();
                    break;
            }
        }

        if (portalTrigger && head && portalTrigger.enabled && AreAllRequiredStagesCompleted())
        {
            if (portalTrigger.bounds.Contains(head.position))
            {
                TryLoadNextScene();
            }
        }

    }

    private void GoToStage(Stage st)
    {
        current = st;

        if (teleportTargetTrigger) teleportTargetTrigger.SetActive(false);


        switch (st)
        {
            case Stage.Saludo:
                if (auraAnimator && !string.IsNullOrEmpty(greetingName))
                {
                    auraAnimator.SetTrigger(greetingName);
                }
                Say("¡Bienvenido al laboratorio! Aquí aprenderás a interactuar en Realidad Virtual.");
                PlayVoice(saludoClip);
                var sa_saludo = GetSA(st);
                instructionsPanel.SetActive(false);
                if (sa_saludo != null && sa_saludo.panel != null) sa_saludo.panel.SetActive(true);
                SetTeleportActive(false);
                break;

            case Stage.Observacion:

                obsAccumYaw = 0f;
                lastForwardFlat = FlatForward(head ? head.forward : Vector3.forward);
                Say("Mira a tu alrededor para familiarizarte con el entorno.");
                instructionsPanel.SetActive(true);
                PlayVoice(obsClip);
                PlayStageEnter(st);
                SetTeleportActive(false);
                break;

            case Stage.Controladores:
                InitControllers();
                Say("Mueve ambas manos para ver tus controladores virtuales.");
                instructionsPanel.SetActive(true);
                PlayVoice(ctrlClip);
                PlayStageEnter(st);
                SetTeleportActive(false);
                break;

            case Stage.Teleport:
                //teleportDone = false;
                instructionsPanel.SetActive(true);
                teleportStep = 0;
                thumbHoldTimer = 0f;
                hasPlayedTargetAudio = false;
                teleportTargetReached = false;

                if (teleportTargetTrigger) teleportTargetTrigger.SetActive(true); 

                StartCoroutine(PlayTeleportSequence());
                PlayStageEnter(st);
                SetTeleportActive(true);
                break;

            case Stage.Agarre:
                InitGrab();
                Say("Apunta al objeto y aprieta el gatillo para sujetarlo. Mantén presionado para sostenerlo y suelta para dejarlo.");
                instructionsPanel.SetActive(true);
                PlayVoice(agarreClip);
                PlayStageEnter(st);
                SetTeleportActive(true);
                break;

            case Stage.Cierre:
                Say("¡Excelente! Dirígete a la puerta holográfica para continuar.");
                PlayVoice(cierreClip);


                if (AreAllRequiredStagesCompleted()) EnablePortal(true);

                tutorialFinished = true;
                PlayStageEnter(st);
                SetTeleportActive(true);
                break;
        }
    }

    private IEnumerator PlayTeleportSequence()
    {
        Say("Vamos a enseñarte cómo moverte usando Teleport.");
        if (teleportIntroClip)
        {
            PlayVoice(teleportIntroClip);
            yield return new WaitForSeconds(teleportIntroClip.length);
        }
        else
        {
            yield return new WaitForSeconds(2f);
        }

        Say("Empuja el joystick derecho hacia adelante para activar el rayo de teletransporte.");
        PlayVoice(teleportClip);
    }

    private void UpdateTeleportLogic()
    {
        if (!teleportEnabled) return;

        Vector2 rs = Vector2.zero;
#if UNITY_ANDROID || UNITY_STANDALONE
        rs = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
#endif
        if (rs.y > thumbstickForwardThreshold)
        {
            thumbHoldTimer += Time.deltaTime;

            if (teleportStep == 0 && thumbHoldTimer > 0.05f)
            {
                TeleportAnimTrigger("Push");
                teleportStep = 1;
                if (!hasPlayedTargetAudio)
                {
                    hasPlayedTargetAudio = true;
                    if (teleportTargetClip)
                    {
                        PlayVoice(teleportTargetClip);
                        Say("Con el rayo, apunta al sitio indicado en el piso y suelta.");
                    }
                }
            }
            if (teleportStep == 1 && thumbHoldTimer >= holdTimeToConfirm)
            {
                TeleportAnimTrigger("Charge");
                teleportStep = 2;
                StartCoroutine(TeleportBeamFinishAfter(teleportTransitionTime));
            }
        }
        else
        {
            if (teleportStep < 2)
            {
                thumbHoldTimer = 0f;
                teleportStep = 0;
            }
        }
    }



    private void BuildTeleportCacheIfNeeded()
    {
        if (teleportCacheBuilt) return;
        teleportCacheBuilt = true;
        cachedTeleportBehaviours.Clear();

        if (!locomotionRoot) return;

        var behaviours = locomotionRoot.GetComponentsInChildren<Behaviour>(true);
        foreach (var b in behaviours)
        {
            if (b == null) continue;
            var typeName = b.GetType().Name;
            if (typeName.Contains("Teleport"))
            {
                cachedTeleportBehaviours.Add(b);
            }
        }
    }

    private void SetTeleportActive(bool on)
    {
        teleportEnabled = on;
        BuildTeleportCacheIfNeeded();

        if (locomotionRoot)
            locomotionRoot.SetActive(on);

        if (cachedTeleportBehaviours.Count > 0)
        {
            foreach (var b in cachedTeleportBehaviours)
                if (b) b.enabled = on;
        }
    }

    private IEnumerator AdvanceAfter(float seconds)
    {
        if (_advancing) yield break;
        _advancing = true;
        yield return new WaitForSeconds(seconds);
        _advancing = false;
        if (current == Stage.Saludo)
        {
            //initialDelayPassed = true;
        }
        Advance();
    }

    private void Advance()
    {
        if (current == Stage.Cierre) return;
        if (current == Stage.Saludo && welcomePanel && welcomePanel.activeSelf)
        {
            welcomePanel.SetActive(false);
        }
        GoToStage(current + 1);
    }

    private int GetProgressIndexFor(Stage st)
    {
        switch (st)
        {
            case Stage.Observacion: return 0;
            case Stage.Controladores: return 1;
            case Stage.Teleport: return 2;
            case Stage.Agarre: return 3;
            default: return -1;
        }
    }

    private void ActivateProgressAnimator(Stage st)
    {
        int index = GetProgressIndexFor(st);

        if (index >= 0 && progressAnimators != null && index < progressAnimators.Length)
        {
            var anim = progressAnimators[index];
            if (anim != null && !string.IsNullOrEmpty(progressTrigger))
            {

                if (!anim.gameObject.activeSelf)
                {
                    anim.gameObject.SetActive(true);
                }
                anim.SetTrigger(progressTrigger);
            }
        }
    }

    private void CompleteCurrentStage()
    {
        if (current == Stage.Teleport && teleportTargetTrigger)
        {
            teleportTargetTrigger.SetActive(false);
        }

        if (!completedStages.Contains(current))
        {
            completedStages.Add(current);
            if (current != Stage.Saludo && current != Stage.Cierre)
            {
                ActivateProgressAnimator(current);
                if (feedbackSource && completionCheckClip)
                {
                    feedbackSource.PlayOneShot(completionCheckClip);
                }
            }

        }

        if (current == Stage.Agarre && AreAllRequiredStagesCompleted()) 
        {
           
            StartCoroutine(PlayCelebrationSequence());
        }
        else 
        {
            if (AreAllRequiredStagesCompleted()) EnablePortal(true);

            PlayStageComplete(current);
            Advance();
        }
    }
    private IEnumerator PlayCelebrationSequence()
    {
        const float celebrationDuration = 8.0f;
        float waitTime = celebrationDuration;
        yield return new WaitForSeconds(0.1f);
        bool musicWasPlaying = false;
        if (musicSource != null && musicSource.isPlaying)
        {
            musicWasPlaying = true;
            musicSource.Pause();
        }
        if (auraAnimator && !string.IsNullOrEmpty(clappingTriggerName))
        {
            auraAnimator.SetTrigger(clappingTriggerName);
        }
        AudioClip clipToPlay = null;
        if (celebrationSource != null && celebrationSource.clip != null)
            clipToPlay = celebrationSource.clip;
        else if (completionCelebrationClip != null)
            clipToPlay = completionCelebrationClip;

        if (celebrationSource != null && clipToPlay != null)
        {
            if (!celebrationSource.isPlaying)
            {
                if (celebrationSource.clip != clipToPlay) celebrationSource.clip = clipToPlay;
                celebrationSource.Play();
            }
            else
            {
                Debug.Log("Celebration: celebrationSource ya está sonando; no lanzo otra reproducción.");
            }

            waitTime = Mathf.Min(clipToPlay.length, celebrationDuration);
        }


        yield return new WaitForSeconds(waitTime);

        if (celebrationSource != null && celebrationSource.isPlaying)
        {
            celebrationSource.Stop();
        }
        if (musicWasPlaying && musicSource != null)
        {
            musicSource.UnPause();
        }
        PlayStageComplete(current);
        Advance();
    }

    private bool AreAllRequiredStagesCompleted()
    {
        return completedStages.Contains(Stage.Observacion) &&
           completedStages.Contains(Stage.Controladores) &&
           completedStages.Contains(Stage.Teleport) &&
           completedStages.Contains(Stage.Agarre);
    }

    private void Say(string text) { if (instructionText) instructionText.text = text; }

    private void PlayVoice(AudioClip clip)
    {
        if (clip == null || voiceSource == null)
        {
            if (currentVoiceCoroutine != null) { StopCoroutine(currentVoiceCoroutine); currentVoiceCoroutine = null; }
            if (auraAnimator) auraAnimator.SetBool(talkingBoolName, false);
            return;
        }

        if (currentVoiceCoroutine != null)
        {
            StopCoroutine(currentVoiceCoroutine);
            currentVoiceCoroutine = null;
        }
        voiceSource.Stop();

        voiceSource.clip = clip;
        voiceSource.Play();

        if (auraAnimator) auraAnimator.SetBool(talkingBoolName, true);

        currentVoiceCoroutine = StartCoroutine(VoiceLifecycleCoroutine());
    }
    private IEnumerator VoiceLifecycleCoroutine()
    {
        while (voiceSource != null && voiceSource.isPlaying)
        {
            yield return null;
        }

        if (auraAnimator) auraAnimator.SetBool(talkingBoolName, false);

        currentVoiceCoroutine = null;
        yield break;
    }
    private IEnumerator StopTalkingAfterClip(float clipLength)
    {
        
        yield return new WaitForSeconds(clipLength);
        print("acabó el audio");
        if (auraAnimator)
        {
            auraAnimator.SetBool(talkingBoolName, false);
        }
    }

    public void NotifyTeleportTargetReached()
    {
        if (current == Stage.Teleport && !teleportTargetReached)
        {
            teleportTargetReached = true;
            CompleteCurrentStage();
        }
    }

    private void EnablePortal(bool on)
    {
        if (portalRoot) portalRoot.SetActive(on);
        if (portalTrigger) portalTrigger.enabled = on;
    }

    private bool CheckObservation360()
    {
        if (!head) return false;
        Vector3 now = FlatForward(head.forward);
        float delta = Vector3.SignedAngle(lastForwardFlat, now, Vector3.up);
        if (Mathf.Abs(delta) > minAngularSpeed * Time.deltaTime)
        {
            obsAccumYaw += Mathf.Abs(delta);
            lastForwardFlat = now;
        }
        return obsAccumYaw >= requiredYaw;
    }
    private Vector3 FlatForward(Vector3 fwd)
    {
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        return fwd.normalized;
    }

    private void InitControllers()
    {
        leftMoved = rightMoved = false;
        leftNearFace = rightNearFace = false;

        if (leftController) l0 = leftController.position;
        if (rightController) r0 = rightController.position;
    }

    public void NotifyFaceProximity(bool isLeft)
    {
        if (isLeft) leftNearFace = true;
        else rightNearFace = true;
        if (current == Stage.Controladores && CheckControllersMoved())
            CompleteCurrentStage();
    }

    private bool CheckControllersMoved()
    {
        if (head == null || leftController == null || rightController == null) return false;

        if (leftController && Vector3.Distance(l0, leftController.position) >= moveDelta) leftMoved = true;
        if (rightController && Vector3.Distance(r0, rightController.position) >= moveDelta) rightMoved = true;

        if (!useTriggersForFace && head)
        {
            if (leftController && Vector3.Distance(leftController.position, head.position) <= faceRadius) leftNearFace = true;
            if (rightController && Vector3.Distance(rightController.position, head.position) <= faceRadius) rightNearFace = true;
        }

        bool leftOk = leftMoved || leftNearFace;
        bool rightOk = rightMoved || rightNearFace;

        return leftOk && rightOk;
    }

    private void InitGrab() { anyGrabRegistered = false; }

    private bool CheckGrabbedOnce()
    {
        if (anyGrabRegistered) return true;
        if (grabbables == null || grabbables.Count == 0) return false;

        foreach (var go in grabbables)
        {
            if (!go) continue;

            var gi = go.GetComponent<GrabInteractable>() ?? go.GetComponentInChildren<GrabInteractable>(true);
            var hgi = go.GetComponent<HandGrabInteractable>() ?? go.GetComponentInChildren<HandGrabInteractable>(true);
            var dgi = go.GetComponent<DistanceGrabInteractable>() ?? go.GetComponentInChildren<DistanceGrabInteractable>(true);
            var dhg = go.GetComponent<DistanceHandGrabInteractable>() ?? go.GetComponentInChildren<DistanceHandGrabInteractable>(true);

            if ((gi && gi.State == InteractableState.Select) ||
              (hgi && hgi.State == InteractableState.Select) ||
              (dgi && dgi.State == InteractableState.Select) ||
              (dhg && dhg.State == InteractableState.Select))
            {
                anyGrabRegistered = true; break;
            }
        }
        return anyGrabRegistered;
    }
    private IEnumerator TeleportBeamFinishAfter(float t)
    {
        yield return new WaitForSeconds(t);
        TeleportAnimTrigger("Beam");
        //teleportDone = true;
    }

    private void TeleportAnimTrigger(string trig)
    {
        var sa = GetSA(Stage.Teleport);
        if (sa != null && sa.animator && !string.IsNullOrEmpty(trig))
            sa.animator.SetTrigger(trig);
    }
    private void TryLoadNextScene()
    {
        if (!AreAllRequiredStagesCompleted()) return;
        if (string.IsNullOrEmpty(sceneToLoad)) return;

        SceneManager.LoadScene(sceneToLoad);
    }

}