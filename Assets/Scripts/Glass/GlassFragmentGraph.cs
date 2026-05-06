using System.Collections.Generic;
using UnityEngine;

namespace SmashHit.Gameplay
{
    public sealed class GlassFragmentGraph
    {
        private const float EdgeTolerance = 0.01f;

        private readonly List<HashSet<int>> adjacency;
        private readonly bool[] supported;

        private GlassFragmentGraph(List<HashSet<int>> adjacency, bool[] supported)
        {
            this.adjacency = adjacency;
            this.supported = supported;
        }

        public static GlassFragmentGraph Build(
            IReadOnlyList<GlassFractureGenerator.Fragment> fragments,
            Vector2 impactPoint,
            float disconnectRadius)
        {
            var adjacency = new List<HashSet<int>>(fragments.Count);
            var supported = new bool[fragments.Count];

            for (var i = 0; i < fragments.Count; i++)
            {
                adjacency.Add(new HashSet<int>());
                supported[i] = fragments[i].TouchesBoundary;
            }

            for (var i = 0; i < fragments.Count; i++)
            {
                for (var j = i + 1; j < fragments.Count; j++)
                {
                    if (!TryGetSharedEdgeMidpoint(fragments[i].Polygon, fragments[j].Polygon, out var midpoint))
                    {
                        continue;
                    }

                    // Crack edges around the impact are considered broken, which lets islands detach.
                    if ((midpoint - impactPoint).sqrMagnitude <= disconnectRadius * disconnectRadius)
                    {
                        continue;
                    }

                    adjacency[i].Add(j);
                    adjacency[j].Add(i);
                }
            }

            return new GlassFragmentGraph(adjacency, supported);
        }

        public bool[] FindSupportConnected()
        {
            return FindMainSupportConnected(null);
        }

        public bool[] FindMainSupportConnected(IReadOnlyList<GlassFractureGenerator.Fragment> fragments)
        {
            var connected = new bool[adjacency.Count];
            var visited = new bool[adjacency.Count];
            var queue = new Queue<int>();
            var bestComponent = new List<int>();
            var bestArea = 0f;

            for (var i = 0; i < adjacency.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                var component = new List<int>();
                var componentHasSupport = false;
                var componentArea = 0f;
                visited[i] = true;
                queue.Enqueue(i);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    component.Add(current);
                    componentHasSupport |= supported[current];
                    componentArea += fragments == null ? 1f : fragments[current].Area;

                    foreach (var neighbor in adjacency[current])
                    {
                        if (visited[neighbor])
                        {
                            continue;
                        }

                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }

                if (componentHasSupport && (bestComponent.Count == 0 || componentArea > bestArea))
                {
                    bestComponent = component;
                    bestArea = componentArea;
                }
            }

            for (var i = 0; i < bestComponent.Count; i++)
            {
                connected[bestComponent[i]] = true;
            }

            return connected;
        }

        private static bool TryGetSharedEdgeMidpoint(
            IReadOnlyList<Vector2> first,
            IReadOnlyList<Vector2> second,
            out Vector2 midpoint)
        {
            for (var firstIndex = 0; firstIndex < first.Count; firstIndex++)
            {
                var firstA = first[firstIndex];
                var firstB = first[(firstIndex + 1) % first.Count];

                for (var secondIndex = 0; secondIndex < second.Count; secondIndex++)
                {
                    var secondA = second[secondIndex];
                    var secondB = second[(secondIndex + 1) % second.Count];

                    if (AreSamePoint(firstA, secondB) && AreSamePoint(firstB, secondA))
                    {
                        midpoint = (firstA + firstB) * 0.5f;
                        return true;
                    }
                }
            }

            midpoint = Vector2.zero;
            return false;
        }

        private static bool AreSamePoint(Vector2 first, Vector2 second)
        {
            return (first - second).sqrMagnitude <= EdgeTolerance * EdgeTolerance;
        }
    }
}
