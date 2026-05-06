using System.Collections.Generic;
using UnityEngine;

namespace SmashHit.Gameplay
{
    public static class GlassPassageUtility
    {
        private static readonly Vector2[] FullPanePolygon =
        {
            new(-0.5f, -0.5f),
            new(0.5f, -0.5f),
            new(0.5f, 0.5f),
            new(-0.5f, 0.5f)
        };

        public static bool FullPaneBlocksCameraPath(
            Transform glassTransform,
            Vector2 cameraPathWorldCenter,
            float cameraClearanceRadius)
        {
            return BlocksCameraPath(FullPanePolygon, glassTransform, cameraPathWorldCenter, cameraClearanceRadius);
        }

        public static bool BlocksCameraPath(
            IReadOnlyList<Vector2> polygon,
            Transform glassTransform,
            Vector2 cameraPathWorldCenter,
            float cameraClearanceRadius)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            var worldPathCenter = new Vector3(
                cameraPathWorldCenter.x,
                cameraPathWorldCenter.y,
                glassTransform.position.z);
            var localPathCenter3 = glassTransform.InverseTransformPoint(worldPathCenter);
            var localPathCenter = new Vector2(localPathCenter3.x, localPathCenter3.y);
            var localRadius = ResolveLocalRadius(glassTransform, cameraClearanceRadius);
            var localRadiusSqr = localRadius * localRadius;

            if (ContainsPoint(polygon, localPathCenter))
            {
                return true;
            }

            for (var i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % polygon.Count];
                if ((current - localPathCenter).sqrMagnitude <= localRadiusSqr ||
                    DistanceToSegmentSquared(localPathCenter, current, next) <= localRadiusSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private static float ResolveLocalRadius(Transform glassTransform, float cameraClearanceRadius)
        {
            var scale = glassTransform.lossyScale;
            var smallestAxis = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            if (smallestAxis <= 0.0001f)
            {
                return cameraClearanceRadius;
            }

            return Mathf.Max(0f, cameraClearanceRadius) / smallestAxis;
        }

        private static bool ContainsPoint(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var current = polygon[i];
                var previous = polygon[j];
                if (current.y > point.y == previous.y > point.y)
                {
                    continue;
                }

                var intersectionX = (previous.x - current.x) * (point.y - current.y) /
                    (previous.y - current.y) + current.x;
                if (point.x < intersectionX)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float DistanceToSegmentSquared(Vector2 point, Vector2 from, Vector2 to)
        {
            var segment = to - from;
            var segmentLengthSqr = segment.sqrMagnitude;
            if (segmentLengthSqr <= 0.0001f)
            {
                return (point - from).sqrMagnitude;
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - from, segment) / segmentLengthSqr);
            var closestPoint = from + segment * t;
            return (point - closestPoint).sqrMagnitude;
        }
    }
}
