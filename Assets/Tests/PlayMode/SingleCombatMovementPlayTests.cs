using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class SingleCombatMovementPlayTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    MonoBehaviour master,flow;
    static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,Flags).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
    Transform Player=>Field<GameObject>(master,"PlayerObject1").transform;
    static void Disable(Scene s,LoadSceneMode mode)
    {
        foreach(var c in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if(new[]{"PhoneControllerHub","HardwareControllerHub","MotionController"}.Contains(c.GetType().Name))c.enabled=false;
    }
    [UnitySetUp] public IEnumerator Setup()
    {
        SceneManager.sceneLoaded+=Disable;yield return SceneManager.LoadSceneAsync("BattleGround");SceneManager.sceneLoaded-=Disable;yield return null;
        master=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="MasterController");
        flow=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="GameFlowController");
        master.enabled=flow.enabled=false;Call(flow,"ChooseMode",true);Call(flow,"Ready",0);
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        SceneManager.sceneLoaded-=Disable;
        foreach(var c in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if(new[]{"PhoneControllerHub","HardwareControllerHub"}.Contains(c.GetType().Name))Object.Destroy(c.gameObject);
        yield return null;
    }
    object Spawn(Vector3 offset)
    {
        Call(master,"SpawnSingleNpc");IList all=Field<IList>(master,"singleNpcs");object npc=all[all.Count-1];
        Transform root=Field<GameObject>(npc,"Root").transform;root.position=Player.position+offset;root.LookAt(Player.position);
        Set(npc,"WaitingAnchor",root.position);Set(npc,"Hearts",3);Set(npc,"MaximumHearts",3);Set(npc,"NextAttack",100d);
        return npc;
    }
    void Contact(object npc,CombatResult result,Vector3 cut)
    {
        object player=Field<object>(master,"first");CombatPose p=(CombatPose)Call(master,"PlayerDefencePose",player);
        CombatPose e=(CombatPose)Call(master,"NpcCombatPose",npc);
        p=new CombatPose(p.Grip,p.Blade,p.Grip,p.Blade,p.BodyCenter,p.Forward,cut,p.EndGrip,p.EndBlade);
        e=new CombatPose(e.Grip,e.Blade,e.Grip,e.Blade,e.BodyCenter,e.Forward,cut,e.EndGrip,e.EndBlade);
        Call(master,"ApplySingleContact",npc,result,p,e,false);
    }
    [UnityTest] public IEnumerator HitsAndBlocksMoveTheWholePairLikeDuelAndPreserveHitstop()
    {
        foreach(var sample in new[]{(CombatOutcome.Hit,0,1,3f),(CombatOutcome.Hit,1,0,-3f),
            (CombatOutcome.GuardSuccess,0,0,-2f),(CombatOutcome.GuardSuccess,1,1,2f)})
        {
            Call(master,"ResetMatch");object npc=Spawn(Player.forward*2f);Set(master,"singleActiveNpc",npc);
            Transform root=Field<GameObject>(npc,"Root").transform;Vector3 a=Player.position,b=root.position,forward=Player.forward;
            double now=Field<double>(master,"matchTime");var result=new CombatResult(sample.Item1,sample.Item2,sample.Item3,sample.Item4);
            Contact(npc,result,Vector3.down);
            Assert.That(Player.position,Is.EqualTo(a));Assert.That(root.position,Is.EqualTo(b));
            Call(master,"AdvanceSingleReaction",now+result.HitStopSeconds*.5);
            Assert.That(Player.position,Is.EqualTo(a));Assert.That(root.position,Is.EqualTo(b));
            Call(master,"AdvanceSingleReaction",now+result.HitStopSeconds+DuelMatch.PushSeconds*.5);
            float playerSeconds=Field<float>(master,"singlePlayerPushSeconds");
            float npcSeconds=Field<float>(master,"singleNpcPushSeconds");
            Assert.That(Vector3.Distance(Player.position,a+forward*sample.Item4*CombatAdvance.Blend(DuelMatch.PushSeconds*.5,playerSeconds)),Is.LessThan(.001f));
            Call(master,"AdvanceSingleReaction",now+result.HitStopSeconds+Mathf.Max(playerSeconds,npcSeconds)+.001d);
            Assert.That(Vector3.Distance(Player.position,a+forward*sample.Item4),Is.LessThan(.001f));
            Assert.That(Vector3.Distance(root.position,b+forward*sample.Item4),Is.LessThan(.001f));
            Assert.That(Vector3.Distance(Player.position,root.position),Is.EqualTo(2f).Within(.001f));
        }
        yield return null;
    }
    [UnityTest] public IEnumerator BothModesAdvanceTheStrikerWithVisibleStepsInsteadOfTeleporting()
    {
        foreach(bool single in new[]{true,false})
            foreach(int attacker in new[]{0,1})
                foreach(int fps in new[]{30,60,144})
                {
                    Call(flow,"ReturnToTitle");Call(flow,"ChooseMode",single);
                    Call(flow,"Ready",0);if(!single)Call(flow,"Ready",1);
                    object npc=single?Spawn(Player.forward*3f):null;
                    if(single)Set(master,"singleActiveNpc",npc);
                    Transform other=single?Field<GameObject>(npc,"Root").transform:Field<GameObject>(master,"PlayerObject2").transform;
                    Transform striker=attacker==0?Player:other,defender=attacker==0?other:Player;
                    object presentation=Field<object>(master,"presentation");
                    object avatar=attacker==0||!single?Field<Array>(presentation,"avatars").GetValue(attacker):Field<object>(npc,"Avatar");
                    Call(avatar,"Reset");Call(avatar,"Tick",0d,1f/fps,Quaternion.Euler(-90,0,0),false,0f);
                    Vector3 start=striker.position,defenderStart=defender.position,previous=start;
                    double now=Field<double>(master,"matchTime");
                    var hit=new CombatResult(CombatOutcome.Hit,attacker,1-attacker,attacker==0?3f:-3f);
                    if(single)Contact(npc,hit,Vector3.down);else Call(master,"BeginReaction",hit,now);
                    Array legs=Field<Array>(avatar,"legs");bool[] moved=new bool[2];
                    float floor=Field<Transform>(legs.GetValue(0),"Ankle").position.y;
                    float lift=0;
                    for(int frame=1;frame<=fps*2;frame++)
                    {
                        double time=now+frame/(double)fps;
                        Call(master,single?"AdvanceSingleReaction":"AdvanceReaction",time);
                        Assert.That(Vector3.Distance(striker.position,previous),Is.LessThanOrEqualTo(CombatAdvance.MaximumSpeed/fps+.002f),"Actual striker root must move at a readable pace.");
                        previous=striker.position;
                        Call(avatar,"Tick",time,1f/fps,Quaternion.Euler(-90,0,0),false,0f);
                        for(int i=0;i<legs.Length;i++)
                        {
                            moved[i]|=Field<bool>(legs.GetValue(i),"Moving");
                            lift=Mathf.Max(lift,Field<Transform>(legs.GetValue(i),"Ankle").position.y-floor);
                            Assert.That(Vector3.Distance(Field<Transform>(legs.GetValue(i),"Ankle").position,Field<Vector3>(legs.GetValue(i),"WorldPlant")),
                                Is.LessThan(.006f),$"single={single}, attacker={attacker}, {fps}fps, frame {frame}: visible feet must reach their contacts throughout the real follow.");
                        }
                        if(frame==Mathf.CeilToInt((hit.HitStopSeconds+.46f)*fps))
                            Assert.That(Vector3.Distance(striker.position,start),Is.EqualTo(3f).Within(.002f),"A single hit's follow must finish within .45s after hitstop.");
                        if(time>=now+hit.HitStopSeconds+DuelMatch.PushSeconds && frame==Mathf.CeilToInt((hit.HitStopSeconds+DuelMatch.PushSeconds)*fps))
                            Assert.That(Vector3.Distance(defender.position,defenderStart),Is.EqualTo(3f).Within(.002f),"The struck fighter keeps its original recoil timing.");
                    }
                    Assert.That(Vector3.Distance(striker.position,start),Is.EqualTo(3f).Within(.002f));
                    Assert.That(moved[0]&&moved[1],Is.True);Assert.That(lift,Is.GreaterThan(.05f));
                }
        yield return null;
    }

    [UnityTest] public IEnumerator APlayerHitTriggersSharedAttackerCameraFollowThrough()
    {
        object npc=Spawn(Player.forward*3f); Set(master,"singleActiveNpc",npc);
        object presentation=Field<object>(master,"presentation");
        object view=Field<Array>(presentation,"views").GetValue(0);
        double now=Field<double>(master,"matchTime");
        Contact(npc,new CombatResult(CombatOutcome.Hit,0,1,3f),Player.right);
        Assert.That(Field<double>(view,"attackImpactStart"),Is.GreaterThanOrEqualTo(now));
        Call(view,"Tick",now+.15d);
        Assert.That(Player.GetComponentInChildren<Camera>().fieldOfView,Is.GreaterThan(Field<float>(view,"baseFov")+1f),
            "Single mode must send the same attacker follow-through as duel.");
        yield return null;
    }

    [UnityTest] public IEnumerator LateralRecoilMovesOnlyThePushedFighterAndTheCrowdFollowsTheBattle()
    {
        object active=Spawn(Player.forward*2f);Set(master,"singleActiveNpc",active);
        Spawn(Player.right*7f);object left=Spawn(-Player.right*7f),right=Spawn(Player.right*8f+Player.forward*3f);
        Set(left,"WaitingFollowSpeed",2f);Set(right,"WaitingFollowSpeed",4f);
        Call(master,"UpdateSingleCrowdCenter");Vector3 leftAnchor=Field<Vector3>(left,"WaitingAnchor"),rightAnchor=Field<Vector3>(right,"WaitingAnchor");
        Transform enemy=Field<GameObject>(active,"Root").transform;
        Vector3 a=Player.position,b=enemy.position,forward=Player.forward,side=Player.right;
        double now=Field<double>(master,"matchTime");Contact(active,new CombatResult(CombatOutcome.Hit,0,1,3f),side);
        Call(master,"AdvanceSingleReaction",now+1d);Call(master,"UpdateSingleCrowdCenter");
        Assert.That(Vector3.Distance(Player.position,a+forward*3f),Is.LessThan(.001f));
        Assert.That(Vector3.Distance(enemy.position,b+forward*3f+side*.6f),Is.LessThan(.001f));
        Vector3 shift=forward*3f+side*(.6f*.35f);
        Vector3 leftStep=Field<Vector3>(left,"WaitingAnchor")-leftAnchor;
        Vector3 rightStep=Field<Vector3>(right,"WaitingAnchor")-rightAnchor;
        Assert.That(Vector3.Dot(leftStep,shift),Is.GreaterThan(0f));
        Assert.That(Vector3.Dot(rightStep,shift),Is.GreaterThan(Vector3.Dot(leftStep,shift)),
            "Waiting fighters follow the moving battle at their own pace.");
        Assert.That(leftStep.magnitude,Is.LessThan(shift.magnitude));
        Vector3 oldLeft=Field<GameObject>(left,"Root").transform.position;
        Set(master,"matchTime",now+.2d);Call(master,"MoveSingleNpcs",.1f);
        Assert.That(Vector3.Distance(Field<GameObject>(left,"Root").transform.position,oldLeft),Is.GreaterThan(.01f));
        Assert.That(Field<float>(left,"AttackTimer"),Is.LessThan(0f));
        yield return null;
    }
    [UnityTest] public IEnumerator RootKnockbackDoesNotBecomeASyntheticSwordSweep()
    {
        object player=Field<object>(master,"first");
        Transform sword=Field<Transform>(player,"Sword");
        Set(player,"CombatBaseline",false);
        Set(player,"TargetPose",new SwordPose(sword.localPosition,sword.localRotation));
        var input=new CombatInput(0f,0f,false,true,711);
        Call(master,"PlayerCombatPose",player,input,false);
        Player.position+=Player.forward*.9f;
        CombatPose moved=(CombatPose)Call(master,"PlayerCombatPose",player,input,false);
        Assert.That(Vector3.Distance(moved.PreviousGrip,moved.Grip),Is.LessThan(.0001f));
        Assert.That(Vector3.Angle(moved.PreviousBlade,moved.Blade),Is.LessThan(.01f));
        yield return null;
    }
    [UnityTest] public IEnumerator AnIncomingEnemyBacksOutIfTheMovingBattleEntersItsSpace()
    {
        object npc=Spawn(Player.forward*.5f);Set(master,"singleActiveNpc",npc);
        Transform root=Field<GameObject>(npc,"Root").transform;
        for(int step=0;step<10;step++)
        {
            Vector3 before=root.position;float oldGap=Vector3.Distance(before,Player.position);
            Call(master,"AdvanceSingleNpc",npc,.1f,2.25f,1.8f);
            Assert.That(Vector3.Distance(before,root.position),Is.LessThanOrEqualTo(.226f),"Retreat must stay smooth rather than teleporting through the crowd.");
            if(oldGap<1.5f)Assert.That(Vector3.Distance(root.position,Player.position),Is.GreaterThan(oldGap));
        }
        Assert.That(Vector3.Distance(root.position,Player.position),Is.GreaterThanOrEqualTo(1.5f));
        yield return null;
    }

}
