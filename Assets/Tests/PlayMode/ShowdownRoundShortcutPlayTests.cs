using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class ShowdownRoundShortcutPlayTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    MonoBehaviour master, flow;
    Keyboard keyboard;
    Keyboard[] physical;
    InputSettings.BackgroundBehavior previousBackground;
    InputSettings.EditorInputBehaviorInPlayMode previousEditor;
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target,args);
    static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (var c in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] {"PhoneControllerHub","HardwareControllerHub","MotionController"}.Contains(c.GetType().Name)) c.enabled=false;
    }
    [UnitySetUp] public IEnumerator Setup()
    {
        previousBackground=InputSystem.settings.backgroundBehavior;
        previousEditor=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        physical=InputSystem.devices.OfType<Keyboard>().Where(k=>k.enabled).ToArray();
        foreach(var k in physical) InputSystem.DisableDevice(k);
        keyboard=InputSystem.AddDevice<Keyboard>();
        SceneManager.sceneLoaded+=DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded-=DisableTransport;yield return null;
        var all=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        master=all.Single(c=>c.GetType().Name=="MasterController");
        flow=all.Single(c=>c.GetType().Name=="GameFlowController");
        master.enabled=flow.enabled=false;
        keyboard.MakeCurrent();
        Call(flow,"ChooseMode",true);Call(flow,"Ready",0);Call(flow,"BeginMatch");
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior=previousBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode=previousEditor;
        foreach(var k in physical)if(k.added)InputSystem.EnableDevice(k);
        foreach(var c in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if(new[]{"PhoneControllerHub","HardwareControllerHub"}.Contains(c.GetType().Name))Object.Destroy(c.gameObject);
        yield return null;
    }
    void Press(Key key)
    {
        keyboard.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));InputSystem.Update();
        Call(master,"LateUpdate");
    }
    [UnityTest] public IEnumerator EveryDigitStartsItsWaveAndNineStartsWithNinetyDefeats()
    {
        Key[] keys={Key.Digit0,Key.Digit1,Key.Digit2,Key.Digit3,Key.Digit4,Key.Digit5,Key.Digit6,Key.Digit7,Key.Digit8,Key.Digit9};
        foreach(int digit in Enumerable.Range(0,10))
        {
            Press(keys[digit]);
            var progress=Field<ShowdownProgress>(master,"showdown");
            Assert.That(progress.Defeated,Is.EqualTo(digit*10));
            Assert.That(progress.Spawned,Is.EqualTo((digit+1)*10));
            Assert.That(master.GetType().GetProperty("SingleSection").GetValue(master),Is.EqualTo(digit+1));
            Assert.That(Field<IList>(master,"singleNpcs").Count,Is.EqualTo(10));
            Assert.That(progress.Hearts,Is.EqualTo(10));
            Assert.That(Field<bool>(master,"singlePushActive"),Is.False);
            yield return null;
        }
        foreach(object npc in Field<IList>(master,"singleNpcs"))Call(master,"RemoveSingleNpc",npc);
        Assert.That(Field<ShowdownProgress>(master,"showdown").Cleared,Is.True);
        Press(Key.Numpad0);
        Assert.That(Field<ShowdownProgress>(master,"showdown").Defeated,Is.Zero);
        Assert.That(Field<IList>(master,"singleNpcs").Count,Is.EqualTo(10));
    }
    [UnityTest] public IEnumerator PauseAndDuelIgnoreTheShortcutAndDefeatCanBeRestartedAtAnyWave()
    {
        Call(flow,"TogglePause");Press(Key.Digit9);
        Assert.That(Field<ShowdownProgress>(master,"showdown").Defeated,Is.Zero);
        Call(flow,"TogglePause");
        var progress=Field<ShowdownProgress>(master,"showdown");
        for(int hit=0;hit<5;hit++)progress.Damage(hit*.25);
        Call(master,"BeginSingleDefeat",Field<double>(master,"matchTime"));
        Press(Key.Digit5);
        Assert.That(progress.Defeated,Is.EqualTo(50));
        Assert.That(progress.Hearts,Is.EqualTo(10));
        Assert.That(Field<bool>(master,"singleGameOver"),Is.False);
        Call(flow,"ChooseMode",false);Call(flow,"Ready",0);Call(flow,"Ready",1);Call(flow,"BeginMatch");
        Vector3 position=Field<GameObject>(master,"PlayerObject1").transform.position;
        Press(Key.Digit9);
        Assert.That(Field<ShowdownProgress>(master,"showdown").Defeated,Is.Zero);
        Assert.That(Field<GameObject>(master,"PlayerObject1").transform.position,Is.EqualTo(position));
        yield return null;
    }

    [UnityTest] public IEnumerator TheOpponentStopsAtDuelDistanceAndAdvertisesItsActualLaneFromTheFirstFrame()
    {
        Press(Key.Digit0);
        var enemies=Field<IList>(master,"singleNpcs");object npc=enemies[0];
        Transform player=Field<GameObject>(master,"PlayerObject1").transform;
        Transform root=Field<GameObject>(npc,"Root").transform;
        foreach(object other in enemies)
            if(other!=npc) Field<GameObject>(other,"Root").transform.position=player.position+Vector3.right*20f+Vector3.forward*20f;
        root.SetPositionAndRotation(player.position+player.forward*6f,Quaternion.LookRotation(-player.forward));
        Call(Field<object>(npc,"Avatar"),"Reset");
        float distance=(float)master.GetType().GetProperty("FightingDistance",Flags).GetValue(master);
        for(int frame=0;frame<200;frame++)Call(master,"AdvanceSingleNpc",npc,.01f,3f,-1f);
        Assert.That(Vector3.Distance(root.position,player.position),Is.EqualTo(distance).Within(.001f));
        npc.GetType().GetField("GuardOctant",Flags).SetValue(npc,0);
        npc.GetType().GetField("NextAttack",Flags).SetValue(npc,0d);
        npc.GetType().GetField("Engaged",Flags).SetValue(npc,true);
        master.GetType().GetField("singleActiveNpc",Flags).SetValue(master,npc);
        Call(master,"UpdateNpcGuard",npc,0f);
        Camera view=player.GetComponentInChildren<Camera>();
        view.transform.position=player.TransformPoint(new Vector3(.35f,1.6f,-3.4f));view.transform.LookAt(root.position+Vector3.up);
        Call(master,"MoveSingleNpcs",.01f);
        Vector3 advertised=Field<Vector3>(npc,"AttackReadyDirection");
        Assert.That(Vector3.Angle(advertised,Vector3.up),Is.LessThan(4.01f),"An overhead guard cannot announce a right-side cut without first preparing that side.");
        Vector3 previousBlade=Field<SwordPose>(npc,"VisualBasePose").Direction;
        Assert.That(Vector3.Angle(previousBlade,advertised),Is.LessThan(8f),"The announced lane is reached through a smooth preparation.");
        for(int frame=0;frame<50;frame++)
        {
            master.GetType().GetField("matchTime",Flags).SetValue(master,Field<double>(master,"matchTime")+.01d);
            Call(master,"MoveSingleNpcs",.01f);
            Vector3 ready=Field<Vector3>(npc,"AttackReadyDirection");
            Assert.That(Vector3.Angle(ready,advertised),Is.LessThan(4.01f));
            Vector3 blade=Field<SwordPose>(npc,"VisualBasePose").Direction;
            Assert.That(Vector3.Angle(previousBlade,blade),Is.LessThan(8f),"Preparation advances continuously toward the announced lane.");
            previousBlade=blade;
        }
        Assert.That(Field<float>(npc,"AttackTimer"),Is.GreaterThan(Field<float>(npc,"SwingSeconds")),
            "The normal first-wave windup is still in progress after these half-second samples; its visible blade may still be approaching the announced lane.");
        for (int frame = 0; frame < 1200 && Field<float>(npc,"AttackTimer") > Field<float>(npc,"SwingSeconds") + .002f; frame++)
        {
            master.GetType().GetField("matchTime",Flags).SetValue(master,Field<double>(master,"matchTime")+.001d);
            Call(master,"MoveSingleNpcs",.001f);
        }
        Vector3 preparedBlade = root.TransformDirection(Field<SwordPose>(npc,"VisualBasePose").Direction);
        Assert.That(Vector3.Angle(preparedBlade,Field<Vector3>(npc,"AttackStart")),Is.LessThan(.5f),
            "The visible blade reaches the actual committed start pose before the swing begins.");
        yield return null;
    }

    [UnityTest] public IEnumerator PlayerAndNpcIgnoreRepeatedBodyContactsForExactlyTheSameTwoTenths()
    {
        Press(Key.Digit0);
        object npc=Field<IList>(master,"singleNpcs")[0],hero=Field<object>(master,"first");
        npc.GetType().GetField("Hearts",Flags).SetValue(npc,3);
        CombatPose player=(CombatPose)Call(master,"PlayerDefencePose",hero),enemy=(CombatPose)Call(master,"NpcCombatPose",npc);
        double now=Field<double>(master,"matchTime");
        void At(double elapsed)=>master.GetType().GetField("matchTime",Flags).SetValue(master,now+elapsed);
        var hitNpc=new CombatResult(CombatOutcome.Hit,0,1,DuelMatch.HitPush);
        Call(master,"ApplySingleContact",npc,hitNpc,player,enemy,false);
        At(.1d);Call(master,"ApplySingleContact",npc,hitNpc,player,enemy,false);
        Assert.That(Field<int>(npc,"Hearts"),Is.EqualTo(2));
        At(.2d);Call(master,"ApplySingleContact",npc,hitNpc,player,enemy,false);
        Assert.That(Field<int>(npc,"Hearts"),Is.EqualTo(1));
        var hitPlayer=new CombatResult(CombatOutcome.Hit,1,0,-DuelMatch.HitPush);
        At(.3d);Call(master,"ApplySingleContact",npc,hitPlayer,player,enemy,false);
        At(.4d);Call(master,"ApplySingleContact",npc,hitPlayer,player,enemy,false);
        Assert.That(Field<ShowdownProgress>(master,"showdown").Hearts,Is.EqualTo(8));
        At(.5d);Call(master,"ApplySingleContact",npc,hitPlayer,player,enemy,false);
        Assert.That(Field<ShowdownProgress>(master,"showdown").Hearts,Is.EqualTo(6));
        yield return null;
    }
}
