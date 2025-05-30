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

public class KeywordDetector : MonoBehaviour
{
    // Class to store OCR results for each image
    private class ImageOcrResult
    {
        public RawImage image;
        public string recognizedText;
        public List<TesseractWrapper.DetectedWord> detectedWords;
        public Dictionary<string, List<Rect>> detectedKeywordRects = new Dictionary<string, List<Rect>>();
        public bool processed = false;
        public List<KeywordMapping> keywordMappings = new List<KeywordMapping>(); // 이미지별 키워드 매핑 저장
        public int textureWidth; // 텍스처 너비 캐시
        public int textureHeight; // 텍스처 높이 캐시
    }
    
    [SerializeField] private RawImage targetImage; // The UI RawImage to perform OCR on
    [SerializeField] private KeywordMapping[] keywordMappings; // Array of keyword mappings
    [SerializeField] private KeyCode activationKey = KeyCode.F; // Key to trigger OCR
    [SerializeField] private Transform markersParent; // Parent transform for all markers
    [SerializeField] private BezierCurveManager bezierCurveManager; // 베지어 곡선 관리자
    [SerializeField] private bool showMarkers = true; // 마커 표시 여부 (디버깅용)
    
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
    private Dictionary<RawImage, ImageOcrResult> _ocrResultsCache = new Dictionary<RawImage, ImageOcrResult>();
    
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
        
        // If set to process all images at start, do it after a short delay
        if (processAllImagesAtStart)
        {
            Debug.Log("Processing all images at start...");
            StartCoroutine(DelayedProcessAllImages());
        }
    }
    
    private void OnTesseractSetupComplete()
    {
        _isOcrReady = true;
        Debug.Log("Tesseract setup complete. Ready for OCR detection.");
        
        // Process all images at start if enabled, but with a delay
        if (processAllImagesAtStart)
        {
            // 지연 시간을 두고 처리하여 다른 컴포넌트가 초기화될 시간을 확보
            StartCoroutine(DelayedProcessAllImages());
        }
    }
    
    private IEnumerator DelayedProcessAllImages()
    {
        // 2초 지연 - 다른 컴포넌트의 Start 메서드가 실행될 시간을 확보
        Debug.Log("Waiting for 2 seconds before processing images...");
        yield return new WaitForSeconds(2f);
        
        ProcessAllRegisteredImages();
    }
    
    // Process all images that will be used in the scene
    private void ProcessAllRegisteredImages()
    {
        Debug.Log("[KeywordDetector] Pre-processing all registered images...");
        
        // Find all InteractableKeywordVisualizer components in the scene
        InteractableKeywordVisualizer[] visualizers = FindObjectsOfType<InteractableKeywordVisualizer>();
        
        // 캐시 상태 확인
        Debug.Log($"[KeywordDetector] Current cache status: {_ocrResultsCache.Count} images cached");
        
        foreach (var visualizer in visualizers)
        {
            // Get all images from the visualizer
            RawImage[] images = visualizer.GetAllImages();
            
            if (images != null && images.Length > 0)
            {
                Debug.Log($"[KeywordDetector] Found {images.Length} images in visualizer {visualizer.name}");
                
                // Process each image
                foreach (var image in images)
                {
                    if (image != null)
                    {
                        Debug.Log($"[KeywordDetector] Pre-processing image: {image.name}");
                        
                        // 이미지를 캐시에 추가
                        if (!_ocrResultsCache.ContainsKey(image))
                        {
                            _ocrResultsCache[image] = new ImageOcrResult { image = image };
                            Debug.Log($"[KeywordDetector] Created new cache entry for image: {image.name}");
                        }
                        else
                        {
                            Debug.Log($"[KeywordDetector] Image {image.name} already in cache");
                        }
                        
                        // 이미지 처리
                        ProcessImageOcr(image);
                        
                        // 캐시 상태 확인
                        if (_ocrResultsCache.ContainsKey(image))
                        {
                            var result = _ocrResultsCache[image];
                            Debug.Log($"[KeywordDetector] Cache status for {image.name}: processed={result.processed}, detectedWords={(result.detectedWords != null ? result.detectedWords.Count : 0)}");
                        }
                    }
                }
            }
        }
        
        Debug.Log("[KeywordDetector] All images pre-processed. Ready for visualization.");
        Debug.Log($"[KeywordDetector] Final cache status: {_ocrResultsCache.Count} images cached");
    }
    
    private void Update()
    {
        // Check if OCR is ready and activation key is pressed
        if (_isOcrReady && Input.GetKeyDown(activationKey))
        {
            PerformOcrOnImage();
        }
    }
    
    // Public method to perform OCR detection (can be called from other scripts)
    public void PerformOCRDetection()
    {
        if (!_isOcrReady)
        {
            Debug.LogWarning("OCR is not ready yet. Please wait for initialization to complete.");
            return;
        }
        
        Debug.Log($"PerformOCRDetection called for image: {(targetImage != null ? targetImage.name : "null")}");
        
        // 현재 키워드 매핑 상태 확인
        Debug.Log($"Current keyword mappings count: {keywordMappings.Length}");
        foreach (var mapping in keywordMappings)
        {
            if (mapping != null)
            {
                Debug.Log($"Mapping: keyword={mapping.keyword}, partialMatch={mapping.partialMatch}");
            }
            else
            {
                Debug.Log("Null mapping found");
            }
        }
        
        // Check if we have a valid target image
        if (targetImage == null)
        {
            Debug.LogError("Target image is null. Cannot perform OCR detection.");
            return;
        }
        
        // Check if the image has already been processed
        if (_ocrResultsCache.ContainsKey(targetImage))
        {
            // Use cached results
            ImageOcrResult cachedResult = _ocrResultsCache[targetImage];
            if (cachedResult.processed)
            {
                Debug.Log($"[KeywordDetector] Using cached OCR results for image: {targetImage.name}");
                VisualizeKeywordsFromCache(cachedResult);
                return;
            }
            else
            {
                Debug.Log($"[KeywordDetector] Image {targetImage.name} is in cache but not processed yet.");
            }
        }
        else
        {
            Debug.Log($"[KeywordDetector] Image {targetImage.name} not found in cache. Processing now.");
        }
        
        // If not cached or not processed, perform OCR
        PerformOcrOnImage();
    }
    
    // Force OCR detection even if cached results exist
    public void ForceOCRDetection()
    {
        if (!_isOcrReady)
        {
            Debug.LogWarning("OCR is not ready yet. Please wait for initialization to complete.");
            return;
        }
        
        Debug.Log($"ForceOCRDetection called for image: {(targetImage != null ? targetImage.name : "null")}");
        
        // Check if we have a valid target image
        if (targetImage == null)
        {
            Debug.LogError("Target image is null. Cannot perform OCR detection.");
            return;
        }
        
        // Remove from cache if exists
        if (_ocrResultsCache.ContainsKey(targetImage))
        {
            Debug.Log($"Removing existing cache entry for image: {targetImage.name}");
            _ocrResultsCache.Remove(targetImage);
        }
        
        // Perform OCR processing
        Debug.Log($"Forcing OCR processing for image: {targetImage.name}");
        PerformOcrOnImage();
    }
    
    // Process image OCR and cache the results
    private void ProcessImageOcr(RawImage image)
    {
        if (!_isOcrReady || image == null || image.texture == null)
            return;
            
        // Set the current target image
        RawImage originalTarget = targetImage;
        targetImage = image;
        
        // Create a new cache entry if needed
        if (!_ocrResultsCache.ContainsKey(image))
        {
            _ocrResultsCache[image] = new ImageOcrResult { image = image };
        }
        
        // Perform OCR without visualization
        Texture2D sourceTexture = ConvertToTexture2D(image.texture);
        if (sourceTexture == null)
        {
            Debug.LogError($"Failed to convert texture to readable format for image: {image.name}");
            targetImage = originalTarget; // Restore original target
            return;
        }
        
        try
        {
            // Perform OCR
            string recognizedText = _tesseractDriver.Recognize(sourceTexture);
            Debug.Log($"OCR Result for {image.name}: {recognizedText}");
            
            // Get word boxes from the highlighted texture
            ExtractWordBoxes();
            
            // Store results in cache
            ImageOcrResult result = _ocrResultsCache[image];
            result.recognizedText = recognizedText;
            
            // 텍스처 크기 캐시
            Texture2D highlightedTexture = _tesseractDriver.GetHighlightedTexture();
            if (highlightedTexture != null)
            {
                result.textureWidth = highlightedTexture.width;
                result.textureHeight = highlightedTexture.height;
                Debug.Log($"[KeywordDetector] Cached texture dimensions for {image.name}: {result.textureWidth}x{result.textureHeight}");
            }
            else
            {
                Debug.LogWarning($"[KeywordDetector] Could not cache texture dimensions for {image.name}: highlighted texture is null");
                // 기본값 설정
                result.textureWidth = 1024;
                result.textureHeight = 1024;
            }
            
            // IMPORTANT: Make a copy of the detected words to prevent them from being lost
            List<TesseractWrapper.DetectedWord> detectedWordsCopy = new List<TesseractWrapper.DetectedWord>();
            var originalWords = _tesseractDriver.GetTesseractWrapper().GetDetectedWords();
            foreach (var word in originalWords)
            {
                detectedWordsCopy.Add(word); // This creates a copy of each word
            }
            result.detectedWords = detectedWordsCopy;
            
            Debug.Log($"[KeywordDetector] Copied {detectedWordsCopy.Count} detected words for {image.name}");
            result.processed = true;
            
            // Cache the detected keyword rectangles for each mapping
            CacheKeywordRects(result);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during OCR processing for {image.name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            // Clean up the temporary texture if it's not the original
            if (sourceTexture != image.texture as Texture2D)
            {
                Destroy(sourceTexture);
            }
            
            // Restore original target
            targetImage = originalTarget;
        }
    }
    
    // Cache keyword rectangles for the given OCR result
    private void CacheKeywordRects(ImageOcrResult result)
    {
        if (result == null || result.detectedWords == null || result.detectedWords.Count == 0)
        {
            Debug.LogWarning($"[KeywordDetector] Cannot cache keyword rects: result={result}, detectedWords={result?.detectedWords?.Count}");
            return;
        }
            
        Debug.Log($"[KeywordDetector] Caching keyword rectangles for image: {result.image.name} with {result.detectedWords.Count} detected words");
        
        // 이미지별 키워드 매핑과 글로벌 키워드 매핑 합치기
        List<KeywordMapping> combinedMappings = new List<KeywordMapping>();
        
        // 이미지별 매핑 추가 (우선순위 높음)
        if (result.keywordMappings != null && result.keywordMappings.Count > 0)
        {
            combinedMappings.AddRange(result.keywordMappings);
            Debug.Log($"[KeywordDetector] Added {result.keywordMappings.Count} image-specific mappings for {result.image.name}");
        }
        
        // 글로벌 매핑 추가
        if (keywordMappings != null && keywordMappings.Length > 0)
        {
            combinedMappings.AddRange(keywordMappings);
            Debug.Log($"[KeywordDetector] Added {keywordMappings.Length} global mappings");
        }
        
        // 매핑이 없는지 확인
        if (combinedMappings.Count == 0)
        {
            Debug.LogWarning($"[KeywordDetector] No keyword mappings available (neither image-specific nor global). Skipping rectangle caching for image: {result.image.name}");
            return;
        }
            
        // Clear previous detections
        result.detectedKeywordRects.Clear();
        
        // Create a temporary TesseractWrapper to use the cached detected words
        TesseractWrapper tempWrapper = new TesseractWrapper();
        tempWrapper.SetDetectedWords(result.detectedWords);

        // Log keywordMappings
        Debug.Log($"[KeywordDetector] Processing {combinedMappings.Count} keyword mappings for image: {result.image.name}");
        foreach (var mapping in combinedMappings)
        {
            if (mapping != null)
            {
                Debug.Log($"[KeywordDetector] Keyword mapping: {mapping.keyword}, partialMatch: {mapping.partialMatch}");
            }
            else
            {
                Debug.LogWarning("[KeywordDetector] Null mapping found in mappings list");
            }
        }
        
        // For each keyword mapping, check if it exists in the detected words
        foreach (var mapping in combinedMappings)
        {
            if (mapping == null || string.IsNullOrEmpty(mapping.keyword))
            {
                Debug.LogWarning($"[KeywordDetector] Skipping empty or null keyword mapping");
                continue;
            }
            
            Debug.Log($"[KeywordDetector] Processing keyword mapping: {mapping.keyword}, partialMatch: {mapping.partialMatch}");
            
            // Check if the keyword contains multiple words
            bool isMultiWord = mapping.keyword.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length > 1;
            
            if (isMultiWord && !mapping.partialMatch)
            {
                // For multi-word phrases, use the phrase search
                List<TesseractWrapper.PhraseMatch> phraseMatches = tempWrapper.FindPhrase(mapping.keyword, false);
                
                if (phraseMatches.Count > 0)
                {
                    Debug.Log($"Found {phraseMatches.Count} instances of phrase: {mapping.keyword} in {result.image.name}");
                    
                    // Store the detected phrase rectangles
                    List<Rect> rects = new List<Rect>();
                    foreach (var match in phraseMatches)
                    {
                        rects.Add(match.BoundingBox);
                    }
                    result.detectedKeywordRects[mapping.keyword] = rects;
                }
                else
                {
                    Debug.Log($"No matches found for phrase: {mapping.keyword}");
                }
            }
            else
            {
                // For single words or partial matches
                List<TesseractWrapper.DetectedWord> matches;
                
                if (mapping.partialMatch)
                {
                    // Find partial matches (like Ctrl+F)
                    matches = tempWrapper.FindKeywordsContaining(mapping.keyword, false);
                }
                else
                {
                    // Find exact word matches
                    matches = tempWrapper.FindKeyword(mapping.keyword, false);
                }
                
                if (matches.Count > 0)
                {
                    Debug.Log($"Found {matches.Count} instances of keyword: {mapping.keyword} in {result.image.name}");
                    
                    // Store the detected keyword rectangles
                    List<Rect> rects = new List<Rect>();
                    foreach (var match in matches)
                    {
                        rects.Add(match.BoundingBox);
                    }
                    result.detectedKeywordRects[mapping.keyword] = rects;
                }
                else
                {
                    Debug.Log($"No matches found for keyword: {mapping.keyword}");
                }
            }
        }
        
        // Log the total number of cached rectangles
        int totalRects = 0;
        foreach (var pair in result.detectedKeywordRects)
        {
            totalRects += pair.Value.Count;
        }
        Debug.Log($"Cached a total of {totalRects} keyword rectangles for {result.detectedKeywordRects.Count} keywords in {result.image.name}");
    }
    
    // Visualize keywords from cached results
    private void VisualizeKeywordsFromCache(ImageOcrResult cachedResult)
    {
        if (cachedResult == null || !cachedResult.processed)
        {
            Debug.LogWarning($"[KeywordDetector] Cannot visualize from cache: cachedResult={cachedResult}, processed={cachedResult?.processed}");
            return;
        }
            
        Debug.Log($"[KeywordDetector] Visualizing keywords from cache for image: {cachedResult.image.name}");
        Debug.Log($"[KeywordDetector] Cache contains {cachedResult.detectedKeywordRects.Count} keyword entries");
        
        // Clear previous markers for this image only
        // ClearMarkers();
        
        // 이미지별 키워드 매핑과 글로벌 키워드 매핑 합치기
        List<KeywordMapping> combinedMappings = new List<KeywordMapping>();
        
        // 이미지별 매핑 추가 (우선순위 높음)
        if (cachedResult.keywordMappings != null && cachedResult.keywordMappings.Count > 0)
        {
            combinedMappings.AddRange(cachedResult.keywordMappings);
            Debug.Log($"[KeywordDetector] Using {cachedResult.keywordMappings.Count} image-specific mappings for visualization");
        }
        
        // 글로벌 매핑 추가
        if (keywordMappings != null && keywordMappings.Length > 0)
        {
            combinedMappings.AddRange(keywordMappings);
            Debug.Log($"[KeywordDetector] Using {keywordMappings.Length} global mappings for visualization");
        }
        
        // 키워드 위치를 저장할 리스트
        List<Vector3> keywordWorldPositions = new List<Vector3>();
        
        // For each keyword mapping, check if it exists in the cache
        foreach (var mapping in combinedMappings)
        {
            if (mapping == null || string.IsNullOrEmpty(mapping.keyword))
                continue;
                
            Debug.Log($"[KeywordDetector] Checking for keyword: {mapping.keyword}");
                
            if (cachedResult.detectedKeywordRects.TryGetValue(mapping.keyword, out List<Rect> rects))
            {
                Debug.Log($"[KeywordDetector] Found {rects.Count} rectangles for keyword: {mapping.keyword}");
                foreach (var rect in rects)
                {
                    // 키워드의 월드 좌표 계산
                    Vector3 worldPos = ConvertTextureToWorldPosition(rect, cachedResult.image.rectTransform);
                    keywordWorldPositions.Add(worldPos);
                    
                    // 마커 표시 옵션이 켜져 있으면 마커도 생성
                    if (showMarkers)
                    {
                        Debug.Log($"[KeywordDetector] Creating marker for {mapping.keyword} at {rect}");
                        CreateMarkerForKeyword(mapping, rect);
                    }
                }
            }
            else
            {
                Debug.Log($"[KeywordDetector] No rectangles found for keyword: {mapping.keyword}");
            }
        }
        
        // 베지어 곡선 관리자로 키워드 위치 전달
        if (bezierCurveManager != null && keywordWorldPositions.Count > 0)
        {
            Debug.Log($"[KeywordDetector] Sending {keywordWorldPositions.Count} keyword positions to BezierCurveManager");
            bezierCurveManager.CreateCurvesForKeywords(keywordWorldPositions);
        }
        
        Debug.Log($"[KeywordDetector] Visualization complete. Created {_activeMarkers.Count} markers and {keywordWorldPositions.Count} bezier curves.");
    }
    
    private void PerformOcrOnImage()
    {
        if (targetImage == null || targetImage.texture == null)
        {
            Debug.LogError("Target image or texture is null!");
            return;
        }
        
        // Always convert the texture to ensure it's readable
        Texture2D sourceTexture = ConvertToTexture2D(targetImage.texture);
        if (sourceTexture == null)
        {
            Debug.LogError("Failed to convert texture to readable format");
            return;
        }
        
        // Clear previous markers
        ClearMarkers();
        
        try
        {
            // Perform OCR
            string recognizedText = _tesseractDriver.Recognize(sourceTexture);
            Debug.Log("OCR Result: " + recognizedText);
            
            // Get word boxes from the highlighted texture
            ExtractWordBoxes();
            
            // Find and mark keywords
            FindAndMarkKeywords(recognizedText);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during OCR processing: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            // Clean up the temporary texture if it's not the original
            if (sourceTexture != targetImage.texture as Texture2D)
            {
                Destroy(sourceTexture);
            }
        }
    }
    
    private Texture2D ConvertToTexture2D(Texture texture)
    {
        try
        {
            // Check if the texture is already a readable Texture2D
            Texture2D existingTex2D = texture as Texture2D;
            if (existingTex2D != null)
            {
                try
                {
                    // Test if the texture is readable
                    Color32[] pixels = existingTex2D.GetPixels32();
                    // If we got here, the texture is readable, so we can return it directly
                    return existingTex2D;
                }
                catch
                {
                    // Texture is not readable, continue with conversion
                    Debug.Log("Texture is not readable, converting to readable format");
                }
            }
            
            // Create a new readable Texture2D
            Texture2D tex2D = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            
            // Create a temporary RenderTexture
            RenderTexture tempRT = RenderTexture.GetTemporary(
                texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            
            // Copy the source texture to the temporary RenderTexture
            Graphics.Blit(texture, tempRT);
            
            // Store the active RenderTexture
            RenderTexture previousRT = RenderTexture.active;
            
            // Set the temporary RenderTexture as active
            RenderTexture.active = tempRT;
            
            // Read pixels from the active RenderTexture to the new Texture2D
            tex2D.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            tex2D.Apply();
            
            // Restore the previously active RenderTexture
            RenderTexture.active = previousRT;
            
            // Release the temporary RenderTexture
            RenderTexture.ReleaseTemporary(tempRT);
            
            return tex2D;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error converting texture: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }
    
    private void ExtractWordBoxes()
    {
        // This method is no longer needed as we're directly using the DetectedWords from TesseractWrapper
        // The information is already stored in the TesseractWrapper.GetDetectedWords() method
    }
    
    private void FindAndMarkKeywords(string recognizedText)
    {
        // Clear previous detections
        _detectedKeywordRects.Clear();
        
        if (string.IsNullOrEmpty(recognizedText))
        {
            Debug.LogWarning("No text recognized in the image.");
            return;
        }
        
        // Get all detected words with their bounding boxes from the TesseractWrapper
        TesseractWrapper wrapper = _tesseractDriver.GetTesseractWrapper();
        if (wrapper == null)
        {
            Debug.LogError("TesseractWrapper is null");
            return;
        }
        
        List<TesseractWrapper.DetectedWord> detectedWords = wrapper.GetDetectedWords();
        Debug.Log($"Total detected words: {detectedWords.Count}");
        
        // For each keyword mapping, check if it exists in the detected words
        foreach (var mapping in keywordMappings)
        {
            if (string.IsNullOrEmpty(mapping.keyword))
                continue;
            
            // Check if the keyword contains multiple words
            bool isMultiWord = mapping.keyword.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length > 1;
            
            if (isMultiWord && !mapping.partialMatch)
            {
                // For multi-word phrases, use the phrase search
                List<TesseractWrapper.PhraseMatch> phraseMatches = wrapper.FindPhrase(mapping.keyword, false);
                
                if (phraseMatches.Count > 0)
                {
                    Debug.Log($"Found {phraseMatches.Count} instances of phrase: {mapping.keyword}");
                    
                    // Store the detected phrase rectangles
                    List<Rect> rects = new List<Rect>();
                    foreach (var match in phraseMatches)
                    {
                        rects.Add(match.BoundingBox);
                        CreateMarkerForKeyword(mapping, match.BoundingBox);
                    }
                    _detectedKeywordRects[mapping.keyword] = rects;
                }
            }
            else
            {
                // For single words or partial matches
                List<TesseractWrapper.DetectedWord> matches;
                
                if (mapping.partialMatch)
                {
                    // Find partial matches (like Ctrl+F)
                    matches = wrapper.FindKeywordsContaining(mapping.keyword, false);
                }
                else
                {
                    // Find exact word matches
                    matches = wrapper.FindKeyword(mapping.keyword, false);
                }
                
                if (matches.Count > 0)
                {
                    Debug.Log($"Found {matches.Count} instances of keyword: {mapping.keyword}");
                    
                    // Store the detected keyword rectangles
                    List<Rect> rects = new List<Rect>();
                    foreach (var match in matches)
                    {
                        rects.Add(match.BoundingBox);
                        CreateMarkerForKeyword(mapping, match.BoundingBox);
                    }
                    _detectedKeywordRects[mapping.keyword] = rects;
                }
            }
        }
    }
    
    private Vector3 ConvertTextureToWorldPosition(Rect boundingBox, RectTransform imageRect)
    {
        // 현재 이미지에 대한 캐시된 텍스처 크기 가져오기
        int textureWidth = 1024; // 기본값
        int textureHeight = 1024; // 기본값
        
        // 타겟 이미지에 대한 캐시된 결과 찾기
        if (targetImage != null && _ocrResultsCache.TryGetValue(targetImage, out ImageOcrResult cachedResult))
        {
            textureWidth = cachedResult.textureWidth;
            textureHeight = cachedResult.textureHeight;
            Debug.Log($"[KeywordDetector] Using cached texture dimensions for {targetImage.name}: {textureWidth}x{textureHeight}");
        }
        else
        {
            Debug.LogWarning($"[KeywordDetector] No cached texture dimensions found for target image. Using default values.");
        }
        
        // Calculate the center of the bounding box in texture coordinates (0-1)
        Vector2 texturePosNormalized = new Vector2(
            (boundingBox.x + boundingBox.width / 2) / textureWidth,
            (boundingBox.y + boundingBox.height / 2) / textureHeight
        );
        
        // Convert the normalized texture position to RawImage local position
        Vector2 localPos = new Vector2(
            Mathf.Lerp(-imageRect.rect.width / 2, imageRect.rect.width / 2, texturePosNormalized.x),
            Mathf.Lerp(-imageRect.rect.height / 2, imageRect.rect.height / 2, texturePosNormalized.y)
        );
        
        // Convert the local position to world position
        Vector3 worldPos = imageRect.TransformPoint(localPos);
        
        // Position slightly in front of the RawImage
        return worldPos + Camera.main.transform.forward * 0.1f;
    }
    
    private void CreateMarkerForKeyword(KeywordMapping mapping, Rect boundingBox)
    {
        try
        {
            if (mapping == null)
            {
                Debug.LogError("[KeywordDetector] Cannot create marker: mapping is null");
                return;
            }
            
            if (mapping.markerPrefab == null || targetImage == null)
            {
                Debug.LogWarning($"[KeywordDetector] Cannot create marker: markerPrefab={mapping.markerPrefab}, targetImage={targetImage}");
                return;
            }
            
            Debug.Log($"[KeywordDetector] Creating marker for keyword '{mapping.keyword}' at {boundingBox} on image {targetImage.name}");
                
            // Convert from texture coordinates to world coordinates
            RectTransform imageRect = targetImage.rectTransform;
            if (imageRect == null)
            {
                Debug.LogError($"[KeywordDetector] Cannot create marker: targetImage.rectTransform is null");
                return;
            }
            
            Vector3 worldPos = ConvertTextureToWorldPosition(boundingBox, imageRect);
            
            Debug.Log($"[KeywordDetector] Marker world position: {worldPos}");
            
            // Instantiate the marker at the calculated position
            GameObject marker = Instantiate(mapping.markerPrefab, worldPos, Quaternion.identity);
            
            // markersParent가 설정되어 있지 않으면 targetImage를 부모로 사용
            Transform parent = markersParent;
            if (parent == null && targetImage != null)
            {
                parent = targetImage.transform;
            }
            
            // 부모 설정
            if (parent != null)
            {
                marker.transform.SetParent(parent);
                Debug.Log($"[KeywordDetector] Setting marker parent to: {parent.name}");
            }
            else
            {
                Debug.LogWarning("[KeywordDetector] No parent available for marker, leaving at root");
            }
            
            marker.name = $"Marker_{mapping.keyword}_{_activeMarkers.Count}";
            
            // Add to the active markers list
            _activeMarkers.Add(marker);
            
            // Set the marker color if it has a renderer
            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = mapping.highlightColor;
                Debug.Log($"[KeywordDetector] Set marker color to {mapping.highlightColor}");
            }
            else
            {
                Debug.LogWarning("[KeywordDetector] Marker has no renderer component");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[KeywordDetector] Error creating marker: {e.Message}\n{e.StackTrace}");
        }
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
    
    // Method to manually set the target image at runtime
    public void SetTargetImage(RawImage image)
    {
        targetImage = image;
        Debug.Log($"[KeywordDetector] Target image set to {(image != null ? image.name : "null")}");
    }
    
    // Method to add a new keyword mapping at runtime (global mapping)
    public void AddKeywordMapping(KeywordMapping mapping)
    {
        if (mapping == null || string.IsNullOrEmpty(mapping.keyword))
        {
            Debug.LogError("Cannot add empty or null keyword mapping");
            return;
        }
        
        Debug.Log($"[KeywordDetector] Adding global keyword mapping: {mapping.keyword}, partialMatch: {mapping.partialMatch}");
        
        // Check if this mapping already exists
        bool alreadyExists = false;
        foreach (var existingMapping in keywordMappings)
        {
            if (existingMapping.keyword == mapping.keyword)
            {
                alreadyExists = true;
                break;
            }
        }
        
        if (!alreadyExists)
        {
            // Add mapping to the list
            Array.Resize(ref keywordMappings, keywordMappings.Length + 1);
            keywordMappings[keywordMappings.Length - 1] = mapping;
            Debug.Log($"[KeywordDetector] Successfully added global mapping. Total mappings: {keywordMappings.Length}");
        }
        else
        {
            Debug.Log($"[KeywordDetector] Global mapping for keyword '{mapping.keyword}' already exists, skipping.");
        }
    }
    
    // Method to add a keyword mapping for a specific image
    public void AddKeywordMappingForImage(RawImage image, KeywordMapping mapping)
    {
        if (image == null || mapping == null || string.IsNullOrEmpty(mapping.keyword))
        {
            Debug.LogError("[KeywordDetector] Cannot add mapping: image or mapping is invalid");
            return;
        }
        
        // Create cache entry if it doesn't exist
        if (!_ocrResultsCache.ContainsKey(image))
        {
            _ocrResultsCache[image] = new ImageOcrResult { image = image };
            Debug.Log($"[KeywordDetector] Created new cache entry for image: {image.name}");
        }
        
        // Get the cache entry
        ImageOcrResult result = _ocrResultsCache[image];
        
        // Check if this mapping already exists for this image
        bool alreadyExists = false;
        foreach (var existingMapping in result.keywordMappings)
        {
            if (existingMapping.keyword == mapping.keyword)
            {
                alreadyExists = true;
                break;
            }
        }
        
        if (!alreadyExists)
        {
            // Add mapping to the image's list
            result.keywordMappings.Add(mapping);
            Debug.Log($"[KeywordDetector] Added mapping for image {image.name}: {mapping.keyword}, partialMatch: {mapping.partialMatch}");
        }
        else
        {
            Debug.Log($"[KeywordDetector] Mapping for keyword '{mapping.keyword}' already exists for image {image.name}, skipping.");
        }
    }
    
    // Method to clear keyword mappings for a specific image
    public void ClearKeywordMappingsForImage(RawImage image)
    {
        if (image == null)
        {
            Debug.LogError("[KeywordDetector] Cannot clear mappings: image is null");
            return;
        }
        
        if (_ocrResultsCache.ContainsKey(image))
        {
            _ocrResultsCache[image].keywordMappings.Clear();
            Debug.Log($"[KeywordDetector] Cleared all keyword mappings for image: {image.name}");
        }
    }
    
    // Method to clear all keyword mappings (global)
    public void ClearKeywordMappings()
    {
        keywordMappings = new KeywordMapping[0];
        Debug.Log("[KeywordDetector] Cleared all global keyword mappings");
    }
    
    // Method to clear all active markers
    public void ClearAllMarkers()
    {
        ClearMarkers();
    }
}
