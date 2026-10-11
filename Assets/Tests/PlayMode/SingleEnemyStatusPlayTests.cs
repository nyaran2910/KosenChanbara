using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class SingleEnemyStatusPlayTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Call(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, Fields).Invoke(target, args);

    private sealed class CueFixture : IDisposable
    {
        public readonly GameObject Enemy = new GameObject("Preparation cue test enemy");
        public readonly Camera Camera;
        public readonly Component Status;
        public CueFixture()
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("SingleEnemyStatus")).First(found => found != null);
            var label = new GameObject("Preparation cue test status", typeof(RectTransform));
            label.transform.SetParent(Enemy.transform, false);
            label.transform.localPosition = Vector3.up * 1.4f;
            Status = label.AddComponent(type);
            Call(Status, "Initialize", 3);
            Camera = new GameObject("Preparation cue test camera", typeof(Camera)).GetComponent<Camera>();
            Camera.enabled = false; Camera.fieldOfView = 60f;
            Camera.pixelRect = new Rect(0f, 0f, 1280f, 720f);
            Enemy.transform.position = new Vector3(.3f, 0f, 3f);
            Enemy.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }
        public Vector3 CuePosition => Enemy.transform.position + Vector3.up * 1.25f + Enemy.transform.forward * .6f;
        public void Dispose() { Object.Destroy(Enemy); Object.Destroy(Camera.gameObject); }
    }

    [UnityTest]
    public IEnumerator PreparationFlashAndActiveOpponentCueRemainWithoutCutDirectionImages()
    {
        using (var fixture = new CueFixture())
        {
            Call(fixture.Status, "Activate", 2d);
            fixture.Camera.transform.position = new Vector3(3f, 2.5f, -4f);
            fixture.Camera.transform.rotation = Quaternion.LookRotation(fixture.CuePosition - fixture.Camera.transform.position)
                * Quaternion.AngleAxis(31f, Vector3.forward);
            foreach (float progress in new[] { 0f, .5f, .95f })
            {
                Call(fixture.Status, "Refresh", 2, progress, true, fixture.CuePosition, 2d + progress * .5d);
                Call(fixture.Status, "FaceCamera", fixture.Camera);
                Canvas cue = Field<Canvas>(fixture.Status, "attackCue");
                Assert.That(cue.enabled, Is.True);
                Assert.That(Field<Canvas>(fixture.Status, "activationCue").enabled, Is.True);
                Assert.That(Field<Image>(fixture.Status, "cueCore").color.a, Is.GreaterThan(.6f));
                Assert.That(Field<Image>(fixture.Status, "cueCenter").color.a, Is.GreaterThan(.6f));
                Assert.That(cue.GetComponentsInChildren<Image>(true).Length, Is.EqualTo(2), "Only the preparation spark and bright center remain, without a direction arrow or trail.");
                Assert.That(Vector3.Angle(cue.transform.forward, fixture.Camera.transform.forward), Is.LessThan(.02f));
                Assert.That(Field<Image[]>(fixture.Status, "hearts").Length, Is.EqualTo(3));
                Assert.That(Field<AudioSource>(fixture.Status, "activationAudio"), Is.Not.Null);
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator MovingEnemyReusesFlashAndCompletedOrInactivePreparationsHideIt()
    {
        using (var fixture = new CueFixture())
        {
            fixture.Camera.transform.position = new Vector3(0f, 1.5f, -5f);
            fixture.Camera.transform.LookAt(new Vector3(0f, 1f, 3f));
            int imageCount = fixture.Enemy.GetComponentsInChildren<Image>(true).Length;
            foreach (int fps in new[] { 30, 60, 144 })
                for (int frame = 0; frame <= fps / 2; frame++)
                {
                    float progress = frame * 2f / fps;
                    fixture.Enemy.transform.position = new Vector3(progress * 1.3f, 0f, 3f + progress);
                    fixture.Enemy.transform.rotation = Quaternion.Euler(0f, 180f + progress * 70f, 0f);
                    Call(fixture.Status, "Refresh", 1, progress, true, fixture.CuePosition, 3d + frame / (double)fps);
                    Call(fixture.Status, "FaceCamera", fixture.Camera);
                    Assert.That(Field<Canvas>(fixture.Status, "attackCue").enabled, Is.True);
                    Assert.That(fixture.Enemy.GetComponentsInChildren<Image>(true).Length, Is.EqualTo(imageCount));
                }
            Call(fixture.Status, "Refresh", 1, -1f, true, fixture.CuePosition, 4d);
            Assert.That(Field<Canvas>(fixture.Status, "attackCue").enabled, Is.False);
            Call(fixture.Status, "Refresh", 1, .5f, false, fixture.CuePosition, 4.1d);
            Assert.That(Field<Canvas>(fixture.Status, "attackCue").enabled, Is.False);
            Assert.That(Field<Canvas>(fixture.Status, "activationCue").enabled, Is.False);
            Call(fixture.Status, "Refresh", 1, .5f, true, fixture.CuePosition, 4.2d);
            Assert.That(Field<Canvas>(fixture.Status, "attackCue").enabled, Is.True);
        }
        yield return null;
    }
}
