using System.ComponentModel.DataAnnotations;

namespace UserApi.Models;

public class UserChangePasswordRequest
{
    [Required]
    public string TargetLogin { get; set; }

    [Required]
    [RegularExpression("^[a-zA-Z0-9]+$")]
    public string NewPassword { get; set; }
}
