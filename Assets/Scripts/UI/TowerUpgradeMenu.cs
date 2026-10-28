using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows the upgrades for one tower. Opened by TowerUpgradeInteractor when the player aims at a tower.
public class TowerUpgradeMenu : MonoBehaviour
{
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private TMP_Text titleText;     // "Harpoon"
    [SerializeField] private TMP_Text levelText;     // "Level 2 / 3  -  next level at 6 upgrades"
    [SerializeField] private TMP_Text statsText;     // Health, damage, range, fire rate
    [SerializeField] private Transform rowContainer; // Put a Vertical Layout Group on this
    [SerializeField] private UpgradeRowUI rowPrefab;
    [SerializeField] private Button closeButton;     // Optional

    public static TowerUpgradeMenu Instance;
    public bool IsOpen { get; private set; }
    public Tower CurrentTower => _tower;

    private Tower _tower;
    private readonly List<UpgradeRowUI> _rows = new List<UpgradeRowUI>();

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    private void Start()
    {
        menuPanel.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen) return;

        // Tower was destroyed while the menu was open
        if (_tower == null || Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    public void Open(Tower tower)
    {
        if (tower == null || !tower.CanBeUpgraded) return;

        _tower = tower;
        _tower.OnUpgraded += HandleUpgraded;

        IsOpen = true;
        menuPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (PlayerMovement.Instance != null) PlayerMovement.Instance.canMove = false;

        BuildRows();
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;

        if (_tower != null)
            _tower.OnUpgraded -= HandleUpgraded;

        _tower = null;
        IsOpen = false;
        menuPanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (PlayerMovement.Instance != null) PlayerMovement.Instance.canMove = true;
    }

    // Each tower can have a different number of tracks, so rows are rebuilt on every open
    private void BuildRows()
    {
        foreach (UpgradeRowUI row in _rows)
            Destroy(row.gameObject);
        _rows.Clear();

        for (int i = 0; i < _tower.UpgradeData.tracks.Length; i++)
            _rows.Add(Instantiate(rowPrefab, rowContainer));
    }

    private void Refresh()
    {
        if (_tower == null) return;

        titleText.text = _tower.DisplayName;
        levelText.text = BuildLevelText();
        statsText.text =
            $"Health {_tower.CurrentHealth}/{_tower.MaxHealth}\n" +
            $"Damage {_tower.Damage}\n" +
            $"Range {_tower.Range:0.#}m\n" +
            $"Fire rate {_tower.ShotsPerSecond:0.#}/s";

        for (int i = 0; i < _rows.Count; i++)
        {
            int trackIndex = i; // Captured by the lambda, so it needs its own copy
            _rows[i].Setup(_tower, trackIndex, () => _tower.TryUpgrade(trackIndex));
        }
    }

    private string BuildLevelText()
    {
        string text = $"Level {_tower.Level} / {_tower.MaxLevel}";

        TowerLevelVisual[] visuals = _tower.UpgradeData.levelVisuals;
        if (visuals != null && _tower.Level < _tower.MaxLevel)
            text += $"  -  next level at {visuals[_tower.Level - 1].upgradesRequired} upgrades ({_tower.TotalUpgrades} bought)";

        return text;
    }

    private void HandleUpgraded(Tower tower)
    {
        Refresh();
    }
}
