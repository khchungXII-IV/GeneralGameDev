using UnityEngine;



public class CAMERACONTROLS : MonoBehaviour
// Code for 2D platformer/sidescroller camera controls. 
// Implements the following features:
// - Smooth camera follow after passing screen deadzones
{
    //---------------------------------------------------------------------------------------------------
    // REFERENCES
    //---------------------------------------------------------------------------------------------------

    // Camera follow
    public Transform target;
    public Vector3 offset = new Vector3(0f, 0f, -10f);
    public Vector2 deadzone = new Vector2(2f, 1f);
    public float smoothing = 0.1f;
    private Vector3 velocity = Vector3.zero;
    private Vector3 newCamPos = Vector3.zero;



    //---------------------------------------------------------------------------------------------------
    // UNITY DEFAULT METHODS
    //---------------------------------------------------------------------------------------------------

    void LateUpdate()
    // Called at constant freq after FixedUpate
    {
        if (target == null) return;

        camFollow();

        transform.position = Vector3.SmoothDamp(transform.position, newCamPos, ref velocity, smoothing);
    }



    //---------------------------------------------------------------------------------------------------
    // CORE LOGIC
    //---------------------------------------------------------------------------------------------------
    void camFollow()
    // Calculates the needed camera position to follow the player once they pass screen deadzones
    {
        Vector3 currPos = transform.position;
        Vector3 newPos = target.position + offset;

        float xDiff = newPos.x - currPos.x;
        if (Mathf.Abs(xDiff) > deadzone.x) currPos.x = newPos.x - Mathf.Sign(xDiff) * deadzone.x;

        float yDiff = newPos.y - currPos.y;
        if (Mathf.Abs(yDiff) > deadzone.y) currPos.y = newPos.y - Mathf.Sign(yDiff) * deadzone.y;

        newCamPos = currPos;
    }
}