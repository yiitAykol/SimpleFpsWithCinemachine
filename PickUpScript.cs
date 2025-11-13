using UnityEngine;

public class PickUpScript : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject player;
    [SerializeField] private Transform holdPos;                 // Elde tutulacak hedef nokta (kamera önünde boş child)
    [SerializeField] private Collider playerCollider;           // Oyuncu collider'ı (boşsa Start'ta bulunur)
    [SerializeField] private Transform rayOrigin;               // Genelde kamera transformu; boşsa this.transform

    [Header("Interaction")]
    [SerializeField] private string pickableTag = "canPickUp";  // Alınabilir objelerin tag’i
    [SerializeField] private LayerMask interactableMask = ~0;   // Raycast’in vuracağı katmanlar
    [SerializeField] private float pickUpRange = 5f;
    [SerializeField] private KeyCode pickKey = KeyCode.E;
    [SerializeField] private KeyCode rotateKey = KeyCode.R;
    [SerializeField] private KeyCode throwKey = KeyCode.Mouse0;

    [Header("Hold & Movement")]
    [Tooltip("Tutarken nesnenin taşınacağı katman (duvar çarpışmalarını azaltır).")]
    [SerializeField] private string holdLayerName = "holdLayer";
    [SerializeField, Range(0.05f, 20f)] private float throwForce = 8f; // Impulse (m/s * mass) gibi düşün
    [SerializeField, Range(0.01f, 30f)] private float followSmoothTime = 0.06f; // SmoothDamp süresi
    [SerializeField, Range(0.1f, 15f)] private float maxFollowSpeed = 10f;
    [SerializeField] private bool smoothFollow = true;          // Yumuşak takip (SmoothDamp + MovePosition)
    [SerializeField] private bool parentWhileHolding = false;   // İstersen doğrudan parent et (basit ama daha "oyuncak" hissi)

    [Header("Rotation")]
    [SerializeField, Range(0.1f, 10f)] private float rotationSensitivity = 1f;
    [SerializeField] private bool invertY = false;

    [Header("Drop / Placement")]
    [Tooltip("Bırakırken önündeki engellere dolan: duvar içinden düşmeyi engeller.")]
    [SerializeField] private bool preventClippingOnDrop = true;
    [Tooltip("Bırakırken alt yüzeye snap: masanın üstüne düzgün yerleşsin.")]
    [SerializeField] private bool snapToSurfaceOnDrop = true;
    [SerializeField] private float snapCastDistance = 2.0f;
    [SerializeField] private float snapOffsetY = 0.02f;
    [SerializeField] private QueryTriggerInteraction triggerHits = QueryTriggerInteraction.Ignore;

    // Runtime
    private GameObject heldObj;
    private Rigidbody heldObjRb;
    private bool canDrop = true;                 // R ile döndürürken drop/throw kilidi
    private int holdLayer;
    private int originalLayer;
    private Transform originalParent;

    // SmoothDamp için
    private Vector3 velocityFollow;

    private void OnValidate()
    {
        if (!rayOrigin) rayOrigin = transform;
    }

    private void Start()
    {
        holdLayer = LayerMask.NameToLayer(holdLayerName);
        if (holdLayer == -1)
        {
            Debug.LogWarning($"[PickUpScript] '{holdLayerName}' isminde bir Layer bulunamadı. Default Layer kullanılacak.");
            holdLayer = 0;
        }

        if (!playerCollider && player)
            playerCollider = player.GetComponentInChildren<Collider>();

        if (!rayOrigin) rayOrigin = transform;
        if (!holdPos)
        {
            // Otomatik bir holdPos oluştur (kamera önüne küçük offset)
            GameObject hp = new GameObject("HoldPos_Auto");
            hp.transform.SetParent(rayOrigin, false);
            hp.transform.localPosition = new Vector3(0f, -0.05f, 1.0f);
            holdPos = hp.transform;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(pickKey))
        {
            if (heldObj == null)
                TryPickUp();
            else if (canDrop)
                DropObject();
        }

        if (heldObj != null)
        {
            RotateHeldObject();
            if (Input.GetKeyDown(throwKey) && canDrop)
                ThrowObject();
        }
    }

    private void FixedUpdate()
    {
        if (heldObj != null)
            FollowHoldPosition();
    }

    private void TryPickUp()
    {
        if (!rayOrigin) rayOrigin = transform;

        if (Physics.Raycast(rayOrigin.position, rayOrigin.forward, out var hit, pickUpRange, interactableMask, triggerHits))
        {
            var go = hit.transform.gameObject;
            if (!go.CompareTag(pickableTag)) return;

            var rb = go.GetComponent<Rigidbody>();
            if (!rb)
            {
                Debug.LogWarning("[PickUpScript] Nesnenin Rigidbody'si yok.");
                return;
            }

            PickUpObject(go, rb);
        }
    }

    private void PickUpObject(GameObject pickUpObj, Rigidbody rb)
    {
        heldObj = pickUpObj;
        heldObjRb = rb;

        originalLayer = heldObj.layer;
        originalParent = heldObj.transform.parent;

        if (playerCollider && heldObj.TryGetComponent(out Collider col))
            Physics.IgnoreCollision(col, playerCollider, true);

        heldObj.layer = holdLayer;

        if (parentWhileHolding)
        {
            // Basit yöntem: kinematik + parent
            heldObjRb.isKinematic = true;
            heldObj.transform.SetParent(holdPos, worldPositionStays: true);
            heldObj.transform.position = holdPos.position;
        }
        else
        {
            // Fiziksel takip: kinematik tutup MovePosition ile yumuşak taşı
            heldObjRb.isKinematic = true;
            heldObjRb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void FollowHoldPosition()
    {
        if (!heldObjRb) return;

        if (parentWhileHolding)
        {
            // Parent’lı modda zaten holdPos ile beraber gelir, güvenlik için:
            heldObj.transform.position = holdPos.position;
            return;
        }

        if (smoothFollow)
        {
            Vector3 target = holdPos.position;
            Vector3 newPos = Vector3.SmoothDamp(heldObj.transform.position, target, ref velocityFollow, followSmoothTime, maxFollowSpeed);
            heldObjRb.MovePosition(newPos);
        }
        else
        {
            heldObjRb.MovePosition(holdPos.position);
        }
    }

    private void RotateHeldObject()
    {
        if (!Input.GetKey(rotateKey))
        {
            canDrop = true;
            return;
        }

        canDrop = false;

        float mx = Input.GetAxis("Mouse X") * rotationSensitivity;
        float my = Input.GetAxis("Mouse Y") * rotationSensitivity * (invertY ? 1f : -1f);

        // Dünya eksenlerinde rahat döndürme:
        heldObj.transform.Rotate(Vector3.up, mx, Space.World);
        heldObj.transform.Rotate(Vector3.right, my, Space.World);
    }

    private void DropObject()
    {
        if (heldObj == null) return;

        if (preventClippingOnDrop)
            PreventClipping();

        // İsteğe bağlı yüzeye snap
        if (snapToSurfaceOnDrop)
            TrySnapToSurface();

        RestoreAndClear(throwImpulse: Vector3.zero);
    }

    private void ThrowObject()
    {
        if (heldObj == null) return;

        if (preventClippingOnDrop)
            PreventClipping();

        Vector3 impulse = rayOrigin.forward * throwForce;
        RestoreAndClear(impulse);
    }

    private void RestoreAndClear(Vector3 throwImpulse)
    {
        // Parent’ı geri al
        heldObj.transform.SetParent(originalParent, worldPositionStays: true);

        // Katman geri
        heldObj.layer = originalLayer;

        // Fizik geri
        heldObjRb.isKinematic = false;
        heldObjRb.interpolation = RigidbodyInterpolation.None;

        // Oyuncu çarpışması geri
        if (playerCollider && heldObj.TryGetComponent(out Collider col))
            Physics.IgnoreCollision(col, playerCollider, false);

        // Fırlatma
        if (throwImpulse != Vector3.zero)
            heldObjRb.AddForce(throwImpulse, ForceMode.Impulse);

        // Temizle
        heldObj = null;
        heldObjRb = null;
        velocityFollow = Vector3.zero;
        canDrop = true;
    }

    private void PreventClipping()
    {
        // Kamera → holdPos doğrultusunda engel var mı, kontrol et
        Vector3 start = rayOrigin.position;
        Vector3 dir = (holdPos.position - start).normalized;
        float dist = Vector3.Distance(start, holdPos.position);

        // Nesnenin yarıçapını tahmin etmek için bounds kullan
        float radius = 0.15f;
        if (heldObj.TryGetComponent(out Collider col))
            radius = Mathf.Max(0.05f, Mathf.Min(col.bounds.extents.x, col.bounds.extents.y, col.bounds.extents.z));

        if (Physics.SphereCast(start, radius, dir, out var hit, dist, interactableMask, triggerHits))
        {
            // Engel varsa nesneyi daha yakına çek
            Vector3 safePos = hit.point - dir * (radius + 0.01f);
            heldObj.transform.position = safePos;
        }
    }

    private void TrySnapToSurface()
    {
        // Nesnenin altına doğru ray at, yakın yüzey varsa hafif offset ile oturt
        Vector3 origin = heldObj.transform.position + Vector3.up * 0.05f;
        if (Physics.Raycast(origin, Vector3.down, out var hit, snapCastDistance, interactableMask, triggerHits))
        {
            Vector3 p = hit.point;
            p.y += snapOffsetY;
            heldObj.transform.position = p;

            // Yüzeye normaline kabaca hizalama (çok agresif değil)
            Quaternion align = Quaternion.FromToRotation(heldObj.transform.up, hit.normal) * heldObj.transform.rotation;
            heldObj.transform.rotation = align;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!rayOrigin) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(rayOrigin.position, rayOrigin.position + rayOrigin.forward * pickUpRange);
        if (holdPos)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(holdPos.position, 0.05f);
        }
    }
#endif
}
