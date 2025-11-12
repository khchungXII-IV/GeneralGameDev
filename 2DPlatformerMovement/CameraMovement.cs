using UnityEngine;

public class CameraMovement : MonoBehaviour
// Code for 2D platformer camera movement. Implements the following features:
// - Smooth camera movement which follows the player after passing dead zones on screen
// - Look ahead shift when player inputs on the right stick
{
    //---------------------------------------------------------------------------------------------------
    // REFERENCES
    //---------------------------------------------------------------------------------------------------
    // Camera follow variables
    public Transform target; // Player transform component to follow
    public Vector3 offset = new Vector3(0f, 0f, -10f);
    public float smoothing = 0.1f;
    public Vector2 deadZone = new Vector2(2f, 1f); // Deadzone (width, height)
    private Vector3 velocity = Vector3.zero;
    private Vector3 newCamPos = Vector3.zero;

    // Camera shift variables
    private PlayerControls controls;
    public float camShiftStep = 5f;
    public float maxCamShift = 10f;
    private Vector2 dirInput = Vector2.zero; // (x, y) direction
    private Vector3 currShift = Vector3.zero;


    //---------------------------------------------------------------------------------------------------
    // UNITY DEFAULT METHODS
    //---------------------------------------------------------------------------------------------------
    void Awake()
    // Called on scene init
    {
         // Init input sys
        controls = new PlayerControls();
        controls.Camera.Enable();

        // Action subscriptions
        controls.Camera.CAMSHIFT.performed += ctx => dirInput = ctx.ReadValue<Vector2>();
        controls.Camera.CAMSHIFT.canceled += ctx => dirInput = Vector2.zero;
    }

    void FixedUpdate()
    // Called at constant freq
    {
        if (target == null)
        {
            return;
        }

        camFollow();
        camShift();

        transform.position = Vector3.SmoothDamp(
            transform.position,
            newCamPos + currShift,
            ref velocity,
            smoothing
            ); // Smoothly moves camera
    }

    void OnDisable()
    // Called when obj is disabled
    {
        controls.Camera.Disable(); // Disable input system
    }

    //---------------------------------------------------------------------------------------------------
    // CUSTOM METHODS
    //---------------------------------------------------------------------------------------------------
    void camFollow()
    // Calculates the needed camera position to follow the player once they pass screen deadzones
    {
        Vector3 currPos = transform.position;
        Vector3 newPos = target.position + offset;

        float xDiff = newPos.x - currPos.x;
        if (Mathf.Abs(xDiff) > deadZone.x)
        {
            currPos.x = newPos.x - Mathf.Sign(xDiff) * deadZone.x; // Moves camera horizontally when player passes dead zone width
        }

        float yDiff = newPos.y - currPos.y;
        if (Mathf.Abs(yDiff) > deadZone.y)
        {
            currPos.y = newPos.y - Mathf.Sign(yDiff) * deadZone.y; // Moves camera vertically when player passes dead zone height
        }

        newCamPos = currPos;
    }
    
    void camShift()
    // Calculates the shifted camera position when player inputs on the right stick
    {
        Vector3 targetShift = new Vector3(dirInput.x, dirInput.y * 0.5f, 0f) * maxCamShift; // Determine target camera shift
        currShift = Vector3.Lerp(currShift, targetShift, camShiftStep * Time.fixedDeltaTime); // Move the camera to shifted position
    }
}
