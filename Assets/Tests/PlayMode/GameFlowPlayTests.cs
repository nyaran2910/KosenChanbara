using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class GameFlowPlayTests
{
    private MonoBehaviour flow, master, phone;
    private Keyboard keyboard;
    private InputSettings.BackgroundBehavior background;
    private InputSettings.EditorInputBehaviorInPlayMode editorInput;
    private static MonoBehaviour Find(string name) => Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(x => x.GetType().Name == name);
    private static object Get(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private string Screen => Get(flow, "CurrentScreen").ToString();

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        background=InputSystem.settings.backgroundBehavior;
        editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>();
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        flow = Find("GameFlowController"); master = Find("MasterController"); phone = Find("PhoneControllerHub");
    }
    private void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "MotionController"
                || component.GetType().Name == "HardwareControllerHub") component.enabled = false;
    }
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior=background;
        InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
        if(phone != null) Object.Destroy(phone.gameObject);
        foreach(MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if(component.GetType().Name=="HardwareControllerHub")Object.Destroy(component.gameObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SingleDifficultySupportsBothControllersMouseAndBackWithoutCommittingFocus()
    {
        foreach(int controller in new[]{0,1})
        {
            Call(flow,"ReturnToTitle");Call(master,"SetSingleDifficulty",false);
            if(controller==0)Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text=="ひとりで遊ぶ").onClick.Invoke();
            else Call(flow,"Confirm",controller);
            Assert.That(Screen,Is.EqualTo("Difficulty"));
            Assert.That(Field<int>(flow,"difficultyChoice"),Is.EqualTo(1));
            Call(flow,"Navigate",controller,Vector2Int.up);
            Assert.That(Get(master,"IsEasySingleDifficulty"),Is.False,"Navigation only moves focus.");
            Call(flow,"Confirm",controller);
            Assert.That(Screen,Is.EqualTo("Characters"));Assert.That(Get(master,"IsEasySingleDifficulty"),Is.True);
            Call(flow,"Back");Assert.That(Screen,Is.EqualTo("Difficulty"));
            Assert.That(Field<int>(flow,"difficultyChoice"),Is.Zero,"Returning preserves the selected difficulty.");
            Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text=="ノーマル").onClick.Invoke();
            Assert.That(Get(master,"IsEasySingleDifficulty"),Is.False);Assert.That(Screen,Is.EqualTo("Characters"));
            Call(flow,"Back");Call(flow,"Back");Assert.That(Screen,Is.EqualTo("Title"));
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator StartButtonsEnterPlayImmediatelyOnceTheRequiredPlayersAreReady()
    {
        foreach(int mode in new[]{0,1,2})
        {
            if(mode==2)Call(flow,"ChooseFreeMode");else Call(flow,"ChooseMode",mode==1);
            int required=mode==1?1:2;
            Assert.That(StartButtons().Length,Is.EqualTo(required));
            Call(flow,"BeginMatch");
            Assert.That(Screen,Is.EqualTo("Characters"),"A direct start call cannot skip player readiness.");
            if(required==2)
            {
                StartButtons()[1].onClick.Invoke();
                Assert.That(Screen,Is.EqualTo("Characters"));
                Assert.That(Field<bool[]>(flow,"ready"),Is.EqualTo(new[]{false,true}));
                Call(flow,"BeginMatch");
                Assert.That(Screen,Is.EqualTo("Characters"),"The second player's Start does not bypass the first player.");
                StartButtons()[1].onClick.Invoke();
                Assert.That(Field<bool[]>(flow,"ready").All(r=>!r),Is.True,"A player can withdraw readiness while waiting.");
                StartButtons()[0].onClick.Invoke();
                Assert.That(Screen,Is.EqualTo("Characters"));
                StartButtons()[1].onClick.Invoke();
            }
            else StartButtons()[0].onClick.Invoke();
            Assert.That(Screen,Is.EqualTo("Playing"));
            Assert.That(Field<GameObject>(flow,"menuCanvas").activeSelf,Is.False);
            Assert.That(Get(master,"StageIndex"),Is.EqualTo(mode==0?0:2));
            Assert.That(Get(master,"IsPaused"),Is.False);
            Assert.That(Get(phone,"OverlayVisible"),Is.False);
            Call(flow,"ReturnToCharacters");
            Assert.That(Field<bool[]>(flow,"ready").All(r=>!r),Is.True);
            Assert.That(StartButtons().Length,Is.EqualTo(required));
        }
        yield return null;

        Button[] StartButtons()=>Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>()
            .Where(b=>b.GetComponentInChildren<Text>().text=="スタート")
            .OrderBy(b=>b.GetComponent<RectTransform>().anchoredPosition.x).ToArray();
    }

    [UnityTest]
    public IEnumerator DuelAndFreeDisplayOptionsExistOnlyInPauseAndPersistIntoPlay()
    {
        foreach(bool free in new[]{false,true})
        {
            if(free)Call(flow,"ChooseFreeMode");else Call(flow,"ChooseMode",false);
            foreach(string screen in new[]{"Characters","Playing"})
            {
                Assert.That(Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>().Any(IsDisplayButton),Is.False);
                int before=(int)Get(master,"DisplayMode");
                Call(flow,"ChooseDisplayMode",before==1?2:1);
                Assert.That(Get(master,"DisplayMode"),Is.EqualTo(before),"Display changes are restricted to pause.");
                Call(flow,"TogglePause");
                Button[] buttons=Field<GameObject>(flow,"pauseCanvas").GetComponentsInChildren<Button>().Where(IsDisplayButton).ToArray();
                Assert.That(buttons.Length,Is.EqualTo(2));
                buttons.Single(b=>b.GetComponentInChildren<Text>().text.StartsWith("1画面")).onClick.Invoke();
                Assert.That(Get(master,"DisplayMode"),Is.EqualTo(2));
                buttons.Single(b=>b.GetComponentInChildren<Text>().text.StartsWith("画面分割")).onClick.Invoke();
                Assert.That(Get(master,"DisplayMode"),Is.EqualTo(1));
                Call(flow,"TogglePause");
                if(screen=="Characters"){Call(flow,"Ready",0);Call(flow,"Ready",1);}
            }
            Call(flow,"ReturnToTitle");
        }
        Call(flow,"ChooseMode",true);Call(flow,"TogglePause");
        Assert.That(Field<GameObject>(flow,"pauseCanvas").GetComponentsInChildren<Button>().Any(IsDisplayButton),Is.False);
        yield return null;
    }
    private static bool IsDisplayButton(Button button)
    {
        string label=button.GetComponentInChildren<Text>().text;
        return label.StartsWith("画面分割")||label.StartsWith("1画面");
    }

    [UnityTest]
    public IEnumerator BothControllersCanFocusAndConfirmBackAcrossSelectionScreens()
    {
        foreach(int controller in new[]{0,1})
        {
            Call(flow,"ReturnToTitle");Call(master,"SetSingleDifficulty",false);
            Call(flow,"ChooseSingleMode");Call(flow,"Navigate",controller,Vector2Int.down);
            AssertMenuFocus("もどる");Call(flow,"Confirm",controller);
            Assert.That(Screen,Is.EqualTo("Title"));
            foreach(int mode in new[]{0,1,2})
            {
                if(mode==0){Call(flow,"ChooseSingleMode");Call(flow,"ChooseDifficulty",false);}
                else if(mode==1)Call(flow,"ChooseMode",false);else Call(flow,"ChooseFreeMode");
                int player=mode==0?0:controller;
                int[] characters=(int[])Field<int[]>(flow,"focusedCharacter").Clone();
                int[] colors=(int[])Field<int[]>(flow,"focusedColor").Clone();
                for(int step=0;step<3;step++)Call(flow,"Navigate",controller,Vector2Int.down);
                Call(flow,"Navigate",controller,Vector2Int.left);AssertMenuFocus("もどる");
                Assert.That(Field<int[]>(flow,"focusedCharacter"),Is.EqualTo(characters));
                Assert.That(Field<int[]>(flow,"focusedColor"),Is.EqualTo(colors));
                if(mode!=0)Assert.That(Field<int[]>(flow,"row")[1-player],Is.Zero,"The other player's cursor stays independent.");
                Call(flow,"Navigate",controller,Vector2Int.right);
                Assert.That(Field<bool[]>(flow,"focusedBack")[player],Is.False);
                Call(flow,"Confirm",controller);Assert.That(Screen,Is.EqualTo("Preview"));
                Call(flow,"Confirm",controller);Assert.That(Screen,Is.EqualTo("Preview"),"Guard can be tested before selecting Back.");
                Call(flow,"Navigate",controller,Vector2Int.down);AssertMenuFocus("もどる");
                Call(flow,"Navigate",controller,Vector2Int.up);
                Call(flow,"Confirm",controller);Assert.That(Screen,Is.EqualTo("Preview"));
                Call(flow,"Navigate",controller,Vector2Int.down);Call(flow,"Confirm",controller);
                Assert.That(Screen,Is.EqualTo("Characters"));
                Call(flow,"Navigate",controller,Vector2Int.left);AssertMenuFocus("もどる");
                Call(flow,"Confirm",controller);Assert.That(Screen,Is.EqualTo(mode==0?"Difficulty":"Title"));

                if(mode==0)Call(flow,"ChooseMode",true);
                else if(mode==1)Call(flow,"ChooseMode",false);else Call(flow,"ChooseFreeMode");
                Call(flow,"Ready",0);if(mode!=0)Call(flow,"Ready",1);
                Assert.That(Screen,Is.EqualTo("Playing"));
                Call(flow,"ReturnToCharacters");Assert.That(Screen,Is.EqualTo("Characters"));
                Assert.That(Field<bool[]>(flow,"ready").All(r=>!r),Is.True);
                yield return null;
            }
        }
    }

    private void AssertMenuFocus(string label)
    {
        Button button=Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>()
            .Single(b=>b.GetComponentInChildren<Text>().text==label);
        Assert.That(button.image.GetComponents<Outline>().Any(o=>o.enabled&&o.effectColor==new Color(.08f,.57f,.86f)),Is.True,
            label+" must have a visible focus outline.");
    }

    [UnityTest]
    public IEnumerator PauseResumeHasVisibleFocusAndIsReachableFromEveryPauseButton()
    {
        foreach(bool single in new[]{true,false})
        {
            Call(flow,"ChooseMode",single);Call(flow,"TogglePause");
            Button resume=Field<Button>(flow,"pauseResumeButton");
            Button[] buttons=Field<GameObject>(flow,"pauseCanvas").GetComponentsInChildren<Button>();
            foreach(Button start in buttons)
            {
                var pending=new System.Collections.Generic.Queue<Button>();
                var visited=new System.Collections.Generic.HashSet<Button>();
                pending.Enqueue(start);visited.Add(start);
                while(pending.Count>0&&!visited.Contains(resume))
                {
                    Button current=pending.Dequeue();
                    foreach(Vector2Int direction in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right})
                    {
                        current.Select();Call(flow,"NavigatePause",direction);
                        Button next=EventSystem.current.currentSelectedGameObject.GetComponent<Button>();
                        if(visited.Add(next))pending.Enqueue(next);
                    }
                }
                Assert.That(visited.Contains(resume),Is.True,"Cannot reach Resume from "+start.name);
            }
            buttons.Single(b=>b.name=="タイトルへ").Select();Call(flow,"NavigatePause",Vector2Int.down);
            Assert.That(EventSystem.current.currentSelectedGameObject,Is.EqualTo(resume.gameObject));
            Assert.That(resume.image.GetComponents<Outline>().Any(o=>o.enabled&&o.effectColor==new Color(.08f,.57f,.86f)),Is.True);
            Call(flow,"ConfirmPause");Assert.That(Get(flow,"PauseOpen"),Is.False);
            Assert.That(Screen,Is.EqualTo("Characters"));yield return null;
        }
    }

    [UnityTest]
    public IEnumerator PauseConnectionTextClearsOldOutlineCopiesAndKeepsPlainSourceLabels()
    {
        Text[] labels=Field<Text[]>(flow,"controllerStatus");
        foreach(Text label in labels)
        {
            Assert.That(label.GetComponents<Shadow>(),Is.Empty,"New labels must not duplicate glyphs.");
            Outline old=label.gameObject.AddComponent<Outline>();old.effectDistance=new Vector2(3,-3);
        }
        Call(flow,"TogglePause");
        foreach(Text label in labels)
        {
            Assert.That(label.GetComponents<Shadow>().All(effect=>!effect.enabled),Is.True,
                "An already-created pause must stop drawing the old outline immediately.");
            Assert.That(label.text,Does.Not.Contain("○").And.Not.Contain("●"));
            Assert.That(label.text,Does.Contain("未接続"));
        }
        yield return null;
        foreach(Text label in labels)Assert.That(label.GetComponents<Shadow>(),Is.Empty);
    }

    [UnityTest]
    public IEnumerator PauseContainsOnlyBasicActionsAndDisplayChoicesWithNonInteractiveQrCards()
    {
        foreach(int mode in new[]{0,1,2})
        {
            if(mode==2)Call(flow,"ChooseFreeMode");else Call(flow,"ChooseMode",mode==1);
            Call(flow,"TogglePause");
            Button[] buttons=Field<GameObject>(flow,"pauseCanvas").GetComponentsInChildren<Button>();
            Assert.That(buttons.Where(b=>!IsDisplayButton(b)).Select(b=>b.GetComponentInChildren<Text>().text),
                Is.EquivalentTo(new[]{"タイトルへ","ゲームに戻る","やり直す"}));
            Assert.That(buttons.Count(IsDisplayButton),Is.EqualTo(mode==1?0:2));
            GameObject overlay=Field<GameObject>(phone,"overlay");
            Assert.That(overlay.GetComponentsInChildren<Button>(true),Is.Empty,"QR cards cannot steal pause focus.");
            Assert.That(overlay.GetComponentsInChildren<Text>(true),Is.Empty,"Connection and guard text are not duplicated on QR cards.");
            RawImage[] qr=overlay.GetComponentsInChildren<RawImage>();
            Assert.That(qr.Length,Is.EqualTo(2));
            Assert.That(qr.All(image=>!image.raycastTarget),Is.True);
            UnityEngine.Canvas.ForceUpdateCanvases();
            foreach(RawImage image in qr)foreach(Button button in buttons)
                Assert.That(ScreenBounds(image.rectTransform).Overlaps(ScreenBounds((RectTransform)button.transform)),
                    Is.False,"The QR must not cover "+button.name);
            foreach(Text status in Field<Text[]>(flow,"controllerStatus"))
                Assert.That(status.text,Does.Contain("未接続"));
            Call(flow,"TogglePause");
        }
        Call(flow,"ChooseMode",false);Call(flow,"Ready",0);Call(flow,"Ready",1);Call(flow,"BeginMatch");
        Call(flow,"TogglePause");
        object calibration=Field<object>(flow,"axisCalibration");
        Call(calibration,"Update");
        Button entry=(Button)calibration.GetType().GetProperty("EntryButton").GetValue(calibration);
        Assert.That(entry.gameObject.activeInHierarchy,Is.False,"The gameplay pause has exactly three actions plus display options.");
        Call(flow,"TogglePause");
        yield return null;

        Rect ScreenBounds(RectTransform rect)
        {
            Vector3[] corners=new Vector3[4];rect.GetWorldCorners(corners);
            Vector2 lower=RectTransformUtility.WorldToScreenPoint(null,corners[0]);
            Vector2 upper=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
            return Rect.MinMaxRect(lower.x,lower.y,upper.x,upper.y);
        }
    }

    [UnityTest]
    public IEnumerator PauseRestartResetsTheMatchAndResumesEveryModeWithoutChangingSelectionOrConnection()
    {
        master.enabled=false;
        foreach(int mode in new[]{0,1,2})
        {
            if(mode==2)Call(flow,"ChooseFreeMode");else Call(flow,"ChooseMode",mode==1);
            Call(flow,"Ready",0);if(mode!=1)Call(flow,"Ready",1);
            GameObject player=Field<GameObject>(master,"PlayerObject1");
            Vector3 start=player.transform.position;
            int character=(int)master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{0});
            int color=(int)master.GetType().GetMethod("ColorIndex").Invoke(master,new object[]{0});
            object stage=Get(master,"StageIndex"),display=Get(master,"DisplayMode");
            int phoneId=phone.GetInstanceID();
            ShowdownProgress showdown=Field<ShowdownProgress>(master,"showdown");
            showdown.TrySpawn();showdown.Defeat();showdown.Damage((double)Get(master,"MatchTime"));
            DuelSeries series=Field<DuelSeries>(master,"series");
            double now=(double)Get(master,"MatchTime");
            series.Tick(now+3.3);series.Tick(now+4.7);series.Tick(now+6);
            Assert.That(series.EndRound(MatchWinner.Player1,now+6.1),Is.True);
            player.transform.position+=new Vector3(4f,0f,3f);
            Call(flow,"TogglePause");
            Button restart=Field<GameObject>(flow,"pauseCanvas").GetComponentsInChildren<Button>().Single(b=>b.name=="やり直す");
            restart.Select();Call(flow,"ConfirmPause");
            Assert.That(Screen,Is.EqualTo("Playing"));Assert.That(Get(flow,"PauseOpen"),Is.False);
            Assert.That(Get(master,"IsPaused"),Is.False);Assert.That(Get(phone,"OverlayVisible"),Is.False);
            Assert.That(player.transform.position,Is.EqualTo(start));
            Assert.That(series.FirstScore,Is.Zero);Assert.That(series.SecondScore,Is.Zero);
            Assert.That(series.RoundNumber,Is.EqualTo(1));Assert.That(series.Phase,Is.EqualTo(RoundPhase.Versus));
            Assert.That(showdown.Hearts,Is.EqualTo(ShowdownProgress.MaxHearts));Assert.That(showdown.Defeated,Is.Zero);
            Assert.That(Get(master,"StageIndex"),Is.EqualTo(stage));Assert.That(Get(master,"DisplayMode"),Is.EqualTo(display));
            Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{0}),Is.EqualTo(character));
            Assert.That(master.GetType().GetMethod("ColorIndex").Invoke(master,new object[]{0}),Is.EqualTo(color));
            Assert.That(phone.GetInstanceID(),Is.EqualTo(phoneId));
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator PhoneAndUsbShortResetRecentersBothPlayersOnEveryScreenIncludingPauseAndResults()
    {
        MonoBehaviour hardwareInput=InactiveHub("HardwareControllerHub"),phoneInput=InactiveHub("PhoneControllerHub");
        Array sensors=Field<Array>(flow,"motion");
        foreach(MonoBehaviour sensor in sensors){SetField(sensor,"hardware",hardwareInput);SetField(sensor,"hub",phoneInput);}
        flow.enabled=false;uint sequence=0;ushort resetCounter=0;
        try
        {
            foreach(bool usePhone in new[]{true,false})for(int slot=0;slot<2;slot++)
            {
                foreach(object state in Field<Array>(phoneInput,"slots")){SetField(state,"ChannelOpen",false);SetField(state,"HasSample",false);}
                Array.Clear(Field<bool[]>(hardwareInput,"valid"),0,2);
                foreach(MonoBehaviour sensor in sensors)Call(sensor,"Update");
                Call(flow,"Update");
                MonoBehaviour activeSensor=(MonoBehaviour)sensors.GetValue(slot);
                MonoBehaviour hub=usePhone?phoneInput:hardwareInput;
                object controllerSlot=Enum.ToObject(Type.GetType("ControllerSlot, Assembly-CSharp",true),slot);
                int Version()=>(int)hub.GetType().GetMethod("GetRecenterVersion").Invoke(hub,new[]{controllerSlot});
                void Send(bool held)=>SupplyMenuMotion(hardwareInput,phoneInput,usePhone,slot,Quaternion.identity,0f,
                    false,true,++sequence,activeSensor,Vector3.zero,resetCounter,held);
                foreach(string context in new[]{"Title","Difficulty","Characters","Preview","Playing","Result"})
                foreach(bool paused in new[]{false,true})
                {
                    Call(flow,"ReturnToTitle");
                    if(context=="Difficulty")Call(flow,"ChooseSingleMode");
                    else if(context!="Title")
                    {
                        Call(flow,"ChooseMode",true);
                        if(context=="Preview")Call(flow,"OpenCharacterPreview",slot);
                        else if(context!="Characters")
                        {
                            Call(flow,"Ready",0);
                            if(context=="Result")Call(Find("DuelPresentation"),"ShowSingleGameOver",true);
                        }
                    }
                    if(paused)Call(flow,"TogglePause");
                    Send(false);int version=Version();string screen=Screen;
                    int[] rows=(int[])Field<int[]>(flow,"row").Clone();
                    Send(true);Assert.That(Version(),Is.EqualTo(version),"Pressing alone does not recenter.");
                    resetCounter++;Send(false);
                    Assert.That(Version(),Is.EqualTo(version+1),$"{usePhone} P{slot+1} {context} pause={paused}");
                    Assert.That(Screen,Is.EqualTo(screen),"Reset must keep the current screen.");
                    Assert.That(Get(flow,"PauseOpen"),Is.EqualTo(paused));
                    Assert.That(Field<int[]>(flow,"row"),Is.EqualTo(rows));
                    Send(false);Assert.That(Version(),Is.EqualTo(version+1),"The same reset counter must not run twice.");
                    yield return null;
                }
            }
        }
        finally {Object.Destroy(phoneInput.gameObject);Object.Destroy(hardwareInput.gameObject);}
    }

    [UnityTest]
    public IEnumerator PhoneAndUsbHalfSecondResetTogglePauseOnceAcrossMenusPlayAndResults()
    {
        MonoBehaviour hardwareInput=InactiveHub("HardwareControllerHub"),phoneInput=InactiveHub("PhoneControllerHub");
        Array sensors=Field<Array>(flow,"motion");
        foreach(MonoBehaviour sensor in sensors){SetField(sensor,"hardware",hardwareInput);SetField(sensor,"hub",phoneInput);}
        flow.enabled=false;uint sequence=0;ushort resetCounter=0;
        try
        {
            foreach(bool usePhone in new[]{true,false})for(int slot=0;slot<2;slot++)
            {
                foreach(object state in Field<Array>(phoneInput,"slots")){SetField(state,"ChannelOpen",false);SetField(state,"HasSample",false);}
                Array.Clear(Field<bool[]>(hardwareInput,"valid"),0,2);
                foreach(MonoBehaviour sensor in sensors)Call(sensor,"Update");
                Call(flow,"Update");
                MonoBehaviour activeSensor=(MonoBehaviour)sensors.GetValue(slot);
                MonoBehaviour hub=usePhone?phoneInput:hardwareInput;
                object controllerSlot=Enum.ToObject(Type.GetType("ControllerSlot, Assembly-CSharp",true),slot);
                int Version()=>(int)hub.GetType().GetMethod("GetRecenterVersion").Invoke(hub,new[]{controllerSlot});
                void Send(bool held)=>SupplyMenuMotion(hardwareInput,phoneInput,usePhone,slot,Quaternion.identity,0f,
                    false,true,++sequence,activeSensor,Vector3.zero,resetCounter,held);
                foreach(string context in new[]{"Title","Difficulty","Characters","Playing","Result"})
                foreach(bool paused in new[]{false,true})
                {
                    Call(flow,"ReturnToTitle");
                    if(context=="Difficulty")Call(flow,"ChooseSingleMode");
                    else if(context!="Title")
                    {
                        Call(flow,"ChooseMode",true);
                        if(context!="Characters")
                        {
                            Call(flow,"Ready",0);
                            if(context=="Result")Call(Find("DuelPresentation"),"ShowSingleGameOver",true);
                        }
                    }
                    if(paused)Call(flow,"TogglePause");
                    Send(false);int version=Version();string screen=Screen;
                    Send(true);
                    object reset=Field<Array>(flow,"resetButtons").GetValue(slot);
                    SetField(reset,"heldSince",Time.unscaledTime-.49f);Send(true);
                    Assert.That(Get(flow,"PauseOpen"),Is.EqualTo(paused),"A shorter press has not opened pause.");
                    SetField(reset,"heldSince",Time.unscaledTime-.51f);Send(true);
                    Assert.That(Get(flow,"PauseOpen"),Is.EqualTo(!paused),$"{usePhone} P{slot+1} {context}");
                    Assert.That(Screen,Is.EqualTo(screen));
                    Send(true);Assert.That(Get(flow,"PauseOpen"),Is.EqualTo(!paused),"Continued holding must not toggle again.");
                    resetCounter++;Send(false);
                    Assert.That(Version(),Is.EqualTo(version),"A long release must not also recenter, including an older sender's counter.");
                    Assert.That(Get(flow,"PauseOpen"),Is.EqualTo(!paused));
                    yield return null;
                }
            }
        }
        finally {Object.Destroy(phoneInput.gameObject);Object.Destroy(hardwareInput.gameObject);}
    }

    [UnityTest]
    public IEnumerator PhoneAndUsbRecenterBlendFromTheCurrentSwordWithoutAnAttackOrSingleControllerHandoff()
    {
        MonoBehaviour hardwareInput = InactiveHub("HardwareControllerHub"), phoneInput = InactiveHub("PhoneControllerHub");
        Array sensors = Field<Array>(flow, "motion");
        foreach (MonoBehaviour sensor in sensors) { SetField(sensor, "hardware", hardwareInput); SetField(sensor, "hub", phoneInput); }
        master.enabled = flow.enabled = false;
        uint sequence = 0;
        try
        {
            foreach (bool single in new[] { false, true })
            foreach (bool usePhone in new[] { false, true })
            for (int slot = 0; slot < 2; slot++)
            foreach (bool guarding in new[] { false, true })
            {
                foreach (object state in Field<Array>(phoneInput, "slots")) { SetField(state, "ChannelOpen", false); SetField(state, "HasSample", false); }
                Array.Clear(Field<bool[]>(hardwareInput, "valid"), 0, 2);
                foreach (MonoBehaviour sensor in sensors) Call(sensor, "Update");
                Call(flow, "ReturnToTitle");
                if (single) Call(flow, "ChooseMode", true); else Call(flow, "ChooseFreeMode");
                Call(flow, "Ready", 0); if (!single) Call(flow, "Ready", 1);
                MonoBehaviour active = (MonoBehaviour)sensors.GetValue(slot);
                MonoBehaviour other = (MonoBehaviour)sensors.GetValue(1 - slot);
                SupplyMenuMotion(hardwareInput, phoneInput, usePhone, 1 - slot, Quaternion.identity, 0f, false, true, ++sequence, other);
                SupplyMenuMotion(hardwareInput, phoneInput, usePhone, slot, Quaternion.identity, 0f, guarding, true, ++sequence, active);
                if (single) SetField(master, "singleMotion", active);
                object player = Field<object>(master, single || slot == 0 ? "first" : "second");
                bool isFirst = single || slot == 0;
                SwordPose Pose() => (SwordPose)player.GetType().GetField("TargetPose").GetValue(player);
                CombatInput Read(double now)
                {
                    SetField(master, "matchTime", now);
                    return (CombatInput)master.GetType().GetMethod("ReadInput", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(master, new[] { player, (object)isFirst, true });
                }
                Read(10);
                SupplyMenuMotion(hardwareInput, phoneInput, usePhone, slot, Quaternion.Euler(45f, 60f, 25f), 0f, guarding, true, ++sequence, active);
                Read(10.3);
                SwordPose before = Pose();
                int generation = (int)player.GetType().GetField("InputGeneration").GetValue(player);
                Call(active, "Recenter");
                Assert.That(Get(active, "IsPoseValid"), Is.False);
                CombatInput invalid = Read(10.3);
                Assert.That(invalid.Valid, Is.False);
                Assert.That(Vector3.Distance(Pose().Position, before.Position), Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(Pose().Rotation, before.Rotation), Is.LessThan(.05f));
                if (single) Assert.That(Field<object>(master, "singleMotion"), Is.SameAs(active), "Reset must keep the selected hand while its reference is refreshed.");
                Call(active, "Update");
                CombatInput centered = Read(10.3);
                Assert.That(centered.Valid, Is.True);
                Assert.That(centered.PoseGeneration, Is.GreaterThan(generation));
                Assert.That(centered.AngularSpeed, Is.Zero);
                Assert.That(centered.ThrustRequested, Is.False);
                Assert.That(Vector3.Distance(Pose().Position, before.Position), Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(Pose().Rotation, before.Rotation), Is.LessThan(.05f));
                Read(10.36);
                SwordPose halfway = Pose();
                Read(10.43);
                SwordPose after = Pose();
                Assert.That(Quaternion.Angle(before.Rotation, after.Rotation), Is.GreaterThan(10f));
                Assert.That(Quaternion.Angle(halfway.Rotation, before.Rotation), Is.GreaterThan(1f));
                Assert.That(Quaternion.Angle(halfway.Rotation, after.Rotation), Is.GreaterThan(1f));
                Read(10.5);
                Assert.That(Vector3.Distance(Pose().Position, after.Position), Is.LessThan(.00001f));
                Assert.That(Quaternion.Angle(Pose().Rotation, after.Rotation), Is.LessThan(.05f));
                Assert.That((Vector3)player.GetType().GetField("CutVelocity").GetValue(player), Is.EqualTo(Vector3.zero));
                yield return null;
            }
        }
        finally { Object.Destroy(phoneInput.gameObject); Object.Destroy(hardwareInput.gameObject); }
    }

    [UnityTest]
    public IEnumerator PhoneAndUsbLongResetReturnFromPreviewWithoutPausingOrRecentering()
    {
        MonoBehaviour hardwareInput=InactiveHub("HardwareControllerHub"),phoneInput=InactiveHub("PhoneControllerHub");
        Array sensors=Field<Array>(flow,"motion");
        foreach(MonoBehaviour sensor in sensors){SetField(sensor,"hardware",hardwareInput);SetField(sensor,"hub",phoneInput);}
        flow.enabled=false;uint sequence=0;ushort resetCounter=0;
        try
        {
            foreach(bool usePhone in new[]{true,false})for(int slot=0;slot<2;slot++)
            {
                foreach(object state in Field<Array>(phoneInput,"slots")){SetField(state,"ChannelOpen",false);SetField(state,"HasSample",false);}
                Array.Clear(Field<bool[]>(hardwareInput,"valid"),0,2);
                foreach(MonoBehaviour motionSensor in sensors)Call(motionSensor,"Update");
                Call(flow,"Update");Call(flow,"ReturnToTitle");Call(flow,"ChooseFreeMode");Call(flow,"OpenCharacterPreview",slot);
                MonoBehaviour sensor=(MonoBehaviour)sensors.GetValue(slot);
                MonoBehaviour hub=usePhone?phoneInput:hardwareInput;
                object controllerSlot=Enum.ToObject(Type.GetType("ControllerSlot, Assembly-CSharp",true),slot);
                int Version()=>(int)hub.GetType().GetMethod("GetRecenterVersion").Invoke(hub,new[]{controllerSlot});
                void Send(bool held)=>SupplyMenuMotion(hardwareInput,phoneInput,usePhone,slot,Quaternion.identity,0f,
                    false,true,++sequence,sensor,Vector3.zero,resetCounter,held);
                Send(false);int version=Version();int[] rows=(int[])Field<int[]>(flow,"row").Clone();
                Send(true);Assert.That(Screen,Is.EqualTo("Preview"));
                object reset=Field<Array>(flow,"resetButtons").GetValue(slot);
                SetField(reset,"heldSince",Time.unscaledTime-.51f);
                Send(true);
                Assert.That(Screen,Is.EqualTo("Characters"));
                Assert.That(Get(master,"IsCharacterPreview"),Is.False);
                Assert.That(Get(flow,"PauseOpen"),Is.False);
                Assert.That(Field<int[]>(flow,"row"),Is.EqualTo(rows));
                Send(true);Assert.That(Screen,Is.EqualTo("Characters"),"Continued holding must not open pause after returning.");
                resetCounter++;Send(false);
                Assert.That(Version(),Is.EqualTo(version),"Long-press release must not also recenter.");
                yield return null;
            }
        }
        finally {Object.Destroy(phoneInput.gameObject);Object.Destroy(hardwareInput.gameObject);}
    }

    [UnityTest]
    public IEnumerator FreeMovementPassesTheOldBoundaryAndStopsAtTheImportedStadiumFloor()
    {
        Call(flow,"ChooseFreeMode");Call(flow,"Ready",0);Call(flow,"Ready",1);
        master.enabled=false;
        MonoBehaviour arena=Find("DuelArena");
        Bounds floor=Field<GameObject>(arena,"environment").GetComponentsInChildren<Renderer>()
            .Single(r=>r.name=="Infield_concrete_foundation").bounds;
        MethodInfo fraction=master.GetType().GetMethod("FieldMovementFraction",BindingFlags.NonPublic|BindingFlags.Instance);
        Assert.That(Field<float>(master,"fieldRadius"),Is.EqualTo(Mathf.Min(floor.extents.x,floor.extents.z)).Within(.001f));
        foreach(float angle in new[]{0f,45f,90f,180f,270f})
        {
            Vector3 direction=Quaternion.Euler(0f,angle,0f)*Vector3.forward;
            Vector3 center=new Vector3(floor.center.x,0f,floor.center.z);
            Assert.That((float)fraction.Invoke(master,new object[]{center+direction*31f,direction*20f}),Is.EqualTo(1f));
            Vector3 start=center+direction*82f;
            float allowed=(float)fraction.Invoke(master,new object[]{start,direction*4f});
            Assert.That(allowed,Is.InRange(.2f,.3f));
            Assert.That(Vector3.Distance(start+direction*4f*allowed,center),Is.EqualTo(82.99f).Within(.002f));
        }
        master.enabled=true;
        yield return null;
    }

    [UnityTest]
    public IEnumerator SingleAndFreeUseTheStadiumWhileDuelUsesTheImportedPlatform()
    {
        foreach(int mode in new[]{0,1,2})
        {
            bool single=mode!=0;
            Call(flow,"ChooseFreeMode");
            Assert.That(Get(master,"StageIndex"),Is.EqualTo(2),"The stadium is already selected for character preview.");
            MonoBehaviour arena=Find("DuelArena");
            GameObject stadium=Field<GameObject>(master,"circularStadiumStage");
            GameObject environment=Field<GameObject>(arena,"environment");
            Assert.That(stadium,Is.Not.Null);
            Assert.That(environment,Is.Not.Null);
            Mesh[] stadiumMeshes=stadium.GetComponentsInChildren<MeshFilter>(true).Select(m=>m.sharedMesh).ToArray();
            Assert.That(stadiumMeshes.Length,Is.GreaterThan(0));
            Assert.That(environment.GetComponentsInChildren<MeshFilter>(true).Select(m=>m.sharedMesh),
                Is.EquivalentTo(stadiumMeshes),"Free mode uses the supplied circular stadium model.");
            GameObject menu=Field<GameObject>(flow,"menuCanvas");
            Assert.That(menu.GetComponentsInChildren<Button>().Count(b=>b.GetComponentInChildren<Text>().text=="スタート"),Is.EqualTo(2));
            Call(flow,"Ready",0);Assert.That(Screen,Is.EqualTo("Characters"));
            Call(flow,"Ready",1);Assert.That(Screen,Is.EqualTo("Playing"));
            Assert.That(menu.activeSelf,Is.False);Assert.That(Get(master,"StageIndex"),Is.EqualTo(2));
            Call(flow,"ReturnToTitle");
            if(single){Call(flow,"ChooseSingleMode");Call(flow,"ChooseDifficulty",mode==1);}
            else Call(flow,"ChooseMode",false);
            int expectedStage=single?2:0;
            Assert.That(Get(master,"StageIndex"),Is.EqualTo(expectedStage),"Single uses the stadium; duel restores the supplied platform.");
            Call(flow,"Ready",0);if(!single)Call(flow,"Ready",1);
            Assert.That(Screen,Is.EqualTo("Playing"));Assert.That(Get(master,"StageIndex"),Is.EqualTo(expectedStage));
            Assert.That(Field<GameObject>(arena,"platform").activeSelf,Is.False,"An imported stage replaces the generic platform.");
            Assert.That(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Count(renderer=>renderer.name=="Platform_floor" && renderer.enabled && renderer.gameObject.activeInHierarchy),
                Is.EqualTo(single?0:1),"The duel floor appears only in duel mode.");
            if(single)
                Assert.That(Field<GameObject>(arena,"environment").GetComponentsInChildren<MeshFilter>(true).Select(m=>m.sharedMesh),
                    Is.EquivalentTo(stadiumMeshes),"Both single difficulties use the same supplied circular stadium model.");
            if(!single)
            {
                Renderer[] parts=Field<GameObject>(arena,"environment").GetComponentsInChildren<Renderer>();
                Renderer floor=parts.Single(renderer=>renderer.name=="Platform_floor");
                Renderer rim=parts.Single(renderer=>renderer.name=="Pale_red_rim");
                Assert.That(floor.bounds.max.y,Is.EqualTo(0f).Within(0.001f));
                Assert.That(rim.bounds.extents.x,Is.EqualTo(Field<float>(master,"arenaRadius")).Within(0.001f));
            }
            Call(flow,"ReturnToTitle");
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator FieldPreviewUsesEachSelectedPlayerAndPreservesSelectionWhenReturning()
    {
        GameObject[] players=Field<GameObject[]>(flow,"players"),swords=Field<GameObject[]>(flow,"swords");
        foreach(int mode in new[]{0,1,2})
        {
            if(mode==2)Call(flow,"ChooseFreeMode");else Call(flow,"ChooseMode",mode==0);
            int count=mode==0?1:2;
            Button[] buttons=Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>().Where(b=>b.GetComponentInChildren<Text>().text=="プレビュー").ToArray();
            Assert.That(buttons.Length,Is.EqualTo(count));
            for(int player=0;player<count;player++)
            {
                Call(master,"SetCharacter",player,player+1);Call(flow,"ChooseMode",mode==0);
                if(mode==2)Call(flow,"ChooseFreeMode");
                if(count==2)Call(flow,"Ready",1-player);
                // Leave one uncommitted choice to prove preview never commits focus.
                Call(flow,"Navigate",player,Vector2Int.right);
                int[] focus=(int[])Field<int[]>(flow,"focusedCharacter").Clone();
                int[] colors=(int[])Field<int[]>(flow,"focusedColor").Clone();
                int[] rows=(int[])Field<int[]>(flow,"row").Clone();
                bool[] ready=(bool[])Field<bool[]>(flow,"ready").Clone();
                string[] buttonLabels=Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>().Select(b=>b.GetComponentInChildren<Text>().text).ToArray();
                Color[] outlineColors=Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Outline>().Select(o=>o.effectColor).ToArray();
                int character=(int)master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{player});
                Camera[] cameras=Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(c=>c.targetTexture==null&&c.gameObject.activeInHierarchy).ToArray();
                bool[] cameraEnabled=cameras.Select(c=>c.enabled).ToArray();
                double clock=Field<double>(master,"matchTime");
                buttons=Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>().Where(b=>b.GetComponentInChildren<Text>().text=="プレビュー").ToArray();
                RectTransform buttonRect=buttons[player].GetComponent<RectTransform>();
                Assert.That(buttonRect.anchorMin,Is.EqualTo(new Vector2(count==1||player==1?1f:.5f,0f)));
                buttons[player].onClick.Invoke();
                Assert.That(Screen,Is.EqualTo("Preview"));Assert.That(Get(master,"IsCharacterPreview"),Is.True);
                Assert.That(Get(master,"PreviewPlayer"),Is.EqualTo(player));Assert.That(Get(flow,"IsPlaying"),Is.False);
                Camera frontCamera=(Camera)Get(master,"CharacterPreviewCamera");
                Assert.That(Vector3.Dot((frontCamera.transform.position-players[player].transform.position).normalized,players[player].transform.forward),Is.GreaterThan(.8f));
                foreach(string bone in new[]{"head","ankle_l","ankle_r"})
                {
                    Transform joint=players[player].GetComponentsInChildren<Transform>().First(t=>t.name==bone);
                    Vector3 viewport=frontCamera.WorldToViewportPoint(joint.position);
                    Assert.That(viewport.x,Is.InRange(.01f,.99f),bone+" must fit horizontally.");
                    Assert.That(viewport.y,Is.InRange(.01f,.99f),bone+" must fit vertically.");
                    Assert.That(viewport.z,Is.GreaterThan(frontCamera.nearClipPlane));
                }
                Assert.That(Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<RawImage>().Any(),Is.False,"The real field must be visible.");
                Assert.That(players.Concat(swords).All(p=>p.activeInHierarchy),Is.True,"Controller roots stay active.");
                Assert.That(players[player].GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled&&!r.forceRenderingOff),Is.True);
                Assert.That(players[1-player].GetComponentsInChildren<Renderer>(true).All(r=>!r.enabled||r.forceRenderingOff),Is.True);
                yield return new WaitForSecondsRealtime(.25f);
                Assert.That(Field<double>(master,"matchTime"),Is.EqualTo(clock),"Preview does not advance the match clock.");
                Quaternion before=swords[player].transform.localRotation;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(player==0?Key.A:Key.LeftArrow));
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(Quaternion.Angle(before,swords[player].transform.localRotation),Is.GreaterThan(1f),"The runtime sword receives keyboard input in preview.");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Vector3 unguarded=swords[player].transform.localPosition;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(player==0?Key.LeftShift:Key.RightShift));
                yield return new WaitForSecondsRealtime(.12f);
                Assert.That(Vector3.Distance(unguarded,swords[player].transform.localPosition),Is.GreaterThan(.01f),"Guard changes the runtime sword pose without leaving preview.");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                yield return Press(Key.Escape);Assert.That(Get(flow,"PauseOpen"),Is.True);
                Assert.That(Get(phone,"OverlayVisible"),Is.True);
                Assert.That(Field<GameObject>(flow,"pauseCanvas").transform.Find("Connection controls/Menu background").GetComponent<RawImage>().enabled,Is.False);
                yield return Press(Key.Escape);Assert.That(Screen,Is.EqualTo("Preview"));
                AssertBackButton(Field<GameObject>(flow,"menuCanvas"),"もどる",60);
                Call(flow,"Back");Assert.That(Screen,Is.EqualTo("Characters"));
                Assert.That(Get(master,"IsCharacterPreview"),Is.False);
                Assert.That(Field<int[]>(flow,"focusedCharacter"),Is.EqualTo(focus));
                Assert.That(Field<int[]>(flow,"focusedColor"),Is.EqualTo(colors));
                Assert.That(Field<int[]>(flow,"row"),Is.EqualTo(rows));Assert.That(Field<bool[]>(flow,"ready"),Is.EqualTo(ready));
                Assert.That(Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>().Select(b=>b.GetComponentInChildren<Text>().text),Is.EqualTo(buttonLabels));
                Assert.That(Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Outline>().Select(o=>o.effectColor),Is.EqualTo(outlineColors),"Rebuilt buttons retain the selected and focused outlines.");
                Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{player}),Is.EqualTo(character));
                Assert.That(cameras.Select(c=>c.enabled),Is.EqualTo(cameraEnabled),"The original camera states are restored.");
            }
            // The fourth row is accessible through either controller as well.
            Call(flow,"Navigate",count-1,Vector2Int.up);Call(flow,"Confirm",count-1);
            Assert.That(Screen,Is.EqualTo("Preview"));Call(flow,"CloseCharacterPreview");
            Call(flow,"ReturnToTitle");
        }
    }

    [UnityTest]
    public IEnumerator FieldPreviewReceivesBothRemoteSlotsAndSinglePlayerUsesSlotTwo()
    {
        MonoBehaviour hardwareInput=InactiveHub("HardwareControllerHub"),phoneInput=InactiveHub("PhoneControllerHub");
        Array sensors=Field<Array>(flow,"motion");
        GameObject[] swords=Field<GameObject[]>(flow,"swords");
        foreach(MonoBehaviour sensor in sensors){SetField(sensor,"hardware",hardwareInput);SetField(sensor,"hub",phoneInput);}
        uint sequence=0;
        try
        {
            // Duel P1, duel P2, then the single character controlled by P2.
            foreach(int scenario in new[]{0,1,2})
            {
                Array.Clear(Field<bool[]>(hardwareInput,"valid"),0,2);
                foreach(MonoBehaviour sensor in sensors)Call(sensor,"Update");
                Call(flow,"Update");
                bool single=scenario==2;int slot=scenario==0?0:1,player=single?0:slot;
                Call(flow,"ChooseMode",single);Call(flow,"OpenCharacterPreview",player);
                double clock=Field<double>(master,"matchTime");
                MonoBehaviour activeSensor=(MonoBehaviour)sensors.GetValue(slot);
                SupplyMenuMotion(hardwareInput,phoneInput,false,slot,Quaternion.identity,0f,false,true,++sequence,activeSensor);
                Call(master,"LateUpdate");
                Assert.That(Get(activeSensor,"IsPoseValid"),Is.True);
                Assert.That(Get(activeSensor,"ActiveSource").ToString(),Is.EqualTo("Hardware"));
                Quaternion neutral=swords[player].transform.localRotation;
                Quaternion tilted=Quaternion.Euler(35f,40f,30f);
                SupplyMenuMotion(hardwareInput,phoneInput,false,slot,tilted,80f,false,true,++sequence,activeSensor);
                Call(master,"LateUpdate");
                Assert.That(Quaternion.Angle(neutral,swords[player].transform.localRotation),Is.GreaterThan(10f),
                    "Remote P"+(slot+1)+" moves the actual preview sword for player "+player+".");
                Vector3 unguarded=swords[player].transform.localPosition;
                SupplyMenuMotion(hardwareInput,phoneInput,false,slot,tilted,0f,true,true,++sequence,activeSensor);
                Call(master,"LateUpdate");
                Assert.That(Get(activeSensor,"GuardHeld"),Is.True);
                Assert.That(Vector3.Distance(unguarded,swords[player].transform.localPosition),Is.LessThan(.001f),
                    "Guard starts from the visible preview pose.");
                double guardStarted=Time.unscaledTimeAsDouble;
                while(Time.unscaledTimeAsDouble-guardStarted<GuardPoseTransition.DurationSeconds+.01d)
                {
                    yield return null;
                    SupplyMenuMotion(hardwareInput,phoneInput,false,slot,tilted,0f,true,true,++sequence,activeSensor);
                    Call(master,"LateUpdate");
                }
                Assert.That(Vector3.Distance(unguarded,swords[player].transform.localPosition),Is.GreaterThan(.01f),"Remote guard reaches the actual sword pose.");
                Assert.That(Screen,Is.EqualTo("Preview"),"Remote guard must not confirm menu buttons during preview.");
                Assert.That(Field<double>(master,"matchTime"),Is.EqualTo(clock));
                Call(flow,"CloseCharacterPreview");
                yield return null;
            }
        }
        finally{Object.Destroy(hardwareInput.gameObject);Object.Destroy(phoneInput.gameObject);}
    }

    [UnityTest]
    public IEnumerator ConnectionStatusShowsRemotePhoneAndNoConnectionOnlyInsidePause()
    {
        MonoBehaviour hardwareInput=InactiveHub("HardwareControllerHub"),phoneInput=InactiveHub("PhoneControllerHub");
        Array sensors=Field<Array>(flow,"motion");
        foreach(MonoBehaviour sensor in sensors){SetField(sensor,"hardware",hardwareInput);SetField(sensor,"hub",phoneInput);}
        try
        {
            Assert.That(flow.transform.Find("Controller status"),Is.Null,"No standalone connection HUD remains.");
            Call(flow,"TogglePause");
            Assert.That(Field<Text[]>(flow,"controllerStatus").All(t=>t.text.Contains("未接続")&&t.isActiveAndEnabled),Is.True);
            Call(flow,"TogglePause");
            SetField(Field<Array>(phoneInput,"slots").GetValue(1),"ChannelOpen",true);
            foreach(string screen in new[]{"Title","Characters","Playing"})
            {
                object sample=Activator.CreateInstance(Type.GetType("MotionSample, Assembly-CSharp",true),new object[]{Quaternion.identity,Vector3.zero,Vector3.zero,false,false,false,false,1u,(ushort)0,10d,Time.realtimeSinceStartupAsDouble});
                Field<Array>(hardwareInput,"latest").SetValue(sample,0);Field<bool[]>(hardwareInput,"valid")[0]=true;
                Call(flow,"Update");
                Assert.That(Field<Text[]>(flow,"controllerStatus").All(t=>!t.gameObject.activeInHierarchy),Is.True,screen);
                Call(flow,"TogglePause");
                Assert.That(Field<Text[]>(flow,"controllerStatus").Select(t=>t.text),Is.EqualTo(new[]{"P1 コントローラ","P2 スマホ"}),screen);
                Assert.That(Field<Text[]>(flow,"controllerStatus").All(t=>t.isActiveAndEnabled&&t.canvas==Field<GameObject>(flow,"pauseCanvas").GetComponent<Canvas>()),Is.True);
                Call(flow,"TogglePause");
                Assert.That(Field<Text[]>(flow,"controllerStatus").All(t=>!t.gameObject.activeInHierarchy),Is.True);
                if(screen=="Title")Call(flow,"ChooseMode",false);
                else if(screen=="Characters"){Call(flow,"Ready",0);Call(flow,"Ready",1);}
            }
        }
        finally{Object.Destroy(hardwareInput.gameObject);Object.Destroy(phoneInput.gameObject);}
        yield return null;
    }

    [UnityTest]
    public IEnumerator BackButtonsUseBottomLeftAnchorsAndCharacterPagesHaveNoPlayerLabels()
    {
        Call(flow,"ChooseSingleMode");AssertBackButton(Field<GameObject>(flow,"menuCanvas"),"もどる",60);
        Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text=="もどる").onClick.Invoke();
        Assert.That(Screen,Is.EqualTo("Title"));
        Call(flow,"ChooseMode",false);
        Assert.That(Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Text>().Any(t=>t.text.Contains("P1")||t.text.Contains("P2")),Is.False);
        AssertBackButton(Field<GameObject>(flow,"menuCanvas"),"もどる",60);
        Call(flow,"Ready",0);
        AssertBackButton(Field<GameObject>(flow,"menuCanvas"),"もどる",60);
        Call(flow,"TogglePause");
        AssertBackButton(Field<GameObject>(flow,"pauseCanvas"),"ゲームに戻る",60);
        AssertBackButton(Field<GameObject>(flow,"pauseCanvas"),"タイトルへ",140);
        AssertBackButton(Field<GameObject>(flow,"pauseCanvas"),"やり直す",220);
        Field<GameObject>(flow,"pauseCanvas").GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text=="ゲームに戻る").onClick.Invoke();
        Assert.That(Get(flow,"PauseOpen"),Is.False);Assert.That(Screen,Is.EqualTo("Characters"));
        yield return null;
    }
    private static void AssertBackButton(GameObject canvas,string label,float y)
    {
        RectTransform rect=canvas.GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text==label).GetComponent<RectTransform>();
        Assert.That(rect.anchorMin,Is.EqualTo(Vector2.zero));Assert.That(rect.anchorMax,Is.EqualTo(Vector2.zero));
        Assert.That(rect.pivot,Is.EqualTo(Vector2.zero));Assert.That(rect.anchoredPosition,Is.EqualTo(new Vector2(60,y)));
    }

    [UnityTest]
    public IEnumerator DedicatedMenusHideSceneModelsKeepPortraitsAndInputAndRestorePlay()
    {
        GameObject[] players=Field<GameObject[]>(flow,"players");
        GameObject[] swords=Field<GameObject[]>(flow,"swords");
        Array sensors=Field<Array>(flow,"motion");
        bool[] inputEnabled=sensors.Cast<MonoBehaviour>().Select(s=>s.enabled).ToArray();
        MonoBehaviour presentation=Find("DuelPresentation");
        AssertSceneModelsHidden();
        Assert.That(Field<Array>(flow,"previews").Cast<object>().Count(p=>p!=null),Is.EqualTo(2),"The title uses independent portraits.");
        Assert.That(Field<Array>(presentation,"views").Cast<object>().All(v=>!Field<bool>(v,"ownBodyFade")),Is.True,"Title bodies remain opaque.");
        foreach(int mode in new[]{0,1,2})
        {
            if(mode==2)Call(flow,"ChooseFreeMode");else Call(flow,"ChooseMode",mode==1);
            AssertSceneModelsHidden();
            Call(flow,"Navigate",0,Vector2Int.right);Call(flow,"Confirm",0); // Replace the model while hidden.
            AssertSceneModelsHidden();
            Array previews=Field<Array>(flow,"previews");
            foreach(object portrait in previews)
                if(portrait!=null)
                    Assert.That(Field<GameObject>(portrait,"root").GetComponentsInChildren<Renderer>().Any(r=>r.enabled&&!r.forceRenderingOff),Is.True,"Portrait clones remain visible after appearance changes.");
            Call(flow,"Ready",0);if(mode!=1)Call(flow,"Ready",1);
            Assert.That(Screen,Is.EqualTo("Playing"));
            Assert.That(players.SelectMany(p=>p.GetComponentsInChildren<Renderer>(true)).All(r=>!r.forceRenderingOff),Is.True);
            Call(flow,"ReturnToCharacters");AssertSceneModelsHidden();
            Call(flow,"ReturnToTitle");
            AssertSceneModelsHidden();
            Assert.That(Field<Array>(flow,"previews").Cast<object>().Where(p=>p!=null).All(p=>Field<GameObject>(p,"root").GetComponentsInChildren<Renderer>().Any(r=>r.enabled&&!r.forceRenderingOff)),Is.True,"Both title portraits are visible even after single mode hides P2.");
            Assert.That(Field<Array>(presentation,"views").Cast<object>().All(v=>!Field<bool>(v,"ownBodyFade")),Is.True);
            if(mode==2)Call(flow,"ChooseFreeMode");else Call(flow,"ChooseMode",mode==1);
            Call(flow,"Ready",0);if(mode!=1)Call(flow,"Ready",1);
            Assert.That(players.SelectMany(p=>p.GetComponentsInChildren<Renderer>(true)).All(r=>!r.forceRenderingOff),Is.True,"Gameplay restores render overrides.");
            Assert.That(swords.SelectMany(s=>s.GetComponentsInChildren<Renderer>()).Any(r=>r.enabled),Is.True);
            Assert.That(Field<Array>(presentation,"views").Cast<object>().All(v=>Field<bool>(v,"ownBodyFade")),Is.True,"Gameplay retains its own-body fade.");
            if(mode==1)Assert.That(players[1].GetComponentsInChildren<Renderer>(true).All(r=>!r.enabled),Is.True,"Single mode still hides P2 through its own settings.");
            Call(flow,"TogglePause");Call(flow,"TogglePause");
            Assert.That(Field<Array>(presentation,"views").Cast<object>().All(v=>Field<bool>(v,"ownBodyFade")),Is.True);
            Call(flow,"ReturnToTitle");
        }
        yield return null;

        void AssertSceneModelsHidden()
        {
            Assert.That(players.Concat(swords).SelectMany(p=>p.GetComponentsInChildren<Renderer>(true)).All(r=>r.forceRenderingOff),Is.True);
            Assert.That(players.Concat(swords).All(p=>p.activeInHierarchy),Is.True,"Input roots stay active.");
            Assert.That(sensors.Cast<MonoBehaviour>().Select(s=>s.enabled),Is.EqualTo(inputEnabled));
        }
    }

    [UnityTest]
    public IEnumerator ThirdTitleChoiceStartsFreeModeAndNormalChoicesClearIt()
    {
        Button[] modes = Field<GameObject>(flow,"menuCanvas").GetComponentsInChildren<Button>();
        Assert.That(modes.Length, Is.EqualTo(3));
        Assert.That(modes.Select(b=>b.GetComponentInChildren<Text>().text), Does.Contain("フリーモード"));
        Call(flow,"Navigate",1,Vector2Int.up);
        Assert.That(Field<int>(flow,"titleChoice"), Is.EqualTo(2), "Either controller can wrap to free mode.");
        Call(flow,"Confirm",1);
        Assert.That(Screen, Is.EqualTo("Characters"));
        Assert.That(Get(master,"IsFreeMode"), Is.True);
        Assert.That(Get(master,"IsSingleMode"), Is.False);
        Call(flow,"Ready",0); Assert.That(Screen,Is.EqualTo("Characters"));
        Call(flow,"Ready",1); Assert.That(Screen,Is.EqualTo("Playing"));
        Assert.That(Get(flow,"IsPlaying"),Is.True);
        MonoBehaviour presentation = Find("DuelPresentation");
        Assert.That(presentation.transform.Find("Match presentation").gameObject.activeSelf, Is.False);
        Call(flow,"TogglePause");Call(flow,"TogglePause");
        Assert.That(presentation.transform.Find("Match presentation").gameObject.activeSelf, Is.False, "Pause preserves free mode without match rules.");
        Call(flow,"ReturnToCharacters"); Assert.That(Get(master,"IsFreeMode"),Is.True);
        foreach(bool single in new[]{true,false})
        {
            Call(flow,"ReturnToModes");Call(flow,"ChooseMode",single);
            Assert.That(Get(master,"IsFreeMode"),Is.False);
            Assert.That(Get(master,"IsSingleMode"),Is.EqualTo(single));
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator DedicatedMenusAndConnectionPauseRetainActionsWithoutExtraWords()
    {
        foreach(string expected in new[]{"Title","Characters"})
        {
            GameObject menu=Field<GameObject>(flow,"menuCanvas");
            RawImage backdrop=menu.GetComponentsInChildren<RawImage>().Single(i=>i.name=="Menu background");
            Assert.That(backdrop.texture,Is.SameAs(Field<Texture2D>(flow,"menuBackground")));
            Assert.That(((Texture2D)backdrop.texture).GetPixels().All(c=>c.a==1f),Is.True,"Opaque menu art hides the arena.");
            Assert.That(menu.GetComponentsInChildren<Image>().All(i=>i.GetComponent<Button>()!=null),Is.True,expected);
            Assert.That(menu.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("：") || t.text.Contains("選んでください") || t.text.Contains("チャンバラ")),Is.False,expected);
            Assert.That(menu.GetComponentsInChildren<Button>().Length,Is.GreaterThan(0));
            Call(flow,"TogglePause");
            Assert.That(Field<GameObject>(flow,"pauseCanvas").GetComponentsInChildren<Image>().All(i=>i.GetComponent<Button>()!=null),Is.True);
            Assert.That(Field<GameObject[]>(phone,"slotCards").All(c=>c.GetComponentsInChildren<Image>().All(i=>i.GetComponent<Button>()!=null)),Is.True);
            Call(flow,"TogglePause");
            if(expected=="Title")Call(flow,"ChooseMode",true);
            else if(expected=="Characters")Call(flow,"Ready",0);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator TitleSingleSelectionAndConnectionPauseKeepTheSession()
    {
        Assert.That(Screen, Is.EqualTo("Title"));
        Assert.That(Get(master,"IsPaused"), Is.True);
        Assert.That(Get(phone,"OverlayVisible"), Is.False);
        int phoneId = phone.GetInstanceID();
        Call(flow,"ChooseMode",true);
        Assert.That(Screen, Is.EqualTo("Characters"));
        Assert.That(Get(master,"IsSingleMode"), Is.True);
        Call(flow,"TogglePause");
        Assert.That(Get(phone,"OverlayVisible"), Is.True);
        GameObject[] cards=Field<GameObject[]>(phone,"slotCards");
        Assert.That(cards.All(c=>c.activeSelf), Is.True,"Both controllers can operate the single-player menu.");
        foreach(Button button in Field<GameObject>(flow,"pauseCanvas").GetComponentsInChildren<Button>())
            Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.None), button.name);
        Call(flow,"TogglePause");
        Call(flow,"Ready",1); Assert.That(Screen,Is.EqualTo("Playing"),"P2 starts the same single-player selection as P1.");
        Assert.That(Field<GameObject>(flow,"menuCanvas").activeSelf,Is.False);
        Assert.That(Get(flow,"IsPlaying"),Is.True); Assert.That(Get(master,"StageIndex"),Is.EqualTo(2));
        Assert.That(phone.GetInstanceID(),Is.EqualTo(phoneId));
        Call(flow,"TogglePause"); Assert.That(Get(master,"IsPaused"),Is.True);
        Call(flow,"TogglePause"); Assert.That(Get(master,"IsPaused"),Is.False);
        Call(flow,"ReturnToTitle");Assert.That(Screen,Is.EqualTo("Title"));Assert.That(Get(master,"IsPaused"),Is.True);
        yield return null;
    }

    [UnityTest]
    public IEnumerator DuelRequiresBothPlayersAndKeepsIndependentAppearance()
    {
        Call(flow,"ChooseMode",false);
        Call(master,"SetCharacter",0,1); Call(master,"SetCharacter",1,3);
        Call(master,"SetColor",0,2); Call(master,"SetColor",1,4);
        Call(flow,"ChooseMode",false); // Synchronize focused choices with the selected appearance.
        Call(flow,"Ready",1);Assert.That(Screen,Is.EqualTo("Characters"));
        Call(flow,"Ready",0);Assert.That(Screen,Is.EqualTo("Playing"));
        Assert.That(Get(flow,"IsPlaying"),Is.True);
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{0}),Is.EqualTo(1));
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{1}),Is.EqualTo(3));
        Assert.That(master.GetType().GetMethod("ColorIndex").Invoke(master,new object[]{0}),Is.EqualTo(2));
        Assert.That(master.GetType().GetMethod("ColorIndex").Invoke(master,new object[]{1}),Is.EqualTo(4));
        Call(flow,"TogglePause");GameObject[] cards=Field<GameObject[]>(phone,"slotCards");Assert.That(cards.All(c=>c.activeSelf),Is.True);
        yield return null;
    }
    [UnityTest]
    public IEnumerator HoldingGuardDoesNotConfirmMultipleScreensAndKeyboardChangesRows()
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift));
        yield return null;
        Assert.That(Screen,Is.EqualTo("Difficulty"));
        yield return new WaitForSecondsRealtime(.3f);
        Assert.That(Screen,Is.EqualTo("Difficulty"),"Held guard must not confirm difficulty.");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift));yield return null;
        Assert.That(Screen,Is.EqualTo("Characters"));
        yield return new WaitForSecondsRealtime(.3f);
        Assert.That(Field<int[]>(flow,"row")[0],Is.EqualTo(0),"Held guard must not spill into character selection.");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));yield return null;
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{0}),Is.EqualTo(0),"Navigation only moves focus.");
        Assert.That(Field<int[]>(flow,"focusedCharacter")[0],Is.EqualTo(1));
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift));yield return null;
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{0}),Is.EqualTo(1),"Guard commits the focused character.");
        Assert.That(Field<int[]>(flow,"row")[0],Is.EqualTo(1));
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift));yield return null;
        Assert.That(Field<int[]>(flow,"row")[0],Is.EqualTo(2));
        yield return new WaitForSecondsRealtime(.3f);
        Assert.That(Screen,Is.EqualTo("Characters"));
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift));yield return null;
        Assert.That(Screen,Is.EqualTo("Playing"));
        double started=Field<double>(master,"singleStarted");
        yield return new WaitForSecondsRealtime(.3f);
        Assert.That(Screen,Is.EqualTo("Playing"));
        Assert.That(Field<double>(master,"singleStarted"),Is.EqualTo(started),"Held guard must not restart the match or its intro.");
    }

    [UnityTest]
    public IEnumerator EscapeWorksAcrossTheFlowAndYDoesNotOpenAMenu()
    {
        foreach(string expected in new[]{"Title","Characters","Playing"})
        {
            Assert.That(Screen,Is.EqualTo(expected));
            yield return Press(Key.Escape);
            Assert.That(Get(flow,"PauseOpen"),Is.True);
            Assert.That(Get(phone,"OverlayVisible"),Is.True);
            Assert.That(Get(master,"IsPaused"),Is.True);
            yield return Press(Key.Escape);
            Assert.That(Get(flow,"PauseOpen"),Is.False);
            Assert.That(Get(phone,"OverlayVisible"),Is.False);
            Assert.That(Screen,Is.EqualTo(expected));
            if(expected=="Title")Call(flow,"ChooseMode",true);
            else if(expected=="Characters")Call(flow,"Ready",0);
        }
        yield return Press(Key.Y);
        Assert.That(Get(master,"IsPaused"),Is.False);
        Assert.That(Get(phone,"OverlayVisible"),Is.False);
        Assert.That(Get(flow,"PauseOpen"),Is.False);
        Assert.That(Get(flow,"IsPlaying"),Is.True);
    }
    private IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
    }

    [UnityTest]
    public IEnumerator SecondControllerUsesTheSharedSingleSelectorAndStartsImmediately()
    {
        yield return Press(Key.RightShift);
        Assert.That(Screen, Is.EqualTo("Difficulty"), "P2 can confirm the title choice.");
        yield return new WaitForSecondsRealtime(.25f);
        yield return Press(Key.RightShift);
        Assert.That(Screen, Is.EqualTo("Characters"), "P2 can confirm difficulty.");
        Assert.That(Get(master,"IsSingleMode"), Is.True);
        yield return new WaitForSecondsRealtime(.25f);
        yield return Press(Key.RightArrow);
        Assert.That(Field<int[]>(flow,"focusedCharacter")[0], Is.EqualTo(1));
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{0}), Is.EqualTo(0));
        yield return Press(Key.RightShift);
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{0}), Is.EqualTo(1));
        yield return Press(Key.RightArrow);
        Assert.That(Field<int[]>(flow,"focusedColor")[0], Is.EqualTo(1));
        Assert.That(master.GetType().GetMethod("ColorIndex").Invoke(master,new object[]{0}), Is.EqualTo(0));
        yield return Press(Key.RightShift);
        Assert.That(master.GetType().GetMethod("ColorIndex").Invoke(master,new object[]{0}), Is.EqualTo(1));
        yield return Press(Key.RightShift);
        Assert.That(Screen, Is.EqualTo("Playing"));
        Assert.That(Get(flow,"IsPlaying"), Is.True, "P2 starts directly from the shared character selection.");
        Assert.That(Get(master,"StageIndex"),Is.EqualTo(2));
        Call(flow,"ReturnToCharacters");
        Assert.That(Screen, Is.EqualTo("Characters"));
        Assert.That(Get(master,"IsPaused"), Is.True);
        Assert.That(Field<bool[]>(flow,"ready").Any(r=>r), Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator DuelFocusIsIndependentAndReadyCommitsPendingChoices()
    {
        Call(flow,"ChooseMode",false);
        Call(flow,"Navigate",0,Vector2Int.right);
        Call(flow,"Navigate",1,Vector2Int.left);
        Assert.That(Field<int[]>(flow,"focusedCharacter"), Is.EqualTo(new[]{1,3}));
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{0}), Is.EqualTo(0));
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{1}), Is.EqualTo(0));
        Call(flow,"Navigate",1,Vector2Int.down); // Color row for P2 alone.
        Call(flow,"Navigate",1,Vector2Int.right);
        Assert.That(Field<int[]>(flow,"row"), Is.EqualTo(new[]{0,1}));
        Assert.That(Field<int[]>(flow,"focusedColor")[1], Is.EqualTo(2));
        Assert.That(master.GetType().GetMethod("ColorIndex").Invoke(master,new object[]{1}), Is.EqualTo(1));
        Call(flow,"Ready",1);
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{1}), Is.EqualTo(3));
        Assert.That(master.GetType().GetMethod("ColorIndex").Invoke(master,new object[]{1}), Is.EqualTo(2));
        Assert.That(Screen, Is.EqualTo("Characters"));
        Call(flow,"Ready",0);
        Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master,new object[]{0}), Is.EqualTo(1));
        Assert.That(Screen, Is.EqualTo("Playing"));
        Call(flow,"ReturnToCharacters"); Assert.That(Screen, Is.EqualTo("Characters"));
        Assert.That(Field<bool[]>(flow,"ready").All(r=>!r), Is.True);
        Call(flow,"Back"); Assert.That(Screen, Is.EqualTo("Title"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator PhoneAndUsbWorldAngularVelocityNavigatesRegardlessOfPostureAboveThreeSixty()
    {
        // Inactive hubs skip Awake: inject real input samples without opening
        // another signaling connection or native serial receiver.
        MonoBehaviour hardwareInput = InactiveHub("HardwareControllerHub");
        MonoBehaviour phoneInput = InactiveHub("PhoneControllerHub");
        Array sensors = Field<Array>(flow, "motion");
        foreach (MonoBehaviour sensor in sensors)
        {
            SetField(sensor, "hardware", hardwareInput);
            SetField(sensor, "hub", phoneInput);
        }
        flow.enabled = false;
        uint sequence = 0;
        try
        {
            foreach (bool usePhone in new[] { false, true })
                for (int slot = 0; slot < 2; slot++)
                {
                    foreach (object state in Field<Array>(phoneInput, "slots"))
                    { SetField(state, "ChannelOpen", false); SetField(state, "HasSample", false); }
                    Array.Clear(Field<bool[]>(hardwareInput, "valid"), 0, 2);
                    foreach (MonoBehaviour sensor in sensors) Call(sensor, "Update");
                    Call(flow, "ChooseMode", usePhone); // USB: independent duel; phone: shared single.
                    yield return new WaitForSecondsRealtime(.25f);
                    int selectedPlayer = usePhone ? 0 : slot;
                    int before = Field<int[]>(flow, "focusedCharacter")[selectedPlayer];
                    int otherBefore = Field<int[]>(flow, "focusedCharacter")[1 - selectedPlayer];
                    MonoBehaviour activeSensor = (MonoBehaviour)sensors.GetValue(slot);
                    // Real decoded phone postures: screen up for P1, screen left
                    // for P2. Neither may use the calibrated sword's local frame.
                    Quaternion reference = usePhone
                        ? (slot == 0 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.Euler(0f, 0f, -90f))
                        : Quaternion.Euler(23f, -37f, 62f);
                    bool gyroValid = slot == 0; // P2 also covers an orientation-only remote.
                    // Phone navigation uses its fixed sensor-world frame. USB
                    // uses the gameplay reference, independent of BNO heading.
                    Vector3 horizontalAxis = usePhone ? Vector3.forward : reference * Vector3.up;
                    void Horizontal(float angle, float speed, bool guard = false)
                    {
                        Quaternion pose = Quaternion.AngleAxis(angle, horizontalAxis) * reference;
                        Vector3 localVelocity = Quaternion.Inverse(pose) * (horizontalAxis * speed);
                        SupplyMenuMotion(hardwareInput, phoneInput, usePhone, slot, pose, speed,
                            guard, gyroValid, ++sequence, activeSensor, localVelocity);
                    }
                    foreach (float delay in new[] { 0f, .06f, .12f })
                    {
                        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
                        Horizontal(0f, 0f);
                    }
                    Assert.That(Get(activeSensor, "ActiveSource").ToString(), Is.EqualTo(usePhone ? "Phone" : "Hardware"));
                    Assert.That(Quaternion.Angle((Quaternion)Get(activeSensor,"ReferenceRotation"),reference),
                        Is.LessThan(.01f),"The reference must remain fixed through the menu swings.");
                    // Below-threshold tilts must not change focus on either path.
                    for (int step = 1; step <= 3; step++)
                    {
                        yield return new WaitForSecondsRealtime(.05f);
                        Horizontal(step * 3f, 60f);
                        Assert.That(Field<int[]>(flow, "focusedCharacter")[selectedPlayer], Is.EqualTo(before));
                    }
                    // One continuous fast burst moves exactly one item.
                    for (int step = 1; step <= 3; step++)
                    {
                        yield return new WaitForSecondsRealtime(.05f);
                        Horizontal(9f + step * 24f, 480f);
                    }
                    int expected = (before + 1) % (int)Get(master, "CharacterCount");
                    Assert.That(Field<int[]>(flow, "focusedCharacter")[selectedPlayer], Is.EqualTo(expected),
                        (usePhone ? "Phone" : "USB") + " P" + (slot + 1) + " must move focus once.");
                    Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master, new object[] { selectedPlayer }),
                        Is.EqualTo(before), "Motion changes focus; guard commits the appearance.");
                    if (!usePhone)
                        Assert.That(Field<int[]>(flow, "focusedCharacter")[1 - selectedPlayer], Is.EqualTo(otherBefore));
                    // A slow partial return rearms without reaching the original pose.
                    yield return new WaitForSecondsRealtime(.05f);
                    Horizontal(81f, 0f);
                    Assert.That(Field<int[]>(flow, "focusedCharacter")[selectedPlayer], Is.EqualTo(expected));
                    foreach (float angle in new[] { 75f, 69f, 63f, 57f })
                    {
                        yield return new WaitForSecondsRealtime(.05f);
                        Horizontal(angle, -120f);
                        Assert.That(Field<int[]>(flow, "focusedCharacter")[selectedPlayer], Is.EqualTo(expected));
                    }
                    expected = (expected + 1) % (int)Get(master, "CharacterCount");
                    foreach (float angle in new[] { 81f, 105f })
                    {
                        yield return new WaitForSecondsRealtime(.05f);
                        Horizontal(angle, 480f);
                        Assert.That(Field<int[]>(flow, "focusedCharacter")[selectedPlayer], Is.EqualTo(expected),
                            "The next fast swing works after a partial slow return.");
                    }
                    Horizontal(105f, 0f);
                    expected = (expected - 1 + (int)Get(master, "CharacterCount")) % (int)Get(master, "CharacterCount");
                    foreach (float angle in new[] { 81f, 57f })
                    {
                        Horizontal(angle, -480f);
                        Assert.That(Field<int[]>(flow, "focusedCharacter")[selectedPlayer], Is.EqualTo(expected),
                            "A left swing moves left even when the phone screen faces up.");
                    }
                    Horizontal(57f, 0f, true);
                    Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master, new object[] { selectedPlayer }),
                        Is.EqualTo(expected), "The actual remote guard edge commits the focused character.");
                    Assert.That(Field<int[]>(flow, "row")[selectedPlayer], Is.EqualTo(1));
                    Quaternion rest = reference * Quaternion.Euler(60f, 0f, 0f);
                    Call(flow, "TogglePause");
                    yield return new WaitForSecondsRealtime(.25f);
                    SupplyMenuMotion(hardwareInput, phoneInput, usePhone, slot, rest, 0f,
                        false, gyroValid, ++sequence, activeSensor);
                    Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("ゲームに戻る"));
                    Vector3 verticalAxis = usePhone ? Vector3.right : reference * Vector3.right;
                    Quaternion up = Quaternion.AngleAxis(-24f, verticalAxis) * rest;
                    Vector3 upVelocity = Quaternion.Inverse(up) * (-verticalAxis * 480f);
                    SupplyMenuMotion(hardwareInput, phoneInput, usePhone, slot, up, 0f,
                        false, gyroValid, ++sequence, activeSensor, upVelocity);
                    Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("タイトルへ"),
                        "Raising the controller also moves up in the connection pause.");
                    SupplyMenuMotion(hardwareInput, phoneInput, usePhone, slot, up, 0f,
                        true, gyroValid, ++sequence, activeSensor);
                    Assert.That(Get(flow, "PauseOpen"), Is.False);
                    Assert.That(Screen, Is.EqualTo("Title"), "Guard confirms the selected pause action.");
                }
        }
        finally
        {
            Object.Destroy(hardwareInput.gameObject);
            Object.Destroy(phoneInput.gameObject);
        }
    }

    [UnityTest]
    public IEnumerator PhysicalPhonePacketsMoveFromBasicDownToRedAndRightToNextCharacter()
    {
        MonoBehaviour phoneInput = InactiveHub("PhoneControllerHub");
        MonoBehaviour hardwareInput = InactiveHub("HardwareControllerHub");
        Array sensors = Field<Array>(flow, "motion");
        foreach (MonoBehaviour sensor in sensors)
        {
            SetField(sensor, "hub", phoneInput); SetField(sensor, "hardware", hardwareInput);
        }
        flow.enabled = false;
        uint sequence = 0;
        // These wire values are also checked against physical browser events
        // in apps/phone: default screen up, screen left, landscape and saved
        // X/Z swap. Pass them through the real decoder and visible menu.
        var packets = new[] {
            (rotation: new Quaternion(-.70710677f, 0f, 0f, .70710677f),
                down: new Vector3(-480f, 0f, 0f), right: new Vector3(0f, -480f, 0f)),
            (rotation: new Quaternion(-.5f, .5f, .5f, .5f),
                down: new Vector3(0f, 480f, 0f), right: new Vector3(-480f, 0f, 0f)),
            (rotation: new Quaternion(-.5f, -.5f, .5f, .5f),
                down: new Vector3(0f, 0f, 480f), right: new Vector3(0f, -480f, 0f)),
            (rotation: new Quaternion(-.70710677f, 0f, .70710677f, 0f),
                down: new Vector3(0f, 0f, 480f), right: new Vector3(-480f, 0f, 0f))
        };
        try
        {
            foreach (var fixture in packets)
                foreach (bool single in new[] { true, false })
                for (int slot = 0; slot < 2; slot++)
                {
                    foreach (object state in Field<Array>(phoneInput, "slots"))
                    { SetField(state, "ChannelOpen", false); SetField(state, "HasSample", false); }
                    foreach (MonoBehaviour sensor in sensors) Call(sensor, "Update");
                    int player = single ? 0 : slot;
                    Call(master, "SetCharacter", player, 0); Call(master, "SetColor", player, 0);
                    Call(flow, "ChooseMode", single);
                    yield return new WaitForSecondsRealtime(.25f);
                    MonoBehaviour activeSensor = (MonoBehaviour)sensors.GetValue(slot);
                    void Send(Vector3 wireGyro)
                    {
                        byte[] packet = new byte[56]; packet[0] = 1; packet[1] = 6;
                        void Write(int offset, byte[] value) => Array.Copy(value, 0, packet, offset, value.Length);
                        Write(4, BitConverter.GetBytes(++sequence));
                        Write(8, BitConverter.GetBytes(sequence * 50d));
                        Write(16, BitConverter.GetBytes(fixture.rotation.x));
                        Write(20, BitConverter.GetBytes(fixture.rotation.y));
                        Write(24, BitConverter.GetBytes(fixture.rotation.z));
                        Write(28, BitConverter.GetBytes(fixture.rotation.w));
                        Write(32, BitConverter.GetBytes(wireGyro.x));
                        Write(36, BitConverter.GetBytes(wireGyro.y));
                        Write(40, BitConverter.GetBytes(wireGyro.z));
                        object[] args = { packet, Time.realtimeSinceStartupAsDouble, null };
                        Assert.That(Type.GetType("MotionPacketCodec, Assembly-CSharp", true)
                            .GetMethod("TryDecodeMotion").Invoke(null, args), Is.True);
                        object state = Field<Array>(phoneInput, "slots").GetValue(slot);
                        SetField(state, "LatestSample", args[2]);
                        SetField(state, "HasSample", true); SetField(state, "ChannelOpen", true);
                        Call(activeSensor, "Update"); Call(flow, "Update");
                    }
                    Send(Vector3.zero);
                    Assert.That(Get(activeSensor, "ActiveSource").ToString(), Is.EqualTo("Phone"));
                    Send(fixture.down);
                    Assert.That(Field<int[]>(flow, "row")[player], Is.EqualTo(1), "Down must move to the color row.");
                    Assert.That(Field<int[]>(flow, "focusedCharacter")[player], Is.Zero, "Down must leave Basic focused.");
                    Assert.That(Field<int[]>(flow, "focusedColor")[player], Is.Zero, "The first color is red.");
                    // Check the visible color cursor too, rather than only the
                    // internal row. The selected player's red swatch is outlined.
                    Button[] colors = Field<GameObject>(flow, "menuCanvas").GetComponentsInChildren<Button>()
                        .Where(b => b.GetComponent<RectTransform>().sizeDelta == new Vector2(65f, 56f)).ToArray();
                    Assert.That(colors[player * 6].GetComponent<Outline>(), Is.Not.Null);
                    Send(Vector3.zero); Send(fixture.right); // Right along colors.
                    Assert.That(Field<int[]>(flow, "row")[player], Is.EqualTo(1));
                    Assert.That(Field<int[]>(flow, "focusedColor")[player], Is.EqualTo(1));
                    Send(Vector3.zero); Send(-fixture.down); // Up to Basic.
                    Assert.That(Field<int[]>(flow, "row")[player], Is.Zero);
                    Send(Vector3.zero); Send(fixture.right); // Right to Sukkiri.
                    Assert.That(Field<int[]>(flow, "row")[player], Is.Zero);
                    Assert.That(Field<int[]>(flow, "focusedCharacter")[player], Is.EqualTo(1));
                    Assert.That(master.GetType().GetMethod("CharacterIndex").Invoke(master, new object[] { player }),
                        Is.EqualTo(0), "Swinging moves the cursor without confirming the character.");
                    Assert.That(master.GetType().GetMethod("ColorIndex").Invoke(master, new object[] { player }), Is.EqualTo(0));
                }
        }
        finally
        {
            Object.Destroy(phoneInput.gameObject); Object.Destroy(hardwareInput.gameObject);
        }
    }

    private void SupplyMenuMotion(MonoBehaviour hardwareInput, MonoBehaviour phoneInput, bool usePhone,
        int slot, Quaternion rotation, float speed, bool guard, bool gyroValid, uint sequence, MonoBehaviour sensor,
        Vector3? angularVelocity = null, ushort resetCounter = 0, bool recenterHeld = false)
    {
        object sample = Activator.CreateInstance(Type.GetType("MotionSample, Assembly-CSharp", true), new object[] {
            rotation, angularVelocity ?? new Vector3(0f, 0f, speed), Vector3.zero, guard, true, gyroValid, false,
            sequence, resetCounter, sequence * 50d, Time.realtimeSinceStartupAsDouble, recenterHeld
        });
        if (usePhone)
        {
            object state = Field<Array>(phoneInput, "slots").GetValue(slot);
            SetField(state, "LatestSample", sample); SetField(state, "HasSample", true); SetField(state, "ChannelOpen", true);
        }
        else
        {
            Field<Array>(hardwareInput, "latest").SetValue(sample, slot);
            Field<bool[]>(hardwareInput, "valid")[slot] = true;
        }
        Call(sensor, "Update");
        Call(flow, "Update");
    }

    private static MonoBehaviour InactiveHub(string name)
    {
        GameObject host = new GameObject("Menu input test " + name); host.SetActive(false);
        MonoBehaviour hub = (MonoBehaviour)host.AddComponent(Type.GetType(name + ", Assembly-CSharp", true));
        hub.enabled = false; return hub;
    }
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

    [UnityTest]
    public IEnumerator FighterPortraitsFrameTheVisibleSkeletonForEveryCharacter()
    {
        AssertPortraitFraming();
        Call(flow,"ChooseMode",false);
        for(int character=0;character<(int)Get(master,"CharacterCount");character++)
        {
            Call(master,"SetCharacter",0,character);Call(master,"SetCharacter",1,character);
            // Rebuild the real selection view after changing the model.
            Call(flow,"ChooseMode",false);
            yield return null;
            AssertPortraitFraming();
        }
    }
    private void AssertPortraitFraming()
    {
        Array portraits=Field<Array>(flow,"previews");
        foreach(object portrait in portraits)
        {
            if(portrait==null)continue;
            Camera camera=Field<Camera>(portrait,"camera");
            Transform fighter=Field<Transform>(portrait,"fighter");
            Transform head=fighter.GetComponentsInChildren<Transform>().First(t=>t.name=="head");
            Transform ankle=fighter.GetComponentsInChildren<Transform>().First(t=>t.name=="ankle_l");
            Vector3 top=camera.WorldToViewportPoint(head.position),bottom=camera.WorldToViewportPoint(ankle.position);
            Assert.That(top.y-bottom.y,Is.InRange(.4f,.95f),"The fighter must fill the portrait: "+fighter.GetChild(1).name+", ortho="+camera.orthographicSize);
            Assert.That(top.y,Is.InRange(.5f,.98f));Assert.That(bottom.y,Is.InRange(.02f,.5f));
            Assert.That(top.z,Is.InRange(camera.nearClipPlane,camera.farClipPlane));
            Transform sword=Field<Transform>(portrait,"sword");
            foreach(float time in new[]{0f,5f,10f,15f})
            {
                Call(portrait,"Tick",time,.016f,null);
                Vector3 face=camera.WorldToViewportPoint(head.position);
                Vector3 grip=camera.WorldToViewportPoint(sword.position);
                Vector3 tip=camera.WorldToViewportPoint(sword.TransformPoint(0f,0f,2.1f));
                float bladeAtFace=Mathf.Lerp(grip.x,tip.x,Mathf.InverseLerp(grip.y,tip.y,face.y));
                Assert.That(Mathf.Abs(face.x-bladeAtFace),Is.GreaterThan(.1f),"The portrait blade must stay beside the face.");
                foreach(MeshFilter mesh in sword.GetComponentsInChildren<MeshFilter>())
                {
                    if(mesh.sharedMesh==null)continue;
                    Bounds bounds=mesh.sharedMesh.bounds;
                    for(int corner=0;corner<8;corner++)
                    {
                        Vector3 vertex=mesh.transform.TransformPoint(bounds.center+Vector3.Scale(bounds.extents,
                            new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                        Vector3 screen=camera.WorldToViewportPoint(vertex);
                        Assert.That(screen.x,Is.InRange(.01f,.99f),"The complete portrait sword must fit horizontally.");
                        Assert.That(screen.y,Is.InRange(.01f,.99f),"The portrait blade tip and grip must not be clipped.");
                    }
                }
            }
        }
    }

    [UnityTest]
    public IEnumerator CaptureSelectionFlowWhenRequested()
    {
        string folder=Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR");
        if(string.IsNullOrEmpty(folder))yield break;
        System.IO.Directory.CreateDirectory(folder);
        yield return Capture(folder,"flow-title.png");
        Call(flow,"ChooseSingleMode");
        yield return Capture(folder,"flow-difficulty.png");
        Call(flow,"ChooseDifficulty",false);
        yield return Capture(folder,"flow-single-character.png");
        Call(flow,"ReturnToTitle");Call(flow,"ChooseMode",false);
        yield return Capture(folder,"flow-duel-character.png");
        Call(flow,"OpenCharacterPreview",1);
        yield return Capture(folder,"flow-character-preview.png");
        Call(flow,"CloseCharacterPreview");
        Call(flow,"Ready",0);Call(flow,"Ready",1);
        yield return Capture(folder,"flow-stage.png");
        Call(flow,"TogglePause");
        yield return Capture(folder,"flow-connections.png");
        Call(flow,"ReturnToTitle");Call(flow,"ChooseFreeMode");Call(flow,"OpenCharacterPreview",0);
        yield return Capture(folder,"flow-free-character-preview.png");
    }
    private IEnumerator Capture(string folder,string name)
    {
        yield return null;yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D image=ScreenCapture.CaptureScreenshotAsTexture();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name),image.EncodeToPNG());
        foreach(Text status in Field<Text[]>(flow,"controllerStatus"))
        {
            if(!status.gameObject.activeInHierarchy){Assert.That(Get(flow,"PauseOpen"),Is.False);continue;}
            Vector3[] corners=new Vector3[4];status.rectTransform.GetWorldCorners(corners);
            Vector2 lower=RectTransformUtility.WorldToScreenPoint(null,corners[0]);
            Vector2 upper=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
            int inkPixels=0;
            for(int y=Mathf.Max(0,Mathf.FloorToInt(lower.y));y<Mathf.Min(image.height,Mathf.CeilToInt(upper.y));y++)
                for(int x=Mathf.Max(0,Mathf.FloorToInt(lower.x));x<Mathf.Min(image.width,Mathf.CeilToInt(upper.x));x++)
                {
                    Color pixel=image.GetPixel(x,y);
                    if(pixel.r<.6f&&pixel.g<.7f&&pixel.b<.8f)inkPixels++;
                }
            Assert.That(inkPixels,Is.GreaterThan(20),name+": connection status must actually be drawn: "+status.text);
        }
        Object.Destroy(image);
    }

}
