using System.ComponentModel.DataAnnotations;

namespace UserApi.Models;

public class UserUpdateInfoRequest
{
    [Required]
    public string TargetLogin { get; set; }  // кого изменяем

    [Required]
    [RegularExpression("^[a-zA-Zа-яА-Я]+$")]
    public string Name { get; set; }

    [Range(0, 2)]
    public int Gender { get; set; }

    public DateTime? Birthday { get; set; }
}
