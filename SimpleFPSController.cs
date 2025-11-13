using UnityEngine;
using Unity.Cinemachine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class SimpleFPSController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform camRoot;         // CameraRoot (head)
    [SerializeField] private CinemachineCamera vcam;        // Cinemachine Camera (v3)
    [SerializeField] private CinemachineCameraOffset camOffset; // Head-bob için
    [SerializeField] private CinemachineBasicMultiChannelPerlin noise; // Mikro sallanma

    private CharacterController cc;
    private Vector3 velocityY;
    private Vector3 defaultOffset;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 2.2f;
    [SerializeField] private float runSpeed = 3.6f;
    [SerializeField] private bool allowRun = true; // Koşmaya izin ver
    [SerializeField] private float gravity = -19.62f;
    [SerializeField] private float groundStick = -2f;

    [Header("Look (mouse bizde)")]
    [SerializeField] private float mouseSens = 1.2f;
    [SerializeField] private float lookSmooth = 12f;
    [SerializeField] private float minPitch = -75f;
    [SerializeField] private float maxPitch = 75f;
    private float yaw, pitch, currYaw, currPitch;

    [Header("Lean (roll → Lens.Dutch)")]
    [SerializeField] private float strafeLeanDeg = 3f;
    [SerializeField] private float leanSmooth = 8f;
    private float roll;

    [Header("Head Bob (CameraOffset)")]
    [SerializeField] private float bobFreq = 1.7f;
    [SerializeField] private float bobHorAmp = 0.03f;
    [SerializeField] private float bobVerAmp = 0.02f;
    [SerializeField] private float bobSmooth = 10f;
    private float bobTime;
    private Vector3 bobOffset;

    [Header("Noise (opsiyonel)")]
    [SerializeField] private float noiseFrequency = 0.8f;  // 0.5–1.2 iyi
    [SerializeField] private float noiseAmplitude = 0.3f;  // 0.0–0.5 arası mikro

    [Header("Misc")]
    [SerializeField] private KeyCode unlockCursorKey = KeyCode.Escape;

    [Header("Strafe Kaydırma")]
    [SerializeField] private float strafeShift = 0.08f;     // A/D ile x ekseninde kayma (metre)
    [SerializeField] private float strafeShiftSmooth = 10f; // 8–14 arası iyi
    private float strafeBlend = 0f;                   // -1..+1 (smooth)
    [Header("Strafe (A/D) yaw eklemesi")]
    [SerializeField] private float strafeYawDeg = 2.0f; // A/D’de küçük lokal yaw


    [Header("Peek (Q/E)")]
    [SerializeField] private float peekShift = 0.18f;       // Q/E ile ekstra kayma
    [SerializeField] private float peekRollDeg = 6f;        // Q/E’de ek roll
    [SerializeField] private float peekYawDeg = 2.0f;       // Q/E’de küçük lokal yaw
    [SerializeField] private float peekSmooth = 12f;        // 10–16 arası iyi
    private float peek = 0f;                        // -1 (Q) .. +1 (E)

    [Header("Run Shake")]
    [SerializeField] private float runNoiseAmp = 0.6f;   // Koşarken ekstra şiddet
    [SerializeField] private float runNoiseFreq = 1.0f;  // Koşarken ekstra hız
    [SerializeField] private float noiseBlend = 10f;     // Shake geçiş hızı (smooth)
    [SerializeField] private KeyCode rotateKey = KeyCode.R;
    private float _currAmp, _currFreq; // Dahili smooth değerler

    private bool isMoving = false;
    private bool running = false;


    // Koşuya bağlı sarsıntı ve kick ile ilgili değişkenler kaldırıldı.

    void Awake()
    {
        cc = GetComponent<CharacterController>();

        if (!vcam) vcam = FindFirstObjectByType<CinemachineCamera>();
        if (vcam)
        {
            if (!camOffset) camOffset = vcam.GetComponent<CinemachineCameraOffset>();
            if (!noise) noise = vcam.GetComponent<CinemachineBasicMultiChannelPerlin>();

            // CameraOffset yoksa ekle
            if (!camOffset) camOffset = vcam.GetComponent<CinemachineCameraOffset>();
            if (!camOffset) camOffset = vcam.gameObject.AddComponent<CinemachineCameraOffset>();

            // Perlin Noise yoksa ekle (opsiyonel)
            if (!noise) noise = vcam.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (!noise) noise = vcam.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
        }
        if (camOffset) defaultOffset = camOffset.Offset;
        // Başlangıçta inspector’daki mikro değerlerle başla
        _currAmp = noiseAmplitude;
        _currFreq = noiseFrequency;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(unlockCursorKey))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        //LookUpdate();

        if (Input.GetKey(rotateKey) == false)//düzelt bunu 
        {
            LookUpdate();
            MoveUpdate();
        }
        //MoveUpdate();
        
        CameraFXUpdate();
        NoiseUpdate();    // Sadece sabit noise için
    }
    

    void LookUpdate()
    {
        float mx = Input.GetAxis("Mouse X");
        float my = Input.GetAxis("Mouse Y");

        yaw += mx * mouseSens;
        pitch -= my * mouseSens;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        float s = 1f - Mathf.Exp(-lookSmooth * Time.deltaTime);
        currYaw = Mathf.Lerp(currYaw, yaw, s);
        currPitch = Mathf.Lerp(currPitch, pitch, s);

        transform.rotation = Quaternion.Euler(0f, currYaw, 0f);
        camRoot.localRotation = Quaternion.Euler(currPitch, 0f, 0f);
    }

    void MoveUpdate()
    {
        Vector2 moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        moveInput = Vector2.ClampMagnitude(moveInput, 1f);
        Vector2 moveInputw = new Vector2(0 ,0);

        if(moveInput != moveInputw && isMoving == false)
        {
            //Debug.Log("Hareket Ediliyor");
            //noise.AmplitudeGain = 1000f;
            // noise.FrequencyGain = 10f;
            isMoving = true;
        }
        else
        {
            if(isMoving == true) isMoving = false;
            //noise.AmplitudeGain = 0f;
        }

            // --- KOŞMA MEKANİĞİ BURADA ---
        float targetSpeed = allowRun && Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
        

        Vector3 wishDir = (transform.right * moveInput.x + transform.forward * moveInput.y);
        Vector3 horizontalMove = wishDir * targetSpeed;

        if (cc.isGrounded && velocityY.y < 0f) velocityY.y = groundStick;
        velocityY.y += gravity * Time.deltaTime;

        cc.Move(horizontalMove * Time.deltaTime);
        cc.Move(velocityY * Time.deltaTime);
    }

    void CameraFXUpdate()
    {
        // ----- Head-bob -----
        Vector3 vel = cc.velocity;
        float hSpeed = new Vector2(vel.x, vel.z).magnitude;
        bool moving = cc.isGrounded && hSpeed > 0.05f;

        float t = bobTime;
        if (moving) t += hSpeed * bobFreq * Time.deltaTime;
        else t = Mathf.Lerp(t, 0f, 1f - Mathf.Exp(-bobSmooth * Time.deltaTime));
        bobTime = t;

        Vector3 targetBob = new Vector3(
            Mathf.Sin(t * Mathf.PI * 2f) * bobHorAmp,
            Mathf.Abs(Mathf.Cos(t * Mathf.PI * 2f)) * bobVerAmp,
            0f
        );
        bobOffset = Vector3.Lerp(bobOffset, targetBob, 1f - Mathf.Exp(-bobSmooth * Time.deltaTime));

        // --- Koşuya bağlı sarsıntı ve footstep kick kodları buradan kaldırıldı ---

        // ----- A/D → strafe offset -----
        float inputX = Input.GetAxisRaw("Horizontal"); // -1 .. +1
        float s1 = 1f - Mathf.Exp(-strafeShiftSmooth * Time.deltaTime);
        strafeBlend = Mathf.Lerp(strafeBlend, inputX, s1);
        Vector3 strafeOffset = new Vector3(strafeBlend * strafeShift, 0f, 0f);

        // ----- Q/E → peek offset -----
        float peekTarget = 0f;
        if (Input.GetKey(KeyCode.Q)) peekTarget -= 1f;
        if (Input.GetKey(KeyCode.E)) peekTarget += 1f;
        float s2 = 1f - Mathf.Exp(-peekSmooth * Time.deltaTime);
        peek = Mathf.Lerp(peek, Mathf.Clamp(peekTarget, -1f, 1f), s2);
        Vector3 peekOffset = new Vector3(peek * peekShift, 0f, 0f);

        // ----- Toplam konumsal ofset -----
        if (camOffset)
            camOffset.Offset = defaultOffset + bobOffset + strafeOffset + peekOffset;

        // ----- Roll (Dutch): A/D + Q/E birlikte -----
        float targetRoll = (-inputX * strafeLeanDeg) + (peek * peekRollDeg);
        float r = 1f - Mathf.Exp(-leanSmooth * Time.deltaTime);
        roll = Mathf.Lerp(roll, targetRoll, r);

        if (vcam)
        {
            var lens = vcam.Lens;
            lens.Dutch = roll;
            vcam.Lens = lens;
        }

        // ----- Lokal yaw enjeksiyonu: A/D de Q/E gibi küçük yaw ekle -----
        if (camRoot)
        {
            float extraYaw = (strafeBlend * strafeYawDeg) + (peek * peekYawDeg);
            Quaternion baseRot = Quaternion.Euler(currPitch, 0f, 0f);
            Quaternion yawAdj = Quaternion.Euler(0f, extraYaw, 0f);
            camRoot.localRotation = baseRot * yawAdj;
        }
    }

    void NoiseUpdate()
    {
        if (!noise) return;

        if (Input.GetKey(KeyCode.LeftShift) == true && allowRun && isMoving)
        {
            //Debug.Log("koşuluyor");
            running = true;
            //noise.AmplitudeGain = 1000f;
            // noise.FrequencyGain = 10f;
        }
        else
        {
            running = false;
            //noise.AmplitudeGain = 0f;
        }

            // Hedef değerler: yürürken/boştayken inspector’daki mikro noise,
            // koşarken buna ekstra ekleme (toplanır).
        float targetAmp = running ?  runNoiseAmp : noiseAmplitude;
        float targetFreq = running ? runNoiseFreq : noiseFrequency;

        // Exponential smoothing ile yumuşak geçiş
        float s = 1f - Mathf.Exp(-noiseBlend * Time.deltaTime);
        _currAmp = Mathf.Lerp(_currAmp, targetAmp, s);
        _currFreq = Mathf.Lerp(_currFreq, targetFreq, s);

        noise.AmplitudeGain = _currAmp;
        noise.FrequencyGain = _currFreq;
    }


}