using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class HUDManager : MonoBehaviour
{
    [SerializeField] private GameObject hudUI;
    [SerializeField] private GameObject deviceUI;
    [SerializeField] private GameObject gameOverUI;
    [SerializeField] private Slider waveMeterSlider;
    [SerializeField] private TMP_Text waveNumberText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private Slider mainTowerHealthSlider;

    private float _time;
    private Coroutine _timerCoroutine;
    
    public static HUDManager Instance;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    // Show Tower Shop UI
    public void ShowDeviceUI()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        hudUI.SetActive(false);
        deviceUI.SetActive(true);
    }

    // Hide Tower Shop UI
    public void HideDeviceUI()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        hudUI.SetActive(true);
        deviceUI.SetActive(false);
    }

    // Updates Wave Slider with enemy spawns
    public void UpdateWaveMeterSlider(int amount, int max)
    {
        waveMeterSlider.maxValue = max;
        waveMeterSlider.value = amount;
    }

    // Updates Current wave
    public void UpdateWaveNumberText(int amount)
    {
        waveNumberText.text = "WAVE " + amount;
    }

    public void StartTimer(float time)
    {
        if (_timerCoroutine != null)
            StopCoroutine(_timerCoroutine);

        _timerCoroutine = StartCoroutine(Timer(time));
    }

    public void ShowWaveInProgress()
    {
        if (_timerCoroutine != null)
        {
            StopCoroutine(_timerCoroutine);
            _timerCoroutine = null;
        }

        timerText.text = "UNDER ATTACK";
    }

    private IEnumerator Timer(float time)
    {
        _time = time;
    
        while (_time > 0)
        {
            timerText.text = "NEXT WAVE\n" + FormatTime(_time);
            yield return new WaitForEndOfFrame();
            _time -= Time.deltaTime;
        }
    
        timerText.text = "00:00";
        _timerCoroutine = null;
    }

    private string FormatTime(float time)
    {
        int totalSeconds = Mathf.CeilToInt(time);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    public void ShowGameOverUI()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        hudUI.SetActive(false);
        deviceUI.SetActive(false);
        gameOverUI.SetActive(true);
    }

    public void MainMenu()
    {
        SceneManager.LoadScene(0);
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // Updates slider for main tower health
    public void UpdateHealthSlider(int amount, int max)
    {
        mainTowerHealthSlider.maxValue = max;
        mainTowerHealthSlider.value = amount;
    }
}
