using UnityEngine;

public class GroupButtonClicked : MonoBehaviour
{
    public TroopUIController uiController;
    public bool isFirstGroupButton = false;
    public void OnGroupButtonClicked()
    {
        TroopGroup troopGroup = GetComponent<TroopGroup>();
        if (uiController != null)
        {
            uiController.SetSelectedTroopGroup(troopGroup);
        }
    }
    public void Start()
    {
        TroopGroup troopGroup = GetComponent<TroopGroup>();
        if (uiController != null)
            uiController.RegisterTroopGroup(troopGroup);

        if (isFirstGroupButton)
        {
            OnGroupButtonClicked();
        }
    }
}
