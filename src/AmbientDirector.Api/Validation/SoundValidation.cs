using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;

namespace AmbientDirector.Api.Validation;

/// <summary>Guards sound metadata before it reaches the store; failures map to HTTP 400.</summary>
public static class SoundValidation
{
    private const int MaxNameLength = 80;
    private const int MaxCategoryLength = 40;

    public static void Validate(Sound sound)
    {
        if (string.IsNullOrWhiteSpace(sound.Name))
            throw new ValidationException("error.common.nameRequired");
        if (sound.Name.Length > MaxNameLength)
            throw new ValidationException("error.sound.nameLength", MaxNameLength);
        if (sound.Category.Length > MaxCategoryLength)
            throw new ValidationException("error.sound.categoryLength", MaxCategoryLength);
        if (sound.Volume is < 0.0 or > 1.0)
            throw new ValidationException("error.sound.volumeRange");
        if (sound.Image is not null && !ImageFileStorage.IsValidName(sound.Image))
            throw new ValidationException("error.common.invalidImage");
    }
}
