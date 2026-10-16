public sealed partial class DuelPresentation
{
    public void SetTutorialHud(bool active)
    {
        if (matchHud == null) return;
        singleProgress.gameObject.SetActive(!active && singleMode);
        singleHearts.gameObject.SetActive(!active && singleMode);
        progressFill.gameObject.SetActive(!active && singleMode);
        clockLabel.gameObject.SetActive(!active);
        phaseLabel.gameObject.SetActive(!active);
        phaseDetail.gameObject.SetActive(false);
        if (active) SetBanner("", "", 0f);
    }
}
