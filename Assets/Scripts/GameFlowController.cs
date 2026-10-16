using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Runtime UI keeps the battle scene, avatar assets and controller sessions alive.
public sealed partial class GameFlowController : MonoBehaviour
{
    public enum Screen { Title, Difficulty, Characters, Preview, Playing }
    public Screen CurrentScreen { get; private set; } = Screen.Title;
    public bool IsPlaying => CurrentScreen == Screen.Playing && !pauseOpen && !ScoreDialogOpen;
    public bool PauseOpen => pauseOpen;
    private MasterController master;
    private DuelPresentation presentation;
    private PhoneControllerHub phone;
    private GameObject menuCanvas, pauseCanvas, tutorialCanvas;
    private Text tutorialPrompt;
    private ControllerAxisCalibrationPanel axisCalibration;
    private ControllerRegistrationPanel registration;
    private readonly Text[] controllerStatus = new Text[2];
    private readonly Button[] pauseDisplayButtons = new Button[2];
    private readonly Dictionary<Button, Outline> pauseFocusOutlines = new Dictionary<Button, Outline>();
    private Button pauseResumeButton;
    private Transform pauseDisplayOptions;
    private Transform page;
    private Font font;
    private Texture2D menuBackground;
    private readonly GameObject[] players = new GameObject[2];
    private readonly GameObject[] swords = new GameObject[2];
    private readonly MotionController[] motion = new MotionController[2];
    private readonly Preview[] previews = new Preview[2];
    private readonly HashSet<Renderer> hiddenGameplayRenderers = new HashSet<Renderer>();
    private readonly bool[] ready = new bool[2];
    private readonly int[] row = new int[2];
    private readonly bool[] focusedBack = new bool[2];
    private readonly int[] focusedCharacter = new int[2];
    private readonly int[] focusedColor = new int[2];
    private readonly bool[] guardWasHeld = new bool[2];
    private readonly MenuGestureNavigation[] gestures = { new MenuGestureNavigation(), new MenuGestureNavigation() };
    private readonly MenuResetButton[] resetButtons = { new MenuResetButton(), new MenuResetButton() };
    private readonly ControllerInputSource[] resetSource = new ControllerInputSource[2];
    private bool pauseOpen;
    private bool singleDifficultyFlow;
    private bool previewBackFocused;
    private int titleChoice, difficultyChoice = 1;
    private float inputAfter;
    private static readonly Color Ink = new Color(.10f, .24f, .34f);
    private static readonly Color Muted = new Color(.32f, .46f, .56f);
    private static readonly Color Sky = new Color(.08f, .57f, .86f);
    private static readonly Color Pale = new Color(.90f, .96f, 1f);
    private static readonly Color[] Palette = {
        new Color(.96f,.24f,.28f), new Color(.2f,.61f,1f), new Color(.23f,.8f,.49f),
        new Color(1f,.8f,.24f), new Color(.72f,.39f,.94f), new Color(.21f,.24f,.3f) };

    public void Initialize(MasterController controls, GameObject p1, GameObject p2, GameObject s1, GameObject s2)
    {
        master = controls; presentation = master.GetComponent<DuelPresentation>();
        players[0] = p1; players[1] = p2; swords[0] = s1; swords[1] = s2;
        motion[0] = s1.GetComponent<MotionController>(); motion[1] = s2.GetComponent<MotionController>();
        font = DuelPresentation.CreateJapaneseFont();
        menuBackground=new Texture2D(1,2,TextureFormat.RGBA32,false){name="Menu sky gradient",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        menuBackground.SetPixels(new[]{Color.white,new Color(.83f,.93f,1f)});menuBackground.Apply();
        phone = PhoneControllerHub.EnsureInstance();
        SyncFocus();
        menuCanvas = Canvas("Game flow", 1200); pauseCanvas = Canvas("Connection pause", 1210);
        BuildPause(); pauseCanvas.SetActive(false);
        BuildTutorialOverlay(); tutorialCanvas.SetActive(false);
        BuildSessionTimer();
        axisCalibration = gameObject.AddComponent<ControllerAxisCalibrationPanel>();
        axisCalibration.Initialize(motion, font, CloseAxisCalibration);
        registration = gameObject.AddComponent<ControllerRegistrationPanel>();
        registration.Initialize(motion, font, CloseAxisCalibration);
        scoreClient = new ScoreboardClient();
        master.SetGameplaySuspended(true); phone.SetOverlayVisible(false);
        BuildPage();
    }

    private void Update()
    {
        if (master == null) return;
        UpdateLeaderboard();
        UpdateTutorialOverlay();
        UpdateSessionTimer();
        if ((axisCalibration != null && axisCalibration.IsOpen) || (registration != null && registration.IsOpen)) return;
        if (ScoreDialogOpen) { UpdateScoreDialog(); return; }
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
        }
        if (pauseOpen)
        {
            UpdateControllerStatus();
            RefreshPauseFocus();
        }
        for (int i = 0; i < 2; i++)
        {
            MotionController sensor = motion[i];
            ReadReset(i, sensor);
            bool guard = (sensor != null && sensor.GuardHeld) || (keyboard != null &&
                (i == 0 ? keyboard.leftShiftKey.isPressed : keyboard.rightShiftKey.isPressed));
            bool confirm = guard && !guardWasHeld[i]; guardWasHeld[i] = guard;
            Vector2Int move = ReadMove(i, keyboard);
            if (Time.unscaledTime < inputAfter) continue;
            if (pauseOpen)
            {
                if (move != Vector2Int.zero) NavigatePause(move);
                if (confirm) ConfirmPause();
                continue;
            }
            if (CurrentScreen == Screen.Preview)
            {
                if (move != Vector2Int.zero) Navigate(i, move);
                if (confirm) Confirm(i);
                continue;
            }
            if (CurrentScreen == Screen.Playing)
            {
                if (presentation != null && presentation.ResultVisible)
                {
                    if (move != Vector2Int.zero) presentation.NavigateResult(move);
                    if (confirm) { presentation.ConfirmResult(); inputAfter = Time.unscaledTime + .2f; }
                }
                continue;
            }
            if (move != Vector2Int.zero) Navigate(i, move);
            if (confirm) Confirm(i);
        }
        for (int i = 0; i < 2; i++) if (previews[i] != null)
            previews[i].Tick(Time.unscaledTime, Time.unscaledDeltaTime, motion[i]);
    }

    private void ReadReset(int player, MotionController sensor)
    {
        ControllerInputSource source = sensor != null ? sensor.ActiveSource : ControllerInputSource.None;
        bool connected = sensor != null && sensor.IsConnected && !sensor.IsStale;
        if (source != resetSource[player])
        {
            resetButtons[player].Read(false, false, 0, Time.unscaledTime);
            resetSource[player] = source;
        }
        MenuResetAction action = resetButtons[player].Read(connected,
            connected && sensor.RecenterHeld, connected ? sensor.ResetCounter : (ushort)0, Time.unscaledTime);
        if (action == MenuResetAction.LongPress)
        {
            if (CurrentScreen == Screen.Preview) CloseCharacterPreview();
            else TogglePause();
        }
        else if (action == MenuResetAction.ShortPress)
        {
            sensor.Recenter();
            if (CurrentScreen == Screen.Playing && !pauseOpen) master.NotifyTutorialReset();
        }
    }

    private void UpdateControllerStatus()
    {
        for(int i=0;i<2;i++)
        {
            MotionController sensor=motion[i];
            ControllerInputSource source=sensor!=null&&sensor.IsConnected?sensor.ActiveSource:ControllerInputSource.None;
            controllerStatus[i].text="P"+(i+1)+" "+
                (source==ControllerInputSource.Phone?"スマホ":source==ControllerInputSource.Hardware?"コントローラ":"未接続");
            controllerStatus[i].color=Ink;
            // Clear old runtime text effects as well as constructing new labels
            // without them. Outline duplicates every glyph around the label.
            foreach (Shadow effect in controllerStatus[i].GetComponents<Shadow>())
            {
                effect.enabled=false;
                Destroy(effect);
            }
        }
    }

    private Vector2Int ReadMove(int i, Keyboard keyboard)
    {
        Vector2Int result = Vector2Int.zero;
        if (keyboard != null)
        {
            if ((i == 0 ? keyboard.aKey : keyboard.leftArrowKey).wasPressedThisFrame) result = Vector2Int.left;
            if ((i == 0 ? keyboard.dKey : keyboard.rightArrowKey).wasPressedThisFrame) result = Vector2Int.right;
            if ((i == 0 ? keyboard.wKey : keyboard.upArrowKey).wasPressedThisFrame) result = Vector2Int.up;
            if ((i == 0 ? keyboard.sKey : keyboard.downArrowKey).wasPressedThisFrame) result = Vector2Int.down;
        }
        MotionController sensor = motion[i];
        MotionSample sample = default;
        bool valid = sensor != null && sensor.IsPoseValid
            && sensor.TryGetMenuSample(out sample);
        Vector2Int flick = gestures[i].Read(sample.Rotation, sample.AngularVelocityDegrees, sample.GyroscopeValid, valid,
            valid && sensor.GuardHeld, valid ? sensor.PoseGeneration : -1,
            valid ? sample.BrowserTimestampMilliseconds / 1000.0 : double.NaN, sensor != null && sensor.ActiveSource == ControllerInputSource.Phone);
        return result != Vector2Int.zero ? result : flick;
    }

    private int SelectionPlayer(int controller) => master.IsSingleMode ? 0 : controller;
    private void Navigate(int controller, Vector2Int direction)
    {
        if (CurrentScreen == Screen.Title)
        { titleChoice = Wrap(titleChoice + (direction.y != 0 ? -direction.y : direction.x), 4); BuildPage(); return; }
        if (CurrentScreen == Screen.Difficulty)
        { difficultyChoice=Wrap(difficultyChoice+(direction.y!=0?-direction.y:direction.x),3);BuildPage();return; }
        if (CurrentScreen == Screen.Preview)
        {
            if(direction.y!=0) { previewBackFocused=direction.y<0;BuildPage(); }
            return;
        }
        if (CurrentScreen != Screen.Characters) return;
        int i = SelectionPlayer(controller);
        ready[i] = false;
        if (direction.y != 0) row[i] = Wrap(row[i] - direction.y,4);
        else if (row[i] == 0) focusedCharacter[i] = Wrap(focusedCharacter[i] + direction.x, master.CharacterCount);
        else if (row[i] == 1) focusedColor[i] = Wrap(focusedColor[i] + direction.x, Palette.Length);
        else if (row[i] == 3) focusedBack[i] = direction.x < 0;
        BuildPage();
    }

    private void Confirm(int controller)
    {
        if (CurrentScreen == Screen.Title)
        {
            if (titleChoice == 0) ChooseSingleMode();
            else if (titleChoice == 1) ChooseMode(false);
            else if (titleChoice == 2) ChooseFreeMode();
            else ChooseTutorialMode();
            return;
        }
        if (CurrentScreen == Screen.Difficulty)
        { if(difficultyChoice==2)Back();else ChooseDifficulty(difficultyChoice==0);return; }
        if (CurrentScreen == Screen.Preview)
        { if(previewBackFocused)CloseCharacterPreview();return; }
        if (CurrentScreen != Screen.Characters) return;
        int i = SelectionPlayer(controller);
        if(row[i]==3){if(focusedBack[i])Back();else OpenCharacterPreview(i);return;}
        if (ready[i]) return;
        if (row[i] == 0) { master.SetCharacter(i, focusedCharacter[i]); BuildPage(); }
        else if (row[i] == 1) { master.SetColor(i, focusedColor[i]); BuildPage(); }
        else Ready(i);
    }

    private void SyncFocus()
    {
        for (int i = 0; i < 2; i++)
        { focusedCharacter[i] = master.CharacterIndex(i); focusedColor[i] = master.ColorIndex(i); }
    }

    public void ChooseMode(bool single)
    {
        if(!single)singleDifficultyFlow=false;
        master.SetGameplaySuspended(true); master.SetTutorialMode(false); master.SetFreeMode(false); master.SetGameMode(single);
        master.SetStage(single ? 2 : 0);
        OpenCharacters();
    }

    public void ChooseFreeMode()
    {
        singleDifficultyFlow=false;
        master.SetGameplaySuspended(true); master.SetTutorialMode(false); master.SetGameMode(false); master.SetFreeMode(true);
        master.SetStage(2);
        OpenCharacters();
    }

    public void ChooseTutorialMode()
    {
        singleDifficultyFlow=false;
        master.SetGameplaySuspended(true); master.SetFreeMode(false); master.SetGameMode(true);
        master.SetTutorialMode(true); master.SetStage(2);
        OpenCharacters();
    }

    public void ChooseSingleMode()
    {
        master.SetGameplaySuspended(true);singleDifficultyFlow=true;
        difficultyChoice=master.IsEasySingleDifficulty?0:1;
        ResetMenuGestures();CurrentScreen=Screen.Difficulty;
        inputAfter=Time.unscaledTime+.2f;BuildPage();
    }

    public void ChooseDifficulty(bool easy)
    {
        if(CurrentScreen!=Screen.Difficulty)return;
        master.SetSingleDifficulty(easy);ChooseMode(true);
    }

    private void OpenCharacters()
    {
        leaderboardRefreshAt = 0;
        ResetMenuGestures();
        phone.SetSinglePlayerMode(false); ready[0] = ready[1] = false; row[0] = row[1] = 0;
        focusedBack[0] = focusedBack[1] = false;
        SyncFocus(); CurrentScreen = Screen.Characters; inputAfter = Time.unscaledTime + .2f; BuildPage();
    }

    public void Ready(int controller)
    {
        if (CurrentScreen != Screen.Characters) return;
        int player = SelectionPlayer(controller);
        master.SetCharacter(player, focusedCharacter[player]); master.SetColor(player, focusedColor[player]);
        ready[player] = true;
        if (ready[0] && (master.IsSingleMode || ready[1]))
        { BeginMatch(); return; }
        BuildPage();
    }

    public void BeginMatch()
    {
        if (CurrentScreen != Screen.Characters || !ready[0] || (!master.IsSingleMode && !ready[1])) return;
        ResetMenuGestures();
        master.SetStage(master.IsFreeMode || master.IsSingleMode ? 2 : 0); master.ResetMatch();
        inputAfter = Time.unscaledTime + .25f;
        CurrentScreen = Screen.Playing; ClearPreviews(); menuCanvas.SetActive(false);
        RestoreGameplayRenderers(); presentation.SetMenuPresentation(false);
        master.SetGameplaySuspended(false); phone.SetOverlayVisible(false); ClearSelection();
    }

    private void CloseOverlays()
    {
        if(CurrentScreen==Screen.Preview)master.EndCharacterPreview();
        pauseOpen = false; pauseCanvas.SetActive(false);
        master.SetGameplaySuspended(true); phone.SetOverlayVisible(false); phone.SetSinglePlayerMode(false);
        master.ResetMatch();
    }
    public void ReturnToTitle()
    {
        CloseOverlays(); master.SetTutorialMode(false); CurrentScreen = Screen.Title; titleChoice = 0; singleDifficultyFlow=false;
        ResetMenuGestures();
        inputAfter = Time.unscaledTime + .25f; BuildPage();
    }
    public void ReturnToModes() => ReturnToTitle();
    public void ReturnToCharacters()
    {
        if(CurrentScreen==Screen.Preview){CloseCharacterPreview();return;}
        leaderboardRefreshAt = 0;
        CloseOverlays(); ready[0] = ready[1] = false; row[0] = row[1] = 0; SyncFocus();
        focusedBack[0] = focusedBack[1] = false;
        ResetMenuGestures();
        CurrentScreen = Screen.Characters; inputAfter = Time.unscaledTime + .25f; BuildPage();
    }
    public void Back()
    {
        if(CurrentScreen==Screen.Preview)CloseCharacterPreview();
        else if(CurrentScreen==Screen.Difficulty)ReturnToTitle();
        else if (CurrentScreen == Screen.Characters)
        {if(master.IsSingleMode&&singleDifficultyFlow)ChooseSingleMode();else ReturnToTitle();}
        else if (CurrentScreen == Screen.Playing && presentation != null && presentation.ResultVisible) ReturnToCharacters();
    }

    public void OpenCharacterPreview(int controller)
    {
        if(CurrentScreen!=Screen.Characters || pauseOpen)return;
        int player=SelectionPlayer(controller);
        if(player<0 || player>=players.Length)return;
        CurrentScreen=Screen.Preview;previewBackFocused=false;ResetMenuGestures();
        inputAfter=Time.unscaledTime+.2f;BuildPage();
        master.BeginCharacterPreview(player);
    }

    public void CloseCharacterPreview()
    {
        if(CurrentScreen!=Screen.Preview)return;
        pauseOpen=false;pauseCanvas.SetActive(false);phone.SetOverlayVisible(false);
        master.EndCharacterPreview();master.SetGameplaySuspended(true);
        CurrentScreen=Screen.Characters;ResetMenuGestures();
        inputAfter=Time.unscaledTime+.2f;BuildPage();
    }

    public void TogglePause()
    {
        if (ScoreDialogOpen) return;
        pauseOpen = !pauseOpen; master.SetGameplaySuspended(pauseOpen || CurrentScreen != Screen.Playing);
        pauseCanvas.SetActive(pauseOpen); phone.SetSinglePlayerMode(false);
        UpdateTutorialOverlay();
        if(pauseOpen){UpdateControllerStatus();RefreshPauseDisplayOptions();}
        menuCanvas.SetActive(!pauseOpen && CurrentScreen != Screen.Playing);
        if(pauseOpen && CurrentScreen!=Screen.Playing && CurrentScreen!=Screen.Preview)
        {
            RawImage background=pauseCanvas.transform.Find("Connection controls/Menu background").GetComponent<RawImage>();
            background.enabled=true;
        }
        else pauseCanvas.transform.Find("Connection controls/Menu background").GetComponent<RawImage>().enabled=false;
        phone.SetOverlayVisible(false); inputAfter = Time.unscaledTime + .2f;
        ResetMenuGestures(); ClearSelection();
        if (pauseOpen)
        {
            foreach (Button button in PauseButtons())
            {
                ColorBlock colors = button.colors;
                colors.selectedColor = new Color(.74f, .88f, .98f);
                button.colors = colors;
            }
            pauseResumeButton.Select();
            RefreshPauseFocus();
        }
    }

    private void NavigatePause(Vector2Int direction)
    {
        if (EventSystem.current == null) return;
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        Button current = selected != null ? selected.GetComponent<Button>() : null;
        if (current == null || !current.isActiveAndEnabled)
        { pauseResumeButton.Select(); RefreshPauseFocus(); return; }
        UnityEngine.Canvas.ForceUpdateCanvases();
        Vector2 origin = ButtonCentre(current);
        Button next = null;
        float bestScore = 0f;
        foreach (Button candidate in PauseButtons())
        {
            if (candidate == current || !candidate.IsInteractable()) continue;
            Vector2 offset = ButtonCentre(candidate) - origin;
            float dot = Vector2.Dot(direction, offset);
            if (dot <= 0f) continue;
            float score = dot / offset.sqrMagnitude;
            if (score > bestScore) { bestScore = score; next = candidate; }
        }
        if (next != null) { next.Select(); RefreshPauseFocus(); }
    }

    private void RefreshPauseFocus()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        foreach (Button button in PauseButtons())
        {
            if (!pauseFocusOutlines.TryGetValue(button, out Outline outline))
            {
                outline = button.image.gameObject.AddComponent<Outline>();
                outline.effectColor = Sky; outline.effectDistance = new Vector2(3, -3);
                pauseFocusOutlines.Add(button, outline);
            }
            outline.enabled = button.gameObject == selected;
        }
    }

    private IEnumerable<Button> PauseButtons()
    {
        foreach (Button button in pauseCanvas.GetComponentsInChildren<Button>()) yield return button;
    }

    private void RestartFromPause()
    {
        if (!pauseOpen) return;
        master.ResetMatch();
        TogglePause();
    }

    private void BuildTutorialOverlay()
    {
        tutorialCanvas = Canvas("Tutorial guidance", 1000);
        Transform panel = FullScreen(tutorialCanvas.transform, "Tutorial instruction");
        Image background = Box(panel, "Instruction background", new Vector2(0f, 365f),
            new Vector2(1570f, 180f), new Color(.91f, .97f, 1f, .92f));
        background.raycastTarget = false;
        tutorialPrompt = Label(panel, "", new Vector2(0f, 365f), new Vector2(1530f, 160f), 34, Ink);
        tutorialPrompt.resizeTextMinSize = 28;
    }

    private void UpdateTutorialOverlay()
    {
        if (tutorialCanvas == null) return;
        bool visible = CurrentScreen == Screen.Playing && master.IsTutorialMode && !pauseOpen;
        tutorialCanvas.SetActive(visible);
        if (!visible) return;
        tutorialPrompt.text = master.TutorialInstruction;
    }

    private static Vector2 ButtonCentre(Button button)
    {
        RectTransform rect = (RectTransform)button.transform;
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }

    private void ConfirmPause()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null) return;
        foreach (Button button in PauseButtons())
            if (button.gameObject == selected && button.IsInteractable())
            { button.onClick.Invoke(); inputAfter = Time.unscaledTime + .2f; return; }
    }
    private void BuildPage()
    {
        ClearPreviews();
        // Portrait clones must inherit the original render state before the
        // scene models are hidden again for the selection page.
        RestoreGameplayRenderers();
        if(CurrentScreen!=Screen.Preview)presentation.SetMenuPresentation(true);
        if (page != null) { page.gameObject.SetActive(false); Destroy(page.gameObject); }
        menuCanvas.SetActive(true); page = FullScreen(menuCanvas.transform, "Page");
        if(CurrentScreen!=Screen.Preview)
        {
            RawImage background=FullScreen(page,"Menu background").gameObject.AddComponent<RawImage>();
            background.texture=menuBackground;background.raycastTarget=false;
        }
        if (CurrentScreen == Screen.Title) BuildTitle();
        else if(CurrentScreen==Screen.Difficulty)BuildDifficulty();
        else if (CurrentScreen == Screen.Characters) BuildCharacters();
        else if(CurrentScreen==Screen.Preview)
        { Button back=BackButton(page,"もどる",CloseCharacterPreview);if(previewBackFocused)Outline(back.image,Sky); }
        if(CurrentScreen!=Screen.Playing && CurrentScreen!=Screen.Preview) HideGameplayRenderers();
        ClearSelection();
    }

    private void HideGameplayRenderers()
    {
        for(int i=0;i<2;i++)
            foreach(GameObject root in new[]{players[i],swords[i]})
                if(root!=null)
                    foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                        if(!renderer.forceRenderingOff)
                        {hiddenGameplayRenderers.Add(renderer);renderer.forceRenderingOff=true;}
    }
    private void RestoreGameplayRenderers()
    {
        foreach(Renderer renderer in hiddenGameplayRenderers)
            if(renderer!=null)renderer.forceRenderingOff=false;
        hiddenGameplayRenderers.Clear();
    }

    private void BuildTitle()
    {
        PreviewImage(0,new Vector2(-640,45),new Vector2(490,770));
        PreviewImage(1,new Vector2(640,45),new Vector2(490,770));
        Choice(page,"ひとりで遊ぶ",new Vector2(0,225),titleChoice==0,ChooseSingleMode);
        Choice(page,"ふたりで対戦",new Vector2(0,75),titleChoice==1,()=>ChooseMode(false));
        Choice(page,"フリーモード",new Vector2(0,-75),titleChoice==2,ChooseFreeMode);
        Choice(page,"チュートリアル",new Vector2(0,-225),titleChoice==3,ChooseTutorialMode);
    }

    private void BuildDifficulty()
    {
        Choice(page,"イージー",new Vector2(0,80),difficultyChoice==0,()=>ChooseDifficulty(true));
        Choice(page,"ノーマル",new Vector2(0,-80),difficultyChoice==1,()=>ChooseDifficulty(false));
        Button back=BackButton(page,"もどる",ReturnToTitle);
        if(difficultyChoice==2)Outline(back.image,Sky);
    }

    private void RefreshPauseDisplayOptions()
    {
        pauseDisplayOptions.gameObject.SetActive(!master.IsSingleMode);
        for(int i=0;i<2;i++)
        {
            bool selected=master.DisplayMode==i+1;
            pauseDisplayButtons[i].image.color=selected?Pale:Color.white;
            pauseDisplayButtons[i].GetComponent<Outline>().effectColor=selected?Sky:Pale;
            pauseDisplayButtons[i].GetComponentInChildren<Text>().text=(i==0?"画面分割":"1画面")+(selected?" ✓":"");
        }
    }

    public void ChooseDisplayMode(int mode)
    {
        if(!pauseOpen||master.IsSingleMode)return;
        master.SetDisplayMode(mode);RefreshPauseDisplayOptions();
    }

    private void BuildCharacters()
    {
        bool showRanking = master.IsSingleMode && !master.IsTutorialMode;
        int count = master.IsSingleMode ? 1 : 2;
        for (int i=0;i<count;i++)
        {
            int player=i; float x=count==1?(showRanking?-465:0):i==0?-465:465; Color accent=Palette[i];
            if(ready[i])Label(page,"準備OK",new Vector2(x,290),new Vector2(510,44),25,accent);
            PreviewImage(i,new Vector2(x,120),new Vector2(640,390));
            Label(page,CharacterLabel(master.CharacterIndex(i)),new Vector2(x,-84),new Vector2(750,48),32,Ink);
            int n=Mathf.Max(1,master.CharacterCount); float width=Mathf.Min(180,760f/n);
            for(int c=0;c<n;c++)
            {
                int character=c; bool selected=master.CharacterIndex(i)==c;
                Button b=Button(page,CharacterLabel(c)+(selected?" ✓":""),new Vector2(x+(c-(n-1)*.5f)*(width+8),-163),new Vector2(width,70),
                    selected?Pale:Color.white,()=>{ready[player]=false;row[player]=0;focusedCharacter[player]=character;master.SetCharacter(player,character);BuildPage();});
                if(row[i]==0&&focusedCharacter[i]==c) Outline(b.image,Sky);
                else Outline(b.image,selected?accent:Pale);
            }
            for(int c=0;c<Palette.Length;c++)
            {
                int color=c;
                Button b=Button(page,"",new Vector2(x+(c-2.5f)*83,-255),new Vector2(65,56),Palette[c],()=>{ready[player]=false;row[player]=1;focusedColor[player]=color;master.SetColor(player,color);BuildPage();});
                if(row[i]==1&&focusedColor[i]==c) Outline(b.image,Sky);
                if(master.ColorIndex(i)==c) Label(b.transform,"✓",Vector2.zero,new Vector2(45,45),28,Color.white);
            }
            Button readyButton=Button(page,"スタート",new Vector2(x,-355),new Vector2(760,70),
                ready[i]?accent:Pale,()=>{if(ready[player]){ready[player]=false;BuildPage();}else Ready(player);});
            if(row[i]==2) Outline(readyButton.image,Sky);
            Button previewButton=Button(page,"プレビュー",Vector2.zero,new Vector2(220,55),Pale,()=>OpenCharacterPreview(player));
            RectTransform previewRect=previewButton.GetComponent<RectTransform>();
            previewRect.anchorMin=previewRect.anchorMax=new Vector2((count==1&&!showRanking)||i==1?1f:.5f,0f);
            previewRect.pivot=new Vector2(1f,0f);previewRect.anchoredPosition=new Vector2(-60,60);
            if(row[i]==3&&!focusedBack[i])Outline(previewButton.image,Sky);
        }
        Button back=BackButton(page,"もどる",Back);
        for(int i=0;i<count;i++)if(row[i]==3&&focusedBack[i]) { Outline(back.image,Sky);break; }
        if(showRanking) BuildLeaderboard();
    }

    private void BuildPause()
    {
        Transform panel=FullScreen(pauseCanvas.transform,"Connection controls");
        RawImage background=FullScreen(panel,"Menu background").gameObject.AddComponent<RawImage>();
        background.texture=menuBackground;background.raycastTarget=false;background.enabled=false;
        pauseDisplayOptions=FullScreen(panel,"Display options");
        for(int i=0;i<2;i++)
        {
            int mode=i+1;
            pauseDisplayButtons[i]=Button(pauseDisplayOptions,i==0?"画面分割":"1画面",new Vector2(i==0?-140:140,430),new Vector2(250,54),Pale,()=>ChooseDisplayMode(mode));
            Outline(pauseDisplayButtons[i].image,Pale);
            controllerStatus[i]=Label(panel,"",new Vector2(i==0?-225:225,300),new Vector2(340,44),26,Ink);
            controllerStatus[i].resizeTextForBestFit=false;
        }
        BackButton(panel,"タイトルに戻る",ReturnToTitle,240);
        pauseResumeButton=BackButton(panel,"ゲームに戻る",TogglePause,160);
        BackButton(panel,"やり直す",RestartFromPause,80);
        BackButton(panel,"コントローラ",OpenControllerRegistration);
    }

    public void OpenControllerRegistration()
    {
        if (!pauseOpen || registration == null || Time.unscaledTime < inputAfter) return;
        pauseCanvas.SetActive(false); phone.SetOverlayVisible(false);
        ResetMenuGestures(); registration.Open();
    }

    public void OpenAxisCalibration()
    {
        if (!pauseOpen || axisCalibration == null || Time.unscaledTime < inputAfter) return;
        pauseCanvas.SetActive(false);
        phone.SetOverlayVisible(false);
        ResetMenuGestures();
        axisCalibration.Open();
    }

    private void CloseAxisCalibration()
    {
        pauseCanvas.SetActive(pauseOpen);
        phone.SetOverlayVisible(false);
        ResetMenuGestures();
        for (int i=0;i<2;i++)
        {
            MotionController sensor=motion[i];
            guardWasHeld[i]=sensor!=null&&sensor.GuardHeld;
            // The wizard consumes reset presses while normal menus are paused.
            // Do not replay its reset counter as another recenter on return.
            bool connected=sensor!=null&&sensor.IsConnected&&!sensor.IsStale;
            resetButtons[i].Read(false,false,0,Time.unscaledTime);
            resetButtons[i].Read(connected,connected&&sensor.RecenterHeld,
                connected?sensor.ResetCounter:(ushort)0,Time.unscaledTime);
            resetSource[i]=sensor!=null?sensor.ActiveSource:ControllerInputSource.None;
        }
        inputAfter=Time.unscaledTime+.2f;
        if (pauseOpen) { pauseResumeButton.Select(); RefreshPauseFocus(); }
    }

    private string CharacterLabel(int index)
    {
        if(master.CharacterCount==0)return "キャラクター";
        switch(master.CharacterName(index))
        { case "ORIGINAL":return "ベーシック"; case "BALD":return "すっきり"; case "AFRO":return "アフロ"; case "ROBOT":return "ロボット"; default:return master.CharacterName(index); }
    }
    private void PreviewImage(int player,Vector2 position,Vector2 size)
    {
        previews[player]=new Preview(players[player],swords[player],player,size.x/size.y);
        GameObject image=new GameObject("Live fighter preview",typeof(RectTransform),typeof(RawImage));image.transform.SetParent(page,false);
        RawImage raw=image.GetComponent<RawImage>();Place(raw.rectTransform,position,size);raw.texture=previews[player].Texture;raw.raycastTarget=false;
    }
    private void ClearPreviews(){for(int i=0;i<2;i++){previews[i]?.Dispose();previews[i]=null;}}
    private void Choice(Transform parent,string title,Vector2 position,bool selected,Action action)
    {
        Button b=Button(parent,title,position,new Vector2(650,125),selected?Pale:Color.white,action);
        Outline(b.image,selected?Sky:Pale);
        Text label=b.GetComponentInChildren<Text>();label.fontSize=label.resizeTextMaxSize=36;label.resizeTextMinSize=26;
    }
    private GameObject Canvas(string name,int order)
    {
        GameObject root=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.transform.SetParent(transform,false);
        UnityEngine.Canvas c=root.GetComponent<UnityEngine.Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=order;
        CanvasScaler scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;return root;
    }
    private static Image Box(Transform parent,string name,Vector2 position,Vector2 size,Color color)
    {GameObject go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);Image image=go.GetComponent<Image>();image.color=color;Place(image.rectTransform,position,size);return image;}
    private static RectTransform FullScreen(Transform parent,string name)
    {
        GameObject go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
        RectTransform rect=go.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
        rect.offsetMin=rect.offsetMax=Vector2.zero;return rect;
    }
    private Text Label(Transform parent,string value,Vector2 position,Vector2 size,int fontSize,Color color,TextAnchor alignment=TextAnchor.MiddleCenter)
    {
        GameObject go=new GameObject(value,typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);Text text=go.GetComponent<Text>();text.font=font;text.fontSize=fontSize;text.text=value;text.color=color;text.alignment=alignment;text.raycastTarget=false;
        text.resizeTextForBestFit=true;text.resizeTextMinSize=Mathf.Max(12,fontSize-10);text.resizeTextMaxSize=fontSize;Place(text.rectTransform,position,size);return text;
    }
    private Button Button(Transform parent,string label,Vector2 position,Vector2 size,Color color,Action action)
    {
        Image image=Box(parent,label,position,size,color);Button button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;
        button.navigation=new Navigation{mode=Navigation.Mode.None};button.onClick.AddListener(()=>action());
        ColorBlock colors=button.colors;colors.highlightedColor=new Color(.9f,.96f,1f);colors.pressedColor=new Color(.74f,.88f,.98f);button.colors=colors;
        Label(button.transform,label,Vector2.zero,size-new Vector2(24,8),24,color==Sky||color==Palette[0]||color==Palette[1]?Color.white:Ink);return button;
    }
    private Button BackButton(Transform parent,string label,Action action,float offsetY=0)
    {
        Button button=Button(parent,label,Vector2.zero,new Vector2(220,55),Pale,action);
        RectTransform rect=button.GetComponent<RectTransform>();
        rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.zero;
        rect.anchoredPosition=new Vector2(60,60+offsetY);
        return button;
    }
    private static void Outline(Graphic graphic,Color color){Outline outline=graphic.gameObject.AddComponent<Outline>();outline.effectColor=color;outline.effectDistance=new Vector2(3,-3);}
    private static void Place(RectTransform rect,Vector2 position,Vector2 size){rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;}
    private static int Wrap(int value,int count)=>count<=0?0:(value%count+count)%count;
    private void ResetMenuGestures(){foreach(MenuGestureNavigation gesture in gestures)gesture.Reset();}
    private static void ClearSelection(){if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(null);}
    private void OnDestroy(){if(timerChime!=null)Destroy(timerChime);scoreClient?.Dispose();if(master!=null&&CurrentScreen==Screen.Preview)master.EndCharacterPreview();ClearPreviews();RestoreGameplayRenderers();if(presentation!=null)presentation.SetMenuPresentation(false);if(menuBackground!=null)Destroy(menuBackground);}

    private sealed class Preview : IDisposable
    {
        public readonly RenderTexture Texture;
        private readonly GameObject root;
        private readonly Transform fighter,sword;
        private readonly Camera camera;
        private readonly DuelAvatar avatar;
        private bool disposed;
        public Preview(GameObject source,GameObject sourceSword,int index,float aspect)
        {
            root=new GameObject("Fighter preview studio "+index);root.transform.position=new Vector3(2000+index*30,2000,2000);
            fighter=new GameObject("Fighter").transform;fighter.SetParent(root.transform,false);
            sword=new GameObject("Preview sword").transform;sword.SetParent(fighter,false);
            // A portrait stance: hold the grip beside the hip and lean the blade
            // away from the face. Combat and menu navigation retain live input.
            sword.localPosition=new Vector3(-.6f,-.16f,.2f);
            sword.localRotation=PortraitSwordPose(0f);
            sword.localScale=sourceSword.transform.localScale*.6f;
            foreach(Transform child in source.transform)
                if(child.gameObject.activeSelf&&child!=sourceSword.transform&&child.name.StartsWith("Chambara"))Instantiate(child.gameObject,fighter,false);
            foreach(Transform child in sourceSword.transform)
                if(child.gameObject.activeSelf)Instantiate(child.gameObject,sword,false);
            // The title may clone P2 after a single-player match. Its portrait
            // is independent of the hidden game character's renderer settings.
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {renderer.enabled=true;renderer.forceRenderingOff=false;}
            foreach(MonoBehaviour behavior in root.GetComponentsInChildren<MonoBehaviour>()){behavior.enabled=false;Destroy(behavior);}
            foreach(Collider collider in root.GetComponentsInChildren<Collider>())collider.enabled=false;
            avatar=new DuelAvatar(fighter,sword);
            foreach(Transform t in root.GetComponentsInChildren<Transform>())t.gameObject.layer=31;
            avatar.Tick(0f,1f/60f,PortraitSwordPose(0f),false,0f);
            Texture=new RenderTexture(Mathf.RoundToInt(768*aspect),768,24,RenderTextureFormat.ARGB32){name="Fighter portrait",antiAliasing=2};Texture.Create();
            GameObject cameraObject=new GameObject("Portrait camera",typeof(Camera));cameraObject.transform.SetParent(root.transform,false);camera=cameraObject.GetComponent<Camera>();
            camera.targetTexture=Texture;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(1f,1f,1f,0f);camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=30f;
            Bounds bounds=new Bounds(fighter.position,Vector3.one);bool found=false;
            foreach(SkinnedMeshRenderer skin in fighter.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                // Use world-space joints, not imported renderer bounds or BakeMesh bounds.
                // Those include FBX scale compensation and combat culling margins, which
                // can dwarf the visible model and reduce the portrait to a few pixels.
                foreach(Transform bone in skin.bones)
                {
                    if(bone==null)continue;
                    if(!found){bounds=new Bounds(bone.position,Vector3.zero);found=true;}else bounds.Encapsulate(bone.position);
                }
            }
            // Include the real static sword geometry, whose tip reaches above
            // the head. Body renderer bounds include unrelated culling margins.
            foreach(MeshFilter mesh in sword.GetComponentsInChildren<MeshFilter>())
            {
                if(mesh.sharedMesh==null)continue;
                Bounds local=mesh.sharedMesh.bounds;
                for(int corner=0;corner<8;corner++)
                    bounds.Encapsulate(mesh.transform.TransformPoint(local.center+Vector3.Scale(local.extents,
                        new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1))));
            }
            camera.orthographicSize=Mathf.Max(1.05f,bounds.extents.y+.25f,(bounds.extents.x+.2f)/aspect);
            camera.transform.position=bounds.center+new Vector3(.45f,.16f,6f);camera.transform.LookAt(bounds.center);
            GameObject lightObject=new GameObject("Portrait softbox",typeof(Light));lightObject.transform.SetParent(root.transform,false);Light light=lightObject.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(38,155,0);
        }
        public void Tick(float time,float dt,MotionController sensor)
        {
            Quaternion pose=PortraitSwordPose(time);
            avatar.Tick(time,dt,pose,false,0f);
            sword.localRotation=pose;fighter.localRotation=Quaternion.Euler(0,Mathf.Sin(time*.38f)*8f,0);
        }
        private static Quaternion PortraitSwordPose(float time)
            =>Quaternion.AngleAxis(12f+Mathf.Sin(time*.65f)*2f,Vector3.forward)*Quaternion.Euler(-84f,0f,0f);
        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            // Scene unload can destroy the studio camera before the flow's OnDestroy.
            if(camera!=null)camera.targetTexture=null;
            if(root!=null){root.SetActive(false);Destroy(root);}
            if(Texture!=null){Texture.Release();Destroy(Texture);}
        }
    }
}
