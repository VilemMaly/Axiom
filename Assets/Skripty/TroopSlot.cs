using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class TroopSlot : MonoBehaviour
{
    public Image icon;
    public bool hasTroop = false;
    public int SlotIndex;
    public void SetIcon(Sprite newIcon)
    {
        icon.sprite = newIcon;
        hasTroop = true;
    }
    void Start()
    {
        if (icon == null)
            icon = GetComponentsInChildren<Image>().FirstOrDefault(c => c.gameObject != gameObject);
        hasTroop = false;
    }
    

}
