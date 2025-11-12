using System;
using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerMovement : MonoBehaviour
// Code for 2D platformer player movement using kinematic Rigidbody2D. Implements the following features:
// - Ground collisions
// - Ceiling collisions
// - Wall collisions and wall sliding
// - Wall climbing
// - Custom gravity with increased fall speed and terminal velocity
// - Variable height jumping and coyote jump
// - Air jumps
// - Wall jumping
// - Smoothed left/right movement and sprinting
// - Distance based dash
{
    // --------------------------------------------------------------------------------------------------
    // REFERENCES
    // --------------------------------------------------------------------------------------------------
    public Rigidbody2D body; // Player's kinematic physics body component
    private Vector2 velocity; // Player's net velocity
    private PlayerControls controls;
    private float collisionCheckRad = 0.075f; // Radius of collision check objects
    public LayerMask groundWallMask; // Layer which specifies ground/walls
    private float validInputTime = 0.1f; // Time which inputs are still valid

    // Ground variables
    public Transform groundCheck; // Player-collider child obj which detects ground collisions
    private bool isGrounded;

    // Ceiling variables
    public Transform ceilingCheck; // Player collider child obj that checks for cieling collisions 
    private float ceilingHitTime = 0.1f; // Time which player is stuck to ceiling when colliding with it
    private float ceilingHitTimer; // Countdown timer for ceilingHitTime  

    // Wallslide variables
    public Transform wallCheckLeft; // Player collider child obj that checks for left wall collisions
    public Transform wallCheckRight; // Player collider child obj that check for right wall collisions
    private bool isOnWall;
    private float normalDir; // Direction opposite to wall
    public float wallSlideFract = 0.7f; // Reduces fall speed when on wall

    // Wallclimb variables
    private bool climbPressed;
    private bool isClimbing;
    private float yDir;
    public float climbSpeed = 5f;
    
    // Gravity variables
    public float gravity = -25f; // Downwards acceleration due to gravity
    public float terminalV = -30f; // Max fall speed
    public float fallMult = 2.3f; // Gravity multiplies when falling

    // Jumping variables
    public float jumpPower = 12.5f; // Upwards velocity when jumping
    private float jumpBufferTimer; // Countdown timer for jump buffer
    public float jumpFract = 0.6f; // Jump fraction when JUMP released early. Range = (0.0,1.0)
    private bool canCoyoteJump;
    private float coyoteTime = 0.1f; // Time which player can still jump after leaving the ground
    private float coyoteTimer; // Countdown timer for coyote time

    // Airjump variables
    private float airJumpTimer; // Countdown timer for valid airjump input
    public float airJumpFract = 0.85f; // Reduces jump height when doing an air jump

    // Walljump variables
    private bool canWallJump;
    public Vector2 wallJumpPower = new Vector2(10f, 10f); // (x, y) velocity when wall jumping
    private float wallJumpTime = 0.1f; // Time which player can still wall jump after leaving wall
    private float wallJumpTimer; // Countdown timer for wall jump
    private float wallJumpLockTime = 0.15f; // Time which wall snap doesn't occur due to wall jumping
    private float wallJumpLockTimer; // Countdown timer for wallJumpLockTime

    // Move variables
    public float moveSpeed = 7f; // Base movement speed
    public float frict = 17.5f; // How fast player accelerates/deccelerates on ground
    private float dirInput; // -1 if inputting left, +1 if inputting right
    private bool isSprinting;
    public float sprintMult = 1.25f;
    private float sprintResetTimer; // CountdownTimer for sprintResetTime

    // Dash variables
    private float facingDir; // Direction which player is facing
    private bool isDashing;
    public float dashDist = 3.5f;
    private Vector2 dashStart;
    private Vector2 dashEnd;
    private float dashElapsed; // Amount of max dash that has been done
    public float dashSpeed = 10f;
    private float dashTimer; // Countdown timer for dashTime
    private float dashCooldown = 0.33f; // Minimum time between successive dashes
    private float dashCooldownTimer; // Countdown timer for dash cooldown

    // Air move variables
    public int maxAirMoves = 3; // Max amount of dashes/air jumps players can do before grounding/wallsliding
    private int currAirMoves; // current amount of dashes/air jumps players can do


    // --------------------------------------------------------------------------------------------------
    // UNITY DEFAULT METHODS
    // --------------------------------------------------------------------------------------------------
    void Awake()
    // Called on player at scene init
    {
        // Init input sys
        controls = new PlayerControls();
        controls.Player.Enable();

        // Action subscriptions
        controls.Player.JUMP.performed += ctx => onJumpPressed();
        controls.Player.JUMP.canceled += ctx => jumpCut();
        controls.Player.MOVE.performed += ctx => dirInput = ctx.ReadValue<float>();
        controls.Player.MOVE.canceled += ctx => dirInput = 0f;
        controls.Player.MOVE.performed += ctx => facingDir = ctx.ReadValue<float>();
        controls.Player.DASH.performed += ctx => onDashPressed();
        controls.Player.TOGGLESPRINT.performed += ctx => toggleSprint();
        controls.Player.CLIMB.performed += ctx => climbPressed = true;
        controls.Player.CLIMB.canceled += ctx => climbPressed = false;
        controls.Player.UPDOWN.performed += ctx => yDir = ctx.ReadValue<float>();
        controls.Player.UPDOWN.canceled += ctx => yDir = 0f;
    }

    void FixedUpdate()
    // Called at constant frequency
    {
        // Calculate net velocity
        countdownTimers();
        checkGrounded();
        checkCeiling();
        wallSlide();
        wallClimb();
        createGravity();
        jump();
        airJump();
        wallJump();
        move();
        sprintAutoReset();
        storeDir();
        dash();
        resetAirMoves();

        body.MovePosition(body.position + velocity * Time.fixedDeltaTime); // Calculate new position due to net velocity and move to it
    }

    void OnDisable()
    // Called when player obj is disabled
    {
        controls.Player.Disable(); // Disable input system
    }


    // --------------------------------------------------------------------------------------------------
    // CUSTOM METHODS
    // --------------------------------------------------------------------------------------------------
    void countdownTimers()
    // Decrements all timers
    {
        if (ceilingHitTimer > 0) ceilingHitTimer -= Time.fixedDeltaTime;
        if (jumpBufferTimer > 0) jumpBufferTimer -= Time.fixedDeltaTime;
        if (coyoteTimer > 0) coyoteTimer -= Time.fixedDeltaTime;
        if (airJumpTimer > 0) airJumpTimer -= Time.fixedDeltaTime;
        if (wallJumpTimer > 0) wallJumpTimer -= Time.fixedDeltaTime;
        if (wallJumpLockTimer > 0) wallJumpLockTimer -= Time.fixedDeltaTime;
        if (dashTimer > 0) dashTimer -= Time.fixedDeltaTime;
        if (dashCooldownTimer > 0) dashCooldownTimer -= Time.fixedDeltaTime;
        if (sprintResetTimer > 0) sprintResetTimer -= Time.fixedDeltaTime;
    }


    void checkGrounded()
    // Checks if player is on ground and halts Y velocity if so
    {
        RaycastHit2D groundCollision = Physics2D.CircleCast(
            groundCheck.position, collisionCheckRad, Vector2.down,
            Mathf.Abs(velocity.y * Time.fixedDeltaTime) + 0.05f, groundWallMask
        ); // Circle cast down to detect ground

        isGrounded = groundCollision.collider != null; // True if CircleCast collides with Ground, false otherwise
        if (isGrounded)
        {
            coyoteTimer = coyoteTime; // Set coyote jump timer

            // Snap to ground if on or falling to it
            if (velocity.y <= 0)
            {
                float groundOffset = Mathf.Abs(groundCheck.localPosition.y);
                body.position = new Vector2(body.position.x, groundCollision.point.y + groundOffset);
                velocity.y = 0f;
            }
        }
        canCoyoteJump = coyoteTimer > 0; // True if coyoteTimer > 0, false otherwise
    }

    void checkCeiling()
    // Checks if player hits the cieling, stops their upward velocity and briefly snaps to it if so
    {
        RaycastHit2D ceilingCollision = Physics2D.CircleCast(
            ceilingCheck.position, collisionCheckRad, Vector2.up,
            Mathf.Abs(velocity.y * Time.fixedDeltaTime) + 0.05f, groundWallMask
        ); // Circle cast up to detect ceiling

        // Snaps to ceiling if colliding with it
        if (ceilingCollision.collider != null && velocity.y > 0)
        {
            ceilingHitTimer = ceilingHitTime;
            float ceilingOffset = Mathf.Abs(ceilingCheck.localPosition.y);
            body.position = new Vector2(body.position.x, ceilingCollision.point.y - ceilingOffset);
            velocity.y = 0f;
        }
    }

    void wallSlide()
    // Checks if player is on wall, halts any velocity towards it, and slows down falling speed if so
    {
        if (wallJumpLockTimer > 0)
        {
            isOnWall = false;
            return;
        }

        RaycastHit2D wallCollisionLeft = Physics2D.CircleCast(
            wallCheckLeft.position, collisionCheckRad, Vector2.left,
            Mathf.Abs(velocity.x * Time.fixedDeltaTime) + 0.05f, groundWallMask
        ); // Circle cast left to detect wall
        RaycastHit2D wallCollisionRight = Physics2D.CircleCast(
            wallCheckRight.position, collisionCheckRad, Vector2.right,
            Mathf.Abs(velocity.x * Time.fixedDeltaTime) + 0.05f, groundWallMask
        ); // Circle cast right to detect wall

        isOnWall = wallCollisionLeft.collider != null || wallCollisionRight.collider != null; // True if either CircleCast collides with wall, false otherwise
        if (isOnWall)
        {
            float wallOffset = 0f; // Used to calculate position of closest wall surface
            Vector2 wallSnap = Vector2.zero; // Position of closest wall surface
            if (wallCollisionLeft.collider != null)
            {
                normalDir = 1f;
                wallOffset = Mathf.Abs(wallCheckLeft.localPosition.x);
                wallSnap = new Vector2(wallCollisionLeft.point.x + wallOffset, body.position.y);
            }
            else if (wallCollisionRight.collider != null)
            {
                normalDir = -1f;
                wallOffset = Mathf.Abs(wallCheckRight.localPosition.x);
                wallSnap = new Vector2(wallCollisionRight.point.x - wallOffset, body.position.y);
            }
            else normalDir = 0f;

            if (MathF.Sign(dirInput) == -normalDir) body.position = wallSnap; // Snaps to wall if inputting into it
            if (velocity.y < 0 && !isClimbing) velocity.y *= wallSlideFract; // Slows down falling speed if falling on wall

            wallJumpTimer = wallJumpTime; // Sets wall jump timer
        }
    }
    
    void wallClimb()
    // Stops player from falling on wall if player is holding onto it. Also lets them climb up and down.
    {
        if (isOnWall && climbPressed)
        {
            RaycastHit2D wallCollisionLeft = Physics2D.CircleCast(
                wallCheckLeft.position, collisionCheckRad, Vector2.left,
                Mathf.Abs(velocity.x * Time.fixedDeltaTime) + 0.05f, groundWallMask
            ); // Circle cast left to detect wall
            RaycastHit2D wallCollisionRight = Physics2D.CircleCast(
                wallCheckRight.position, collisionCheckRad, Vector2.right,
                Mathf.Abs(velocity.x * Time.fixedDeltaTime) + 0.05f, groundWallMask
            ); // Circle cast right to detect wall
            float wallOffset = 0f; // Used to calculate position of closest wall surface
            Vector2 wallSnap = Vector2.zero; // Position of closest wall surface
            if (wallCollisionLeft.collider != null)
            {
                wallOffset = Mathf.Abs(wallCheckLeft.localPosition.x);
                wallSnap = new Vector2(wallCollisionLeft.point.x + wallOffset, body.position.y);
            }
            else if (wallCollisionRight.collider != null)
            {
                wallOffset = Mathf.Abs(wallCheckRight.localPosition.x);
                wallSnap = new Vector2(wallCollisionRight.point.x - wallOffset, body.position.y);
            }
            body.position = wallSnap; // Snaps to wall if inputting climb while on it
            isClimbing = true;
            velocity = Vector2.zero;

            if (yDir != 0) velocity.y = Mathf.Lerp(velocity.y, climbSpeed * yDir, frict * Time.fixedDeltaTime);
        }
        else isClimbing = false;
    }


    void createGravity()
    // Calculates downward velocity due to gravity when player is in air.
    // Halts Y velocity when player is on ground.
    {
        if (isClimbing) return; // Halts downward velocity if climbing

        else if (!isGrounded)
        {
            float appliedGravity = gravity;
            if (velocity.y < 0) appliedGravity *= fallMult; // Applies multiplied gravity when falling
            velocity.y += appliedGravity * Time.fixedDeltaTime;
            if (velocity.y < terminalV) velocity.y = terminalV;

            if (dashTimer > 0 && dashCooldownTimer <= 0) velocity.y = 0f; // Halts downard velocity while dashing in air
        }
        else if (isGrounded && velocity.y < 0) velocity.y = -2f; // Slight downards velocity to snap player to ground
    }


    void onJumpPressed()
    // Called when JUMP is pressed
    {
        jumpBufferTimer = validInputTime; // Start jump buffer timer
        if (!isGrounded) airJumpTimer = validInputTime; // Start air jump timer
    }

    void jump()
    // Calculates upward velocity due to jumping
    {
        if (jumpBufferTimer > 0 && canCoyoteJump) // Checks for valid jump
        {
            velocity.y = jumpPower;
            jumpBufferTimer = 0f; // Uses up valid jump time
            coyoteTimer = 0f; // Uses up coyote jump time
        }
    }

    void jumpCut()
    // Called when JUMP is released early to reduce jump height
    {
        if (velocity.y > 0) velocity.y *= jumpFract;
    }

    void airJump()
    // Jumps if player is in air and has needed air jump resources
    {
        if (airJumpTimer > 0 && currAirMoves > 0)
        {
            velocity.y = jumpPower * airJumpFract;
            airJumpTimer = 0f; // Uses up valid air jump
            currAirMoves -= 1;
        }
    }


    void wallJump()
    // Jumps off of wall if JUMP is pressed while on one
    {
        canWallJump = wallJumpTimer > 0;

        if (jumpBufferTimer > 0 && canWallJump)
        {
            velocity = new Vector2(wallJumpPower.x * normalDir, wallJumpPower.y);
            wallJumpLockTimer = wallJumpLockTime;
            jumpBufferTimer = 0f;
            wallJumpTimer = 0f;
            isOnWall = false;
        }
    }


    void move()
    // Calcaultes left/right velocity due to moving.
    // Also checks for edge cases where dirInput should be ignored
    {
        if (wallJumpLockTimer > 0 || dashTimer > 0 || isClimbing) return; // Ignores input when wall jumping/dashing/climbing

        else if (isOnWall && MathF.Sign(dirInput) != normalDir) velocity.x = 0f; // Stops tunneling into walls if inputting into them

        else
        {
            velocity.x = Mathf.Lerp(velocity.x, moveSpeed * dirInput, frict * Time.fixedDeltaTime);
            if (isSprinting) velocity.x *= sprintMult;
        }

    }
    
    void toggleSprint()
    // Toggles sprinting to true after a single press
    {
        isSprinting = true;
    }

    void sprintAutoReset()
    // Automatically sets sprinting to false if player has no directional input for some time
    {
        if (isSprinting && dirInput != 0) sprintResetTimer = validInputTime; // Start sprint reset timer
        if (sprintResetTimer <= 0) isSprinting = false;
    }


    void storeDir()
    // Stores the direction which the player is facing (-1 = left, 1 = right)
    {
        facingDir = MathF.Sign(facingDir);
    }

    void onDashPressed()
    // Called when DASH is pressed
    {
        if (dashCooldownTimer > 0 || isDashing || (!isGrounded && currAirMoves <= 0)) return;

        Vector2 dashDir = Vector2.zero;
        if (dirInput != 0) dashDir.x = MathF.Sign(dirInput);
        else dashDir.x = facingDir;

        RaycastHit2D dashCollision = Physics2D.CircleCast(
            body.position, collisionCheckRad, dashDir, dashDist, groundWallMask
        ); // Checks if dash collides into wall

        // Set dash distance to max dash distance or to distance until wall
        float dist;
        if (dashCollision.collider != null) dist = dashCollision.distance - (collisionCheckRad + 0.02f);
        else dist = dashDist;

        // Set up dash
        dashStart = body.position;
        dashEnd = dashStart + dashDir * dist;
        dashElapsed = 0f;
        isDashing = true;
        if (!isGrounded) currAirMoves -= 1;

        dashCooldownTimer = dashCooldown; // Set dash cooldown timer
    }

    void dash()
    // Calculates left/right velocity boost due to dashing
    {
        if (isDashing)
        {
            dashElapsed += Time.fixedDeltaTime;
            float t = dashElapsed / validInputTime;

            body.position = Vector2.Lerp(dashStart, dashEnd, t); // smooth dash movement
            velocity = (dashEnd - dashStart).normalized * dashSpeed * (1f - t); // visual speed boost

            // End dash at max distance
            if (t >= 1f)
            {
                isDashing = false;
                velocity = Vector2.zero;
            }
        }
    }


    void resetAirMoves()
    // Resets available air moves to player when they land on ground or collide with wall
    {
        if (isGrounded || isOnWall) currAirMoves = maxAirMoves;
    }

}
