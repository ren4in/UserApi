using System.ComponentModel.DataAnnotations;

namespace UserApi.Models;

public class UserCreateRequest
{ 
    [Required]
    [RegularExpression("^[a-zA-Z0-9]+$")]
    public string Login { get; set; }

    [Required]
    [RegularExpression("^[a-zA-Z0-9]+$")]
    public string Password { get; set; }

    [Required]
    [RegularExpression("^[a-zA-Zа-яА-Я]+$")]
    public string Name { get; set; }

    [Range(0, 2)]
    public int Gender  {get; set; }

public DateTime? Birthday { get; set; }

public bool Admin { get; set; }
}

