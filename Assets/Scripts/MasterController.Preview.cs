using System.Collections.Generic;
using SchoolFestival.Combat;
using UnityEngine;

public sealed partial class MasterController
{
    public bool IsCharacterPreview => PreviewPlayer >= 0;
    public int PreviewPlayer { get; private set; } = -1;
    public Camera CharacterPreviewCamera { get; private set; }
    private double SwordPoseTime => IsCharacterPreview ? Time.unscaledTimeAsDouble : matchTime;
    private readonly Dictionary<Camera, bool> previewCameras = new Dictionary<Camera, bool>();
    private readonly Dictionary<AudioListener, bool> previewListeners = new Dictionary<AudioListener, bool>();
    private readonly Dictionary<Renderer, bool> previewRenderers = new Dictionary<Renderer, bool>();
    private float previewPitch, previewRoll;
    private Vector3 previewGuardDirection;
    private int previewDisplayMode, previewViewedPlayer;

    public void BeginCharacterPreview(int playerIndex)
    {
        if (!IsPaused || first == null || second == null || playerIndex < 0 || playerIndex > 1) return;
        EndCharacterPreview();
        PreviewPlayer = playerIndex;
        PlayerSword player = playerIndex == 0 ? first : second;
        PlayerSword other = playerIndex == 0 ? second : first;
        previewPitch = player.Pitch; previewRoll = player.Roll;
        previewGuardDirection = player.LastGuardDirection;
        previewDisplayMode = DisplayMode; previewViewedPlayer = ViewedPlayer;
        player.ThrustAt = other.ThrustAt = -100d;
        player.CombatBaseline = other.CombatBaseline = false;
        player.DirectionGeneration = -1;
        presentation.SetMenuPresentation(true);
        presentation.ClearMotionHistory();

        Camera template = player.Player.GetComponentInChildren<Camera>();
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (camera.targetTexture != null || !camera.gameObject.scene.IsValid() || !camera.gameObject.activeInHierarchy) continue;
            previewCameras.Add(camera, camera.enabled); camera.enabled = false;
        }
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            previewListeners.Add(listener, listener.enabled); listener.enabled = false;
        }
        HidePreviewRenderers(other.Player.gameObject);
        HidePreviewRenderers(other.Sword.gameObject);
        foreach (SingleNpc npc in singleNpcs) if (npc.Root != null) HidePreviewRenderers(npc.Root);

        var cameraObject = new GameObject("Character field preview", typeof(Camera), typeof(AudioListener));
        cameraObject.transform.SetParent(transform, false);
        CharacterPreviewCamera = cameraObject.GetComponent<Camera>();
        if (template != null) CharacterPreviewCamera.CopyFrom(template);
        CharacterPreviewCamera.targetTexture = null;
        CharacterPreviewCamera.rect = new Rect(0f, 0f, 1f, 1f);
        CharacterPreviewCamera.fieldOfView = 45f;
        CharacterPreviewCamera.orthographic = false;
        CharacterPreviewCamera.nearClipPlane = .1f;
        CharacterPreviewCamera.depth = 100f;
        CharacterPreviewCamera.enabled = true;
        PositionPreviewCamera(player);
    }

    private void HidePreviewRenderers(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!previewRenderers.ContainsKey(renderer)) previewRenderers.Add(renderer, renderer.forceRenderingOff);
            renderer.forceRenderingOff = true;
        }
    }

    private void TickCharacterPreview()
    {
        PlayerSword player = PreviewPlayer == 0 ? first : second;
        PlayerSword other = PreviewPlayer == 0 ? second : first;
        // Keep the other input source fresh while showing just the chosen fighter.
        CombatInput input = ReadInput(player, PreviewPlayer == 0);
        ReadInput(other, PreviewPlayer != 0, false);
        bool guard = input.Valid && input.GuardHeld;
        presentation.PoseCharacterPreview(PreviewPlayer, SwordPoseTime, Time.unscaledDeltaTime,
            player.TargetRotation, guard, input.Valid ? input.AngularSpeed : 0f);
        UpdatePose(player, guard);
        ApplyPose(player, player.TargetPose);
        // Changing the layout in Esc must not replace the preview camera.
        foreach (Camera camera in previewCameras.Keys) if (camera != null) camera.enabled = false;
        foreach (AudioListener listener in previewListeners.Keys) if (listener != null) listener.enabled = false;
        PositionPreviewCamera(player);
    }

    private void PositionPreviewCamera(PlayerSword player)
    {
        if (CharacterPreviewCamera == null) return;
        Vector3 center = player.Player.position + Vector3.up * .35f;
        CharacterPreviewCamera.transform.position = player.Player.position + player.Player.forward * 7.5f + Vector3.up * 1.4f;
        CharacterPreviewCamera.transform.rotation = Quaternion.LookRotation(center - CharacterPreviewCamera.transform.position);
    }

    public void EndCharacterPreview()
    {
        if (!IsCharacterPreview) return;
        PlayerSword player = PreviewPlayer == 0 ? first : second;
        PreviewPlayer = -1;
        foreach (var pair in previewRenderers) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
        foreach (var pair in previewCameras) if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in previewListeners) if (pair.Key != null) pair.Key.enabled = pair.Value;
        previewRenderers.Clear(); previewCameras.Clear(); previewListeners.Clear();
        if (CharacterPreviewCamera != null)
        {
            CharacterPreviewCamera.gameObject.SetActive(false);
            Destroy(CharacterPreviewCamera.gameObject);
            CharacterPreviewCamera = null;
        }
        if (player != null)
        {
            player.Pitch = previewPitch; player.Roll = previewRoll;
            player.LastGuardDirection = previewGuardDirection;
            first.ThrustAt = second.ThrustAt = -100d;
            first.CombatBaseline = second.CombatBaseline = false;
            first.DirectionGeneration = second.DirectionGeneration = -1;
        }
        if (presentation != null)
        {
            presentation.ClearMotionHistory();
            if (previewDisplayMode != DisplayMode || previewViewedPlayer != ViewedPlayer)
                presentation.SetLayout(IsSingleMode ? 2 : DisplayMode, IsSingleMode ? 0 : ViewedPlayer);
        }
    }
}
