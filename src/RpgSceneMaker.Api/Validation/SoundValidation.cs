using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Guards sound metadata before it reaches the store; failures map to HTTP 400.</summary>
public static class SoundValidation
{
    public static void Validate(Sound sound)
    {
        if (string.IsNullOrWhiteSpace(sound.Name))
            throw new ArgumentException("Sound name is required.");
        if (sound.Name.Length > 80)
            throw new ArgumentException("Sound name must be 80 characters or fewer.");
        if (sound.Category.Length > 40)
            throw new ArgumentException("Sound category must be 40 characters or fewer.");
        if (sound.Volume is < 0.0 or > 1.0)
            throw new ArgumentException("Sound volume must be between 0.0 and 1.0.");
        if (sound.Image is not null && !ImageFileStorage.IsValidName(sound.Image))
            throw new ArgumentException("Invalid image reference.");
    }
}
