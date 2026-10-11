using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Health stays overhead; a brief spark at the blade announces the actual cut.
public sealed class SingleEnemyStatus : MonoBehaviour
{
    private Image[] hearts;
    private Canvas canvas;
    private Canvas attackCue;
    private Image cueCore;
    private Image cueCenter;
    private bool attackAnnounced;
    private Canvas activationCue;
    private Image[] activationRing;
    private double activatedAt = double.NegativeInfinity;
    private AudioSource activationAudio;
    private static AudioClip activationSound;
    private static AudioClip attackSound;

    public void Initialize(int maximumHearts)
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;
        var rect = (RectTransform)transform;
        rect.sizeDelta = new Vector2(90f, 12f);
        rect.localScale = Vector3.one * .009f;
        hearts = new Image[maximumHearts];
        for (int i = 0; i < hearts.Length; i++)
            hearts[i] = Bar("Health", new Vector2((i - (hearts.Length - 1) * .5f) * 18f, 0f), new Vector2(14f, 7f), new Color(.95f, .2f, .18f));
        var cueObject = new GameObject("Attack cue", typeof(RectTransform), typeof(Canvas));
        cueObject.transform.SetParent(transform.parent, false);
        cueObject.transform.localScale = Vector3.one * .009f;
        attackCue = cueObject.GetComponent<Canvas>();
        attackCue.renderMode = RenderMode.WorldSpace;
        attackCue.sortingOrder = 30;
        attackCue.enabled = false;
        var spark = new GameObject("Spark", typeof(RectTransform), typeof(Image));
        spark.transform.SetParent(cueObject.transform, false);
        cueCore = spark.GetComponent<Image>();
        cueCore.rectTransform.sizeDelta = new Vector2(32f, 32f);
        cueCore.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        cueCore.raycastTarget = false;
        cueCenter = CueImage(cueObject.transform, "Bright center", Vector2.zero, new Vector2(13f, 13f), Color.white);
        cueCenter.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        var activationObject = new GameObject("Active opponent", typeof(RectTransform), typeof(Canvas));
        activationObject.transform.SetParent(transform.parent, false);
        activationObject.transform.localScale = Vector3.one * .009f;
        activationCue = activationObject.GetComponent<Canvas>();
        activationCue.renderMode = RenderMode.WorldSpace;
        activationCue.sortingOrder = 30;
        activationCue.enabled = false;
        Color gold = new Color(1f, .78f, .14f);
        for (int i = 0; i < 2; i++)
        {
            Image arrow = CueImage(activationObject.transform, "Arrow", new Vector2(i == 0 ? -5f : 5f, 0f), new Vector2(14f, 4f), gold);
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -45f : 45f);
        }
        activationRing = new Image[12];
        for (int i = 0; i < activationRing.Length; i++)
        {
            float angle = i * Mathf.PI * 2f / activationRing.Length;
            Image segment = CueImage(activationObject.transform, "Activation ring", new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 24f,
                new Vector2(10f, 3f), gold);
            segment.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -i * 360f / activationRing.Length);
            activationRing[i] = segment;
        }
        activationAudio = gameObject.AddComponent<AudioSource>();
        activationAudio.playOnAwake = false;
        activationAudio.spatialBlend = 0f;
        activationAudio.volume = .35f;
        if (activationSound == null) activationSound = CreateActivationSound();
        if (attackSound == null) attackSound = CreateAttackSound();
    }

    public void Activate(double now)
    {
        activatedAt = now;
        activationAudio.PlayOneShot(activationSound);
    }

    private static AudioClip CreateActivationSound()
    {
        const int rate = 24000;
        var samples = new float[(int)(rate * .24f)];
        for (int i = 0; i < samples.Length; i++)
        {
            float time = (float)i / rate;
            float noteTime = time < .12f ? time : time - .12f;
            float frequency = time < .12f ? 523.25f : 783.99f;
            float envelope = Mathf.Sin(Mathf.Clamp01(noteTime / .12f) * Mathf.PI);
            samples[i] = Mathf.Sin(time * frequency * Mathf.PI * 2f) * envelope * .22f;
        }
        AudioClip clip = AudioClip.Create("Opponent activation", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip CreateAttackSound()
    {
        const int rate = 24000;
        var samples = new float[(int)(rate * .14f)];
        for (int i = 0; i < samples.Length; i++)
        {
            float time = (float)i / rate;
            float phase = 2f * Mathf.PI * (660f * time + 1200f * time * time);
            samples[i] = Mathf.Sin(phase) * Mathf.Sin(time / .14f * Mathf.PI) * .24f;
        }
        AudioClip clip = AudioClip.Create("Enemy preparing cut", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static Image CueImage(Transform parent, string label, Vector2 position, Vector2 size, Color color)
    {
        var node = new GameObject(label, typeof(RectTransform), typeof(Image));
        node.transform.SetParent(parent, false);
        Image result = node.GetComponent<Image>();
        result.rectTransform.sizeDelta = size;
        result.rectTransform.anchoredPosition = position;
        result.color = color;
        result.raycastTarget = false;
        return result;
    }

    private Image Bar(string label, Vector2 position, Vector2 size, Color color)
    {
        var node = new GameObject(label, typeof(RectTransform), typeof(Image));
        node.transform.SetParent(transform, false);
        Image result = node.GetComponent<Image>();
        result.rectTransform.sizeDelta = size;
        result.rectTransform.anchoredPosition = position;
        result.color = color;
        result.raycastTarget = false;
        return result;
    }

    public void Refresh(int remaining, float cueProgress, bool visible, Vector3 cuePosition, double now)
    {
        canvas.enabled = visible;
        for (int i = 0; i < hearts.Length; i++)
            hearts[i].color = i < remaining ? new Color(.95f, .2f, .18f) : new Color(.2f, .2f, .2f, .7f);
        attackCue.enabled = visible && cueProgress >= 0f;
        if (attackCue.enabled && !attackAnnounced) activationAudio.PlayOneShot(attackSound, .8f);
        attackAnnounced = attackCue.enabled;
        attackCue.transform.position = cuePosition;
        float progress = Mathf.Clamp01(cueProgress);
        float pulse = .85f + .15f * Mathf.Sin(progress * Mathf.PI * 6f);
        cueCore.color = new Color(1f, .65f + progress * .25f, .08f, pulse);
        cueCore.rectTransform.localScale = Vector3.one * (1f + progress * 1.2f);
        cueCenter.color = new Color(1f, 1f, .8f, pulse);
        cueCenter.rectTransform.localScale = Vector3.one * (1f + progress * 1.7f);
        activationCue.enabled = visible;
        activationCue.transform.position = transform.position + Vector3.up * .35f;
        float age = (float)(now - activatedAt);
        float fade = 1f - Mathf.Clamp01(age / .7f);
        for (int i = 0; i < activationRing.Length; i++)
        {
            activationRing[i].color = new Color(1f, .78f, .14f, fade);
            activationRing[i].rectTransform.localScale = Vector3.one * (1f + (1f - fade) * .5f);
        }
        Camera view = Camera.main;
        if (view == null)
            foreach (Camera camera in Camera.allCameras)
                if (camera.enabled && camera.gameObject.activeInHierarchy) { view = camera; break; }
        if (view != null) FaceCamera(view);
    }

    private void FaceCamera(Camera view)
    {
        if (canvas == null || view == null) return;
        transform.rotation = view.transform.rotation;
        attackCue.transform.rotation = view.transform.rotation;
        activationCue.transform.rotation = view.transform.rotation;
    }

    private void BeforeCameraRendering(ScriptableRenderContext context, Camera view) => FaceCamera(view);
    private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
    private void OnDisable() => RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering;
}
