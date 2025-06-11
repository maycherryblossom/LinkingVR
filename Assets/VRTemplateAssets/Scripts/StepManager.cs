using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.VRTemplate
{
    /// <summary>
    /// Controls the steps in the in coaching card.
    /// </summary>
    public class StepManager : MonoBehaviour
    {
        [Serializable]
        class Step
        {
            [SerializeField]
            public GameObject stepObject;

            [SerializeField]
            public string buttonText;

            [Header("Main Chart")]
            [SerializeField] public Texture2D chartTexture;           // 메인 차트에 사용할 텍스처

            [Header("Colliders")]
            [SerializeField] public GameObject[] collidersToActivate;
        }

        [SerializeField]
        public TextMeshProUGUI m_StepButtonTextField;

        [SerializeField]
        List<Step> m_StepList = new List<Step>();

        [Header("Main Chart Display")]
        [SerializeField] private RawImage m_MainChartRawImage;         // 메인 차트용 RawImage

        int m_CurrentStepIndex = 0;
        private List<GameObject> m_ActiveColliders = new List<GameObject>();

        private void Start()
        {
            // 초기 상태: 첫 번째 스텝만 활성화
            for (int i = 0; i < m_StepList.Count; i++)
            {
                m_StepList[i].stepObject.SetActive(i == m_CurrentStepIndex);
            }

            // 버튼 텍스트, 차트, 콜라이더 설정
            m_StepButtonTextField.text = m_StepList[m_CurrentStepIndex].buttonText;
            UpdateMainChart();
            UpdateColliders();
        }

        public void Next()
        {
            m_StepList[m_CurrentStepIndex].stepObject.SetActive(false);

            foreach (var col in m_ActiveColliders)
            {
                if (col != null)
                    col.SetActive(false);
            }

            m_CurrentStepIndex = (m_CurrentStepIndex + 1) % m_StepList.Count;
            var step = m_StepList[m_CurrentStepIndex];
            step.stepObject.SetActive(true);
            m_StepButtonTextField.text = step.buttonText;
            
            // m_ActiveColliders.Clear();

            // m_CurrentStepIndex = (m_CurrentStepIndex + 1) % m_StepList.Count;
            // m_StepList[m_CurrentStepIndex].stepObject.SetActive(true);
            // m_StepButtonTextField.text = m_StepList[m_CurrentStepIndex].buttonText;

            UpdateMainChart();
            UpdateColliders();
        }

        private void UpdateMainChart()
        {
            var texture = m_StepList[m_CurrentStepIndex].chartTexture;
            if (m_MainChartRawImage != null && texture != null)
            {
                m_MainChartRawImage.texture = texture;
            }
        }

        private void UpdateColliders()
        {
            var colliders = m_StepList[m_CurrentStepIndex].collidersToActivate;
            if (colliders == null) return;

            foreach (var col in colliders)
            {
                if (col != null)
                {
                    col.SetActive(true);
                    m_ActiveColliders.Add(col);
                }
            }
        }
    }
}
