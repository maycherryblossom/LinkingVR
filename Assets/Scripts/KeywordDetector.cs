using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

[System.Serializable]
public class KeywordMapping
{
    public string keyword; // Can be a single word or a phrase (multiple words)
    public GameObject markerPrefab; // 3D cube or other marker to show at the location
    public Color highlightColor = Color.green; // Color to highlight the keyword with
    [Tooltip("If true, will match partial words")]
    public bool partialMatch = false; // Whether to match partial words or exact phrases

    public string themeLabel;            // ex: "CEO Comment"
    public GameObject labelPrefab;
}

[System.Serializable]
public class ImageOcrResult
{
    public Texture2D texture;
    public string    recognizedText;
    public List<TesseractWrapper.DetectedWord> detectedWords = new();
    public Dictionary<string, List<Rect>> detectedKeywordRects = new();
    public List<KeywordMapping> keywordMappings = new();
    public Dictionary<string, List<Rect>> wordIndex;   // 단어 → Rect[]
    public int texWidth, texHeight;
    public bool processed;
}


public class KeywordDetector : MonoBehaviour
{  
    // [SerializeField] private RawImage targetImage; // The UI RawImage to perform OCR on
    // [SerializeField] private KeywordMapping[] keywordMappings; // Array of keyword mappings
    [SerializeField] private KeyCode activationKey = KeyCode.F; // Key to trigger OCR
    [SerializeField] private Transform markersParent; // Parent transform for all markers
    [SerializeField] private BezierCurveManager bezierCurveManager; // 베지어 곡선 관리자
    [SerializeField] private bool showMarkers = true; // 마커 표시 여부 (디버깅용)
    [SerializeField, Range(0.25f, 1f)]private float ocrScale = 0.5f;   // 1.0 = 원본, 0.5 = 절반 해상도   
    [SerializeField, Tooltip("미리 OCR 해둘 텍스처-키워드 매핑")]
    private readonly Dictionary<Texture2D, ImageOcrResult> _ocrCache =
        new Dictionary<Texture2D, ImageOcrResult>();
    
    // 마커 부모 객체를 자동으로 설정하는 메서드
    private void InitializeMarkersParent()
    {
        // 마커 부모가 설정되어 있지 않으면 자신을 사용
        if (markersParent == null)
        {
            markersParent = transform;
            Debug.Log($"[KeywordDetector] Initialized markers parent to self: {markersParent.name}");
        }
    }
    [SerializeField] private bool processAllImagesAtStart = true; // Process all images at start
    
    private TesseractDriver _tesseractDriver;
    private Dictionary<string, List<Rect>> _detectedKeywordRects = new Dictionary<string, List<Rect>>();
    private List<GameObject> _activeMarkers = new List<GameObject>();
    private bool _isOcrReady = false;
    
    private readonly List<(Texture2D tex, KeywordMapping[] maps)> _pending
        = new();                 // OCR 준비 전 들어온 요청 저장

    public bool IsReady => _isOcrReady;   // 외부에서 준비 여부 확인용
    
    // Initialize the detector
    private void Start()
    {
        // Initialize the Tesseract driver
        _tesseractDriver = new TesseractDriver();
        string version = _tesseractDriver.CheckTessVersion();
        Debug.Log("Tesseract Version: " + version);
        
        // Setup Tesseract
        _tesseractDriver.Setup(OnTesseractSetupComplete);
        
        // Create markers parent if not assigned
        if (markersParent == null)
        {
            markersParent = new GameObject("OCR Markers").transform;
            markersParent.SetParent(transform);
        }
        
        // Initialize markers parent if not set
        InitializeMarkersParent();
    }
    
    private void OnTesseractSetupComplete()
    {
        _isOcrReady = true;
        Debug.Log("[KeywordDetector] Tesseract ready!");

        /* 큐에 쌓인 선-요청 처리 */
        foreach (var (tex, maps) in _pending)
            PreprocessTexture(tex, maps);   // 이제 _isOcrReady==true 이므로 바로 수행
        _pending.Clear();
    }


    private void CacheKeywordRects(ImageOcrResult res)
    {
        res.detectedKeywordRects.Clear();
        foreach (var m in res.keywordMappings)
        {
            if (m.partialMatch)
                foreach (var k in res.wordIndex.Keys.Where(
                        k => k.Contains(m.keyword, StringComparison.OrdinalIgnoreCase)))
                    res.detectedKeywordRects[k] = res.wordIndex[k];
            else if (res.wordIndex.TryGetValue(m.keyword, out var rects))
                res.detectedKeywordRects[m.keyword] = rects;
            else if (m.keyword.Contains(' '))          // 공백 → 다중 단어
            {
                string[] tokens = m.keyword
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i <= res.detectedWords.Count - tokens.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < tokens.Length; j++)
                    {
                        if (!res.detectedWords[i + j].Text
                            .Equals(tokens[j], StringComparison.OrdinalIgnoreCase))
                        { match = false; break; }
                    }
                    if (match)
                    {
                        // 여러 개의 bounding box → 하나로 합치기
                        var seg = res.detectedWords.GetRange(i, tokens.Length);

                        float minX = seg.Min(w => w.BoundingBox.x);
                        float minY = seg.Min(w => w.BoundingBox.y);
                        float maxX = seg.Max(w => w.BoundingBox.x + w.BoundingBox.width);
                        float maxY = seg.Max(w => w.BoundingBox.y + w.BoundingBox.height);

                        Rect combined = new Rect(minX, minY, maxX - minX, maxY - minY);

                        if (!res.detectedKeywordRects.TryGetValue(m.keyword, out var list))
                            res.detectedKeywordRects[m.keyword] = list = new();
                        list.Add(combined);
                    }
                }
            }
        }
    }

    private void BuildWordIndex(ImageOcrResult res)
    {
        res.wordIndex = new(StringComparer.OrdinalIgnoreCase);
        foreach (var w in res.detectedWords)
        {
            if (!res.wordIndex.TryGetValue(w.Text, out var list))
                res.wordIndex[w.Text] = list = new();
            list.Add(w.BoundingBox);
        }
    }

    public void VisualizeKeywordsForTexture(Texture2D tex, RectTransform viewRect)
    {
        Debug.Log($"[KeywordDetector Debug] VisualizeKeywordsForTexture called for texture: {(tex != null ? tex.name : "null")}");
        Debug.Log($"[KeywordDetector Debug] _ocrCache contains {(_ocrCache != null ? _ocrCache.Count.ToString() : "null")} entries.");

        if (tex != null && _ocrCache != null && _ocrCache.ContainsKey(tex))
        {
            Debug.Log($"[KeywordDetector Debug] Texture '{tex.name}' FOUND in _ocrCache.");
            ImageOcrResult cachedRes = _ocrCache[tex];
            Debug.Log($"[KeywordDetector Debug] Cached result for '{tex.name}': processed = {cachedRes.processed}");
        }
        else if (tex != null)
        {
            Debug.Log($"[KeywordDetector Debug] Texture '{tex.name}' NOT FOUND in _ocrCache.");
        }
        else
        {
            Debug.Log($"[KeywordDetector Debug] Input texture 'tex' is null.");
        }

        if (!_ocrCache.TryGetValue(tex, out var res) || !res.processed) 
        {
            Debug.Log($"[KeywordDetector Debug] Condition TRUE, returning early. _ocrCache.TryGetValue success: {_ocrCache.TryGetValue(tex, out _)}, res.processed: {(res != null ? res.processed.ToString() : "res is null or not found")}");
            return;
        }

        var worldPos     = new List<Vector3>();
        var mappingList  = new List<KeywordMapping>();

        foreach (var m in res.keywordMappings)
        {
            if (!res.detectedKeywordRects.TryGetValue(m.keyword, out var rects))
                continue;

            foreach (var r in rects)
            {
                // 텍스처 → 월드 좌표
                Vector3 wp = ConvertTextureToWorldPosition(
                    r, viewRect, res.texWidth, res.texHeight);

                worldPos.Add(wp);
                mappingList.Add(m);

                if (showMarkers)
                    CreateMarkerForKeyword(m, wp);
            }
        }

        // 곡선 + 레이블 한 번에 생성
        if (bezierCurveManager != null && worldPos.Count > 0)
        {
            bezierCurveManager.CreateCurvesForKeywords(worldPos, mappingList);
        }
    }
    
    private Texture2D ConvertToTexture2D(Texture texture)
    {
        // ❶ 다운스케일이 1.0이면, 그리고 texture가 Texture2D·isReadable==true 면 그대로 사용
        if (ocrScale >= 0.999f &&
            texture is Texture2D srcT2D &&
            srcT2D.isReadable)
            return srcT2D;                    // 크기 유지, 복사 없음

        // ❷ 그 외의 경우에는 항상 (다운스케일 또는 그레이 변환을 위해) RenderTexture 경유
        int w = Mathf.CeilToInt(texture.width  * ocrScale);
        int h = Mathf.CeilToInt(texture.height * ocrScale);

        RenderTexture rt = RenderTexture.GetTemporary(
            w, h, 0, RenderTextureFormat.R8); // 1채널 = 8-bit Gray
        Graphics.Blit(texture, rt);           // 다운스케일 + RGB→Gray

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D dst = new Texture2D(w, h, TextureFormat.R8, false, true);
        dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        dst.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return dst;                           // 항상 읽기 가능
    }

        
    private Vector3 ConvertTextureToWorldPosition(Rect box, RectTransform imgRect, int w, int h)
    {
        Vector2 nrm = new(box.x + box.width * .5f, box.y + box.height * .5f);
        nrm.x /= w; nrm.y /= h;
        Vector2 local = new(
            Mathf.Lerp(-imgRect.rect.width  * .5f, imgRect.rect.width  * .5f, nrm.x),
            Mathf.Lerp(-imgRect.rect.height * .5f, imgRect.rect.height * .5f, nrm.y));
        return imgRect.TransformPoint(local) + Camera.main.transform.forward * 0.1f;
    }

    private void CreateMarkerForKeyword(KeywordMapping map, Vector3 worldPos)
    {
        if (map == null || map.markerPrefab == null) return;

        /* 1) 마커 생성 & 부모 설정 --------------------------------- */
        Transform parent = markersParent != null ? markersParent : transform;  // fallback
        GameObject marker = Instantiate(map.markerPrefab, worldPos,
                                        Quaternion.identity, parent);

        marker.name = $"Marker_{map.keyword}_{_activeMarkers.Count}";
        _activeMarkers.Add(marker);

        /* 2) 색상 지정 --------------------------------------------- */
        if (marker.TryGetComponent<Renderer>(out var rd))
            rd.material.color = map.highlightColor;
    }

    
    private void ClearMarkers()
    {
        foreach (var marker in _activeMarkers)
        {
            if (marker != null)
            {
                Destroy(marker);
            }
        }
        
        _activeMarkers.Clear();
    }

    public void AddKeywordMappingForTexture(Texture2D tex, KeywordMapping map)
    {
        if (!_ocrCache.TryGetValue(tex, out var res))
            _ocrCache[tex] = res = new ImageOcrResult { texture = tex };

        // 중복 검사
        if (res.keywordMappings.Any(k => k.keyword == map.keyword)) return;
        res.keywordMappings.Add(map);

        // Rect 재계산
        CacheKeywordRects(res);
    }
    
    // Method to clear all active markers
    public void ClearAllMarkers()
    {
        ClearMarkers();
    }

    public void PreprocessTexture(Texture2D tex, KeywordMapping[] maps)
    {
        if (!_isOcrReady)
        {
            _pending.Add((tex, maps));
            return;
        }

        // ───── 캐시 초기화 ─────
        if (!_ocrCache.TryGetValue(tex, out var res))
            _ocrCache[tex] = res = new ImageOcrResult { texture = tex };


        /* OCR 수행(캐시에 없을 때만) */
        if (!res.processed)
        {
            Texture2D readable = ConvertToTexture2D(tex);    // 다운스케일·Grayscale 포함
            string txt = _tesseractDriver.Recognize(readable);
            Debug.Log($"[KeywordDetector] Recognized text: {txt}");

            // 단어 복사
            res.detectedWords = new List<TesseractWrapper.DetectedWord>(
                _tesseractDriver.GetTesseractWrapper().GetDetectedWords());
            res.recognizedText = txt;
            res.texWidth  = readable.width;
            res.texHeight = readable.height;
            BuildWordIndex(res);
            res.processed = true;
        }

        // log
        Debug.Log($"[KeywordDetector] Preprocessed texture: {tex.name}, processed: {res.processed}");

        /* 키워드 매핑 병합 */
        if (maps != null)
            foreach (var m in maps)
                if (!res.keywordMappings.Any(k => k.keyword == m.keyword))
                    res.keywordMappings.Add(m);

        CacheKeywordRects(res);
    }
}