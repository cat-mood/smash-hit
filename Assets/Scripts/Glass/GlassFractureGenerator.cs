using System;
using System.Collections.Generic;
using UnityEngine;

namespace SmashHit.Gameplay
{
    public static class GlassFractureGenerator
    {
        private const float BoundsHalfSize = 0.5f;
        private const float Epsilon = 0.0001f;

        public readonly struct Options
        {
            public Options(int rayCount, int ringCount, float jitter, float minArea, int seed)
            {
                RayCount = Mathf.Max(3, rayCount);
                RingCount = Mathf.Max(1, ringCount);
                Jitter = Mathf.Clamp01(jitter);
                MinArea = Mathf.Max(0.0001f, minArea);
                Seed = seed;
            }

            public int RayCount { get; }
            public int RingCount { get; }
            public float Jitter { get; }
            public float MinArea { get; }
            public int Seed { get; }
        }

        public sealed class Fragment
        {
            public Fragment(IReadOnlyList<Vector2> polygon)
            {
                Polygon = polygon;
                Center = ComputeCentroid(polygon);
                Area = Mathf.Abs(ComputeSignedArea(polygon));
                TouchesBoundary = DoesTouchBoundary(polygon);
            }

            public IReadOnlyList<Vector2> Polygon { get; }
            public Vector2 Center { get; }
            public float Area { get; }
            public bool TouchesBoundary { get; }
        }

        public static List<Fragment> Generate(Vector2 impactPoint, Options options)
        {
            return Generate(impactPoint, options, BuildBoundsPolygon());
        }

        public static List<Fragment> Generate(Vector2 impactPoint, Options options, IReadOnlyList<Vector2> boundsPolygon)
        {
            impactPoint = ClampToBounds(impactPoint);

            var sites = BuildSites(impactPoint, options, boundsPolygon);
            var fragments = new List<Fragment>(sites.Count);

            for (var siteIndex = 0; siteIndex < sites.Count; siteIndex++)
            {
                var polygon = new List<Vector2>(boundsPolygon);
                var site = sites[siteIndex];

                for (var otherIndex = 0; otherIndex < sites.Count; otherIndex++)
                {
                    if (siteIndex == otherIndex)
                    {
                        continue;
                    }

                    polygon = ClipToNearestSite(polygon, site, sites[otherIndex]);
                    if (polygon.Count < 3)
                    {
                        break;
                    }
                }

                if (polygon.Count >= 3)
                {
                    var fragment = new Fragment(polygon);
                    if (fragment.Area >= options.MinArea)
                    {
                        fragments.Add(fragment);
                    }
                }
            }

            return fragments;
        }

        private static List<Vector2> BuildSites(Vector2 impactPoint, Options options, IReadOnlyList<Vector2> boundsPolygon)
        {
            var random = options.Seed == 0 ? new System.Random() : new System.Random(options.Seed);
            var sites = new List<Vector2>(options.RayCount * options.RingCount + 1)
            {
                impactPoint
            };

            var angleOffset = NextSigned(random) * Mathf.PI * 2f / options.RayCount;
            for (var rayIndex = 0; rayIndex < options.RayCount; rayIndex++)
            {
                var angle = angleOffset + Mathf.PI * 2f * rayIndex / options.RayCount;
                angle += NextSigned(random) * options.Jitter * Mathf.PI / options.RayCount;

                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var maxDistance = DistanceToBounds(impactPoint, direction);

                for (var ringIndex = 1; ringIndex <= options.RingCount; ringIndex++)
                {
                    var fraction = (float)ringIndex / options.RingCount;
                    fraction += NextSigned(random) * options.Jitter / options.RingCount;
                    fraction = Mathf.Clamp01(fraction);

                    var perpendicular = new Vector2(-direction.y, direction.x);
                    var lateralJitter = perpendicular * (NextSigned(random) * options.Jitter * 0.08f);
                    var site = impactPoint + direction * (maxDistance * fraction) + lateralJitter;
                    AddUniqueSite(sites, ClampToBounds(site));
                }
            }

            for (var i = 0; i < boundsPolygon.Count; i++)
            {
                AddUniqueSite(sites, boundsPolygon[i]);
            }

            return sites;
        }

        private static List<Vector2> BuildBoundsPolygon()
        {
            return new List<Vector2>
            {
                new(-BoundsHalfSize, -BoundsHalfSize),
                new(BoundsHalfSize, -BoundsHalfSize),
                new(BoundsHalfSize, BoundsHalfSize),
                new(-BoundsHalfSize, BoundsHalfSize)
            };
        }

        private static List<Vector2> ClipToNearestSite(IReadOnlyList<Vector2> polygon, Vector2 site, Vector2 otherSite)
        {
            var midpoint = (site + otherSite) * 0.5f;
            var normal = otherSite - site;
            var clipped = new List<Vector2>(polygon.Count + 1);

            for (var i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var previous = polygon[(i + polygon.Count - 1) % polygon.Count];
                var currentInside = IsInside(current, midpoint, normal);
                var previousInside = IsInside(previous, midpoint, normal);

                if (currentInside)
                {
                    if (!previousInside && TryGetIntersection(previous, current, midpoint, normal, out var enterPoint))
                    {
                        clipped.Add(enterPoint);
                    }

                    clipped.Add(current);
                }
                else if (previousInside && TryGetIntersection(previous, current, midpoint, normal, out var exitPoint))
                {
                    clipped.Add(exitPoint);
                }
            }

            return clipped;
        }

        private static bool IsInside(Vector2 point, Vector2 midpoint, Vector2 normal)
        {
            return Vector2.Dot(point - midpoint, normal) <= Epsilon;
        }

        private static bool TryGetIntersection(Vector2 from, Vector2 to, Vector2 midpoint, Vector2 normal, out Vector2 intersection)
        {
            var direction = to - from;
            var denominator = Vector2.Dot(direction, normal);
            if (Mathf.Abs(denominator) < Epsilon)
            {
                intersection = from;
                return false;
            }

            var t = Vector2.Dot(midpoint - from, normal) / denominator;
            intersection = from + direction * Mathf.Clamp01(t);
            return true;
        }

        private static float DistanceToBounds(Vector2 origin, Vector2 direction)
        {
            var distance = float.PositiveInfinity;

            if (Mathf.Abs(direction.x) > Epsilon)
            {
                var targetX = direction.x > 0f ? BoundsHalfSize : -BoundsHalfSize;
                distance = Mathf.Min(distance, (targetX - origin.x) / direction.x);
            }

            if (Mathf.Abs(direction.y) > Epsilon)
            {
                var targetY = direction.y > 0f ? BoundsHalfSize : -BoundsHalfSize;
                distance = Mathf.Min(distance, (targetY - origin.y) / direction.y);
            }

            return Mathf.Max(0f, distance);
        }

        private static Vector2 ClampToBounds(Vector2 point)
        {
            return new Vector2(
                Mathf.Clamp(point.x, -BoundsHalfSize, BoundsHalfSize),
                Mathf.Clamp(point.y, -BoundsHalfSize, BoundsHalfSize));
        }

        private static void AddUniqueSite(List<Vector2> sites, Vector2 site)
        {
            for (var i = 0; i < sites.Count; i++)
            {
                if ((sites[i] - site).sqrMagnitude <= Epsilon * Epsilon)
                {
                    return;
                }
            }

            sites.Add(site);
        }

        private static float NextSigned(System.Random random)
        {
            return (float)(random.NextDouble() * 2.0 - 1.0);
        }

        private static Vector2 ComputeCentroid(IReadOnlyList<Vector2> polygon)
        {
            var signedArea = ComputeSignedArea(polygon);
            if (Mathf.Abs(signedArea) < Epsilon)
            {
                var average = Vector2.zero;
                for (var i = 0; i < polygon.Count; i++)
                {
                    average += polygon[i];
                }

                return average / polygon.Count;
            }

            var centroid = Vector2.zero;
            for (var i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % polygon.Count];
                var cross = current.x * next.y - next.x * current.y;
                centroid += (current + next) * cross;
            }

            return centroid / (6f * signedArea);
        }

        private static float ComputeSignedArea(IReadOnlyList<Vector2> polygon)
        {
            var area = 0f;
            for (var i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % polygon.Count];
                area += current.x * next.y - next.x * current.y;
            }

            return area * 0.5f;
        }

        private static bool DoesTouchBoundary(IReadOnlyList<Vector2> polygon)
        {
            for (var i = 0; i < polygon.Count; i++)
            {
                var point = polygon[i];
                if (Mathf.Abs(Mathf.Abs(point.x) - BoundsHalfSize) <= 0.001f ||
                    Mathf.Abs(Mathf.Abs(point.y) - BoundsHalfSize) <= 0.001f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
