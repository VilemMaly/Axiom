using UnityEngine;

public class PersistentEventSystem : MonoBehaviour
{
    private static PersistentEventSystem instance;

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject); // už jeden existuje, tenhle zahoď
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }
}