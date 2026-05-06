using System.Collections.Generic;
using UnityEngine;

namespace SmashHit.Gameplay
{
    public class GlassLaneSpawner : MonoBehaviour
    {
        [SerializeField] private GameStateController gameState;
        [SerializeField] private GlassFragment glassFragmentPrefab;
        [SerializeField] private Transform spawnRoot;
        [SerializeField] private float initialSpawnDelay = 2.0f;
        [SerializeField] private float spawnInterval = 0.9f;
        [SerializeField] private float fragmentMoveSpeed = 8f;
        [SerializeField] private float spawnZ = 30f;
        [SerializeField] private Transform loseTriggerTransform;
        [SerializeField] private float loseLineZ = -8f;
        [SerializeField] private float laneHalfWidth = 3.5f;
        [SerializeField] private float laneHalfHeight = 2f;
        [SerializeField] private int maxActiveFragments = 20;
        [SerializeField] private Vector2 widthRange = new(1.6f, 4.2f);
        [SerializeField] private Vector2 heightRange = new(1.6f, 4.2f);
        [SerializeField] private Vector2 thicknessRange = new(0.15f, 0.55f);
        [SerializeField] private float squareChance = 0.5f;

        private readonly List<GlassFragment> activeFragments = new();
        private float nextSpawnTime;

        private void Start()
        {
            nextSpawnTime = Time.time + initialSpawnDelay;
        }

        private void Update()
        {
            CleanupDestroyed();

            if (gameState != null && !gameState.IsPlaying)
            {
                return;
            }

            if (Time.time < nextSpawnTime || activeFragments.Count >= maxActiveFragments)
            {
                return;
            }

            SpawnFragment();
            nextSpawnTime = Time.time + spawnInterval;
        }

        private void SpawnFragment()
        {
            if (glassFragmentPrefab == null)
            {
                return;
            }

            var x = Random.Range(-laneHalfWidth, laneHalfWidth);
            var y = Random.Range(-laneHalfHeight, laneHalfHeight);
            var spawnPosition = new Vector3(x, y, spawnZ);
            var parent = spawnRoot == null ? transform : spawnRoot;

            var fragment = Instantiate(glassFragmentPrefab, spawnPosition, Quaternion.identity, parent);
            fragment.Initialize(gameState, fragmentMoveSpeed, ResolveLoseLineZ());
            fragment.transform.localScale = BuildRandomScale();
            activeFragments.Add(fragment);
        }

        private float ResolveLoseLineZ()
        {
            return loseTriggerTransform == null ? loseLineZ : loseTriggerTransform.position.z;
        }

        private Vector3 BuildRandomScale()
        {
            var width = Random.Range(widthRange.x, widthRange.y);
            float height;

            if (Random.value <= squareChance)
            {
                height = width;
            }
            else
            {
                height = Random.Range(heightRange.x, heightRange.y);
            }

            var thickness = Random.Range(thicknessRange.x, thicknessRange.y);
            return new Vector3(width, height, thickness);
        }

        private void CleanupDestroyed()
        {
            for (var i = activeFragments.Count - 1; i >= 0; i--)
            {
                if (activeFragments[i] == null)
                {
                    activeFragments.RemoveAt(i);
                }
            }
        }
    }
}
