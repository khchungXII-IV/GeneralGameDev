using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraMovement : MonoBehaviour
// Code for 3D platformer camera movement. Implements the following features:
// - Smooth following
// - Recentering the camera on input
// - Look ahead shifts in any direction
{
    //---------------------------------------------------------------------------------------------------
    // REFERENCES
    //---------------------------------------------------------------------------------------------------

    public Vector3 offset = new Vector3 (0f, 7f, -12f);
    public Quaternion angle = Quaternion.Euler(30f, 0f, 0f);
    Controls controls;
    struct TIMERS
    {
        public float doubleTap;
        public float doubleTapReset;
        public float rotateOff;
        public float rotateCooldown;
    }
    private TIMERS timers;
    private Vector3 relativeDir = Vector3.zero;
    
    // Follow
    public Transform target;
    public float smoothing = 0.3f;
    private Vector3 velocity = Vector3.zero;
    private Vector3 newCamPos = Vector3.zero;

    // Look ahead
    private Vector2 dirInput;
    public float camShiftStep = 400f;
    public float maxCamShift = 10f;
    private Vector3 camShift;
    public float zShiftFract = 0.6f;
    public float upShiftFract = 0.7f;
    public float downShiftFract = 0.8f;

    // Double tap
    private float doubleTapWindow = 0.3f;
    private bool stickPressedLastFrame;
    private Vector2 lastTapDir;
    private bool isDoubleTap;
    private float validInputTime = 0.1f;

    // Rotate
    private bool isRotating;
    public float rotAngle = 90f;
    private Vector3 currOffset = Vector3.zero;
    public float rotSpeed = 10f;
    private float rotateCooldownTime = 0.2f;


    //---------------------------------------------------------------------------------------------------
    // UNITY DEFAULT METHODS
    //---------------------------------------------------------------------------------------------------

    void Awake()
    // Called on scene init
    {
        transform.position = target.position + offset;
        currOffset = offset;

        // Init camera controls input system
        controls = new Controls();
        controls.Camera.Enable();

        // Action subscriptions
        controls.Camera.MOVE.performed += onMOVEpressed;
        controls.Camera.MOVE.canceled += onMOVEreleased;
    }
    
    void FixedUpdate()
    // Called at fixed freq
    {
        if (target == null) return;

        countdownTimers();
        getRelativeDir();
        checkDoubleTap();
        rotate();
        lookAhead();
        doubleTapReset();
        follow();

        transform.position = Vector3.SmoothDamp(transform.position, newCamPos + camShift, ref velocity, smoothing);
        if (isRotating) 
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, angle, rotSpeed);
            if (Quaternion.Angle(transform.rotation, angle) < 0.1f) isRotating = false;
            target.GetComponent<PlayerMovement>().updateRelativeCamAxes();
        }
        else transform.rotation = angle;
    }

    void OnDisable()
    // Called when camera obj disabled
    {
        // Action unscubscriuptions
        controls.Camera.MOVE.performed -= onMOVEpressed;
        controls.Camera.MOVE.canceled -= onMOVEreleased;
        
        controls.Camera.Disable(); // Disable input system
    }


    //---------------------------------------------------------------------------------------------------
    // INPUT HANDLER
    //---------------------------------------------------------------------------------------------------

    void onMOVEpressed(InputAction.CallbackContext ctx)
    // Called when MOVE pressed
    {
        dirInput = ctx.ReadValue<Vector2>();
    }

    void onMOVEreleased(InputAction.CallbackContext ctx)
    {
        dirInput = Vector2.zero;
    }


    //---------------------------------------------------------------------------------------------------
    // CORE LOGIC
    //---------------------------------------------------------------------------------------------------

    void follow()
    // Calculates new camera position to follow player
    {
        newCamPos = target.position + currOffset;
    }

    
    void lookAhead()
    // Calculates camera shift to look ahead on input in any direction
    {
        if (isRotating) return;
        
        Vector3 targetShift = Vector3.zero;

        // Look ahead in the Y axis
        if (isDoubleTap && (dirInput.y > 0.8f || dirInput.y < -0.8f)) 
        {
            float targetY = dirInput.y * maxCamShift * upShiftFract;
            if (dirInput.y < 0f) targetY *= downShiftFract; // Decrease look down shift
            targetShift.y += targetY;
        }
        else
        {
            if ((angle.eulerAngles.y > -0.9f && angle.eulerAngles.y < 0.1f)
                || (angle.eulerAngles.y > 179.9f && angle.eulerAngles.y < 180.1f))
                targetShift = new Vector3(relativeDir.x, 0f, relativeDir.z * zShiftFract) * maxCamShift;
            else if ((angle.eulerAngles.y > 89.9f && angle.eulerAngles.y < 90.1f)
                || (angle.eulerAngles.y > 269.9f && angle.eulerAngles.y < 270.1f))
                targetShift = new Vector3(relativeDir.x * zShiftFract, 0f, relativeDir.z) * maxCamShift;
            else 
                targetShift = new Vector3(relativeDir.x, 0f, relativeDir.z * zShiftFract * 0.5f) * maxCamShift;
        }
        camShift = Vector3.MoveTowards(camShift, targetShift, camShiftStep * Time.fixedDeltaTime);
    }

    void checkDoubleTap()
    // Checks if stick is double tapped
    {
        if (isRotating) return;
        
        float mag = dirInput.magnitude;
        bool stickPressed = mag > 0.8f;

        if (stickPressed && !stickPressedLastFrame)
        {
            Vector2 currentTapDir = dirInput;

            if (timers.doubleTap > 0f)
            {
                float dirSimilar = Vector2.Dot(currentTapDir, lastTapDir);
                if (dirSimilar > 0.6f) isDoubleTap = true;
                timers.doubleTap = 0f;
            }
            else // Sets current tap dir as last tap dir and restarts the double tap window if double tap missed
            {
                lastTapDir = currentTapDir;
                timers.doubleTap = doubleTapWindow;
            }
        }
        stickPressedLastFrame = stickPressed; // Set stickPriorPressed for next frame
    }

    void doubleTapReset()
    // Automatically turns off double tap when no input pressed after period of time
    {
        if (isDoubleTap && dirInput != Vector2.zero) timers.doubleTapReset = validInputTime;
        if (timers.doubleTapReset <= 0f) isDoubleTap = false;
    }

    
    void rotate()
    // Calls the rotate method in target
    {
        if (timers.rotateCooldown > 0f) return;
        
        if (isDoubleTap && (dirInput.x < -0.5f || dirInput.x > 0.5f))
        {
            isRotating = true;
            if (dirInput.x > 0.5f) 
            {
                target.GetComponent<PlayerMovement>().rotate(rotAngle);
                currOffset = Quaternion.Euler(0f, rotAngle, 0f) * currOffset;
                angle = Quaternion.Euler(30f, angle.eulerAngles.y + rotAngle, 0f);
            }
            else if (dirInput.x < -0.5f) 
            {
                target.GetComponent<PlayerMovement>().rotate(-rotAngle);
                currOffset = Quaternion.Euler(0f, -rotAngle, 0f) * currOffset;
                angle = Quaternion.Euler(30f, angle.eulerAngles.y + -rotAngle, 0f);
            }
            isDoubleTap = false;
            timers.rotateCooldown = rotateCooldownTime;
        }
    }


    void getRelativeDir()
    // Gets relative direction of player based on rotation
    {
        Vector3 camForward = transform.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Vector3 camRight = transform.right;
        camRight.y = 0f;
        camRight.Normalize();

        relativeDir = camRight * dirInput.x + camForward * dirInput.y;
    }


    void countdownTimers()
    // Decrements timers
    {
        if (timers.doubleTap > 0f) timers.doubleTap -= Time.fixedDeltaTime;
        if (timers.doubleTapReset > 0f) timers.doubleTapReset -= Time.fixedDeltaTime;
        if (timers.rotateCooldown > 0f) timers.rotateCooldown -= Time.fixedDeltaTime;
    }
}