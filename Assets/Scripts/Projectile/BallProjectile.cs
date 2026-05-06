using UnityEngine;

namespace SmashHit.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    public class BallProjectile : MonoBehaviour
    {
        private const int MaxImpactColliders = 32;

        [SerializeField] private float maxLifetime = 6f;
        [SerializeField] private float maxDistanceFromCamera = 80f;
        [SerializeField] private float impactRadius = 1f;
        [SerializeField] private float impactImpulseMultiplier = 1.35f;
        [SerializeField] private float postImpactHorizontalVelocityRetention = 0.15f;
        [SerializeField] private float postImpactDownwardVelocity = 2f;
        [SerializeField] private LayerMask glassLayerMask = 1 << 9;

        private readonly Collider[] impactColliders = new Collider[MaxImpactColliders];
        private Transform cameraTransform;
        private Rigidbody projectileRigidbody;
        private float spawnTime;

        public void Initialize(Transform mainCamera, float lifetimeOverride)
        {
            cameraTransform = mainCamera;
            maxLifetime = lifetimeOverride;
            spawnTime = Time.time;
        }

        private void Awake()
        {
            projectileRigidbody = GetComponent<Rigidbody>();
            spawnTime = Time.time;
        }

        private void OnCollisionEnter(Collision collision)
        {
            var impactPoint = transform.position;
            if (collision.contactCount > 0)
            {
                impactPoint = collision.GetContact(0).point;
            }

            var impactDirection = ResolveImpactDirection();
            ApplyHeavyImpact(impactPoint, impactDirection);
            DropTowardFloor();
        }

        private void Update()
        {
            if (Time.time - spawnTime >= maxLifetime)
            {
                Destroy(gameObject);
                return;
            }

            if (cameraTransform == null)
            {
                return;
            }

            var distanceFromCamera = Vector3.Distance(transform.position, cameraTransform.position);
            if (distanceFromCamera > maxDistanceFromCamera)
            {
                Destroy(gameObject);
            }
        }

        private Vector3 ResolveImpactDirection()
        {
            if (projectileRigidbody != null && projectileRigidbody.linearVelocity.sqrMagnitude > 0.0001f)
            {
                return projectileRigidbody.linearVelocity.normalized;
            }

            return transform.forward;
        }

        private void ApplyHeavyImpact(Vector3 impactPoint, Vector3 impactDirection)
        {
            var colliderCount = Physics.OverlapSphereNonAlloc(
                impactPoint,
                impactRadius,
                impactColliders,
                glassLayerMask,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < colliderCount; i++)
            {
                var targetCollider = impactColliders[i];
                if (targetCollider == null)
                {
                    continue;
                }

                if (targetCollider.TryGetComponent<GlassFragment>(out var glassFragment))
                {
                    glassFragment.ApplyProjectileImpact(impactPoint, impactDirection, impactImpulseMultiplier);
                    continue;
                }

                if (targetCollider.TryGetComponent<GlassShard>(out var glassShard))
                {
                    glassShard.ApplyProjectileImpact(impactPoint, impactDirection, impactImpulseMultiplier);
                }
            }
        }

        private void DropTowardFloor()
        {
            if (projectileRigidbody == null)
            {
                return;
            }

            projectileRigidbody.useGravity = true;

            var velocity = projectileRigidbody.linearVelocity;
            var horizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up) *
                Mathf.Clamp01(postImpactHorizontalVelocityRetention);
            var downwardVelocity = Mathf.Min(velocity.y, -Mathf.Max(0f, postImpactDownwardVelocity));

            projectileRigidbody.linearVelocity = horizontalVelocity + Vector3.up * downwardVelocity;
        }
    }
}
