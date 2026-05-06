using UnityEngine;

namespace SmashHit.Gameplay
{
    [RequireComponent(typeof(Collider))]
    public class GlassFragment : MonoBehaviour
    {
        [SerializeField] private float cleanupDelayAfterBreak = 1.5f;
        [Header("Fracture")]
        [SerializeField] private int fractureRayCount = 12;
        [SerializeField] private int fractureRingCount = 3;
        [SerializeField] private float fractureJitter = 0.35f;
        [SerializeField] private float minimumShardArea = 0.0025f;
        [SerializeField] private float minimumVisibleShardArea = 0.01f;
        [SerializeField] private float minimumSupportedShardArea = 0.04f;
        [SerializeField] private float disconnectRadius = 0.18f;
        [SerializeField] private float impactDetachRadius = 0.12f;
        [SerializeField] private float detachedShardLifetime = 4f;
        [SerializeField] private float detachedShardImpulse = 4.5f;
        [SerializeField] private float detachedShardUpwardBias = 0.25f;
        [SerializeField] private int fractureSeed;
        [Header("Camera Clearance")]
        [SerializeField] private Vector2 cameraPathWorldCenter = Vector2.zero;
        [SerializeField] private float cameraClearanceRadius = 0.45f;

        private GameStateController gameState;
        private float moveSpeed;
        private float loseLineZ;
        private bool hasReachedLoseLine;

        public bool IsBroken { get; private set; }

        public void Initialize(GameStateController targetGameState, float speed, float loseZ)
        {
            gameState = targetGameState;
            moveSpeed = speed;
            loseLineZ = loseZ;
            IsBroken = false;
            hasReachedLoseLine = false;
        }

        private void Update()
        {
            if (gameState != null && !gameState.IsPlaying)
            {
                return;
            }

            transform.position += Vector3.back * (moveSpeed * Time.deltaTime);

            if (hasReachedLoseLine || IsBroken)
            {
                return;
            }

            if (transform.position.z <= loseLineZ)
            {
                hasReachedLoseLine = true;
                if (GlassPassageUtility.FullPaneBlocksCameraPath(
                    transform,
                    cameraPathWorldCenter,
                    cameraClearanceRadius))
                {
                    gameState?.Lose("Glass fragment blocked the camera path.");
                }

                Break();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsBroken)
            {
                return;
            }

            if (!collision.collider.TryGetComponent<BallProjectile>(out _))
            {
                return;
            }

            Break(collision);
        }

        public void Break()
        {
            BreakAt(Vector2.zero, Vector3.back, false, 1f);
        }

        public void ApplyProjectileImpact(Vector3 worldPoint, Vector3 impactDirection, float impulseMultiplier = 1f)
        {
            var localImpact = transform.InverseTransformPoint(worldPoint);
            var resolvedImpactDirection = impactDirection.sqrMagnitude > 0.0001f
                ? impactDirection.normalized
                : Vector3.back;

            BreakAt(new Vector2(localImpact.x, localImpact.y), resolvedImpactDirection, true, impulseMultiplier);
        }

        private void Break(Collision collision)
        {
            var contactPoint = transform.position;
            if (collision.contactCount > 0)
            {
                contactPoint = collision.GetContact(0).point;
            }

            var projectileVelocity = collision.rigidbody == null ? Vector3.zero : collision.rigidbody.linearVelocity;
            var impactDirection = projectileVelocity.sqrMagnitude > 0.0001f
                ? projectileVelocity.normalized
                : Vector3.back;

            ApplyProjectileImpact(contactPoint, impactDirection);
        }

        private void BreakAt(Vector2 localImpact, Vector3 impactDirection, bool spawnShards, float impulseMultiplier)
        {
            if (IsBroken)
            {
                return;
            }

            IsBroken = true;
            localImpact = ClampToPane(localImpact);

            if (spawnShards)
            {
                SpawnFracturedShards(localImpact, impactDirection, impulseMultiplier);
            }

            if (TryGetComponent<Collider>(out var colliderComponent))
            {
                colliderComponent.enabled = false;
            }

            foreach (var rendererComponent in GetComponentsInChildren<MeshRenderer>())
            {
                rendererComponent.enabled = false;
            }

            Destroy(gameObject, cleanupDelayAfterBreak);
        }

        private void SpawnFracturedShards(Vector2 localImpact, Vector3 impactDirection, float impulseMultiplier)
        {
            var options = new GlassFractureGenerator.Options(
                fractureRayCount,
                fractureRingCount,
                fractureJitter,
                minimumShardArea,
                ResolveFractureSeed());
            var fragments = GlassFractureGenerator.Generate(localImpact, options);
            if (fragments.Count == 0)
            {
                return;
            }

            var graph = GlassFragmentGraph.Build(fragments, localImpact, disconnectRadius);
            var supportConnected = graph.FindMainSupportConnected(fragments);
            ForceImpactFragmentsToDetach(fragments, supportConnected, localImpact);

            var materials = ResolveShardMaterials();
            var inheritedVelocity = Vector3.back * moveSpeed;
            for (var i = 0; i < fragments.Count; i++)
            {
                if (ShouldCullFragment(fragments[i]))
                {
                    continue;
                }

                var isSupportConnected = supportConnected[i] && IsLargeEnoughToStaySupported(fragments[i]);
                SpawnShard(fragments[i], isSupportConnected, materials, inheritedVelocity, impactDirection, localImpact, impulseMultiplier);
            }
        }

        private void ForceImpactFragmentsToDetach(
            System.Collections.Generic.IReadOnlyList<GlassFractureGenerator.Fragment> fragments,
            bool[] supportConnected,
            Vector2 localImpact)
        {
            var detachedAny = false;
            var nearestIndex = 0;
            var nearestDistance = float.PositiveInfinity;
            var detachRadiusSqr = impactDetachRadius * impactDetachRadius;

            for (var i = 0; i < fragments.Count; i++)
            {
                var distanceSqr = (fragments[i].Center - localImpact).sqrMagnitude;
                if (distanceSqr < nearestDistance)
                {
                    nearestDistance = distanceSqr;
                    nearestIndex = i;
                }

                if (distanceSqr <= detachRadiusSqr)
                {
                    supportConnected[i] = false;
                    detachedAny = true;
                }
                else if (!supportConnected[i])
                {
                    detachedAny = true;
                }
            }

            if (!detachedAny && fragments.Count > 0)
            {
                supportConnected[nearestIndex] = false;
            }
        }

        private void SpawnShard(
            GlassFractureGenerator.Fragment fragment,
            bool isSupportConnected,
            Material[] materials,
            Vector3 inheritedVelocity,
            Vector3 impactDirection,
            Vector2 localImpact,
            float impulseMultiplier)
        {
            var shardObject = new GameObject(isSupportConnected ? "SupportedGlassShard" : "DetachedGlassShard");
            var shardTransform = shardObject.transform;
            shardTransform.SetParent(transform.parent, false);
            shardTransform.localPosition = transform.localPosition;
            shardTransform.localRotation = transform.localRotation;
            shardTransform.localScale = transform.localScale;

            var shard = shardObject.AddComponent<GlassShard>();
            shard.Build(fragment.Polygon, 1f, materials, gameObject.layer);

            if (isSupportConnected)
            {
                shard.InitializeSupported(
                    gameState,
                    moveSpeed,
                    loseLineZ,
                    fractureRayCount,
                    fractureRingCount,
                    fractureJitter,
                    minimumShardArea,
                    minimumVisibleShardArea,
                    minimumSupportedShardArea,
                    disconnectRadius,
                    impactDetachRadius,
                    detachedShardLifetime,
                    detachedShardImpulse,
                    detachedShardUpwardBias,
                    fractureSeed,
                    cameraPathWorldCenter,
                    cameraClearanceRadius);
                return;
            }

            var impulse = BuildShardImpulse(fragment.Center, localImpact, impactDirection) * Mathf.Max(0f, impulseMultiplier);
            var mass = Mathf.Max(0.02f, fragment.Area * 0.35f);
            shard.InitializeDetached(inheritedVelocity, impulse, mass, detachedShardLifetime);
        }

        private Vector3 BuildShardImpulse(Vector2 fragmentCenter, Vector2 localImpact, Vector3 impactDirection)
        {
            var localDirection = fragmentCenter - localImpact;
            var radialDirection = localDirection.sqrMagnitude > 0.0001f
                ? transform.TransformDirection(new Vector3(localDirection.x, localDirection.y, 0f)).normalized
                : Random.onUnitSphere;

            var direction = radialDirection + impactDirection.normalized + Vector3.up * detachedShardUpwardBias;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.back;
            }

            return direction.normalized * detachedShardImpulse;
        }

        private bool ShouldCullFragment(GlassFractureGenerator.Fragment fragment)
        {
            return fragment.Area < Mathf.Max(minimumShardArea, minimumVisibleShardArea);
        }

        private bool IsLargeEnoughToStaySupported(GlassFractureGenerator.Fragment fragment)
        {
            return fragment.Area >= Mathf.Max(minimumVisibleShardArea, minimumSupportedShardArea);
        }

        private Material[] ResolveShardMaterials()
        {
            if (TryGetComponent<MeshRenderer>(out var meshRenderer) && meshRenderer.sharedMaterials.Length > 0)
            {
                return meshRenderer.sharedMaterials;
            }

            var childRenderer = GetComponentInChildren<MeshRenderer>();
            return childRenderer == null ? System.Array.Empty<Material>() : childRenderer.sharedMaterials;
        }

        private int ResolveFractureSeed()
        {
            if (fractureSeed != 0)
            {
                return fractureSeed;
            }

            return GetInstanceID() ^ Mathf.RoundToInt(Time.time * 1000f);
        }

        private static Vector2 ClampToPane(Vector2 point)
        {
            return new Vector2(
                Mathf.Clamp(point.x, -0.5f, 0.5f),
                Mathf.Clamp(point.y, -0.5f, 0.5f));
        }
    }
}
