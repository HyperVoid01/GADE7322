using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float runSpeed = 12f;
    [SerializeField] private float jumpPower = 7f;
    [SerializeField] private float gravity = 10f;
    [SerializeField] private float lookSpeed = 2f;
    [SerializeField] private float lookXLimit = 45f;
    [SerializeField] private float boundaryPadding = 1f;
    [SerializeField] private float fallResetHeight = -20f;

    [Header("Audio")]
    [Tooltip("Distance walked between footsteps")]
    [SerializeField] private float stepLength = 2.2f;
    [Tooltip("Minimum time in the air before landing makes a sound (ignores small bumps on slopes)")]
    [SerializeField] private float minAirTimeForLand = 0.25f;
    [Tooltip("Fall speed that gives the loudest landing")]
    [SerializeField] private float hardLandingSpeed = 15f;

    private float _stepDistance;
    private float _airTime;

    private Vector3 _lastGroundedPosition;

    private Vector3 moveDirection = Vector3.zero;
    private float rotationX = 0;
    private CharacterController characterController;

    public bool canMove = true;

    // True while sprinting and actually moving. Breathing speeds up with it.
    public bool IsRunning { get; private set; }
    
    public static PlayerMovement Instance;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _lastGroundedPosition = transform.position;
    }

    void Update()
    {
        // Converts directions into world space
        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);

        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float curSpeedX = canMove ? (isRunning ? runSpeed : walkSpeed) * Input.GetAxis("Vertical") : 0;
        float curSpeedY = canMove ? (isRunning ? runSpeed : walkSpeed) * Input.GetAxis("Horizontal") : 0;
        float movementDirectionY = moveDirection.y;
        moveDirection = (forward * curSpeedX) + (right * curSpeedY);

        // Jumping
        if (Input.GetButton("Jump") && canMove && characterController.isGrounded)
        {
            moveDirection.y = jumpPower;

            if (SfxPlayer.Instance != null)
                SfxPlayer.Instance.PlayJump(transform.position);
        }
        else
        {
            moveDirection.y = movementDirectionY;
        }

        // Gravity
        if (!characterController.isGrounded)
        {
            moveDirection.y -= gravity * Time.deltaTime;
        }

        bool wasGrounded = characterController.isGrounded;
        float verticalSpeed = moveDirection.y;

        // Move Player
        characterController.Move(moveDirection * Time.deltaTime);

        UpdateMovementAudio(wasGrounded, verticalSpeed, isRunning);
        
        if (characterController.isGrounded)
        {
            _lastGroundedPosition = transform.position;
        }

        ClampToMapBounds();
        CheckFallOut();
        
        // Mouse Look
        if (canMove)
        {
            rotationX += -Input.GetAxis("Mouse Y") * lookSpeed;
            rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
            transform.rotation *= Quaternion.Euler(0, Input.GetAxis("Mouse X") * lookSpeed, 0);
        }
    }
    
    // Footsteps every stepLength units walked, and a landing thump after a real fall
    private void UpdateMovementAudio(bool wasGrounded, float verticalSpeed, bool isRunning)
    {
        bool grounded = characterController.isGrounded;
        float horizontalSpeed = new Vector3(moveDirection.x, 0f, moveDirection.z).magnitude;

        IsRunning = isRunning && horizontalSpeed > 0.1f;

        if (!grounded)
        {
            _airTime += Time.deltaTime;
            return;
        }

        if (!wasGrounded && _airTime >= minAirTimeForLand && SfxPlayer.Instance != null)
        {
            float strength = Mathf.InverseLerp(2f, hardLandingSpeed, -verticalSpeed);
            SfxPlayer.Instance.PlayLand(transform.position, strength);
            _stepDistance = 0f;
        }

        _airTime = 0f;

        if (horizontalSpeed < 0.1f) return;

        _stepDistance += horizontalSpeed * Time.deltaTime;
        if (_stepDistance >= stepLength)
        {
            _stepDistance -= stepLength;

            if (SfxPlayer.Instance != null)
                SfxPlayer.Instance.PlayFootstep(transform.position, IsRunning);
        }
    }

    private void ClampToMapBounds()
    {
        if (MarchingCubes.Instance == null) return;

        Vector3 pos = transform.position;
        Vector3 correction = Vector3.zero;

        float minX = boundaryPadding;
        float maxX = MarchingCubes.Instance.MapWidth - boundaryPadding;
        float minZ = boundaryPadding;
        float maxZ = MarchingCubes.Instance.MapDepth - boundaryPadding;

        if (pos.x < minX) correction.x = minX - pos.x;
        else if (pos.x > maxX) correction.x = maxX - pos.x;

        if (pos.z < minZ) correction.z = minZ - pos.z;
        else if (pos.z > maxZ) correction.z = maxZ - pos.z;

        if (correction != Vector3.zero)
            characterController.Move(correction);
    }
    
    private void CheckFallOut()
    {
        if (transform.position.y < fallResetHeight)
        {
            characterController.enabled = false;
            transform.position = _lastGroundedPosition;
            characterController.enabled = true;
            moveDirection.y = 0f;
        }
    }
}