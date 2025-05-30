using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.UI;

[System.Serializable]
public class ImageKeywordVisualization
{
    public RawImage targetImage;
    public KeywordMapping[] keywordMappings;
}

[RequireComponent(typeof(XRSimpleInteractable))]
public class InteractableKeywordVisualizer : MonoBehaviour
{
    [SerializeField] private ImageKeywordVisualization[] visualizations;
    [SerializeField] private KeywordDetector keywordDetector;
    [SerializeField] private Material highlightMaterial;
    [SerializeField] private float highlightAlpha = 0.3f;
    [SerializeField] private Color highlightColor = new Color(0.5f, 0.8f, 1f, 0.3f);
    [SerializeField] private BezierCurveManager bezierCurveManager; // 베지어 곡선 관리자 참조
    
    private XRSimpleInteractable _interactable;
    private MeshRenderer _highlightRenderer;
    private GameObject _highlightObject;
    private bool _isVisualizationActive = false;
    
    private void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        
        // If no KeywordDetector is assigned, try to find one in the scene
        if (keywordDetector == null)
        {
            keywordDetector = FindObjectOfType<KeywordDetector>();
            if (keywordDetector == null)
            {
                Debug.LogError("No KeywordDetector found in the scene. Please assign one in the inspector.");
            }
        }
        
        // Create highlight object
        CreateHighlightObject();
        
        // Register for interactable events
        _interactable.hoverEntered.AddListener(OnHoverEntered);
        _interactable.hoverExited.AddListener(OnHoverExited);
        _interactable.selectEntered.AddListener(OnSelectEntered);
        _interactable.selectExited.AddListener(OnSelectExited);
    }
    
    private void Start()
    {
        // 시작 시 모든 키워드 매핑을 KeywordDetector에 전달
        if (keywordDetector != null && visualizations != null)
        {
            Debug.Log($"[InteractableKeywordVisualizer] Initializing keyword mappings for {visualizations.Length} visualizations");
            
            // 모든 시각화에 대한 키워드 매핑 추가
            foreach (var visualization in visualizations)
            {
                if (visualization.targetImage != null && visualization.keywordMappings != null)
                {
                    // 이미지별 매핑 초기화
                    keywordDetector.ClearKeywordMappingsForImage(visualization.targetImage);
                    
                    foreach (var mapping in visualization.keywordMappings)
                    {
                        if (mapping != null && !string.IsNullOrEmpty(mapping.keyword))
                        {
                            // 새로운 KeywordMapping 객체 생성하여 전달
                            KeywordMapping newMapping = new KeywordMapping
                            {
                                keyword = mapping.keyword,
                                markerPrefab = mapping.markerPrefab,
                                highlightColor = mapping.highlightColor,
                                partialMatch = mapping.partialMatch
                            };
                            keywordDetector.AddKeywordMappingForImage(visualization.targetImage, newMapping);
                            Debug.Log($"[InteractableKeywordVisualizer] Added initial keyword mapping for image {visualization.targetImage.name}: {mapping.keyword}, partialMatch: {mapping.partialMatch}");
                        }
                    }
                }
            }
        }
    }
    
    private void OnDestroy()
    {
        // Unregister from interactable events
        if (_interactable != null)
        {
            _interactable.hoverEntered.RemoveListener(OnHoverEntered);
            _interactable.hoverExited.RemoveListener(OnHoverExited);
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.selectExited.RemoveListener(OnSelectExited);
        }
    }
    
    private void CreateHighlightObject()
    {
        // Create a child object for the highlight
        _highlightObject = new GameObject("Highlight");
        _highlightObject.transform.SetParent(transform);
        _highlightObject.transform.localPosition = Vector3.zero;
        _highlightObject.transform.localRotation = Quaternion.identity;
        _highlightObject.transform.localScale = Vector3.one;
        
        // Add mesh components
        MeshFilter meshFilter = _highlightObject.AddComponent<MeshFilter>();
        _highlightRenderer = _highlightObject.AddComponent<MeshRenderer>();
        
        // Copy the mesh from the parent if it exists
        MeshFilter parentMeshFilter = GetComponent<MeshFilter>();
        if (parentMeshFilter != null && parentMeshFilter.sharedMesh != null)
        {
            meshFilter.sharedMesh = parentMeshFilter.sharedMesh;
        }
        else
        {
            // Create a simple box mesh if no mesh is found
            Collider collider = GetComponent<Collider>();
            if (collider != null)
            {
                if (collider is BoxCollider boxCollider)
                {
                    // Create a box mesh based on the box collider
                    GameObject tempBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    meshFilter.sharedMesh = tempBox.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(tempBox);
                    
                    // Scale the highlight to match the collider
                    _highlightObject.transform.localScale = boxCollider.size;
                    _highlightObject.transform.localPosition = boxCollider.center;
                }
                else if (collider is SphereCollider sphereCollider)
                {
                    // Create a sphere mesh based on the sphere collider
                    GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    meshFilter.sharedMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(tempSphere);
                    
                    // Scale the highlight to match the collider
                    float diameter = sphereCollider.radius * 2f;
                    _highlightObject.transform.localScale = new Vector3(diameter, diameter, diameter);
                    _highlightObject.transform.localPosition = sphereCollider.center;
                }
                else
                {
                    // Default to a cube for other collider types
                    GameObject tempBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    meshFilter.sharedMesh = tempBox.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(tempBox);
                }
            }
        }
        
        // Create or use highlight material
        if (highlightMaterial != null)
        {
            _highlightRenderer.material = new Material(highlightMaterial);
        }
        else
        {
            // Create a transparent material using Universal Render Pipeline or Built-in RP
            // First try URP transparent shader
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            
            // If URP shader not found, try Built-in RP
            if (shader == null)
            {
                shader = Shader.Find("Standard");
                
                if (shader == null)
                {
                    // Last resort - use a simple unlit transparent shader
                    shader = Shader.Find("Unlit/Transparent");
                    
                    if (shader == null)
                    {
                        // If all else fails, use the default shader
                        Debug.LogWarning("Could not find appropriate shader for highlight. Using default shader.");
                        _highlightRenderer.material = new Material(Shader.Find("Default"));
                        _highlightRenderer.material.color = highlightColor;
                        return;
                    }
                }
            }
            
            _highlightRenderer.material = new Material(shader);
            
            // Configure the material based on shader type
            if (shader.name.Contains("Universal"))
            {
                // URP shader setup
                _highlightRenderer.material.SetFloat("_Surface", 1); // 0 = opaque, 1 = transparent
                _highlightRenderer.material.SetFloat("_Blend", 0); // 0 = alpha, 1 = premultiply, 2 = additive, 3 = multiply
                _highlightRenderer.material.SetFloat("_ZWrite", 0); // 0 = disable depth write
                _highlightRenderer.material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                _highlightRenderer.material.renderQueue = 3000;
            }
            else if (shader.name.Contains("Standard"))
            {
                // Standard shader setup
                _highlightRenderer.material.SetFloat("_Mode", 3); // Transparent mode
                _highlightRenderer.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _highlightRenderer.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _highlightRenderer.material.SetInt("_ZWrite", 0);
                _highlightRenderer.material.DisableKeyword("_ALPHATEST_ON");
                _highlightRenderer.material.EnableKeyword("_ALPHABLEND_ON");
                _highlightRenderer.material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _highlightRenderer.material.renderQueue = 3000;
            }
            // Unlit/Transparent shader doesn't need additional setup
        }
        
        // Set highlight color
        _highlightRenderer.material.color = highlightColor;
        
        // Initially hide the highlight
        _highlightObject.SetActive(false);
    }
    
    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        // Show highlight
        if (_highlightObject != null)
        {
            _highlightObject.SetActive(true);
        }
        
        // Debug.Log($"Hover entered: {gameObject.name}");
    }
    
    private void OnHoverExited(HoverExitEventArgs args)
    {
        // Hide highlight if visualization is not active
        if (_highlightObject != null && !_isVisualizationActive)
        {
            _highlightObject.SetActive(false);
        }
        
        // Debug.Log($"Hover exited: {gameObject.name}");
    }
    
    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Toggle visualization
        _isVisualizationActive = !_isVisualizationActive;
        
        if (_isVisualizationActive)
        {
            if (bezierCurveManager != null)
            {
                bezierCurveManager.SetSourcePoint(transform);
                Debug.Log($"[InteractableKeywordVisualizer] Set source point to: {transform.name}");
            }
            // Activate visualization
            ActivateKeywordVisualization();

        }
        else
        {
            // Deactivate visualization
            DeactivateKeywordVisualization();
            
            // Keep highlight visible if still being hovered
            bool isHovered = _interactable.isHovered;
            if (_highlightObject != null)
            {
                _highlightObject.SetActive(isHovered);
            }
            
            // 베지어 곡선 관리자가 있으면 모든 곡선 제거
            if (bezierCurveManager != null)
            {
                bezierCurveManager.ClearActiveCurves();
                Debug.Log($"[InteractableKeywordVisualizer] Cleared all bezier curves for {gameObject.name}");
            }
        }
        
        Debug.Log($"[InteractableKeywordVisualizer] Select entered: {gameObject.name}, Visualization active: {_isVisualizationActive}");
    }
    
    private void OnSelectExited(SelectExitEventArgs args)
    {
        // No action needed here since we're toggling on select enter
        Debug.Log($"Select exited: {gameObject.name}");
    }
    
    // Get all images used by this visualizer
    public RawImage[] GetAllImages()
    {
        if (visualizations == null || visualizations.Length == 0)
            return new RawImage[0];
            
        List<RawImage> images = new List<RawImage>();
        foreach (var visualization in visualizations)
        {
            if (visualization.targetImage != null)
            {
                images.Add(visualization.targetImage);
            }
        }
        
        return images.ToArray();
    }
    
    // 항상 강제 OCR 처리를 활성화하여 테스트 (문제 해결 후 false로 변경할 수 있음)
    [SerializeField] private bool forceOcrProcessing = false; 
    
    private void ActivateKeywordVisualization()
    {
        if (keywordDetector == null)
        {
            Debug.LogError("[InteractableKeywordVisualizer] KeywordDetector is null. Cannot activate visualization.");
            return;
        }
            
        Debug.Log($"[InteractableKeywordVisualizer] Activating keyword visualization for: {gameObject.name}");
        
        // Clear any existing markers
        keywordDetector.ClearAllMarkers();
        
        // Process each visualization
        foreach (var visualization in visualizations)
        {
            if (visualization.targetImage == null || visualization.keywordMappings == null)
            {
                Debug.LogWarning($"[InteractableKeywordVisualizer] Skipping invalid visualization: targetImage={visualization.targetImage}, keywordMappings={visualization.keywordMappings}");
                continue;
            }
            
            // Set the target image for OCR
            keywordDetector.SetTargetImage(visualization.targetImage);
            
            // 이미지별 키워드 매핑 초기화
            keywordDetector.ClearKeywordMappingsForImage(visualization.targetImage);
            
            // 이미지별 키워드 매핑 추가
            foreach (var mapping in visualization.keywordMappings)
            {
                // 새로운 KeywordMapping 객체 생성하여 전달
                KeywordMapping newMapping = new KeywordMapping
                {
                    keyword = mapping.keyword,
                    markerPrefab = mapping.markerPrefab,
                    highlightColor = mapping.highlightColor,
                    partialMatch = mapping.partialMatch
                };
                keywordDetector.AddKeywordMappingForImage(visualization.targetImage, newMapping);
                Debug.Log($"[InteractableKeywordVisualizer] Added keyword mapping for image {visualization.targetImage.name}: {mapping.keyword}, partialMatch: {mapping.partialMatch}");
            }
            
            // Perform OCR and detect keywords for this image only
            if (forceOcrProcessing)
            {
                // Force OCR processing (ignore cache)
                Debug.Log($"[InteractableKeywordVisualizer] Forcing OCR processing for image: {visualization.targetImage.name}");
                keywordDetector.ForceOCRDetection();
            }
            else
            {
                // Use cached results if available
                Debug.Log($"[InteractableKeywordVisualizer] Attempting to use cached OCR results for image: {visualization.targetImage.name}");
                keywordDetector.PerformOCRDetection();
            }
            
            // Debug log to check detection
            Debug.Log($"[InteractableKeywordVisualizer] Processed image {visualization.targetImage.name} with {visualization.keywordMappings.Length} keywords");
        }
    }
    
    // Optional coroutine to add delay between processing images
    private IEnumerator WaitForProcessing()
    {
        // Wait for a frame to allow OCR processing to complete
        yield return null;
    }
    
    private void DeactivateKeywordVisualization()
    {
        if (keywordDetector == null)
            return;
            
        Debug.Log($"[InteractableKeywordVisualizer] Deactivating keyword visualization for: {gameObject.name}");
        
        // Clear all markers without clearing the cache or mappings
        keywordDetector.ClearAllMarkers();
        
        // 케이스나 매핑은 유지하면서 마커만 제거
        // 이것을 통해 다시 활성화할 때도 정상작동하게 함
    }
}
