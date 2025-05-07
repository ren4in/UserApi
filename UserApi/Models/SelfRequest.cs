using System.ComponentModel.DataAnnotations;

namespace UserApi.Models;

public class SelfRequest
{
    [Required]
    public string Login { get; set; }

    [Required]
    public string Password { get; set; }
}
