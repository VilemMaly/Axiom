using UnityEngine;

/// <summary>UI button for a player's troop group, identified by a stable ID.</summary>
public class GroupButtonClicked : MonoBehaviour
{
    [SerializeField, Min(0)] private int groupId;
    [SerializeField] private bool isFirstGroupButton;

    private TroopInteractor troopInteractor;

    private void Awake()
    {
        troopInteractor = GetComponentInParent<TroopInteractor>();
    }

    public void OnGroupButtonClicked()
    {
        if (troopInteractor == null)
            troopInteractor = GetComponentInParent<TroopInteractor>();

        if (troopInteractor != null)
            troopInteractor.RequestActivateGroup(groupId);
        else
            Debug.LogError("GroupButtonClicked could not find the player's TroopInteractor.", this);
    }

    private void Start()
    {
        if (isFirstGroupButton)
            OnGroupButtonClicked();
    }
}
