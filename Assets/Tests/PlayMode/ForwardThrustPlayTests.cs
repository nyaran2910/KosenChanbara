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

public sealed class ForwardThrustPlayTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private MonoBehaviour master, flow;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    private static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub", "MotionController" }.Contains(component.GetType().Name)) component.enabled = false;
    }

    [UnitySetUp]
    public IEnumerator Setup()
    {
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        var components = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        master = components.Single(component => component.GetType().Name == "MasterController");
        flow = components.Single(component => component.GetType().Name == "GameFlowController");
        master.enabled = flow.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub" }.Contains(component.GetType().Name)) Object.Destroy(component.gameObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator NearForwardSensorImpulsesCenterBothSlotsInEveryCombatMode()
    {
        object[] players = { Field<object>(master, "first"), Field<object>(master, "second") };
        object[] motions = players.Select(player => Field<object>(player, "Motion")).ToArray();
        object hardware = Field<object>(motions[0], "hardware"), phone = Field<object>(motions[0], "hub");
        Array latest = Field<Array>(hardware, "latest"), slots = Field<Array>(phone, "slots");
        bool[] hardwareValid = Field<bool[]>(hardware, "valid");
        Type sampleType = Type.GetType("MotionSample, Assembly-CSharp", true);
        uint[] sequence = { 0, 0 };
        foreach (string mode in new[] { "Duel", "Free", "Single" })
        foreach (bool phoneInput in new[] { false, true })
        foreach (int sourceSlot in new[] { 0, 1 })
        {
            if (mode == "Free") Call(flow, "ChooseFreeMode");
            else Call(flow, "ChooseMode", mode == "Single");
            Call(flow, "Ready", 0);
            if (mode != "Single") Call(flow, "Ready", 1);
            Call(flow, "BeginMatch");
            Set(master, "matchTime", Field<double>(master, "matchTime") + 3d);
            Call(Field<object>(master, "series"), "Enter", RoundPhase.Fight, Field<double>(master, "matchTime"));
            Call(master, "SetDisplayMode", 2);
            object hero = players[mode == "Single" ? 0 : sourceSlot];
            Transform root = Field<Transform>(hero, "Player");
            for (int slot = 0; slot < 2; slot++)
            {
                Set(slots.GetValue(slot), "ChannelOpen", phoneInput);
                Set(players[slot], "ThrustAt", -100d);
                Set(motions[slot], "thrustPending", false);
                Call(Field<object>(motions[slot], "thrustGesture"), "Reset");
                Set(motions[slot], "hasReference", false);
            }
            Quaternion near = Quaternion.Euler(-9f, sourceSlot == 0 ? 18f : -18f, 0f);
            void Supply(bool calibrating, bool pushing)
            {
                for (int slot = 0; slot < 2; slot++)
                {
                    uint next = ++sequence[slot];
                    Quaternion local = slot == sourceSlot ? near : Quaternion.Euler(-90f, 0f, 0f);
                    Quaternion rotation = calibrating ? Quaternion.identity : Quaternion.Euler(90f, 0f, 0f) * local;
                    Vector3 acceleration = pushing && slot == sourceSlot ? Quaternion.Inverse(local) * (Vector3.forward * 5f) : Vector3.zero;
                    object sample = Activator.CreateInstance(sampleType, new object[] {
                        rotation, Vector3.zero, acceleration, false, true, true, true,
                        next, (ushort)0, next * 20d, Time.realtimeSinceStartupAsDouble
                    });
                    latest.SetValue(sample, slot); hardwareValid[slot] = true;
                    Set(slots.GetValue(slot), "LatestSample", sample); Set(slots.GetValue(slot), "HasSample", true);
                    Call(motions[slot], "Update");
                }
            }
            CombatInput Read(float dt)
            {
                Set(master, "matchTime", Field<double>(master, "matchTime") + dt);
                return (CombatInput)Call(master, "ReadInput", hero, hero == players[0], true);
            }
            Supply(true, false); Read(.01f);
            for (int frame = 0; frame < 10; frame++) { Supply(false, false); Read(.01f); }
            Supply(false, true); Read(.01f);
            Supply(false, true); CombatInput start = Read(.01f);
            Assert.That(start.ThrustRequested, Is.True, mode + " / phone=" + phoneInput + " / slot=" + sourceSlot);
            Assert.That(start.ForwardAngle, Is.Zero);
            var gate = new AttackStroke();
            CombatPose beginning = (CombatPose)Call(master, "PlayerCombatPose", hero, start, false);
            Assert.That(gate.Update(start, beginning, Field<double>(master, "matchTime")), Is.False, "Chamber and centering cannot damage.");
            Supply(false, false); CombatInput drive = Read(.05f);
            CombatPose driven = (CombatPose)Call(master, "PlayerCombatPose", hero, drive, false);
            Assert.That(gate.Update(drive, driven, Field<double>(master, "matchTime")), Is.True);
            Assert.That(Vector3.Angle(driven.Blade, root.forward), Is.LessThan(.01f));
            Assert.That(Field<SwordPose>(hero, "TargetPose").Position.z, Is.GreaterThan(Field<SwordPose>(hero, "ThrustFrom").Position.z + .1f));
            Assert.That(drive.AngularSpeed, Is.Zero, "Automatic centering cannot become a slash.");
            Supply(false, false); CombatInput returning = Read(.1f);
            CombatPose restored = (CombatPose)Call(master, "PlayerCombatPose", hero, returning, false);
            Assert.That(gate.Update(returning, restored, Field<double>(master, "matchTime")), Is.False, "The return cannot damage either.");
            Supply(false, false); CombatInput recovered = Read(.2f);
            Assert.That(recovered.ThrustRequested, Is.False);
            SwordPose pose = Field<SwordPose>(hero, "TargetPose");
            Assert.That(Vector3.Angle(pose.Direction, Vector3.forward), Is.GreaterThan(10f), "The player's normal input is restored after the thrust.");
        }
        yield return null;
    }
}
