using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ControllerAxisCalibrationPlayTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private MonoBehaviour flow, panel, phone, hardware;
    private MonoBehaviour[] sensors;
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Fields).GetValue(value);
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, Fields).SetValue(value, field);
    private static object Get(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    private static void Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Fields).Invoke(value, args);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        flow = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(x => x.GetType().Name == "GameFlowController");
        flow.enabled = false;
        panel = (MonoBehaviour)Field<object>(flow, "axisCalibration"); panel.enabled = false;
        sensors = Field<Array>(flow, "motion").Cast<MonoBehaviour>().ToArray();
        phone = Hub("PhoneControllerHub"); hardware = Hub("HardwareControllerHub");
        foreach (MonoBehaviour sensor in sensors) { Set(sensor, "hub", phone); Set(sensor, "hardware", hardware); }
    }

    private static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "HardwareControllerHub"
                || component.GetType().Name == "MotionController") component.enabled = false;
    }

    private static MonoBehaviour Hub(string name)
    {
        GameObject host = new GameObject("Axis test " + name); host.SetActive(false);
        return (MonoBehaviour)host.AddComponent(Runtime(name));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "HardwareControllerHub")
                Object.Destroy(component.gameObject);
        yield return null;
    }

    private void Supply(bool usePhone, int player, uint sequence, Quaternion pose, Vector3 gyro, ushort reset = 0, bool guard = false, Vector3? acceleration = null)
    {
        object sample = Activator.CreateInstance(Runtime("MotionSample"), new object[] {
            pose, gyro, acceleration ?? Vector3.forward * 5f, guard, true, usePhone, true,
            sequence, reset, sequence * 20d, Time.realtimeSinceStartupAsDouble, false
        });
        if (usePhone)
        {
            object slot = Field<Array>(phone, "slots").GetValue(player);
            Set(slot, "LatestSample", sample); Set(slot, "HasSample", true); Set(slot, "ChannelOpen", true);
        }
        else
        { Field<Array>(hardware, "latest").SetValue(sample, player); Field<bool[]>(hardware, "valid")[player] = true; }
        Call(sensors[player], "Update");
    }

    [UnityTest]
    public IEnumerator BothPlayersAndSourcesCanTryAllSixPresetsWithLiveDirectionAndImmediateApplication()
    {
        foreach (bool usePhone in new[] { false, true })
            foreach (int player in new[] { 0, 1 })
            {
                foreach (object state in Field<Array>(phone, "slots")) { Set(state, "ChannelOpen", false); Set(state, "HasSample", false); }
                Array.Clear(Field<bool[]>(hardware, "valid"), 0, 2);
                foreach (MonoBehaviour sensor in sensors) Call(sensor, "Update");
                Quaternion reference = usePhone ? Quaternion.Euler(90, 0, 0) : Quaternion.Euler(23, -70, 15);
                uint sequence = 1;
                Supply(usePhone, player, sequence++, reference, Vector3.zero);
                if (!(bool)Get(flow, "PauseOpen")) Call(flow, "TogglePause");
                yield return new WaitForSecondsRealtime(.25f);
                Supply(usePhone, player, sequence++, reference, Vector3.zero);
                Call(panel, "Update");
                Button entry = (Button)Get(panel, "EntryButton");
                Assert.That(entry.gameObject.activeInHierarchy, Is.True);
                entry.onClick.Invoke(); Call(panel, "SelectPlayer", player);
                Assert.That(Get(panel, "CurrentStep").ToString(), Is.EqualTo("Presets"));
                var choices = Field<System.Collections.Generic.List<Button>>(panel, "presetButtons");
                Assert.That(choices.Count, Is.EqualTo(6));
                Assert.That(Field<GameObject>(panel, "page").GetComponentsInChildren<Text>()
                    .Any(t => t.text.Contains("記録開始")), Is.False);
                Assert.That(Field<Text>(panel, "feedback").rectTransform.anchoredPosition.y,
                    Is.GreaterThan(choices.Max(b => b.GetComponent<RectTransform>().anchoredPosition.y)));
                yield return new WaitForSecondsRealtime(.25f);
                Supply(usePhone, player, sequence++, reference, Vector3.zero);
                ControllerAxisMapping other = (ControllerAxisMapping)Get(sensors[1-player], "AxisMapping");
                for (int index = 0; index < 6; index++)
                {
                    choices[index].Select(); choices[index].onClick.Invoke(); Call(sensors[player], "Update");
                    Assert.That((int)Get(panel, "CurrentPreset"), Is.EqualTo(index));
                    Assert.That(((ControllerAxisMapping)Get(sensors[player], "AxisMapping")).PresetIndex, Is.EqualTo(index));
                    Assert.That(Quaternion.Angle((Quaternion)Get(sensors[player], "ReferenceRotation"), reference), Is.LessThan(.01f),
                        "Switching axes must retain the original neutral pose.");
                    var mapping = ControllerAxisMapping.FromPreset(index);
                    Quaternion basis = mapping.IsCalibrated ? mapping.PoseBasis : Quaternion.identity;
                    Quaternion inverse = Quaternion.Inverse(basis);
                    Supply(usePhone, player, sequence++, reference, Vector3.zero); Call(panel, "Update");
                    Quaternion up = Quaternion.Euler(-8,0,0);
                    Supply(usePhone, player, sequence++, reference * inverse * up * basis,
                        inverse * Vector3.left * 400); Call(panel, "Update");
                    Assert.That(Field<Text>(panel, "feedback").text, Does.Contain("↑ 上"));
                    Assert.That((int)Get(panel, "CurrentPreset"), Is.EqualTo(index), "Test swings cannot switch options.");
                    Assert.That(Quaternion.Angle((Quaternion)Get(sensors[player], "CurrentLocalRotation"),
                        Quaternion.Euler(-90,0,0) * inverse * up * basis), Is.LessThan(.05f));
                    Quaternion right = Quaternion.Euler(0,8,0) * up;
                    Supply(usePhone, player, sequence++, reference * inverse * right * basis,
                        inverse * (Quaternion.Inverse(right) * Vector3.up * 400)); Call(panel, "Update");
                    Assert.That(Field<Text>(panel, "feedback").text, Does.Contain("→ 右"));
                    Assert.That(((ControllerAxisMapping)Get(sensors[1-player], "AxisMapping")).PresetIndex,
                        Is.EqualTo(other.PresetIndex));
                    // Repeated render frames retain the last detected direction.
                    Call(panel, "Update");
                    Assert.That(Field<Text>(panel, "feedback").text, Does.Contain("→ 右"));
                }
                Call(panel, "Close");
                Assert.That(((ControllerAxisMapping)Get(sensors[player], "AxisMapping")).PresetIndex, Is.EqualTo(5));
                Assert.That(Get(flow, "PauseOpen"), Is.True);
                int recenter = (int)Get(sensors[player], "RecenterVersion"); Call(flow, "Update");
                Assert.That(Get(sensors[player], "RecenterVersion"), Is.EqualTo(recenter));
                Call(sensors[player], "Recenter"); Call(sensors[player], "Update");
                Assert.That(((ControllerAxisMapping)Get(sensors[player], "AxisMapping")).PresetIndex, Is.EqualTo(5));
                Call(sensors[player], "SetMenuAxisMapping", default(ControllerAxisMapping));
            }
    }

    [UnityTest]
    public IEnumerator StandardRestoresOriginalInputAndConnectionChangesReturnToPlayerChoice()
    {
        Supply(true, 0, 1, Quaternion.Euler(90,0,0), Vector3.zero);
        Call(flow, "TogglePause"); yield return new WaitForSecondsRealtime(.25f);
        Supply(true, 0, 2, Quaternion.Euler(90,0,0), Vector3.zero);
        Call(flow, "OpenAxisCalibration"); Call(panel, "SelectPlayer", 0);
        Call(panel, "SelectPreset", 4); Call(sensors[0], "Update");
        Assert.That(((ControllerAxisMapping)Get(sensors[0], "AxisMapping")).IsCalibrated, Is.True);
        Call(panel, "SelectPreset", 0); Call(sensors[0], "Update");
        Assert.That(((ControllerAxisMapping)Get(sensors[0], "AxisMapping")).IsCalibrated, Is.False);
        object[] menu = { null, false, Quaternion.identity };
        sensors[0].GetType().GetMethod("TryGetMenuSample").Invoke(sensors[0], menu);
        Assert.That(menu[1], Is.True, "Standard must retain the original phone world framing.");
        Set(Field<Array>(phone, "slots").GetValue(0), "ChannelOpen", false);
        Supply(false, 0, 1, Quaternion.identity, Vector3.zero);
        Call(panel, "Update");
        Assert.That(Get(panel, "CurrentStep").ToString(), Is.EqualTo("Player"));
        Call(panel, "Close");
    }
    [UnityTest]
    public IEnumerator SwingsUpdateFeedbackAndCursorButOnlyGuardChangesThePreset()
    {
        foreach (bool usePhone in new[] { false, true })
            foreach (int player in new[] { 0, 1 })
            {
                foreach (object state in Field<Array>(phone, "slots")) { Set(state, "ChannelOpen", false); Set(state, "HasSample", false); }
                Array.Clear(Field<bool[]>(hardware, "valid"), 0, 2);
                foreach (MonoBehaviour sensor in sensors) Call(sensor, "Update");
                Quaternion reference = usePhone ? Quaternion.Euler(90,0,0) : Quaternion.identity;
                uint sequence = 1;
                Supply(usePhone, player, sequence++, reference, Vector3.zero);
                Call(sensors[player], "SetMenuAxisMapping", default(ControllerAxisMapping));
                Call(sensors[player], "Update");
                if (!(bool)Get(flow, "PauseOpen")) Call(flow, "TogglePause");
                yield return new WaitForSecondsRealtime(.25f);
                Supply(usePhone, player, sequence++, reference, Vector3.zero);
                Call(flow, "OpenAxisCalibration"); Call(panel, "SelectPlayer", player);
                yield return new WaitForSecondsRealtime(.25f);
                Supply(usePhone, player, sequence++, reference, Vector3.zero); Call(panel, "Update");
                var choices = Field<System.Collections.Generic.List<Button>>(panel, "presetButtons");
                Quaternion pose = Quaternion.identity;
                void Send(Vector3 worldAxis, float speed, bool guard = false)
                {
                    if (speed != 0) pose = Quaternion.AngleAxis(8,worldAxis) * pose;
                    Supply(usePhone, player, sequence++, reference * pose,
                        Quaternion.Inverse(pose) * worldAxis * speed, guard: guard);
                    Call(panel, "Update");
                }
                void AssertFocus(int index, string direction)
                {
                    Assert.That(UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject,
                        Is.EqualTo(choices[index].gameObject));
                    Assert.That(Field<Text>(panel, "feedback").text, Does.Contain(direction));
                    Assert.That((int)Get(panel, "CurrentPreset"), Is.Zero, "Moving focus must not change axes.");
                    Assert.That(choices[index].GetComponent<Outline>().enabled, Is.True);
                }
                Send(Vector3.right,400); AssertFocus(2,"↓ 下");
                Send(Vector3.right,400); AssertFocus(2,"↓ 下"); // Fast hold does not repeat.
                Send(Vector3.zero,0); Send(Vector3.up,400); AssertFocus(3,"→ 右");
                Send(Vector3.zero,0); Send(Vector3.left,400); AssertFocus(1,"↑ 上");
                Send(Vector3.zero,0); Send(Vector3.down,400); AssertFocus(0,"← 左");
                Send(Vector3.zero,0); Send(Vector3.right,400); AssertFocus(2,"↓ 下");
                Send(Vector3.zero,0); Send(Vector3.up,400); AssertFocus(3,"→ 右");
                Send(Vector3.zero,0,true);
                Assert.That((int)Get(panel,"CurrentPreset"),Is.EqualTo(3),"Guard commits the focused option.");
                Call(panel,"Close");
            }
    }

    private (Quaternion rotation, Vector3 gyro, bool gyroValid, double time) MenuSample(int player)
    {
        object[] args = { null, false, Quaternion.identity };
        Assert.That(sensors[player].GetType().GetMethod("TryGetMenuSample").Invoke(sensors[player], args), Is.True);
        object sample = args[0];
        Quaternion rotation = (Quaternion)Get(sample,"Rotation");
        rotation = (bool)args[1] ? MenuGestureNavigation.PhoneWorldToMenuWorld * rotation
            : Quaternion.Inverse((Quaternion)args[2]) * rotation;
        return (rotation,(Vector3)Get(sample,"AngularVelocityDegrees"),(bool)Get(sample,"GyroscopeValid"),
            (double)Get(sample,"BrowserTimestampMilliseconds") / 1000d);
    }

    [UnityTest]
    public IEnumerator MenuPresetsDoNotChangeCombatInputAcrossResetPoses()
    {
        foreach(bool usePhone in new[]{false,true})
        foreach(int player in new[]{0,1})
        {
            foreach(object state in Field<Array>(phone,"slots")){Set(state,"ChannelOpen",false);Set(state,"HasSample",false);}
            Array.Clear(Field<bool[]>(hardware,"valid"),0,2);
            foreach(MonoBehaviour sensor in sensors)Call(sensor,"Update");
            uint sequence=1;
            foreach(Quaternion reference in new[]{Quaternion.Euler(90,0,0),Quaternion.identity,Quaternion.Euler(27,-68,19),Quaternion.Euler(-35,81,-42)})
            {
                Supply(usePhone,player,sequence++,reference,Vector3.zero,acceleration:Vector3.zero);
                Call(sensors[player],"Recenter");
                Supply(usePhone,player,sequence++,reference,Vector3.zero,acceleration:Vector3.zero);
                Call(sensors[player],"SetMenuAxisMapping",default(ControllerAxisMapping));
                Quaternion[] poses={reference*Quaternion.Euler(-18,25,-11),reference*Quaternion.Euler(22,-31,8)};
                Vector3[] gyros={Vector3.down*400,Vector3.forward*480};
                Quaternion[] original=new Quaternion[2];
                for(int sample=0;sample<poses.Length;sample++)
                {
                    Supply(usePhone,player,sequence++,poses[sample],gyros[sample],acceleration:Vector3.zero);
                    original[sample]=(Quaternion)Get(sensors[player],"CurrentLocalRotation");
                }
                for(int preset=0;preset<6;preset++)
                {
                    int generation=(int)Get(sensors[player],"PoseGeneration");
                    int recenter=(int)Get(sensors[player],"RecenterVersion");
                    int menuGeneration=(int)Get(sensors[player],"MenuPoseGeneration");
                    Quaternion poseBefore=(Quaternion)Get(sensors[player],"CurrentLocalRotation");
                    Call(sensors[player],"SetMenuAxisMapping",ControllerAxisMapping.FromPreset(preset));
                    Assert.That(Get(sensors[player],"IsPoseValid"),Is.True);
                    Assert.That(Get(sensors[player],"PoseGeneration"),Is.EqualTo(generation));
                    Assert.That(Get(sensors[player],"RecenterVersion"),Is.EqualTo(recenter));
                    Assert.That(Get(sensors[player],"MenuPoseGeneration"),Is.GreaterThan(menuGeneration));
                    Assert.That(Get(sensors[player],"CurrentLocalRotation"),Is.EqualTo(poseBefore));
                    Assert.That(Get(sensors[player],"ReferenceRotation"),Is.EqualTo(reference));
                    for(int sample=0;sample<poses.Length;sample++)
                    {
                        Supply(usePhone,player,sequence++,poses[sample],gyros[sample],acceleration:Vector3.zero);
                        Assert.That(Get(sensors[player],"CurrentLocalRotation"),Is.EqualTo(original[sample]));
                        Assert.That(Get(sensors[player],"AngularVelocityDegrees"),Is.EqualTo(usePhone?gyros[sample]:Vector3.zero));
                        Assert.That((float)Get(sensors[player],"AngularSpeedDegrees"),Is.GreaterThan(0));
                    }
                }
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator MenuPresetsPreserveOriginalThrustForBothPlayersAndSources()
    {
        Quaternion upright=Quaternion.Euler(-90,0,0);
        foreach(bool usePhone in new[]{false,true})
        foreach(int player in new[]{0,1})
        {
            foreach(object state in Field<Array>(phone,"slots")){Set(state,"ChannelOpen",false);Set(state,"HasSample",false);}
            Array.Clear(Field<bool[]>(hardware,"valid"),0,2);
            foreach(MonoBehaviour sensor in sensors)Call(sensor,"Update");
            uint sequence=1;
            foreach(Quaternion reference in new[]{Quaternion.identity,Quaternion.Euler(27,-68,19)})
            for(int index=0;index<6;index++)
            {
                Supply(usePhone,player,sequence++,reference,Vector3.zero,acceleration:Vector3.zero);
                var mapping=ControllerAxisMapping.FromPreset(index);
                Call(sensors[player],"SetMenuAxisMapping",mapping);
                Call(sensors[player],"Recenter");
                Supply(usePhone,player,sequence++,reference,Vector3.zero,acceleration:Vector3.zero);
                Quaternion forward=reference*Quaternion.Inverse(upright);
                Vector3 rawPush=Vector3.forward*5;
                for(int sample=0;sample<20;sample++)Supply(usePhone,player,sequence++,forward,Vector3.zero,acceleration:Vector3.zero);
                Assert.That(Quaternion.Angle((Quaternion)Get(sensors[player],"CurrentLocalRotation"),Quaternion.identity),Is.LessThan(.05f));
                Supply(usePhone,player,sequence++,forward,Vector3.zero,acceleration:rawPush);
                Assert.That(sensors[player].GetType().GetMethod("TryConsumeThrust").Invoke(sensors[player],null),Is.False);
                Supply(usePhone,player,sequence++,forward,Vector3.zero,acceleration:rawPush);
                Assert.That(sensors[player].GetType().GetMethod("TryConsumeThrust").Invoke(sensors[player],null),Is.True,
                    "Changing menu axes must not redirect the original forward thrust.");
                Assert.That(sensors[player].GetType().GetMethod("TryConsumeThrust").Invoke(sensors[player],null),Is.False);
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator PhoneMenuGyroscopeCorrectionPreservesAlreadyCorrectSwordPose()
    {
        foreach(int player in new[]{0,1})
        {
            foreach(object state in Field<Array>(phone,"slots")){Set(state,"ChannelOpen",false);Set(state,"HasSample",false);}
            Array.Clear(Field<bool[]>(hardware,"valid"),0,2);
            foreach(MonoBehaviour sensor in sensors)Call(sensor,"Update");
            Quaternion reference=Quaternion.Euler(90,0,0);
            uint sequence=1;
            Supply(true,player,sequence++,reference,Vector3.zero,acceleration:Vector3.zero);
            if(!(bool)Get(flow,"PauseOpen"))Call(flow,"TogglePause");
            yield return new WaitForSecondsRealtime(.25f);
            Supply(true,player,sequence++,reference,Vector3.zero,acceleration:Vector3.zero);
            Call(flow,"OpenAxisCalibration");Call(panel,"SelectPlayer",player);
            // Orientation fusion correctly reports an upward pitch, while
            // this phone's rotationRate uses a different axis for that pitch.
            Quaternion raised=reference*Quaternion.Euler(-8,0,0);
            Supply(true,player,sequence++,raised,Vector3.down*400,acceleration:Vector3.zero);
            Quaternion originalPose=(Quaternion)Get(sensors[player],"CurrentLocalRotation");
            Vector3 originalGyro=(Vector3)Get(sensors[player],"AngularVelocityDegrees");
            var before=MenuSample(player);
            var monitor=new AxisDirectionMonitor();
            Assert.That(monitor.Read(before.rotation,before.gyro,before.gyroValid,true,before.time),Is.EqualTo(Vector2Int.left));
            Call(panel,"SelectPreset",2);Call(sensors[player],"Update");
            var after=MenuSample(player);monitor.Reset();
            Assert.That(monitor.Read(after.rotation,after.gyro,after.gyroValid,true,after.time),Is.EqualTo(Vector2Int.up));
            Assert.That(Quaternion.Angle((Quaternion)Get(sensors[player],"CurrentLocalRotation"),originalPose),Is.LessThan(.05f),
                "Fixing rotationRate for menus cannot rotate the already correct combat orientation.");
            Assert.That((Vector3)Get(sensors[player],"AngularVelocityDegrees"),Is.EqualTo(originalGyro));
            Assert.That((Quaternion)Get(sensors[player],"ReferenceRotation"),Is.EqualTo(reference));
            Call(panel,"Close");
        }
    }

    [UnityTest]
    public IEnumerator PresetChangesDoNotManufactureCursorMotionFromAStationaryPose()
    {
        foreach(int player in new[]{0,1})
        {
            foreach(object state in Field<Array>(phone,"slots")){Set(state,"ChannelOpen",false);Set(state,"HasSample",false);}
            Array.Clear(Field<bool[]>(hardware,"valid"),0,2);
            foreach(MonoBehaviour sensor in sensors)Call(sensor,"Update");
            uint sequence=1;
            Supply(false,player,sequence++,Quaternion.identity,Vector3.zero,acceleration:Vector3.zero);
            Call(sensors[player],"Recenter");
            Supply(false,player,sequence++,Quaternion.identity,Vector3.zero,acceleration:Vector3.zero);
            Vector2Int ReadMove()=>(Vector2Int)flow.GetType().GetMethod("ReadMove",Fields).Invoke(flow,new object[]{player,null});
            Assert.That(ReadMove(),Is.EqualTo(Vector2Int.zero));
            Quaternion stationary=Quaternion.Euler(37,29,16);
            Supply(false,player,sequence++,stationary,Vector3.zero,acceleration:Vector3.zero);ReadMove();
            Supply(false,player,sequence++,stationary,Vector3.zero,acceleration:Vector3.zero);
            Assert.That(ReadMove(),Is.EqualTo(Vector2Int.zero));
            Quaternion original=(Quaternion)Get(sensors[player],"CurrentLocalRotation");
            for(int preset=0;preset<6;preset++)
            {
                Call(sensors[player],"SetMenuAxisMapping",ControllerAxisMapping.FromPreset(preset));
                Assert.That(ReadMove(),Is.EqualTo(Vector2Int.zero),"Changing the coordinate basis is not a swing.");
                Supply(false,player,sequence++,stationary,Vector3.zero,acceleration:Vector3.zero);
                Assert.That(ReadMove(),Is.EqualTo(Vector2Int.zero),"The next stationary packet must not replay the old basis.");
                Assert.That(Get(sensors[player],"CurrentLocalRotation"),Is.EqualTo(original));
            }
        }
        yield return null;
    }

}
