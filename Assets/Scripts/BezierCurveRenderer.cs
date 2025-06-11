using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BezierCurveRenderer : MonoBehaviour
{
    private LineRenderer lineRenderer;

    public Transform startPointTransform; // 호버 지점 또는 시작 지점 Transform
    public Transform endPointTransform;   // 텍스트 목표 지점 Transform

    // 직접 좌표를 설정할 경우
    private Vector3 _startPoint;
    private Vector3 _endPoint;
    private bool _useTransforms = true;

    private Vector3 _p0, _p1, _p2, _p3;

    [Range(3, 100)]
    public int lineSegments = 50; // 곡선을 얼마나 많은 선분으로 나눌지 (부드러움 조절)

    // 제어점 오프셋 계수 (곡선의 '휘어짐' 정도와 방향을 조절)
    // 이 값들을 조정하여 곡선의 모양을 변경할 수 있습니다.
    public float controlPointOffsetFactor = 0.5f;
    // 곡선이 어느 축을 기준으로 주로 휘어질지 (예: 카메라 위쪽, 월드 위쪽 등)
    public Vector3 bendDirectionReference = Vector3.up;

    [Header("Fade Settings")]
    [SerializeField]
    private Color fadeStartColor = Color.white;
    [SerializeField]
    private float fadeEndAlpha = 0f;


    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 0; // 처음에는 보이지 않게
        SetupFadeGradient();
    }

    private void SetupFadeGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(fadeStartColor, 0f),
                new GradientColorKey(fadeStartColor, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(fadeEndAlpha, 1f)
            }
        );
        lineRenderer.colorGradient = gradient;
    }

    void Update()
    {
        if (_useTransforms)
        {
            if (startPointTransform != null && endPointTransform != null)
            {
                DrawCurve(startPointTransform.position, endPointTransform.position);
            }
            else
            {
                HideCurve();
            }
        }
        // else : SetPoints 메서드로 좌표가 설정되었을 때 DrawCurve가 호출됨
    }

    // 외부에서 시작점과 끝점 Transform을 설정
    public void SetTargetTransforms(Transform start, Transform end)
    {
        _useTransforms = true;
        startPointTransform = start;
        endPointTransform = end;
        if (start == null || end == null) HideCurve();
    }

    // 외부에서 직접 시작점과 끝점 좌표를 설정 (Transform 대신)
    public void SetTargetPoints(Vector3 start, Vector3 end)
    {
        _useTransforms = false; // Transform 추적 중지
        _startPoint = start;
        _endPoint = end;
        DrawCurve(_startPoint, _endPoint);
    }

    public void DrawCurve(Vector3 p0, Vector3 p3)
    {
        if (Vector3.Distance(p0, p3) < 0.01f) // 너무 가까우면 그리지 않음
        {
            HideCurve();
            return;
        }

        lineRenderer.positionCount = lineSegments + 1;

        // --- 제어점(P1, P2) 계산 ---
        // P0와 P3 사이의 중간점
        Vector3 midPoint = (p0 + p3) / 2f;
        // P0에서 P3로 향하는 방향 벡터
        Vector3 directionP0toP3 = (p3 - p0).normalized;
        // P0와 P3 사이의 거리
        float distance = Vector3.Distance(p0, p3);

        Vector3 camForward = Camera.main.transform.forward;
        Vector3 cross = Vector3.Cross(directionP0toP3, Vector3.up);
        // “cross가 카메라 쪽을 향하지 않으면” → 뒤집어서 카메라 쪽으로 돌린다
        if (Vector3.Dot(cross, Camera.main.transform.forward) > 0)   // ❶ 부등호 반전
            cross = -cross; 

        Vector3 bendOffset = cross.normalized * distance * controlPointOffsetFactor;

        // 제어점 P1: P0에서 P3쪽으로 약간 이동 후, bendOffset 만큼 이동
        Vector3 p1 = p0 + directionP0toP3 * (distance * 0.25f) + bendOffset;
        // 제어점 P2: P3에서 P0쪽으로 약간 이동 후, bendOffset 만큼 이동 (같은 방향으로 휘게)
        Vector3 p2 = p3 - directionP0toP3 * (distance * 0.25f) + bendOffset;

        _p0 = p0;
        _p1 = p1;
        _p2 = p2;
        _p3 = p3;

        for (int i = 0; i <= lineSegments; i++)
        {
            float t = (float)i / lineSegments;
            Vector3 pointOnCurve = CalculateCubicBezierPoint(t, p0, p1, p2, p3);
            lineRenderer.SetPosition(i, pointOnCurve);
        }
    }

    public void HideCurve()
    {
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
        }
    }

    // 3차 베지어 곡선 계산 함수
    private Vector3 CalculateCubicBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        Vector3 p = uuu * p0; // (1-t)^3 * P0
        p += 3 * uu * t * p1; // 3 * (1-t)^2 * t * P1
        p += 3 * u * tt * p2; // 3 * (1-t) * t^2 * P2
        p += ttt * p3;        // t^3 * P3
        return p;
    }

    /// <summary>
    /// t에 따른 곡선 상의 좌표 반환
    /// </summary>
    public Vector3 GetPoint(float t)
    {
        return CalculateCubicBezierPoint(t, _p0, _p1, _p2, _p3);
    }

    /// <summary>
    /// t에 따른 곡선의 접선 방향 반환
    /// </summary>
    public Vector3 GetTangent(float t)
    {
        float u = 1 - t;
        Vector3 tangent = 3 * u * u * (_p1 - _p0)
                        + 6 * u * t * (_p2 - _p1)
                        + 3 * t * t * (_p3 - _p2);
        return tangent.normalized;
    }
}