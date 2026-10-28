using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class TowerBuyButton : MonoBehaviour
{
    [SerializeField] private TowerData tower;
    [SerializeField] private TMP_Text nameText;   // Optional
    [SerializeField] private TMP_Text costText;   // Optional
    [SerializeField] private Image iconImage;     // Optional

    [SerializeField] private Color affordableColor = Color.white;
    [SerializeField] private Color missingColor = new Color(1f, 0.33f, 0.33f);

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(() => TowerDevice.Instance.BuyTower(tower));

        if (nameText != null) nameText.text = tower.displayName;
        if (iconImage != null && tower.icon != null) iconImage.sprite = tower.icon;
    }

    // Runs every time the shop opens, so the labels reflect current resources
    private void OnEnable()
    {
        if (ResourceManager.Instance == null || tower == null) return;

        _button.interactable = ResourceManager.Instance.HasEnoughResources(tower.costs);
        RefreshCostText();
    }

    // One line per cost; resources the player is short on are shown in red
    private void RefreshCostText()
    {
        if (costText == null) return;

        StringBuilder sb = new StringBuilder();

        foreach (ResourceManager.ResourceCost cost in tower.costs)
        {
            bool enough = ResourceManager.Instance.HasEnough(cost.resourceType, cost.amount);
            string hex = ColorUtility.ToHtmlStringRGB(enough ? affordableColor : missingColor);

            sb.AppendLine($"<color=#{hex}>{cost.amount} {ResourceManager.GetResourceName(cost.resourceType)}</color>");
        }

        costText.text = sb.ToString().TrimEnd();
    }
}