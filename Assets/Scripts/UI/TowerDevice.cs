using TMPro;
using UnityEngine;

// Handles ordering new towers
public class TowerDevice : MonoBehaviour
{
    [SerializeField] private GameObject deviceUI;
    [SerializeField] public GameObject deviceObject;
    [SerializeField] private KeyCode deviceKey;

    [SerializeField] public TMP_Text ironAmount;
    [SerializeField] public TMP_Text copperAmount;
    [SerializeField] public TMP_Text pearlAmount;
    [SerializeField] public TMP_Text woodAmount;
    [SerializeField] public TMP_Text goldAmount;

    public PlayerMovement playerMovement;

    public static TowerDevice Instance;
    public bool IsActive { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this); // Destroy only this component, not the whole GameObject
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        deviceUI.SetActive(false);
    }

    private void Update()
    {
        if (!Input.GetKeyDown(deviceKey)) return;

        // Can't open the shop while placing a tower or upgrading one
        if (TowerBuilder.Instance.IsBuilding) return;
        if (TowerUpgradeMenu.Instance != null && TowerUpgradeMenu.Instance.IsOpen) return;

        if (IsActive) HideUI();
        else ShowUI();
    }

    private void ShowUI()
    {
        IsActive = true;
        deviceObject.SetActive(true);
        HUDManager.Instance.ShowDeviceUI();
        playerMovement.canMove = false;
        UpdateResourceText();
    }

    private void HideUI()
    {
        IsActive = false;
        deviceObject.SetActive(false);
        HUDManager.Instance.HideDeviceUI();
        playerMovement.canMove = true;
    }

    private void UpdateResourceText()
    {
        ironAmount.text = "Iron: " + ResourceManager.Instance.ironOre;
        copperAmount.text = "Copper: " + ResourceManager.Instance.copperOre;
        pearlAmount.text = "Pearls: " + ResourceManager.Instance.pearls;
        woodAmount.text = "Wood: " + ResourceManager.Instance.wood;
        goldAmount.text = "Gold: " + ResourceManager.Instance.gold;
    }

    // Checks affordability and starts placement. Resources are only spent once the tower is placed.
    public void BuyTower(TowerData tower)
    {
        if (tower == null || !ResourceManager.Instance.HasEnoughResources(tower.costs))
            return;

        HideUI();
        TowerBuilder.Instance.EnterBuildMode(tower);
    }
}