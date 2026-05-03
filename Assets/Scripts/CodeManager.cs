using System.Linq;
using UnityEngine;
using TMPro;
using System;
using System.Collections;

public class CodeManager : MonoBehaviour
{
    [Header("Carga de la pista")]
    [SerializeField] private RetoLoader retoLoader;

    [Header("Timer")]
    [SerializeField] private TimerDef timerDef;

    private string respuestaActual;

    [Header("UI de dígitos (input del jugador)")]
    [SerializeField] private TMP_Text[] digitTexts; 
    private int[] digitValues = new int[3];

    [Header("Feedback")]
    [SerializeField] private GameObject panelCorrecto; 
    [SerializeField] private GameObject panelCorrecto1;
    [SerializeField] private GameObject panelCerca;    
    [SerializeField] private GameObject panelClave;    
    [SerializeField] private TextMeshProUGUI txtPosiciones; 
    [SerializeField] private TextMeshProUGUI txtWrongPos;   

    [Header("Timing")]
    [Tooltip("Tiempo que se muestra el feedback 'correcto' ANTES de mostrar estadísticas y volver a registro.")]
    [SerializeField] private float perRetoFeedbackDelay = 1.2f;

    public static event Action<float> OnCodeSuccessEvent;

    private float sessionStartTime;  // inicio de la partida (un reto)
    private float retoStartTime;     // alias por si quieres métricas por reto

    private void Start()
    {
        ResetVisualsOnly();
    }

    /// <summary>
    /// Llamado desde GameController al presionar Play para arrancar la partida (un reto).
    /// </summary>
    public void BeginSession(bool shuffle = false)
    {
        if (retoLoader == null)
        {
            Debug.LogError("CodeManager: RetoLoader no asignado.");
            return;
        }

        if (timerDef == null) timerDef = FindObjectOfType<TimerDef>();

        // RetoLoader ya fijó el reto actual (1 / aleatorio / secuencial).
        retoLoader.UpdatePistasUI();
        CargarRespuestaActual();

        ResetDigits();
        panelCorrecto?.SetActive(false);
        panelCorrecto1?.SetActive(false);
        panelCerca?.SetActive(false);
        panelClave?.SetActive(true);

        sessionStartTime = Time.time;
        retoStartTime = Time.time;
    }
    public void ResetSession()
    {
        ResetVisualsOnly();
    }

    private void ResetVisualsOnly()
    {
        for (int i = 0; i < digitValues.Length; i++) digitValues[i] = 0;
        UpdateDisplay();

        if (panelCorrecto) panelCorrecto.SetActive(false);
        if (panelCorrecto1) panelCorrecto1.SetActive(false);
        if (panelCerca)    panelCerca.SetActive(false);
        if (panelClave)    panelClave.SetActive(true);
    }

    private void ResetDigits()
    {
        for (int i = 0; i < digitValues.Length; i++) digitValues[i] = 0;
        UpdateDisplay();
    }

    private void CargarRespuestaActual()
    {
        var reto = retoLoader?.GetCurrentReto();
        respuestaActual = reto?.respuesta;

        if (string.IsNullOrEmpty(respuestaActual))
            Debug.LogWarning("Respuesta del reto actual no inicializada.");
    }

    public void IncreaseDigit(int index)
    {
        if (panelCorrecto) panelCorrecto.SetActive(false);
        if(panelCorrecto1) panelCorrecto1.SetActive(false);
        if (panelCerca)    panelCerca.SetActive(false);
        if (panelClave)    panelClave.SetActive(true);

        digitValues[index] = (digitValues[index] + 1) % 10;
        UpdateDisplay();
    }

    public void DecreaseDigit(int index)
    {
        if (panelCorrecto) panelCorrecto.SetActive(false);
        if(panelCorrecto1) panelCorrecto1.SetActive(false);
        if (panelCerca)    panelCerca.SetActive(false);
        if (panelClave)    panelClave.SetActive(true);

        digitValues[index] = (digitValues[index] + 9) % 10;
        UpdateDisplay();
    }

    public void OnClear()
    {
        if (panelCorrecto) panelCorrecto.SetActive(false);
        if(panelCorrecto1) panelCorrecto1.SetActive(false);
        if (panelCerca)    panelCerca.SetActive(false);
        if (panelClave)    panelClave.SetActive(true);

        ResetDigits();
    }

    public void OnValidate()
    {
        if (string.IsNullOrEmpty(respuestaActual))
        {
            CargarRespuestaActual();
            if (string.IsNullOrEmpty(respuestaActual))
            {
                Debug.LogWarning("No se puede validar: respuesta no está inicializada.");
                return;
            }
        }
        if (panelCorrecto == null || panelCerca == null || panelClave == null)
        {
            Debug.LogWarning("Asigna los paneles de feedback en el inspector.");
            return;
        }

        panelCorrecto.SetActive(false);
        panelCorrecto1.SetActive(false);
        panelCerca.SetActive(false);

        string currentInput = string.Concat(digitValues.Select(d => d.ToString()));

        if (currentInput == respuestaActual)
        {
            panelCorrecto.SetActive(true);
            panelCorrecto1.SetActive(true);
            panelClave.SetActive(false);
            float totalElapsed;
            if (timerDef != null)
            {
                timerDef.StopTimer();                         
                totalElapsed = timerDef.GetTimeForStats();    
            }
            else
            {
                totalElapsed = Time.time - sessionStartTime;
            }

            StartCoroutine(NotifySuccessAfterDelay(perRetoFeedbackDelay, totalElapsed));
        }
        else
        {
            int good = 0, wrong = 0;
            bool[] usadoRespuesta = new bool[3];
            bool[] usadoInput = new bool[3];

            // Primero contar posiciones correctas
            for (int i = 0; i < 3; i++)
            {
                if (currentInput[i] == respuestaActual[i])
                {
                    good++;
                    usadoRespuesta[i] = true;
                    usadoInput[i] = true;
                }
            }

            // Luego contar dígitos correctos en posición incorrecta
            for (int i = 0; i < 3; i++)
            {
                if (usadoInput[i]) continue; // ya contado como correcto

                for (int j = 0; j < 3; j++)
                {
                    if (usadoRespuesta[j]) continue; // ya usado
                    if (currentInput[i] == respuestaActual[j])
                    {
                        wrong++;
                        usadoRespuesta[j] = true;
                        break;
                    }
                }
            }
            if (txtPosiciones != null) txtPosiciones.SetText("{0}", good);
            if (txtWrongPos != null)   txtWrongPos.SetText("{0}", wrong);

            panelCerca.SetActive(true);
            panelClave.SetActive(false);
        }
    }

    private IEnumerator NotifySuccessAfterDelay(float delay, float totalElapsed)
    {
        yield return new WaitForSeconds(delay);
        OnCodeSuccessEvent?.Invoke(totalElapsed);
        Debug.Log($"Reto completado en {totalElapsed:F2} s. Se mostrarán estadísticas y se volverá al registro.");
    }

    private void UpdateDisplay()
    {
        if (digitTexts == null) return;
        for (int i = 0; i < digitTexts.Length; i++)
            if (digitTexts[i] != null)
                digitTexts[i].text = digitValues[i].ToString();
    }
}
