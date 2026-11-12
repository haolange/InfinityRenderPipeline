using UnityEngine;

namespace InfinityTech.Component.Utility
{
    public class CameraMovement : MonoBehaviour
    {
        // Capture

        public bool enableInputCapture = true;
        public bool holdRightMouseCapture = false;

#if UNITY_EDITOR
        private static Texture2D ms_invisibleCursor = null;
#endif

        // Motion

        public float lookSpeed = 5f;
        public float moveSpeed = 5f;
        public float sprintSpeed = 50f;

        // Frame rate

        public float FPSWait = 1000;
        public int state = 1000;

        [Header("DeBugProperty")]
        [SerializeField]
        private bool DisableFPSLimit = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool m_inputCaptured;
        private float m_yaw;
        private float m_pitch;

        private void Awake()
        {
            enabled = enableInputCapture;
            SetFPSFrame(DisableFPSLimit, -1);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                enabled = enableInputCapture;
            }
        }

        private void CaptureInput()
        {
            Cursor.lockState = CursorLockMode.Locked;

#if UNITY_EDITOR
            Cursor.SetCursor(ms_invisibleCursor, Vector2.zero, CursorMode.ForceSoftware);
#else
            Cursor.visible = false;
#endif
            m_inputCaptured = true;
            m_yaw = transform.eulerAngles.y;
            m_pitch = transform.eulerAngles.x;
        }

        private void ReleaseInput()
        {
            Cursor.lockState = CursorLockMode.None;

#if UNITY_EDITOR
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
#else
            Cursor.visible = true;
#endif
            m_inputCaptured = false;
        }

        private void OnApplicationFocus(bool focus)
        {
            if (m_inputCaptured && !focus)
            {
                ReleaseInput();
            }
        }

        private void Update()
        {
            if (!m_inputCaptured)
            {
                if (!holdRightMouseCapture && Input.GetMouseButtonDown(0))
                {
                    CaptureInput();
                }
                else if (holdRightMouseCapture && Input.GetMouseButtonDown(1))
                {
                    CaptureInput();
                }
            }

            if (!m_inputCaptured)
            {
                return;
            }

            if (m_inputCaptured)
            {
                if (!holdRightMouseCapture && Input.GetKeyDown(KeyCode.Escape))
                {
                    ReleaseInput();
                }
                else if (holdRightMouseCapture && Input.GetMouseButtonUp(1))
                {
                    ReleaseInput();
                }
            }

            float rotStrafe = Input.GetAxis("Mouse X");
            float rotFwd = Input.GetAxis("Mouse Y");

            m_yaw = (m_yaw + lookSpeed * rotStrafe) % 360f;
            m_pitch = (m_pitch - lookSpeed * rotFwd) % 360f;
            transform.rotation = Quaternion.AngleAxis(m_yaw, Vector3.up) * Quaternion.AngleAxis(m_pitch, Vector3.right);

            float speed = Time.deltaTime * (Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : moveSpeed);
            float forward = speed * Input.GetAxis("Vertical");
            float right = speed * Input.GetAxis("Horizontal");
            float up = speed * ((Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f));
            transform.position += transform.forward * forward + transform.right * right + Vector3.up * up;
        }

        private void SetFPSFrame(bool UseHighFPS, int TargetFPS)
        {
            if (UseHighFPS == true)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = TargetFPS;
            }
            else
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = 60;
            }
        }
#endif
    }
}
