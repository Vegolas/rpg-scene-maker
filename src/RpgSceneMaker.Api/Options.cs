namespace RpgSceneMaker.Api;

public class KenkuOptions
{
    public const string Section = "Kenku";

    /// <summary>Base URL of the Kenku FM remote (enable it in Kenku FM &gt; Settings &gt; Remote).</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:3333";
}
