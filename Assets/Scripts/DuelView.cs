using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Materials are swapped only for the camera whose player owns this body.
// Restore at camera end so the other viewport and scene cameras see an opaque opponent.
public sealed class DuelView
{
    private readonly Camera camera;
    private readonly Transform player, opponent;
    private Transform focusTarget;
    private bool smoothFocus, focusInitialized;
    private double lastTick = -1;
    private Vector3 focusPosition, focusVelocity, cameraPosition, cameraVelocity, previousPlayerPosition;
    private Quaternion focusRotation;
    private readonly Vector3 basePosition;
    private readonly Quaternion baseRotation;
    private readonly float baseFov;
    private double shakeStart = -100;
    private float strength;
    private float bodyRoll, bodyPitch;
    public const float MaximumReactionRollDegrees = 7f;
    public const float MaximumReactionPitchDegrees = 4f;
    private readonly Renderer[] body;
    private readonly Material[][] opaque, transparent;
    private readonly List<Material> created = new List<Material>();
    private readonly MaterialPropertyBlock[] saved;
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    private bool swapped;
    private bool ownBodyFade = true, walkingView;
    private float cinematic;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public DuelView(Transform player, Transform opponent, Renderer[] renderers)
    {
        this.player = player; this.opponent = opponent; focusTarget = opponent;
        camera = player.GetComponentInChildren<Camera>(); body = renderers;
        if (camera != null)
        {
            if (camera.GetComponent<AudioListener>() == null)
                camera.gameObject.AddComponent<AudioListener>();
            camera.transform.localPosition = new Vector3(0.35f, 1.6f, -3.4f);
            camera.transform.localRotation = Quaternion.LookRotation(new Vector3(-0.35f, -0.6f, 6.4f));
            camera.nearClipPlane = 0.1f;
            basePosition = camera.transform.localPosition; baseRotation = camera.transform.localRotation; baseFov = camera.fieldOfView;
        }
        opaque = new Material[body.Length][]; transparent = new Material[body.Length][]; saved = new MaterialPropertyBlock[body.Length];
        var cache = new Dictionary<Material, Material>();
        Material template = Resources.Load<Material>("OwnBodyFade");
        for (int i = 0; i < body.Length; i++)
        {
            opaque[i] = body[i].sharedMaterials; transparent[i] = new Material[opaque[i].Length]; saved[i] = new MaterialPropertyBlock();
            for (int j = 0; j < opaque[i].Length; j++)
            {
                Material original = opaque[i][j];
                if (!cache.TryGetValue(original, out Material fade))
                {
                    fade = new Material(template != null ? template : original) { name = original.name + " (own view)" };
                    fade.CopyPropertiesFromMaterial(original);
                    ConfigureFade(fade);
                    Color c = original.color; c.a = 0.3f; fade.color = c;
                    cache.Add(original, fade); created.Add(fade);
                }
                transparent[i][j] = fade;
            }
        }
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    public static void ConfigureFade(Material material)
    {
        material.renderQueue = 3000;
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetShaderPassEnabled("ShadowCaster", true);
    }

    private void BeginCamera(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering != camera || swapped || cinematic > .1f || !ownBodyFade) return;
        swapped = true;
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == null) continue;
            body[i].GetPropertyBlock(saved[i]); body[i].sharedMaterials = transparent[i];
            body[i].GetPropertyBlock(block);
            // With no flash, keep each material's original color (e.g. face and uniform).
            if (block.HasColor(BaseColor)) { Color c = block.GetColor(BaseColor); c.a = 0.3f; block.SetColor(BaseColor, c); }
            body[i].SetPropertyBlock(block);
        }
    }
    private void EndCamera(ScriptableRenderContext context, Camera rendering) { if (rendering == camera) Restore(); }
    private void Restore()
    {
        if (!swapped) return;
        for (int i = 0; i < body.Length; i++)
            if (body[i] != null) { body[i].sharedMaterials = opaque[i]; body[i].SetPropertyBlock(saved[i]); }
        swapped = false;
    }
    public void Kick(double now, float amount) { strength = Mathf.Min(1f, amount + (now - shakeStart < 0.12 ? strength * 0.3f : 0f)); shakeStart = now; }
    public void CopyReactionFrom(DuelView other, double now)
    {
        shakeStart = other.shakeStart; strength = other.strength;
        bodyRoll = other.bodyRoll; bodyPitch = other.bodyPitch; Tick(now);
    }
    public void SetBodyRoll(float degrees)
        => bodyRoll = Mathf.Clamp(degrees * .18f, -MaximumReactionRollDegrees, MaximumReactionRollDegrees);
    public void SetBodyPitch(float degrees)
        => bodyPitch = Mathf.Clamp(degrees * .1f, -MaximumReactionPitchDegrees, MaximumReactionPitchDegrees);
    public void SetLayout(bool visible, Rect viewport, bool listen)
    {
        if (camera == null) return;
        camera.enabled = visible;
        camera.rect = viewport;
        AudioListener listener = camera.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = visible && listen;
    }
    public void Tick(double now)
    {
        if (camera == null) return;
        float t = Mathf.Clamp01((float)(now - shakeStart) / 0.12f);
        float decay = (1f - t) * (1f - t) * strength;
        float oscillation = Mathf.Sin(t * Mathf.PI * 5f);
        Vector3 shake = new Vector3(oscillation, Mathf.Sin(t * Mathf.PI * 7f) * 0.65f, 0f) * (0.045f * decay);
        // Track the opponent while the body settles into its new facing angle.
        // The camera's position orbits with the player, so each exchange reveals
        // another part of the stage without losing the opponent from the frame.
        Transform targetTransform = focusTarget != null ? focusTarget : opponent;
        Quaternion shakeRotation = Quaternion.Euler(oscillation * decay * 0.5f, 0f, oscillation * decay * 1.1f)
            * Quaternion.Euler(bodyPitch, 0f, bodyRoll);
        if (smoothFocus)
        {
            Vector3 desiredPosition = player.TransformPoint(walkingView ? new Vector3(.6f, 2f, -6f) : basePosition);
            Vector3 desiredFocus = walkingView ? player.position + Vector3.up * .3f : targetTransform.position + Vector3.up;
            if (!focusInitialized)
            {
                cameraPosition = desiredPosition; focusPosition = desiredFocus;
                previousPlayerPosition = player.position;
                focusRotation = Quaternion.LookRotation(focusPosition - cameraPosition);
                focusVelocity = cameraVelocity = Vector3.zero; focusInitialized = true;
            }
            float dt = lastTick < 0 ? 0f : Mathf.Clamp((float)(now - lastTick), 0f, .1f);
            if (dt > 0f)
            {
                // Recoil moves both fighters together. Carry the camera and
                // its remembered focus with the player before smoothing the
                // relative orbit/target change, so repeated pushes cannot
                // leave the viewpoint metres behind the combatants.
                Vector3 translation = player.position - previousPlayerPosition;
                cameraPosition += translation;
                focusPosition += translation;
                focusPosition = Vector3.SmoothDamp(focusPosition, desiredFocus, ref focusVelocity, .12f, Mathf.Infinity, dt);
                cameraPosition = Vector3.SmoothDamp(cameraPosition, desiredPosition, ref cameraVelocity, .16f, Mathf.Infinity, dt);
                Quaternion desiredRotation = Quaternion.LookRotation(focusPosition - cameraPosition);
                focusRotation = Quaternion.Slerp(focusRotation, desiredRotation, 1f - Mathf.Exp(-dt / .1f));
            }
            previousPlayerPosition = player.position;
            camera.transform.SetPositionAndRotation(cameraPosition + player.rotation * shake, focusRotation * shakeRotation);
        }
        else
        {
            camera.transform.localPosition = basePosition + shake;
            Vector3 target = player.InverseTransformPoint(targetTransform.position + Vector3.up);
            camera.transform.localRotation = Quaternion.LookRotation(target - basePosition) * shakeRotation;
        }
        lastTick = now;
        camera.fieldOfView = baseFov + decay * 4f;
        if (cinematic > 0)
        {
            Vector3 portrait = new Vector3(3.2f, 1.8f, 4.8f);
            camera.transform.localPosition = Vector3.Lerp(camera.transform.localPosition, portrait, cinematic);
            Quaternion portraitAim = Quaternion.LookRotation(new Vector3(0, .45f, 0) - portrait);
            camera.transform.localRotation = Quaternion.Slerp(camera.transform.localRotation, portraitAim, cinematic);
        }
    }
    public void SetWalkingView(bool enabled) => walkingView = enabled;
    public void SetCinematic(float amount) { cinematic = Mathf.Clamp01(amount); }
    public void SetOwnBodyFade(bool enabled) { ownBodyFade = enabled; if(!enabled)Restore(); }
    public void SetFocusTarget(Transform target)
    {
        bool smoothing = target != null;
        if (smoothFocus != smoothing) focusInitialized = false;
        smoothFocus = smoothing;
        focusTarget = target != null ? target : opponent;
    }
    public void Reset() { walkingView = false; shakeStart = -100; strength = bodyRoll = bodyPitch = 0f; focusInitialized = false; lastTick = -1; Tick(0); Restore(); }
    public void Dispose()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera; RenderPipelineManager.endCameraRendering -= EndCamera;
        Restore();
        if (camera != null)
        { camera.transform.SetLocalPositionAndRotation(basePosition, baseRotation); camera.fieldOfView = baseFov; }
        foreach (Material material in created) Object.Destroy(material);
    }
}
