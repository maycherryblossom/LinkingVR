using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.UI;

[System.Serializable]
public class ImageKeywordVisualization
{
    public Texture2D texture; 
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
    [SerializeField] private DisplayImageManager displayManager;

    private XRSimpleInteractable _interactable;
    private MeshRenderer _highlightRenderer;
    private GameObject _highlightObject;
    private bool _isVisualizationActive = false;
    private KeywordMapping[] globalMappings;
    
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
        if (keywordDetector == null) return;

        foreach (var v in visualizations)
            if (v.texture != null)
                keywordDetector.PreprocessTexture(v.texture, v.keywordMappings);
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
        bool activate = !_isVisualizationActive;
        _isVisualizationActive = activate;
        if (bezierCurveManager) bezierCurveManager.SetSourcePoint(transform);

        if (!activate)
        {
            keywordDetector.ClearAllMarkers();
            if (bezierCurveManager) bezierCurveManager.ClearActiveCurves();
            return;
        }

        // ① 텍스처 배열 추려서 RawImage 로 표시
        Texture2D[] texArray = System.Array.ConvertAll(
            visualizations, v => v.texture);

        RawImage[] rawImgs = displayManager.SetDisplayImages(texArray);

        // ② Texture 기준으로 시각화
        for (int i = 0; i < rawImgs.Length; ++i)
        {
            Texture2D tex = texArray[i];
            RectTransform rect = rawImgs[i].rectTransform;

            keywordDetector.VisualizeKeywordsForTexture(tex, rect);
        }
    }
    
    private void OnSelectExited(SelectExitEventArgs args)
    {
        // No action needed here since we're toggling on select enter
        Debug.Log($"Select exited: {gameObject.name}");
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
