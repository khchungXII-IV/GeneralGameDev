using System;
using System.Threading;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Scripting.APIUpdating;


public class PlayerMovement : MonoBehaviour
// Code for 3D platformer player movement using a kinematic Rigidbody. Implements the following features:
// - Ground, wall, and ceiling collisions
// - Custom gravity with increased fall speed and terminal V
// - Variable height jumping and coyote jumps
// - Smooth movement and sprinting
// - Wallsliding
// - Walljumping
// - Air jumping
// - Dashing
{
    //---------------------------------------------------------------------------------------------------
    // REFERENCES
    //---------------------------------------------------------------------------------------------------
    
    public Rigidbody body;
    private Vector3 velocity = Vector3.zero;
    private float validInputTime = 0.1f;
    private Controls controls;
    struct TIMERS
    {
        public float validJump;
        public float coyote;
        public float sprintReset;
        public float wallJump;
        public float wallJumpLock;
        public float airJump;
        public float dashCooldown;
    }
    private TIMERS timers;
    public Transform cam;

    // Gravity
    public float gravity = -25f;
    public float fallMult = 2.3f;
    public float terminalV = -30f;
    
    // Collisions
    public LayerMask gwc; // GroundWallCeiling layer
    public BoxCollider col;
    private Vector3 halfExtents;
    private Quaternion boxOrientation = Quaternion.identity;
    private float collisionCastDist = 0.075f;
    private float absVelY = 0f;
    private bool isGrounded = false;
    private bool ceilingHit = false;
    private bool isOnWall = false;
    private Vector2 wallDir = Vector2.zero; // (x, z), x and z = -1/1 for left/right and front/back walls respectively
    private float absVelX = 0f;
    private float absVelZ = 0f;

    // Jump
    public float jumpPower = 12.5f;
    private float coyoteTime = 0.1f; // Time which player can still jump even after leaving the ground
    public float jumpFract = 0.6f;
    
    // Move
    private Vector2 dirInput = Vector2.zero;
    public float moveSpeed = 7f;
    public float moveSmoothing = 50f;
    public float sprintMult = 2f;
    private bool isSprinting = false;
    private Vector3 relativeDir = Vector3.zero;

    // Wallslide
    public float wallSlideFallFract = 0.8f;
    public float wallSlideMoveFract = 0.65f;

    // WallJump
    public Vector3 wallJumpPower = new Vector3(10f, 10f, 10f);
    private float wallJumpTime = 0.1f;
    private float wallJumpLockTime = 0.15f;

    // Airjump
    public float airJumpFract = 0.9f;
    public int maxAirMoves = 3;
    private int currAirMoves;

    // Dash
    public float dashDist = 4f;
    public float dashSpeed = 40f;
    private bool isDashing = false;
    private Vector3 dashDir = Vector3.zero;
    private Vector3 dashStart = Vector3.zero;
    private Vector2 facingDir = Vector2.zero;
    public float dashCooldownTime = 0.15f;
    private Vector3 relativeFacingDir = Vector3.zero;

    // Rotate
    private Quaternion targetRot;
    private bool isRotating = false;
    public float rotSpeed = 10f;


    //---------------------------------------------------------------------------------------------------
    // UNITY DEFAULT METHODS
    //---------------------------------------------------------------------------------------------------

    void Awake()
    // Called on scene init
    {
        halfExtents = col.bounds.extents;

        // Init player input system
        controls = new Controls();
        controls.Player.Enable();

        // Action subscriptions
        controls.Player.JUMP.performed += onJUMPpressed;
        controls.Player.JUMP.canceled += onJUMPreleased;
        controls.Player.MOVE.performed += onMOVEpressed;
        controls.Player.MOVE.canceled += onMOVEreleased;
        controls.Player.SPRINT.performed += onSPRINTpressed;
        controls.Player.DASH.performed += onDASHpressed;
    }

    void FixedUpdate()
    // Called at fixed frequency
    {
        absVelY = Mathf.Abs(velocity.y * Time.fixedDeltaTime);
        absVelX = Mathf.Abs(velocity.x * Time.fixedDeltaTime);
        absVelZ = Mathf.Abs(velocity.z * Time.fixedDeltaTime);
        
        countdownTimers();
        getRelativeDir();
        checkCollisions();
        jump();
        wallJump();
        airJump();
        createGravity();
        wallSlide();
        move();
        dash();
        resetAirMoves();
        
        if (!isDashing) body.MovePosition(body.position + velocity * Time.fixedDeltaTime);
        if (isRotating) 
        {
            body.rotation = Quaternion.RotateTowards(body.rotation, targetRot, rotSpeed);
            if (Quaternion.Angle(body.rotation, targetRot) < 0.1f)
            {
                body.rotation = targetRot;
                isRotating = false;
            }
        }
    }

    void OnDisable()
    // Called when player obj is disabled
    {
        // Action unsubscriptions
        controls.Player.JUMP.performed -= onJUMPpressed;
        controls.Player.JUMP.canceled -= onJUMPreleased;
        controls.Player.MOVE.performed -= onMOVEpressed;
        controls.Player.MOVE.canceled -= onMOVEreleased;
        controls.Player.SPRINT.performed -= onSPRINTpressed;
        controls.Player.DASH.performed -= onDASHpressed;
        
        controls.Player.Disable(); // Disable input system
    }


    //---------------------------------------------------------------------------------------------------
    // INPUT HANDLER
    //---------------------------------------------------------------------------------------------------

    void onJUMPpressed(InputAction.CallbackContext ctx)
    // Called when JUMP pressed
    {
        timers.validJump = validInputTime;
        if (!isGrounded) timers.airJump = validInputTime;
    }

    void onJUMPreleased(InputAction.CallbackContext ctx)
    // Called when JUMP released
    {
        jumpCut();
    }

    void onMOVEpressed(InputAction.CallbackContext ctx)
    // Called when MOVE pressed
    {
        dirInput = ctx.ReadValue<Vector2>();
        facingDir = ctx.ReadValue<Vector2>();
    }

    void onMOVEreleased(InputAction.CallbackContext ctx)
    // Called when MOVE released
    {
        dirInput = Vector2.zero;
    }

    void onSPRINTpressed(InputAction.CallbackContext ctx)
    // Called when SPRINT pressed
    {
        toggleSprint();
    }

    void onDASHpressed(InputAction.CallbackContext ctx)
    // Called when DASH pressed;
    {
        if (isDashing || timers.dashCooldown > 0f || (!isGrounded && currAirMoves <= 0)) return;

        if (dirInput.sqrMagnitude > 0.01f) dashDir = new Vector3(relativeDir.x, 0f, relativeDir.z);
        else dashDir = new Vector3(relativeFacingDir.x, 0f, relativeFacingDir.z);
        dashStart = body.position;
        isDashing = true;
    }


    //---------------------------------------------------------------------------------------------------
    // CORE LOGIC
    //---------------------------------------------------------------------------------------------------

    void createGravity()
    // Calculates Y velocity due to gravity
    {
        if (!isGrounded)
        {
            float appliedGravity = gravity;
            if (velocity.y < 0) appliedGravity *= fallMult;
            velocity.y += appliedGravity * Time.fixedDeltaTime;
            if (velocity.y < terminalV) velocity.y = terminalV;

            if (isDashing) velocity.y = 0f;
        }
    }


    void checkCollisions()
    // Calls methods to check for player collisions
    {
        checkGrounded();
        checkIsOnWall();
        checkCeilingHit();
    }

    void checkGrounded()
    // Checks if player is colliding with ground and halts Y velocity if they are
    {
        isGrounded = Physics.BoxCast(body.position, halfExtents, Vector3.down,
        out RaycastHit groundHit, boxOrientation, absVelY + collisionCastDist, gwc);

        // Snap to ground and set Y velocity to 0 if on ground
        if (isGrounded && velocity.y <= 0f)
        {
            float groundSnap = groundHit.point.y + halfExtents.y;
            float diff = groundSnap - body.position.y;
            if (diff > -0.1f && diff < 0.1f)
                body.MovePosition(new Vector3(body.position.x, groundSnap, body.position.z)); // Only snap to ground when close to it
            velocity.y = 0f;
        }

        setCoyoteTimer();
    }

    void checkIsOnWall()
    // Checks if player is colliding with wall
    {
        if (timers.wallJumpLock > 0f)
        {
            isOnWall = false;
            return;
        }
        
        bool lWall = Physics.BoxCast(body.position, halfExtents, Vector3.left, out RaycastHit lWallHit, 
            boxOrientation, absVelX + collisionCastDist, gwc);
        bool rWall = Physics.BoxCast(body.position, halfExtents, Vector3.right, out RaycastHit rWallHit, 
            boxOrientation, absVelX + collisionCastDist, gwc);
        bool fWall = Physics.BoxCast(body.position, halfExtents, Vector3.forward, out RaycastHit fWallHit, 
            boxOrientation, absVelZ + collisionCastDist, gwc);
        bool bWall = Physics.BoxCast(body.position, halfExtents, Vector3.back, out RaycastHit bWallHit, 
            boxOrientation, absVelZ + collisionCastDist, gwc);

        isOnWall = lWall || rWall || fWall || bWall;
        
        if (isOnWall)
        {
            if (lWallHit.collider != null && rWallHit.collider == null) wallDir.x = -1;
            else if (rWallHit.collider != null && lWallHit.collider == null) wallDir.x = 1;
            else wallDir.x = 0;

            if (fWallHit.collider != null && bWallHit.collider == null) wallDir.y = 1;
            else if (bWallHit.collider != null && fWallHit.collider == null) wallDir.y = -1;
            else wallDir.y = 0;

            if (relativeDir.x < 0 && wallDir.x < 0)
            {
                float lWallSnap = lWallHit.point.x + halfExtents.x;
                float diff = lWallSnap - body.position.x;
                if (diff > -0.1f && diff < 0.1f)
                    body.MovePosition(new Vector3(lWallSnap, body.position.y, body.position.z));
            }
            else if (relativeDir.x > 0 && wallDir.x > 0)
            {
                float rWallSnap = rWallHit.point.x - halfExtents.x;
                float diff = rWallSnap - body.position.x;
                if (diff > -0.1f && diff < 0.1f)
                    body.MovePosition(new Vector3(rWallSnap, body.position.y, body.position.z));
            }

            if (relativeDir.z < 0 && wallDir.y < 0)
            {
                float bWallSnap = bWallHit.point.z + halfExtents.z;
                float diff = bWallSnap - body.position.z;
                if (diff > -0.1f && diff < 0.1f)
                    body.MovePosition(new Vector3(body.position.x, body.position.y, bWallSnap));
            }
            else if (relativeDir.z > 0 && wallDir.y > 0)
            {
                float fWallSnap = fWallHit.point.z - halfExtents.z;
                float diff = fWallSnap - body.position.z;
                if (diff > -0.1f && diff < 0.1f)
                    body.MovePosition(new Vector3(body.position.x, body.position.y, fWallSnap));
            }
        }
    }

    void checkCeilingHit()
    // Checks if player is colliding with ceiling
    {
        ceilingHit = Physics.BoxCast(body.position, halfExtents, Vector3.up,
        out RaycastHit hit, boxOrientation, absVelY + collisionCastDist, gwc);

        // Snap to ceiling and set Y velocity to 0 if hitting the ceiling
        if (ceilingHit && velocity.y > 0f)
        {
            float ceilingSnap = hit.point.y - halfExtents.y;
            float diff = ceilingSnap - body.position.y;
            if (diff > -0.1f && diff < 0.1f)
                body.MovePosition(new Vector3(body.position.x, ceilingSnap, body.position.z)); // Only snap to ceiling when close to it
            velocity.y = 0f;
        }
    }


    void jump()
    // Checks if player is jumping and creates upward Y velocity if they are
    {
        if (timers.validJump > 0f && timers.coyote > 0f)
        {
            velocity.y = jumpPower;
            timers.validJump = 0f;
            timers.coyote = 0f;
        }
    }

    void jumpCut()
    // Reduces jump height if player releases jump early
    {
        if (velocity.y > 0) velocity.y *= jumpFract;
    }

    void setCoyoteTimer()
    // Sets coyote jump timer
    {
        if (isGrounded) timers.coyote = coyoteTime;
    }


    void move()
    // Checks player's directional input and calculates X and Z velocity accordingly
    {   
        if (timers.wallJumpLock > 0f || isDashing) return;

        float appliedSpeed = moveSpeed;

        if (isSprinting) appliedSpeed *= sprintMult;
        
        velocity = Vector3.MoveTowards(velocity, 
            new Vector3(appliedSpeed * relativeDir.x, velocity.y, appliedSpeed * relativeDir.z), 
            moveSmoothing * Time.fixedDeltaTime);

        sprintReset();
        
        if (isOnWall)
        {
            if (relativeDir.x <= 0f && wallDir.x < 0f || relativeDir.x >= 0f && wallDir.x > 0f) 
            {
                velocity.x = 0f;
            }
            if (relativeDir.z <= 0f && wallDir.y < 0f || relativeDir.z >= 0f && wallDir.y > 0f) 
            {
                velocity.z = 0f;
            }
            if (velocity.y < 0)
            {
                velocity.x *= wallSlideMoveFract;
                velocity.z *= wallSlideMoveFract;
            }
        }
    }
    
    void toggleSprint()
    // Toggles sprint on and off when pressed
    {
        if (!isSprinting && isGrounded) isSprinting = true;
        else isSprinting = false;
    }

    void sprintReset()
    // Automatically turns off sprint if player has stopped moving for certain period of time
    {
        if (isSprinting && dirInput != Vector2.zero) timers.sprintReset = validInputTime;
        if (timers.sprintReset <= 0f) isSprinting = false;
    }


    void wallSlide()
    // Slows down falling speed if on wall
    {
        if (isOnWall && velocity.y < 0f) velocity.y *= wallSlideFallFract;
    }

    void wallJump()
    // Calculates velocity due to wallJumping
    {
        if (isOnWall) timers.wallJump = wallJumpTime;

        if (timers.validJump > 0f && timers.wallJump > 0f)
        {
            if (wallDir.x != 0f && wallDir.y == 0f)
                velocity = new Vector3(wallJumpPower.x * -wallDir.x, wallJumpPower.y, velocity.z);
            else if (wallDir.y != 0f && wallDir.x == 0f)
                velocity = new Vector3(velocity.x, wallJumpPower.y, wallJumpPower.z * -wallDir.y);
            else if (wallDir.y != 0f && wallDir.x != 0f)
                velocity = new Vector3(wallJumpPower.x * -wallDir.x, wallJumpPower.y, 
                    wallJumpPower.z * -wallDir.y);
            timers.wallJumpLock = wallJumpLockTime;
            timers.validJump = 0f;
            timers.wallJump = 0f;
            isOnWall = false;
        }
    }


    void airJump()
    // Jumps if player is in air and has needed air jump resources
    {
        if (timers.airJump > 0 && currAirMoves > 0)
        {
            velocity.y = jumpPower * airJumpFract;
            timers.airJump = 0f; // Uses up valid air jump
            currAirMoves -= 1;
        }
    }

    void resetAirMoves()
    // Resets available air moves to player when they land on ground or collide with wall
    {
        if (isGrounded || isOnWall || timers.wallJumpLock > 0f) currAirMoves = maxAirMoves;
    }


    void dash()
    // Calculates dash end position and performs dash
    {
        if (isDashing)
        {
            float currDashDist = Vector3.Distance(dashStart, body.position);
            float remainingDashDist = dashDist - currDashDist;

            if (remainingDashDist <= 0f)
            {
                isDashing = false;
                return;
            }

            float frameDist = Mathf.Min(dashSpeed * Time.fixedDeltaTime, remainingDashDist); // Determines dash distance per frame, never more than remaining distance
            Vector3 dashDisplacement = dashDir * frameDist;

            if (Physics.BoxCast(body.position, halfExtents, dashDir, out RaycastHit dashHit, 
                Quaternion.identity, frameDist + 0.05f, gwc))
            {
                Vector3 dashStop = dashHit.point - dashDir * halfExtents.magnitude;
                body.MovePosition(dashStop);
                dashEnd();
                return;
            }
            body.MovePosition(body.position + dashDisplacement);
            if (frameDist == remainingDashDist) dashEnd();
        }

        void dashEnd()
        // Ends dash
        {
            isDashing = false;
            if (!isGrounded) currAirMoves -= 1;
            timers.dashCooldown = dashCooldownTime;
            velocity.x = 0f;
            velocity.z = 0f;
        }
    }

    public void rotate(float angle)
    // Called from camera script to rotate player
    {
        targetRot = Quaternion.Euler(body.rotation.eulerAngles.x, body.rotation.eulerAngles.y + angle, 
            body.rotation.eulerAngles.z);
        isRotating = true;
    }

    void getRelativeDir()
    // Gets relative direction of player based on rotation
    {
        Vector3 camForward = cam.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Vector3 camRight = cam.right;
        camRight.y = 0f;
        camRight.Normalize();

        relativeDir = camRight * dirInput.x + camForward * dirInput.y;
        relativeFacingDir = camRight * facingDir.x + camForward * facingDir.y;
    }
    

    void countdownTimers()
    // Decrements timers
    {
        if (timers.validJump > 0f) timers.validJump -= Time.fixedDeltaTime;
        if (timers.coyote > 0f) timers.coyote -= Time.fixedDeltaTime;
        if (timers.sprintReset > 0f) timers.sprintReset -= Time.fixedDeltaTime;
        if (timers.wallJump > 0f) timers.wallJump -= Time.fixedDeltaTime;
        if (timers.wallJumpLock > 0f) timers.wallJumpLock -= Time.fixedDeltaTime;
        if (timers.airJump > 0f) timers.airJump -= Time.fixedDeltaTime;
        if (timers.dashCooldown > 0f) timers.dashCooldown -= Time.fixedDeltaTime;
    }
}
