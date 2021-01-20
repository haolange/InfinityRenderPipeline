using UnityEngine;

public class CameraMovement : MonoBehaviour {
#if UNITY_EDITOR
	static Texture2D ms_invisibleCursor = null;
#endif

	public bool enableInputCapture = true;
	public bool holdRightMouseCapture = false;

	public float lookSpeed = 5f;
	public float moveSpeed = 5f;
	public float sprintSpeed = 50f;

	public float FPSWait = 1000;

	public int state = 1000;

	//float fps = 0;
	//float ms = 0;

	bool	m_inputCaptured;
	float	m_yaw;
	float	m_pitch;


    [Header("DeBugProperty")]
    [SerializeField]
    bool DisableFPSLimit = false;

	
	void Awake() {
		enabled = enableInputCapture;
        //#if UNITY_EDITOR
            //////DeBug FPS//////
            SetFPSFrame(DisableFPSLimit, -1);
        //#endif
	}
	
    void OnGUI()
    {
		/*GUIStyle FPSGUI = new GUIStyle();
		FPSGUI.fontSize = 32;
		FPSGUI.normal.textColor = new Color(1, 0, 0);

        if(DisableFPSLimit == true) 
		{
			state++;
			if (state > FPSWait)
            {
				state = 0;
				fps = 1 / Time.deltaTime;
				ms = 1000 / (1 / Time.deltaTime);
			}

            GUI.Label(new Rect(32, 32, 512, 512), "FPS : " + fps + "     " + "ms : " + ms, FPSGUI);
            //GUI.color = Color.red;
        }*/
    }

	void OnValidate() {
		if(Application.isPlaying)
			enabled = enableInputCapture;
	}

	void CaptureInput() {
		//按下后隐藏鼠标
		Cursor.lockState = CursorLockMode.Locked;
		//Cursor.lockState = CursorLockMode.None;

#if UNITY_EDITOR
		Cursor.SetCursor(ms_invisibleCursor, Vector2.zero, CursorMode.ForceSoftware);
#else
		Cursor.visible = false;
#endif
		m_inputCaptured = true;

		m_yaw = transform.eulerAngles.y;
		//m_yaw = Input.mousePosition.y;
		m_pitch = transform.eulerAngles.x;
		//m_pitch = Input.mousePosition.x;
	}

	void ReleaseInput() {
		Cursor.lockState = CursorLockMode.None;
#if UNITY_EDITOR
		Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
#else
		Cursor.visible = true;
#endif
		m_inputCaptured = false;
	}

	void OnApplicationFocus(bool focus) {
		if(m_inputCaptured && !focus)
			ReleaseInput();
	}

	void Update() 
	{
		if(!m_inputCaptured) {
			if(!holdRightMouseCapture && Input.GetMouseButtonDown(0)) 
				CaptureInput();
			else if(holdRightMouseCapture && Input.GetMouseButtonDown(1))
				CaptureInput();
		}

		if(!m_inputCaptured)
			return;

		if(m_inputCaptured) {
			if(!holdRightMouseCapture && Input.GetKeyDown(KeyCode.Escape))
				ReleaseInput();
			else if(holdRightMouseCapture && Input.GetMouseButtonUp(1))
				ReleaseInput();
		}

		var rotStrafe = Input.GetAxis("Mouse X");
		var rotFwd = Input.GetAxis("Mouse Y");

		m_yaw = (m_yaw + lookSpeed * rotStrafe) % 360f;
		m_pitch = (m_pitch - lookSpeed * rotFwd) % 360f;
		transform.rotation = Quaternion.AngleAxis(m_yaw, Vector3.up) * Quaternion.AngleAxis(m_pitch, Vector3.right);

		var speed = Time.deltaTime * (Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : moveSpeed);
		var forward = speed * Input.GetAxis("Vertical");
		var right = speed * Input.GetAxis("Horizontal");
		var up = speed * ((Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f));
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
}
