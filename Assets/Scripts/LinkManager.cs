using UnityEngine;

public sealed class LinkManager : MonoBehaviour
{
    const string LinkedInProfileUrl = "https://www.linkedin.com/in/bünyaminaslan/";

    public void OpenLinkedInProfile()
    {
        Application.OpenURL(LinkedInProfileUrl);
    }
}
