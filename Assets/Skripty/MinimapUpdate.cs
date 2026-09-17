using UnityEngine;

public class MinimapUpdate : MonoBehaviour
{
    private int frameCount = 0;
    public int updateFrequency = 60; // Update every 60 frames (1 second at 60 FPS)
    private Camera minimapCamera;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        minimapCamera = GetComponent<Camera>();
    }

    // Update is called once per frame
    void Update()
    {

        frameCount++;
        if (frameCount >= updateFrequency)
        {
            minimapCamera.enabled = true; // Enable the minimap camera to render
            minimapCamera.Render(); // Render the minimap camera to update the minimap texture
            // Perform minimap update logic here
            frameCount = 0;
            minimapCamera.enabled = false; // Disable the minimap camera after rendering
        }
    }
}
