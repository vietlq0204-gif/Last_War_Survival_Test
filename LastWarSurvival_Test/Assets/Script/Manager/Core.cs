using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Core : CoreEventBase
{
    [Header("Scene UI")]
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private GameObject startPanel;
    [SerializeField] private Button startButton;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private Button restartButton;

    private float _defaultTimeScale = 1f;
    private float _defaultFixedDeltaTime = 0.02f;
    private bool _gameStarted;
    private bool _gameOver;

    protected override void Awake()
    {
        base.Awake();
        _defaultTimeScale = Mathf.Approximately(Time.timeScale, 0f) ? 1f : Time.timeScale;
        _defaultFixedDeltaTime = Mathf.Approximately(Time.fixedDeltaTime, 0f) ? 0.02f : Time.fixedDeltaTime;
    }

    private void Start()
    {
        BindButtons();
        ValidateSceneUi();
        ShowStartScreen();
        SetPauseState(isPaused: true);
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            return;

        SetPauseState(isPaused: false);
    }

    public override void SubscribeEvents()
    {
        CoreEvents.gameOver.Subscribe(HandleGameOverEvent, Binder);
    }

    private void HandleGameOverEvent(GameOverEvent gameOverEvent)
    {
        if (gameOverEvent == null || !gameOverEvent.IsGameOver || !_gameStarted || _gameOver)
            return;

        _gameOver = true;
        ShowGameOverScreen();
        SetPauseState(isPaused: true);
    }

    private void StartGame()
    {
        if (_gameStarted && !_gameOver)
            return;

        _gameStarted = true;
        _gameOver = false;
        HideAllScreens();
        SetPauseState(isPaused: false);
        CoreEvents.gameStart.Raise(new GameStartEvent
        {
            IsStarted = true
        });
    }

    private void RestartGame()
    {
        SetPauseState(isPaused: false);
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex);
    }

    private void BindButtons()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartGame);
            startButton.onClick.AddListener(StartGame);
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartGame);
            restartButton.onClick.AddListener(RestartGame);
        }
    }

    private void ValidateSceneUi()
    {
        if (uiCanvas == null)
            Debug.LogError($"'{name}' is missing uiCanvas in the scene.", this);

        if (startPanel == null)
            Debug.LogError($"'{name}' is missing startPanel in the scene.", this);

        if (startButton == null)
            Debug.LogError($"'{name}' is missing startButton in the scene.", this);

        if (gameOverPanel == null)
            Debug.LogError($"'{name}' is missing gameOverPanel in the scene.", this);

        if (restartButton == null)
            Debug.LogError($"'{name}' is missing restartButton in the scene.", this);
    }

    private void ShowStartScreen()
    {
        _gameStarted = false;
        _gameOver = false;

        if (startPanel != null)
            startPanel.SetActive(true);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (startButton != null)
            startButton.interactable = true;
    }

    private void ShowGameOverScreen()
    {
        if (startPanel != null)
            startPanel.SetActive(false);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        if (restartButton != null)
            restartButton.interactable = true;
    }

    private void HideAllScreens()
    {
        if (startPanel != null)
            startPanel.SetActive(false);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    private void SetPauseState(bool isPaused)
    {
        Time.timeScale = isPaused ? 0f : _defaultTimeScale;
        Time.fixedDeltaTime = isPaused ? 0f : _defaultFixedDeltaTime;
    }
}
