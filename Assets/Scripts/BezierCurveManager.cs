using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BezierCurveManager : MonoBehaviour
{
    [SerializeField] private GameObject bezierCurvePrefab; // BezierCurveRenderer 컴포넌트가 있는 프리팹
    [SerializeField] private int initialPoolSize = 10;
    [SerializeField] private Transform curvesParent;
    [SerializeField] private bool showMarkers = false; // 디버깅용 마커 표시 여부
    
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
    
    // 키워드 위치에 베지어 곡선 생성
    public void CreateCurvesForKeywords(List<Vector3> keywordPositions)
    {
        // 이전 곡선 비활성화
        // ClearActiveCurves();
        
        if (_sourceTransform == null || keywordPositions == null || keywordPositions.Count == 0)
        {
            Debug.LogWarning($"[BezierCurveManager] Cannot create curves: source={_sourceTransform}, positions={keywordPositions?.Count}");
            return;
        }
        
        Debug.Log($"[BezierCurveManager] Creating {keywordPositions.Count} bezier curves from {_sourceTransform.name}");
        
        // 각 키워드 위치에 대해 베지어 곡선 생성
        foreach (var position in keywordPositions)
        {
            // 풀에서 사용 가능한 곡선 가져오기
            BezierCurveRenderer curve = GetCurveFromPool();
            if (curve == null)
            {
                Debug.LogWarning("[BezierCurveManager] No more available curves in pool");
                continue;
            }
            
            // 타겟 포인트용 임시 GameObject 생성
            GameObject targetPoint = new GameObject("Keyword Target Point");
            targetPoint.transform.position = position;
            targetPoint.transform.SetParent(curvesParent);
            
            // 베지어 곡선 설정
            curve.SetTargetTransforms(_sourceTransform, targetPoint.transform);
            
            // 활성 곡선 목록에 추가
            _activeCurves.Add(curve);
        }
        
        Debug.Log($"[BezierCurveManager] Created {_activeCurves.Count} active bezier curves");
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
    
    // 모든 활성 곡선 비활성화
    public void ClearActiveCurves()
    {
        foreach (var curve in _activeCurves)
        {
            if (curve != null)
            {
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
