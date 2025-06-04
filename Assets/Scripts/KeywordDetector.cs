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
    private Dictionary<Texture2D, ImageOcrResult> _ocrCache = new();
    
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
    
    // Cache for OCR results
    private Dictionary<Texture2D, ImageOcrResult> _ocrResultsCache =
        new Dictionary<Texture2D, ImageOcrResult>();

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

    
    // private IEnumerator DelayedProcessAllImages()
    // {
    //     // 2초 지연 - 다른 컴포넌트의 Start 메서드가 실행될 시간을 확보
    //     Debug.Log("Waiting for 2 seconds before processing images...");
    //     yield return new WaitForSeconds(2f);
        
    //     ProcessAllRegisteredImages();
    // }
    
    // Process all images that will be used in the scene
    // private void ProcessAllRegisteredImages()
    // {
    //     Debug.Log("[KeywordDetector] Pre-processing all registered images...");
        
    //     // Find all InteractableKeywordVisualizer components in the scene
    //     InteractableKeywordVisualizer[] visualizers = FindObjectsOfType<InteractableKeywordVisualizer>();
        
    //     // 캐시 상태 확인
    //     Debug.Log($"[KeywordDetector] Current cache status: {_ocrResultsCache.Count} images cached");
        
    //     foreach (var visualizer in visualizers)
    //     {
    //         // Get all images from the visualizer
    //         RawImage[] images = visualizer.GetAllImages();
            
    //         if (images != null && images.Length > 0)
    //         {
    //             Debug.Log($"[KeywordDetector] Found {images.Length} images in visualizer {visualizer.name}");
                
    //             // Process each image
    //             foreach (var image in images)
    //             {
    //                 if (image != null)
    //                 {
    //                     Debug.Log($"[KeywordDetector] Pre-processing image: {image.name}");
                        
    //                     // 이미지를 캐시에 추가
    //                     if (!_ocrResultsCache.ContainsKey(image))
    //                     {
    //                         _ocrResultsCache[image] = new ImageOcrResult { image = image };
    //                         Debug.Log($"[KeywordDetector] Created new cache entry for image: {image.name}");
    //                     }
    //                     else
    //                     {
    //                         Debug.Log($"[KeywordDetector] Image {image.name} already in cache");
    //                     }
                        
    //                     // 이미지 처리
    //                     ProcessImageOcr(image);
                        
    //                     // 캐시 상태 확인
    //                     if (_ocrResultsCache.ContainsKey(image))
    //                     {
    //                         var result = _ocrResultsCache[image];
    //                         Debug.Log($"[KeywordDetector] Cache status for {image.name}: processed={result.processed}, detectedWords={(result.detectedWords != null ? result.detectedWords.Count : 0)}");
    //                     }
    //                 }
    //             }
    //         }
    //     }
        
    //     Debug.Log("[KeywordDetector] All images pre-processed. Ready for visualization.");
    //     Debug.Log($"[KeywordDetector] Final cache status: {_ocrResultsCache.Count} images cached");
    // }
    
    // Public method to perform OCR detection (can be called from other scripts)
    // public void PerformOCRDetection()
    // {
    //     if (!_isOcrReady)
    //     {
    //         Debug.LogWarning("OCR is not ready yet. Please wait for initialization to complete.");
    //         return;
    //     }
        
    //     Debug.Log($"PerformOCRDetection called for image: {(targetImage != null ? targetImage.name : "null")}");
        
    //     // Check if we have a valid target image
    //     if (targetImage == null)
    //     {
    //         Debug.LogError("Target image is null. Cannot perform OCR detection.");
    //         return;
    //     }
        
    //     // Check if the image has already been processed
    //     if (_ocrResultsCache.ContainsKey(targetImage))
    //     {
    //         // Use cached results
    //         ImageOcrResult cachedResult = _ocrResultsCache[targetImage];
    //         if (cachedResult.processed)
    //         {
    //             Debug.Log($"[KeywordDetector] Using cached OCR results for image: {targetImage.name}");
    //             VisualizeKeywordsFromCache(cachedResult);
    //             return;
    //         }
    //         else
    //         {
    //             Debug.Log($"[KeywordDetector] Image {targetImage.name} is in cache but not processed yet.");
    //         }
    //     }
    //     else
    //     {
    //         Debug.Log($"[KeywordDetector] Image {targetImage.name} not found in cache. Processing now.");
    //     }
        
    //     // If not cached or not processed, perform OCR
    //     PerformOcrOnImage();
    // }

    // // Cache keyword rectangles for the given OCR result
    // private void CacheKeywordRects(ImageOcrResult res)
    // {
    //     res.detectedKeywordRects.Clear();

    //     // wordIndex가 없으면 (= ①을 안 했으면) 안전하게 원래 방식으로 fallback
    //     if (res.wordIndex == null || res.wordIndex.Count == 0)
    //     {
    //         Debug.LogWarning($"wordIndex가 비어 있습니다. 기존 선형 탐색 방식으로 캐싱을 시도합니다.");
    //         // ---- 여기서 tempWrapper 버전으로 넘어가도 되고, 바로 return 해도 됨 ----
    //         return;
    //     }

    //     // (이미지별 + 글로벌) 매핑 합치기
    //     IEnumerable<KeywordMapping> maps =
    //         (res.keywordMappings ?? new List<KeywordMapping>())
    //         .Concat(keywordMappings ?? Array.Empty<KeywordMapping>());

    //     foreach (var m in maps)
    //     {
    //         if (m == null || string.IsNullOrWhiteSpace(m.keyword)) continue;

    //         if (m.partialMatch)               // 부분 일치
    //         {
    //             foreach (var key in res.wordIndex.Keys.Where(
    //                     k => k.Contains(m.keyword, StringComparison.OrdinalIgnoreCase)))
    //             {
    //                 res.detectedKeywordRects[key] = res.wordIndex[key];
    //             }
    //         }
    //         else                              // 완전 일치
    //         {
    //             if (res.wordIndex.TryGetValue(m.keyword, out var rects))
    //                 res.detectedKeywordRects[m.keyword] = rects;
    //         }
    //     }

    //     Debug.Log($"[KeywordDetector] Cached {res.detectedKeywordRects.Count} keywords, " +
    //             $"{res.detectedKeywordRects.Values.Sum(l => l.Count)} rectangles.");
    // }

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
        if (!_ocrCache.TryGetValue(tex, out var res) || !res.processed) return;

        // (기존 코드 일부를 재사용)
        List<Vector3> worldPos = new();
        foreach (var m in res.keywordMappings)
        {
            if (!res.detectedKeywordRects.TryGetValue(m.keyword, out var rects)) continue;

            // log
            Debug.Log($"[KeywordDetector] Visualizing keywords for texture: {tex.name}, keyword: {m.keyword}, rects: {rects.Count}");

            foreach (var r in rects)
            {
                Vector3 wp = ConvertTextureToWorldPosition(
                                r, viewRect, res.texWidth, res.texHeight);

                worldPos.Add(wp);                          // 베지어 등 전체 경로용
                if (showMarkers) CreateMarkerForKeyword(m, wp);
            }
        }
        if (bezierCurveManager && worldPos.Count > 0)
            bezierCurveManager.CreateCurvesForKeywords(worldPos);
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

        
    private void ExtractWordBoxes()
    {
        // This method is no longer needed as we're directly using the DetectedWords from TesseractWrapper
        // The information is already stored in the TesseractWrapper.GetDetectedWords() method
    }

    // private Vector3 ConvertTextureToWorldPosition(Rect boundingBox, RectTransform imageRect)
    // {
    //     // 현재 이미지에 대한 캐시된 텍스처 크기 가져오기
    //     int textureWidth = 1024; // 기본값
    //     int textureHeight = 1024; // 기본값
        
    //     // 타겟 이미지에 대한 캐시된 결과 찾기
    //     if (targetImage != null && _ocrResultsCache.TryGetValue(targetImage, out ImageOcrResult cachedResult))
    //     {
    //         textureWidth = cachedResult.textureWidth;
    //         textureHeight = cachedResult.textureHeight;
    //         Debug.Log($"[KeywordDetector] Using cached texture dimensions for {targetImage.name}: {textureWidth}x{textureHeight}");
    //     }
    //     else
    //     {
    //         Debug.LogWarning($"[KeywordDetector] No cached texture dimensions found for target image. Using default values.");
    //     }
        
    //     // Calculate the center of the bounding box in texture coordinates (0-1)
    //     Vector2 texturePosNormalized = new Vector2(
    //         (boundingBox.x + boundingBox.width / 2) / textureWidth,
    //         (boundingBox.y + boundingBox.height / 2) / textureHeight
    //     );
        
    //     // Convert the normalized texture position to RawImage local position
    //     Vector2 localPos = new Vector2(
    //         Mathf.Lerp(-imageRect.rect.width / 2, imageRect.rect.width / 2, texturePosNormalized.x),
    //         Mathf.Lerp(-imageRect.rect.height / 2, imageRect.rect.height / 2, texturePosNormalized.y)
    //     );
        
    //     // Convert the local position to world position
    //     Vector3 worldPos = imageRect.TransformPoint(localPos);
        
    //     // Position slightly in front of the RawImage
    //     return worldPos + Camera.main.transform.forward * 0.1f;
    // }

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
    
    // Method to add a new keyword mapping at runtime (global mapping)
    // public void AddKeywordMapping(KeywordMapping mapping)
    // {
    //     if (mapping == null || string.IsNullOrEmpty(mapping.keyword))
    //     {
    //         Debug.LogError("Cannot add empty or null keyword mapping");
    //         return;
    //     }
        
    //     Debug.Log($"[KeywordDetector] Adding global keyword mapping: {mapping.keyword}, partialMatch: {mapping.partialMatch}");
        
    //     Array.Resize(ref keywordMappings, keywordMappings.Length + 1);
    //     keywordMappings[keywordMappings.Length - 1] = mapping;
    //     Debug.Log($"[KeywordDetector] Successfully added global mapping. Total mappings: {keywordMappings.Length}");
    // }
    
    // Method to add a keyword mapping for a specific image
    // public void AddKeywordMappingForImage(RawImage image, KeywordMapping mapping)
    // {
    //     if (image == null || mapping == null || string.IsNullOrEmpty(mapping.keyword))
    //     {
    //         Debug.LogError("[KeywordDetector] Cannot add mapping: image or mapping is invalid");
    //         return;
    //     }
        
    //     // Create cache entry if it doesn't exist
    //     if (!_ocrResultsCache.ContainsKey(image))
    //     {
    //         _ocrResultsCache[image] = new ImageOcrResult { image = image };
    //         Debug.Log($"[KeywordDetector] Created new cache entry for image: {image.name}");
    //     }
        
    //     // Get the cache entry
    //     ImageOcrResult result = _ocrResultsCache[image];
        
    //     // Check if this mapping already exists for this image
    //     bool alreadyExists = false;
    //     foreach (var existingMapping in result.keywordMappings)
    //     {
    //         if (existingMapping.keyword == mapping.keyword)
    //         {
    //             alreadyExists = true;
    //             break;
    //         }
    //     }
        
    //     if (!alreadyExists)
    //     {
    //         // Add mapping to the image's list
    //         result.keywordMappings.Add(mapping);
    //         Debug.Log($"[KeywordDetector] Added mapping for image {image.name}: {mapping.keyword}, partialMatch: {mapping.partialMatch}");
    //     }
    //     else
    //     {
    //         Debug.Log($"[KeywordDetector] Mapping for keyword '{mapping.keyword}' already exists for image {image.name}, skipping.");
    //     }
    // }

    public void AddKeywordMappingForTexture(Texture2D tex, KeywordMapping map)
    {
        if (!_ocrResultsCache.TryGetValue(tex, out var res))
            _ocrResultsCache[tex] = res = new ImageOcrResult { texture = tex };

        // 중복 검사
        if (res.keywordMappings.Any(k => k.keyword == map.keyword)) return;
        res.keywordMappings.Add(map);

        // Rect 재계산
        CacheKeywordRects(res);
    }

    // Method to clear keyword mappings for a specific image
    // public void ClearKeywordMappingsForImage(RawImage image)
    // {
    //     if (image == null)
    //     {
    //         Debug.LogError("[KeywordDetector] Cannot clear mappings: image is null");
    //         return;
    //     }
        
    //     if (_ocrResultsCache.ContainsKey(image))
    //     {
    //         _ocrResultsCache[image].keywordMappings.Clear();
    //         Debug.Log($"[KeywordDetector] Cleared all keyword mappings for image: {image.name}");
    //     }
    // }
    
    // Method to clear all keyword mappings (global)
    // public void ClearKeywordMappings()
    // {
    //     keywordMappings = new KeywordMapping[0];
    //     Debug.Log("[KeywordDetector] Cleared all global keyword mappings");
    // }
    
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
        if (!_ocrResultsCache.TryGetValue(tex, out var res))
            _ocrResultsCache[tex] = res = new ImageOcrResult { texture = tex };


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