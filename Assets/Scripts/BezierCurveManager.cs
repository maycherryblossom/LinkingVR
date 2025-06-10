using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BezierCurveManager : MonoBehaviour
{
    [SerializeField] private GameObject bezierCurvePrefab; // BezierCurveRenderer 컴포넌트가 있는 프리팹
    [SerializeField] private int initialPoolSize = 10;
    [SerializeField] private Transform curvesParent;
    [SerializeField] private bool showMarkers = false; // 디버깅용 마커 표시 여부
    [SerializeField] private float labelForwardOffset = 5f;
    [SerializeField] private float labelVerticalOffset = 0.1f;
    [SerializeField] private float labelSpreadAngle   = 30f; // degree
    [SerializeField] private float baseAngle = 0f; // degree
    
    private List<BezierCurveRenderer> _curvePool = new List<BezierCurveRenderer>();
    private List<BezierCurveRenderer> _activeCurves = new List<BezierCurveRenderer>();
    private Transform _sourceTransform; // 선택된 콜라이더의 Transform
    
    private void Awake()
    {
        // 곡선 부모가 설정되지 않았으면 자신을 사용
        if (curvesParent == null)
        {
            curvesParent = transform;
        }
        
        // 베지어 곡선 풀 초기화
        InitializeCurvePool();
    }
    
    private void InitializeCurvePool()
    {
        // 프리팹이 없으면 기본 GameObject 사용
        if (bezierCurvePrefab == null)
        {
            bezierCurvePrefab = new GameObject("Bezier Curve");
            bezierCurvePrefab.AddComponent<LineRenderer>();
            bezierCurvePrefab.AddComponent<BezierCurveRenderer>();
        }
        
        // 풀 생성
        for (int i = 0; i < initialPoolSize; i++)
        {
            GameObject curveObj = Instantiate(bezierCurvePrefab, curvesParent);
            curveObj.name = $"BezierCurve_{i}";
            curveObj.SetActive(false);
            
            BezierCurveRenderer curve = curveObj.GetComponent<BezierCurveRenderer>();
            if (curve != null)
            {
                _curvePool.Add(curve);
            }
        }
        
        Debug.Log($"[BezierCurveManager] Initialized pool with {_curvePool.Count} bezier curves");
    }
    
    // 소스 포인트 설정
    public void SetSourcePoint(Transform source)
    {
        _sourceTransform = source;
        Debug.Log($"[BezierCurveManager] Set source point to: {(source != null ? source.name : "null")}");
    }
    
    public void CreateCurvesForKeywords(
        List<Vector3> keywordPositions,
        List<KeywordMapping> mappings)
    {
        // 이전 활성 곡선 정리
        // ClearActiveCurves();

        if (_sourceTransform == null
            || keywordPositions == null
            || mappings == null
            || keywordPositions.Count != mappings.Count)
        {
            Debug.LogWarning(
                $"[BezierCurveManager] Cannot create curves+labels: " +
                $"source={_sourceTransform}, positions={keywordPositions?.Count}, mappings={mappings?.Count}");
            return;
        }

        Debug.Log($"[BezierCurveManager] Creating {keywordPositions.Count} curves+labels");

        for (int i = 0; i < keywordPositions.Count; i++)
        {
            Vector3 endPos = keywordPositions[i];
            var mapping = mappings[i];

            // 1) 곡선 생성 (풀에서 가져오기)
            BezierCurveRenderer curve = GetCurveFromPool();
            if (curve == null)
            {
                Debug.LogWarning("[BezierCurveManager] Curve pool empty");
                continue;
            }

            // 2) 임시 타겟 포인트 (곡선 렌더러용)
            GameObject targetPoint = new GameObject("Keyword Target Point");
            targetPoint.transform.position = endPos;
            targetPoint.transform.SetParent(curvesParent);

            curve.SetTargetTransforms(_sourceTransform, targetPoint.transform);
            _activeCurves.Add(curve);

            Vector3 dir = (endPos - _sourceTransform.position).normalized;
            float   angle = baseAngle + labelSpreadAngle * i;
            Vector3 rotatedDir = Quaternion.AngleAxis(angle, Vector3.up) * dir;
            Vector3 labelPos   = _sourceTransform.position
                                 + rotatedDir * labelForwardOffset
                                 + Vector3.up     * labelVerticalOffset;

            // 3) 테마 레이블 생성
            if (mapping.labelPrefab != null)
            {
                // // 시작점 방향으로 살짝 띄워서 위치 계산
                // Vector3 dir = (endPos - _sourceTransform.position).normalized;
                // Vector3 labelPos = _sourceTransform.position
                //                    + dir * 0.2f
                //                    + Vector3.up * 0.1f;

                GameObject labelObj = Instantiate(
                    mapping.labelPrefab,
                    labelPos,
                    Quaternion.identity,
                    curve.transform);

                // 카메라 바라보게
                if (Camera.main != null)
                    labelObj.transform.LookAt(Camera.main.transform);

                // Text 세팅 (TextMeshPro 우선, 없으면 TextMesh)
                var tmp3D = labelObj.GetComponentInChildren<TMPro.TextMeshPro>();
                if (tmp3D != null)
                {
                    tmp3D.text = mapping.themeLabel;
                }
                else
                {
                    // UI 텍스트 (TextMeshProUGUI) 찾기
                    var tmpUI = labelObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                    if (tmpUI != null)
                        tmpUI.text = mapping.themeLabel;
                    else
                    {
                        // 마지막으로 legacy TextMesh
                        var legacy = labelObj.GetComponentInChildren<TextMesh>();
                        if (legacy != null)
                            legacy.text = mapping.themeLabel;
                    }
                }
            }
        }
    }
    
    // 풀에서 사용 가능한 곡선 가져오기
    private BezierCurveRenderer GetCurveFromPool()
    {
        // 비활성화된 곡선 찾기
        foreach (var curve in _curvePool)
        {
            if (!curve.gameObject.activeSelf)
            {
                curve.gameObject.SetActive(true);
                return curve;
            }
        }
        
        // 사용 가능한 곡선이 없으면 새로 생성
        GameObject curveObj = Instantiate(bezierCurvePrefab, curvesParent);
        curveObj.name = $"BezierCurve_{_curvePool.Count}";
        
        BezierCurveRenderer newCurve = curveObj.GetComponent<BezierCurveRenderer>();
        if (newCurve != null)
        {
            _curvePool.Add(newCurve);
            return newCurve;
        }
        
        return null;
    }
    
    public void ClearActiveCurves()
    {
        foreach (var curve in _activeCurves)
        {
            if (curve != null)
            {
                // (1) curve GameObject 하위의 모든 child 오브젝트 삭제
                //     (targetPoint, labelObj 등)
                var children = new List<GameObject>();
                foreach (Transform t in curve.transform)
                    children.Add(t.gameObject);
                foreach (var go in children)
                    Destroy(go);

                // (2) 곡선 숨기기 & 다시 풀로 돌려놓기
                curve.HideCurve();
                curve.gameObject.SetActive(false);
            }
        }
        _activeCurves.Clear();
    }

    
    // 마커 표시 여부 설정
    public void SetMarkersVisibility(bool visible)
    {
        showMarkers = visible;
    }
    
    // KeywordDetector에서 마커 생성 여부 확인
    public bool ShouldShowMarkers()
    {
        return showMarkers;
    }
}
