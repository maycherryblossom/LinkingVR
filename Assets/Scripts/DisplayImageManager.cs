using UnityEngine;
using UnityEngine.UI;

public class DisplayImageManager : MonoBehaviour
{
    [SerializeField] private RawImage[] displaySlots;     // 월드 공간 RawImage 들

    /// <summary>
    /// 슬롯에 텍스처를 채워 넣는다.
    /// slots.Length >= textures.Length 인 경우 나머지는 그대로 둔다
    /// </summary>
    public void SetDisplayImages(Texture2D[] textures)
    {
        int count = Mathf.Min(displaySlots.Length, textures.Length);

        // 필요한 슬롯만 교체
        for (int i = 0; i < count; ++i)
        {
            if (displaySlots[i] && textures[i])
                displaySlots[i].texture = textures[i];
        }

        // 선택 사항: 남은 슬롯을 투명 이미지나 null 로 초기화
        // for (int i = count; i < displaySlots.Length; ++i)
        //     displaySlots[i].texture = null;
    }
}
