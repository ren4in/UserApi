using System.ComponentModel.DataAnnotations;

namespace UserApi.Models;

public class UserDeleteRequest
{
    [Required]
    public bool SoftDelete { get; set; }
}
