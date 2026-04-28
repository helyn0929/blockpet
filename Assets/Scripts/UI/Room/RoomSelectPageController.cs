using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Room selection page shown after login.
/// - Displays current room code (top-right recommended) and supports copy/share.
/// - Join existing room by entering code.
/// - Create a new room by generating a code.
/// </summary>
public class RoomSelectPageController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] TMP_Text currentRoomCodeText;
    [SerializeField] Button copyCodeButton;
    [SerializeField] TMP_InputField joinRoomInput;
    [SerializeField] Button joinButton;
    [SerializeField] Button createNewButton;
    [SerializeField] Button continueButton;
    [SerializeField] TMP_Text hintText;

    [Header("Behavior")]
    [Tooltip("Default room code when user does nothing.")]
    [SerializeField] string defaultRoomId = "global";
    [Tooltip("Generated room codes length.")]
    [SerializeField] int generatedCodeLength = 6;

    PageManager _pageManager;

    void Awake()
    {
        _pageManager = FindObjectOfType<PageManager>(true);
    }

    void OnEnable()
    {
        Wire(copyCodeButton, CopyCodeToClipboard);
        Wire(joinButton, JoinRoomFromInput);
        Wire(createNewButton, CreateNewRoom);
        Wire(continueButton, ContinueCurrentRoom);
        RefreshUi();
    }

    void OnDisable()
    {
        Unwire(copyCodeButton, CopyCodeToClipboard);
        Unwire(joinButton, JoinRoomFromInput);
        Unwire(createNewButton, CreateNewRoom);
        Unwire(continueButton, ContinueCurrentRoom);
    }

    void Wire(Button b, UnityEngine.Events.UnityAction a)
    {
        if (b != null) b.onClick.AddListener(a);
    }

    void Unwire(Button b, UnityEngine.Events.UnityAction a)
    {
        if (b != null) b.onClick.RemoveListener(a);
    }

    void RefreshUi()
    {
        var fb = FirebaseManager.Instance;
        string room = fb != null ? fb.RoomId : defaultRoomId;
        if (string.IsNullOrWhiteSpace(room)) room = defaultRoomId;

        if (currentRoomCodeText != null)
            currentRoomCodeText.text = room;

        if (hintText != null)
            hintText.text = "輸入相同房間碼即可連動";
    }

    void SetRoom(string roomId)
    {
        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.SetRoomId(roomId);
        RefreshUi();
    }

    void EnterMainGameAndGoHome()
    {
        var login = FindObjectOfType<LoginUIHandler>(true);
        if (login != null)
            login.EnterMainGame();

        if (_pageManager == null)
            _pageManager = FindObjectOfType<PageManager>(true);
        if (_pageManager != null)
            _pageManager.ShowHomePage();
    }

    void CopyCodeToClipboard()
    {
        string code = FirebaseManager.Instance != null ? FirebaseManager.Instance.RoomId : defaultRoomId;
        if (string.IsNullOrWhiteSpace(code)) code = defaultRoomId;
        GUIUtility.systemCopyBuffer = code;
        if (hintText != null)
            hintText.text = "已複製房間碼";
    }

    void JoinRoomFromInput()
    {
        string code = joinRoomInput != null ? joinRoomInput.text : string.Empty;
        code = (code ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code))
        {
            if (hintText != null) hintText.text = "請輸入房間碼";
            return;
        }
        SetRoom(code);
        EnterMainGameAndGoHome();
    }

    void ContinueCurrentRoom()
    {
        if (FirebaseManager.Instance == null)
            SetRoom(defaultRoomId);
        EnterMainGameAndGoHome();
    }

    void CreateNewRoom()
    {
        string code = GenerateCode(generatedCodeLength);
        SetRoom(code);
        GUIUtility.systemCopyBuffer = code;
        if (hintText != null)
            hintText.text = "已建立新房間並複製房間碼";
        EnterMainGameAndGoHome();
    }

    static string GenerateCode(int len)
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // avoid 0/O/1/I ambiguity
        len = Mathf.Clamp(len, 4, 12);
        var chars = new char[len];
        for (int i = 0; i < len; i++)
            chars[i] = alphabet[Random.Range(0, alphabet.Length)];
        return new string(chars);
    }
}

