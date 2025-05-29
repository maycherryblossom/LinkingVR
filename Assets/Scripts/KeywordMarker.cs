using UnityEngine;

public class KeywordMarker : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 50f;
    [SerializeField] private float hoverDistance = 0.05f;
    [SerializeField] private float hoverSpeed = 1f;
    
    private Vector3 _initialPosition;
    private float _timeOffset;
    
    private void Start()
    {
        _initialPosition = transform.position;
        _timeOffset = Random.Range(0f, 2f * Mathf.PI); // Random starting phase
    }
    
    private void Update()
    {
        // Rotate the marker
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        
        // Make the marker hover up and down
        float hoverOffset = Mathf.Sin((Time.time + _timeOffset) * hoverSpeed) * hoverDistance;
        transform.position = _initialPosition + new Vector3(0, hoverOffset, 0);
    }
}
