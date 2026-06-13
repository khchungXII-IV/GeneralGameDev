using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;



public class PLAYERCONTROLLER : MonoBehaviour
// Code for 2D platformer/sidescroller.
// Implements the following features:
// - Custom gravity with increased fall speed and terminal velocity
// - Ground, ceiling, and wall collisions with velocity clamping
// - Variable height jumping and coyote jump (button south)
// - Smooth movement and sprint (L stick and L stick press)
// - Wallslide
// - Airjump (button south in air)
// - Walljump (button south on wall)
// - Distance based dash (button east)
// - Climb (L trigger + L stick on wall)
// - Traverse ceilings (L trigger + L stick stick on ceiling)
// - Crouch and crouch movement (L trigger + L stick on ground)
// - Melee attack (button west)
// - Interact (button north)
// - Aimed projectile attacks (R trigger to fire, R stick to aim)
// - Quick change weapons (R shoulder)
// - Inventory and Equipment (L shoulder)
// - Quick item use (D pad)
// - Quick map (R stick press)
{
    // --------------------------------------------------------------------------------------------------
    // REFERENCES
    // --------------------------------------------------------------------------------------------------

    public Rigidbody2D body;
    private Vector2 velocity = Vector2.zero;
    private CONTROLS controls;
    private float dt = 0f;
    struct TIMERS
    {
        public float validJump;  
        public float coyote;
        public float sprintReset;
        public float wallJumpLock;
        public float wallCoyote;
        public float dashCooldown;
        public float climbHang;
    }
    private TIMERS timers;
    private float validInputTime = 0.1f;

    // Gravity
    public float gravity = -27.5f;
    public float fallMult = 2.5f;
    public float terminalV = -30f;

    // Collisions
    public LayerMask gwc;
    public BoxCollider2D col;
    private float colCastDist = 0.02f;
    private RaycastHit2D[] colResults = new RaycastHit2D[1];
    private ContactFilter2D filter;
    private bool isGrounded = false;
    private bool isOnWall = false;
    
    // Jump
    public float jumpPower = 12.5f;
    public float jumpFract = 0.6f;
    private float coyoteTime = 0.07f;
    
    // Move
    private Vector2 dirInput = Vector2.zero;
    public float moveSpeed = 6f;
    public float moveSmooth = 100f;
    public float sprintMult = 3f;
    private bool isSprinting = false;

    // WallSlide
    public float wallSlideFract = 0.75f;

    // Airjump
    public float airJumpPower = 10f;
    public int maxAirMoves = 3;
    private int currAirMoves = 0;

    // Walljump
    public Vector2 wallJumpPower = new Vector2(10f, 10f);
    private float wallJumpLockTime = 0.15f;
    private float normalDir = 0f;

    // Dash
    public float dashDist = 2.5f;
    public float dashSpeed = 20f;
    private float dashDir = 0f;
    private float facingDir = 0f;
    private bool isDashing = false;
    private float dashRemaining = 0f;
    public float dashCooldownTime = 0.2f;

    // Climb
    public float crouchClimbSpeed = 3.5f;
    private bool isClimbing = false;
    private bool isCrouchHold = false;
    public float maxClimbHangTime = 3f;
    private bool climbHangLock = false;

    // CeilingTraversal
    private bool isHanging = false;

    // Crouch
    private bool isCrouching = false;
    public float crouchHeightFract = 0.3f;
    private Vector2 colSize = Vector2.zero;
    private Vector2 colOffset = Vector2.zero;
    public Transform sprite; // May not need in actual games which switch between sprites instead of altering 1 sprite
    private Vector3 spriteLocalPos = Vector3.zero; // May not need in actual games which switch between sprites instead of altering 1 sprite
    

    


    // --------------------------------------------------------------------------------------------------
    // STATE MACHINE
    // --------------------------------------------------------------------------------------------------

    private enum state
    {
        grounded,
        inAir,
        onWall,
        dashing,
        climbing,
        hanging
    }
    private state currState;



    // --------------------------------------------------------------------------------------------------
    // UNITY DEFAULT METHODS
    // --------------------------------------------------------------------------------------------------

    void Awake()
    // Called on scene init
    {
        // Init player input system
        controls = new CONTROLS();
        controls.PLAYER.Enable();

        // Action subscriptions
        controls.PLAYER.JUMP.performed += onJUMPpressed;
        controls.PLAYER.JUMP.canceled += onJUMPreleased;
        controls.PLAYER.DIR.performed += onDIRpressed;
        controls.PLAYER.DIR.canceled += onDIRreleased;
        controls.PLAYER.SPRINT.performed += onSPRINTpressed;
        controls.PLAYER.DASH.performed += onDASHpressed;
        controls.PLAYER.CROUCHHOLD.performed += onCROUCHHOLDpressed;
        controls.PLAYER.CROUCHHOLD.canceled += onCROUCHHOLDreleased;

        // Set up collision checks
        filter = new ContactFilter2D();
        filter.SetLayerMask(gwc);
        filter.useTriggers = false;

        currState = state.inAir; // Init currState

        // Set default collider size and offset
        colSize = col.size;
        colOffset = col.offset;
        spriteLocalPos = sprite.localPosition;
    }

    void FixedUpdate()
    // Called at fixed freq
    {
        // Handle time
        dt = Time.fixedDeltaTime;
        countdownTimers();
        
        // Switch logic based on player state
        switch (currState)
        {
            case state.grounded:
                groundLogic();
                break;

            case state.inAir:
                airLogic();
                break;

            case state.onWall:
                wallLogic();
                break;

            case state.dashing:
                dashLogic();
                break;

            case state.climbing:
                climbLogic();
                break;

            case state.hanging:
                hangLogic();
                break;
        }

        updateState();
    }

    void OnDisable()
    // Called when obj is disabled
    {
        // Action unsubscriptions
        controls.PLAYER.JUMP.performed -= onJUMPpressed;
        controls.PLAYER.JUMP.canceled -= onJUMPreleased;
        controls.PLAYER.DIR.performed -= onDIRpressed;
        controls.PLAYER.DIR.canceled -= onDIRreleased;
        controls.PLAYER.SPRINT.performed -= onSPRINTpressed;
        controls.PLAYER.DASH.performed -= onDASHpressed;
        controls.PLAYER.CROUCHHOLD.performed -= onCROUCHHOLDpressed;
        controls.PLAYER.CROUCHHOLD.canceled -= onCROUCHHOLDreleased;

        controls.PLAYER.Disable(); // Disable input system
    }



    // --------------------------------------------------------------------------------------------------
    // INPUT HANDLER
    // --------------------------------------------------------------------------------------------------

    void onJUMPpressed(InputAction.CallbackContext ctx)
    // Called when JUMP pressed
    {
        timers.validJump = validInputTime;
    }

    void onJUMPreleased(InputAction.CallbackContext ctx)
    // Called when JUMP pressed
    {
        jumpCut();
    }

    void onDIRpressed(InputAction.CallbackContext ctx)
    // Called when JUMP pressed
    {
        dirInput = ctx.ReadValue<Vector2>();
        facingDir = ctx.ReadValue<Vector2>().x;
    }

    void onDIRreleased(InputAction.CallbackContext ctx)
    // Called when JUMP pressed
    {
        dirInput = Vector2.zero;
    }

    void onSPRINTpressed(InputAction.CallbackContext ctx)
    // Called when JUMP pressed
    {
        sprint();
    }

    void onDASHpressed(InputAction.CallbackContext ctx)
    // Called when DASH pressed
    {
        dashSetup();
    }

    void onCROUCHHOLDpressed(InputAction.CallbackContext ctx)
    // Called when CROUCHHOLD pressed
    {
        isCrouchHold = true;
    }

    void onCROUCHHOLDreleased(InputAction.CallbackContext ctx)
    // Called when CROUCHHOLD released
    {
        isCrouchHold = false;
    }



    // --------------------------------------------------------------------------------------------------
    // STATE LOGIC
    // --------------------------------------------------------------------------------------------------

    void updateState()
    // Updates player states
    {
        if (isDashing) changeState(state.dashing);
        else if (isClimbing) changeState(state.climbing);
        else if (isHanging) changeState(state.hanging);
        else if (isGrounded) changeState(state.grounded);
        else if (isOnWall) changeState(state.onWall);
        else changeState(state.inAir);
    }

    void changeState(state newState)
    // Calls methods to change from one state to another
    {
        if (currState == newState) return;
        
        state prevState = currState;

        exitState(currState);
        currState = newState;
        enterState(currState, prevState);
    }

    void enterState(state newState, state prevState)
    // Called when entering a new state from another
    {
        switch (newState)
        {
            case state.grounded:
                currAirMoves = 0;
                timers.wallJumpLock = 0f;
                timers.climbHang = 0f;
                climbHangLock = false;
                break;

            case state.inAir:
                if (prevState == state.grounded) timers.coyote = coyoteTime;
                if (prevState == state.onWall) timers.wallCoyote = coyoteTime;
                break;

            case state.onWall:
                currAirMoves = 0;
                break;

            case state.dashing:
                isSprinting = false; 
                break;

            case state.climbing:
                isSprinting = false; 
                currAirMoves = 0;
                break;

            case state.hanging:
                isSprinting = false;
                currAirMoves = 0;
                break;
        }
    }

    void exitState(state currState)
    // Called when exiting a state
    {
        switch (currState)
        {
            case state.grounded:
                if (isCrouching) crouchCancel();
                break;

            case state.inAir:
                break;

            case state.onWall:
                break;

            case state.dashing:
                timers.dashCooldown = dashCooldownTime;
                break;

            case state.climbing:
                break;

            case state.hanging:
                break;
        }
    }

    void groundLogic()
    // Calls methods needed when on ground
    {
        move();
        jump();
        crouch();
        createGravity();

        applyMovement();
    }

    void airLogic()
    // Calls methods needed when in air
    {
        move();
        coyoteJump();
        airJump();
        createGravity();

        applyMovement();
    }

    void wallLogic()
    // Calls methods needed when on wall
    {
        move();
        wallJump();
        createGravity();
        wallSlide();

        applyMovement();
    }

    void dashLogic()
    // Handles dashing
    {
        dash();
    }

    void climbLogic()
    // Calls methods needed for climbing
    {
        climbHangCancel();
        wallClimb();
        wallJump();

        applyMovement();
    }

    void hangLogic()
    // Calls methods needed for hanging
    {
        detectCeiling();
        climbHangCancel();
        ceilingTraversal();

        applyMovement();
    }



    // --------------------------------------------------------------------------------------------------
    // MOVEMENT LOGIC
    // --------------------------------------------------------------------------------------------------

    void createGravity()
    // Calculates downward velocity due to gravity
    {
        float appliedGravity = gravity;

        if (velocity.y < 0) appliedGravity *= fallMult;
        velocity.y += appliedGravity * dt;
        if (velocity.y <= terminalV) velocity.y = terminalV;
    }


    
    void checkCollisions(ref Vector2 moveStep)
    // Calls methods to check for collisions in X and Y axes
    {
        checkCollisionsY(ref moveStep);
        checkCollisionsX(ref moveStep);
    }

    void checkCollisionsY(ref Vector2 moveStep)
    // Checks and handles collisions in the Y axis
    {
        isGrounded = false;

        if (moveStep.y != 0)
        {
            float yDir = Mathf.Sign(moveStep.y);
            float yDist = Mathf.Abs(moveStep.y);
            int hitCount = col.Cast(Vector2.up * yDir, filter, colResults, yDist + colCastDist);

            if (hitCount > 0)
            {
                float allowedDist = colResults[0].distance - colCastDist;
                moveStep.y = yDir * allowedDist;
                
                if (yDir < 0) isGrounded = true;

                if (yDir > 0 && isCrouchHold && !climbHangLock) isHanging = true; // Check if trying to hang on ceiling

                velocity.y = 0f;
            }
        }
    }

    void checkCollisionsX(ref Vector2 moveStep)
    // Checks and handles collisions in the X axis
    {
        if (moveStep.x != 0)
        {
            float xDir = Mathf.Sign(moveStep.x);
            float xDist = Mathf.Abs(moveStep.x);
            int hitCount = col.Cast(Vector2.right * xDir, filter, colResults, xDist + colCastDist);

            if (hitCount > 0)
            {
                float allowedDist = colResults[0].distance - colCastDist;
                moveStep.x = xDir * allowedDist;

                velocity.x = 0f;
            }
        }

        detectWall();
    }

    void detectWall()
    // Checks if on wall even when not moving into it
    {
        isOnWall = false;

        float checkDist = colCastDist + 0.05f;

        if (col.Cast(Vector2.right, filter, colResults, checkDist) > 0f)
        {
            isOnWall = true;
            normalDir = -1f;
        }
        if (col.Cast(Vector2.left, filter, colResults, checkDist) > 0f)
        {
            isOnWall = true;
            normalDir = 1f;
        }

        if (isOnWall && isCrouchHold && !climbHangLock) isClimbing = true; // Check if trying to climb wall
    }

    void applyMovement()
    // Applies velocity to move player and clamps velocity to 0 when colliding with objects
    {
        Vector2 moveStep = velocity * dt;
        checkCollisions(ref moveStep);
        body.MovePosition(body.position + moveStep);
    }

    

    void jump()
    // Creates upward Y velocity when jumping off ground
    {
        if (timers.validJump > 0f)
        {
            velocity.y = jumpPower;
            timers.validJump = 0f;
        }
    }

    void jumpCut()
    // Reduces jummp height if player releases jump early
    {
        if (velocity.y > 0f) velocity.y *= jumpFract;
    }

    void coyoteJump()
    // Performs regular/wall jump when in some ms interval of leaving the ground/wall
    {
        if (timers.coyote > 0f) 
        {
            jump();
            timers.coyote = 0f;
        }
        if (timers.wallCoyote > 0f) 
        {
            wallJump();
            timers.wallCoyote = 0f;
        }
    }
    
    void airJump()
    // Jumps when in air with available air move resources
    {
        if (timers.validJump > 0f && currAirMoves < maxAirMoves)
        {
            velocity.y = airJumpPower;
            currAirMoves += 1;
            timers.validJump = 0f;
        }
    }



    void move()
    // Calculates X velocity due to X directional input
    {
        if (timers.wallJumpLock > 0f) return;
        
        float appliedSpeed;

        if (isCrouching) appliedSpeed = crouchClimbSpeed * dirInput.x;
        else 
        {
            appliedSpeed = moveSpeed * dirInput.x;
            if (isSprinting) appliedSpeed *= sprintMult;
        }
        
        velocity.x = Mathf.MoveTowards(velocity.x, appliedSpeed, moveSmooth * dt);

        sprintReset();
    }

    void sprint()
    // Toggles on sprinting if player inputs SPRINT
    {
        // Sprinting stops crouch
        if (isCrouching)
        {
            isCrouching = false;
            isCrouchHold = false;
        }

        if (currState == state.grounded) isSprinting = !isSprinting;
    }

    void sprintReset()
    // Sets sprint reset timer and automatically turns off sprint if no directional input after sometime
    {
        if (isSprinting && dirInput.sqrMagnitude >= 0.01f) timers.sprintReset = validInputTime;
        if (timers.sprintReset <= 0f) isSprinting = false;
    }



    void wallSlide()
    // Slows down falling speed on wall
    {
        if (velocity.y < 0f) velocity.y *= wallSlideFract;
    }

    void wallJump()
    // Calculates the upward and normal direction velocity when jumping of the wall
    {
        if (timers.validJump > 0f)
        {
            velocity = new Vector2(wallJumpPower.x * normalDir, wallJumpPower.y);
            timers.wallJumpLock = wallJumpLockTime;
            timers.validJump = 0f;
        }

        if (currState == state.climbing) isClimbing = false; // Stops climbing when walljumping off
    }


    
    void dashSetup()
    // Sets up dash
    {
        if (currState == state.inAir && currAirMoves >= maxAirMoves) return; // No dash if in air with no air moves
        if (isSprinting) return; // Can't dash mid sprint
        
        if (!isDashing && timers.dashCooldown <= 0f)
        {
            isDashing = true;
            dashRemaining = dashDist;
            if (dirInput.sqrMagnitude >= 0.01f) dashDir = Mathf.Sign(dirInput.x);
            else dashDir = Mathf.Sign(facingDir);
            velocity = Vector2.zero; // Zero vertical and horizontal velocity before dashing
            if (currState == state.inAir) currAirMoves += 1; // Dashing in air uses up air moves
        }
    }

    void dash()
    // Calculates velocity and performs dash
    {
        float dashStep = dashSpeed * dt;
        float moveDist = Mathf.Min(dashStep, dashRemaining);

        Vector2 moveStep = new Vector2(dashDir * moveDist, 0f);
        Vector2 originalStep = moveStep;
        
        checkCollisions(ref moveStep);
        body.MovePosition(body.position + moveStep);

        if (Mathf.Abs(moveStep.x) < Mathf.Abs(originalStep.x))
        {
            isDashing = false;
            dashRemaining = 0f;
        }
        else
        {
            dashRemaining -= moveStep.magnitude;
            if (dashRemaining <= 0.01f) isDashing = false;
        } 
    }



    void wallClimb()
    // Calculates Y velocity due to climbing
    {
        float appliedSpeed = crouchClimbSpeed * dirInput.y;
        velocity.y = Mathf.MoveTowards(velocity.y, appliedSpeed, moveSmooth * dt);
    }

    void climbHangCancel()
    // Stops climbing and hanging
    {
        if (!isCrouchHold) 
        {
            isClimbing = false;
            isHanging = false;
        }

        if (timers.climbHang > maxClimbHangTime)
        {
            isClimbing = false;
            isHanging = false;
            climbHangLock = true;
        }
    }



    void ceilingTraversal()
    // Calculates X velocity due to ceiling traversal
    {
        float appliedSpeed = crouchClimbSpeed * dirInput.x;
        velocity.x = Mathf.MoveTowards(velocity.x, appliedSpeed, moveSmooth * dt);
    }

    void detectCeiling()
    {
        float checkDist = colCastDist + 0.05f;
        if (col.Cast(Vector2.up, filter, colResults, checkDist) == 0) isHanging = false;
    }



    void crouch()
    // Adjusts collider and sprite size and offset when crouching
    {
        if (isCrouchHold && !isCrouching) crouchStart();
        if (!isCrouchHold) crouchCancel(); // Can stop crouch by sprinting
    }

    void crouchStart()
    // Initiates crouch and adjusts collider and sprite size
    {
        isSprinting = false; // Crouching stops sprint
        
        float crouchHeight = colSize.y * crouchHeightFract;
        col.size = new Vector2(colSize.x, crouchHeight);

        float crouchOffsetDiff = (colSize.y - crouchHeight)/2f;
        col.offset = colOffset - new Vector2(0f, crouchOffsetDiff);

        // Adjust sprite size and height (for actual games: switch to crouching sprite)
        sprite.localScale = new Vector3(1/(2 * crouchHeightFract), crouchHeightFract, 1/(2 * crouchHeightFract)); 
        sprite.localPosition = spriteLocalPos - new Vector3(0f, crouchOffsetDiff, 0f);

        isCrouching = true;
    }

    void crouchCancel()
    // Stops crouching and resets to original size
    {
        float checkDist = colSize.y - col.size.y + 0.05f;
        if (col.Cast(Vector2.up, filter, colResults, checkDist) > 0f) return; // Ceiling in the way, stays crouched even if player lets go of CROUCHHOLD

        col.size = colSize;
        col.offset = colOffset;

        // Resets sprite size and height (for actual games: switch back to idle sprite)
        sprite.localScale = new Vector3(1f, 1f, 1f); 
        sprite.localPosition = spriteLocalPos;
            
        isCrouching = false;
    }



    // --------------------------------------------------------------------------------------------------
    // MOVEMENT TIMERS
    // --------------------------------------------------------------------------------------------------

    void countdownTimers()
    // Decrements timers
    {
        if (timers.validJump > 0f) timers.validJump -= dt;
        if (timers.coyote >0f) timers.coyote -= dt;
        if (timers.sprintReset > 0f) timers.sprintReset -= dt;
        if (timers.wallJumpLock > 0f) timers.wallJumpLock -= dt;
        if (timers.wallCoyote > 0f) timers.wallCoyote -= dt;
        if (timers.dashCooldown > 0f) timers.dashCooldown -= dt;
        if (currState == state.climbing || currState == state.hanging) timers.climbHang += dt;
    }
}