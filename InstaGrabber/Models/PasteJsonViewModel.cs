namespace InstaGrabber.Models;

/// <summary>Backs the paste form on <c>/Grabber</c>.</summary>
public class PasteJsonViewModel
{
    /// <summary>The raw response body pasted by the user.</summary>
    public string? Json { get; set; }

    /// <summary>Friendly, user-facing failure text. Null when nothing has gone wrong.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Parser detail (JSON path and line) shown under "show details".</summary>
    public string? ErrorDetail { get; set; }
}
