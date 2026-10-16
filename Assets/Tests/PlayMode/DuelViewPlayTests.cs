using System.Collections;
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class DuelViewPlayTests
{
    [UnityTest]
    public IEnumerator WalkingViewShowsTheWholeCharacterAndReturnsToCombatFraming()
    {
        Type viewType = FindViewType();
        foreach(int fps in new[]{30,60,144})
        {
            var player=new GameObject("Walking player");player.transform.position=Vector3.up;
            var opponent=new GameObject("Walking focus");opponent.transform.position=player.transform.position+Vector3.forward*3f;
            var cameraObject=new GameObject("Walking camera",typeof(Camera));cameraObject.transform.SetParent(player.transform,false);
            Camera camera=cameraObject.GetComponent<Camera>();camera.aspect=16f/9f;camera.fieldOfView=60f;
            object view=Activator.CreateInstance(viewType,player.transform,opponent.transform,Array.Empty<Renderer>());
            try
            {
                viewType.GetMethod("SetFocusTarget").Invoke(view,new object[]{opponent.transform});
                viewType.GetMethod("Tick").Invoke(view,new object[]{0d});
                Vector3 normal=camera.transform.localPosition;
                viewType.GetMethod("SetWalkingView").Invoke(view,new object[]{true});
                for(int frame=1;frame<=fps/2;frame++)viewType.GetMethod("Tick").Invoke(view,new object[]{frame/(double)fps});
                foreach(Vector3 point in new[]{player.transform.position-Vector3.up,player.transform.position+Vector3.up*1.15f})
                {
                    Vector3 screen=camera.WorldToViewportPoint(point);
                    Assert.That(screen.z,Is.GreaterThan(0f));
                    Assert.That(screen.x,Is.InRange(.05f,.95f));
                    Assert.That(screen.y,Is.InRange(.05f,.95f),"Walking must show the head and feet at "+fps+" FPS.");
                }
                Assert.That(Vector3.Distance(camera.transform.localPosition,normal),Is.GreaterThan(1.5f));
                viewType.GetMethod("SetWalkingView").Invoke(view,new object[]{false});
                for(int frame=fps/2+1;frame<=fps*2;frame++)viewType.GetMethod("Tick").Invoke(view,new object[]{frame/(double)fps});
                Assert.That(Vector3.Distance(camera.transform.localPosition,normal),Is.LessThan(.01f));
                viewType.GetMethod("SetWalkingView").Invoke(view,new object[]{true});
                viewType.GetMethod("Reset").Invoke(view,null);
                Assert.That(Vector3.Distance(camera.transform.localPosition,normal),Is.LessThan(.01f),"Reset must clear the walking camera.");
            }
            finally
            {
                viewType.GetMethod("Dispose").Invoke(view,null);Object.Destroy(player);Object.Destroy(opponent);
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator RecoveryRollIsSmallFollowsBothLeanDirectionsAndKeepsTheOpponentInAim()
    {
        Type viewType = FindViewType();
        foreach (bool smooth in new[] { false, true })
        {
            var player = new GameObject("Recovery camera player");
            var opponent = new GameObject("Recovery camera opponent");
            var cameraObject = new GameObject("Recovery camera", typeof(Camera));
            cameraObject.transform.SetParent(player.transform, false);
            opponent.transform.position = new Vector3(.5f, 0f, 2f);
            object view = Activator.CreateInstance(viewType, player.transform, opponent.transform, Array.Empty<Renderer>());
            try
            {
                if (smooth) viewType.GetMethod("SetFocusTarget").Invoke(view, new object[] { opponent.transform });
                viewType.GetMethod("Tick").Invoke(view, new object[] { 0d });
                Quaternion upright = cameraObject.transform.rotation;
                Vector3 position = cameraObject.transform.position;
                foreach (float chestRoll in new[] { -40f, -20f, 20f, 40f })
                {
                    viewType.GetMethod("SetBodyRoll").Invoke(view, new object[] { chestRoll });
                    viewType.GetMethod("Tick").Invoke(view, new object[] { .2d });
                    float roll = Mathf.DeltaAngle(0f, (Quaternion.Inverse(upright) * cameraObject.transform.rotation).eulerAngles.z);
                    Assert.That(Mathf.Sign(roll), Is.EqualTo(Mathf.Sign(chestRoll)));
                    Assert.That(Mathf.Abs(roll), Is.InRange(3.5f, 7.01f), "The horizon should follow the body subtly instead of copying its full lean.");
                    Assert.That(Vector3.Angle(upright * Vector3.forward, cameraObject.transform.forward), Is.LessThan(.02f), "Roll must not change the opponent's aim direction.");
                    Assert.That(Vector3.Distance(position, cameraObject.transform.position), Is.LessThan(.00001f));
                }
                viewType.GetMethod("SetBodyRoll").Invoke(view, new object[] { 0f });
                viewType.GetMethod("Tick").Invoke(view, new object[] { .6d });
                Assert.That(Quaternion.Angle(upright, cameraObject.transform.rotation), Is.LessThan(.06f));
                viewType.GetMethod("SetBodyRoll").Invoke(view, new object[] { 40f });
                viewType.GetMethod("Reset").Invoke(view, null);
                Assert.That(Quaternion.Angle(upright, cameraObject.transform.rotation), Is.LessThan(.06f), "A new round must start with a level horizon.");
            }
            finally
            {
                viewType.GetMethod("Dispose").Invoke(view, null);
                Object.Destroy(player); Object.Destroy(opponent);
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator RecoveryPitchFollowsFrontAndBackLeanIsCappedAndSurvivesViewReplacement()
    {
        Type viewType = FindViewType();
        foreach (bool smooth in new[] { false, true })
        {
            var player = new GameObject("Pitch camera player");
            var replacement = new GameObject("Replacement camera player");
            var opponent = new GameObject("Pitch camera opponent");
            var cameraObject = new GameObject("Pitch camera", typeof(Camera));
            var replacementCamera = new GameObject("Replacement pitch camera", typeof(Camera));
            cameraObject.transform.SetParent(player.transform, false);
            replacementCamera.transform.SetParent(replacement.transform, false);
            opponent.transform.position = new Vector3(.5f, 0f, 2f);
            object view = Activator.CreateInstance(viewType, player.transform, opponent.transform, Array.Empty<Renderer>());
            object copied = Activator.CreateInstance(viewType, replacement.transform, opponent.transform, Array.Empty<Renderer>());
            try
            {
                if (smooth)
                    foreach (object current in new[] { view, copied })
                        viewType.GetMethod("SetFocusTarget").Invoke(current, new object[] { opponent.transform });
                viewType.GetMethod("Tick").Invoke(view, new object[] { 0d });
                Quaternion upright = cameraObject.transform.rotation;
                Vector3 position = cameraObject.transform.position;
                foreach (float chestPitch in new[] { -80f, -20f, 20f, 80f })
                {
                    viewType.GetMethod("SetBodyPitch").Invoke(view, new object[] { chestPitch });
                    viewType.GetMethod("Tick").Invoke(view, new object[] { .2d });
                    Quaternion relative = Quaternion.Inverse(upright) * cameraObject.transform.rotation;
                    float pitch = Mathf.DeltaAngle(0f, relative.eulerAngles.x);
                    Assert.That(pitch, Is.EqualTo(Mathf.Clamp(chestPitch * .1f, -4f, 4f)).Within(.01f));
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, relative.eulerAngles.y)), Is.LessThan(.01f));
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, relative.eulerAngles.z)), Is.LessThan(.01f));
                    Assert.That(Vector3.Distance(position, cameraObject.transform.position), Is.LessThan(.00001f), "Pitch changes the camera's tilt without moving its orbit.");
                }
                viewType.GetMethod("CopyReactionFrom").Invoke(copied, new[] { view, (object).2d });
                Assert.That(Quaternion.Angle(cameraObject.transform.rotation, replacementCamera.transform.rotation), Is.LessThan(.06f));
                viewType.GetMethod("SetBodyPitch").Invoke(view, new object[] { 0f });
                viewType.GetMethod("Tick").Invoke(view, new object[] { .6d });
                Assert.That(Quaternion.Angle(upright, cameraObject.transform.rotation), Is.LessThan(.06f), "Finishing recovery must restore the original aim.");
                viewType.GetMethod("Reset").Invoke(copied, null);
                Assert.That(Quaternion.Angle(upright, replacementCamera.transform.rotation), Is.LessThan(.06f), "Reset clears copied pitch.");
            }
            finally
            {
                viewType.GetMethod("Dispose").Invoke(view, null);
                viewType.GetMethod("Dispose").Invoke(copied, null);
                Object.Destroy(player); Object.Destroy(replacement); Object.Destroy(opponent);
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator SmoothCameraCarriesRepeatedCombatRootTranslationsWithoutFallingBehind()
    {
        Type viewType = FindViewType();
        var player = new GameObject("Recoil camera player");
        var opponent = new GameObject("Recoil camera opponent");
        var cameraObject = new GameObject("Recoil camera", typeof(Camera));
        cameraObject.transform.SetParent(player.transform, false);
        opponent.transform.position = player.transform.position + Vector3.forward * 3f;
        object view = Activator.CreateInstance(viewType, player.transform, opponent.transform, Array.Empty<Renderer>());
        try
        {
            viewType.GetMethod("SetFocusTarget").Invoke(view, new object[] { opponent.transform });
            viewType.GetMethod("Tick").Invoke(view, new object[] { 0d });
            Vector3 previousPlayer = player.transform.position;
            Vector3 previousCamera = cameraObject.transform.position;
            for (int hit = 1; hit <= 4; hit++)
            {
                Vector3 translation = Vector3.back * 3f;
                player.transform.position += translation;
                opponent.transform.position += translation;
                viewType.GetMethod("Tick").Invoke(view, new object[] { hit * .12d });
                Assert.That(Vector3.Distance(cameraObject.transform.position - previousCamera, translation), Is.LessThan(.01f),
                    "The camera must carry the combatants' recoil translation.");
                Assert.That(Vector3.Distance(player.transform.position - previousPlayer, translation), Is.LessThan(.001f));
                previousPlayer = player.transform.position;
                previousCamera = cameraObject.transform.position;
            }
        }
        finally
        {
            viewType.GetMethod("Dispose").Invoke(view, null);
            Object.Destroy(player); Object.Destroy(opponent);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator SingleOpponentHandoffAndBodyTurnRemainSmoothAtDifferentFrameRates()
    {
        Type viewType = FindViewType();
        foreach (int fps in new[] { 30, 60, 144 })
        {
            var player = new GameObject("Camera test player");
            var opponent = new GameObject("Camera test opponent");
            var next = new GameObject("Camera test next opponent");
            var cameraObject = new GameObject("Camera test view", typeof(Camera));
            cameraObject.transform.SetParent(player.transform, false);
            opponent.transform.position = Vector3.forward * 2f;
            next.transform.position = new Vector3(6f, 0f, -1f);
            object view = Activator.CreateInstance(viewType, player.transform, opponent.transform, Array.Empty<Renderer>());
            try
            {
                viewType.GetMethod("SetFocusTarget").Invoke(view, new object[] { opponent.transform });
                viewType.GetMethod("Tick").Invoke(view, new object[] { 0d });
                Quaternion initial = cameraObject.transform.rotation;
                Vector3 initialPosition = cameraObject.transform.position;
                viewType.GetMethod("SetFocusTarget").Invoke(view, new object[] { next.transform });
                player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                viewType.GetMethod("Tick").Invoke(view, new object[] { 1d / fps });
                Assert.That(Quaternion.Angle(initial, cameraObject.transform.rotation), Is.LessThan(12f), "No target-switch rotation snap.");
                Assert.That(Vector3.Distance(initialPosition, cameraObject.transform.position), Is.LessThan(.5f), "Body turning must not snap the camera orbit.");
                for (int frame = 2; frame <= fps; frame++)
                    viewType.GetMethod("Tick").Invoke(view, new object[] { (double)frame / fps });
                Quaternion expected = Quaternion.LookRotation(next.transform.position + Vector3.up - cameraObject.transform.position);
                Assert.That(Quaternion.Angle(expected, cameraObject.transform.rotation), Is.LessThan(.2f));
                Quaternion paused = cameraObject.transform.rotation;
                next.transform.position += Vector3.left * 5f;
                viewType.GetMethod("Tick").Invoke(view, new object[] { 1d });
                Assert.That(Quaternion.Angle(paused, cameraObject.transform.rotation), Is.LessThan(.001f), "Paused combat time freezes tracking.");
            }
            finally
            {
                viewType.GetMethod("Dispose").Invoke(view, null);
                Object.Destroy(player); Object.Destroy(opponent); Object.Destroy(next);
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator AttackerFollowThroughIsDirectionalSmoothDuringCombosAndReturnsToFraming()
    {
        Type type = FindViewType();
        foreach (bool smooth in new[] { false, true })
            foreach (int fps in new[] { 30, 60, 144 })
            {
                var player = new GameObject("Attacker camera player");
                var opponent = new GameObject("Attacker camera opponent");
                var cameraObject = new GameObject("Attacker camera", typeof(Camera));
                cameraObject.transform.SetParent(player.transform, false);
                Camera camera = cameraObject.GetComponent<Camera>();
                opponent.transform.position = Vector3.forward * 3f;
                object view = Activator.CreateInstance(type, player.transform, opponent.transform, Array.Empty<Renderer>());
                void Call(string method, params object[] args) => type.GetMethod(method).Invoke(view, args);
                try
                {
                    if (smooth) Call("SetFocusTarget", opponent.transform);
                    Call("Tick", 0d);
                    Vector3 rest = camera.transform.position;
                    Quaternion upright = camera.transform.rotation;
                    float restFov = camera.fieldOfView;
                    Call("AttackImpact", 0d, Vector3.right, .03f);
                    Call("Tick", .02d);
                    Assert.That(Vector3.Distance(camera.transform.position, rest), Is.LessThan(.001f), "Hitstop precedes follow-through.");
                    Call("Tick", .15d);
                    Assert.That(Vector3.Distance(camera.transform.position, rest), Is.InRange(.1f, .23f));
                    Assert.That(camera.fieldOfView, Is.GreaterThan(restFov + 1f));
                    float roll = Mathf.DeltaAngle(0f, (Quaternion.Inverse(upright) * camera.transform.rotation).eulerAngles.z);
                    Assert.That(roll, Is.LessThan(-.5f), "The attack camera follows the cut direction.");
                    Vector3 beforeCombo = camera.transform.position;
                    Call("AttackImpact", .15d, Vector3.left, .03f); Call("Tick", .15d);
                    Assert.That(Vector3.Distance(camera.transform.position, beforeCombo), Is.LessThan(.001f), "An immediate reverse cut must not reset the camera offset abruptly.");
                    for (int frame = 1; frame <= fps; frame++)
                    {
                        Vector3 shift = Vector3.forward * (3f / fps);
                        player.transform.position += shift; opponent.transform.position += shift;
                        Call("Tick", .15d + frame / (double)fps);
                        Vector3 offset = camera.transform.position - player.transform.position - rest;
                        Assert.That(offset.magnitude, Is.LessThan(.23f), "Camera feedback must stay bounded without recoil lag.");
                    }
                    Assert.That(Vector3.Distance(camera.transform.position - player.transform.position, rest), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(camera.transform.rotation, upright), Is.LessThan(.06f));
                    Assert.That(camera.fieldOfView, Is.EqualTo(restFov).Within(.001f));
                    Call("AttackImpact", 1.2d, Vector3.down, 0f); Call("Tick", 1.3d); Call("Reset");
                    Assert.That(camera.fieldOfView, Is.EqualTo(restFov).Within(.001f));
                }
                finally { Call("Dispose"); Object.Destroy(player); Object.Destroy(opponent); }
            }
        yield return null;
    }

    private static Type FindViewType()
    {
        Type type = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (type == null) type = assembly.GetType("DuelView");
        Assert.That(type, Is.Not.Null);
        return type;
    }
}
