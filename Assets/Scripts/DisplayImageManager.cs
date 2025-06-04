using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class DisplayImageManager : MonoBehaviour
{
    [SerializeField] private RawImage[] displaySlots;   // 월드 공간 UI 슬롯
    [SerializeField] private Texture2D defaultPlaceholder;  // 선택: 빈 슬롯용

    /// <summary>
    /// 텍스처를 슬롯에 배치하고, 실제로 활성화된 RawImage 배열을 반환한다.
    /// </summary>
    public RawImage[] SetDisplayImages(Texture2D[] textures)
    {
        List<RawImage> active = new();

        int count = Mathf.Min(displaySlots.Length, textures.Length);

        /* 1) 필요한 만큼 슬롯 활성화 + 텍스처 할당 ------------------ */
        for (int i = 0; i < count; ++i)
        {
            RawImage slot = displaySlots[i];
            if (slot == null) continue;

            slot.texture = textures[i];
            slot.gameObject.SetActive(true);
            slot.name = $"OCR_IMG_{textures[i].GetInstanceID()}";
            active.Add(slot);
        }

        /* 2) 남은 슬롯은 끄거나 플레이스홀더 ---------------------- */
        for (int i = count; i < displaySlots.Length; ++i)
        {
            RawImage slot = displaySlots[i];
            if (slot == null) continue;

            if (defaultPlaceholder)
            {
                slot.texture = defaultPlaceholder;
                slot.gameObject.SetActive(true);
            }
            else
            {
                slot.gameObject.SetActive(false);
            }
        }

        return active.ToArray();
    }
}
