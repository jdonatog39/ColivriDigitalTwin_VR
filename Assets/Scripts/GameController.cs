using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameController : MonoBehaviour
{
    public static GameController Instance { get; private set; }

    [Header("UI Panels")]
    public GameObject initialPanel;
    public GameObject registrationPanel;
    public GameObject instructionsPanel;
    public GameObject codePanel;
    public GameObject gameOverPanel;
    [SerializeField] private HighScoreTable highScoreTable;
    public GameObject statsRankingPanel;

    [Header("Timer Panels")]
    [SerializeField] private GameObject timerPanelDefault;        // Fácil / Normal
    [SerializeField] private GameObject timerPanelCompetitive;    // Competitivo
    [SerializeField] private TextMeshProUGUI timerTextDefault;    // Label del panel default
    [SerializeField] private TextMeshProUGUI timerTextCompetitive;// Label del panel competitivo

    [Header("Reto Loader")]
    [SerializeField] private RetoLoader retoLoader;

    [Header("Ranking Display")]
    public GameObject highScorePanel;
    public Transform highScoreFocusPoint;
    public float rankingDisplayDuration = 3f;
    [SerializeField] private CameraBlink cameraBlink;

    [Header("Timer")]
    [SerializeField] private TimerDef timerDef;
    private bool extraTimeGiven = false;

    [Header("UI Elements")]
    public TMP_InputField nameInput;
    public Button easyButton, normalButton, competitiveButton;
    public Button playButton;
    public Button startGameButton;
    public TextMeshProUGUI gameOverMessage;
    public Button retryButton;

    [Header("Gameplay Objects")]
    [SerializeField] public List<GameObject> teleportHotspots;
    public Color helpColor = Color.green;
    private Vector3 playerStartPos;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip successClip;
    [SerializeField] private float successDisplayDuration = 2f;

    [Header("Audio (Suspenso)")]
    [SerializeField] private AudioSource suspenseSource;
    [SerializeField] private AudioClip suspenseClip;
    [SerializeField, Range(0f, 1f)] private float suspenseVolume = 0.6f;
    private bool suspenseActive = false;
    private Coroutine suspenseGuardRoutine;

    [Header("Locomotion / Teleport")]
    [SerializeField] private GameObject locomotionControllerRoot;

    [Tooltip("componentes a activar/desactivar junto con el teleport")]
    [SerializeField] private List<MonoBehaviour> teleportScriptsToToggle = new List<MonoBehaviour>();

    [Header("Animación AURA")]
    [SerializeField, Tooltip("Animator del robot AURA para activar aplausos")]
    private Animator auraAnimator;
    private const string CLAPPING_TRIGGER = "clapping";

    [Header("Animators a controlar")]
    [SerializeField] private List<Animator> animatorsToControl = new List<Animator>();

    [Tooltip("se reproducirá el estado por defecto del Animator.")]
    [SerializeField] private string stateToPlayOnSuccess = "";

    [Header("Escenas")]
    [SerializeField] private string tutorialSceneName = "TutorialScene";
    [SerializeField] private string registrationSceneName = "RegistrationScene";
    [SerializeField] private Difficulty difficulty = Difficulty.Easy;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureSuspenseSource();
    }

    private void Start()
    {
        var player = GameObject.FindWithTag("Player");
        if (player != null) playerStartPos = player.transform.position;

        if (initialPanel) initialPanel.SetActive(true);
        if (registrationPanel) registrationPanel.SetActive(false);
        if (instructionsPanel) instructionsPanel.SetActive(true);
        if (codePanel) codePanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (statsRankingPanel) statsRankingPanel.SetActive(false);
        if (highScorePanel) highScorePanel.SetActive(true);
        if (timerPanelDefault) timerPanelDefault.SetActive(false);
        if (timerPanelCompetitive) timerPanelCompetitive.SetActive(false);

        // Autostart si venimos de RegistrationScene
        if (PlayerPrefs.GetInt("PendingAutostart", 0) == 1)
        {
            PlayerPrefs.SetInt("PendingAutostart", 0);
            PlayerPrefs.Save();
            StartGameFromRegistration();
        }

        SetTeleportEnabled(false);
        FreezeModelAnimators();

        if (startGameButton) startGameButton.onClick.AddListener(OnStartButtonClicked);

        difficulty = (Difficulty)PlayerPrefs.GetInt("difficulty", (int)Difficulty.Easy);
        if (easyButton) easyButton.onClick.AddListener(() => SelectDifficulty(Difficulty.Easy));
        if (normalButton) normalButton.onClick.AddListener(() => SelectDifficulty(Difficulty.Normal));
        if (competitiveButton) competitiveButton.onClick.AddListener(() => SelectDifficulty(Difficulty.Competitive));
        if (playButton) playButton.onClick.AddListener(OnPlayClicked);
        if (retryButton) retryButton.onClick.AddListener(OnRetryClicked);

        CodeManager.OnCodeSuccessEvent += OnCodeSuccess;

        if (timerDef) timerDef.OnTimerFinished.AddListener(OnTimerFinished);
        else Debug.LogWarning("GameController: timerDef no asignado.");
    }

    private void OnDestroy()
    {
        CodeManager.OnCodeSuccessEvent -= OnCodeSuccess;
        if (timerDef) timerDef.OnTimerFinished.RemoveListener(OnTimerFinished);
    }

    public Difficulty GetCurrentDifficulty() => difficulty;

    public void OnTimerFinished()
    {
        StopSuspenseBed();
        timerDef?.StopTimer();
        var player = GameObject.FindWithTag("Player");
        if (player != null) player.transform.position = playerStartPos;
        if (gameOverPanel) gameOverPanel.SetActive(true);
        if (gameOverMessage) gameOverMessage.text = "¡Se acabó el tiempo!";
        SetTeleportEnabled(false);
        FreezeModelAnimators();
    }

    private void StartGameFromRegistration()
    {
        string playerName = PlayerPrefs.GetString("PlayerName", "").Trim();
        if (string.IsNullOrEmpty(playerName))
        {
            Debug.LogWarning("No hay PlayerName en PlayerPrefs. No se puede autoiniciar.");
            return;
        }

        PlayerDataManager.Instance.CreateOrSelectPlayer(playerName);
        difficulty = (Difficulty)PlayerPrefs.GetInt("difficulty", (int)Difficulty.Easy);

        SetTeleportEnabled(true);
        FreezeModelAnimators();

        if (registrationPanel) registrationPanel.SetActive(false);
        if (instructionsPanel) instructionsPanel.SetActive(false);
        if (highScorePanel) highScorePanel.SetActive(false);
        if (codePanel) codePanel.SetActive(true);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (timerPanelDefault) timerPanelDefault.SetActive(false);
        if (timerPanelCompetitive) timerPanelCompetitive.SetActive(false);

        ApplyHotspotHelp(difficulty == Difficulty.Easy);
        extraTimeGiven = false;

        switch (difficulty)
        {
            case Difficulty.Easy:
                if (teleportHotspots != null)
                    foreach (var h in teleportHotspots) if (h) h.SetActive(true);
                if (timerPanelDefault) timerPanelDefault.SetActive(true);
                if (timerDef && timerTextDefault) timerDef.BindLabel(timerTextDefault);
                if (timerDef)
                {
                    timerDef.SetUrgentColorsEnabled(true);
                    timerDef.SetColorOverride(false, Color.white);
                    timerDef.SetTimerMode(TimerDef.TimerMode.CountUp);
                }
                break;

            case Difficulty.Normal:
                if (teleportHotspots != null)
                    foreach (var h in teleportHotspots) if (h) h.SetActive(false);
                if (timerPanelDefault) timerPanelDefault.SetActive(true);
                if (timerDef && timerTextDefault) timerDef.BindLabel(timerTextDefault);
                if (timerDef)
                {
                    timerDef.SetUrgentColorsEnabled(true);
                    timerDef.SetColorOverride(false, Color.white);
                    timerDef.SetTimerMode(TimerDef.TimerMode.CountUp);
                }
                break;

            case Difficulty.Competitive:
                if (timerPanelCompetitive) timerPanelCompetitive.SetActive(true);
                if (timerDef && timerTextCompetitive) timerDef.BindLabel(timerTextCompetitive);
                if (timerDef)
                {
                    float firstTime = GetFirstPlaceTimeOrDefault(60f);
                    timerDef.SetUrgentColorsEnabled(false);
                    timerDef.SetColorOverride(true, Color.white);
                    timerDef.SetTimerMode(TimerDef.TimerMode.CountDown);
                    timerDef.SetCountdownTime(firstTime);
                }
                break;
        }

        if (timerDef) timerDef.ResetTimer();

        if (retoLoader == null) retoLoader = FindObjectOfType<RetoLoader>();
        if (retoLoader != null)
        {
            
            retoLoader.ConfigureModeByDifficulty(difficulty);
            retoLoader.PrepareForNewSession();
        }
        else
        {
            Debug.LogError("GameController: RetoLoader no asignado ni encontrado en escena.");
        }

        var cm = FindObjectOfType<CodeManager>();
        cm?.BeginSession(shuffle: false);

        StartSuspenseBed();
    }

    public void OnStartButtonClicked()
    {
        if (initialPanel) initialPanel.SetActive(false);
        if (registrationPanel) registrationPanel.SetActive(true);
        if (instructionsPanel) instructionsPanel.SetActive(true);
        if (highScorePanel) highScorePanel.SetActive(true);
        SetTeleportEnabled(false);
        FreezeModelAnimators();
    }

    public void SelectDifficulty(Difficulty d)
    {
        difficulty = d;
        PlayerPrefs.SetInt("difficulty", (int)d);
    }

    public void OnPlayClicked()
    {
        if (nameInput == null)
        {
            Debug.LogWarning("nameInput no asignado en esta escena.");
            return;
        }

        string playerName = nameInput.text.Trim();
        if (string.IsNullOrEmpty(playerName))
        {
            Debug.LogWarning("Debes ingresar un nombre de jugador.");
            return;
        }

        PlayerPrefs.SetString("PlayerName", playerName);
        PlayerDataManager.Instance.CreateOrSelectPlayer(playerName);

        SetTeleportEnabled(true);
        FreezeModelAnimators();

        if (registrationPanel) registrationPanel.SetActive(false);
        if (instructionsPanel) instructionsPanel.SetActive(false);
        if (highScorePanel) highScorePanel.SetActive(false);
        if (codePanel) codePanel.SetActive(true);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (timerPanelDefault) timerPanelDefault.SetActive(false);
        if (timerPanelCompetitive) timerPanelCompetitive.SetActive(false);

        ApplyHotspotHelp(difficulty == Difficulty.Easy);
        extraTimeGiven = false;

        switch (difficulty)
        {
            case Difficulty.Easy:
                if (teleportHotspots != null)
                    foreach (var hotspot in teleportHotspots) if (hotspot) hotspot.SetActive(true);
                if (timerPanelDefault) timerPanelDefault.SetActive(true);
                if (timerDef && timerTextDefault) timerDef.BindLabel(timerTextDefault);
                if (timerDef)
                {
                    timerDef.SetUrgentColorsEnabled(true);
                    timerDef.SetColorOverride(false, Color.white);
                    timerDef.SetTimerMode(TimerDef.TimerMode.CountUp);
                }
                break;

            case Difficulty.Normal:
                if (teleportHotspots != null)
                    foreach (var hotspot in teleportHotspots) if (hotspot) hotspot.SetActive(false);
                if (timerPanelDefault) timerPanelDefault.SetActive(true);
                if (timerDef && timerTextDefault) timerDef.BindLabel(timerTextDefault);
                if (timerDef)
                {
                    timerDef.SetUrgentColorsEnabled(true);
                    timerDef.SetColorOverride(false, Color.white);
                    timerDef.SetTimerMode(TimerDef.TimerMode.CountUp);
                }
                break;

            case Difficulty.Competitive:
                if (timerPanelCompetitive) timerPanelCompetitive.SetActive(true);
                if (timerDef && timerTextCompetitive) timerDef.BindLabel(timerTextCompetitive);
                if (timerDef)
                {
                    float firstTime = GetFirstPlaceTimeOrDefault(60f);
                    timerDef.SetUrgentColorsEnabled(false);
                    timerDef.SetColorOverride(true, Color.white);
                    timerDef.SetTimerMode(TimerDef.TimerMode.CountDown);
                    timerDef.SetCountdownTime(firstTime);
                }
                break;
        }

        if (timerDef) timerDef.ResetTimer();

        if (retoLoader == null) retoLoader = FindObjectOfType<RetoLoader>();
        if (retoLoader != null)
        {
            
            retoLoader.ConfigureModeByDifficulty(difficulty);
            retoLoader.PrepareForNewSession();
        }
        else
        {
            Debug.LogError("GameController: RetoLoader no asignado ni encontrado en escena.");
        }

        var cm = FindObjectOfType<CodeManager>();
        cm?.BeginSession(shuffle: false);

        StartSuspenseBed();
    }

    public void OnCodeSuccess(float elapsedTimeParam)
    {
        AuraTriggerClapping();
        PlayModelAnimatorsFromStart();
        StopSuspenseBed();
        timerDef?.StopTimer();

        float elapsedTime = timerDef ? timerDef.GetTimeForStats() : elapsedTimeParam;

        if (audioSource != null && successClip != null)
            audioSource.PlayOneShot(successClip);

        PlayerDataManager.Instance.UpdateCurrentSessionStats(
            elapsedTime, $"Partida {DateTime.Now:HH:mm:ss}");

        if (highScoreTable) highScoreTable.RefreshTable();

        StartCoroutine(ShowStatsAndReturnToRegister(elapsedTime));
    }

    private IEnumerator ShowStatsAndReturnToRegister(float elapsedTime)
    {
        if (statsRankingPanel) statsRankingPanel.SetActive(true);

        var stats = statsRankingPanel ? statsRankingPanel.GetComponentInChildren<GameStatistics>(true) : null;
        if (stats == null)
        {
            Debug.LogError("GameController: GameStatistics no encontrado en StatsRankingPanel ni en sus hijos.");
        }
        else
        {
            stats.ShowEndGameStatistics(PlayerPrefs.GetString("PlayerName"), elapsedTime);
            Debug.Log($"[Stats] Tiempo mostrado: {elapsedTime:F2} -> {TimerDef.FormatMMSS(elapsedTime)}");
        }

        yield return new WaitForSeconds(rankingDisplayDuration);

        if (cameraBlink != null)
            yield return cameraBlink.DoFadeIn();

        var player = GameObject.FindWithTag("Player");


        if (statsRankingPanel)
            statsRankingPanel.SetActive(false);

        if (retoLoader != null && difficulty == Difficulty.Competitive)
        {
            bool avanzado = retoLoader.LoadNextReto();
            retoLoader.UpdatePistasUI();
            if (!avanzado)
            {
                retoLoader.ResetSequence(shuffle: false);
                retoLoader.UpdatePistasUI();
            }
        }

        if (cameraBlink != null)
            yield return cameraBlink.DoFadeOut();
        var currentName = PlayerDataManager.Instance.CurrentPlayerName ?? "";
        PlayerPrefs.SetString("PlayerName", currentName);
        PlayerPrefs.SetInt("PendingAutostart", 0);
        //PlayerPrefs.Save();

        PlayerPrefs.SetInt("ShowRankingOnReturn", 1);
        PlayerPrefs.Save();

        SceneLoader.LoadRegistration();
    }


    public void TriggerGameOver()
    {
        StopSuspenseBed();
        timerDef?.StopTimer();
        var player = GameObject.FindWithTag("Player");
        if (player != null) player.transform.position = playerStartPos;
        if (gameOverPanel) gameOverPanel.SetActive(true);
        if (gameOverMessage) gameOverMessage.text = "¡Se acabó el tiempo!";
        SetTeleportEnabled(false);
        FreezeModelAnimators();
    }

    private float CalculateExtraTime()
    {
        var r = PlayerDataManager.Instance.GetRanking();
        if (r.Count >= 2)
            return Mathf.Max(0f, r[1].BestTime - r[0].BestTime);
        return 0f;
    }

    private float GetFirstPlaceTimeOrDefault(float @default)
    {
        var r = PlayerDataManager.Instance.GetRanking();
        return (r.Count > 0) ? r[0].BestTime : @default;
    }

    public void OnRetryClicked()
    {
        StopSuspenseBed();
        if (timerDef) timerDef.InitializeTimer();
        SceneLoader.LoadRegistration();
    }

    public void ResetSession()
    {
        var cm = FindObjectOfType<CodeManager>();
        if (cm != null) cm.ResetSession();

        if (timerDef) timerDef.InitializeTimer();

        if (codePanel) codePanel.SetActive(true);
        if (timerPanelDefault) timerPanelDefault.SetActive(false);
        if (timerPanelCompetitive) timerPanelCompetitive.SetActive(false);

        switch (difficulty)
        {
            case Difficulty.Easy:
            case Difficulty.Normal:
                if (timerPanelDefault) timerPanelDefault.SetActive(true);
                if (timerDef && timerTextDefault) timerDef.BindLabel(timerTextDefault);
                if (timerDef)
                {
                    timerDef.SetUrgentColorsEnabled(true);
                    timerDef.SetColorOverride(false, Color.white);
                    timerDef.SetTimerMode(TimerDef.TimerMode.CountUp);
                }
                break;

            case Difficulty.Competitive:
                if (timerPanelCompetitive) timerPanelCompetitive.SetActive(true);
                if (timerDef && timerTextCompetitive) timerDef.BindLabel(timerTextCompetitive);
                if (timerDef)
                {
                    timerDef.SetUrgentColorsEnabled(false);
                    timerDef.SetColorOverride(true, Color.white);
                    timerDef.SetTimerMode(TimerDef.TimerMode.CountDown);
                }
                break;
        }
        if (statsRankingPanel) statsRankingPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);

        ApplyHotspotHelp(difficulty == Difficulty.Easy);

        if (registrationPanel) registrationPanel.SetActive(false);
        if (instructionsPanel) instructionsPanel.SetActive(false);

        if (nameInput) nameInput.text = "";

        var player = GameObject.FindWithTag("Player");
        if (player != null) player.transform.position = playerStartPos;
    }

    public void ApplyHotspotHelp(bool show)
    {
        if (teleportHotspots == null) return;
        foreach (var go in teleportHotspots)
        {
            if (!go) continue;
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.material.color = show ? helpColor : Color.white;
        }
    }

    private void AuraTriggerClapping()
    {
        if (auraAnimator != null)
        {
            auraAnimator.SetTrigger(CLAPPING_TRIGGER);
        }
        else
        {
            Debug.LogWarning("Aura Animator no asignado en GameController. No se puede activar Clapping.");
        }
    }

    private void EnsureSuspenseSource()
    {
        if (suspenseSource == null)
        {
            suspenseSource = gameObject.AddComponent<AudioSource>();
            suspenseSource.playOnAwake = false;
            suspenseSource.loop = true;
            suspenseSource.spatialBlend = 0f;
            suspenseSource.volume = suspenseVolume;
            suspenseSource.dopplerLevel = 0f;
        }
    }

    private void StartSuspenseBed()
    {
        if (suspenseClip == null) return;
        EnsureSuspenseSource();

        suspenseSource.clip = suspenseClip;
        suspenseSource.loop = true;
        suspenseSource.spatialBlend = 0f;
        suspenseSource.ignoreListenerPause = true;
        if (suspenseSource.isPlaying) suspenseSource.Stop();
        suspenseSource.Play();
        suspenseActive = true;
        if (suspenseGuardRoutine == null)
            suspenseGuardRoutine = StartCoroutine(SuspenseGuard());
    }

    private void StopSuspenseBed()
    {
        suspenseActive = false;

        if (suspenseGuardRoutine != null)
        {
            StopCoroutine(suspenseGuardRoutine);
            suspenseGuardRoutine = null;
        }

        if (suspenseSource != null && suspenseSource.isPlaying)
            suspenseSource.Stop();
    }

    private IEnumerator SuspenseGuard()
    {
        while (suspenseActive)
        {
            if (suspenseSource != null && suspenseClip != null)
            {
                if (!suspenseSource.isPlaying)
                {
                    suspenseSource.loop = true;
                    suspenseSource.Play();
                }
            }
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void SetTeleportEnabled(bool enabled)
    {
        if (locomotionControllerRoot)
            locomotionControllerRoot.SetActive(enabled);

        if (teleportScriptsToToggle != null)
        {
            foreach (var s in teleportScriptsToToggle)
            {
                if (s) s.enabled = enabled;
            }
        }
    }

    private void FreezeModelAnimators()
    {
        foreach (var a in animatorsToControl)
        {
            if (!a) continue;
            a.speed = 0f;
            a.Rebind();
            a.Update(0f);
        }
    }

    private void PlayModelAnimatorsFromStart()
    {
        foreach (var a in animatorsToControl)
        {
            if (!a) continue;
            if (!string.IsNullOrEmpty(stateToPlayOnSuccess))
                a.Play(stateToPlayOnSuccess, 0, 0f);
            a.speed = 1f;
        }
    }

    public void ReturnToTutorialScene()
    {
        StopSuspenseBed();
        SetTeleportEnabled(false);
        SceneManager.LoadScene(tutorialSceneName, LoadSceneMode.Single);
    }
}
