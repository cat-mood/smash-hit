using UnityEngine;
using UnityEngine.InputSystem;

namespace SmashHit.Gameplay
{
    public class BallThrower : MonoBehaviour
    {
        [SerializeField] private GameStateController gameState;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private BallProjectile projectilePrefab;
        [SerializeField] private float spawnOffsetFromCamera = 1f;
        [SerializeField] private float throwSpeed = 45f;
        [SerializeField] private float throwCooldown = 0.12f;
        [SerializeField] private float projectileLifetime = 6f;

        private float nextAllowedThrowTime;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (gameState != null && !gameState.IsPlaying)
            {
                return;
            }

            if (!TryGetThrowScreenPosition(out var screenPosition))
            {
                return;
            }

            if (Time.time < nextAllowedThrowTime)
            {
                return;
            }

            Throw(screenPosition);
            nextAllowedThrowTime = Time.time + throwCooldown;
        }

        private static bool TryGetThrowScreenPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = touch.primaryTouch.position.ReadValue();
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }
#endif

            screenPosition = default;
            return false;
        }

        private void Throw(Vector2 screenPosition)
        {
            if (projectilePrefab == null || targetCamera == null)
            {
                return;
            }

            var throwRay = targetCamera.ScreenPointToRay(screenPosition);
            var spawnPosition = throwRay.GetPoint(spawnOffsetFromCamera);
            var projectile = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);
            projectile.Initialize(targetCamera.transform, projectileLifetime);

            var rigidbody = projectile.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.linearVelocity = throwRay.direction * throwSpeed;
            }
        }
    }
}
