using System.Collections.Generic;
using UnityEngine;

namespace SmashHit.Gameplay
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class GlassShard : MonoBehaviour
    {
        private const float DefaultCleanupDelay = 0.05f;

        private GameStateController gameState;
        private IReadOnlyList<Vector2> polygon;
        private Material[] materials;
        private float moveSpeed;
        private float loseLineZ;
        private float disconnectRadius;
        private float impactDetachRadius;
        private float detachedShardLifetime;
        private float detachedShardImpulse;
        private float detachedShardUpwardBias;
        private Vector2 cameraPathWorldCenter;
        private float cameraClearanceRadius;
        private int fractureRayCount;
        private int fractureRingCount;
        private float fractureJitter;
        private float minimumShardArea;
        private float minimumVisibleShardArea;
        private float minimumSupportedShardArea;
        private int fractureSeed;
        private int shardLayer;
        private bool isSupported;
        private bool hasFractured;
        private bool hasReachedLoseLine;

        public void Build(IReadOnlyList<Vector2> polygon, float thickness, Material[] materials, int layer)
        {
            this.polygon = new List<Vector2>(polygon);
            this.materials = materials;
            shardLayer = layer;

            var mesh = BuildExtrudedMesh(polygon, thickness);
            mesh.name = "Generated Glass Shard";

            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterials = materials;

            var meshCollider = GetComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = true;

            gameObject.layer = layer;
        }

        public void InitializeSupported(
            GameStateController targetGameState,
            float speed,
            float loseZ,
            int rayCount,
            int ringCount,
            float jitter,
            float minArea,
            float minVisibleArea,
            float minSupportedArea,
            float supportDisconnectRadius,
            float impactRadius,
            float detachedLifetime,
            float detachedImpulse,
            float upwardBias,
            int seed,
            Vector2 pathWorldCenter,
            float clearanceRadius)
        {
            gameState = targetGameState;
            moveSpeed = speed;
            loseLineZ = loseZ;
            fractureRayCount = rayCount;
            fractureRingCount = ringCount;
            fractureJitter = jitter;
            minimumShardArea = minArea;
            minimumVisibleShardArea = minVisibleArea;
            minimumSupportedShardArea = minSupportedArea;
            disconnectRadius = supportDisconnectRadius;
            impactDetachRadius = impactRadius;
            detachedShardLifetime = detachedLifetime;
            detachedShardImpulse = detachedImpulse;
            detachedShardUpwardBias = upwardBias;
            cameraPathWorldCenter = pathWorldCenter;
            cameraClearanceRadius = clearanceRadius;
            fractureSeed = seed;
            isSupported = true;
            hasFractured = false;
            hasReachedLoseLine = false;
        }

        public void InitializeDetached(Vector3 inheritedVelocity, Vector3 impulse, float mass, float lifetime)
        {
            isSupported = false;

            var body = gameObject.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.01f, mass);
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = inheritedVelocity;
            body.AddForce(impulse, ForceMode.Impulse);
            body.AddTorque(Random.onUnitSphere * impulse.magnitude * 0.25f, ForceMode.Impulse);

            Destroy(gameObject, lifetime);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isSupported || hasFractured)
            {
                return;
            }

            if (!collision.collider.TryGetComponent<BallProjectile>(out _))
            {
                return;
            }

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

        private void Update()
        {
            if (!isSupported)
            {
                return;
            }

            if (gameState != null && !gameState.IsPlaying)
            {
                return;
            }

            transform.position += Vector3.back * (moveSpeed * Time.deltaTime);

            if (hasReachedLoseLine || transform.position.z > loseLineZ)
            {
                return;
            }

            hasReachedLoseLine = true;
            if (GlassPassageUtility.BlocksCameraPath(
                polygon,
                transform,
                cameraPathWorldCenter,
                cameraClearanceRadius))
            {
                gameState?.Lose("Glass shard blocked the camera path.");
                return;
            }

            Destroy(gameObject);
        }

        public void ApplyProjectileImpact(Vector3 worldPoint, Vector3 impactDirection, float impulseMultiplier = 1f)
        {
            if (!isSupported || hasFractured)
            {
                return;
            }

            var resolvedImpactDirection = impactDirection.sqrMagnitude > 0.0001f
                ? impactDirection.normalized
                : Vector3.back;

            Fracture(worldPoint, resolvedImpactDirection, impulseMultiplier);
        }

        private void Fracture(Vector3 worldPoint, Vector3 impactDirection, float impulseMultiplier)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return;
            }

            hasFractured = true;

            var localImpact3 = transform.InverseTransformPoint(worldPoint);
            var localImpact = ClampToPane(new Vector2(localImpact3.x, localImpact3.y));

            var options = new GlassFractureGenerator.Options(
                fractureRayCount,
                fractureRingCount,
                fractureJitter,
                minimumShardArea,
                ResolveFractureSeed());
            var fragments = GlassFractureGenerator.Generate(localImpact, options, polygon);
            if (fragments.Count == 0)
            {
                hasFractured = false;
                return;
            }

            var graph = GlassFragmentGraph.Build(fragments, localImpact, disconnectRadius);
            var supportConnected = graph.FindMainSupportConnected(fragments);
            ForceImpactFragmentsToDetach(fragments, supportConnected, localImpact);

            var inheritedVelocity = Vector3.back * moveSpeed;
            for (var i = 0; i < fragments.Count; i++)
            {
                if (ShouldCullFragment(fragments[i]))
                {
                    continue;
                }

                var isSupportConnected = supportConnected[i] && IsLargeEnoughToStaySupported(fragments[i]);
                SpawnShard(fragments[i], isSupportConnected, inheritedVelocity, impactDirection, localImpact, impulseMultiplier);
            }

            Destroy(gameObject, DefaultCleanupDelay);
        }

        private void SpawnShard(
            GlassFractureGenerator.Fragment fragment,
            bool isSupportConnected,
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
            shard.Build(fragment.Polygon, 1f, materials, shardLayer);

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

        private void ForceImpactFragmentsToDetach(
            IReadOnlyList<GlassFractureGenerator.Fragment> fragments,
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

        private static Mesh BuildExtrudedMesh(IReadOnlyList<Vector2> polygon, float thickness)
        {
            var clampedThickness = Mathf.Max(0.01f, thickness);
            var halfThickness = clampedThickness * 0.5f;
            var vertexCount = polygon.Count * 2;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];

            for (var i = 0; i < polygon.Count; i++)
            {
                var point = polygon[i];
                vertices[i] = new Vector3(point.x, point.y, -halfThickness);
                vertices[i + polygon.Count] = new Vector3(point.x, point.y, halfThickness);
                normals[i] = Vector3.back;
                normals[i + polygon.Count] = Vector3.forward;
            }

            var triangles = new List<int>((polygon.Count - 2) * 6 + polygon.Count * 6);

            for (var i = 1; i < polygon.Count - 1; i++)
            {
                triangles.Add(0);
                triangles.Add(i + 1);
                triangles.Add(i);

                triangles.Add(polygon.Count);
                triangles.Add(polygon.Count + i);
                triangles.Add(polygon.Count + i + 1);
            }

            for (var i = 0; i < polygon.Count; i++)
            {
                var next = (i + 1) % polygon.Count;
                var frontA = i;
                var frontB = next;
                var backA = i + polygon.Count;
                var backB = next + polygon.Count;

                triangles.Add(frontA);
                triangles.Add(backA);
                triangles.Add(backB);

                triangles.Add(frontA);
                triangles.Add(backB);
                triangles.Add(frontB);
            }

            var mesh = new Mesh
            {
                vertices = vertices,
                triangles = triangles.ToArray(),
                normals = normals
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
