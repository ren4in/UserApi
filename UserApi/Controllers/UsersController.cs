using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UserApi.Models;
using UserApi.Services;

namespace UserApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    private User? GetCurrentUser() => _userService.GetByLogin(User.Identity?.Name!);
    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpPost("create")]
    [Authorize(Roles = "Admin")]
    public IActionResult Create([FromBody] UserCreateRequest request)
    {
        if (request == null)
            return BadRequest("Запрос не содержит данных. Проверьте тело запроса.");

        var existing = _userService.GetByLogin(request.Login);
        if (existing != null)
            return Conflict($"Пользователь с логином '{request.Login}' уже существует.");

        var admin = GetCurrentUser();
        if (admin == null)
            return Unauthorized("Токен недействителен или не найден. Повторите авторизацию.");

        var user = new User
        {
            Login = request.Login,
            Password = request.Password,
            Name = request.Name,
            Gender = request.Gender,
            Birthday = request.Birthday,
            Admin = request.Admin,
            CreatedBy = admin.Login
        };

        _userService.Add(user);

        return Ok(new
        {
            message = "Пользователь успешно создан.",
            createdUser = new
            {
                user.Login,
                user.Name,
                user.Gender,
                user.Birthday,
                user.Admin
            }
        });
    }
    [HttpPut("update-1/info")]
    public IActionResult UpdateInfo([FromBody] UserUpdateInfoRequest request)
    {
        var sender = GetCurrentUser();
        if (sender == null)
            return Unauthorized("Вы не авторизованы или токен недействителен.");

        var target = _userService.GetByLogin(request.TargetLogin);
        if (target == null)
            return NotFound($"Пользователь с логином '{request.TargetLogin}' не найден.");

        var isSelf = sender.Login == target.Login;

        if (!sender.Admin && (!isSelf || target.RevokedOn != null))
        {
            if (target.RevokedOn != null)
                return Forbid("Невозможно изменить данные удалённого пользователя.");
            else
                return Forbid("Вы можете изменять только свои данные.");
        }

        target.Name = request.Name;
        target.Gender = request.Gender;
        target.Birthday = request.Birthday;
        target.ModifiedOn = DateTime.UtcNow;
        target.ModifiedBy = sender.Login;

        return Ok(new
        {
            message = "Информация успешно обновлена.",
            updated = new
            {
                target.Login,
                target.Name,
                target.Gender,
                target.Birthday
            }
        });
    }

    [HttpPut("update-1/password")]
    public IActionResult ChangePassword([FromBody] UserChangePasswordRequest request)
    {
        var sender = GetCurrentUser();
        if (sender == null)
            return Unauthorized("Вы не вошли в систему или токен недействителен.");

        var target = _userService.GetByLogin(request.TargetLogin);
        if (target == null)
            return NotFound($"Пользователь с логином '{request.TargetLogin}' не найден.");

        var isSelf = sender.Login == target.Login;
        if (!sender.Admin && (!isSelf || target.RevokedOn != null))
        {
            if (target.RevokedOn != null)
                return Forbid("Невозможно изменить пароль удалённого пользователя.");
            else
                return Forbid("У вас нет прав на изменение пароля другого пользователя.");
        }

        target.Password = request.NewPassword;
        target.ModifiedOn = DateTime.UtcNow;
        target.ModifiedBy = sender.Login;

        return Ok("Пароль успешно изменён.");
    }

    [HttpPut("update-1/login")]
    public IActionResult ChangeLogin([FromBody] UserChangeLoginRequest request)
    {
        if (request == null)
            return BadRequest("Запрос не содержит данных. Проверьте тело запроса.");

        if (string.IsNullOrWhiteSpace(request.NewLogin))
            return BadRequest("Новый логин не может быть пустым.");

        var sender = GetCurrentUser();
        if (sender == null)
            return Unauthorized("Вы не авторизованы или токен недействителен.");

        var target = _userService.GetByLogin(request.TargetLogin);
        if (target == null)
            return NotFound($"Пользователь с логином '{request.TargetLogin}' не найден.");

        if (_userService.GetByLogin(request.NewLogin) != null)
            return Conflict($"Логин '{request.NewLogin}' уже занят другим пользователем.");

        var isSelf = sender.Login == target.Login;
        if (!sender.Admin && (!isSelf || target.RevokedOn != null))
        {
            if (target.RevokedOn != null)
                return Forbid("Нельзя изменить логин удалённого пользователя.");
            else
                return Forbid("Вы можете изменять логин только для себя.");
        }

        target.Login = request.NewLogin;
        target.ModifiedOn = DateTime.UtcNow;
        target.ModifiedBy = sender.Login;

        return Ok(new
        {
            message = "Логин успешно изменён.",
            updatedUser = new
            {
                oldLogin = request.TargetLogin,
                newLogin = request.NewLogin
            }
        });
    }


    [HttpGet("read/active")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetActiveUsers()
    {
        var users = _userService.GetAll()
            .Where(u => u.RevokedOn == null)
            .OrderBy(u => u.CreatedOn)
            .Select(u => new
            {
                u.Login,
                u.Name,
                u.Gender,
                u.Birthday,
                u.CreatedOn
            })
            .ToList();

        if (users.Count == 0)
            return Ok(new { message = "Нет активных пользователей." });

        return Ok(new
        {
            message = $"Найдено активных пользователей: {users.Count}",
            users
        });
    }

    [HttpGet("read/by-login/{login}")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetUserByLogin(string login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest("Логин не указан.");

        var user = _userService.GetByLogin(login);
        if (user == null)
            return NotFound($"Пользователь с логином '{login}' не найден.");

        return Ok(new
        {
            user.Name,
            user.Gender,
            user.Birthday,
            IsActive = user.RevokedOn == null
        });
    }

    [HttpGet("read/self")]
    public IActionResult GetSelf()
    {
        var user = GetCurrentUser();
        if (user == null)
            return Unauthorized("Вы не авторизованы или токен недействителен.");

        if (user.RevokedOn != null)
            return Forbid("Вы были удалены и не можете получить доступ к данным.");

        return Ok(new
        {
            user.Login,
            user.Name,
            user.Gender,
            user.Birthday,
            IsActive = true
        });
    }


    [HttpGet("read/older-than/{age}")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetUsersOlderThan(int age)
    {
        if (age < 0 || age > 150)
            return BadRequest("Возраст должен быть в пределах от 0 до 150.");

        var today = DateTime.UtcNow.Date;

        var users = _userService.GetAll()
            .Where(u => u.Birthday != null && u.RevokedOn == null)
            .Where(u =>
            {
                var birthDate = u.Birthday!.Value;
                var calculatedAge = today.Year - birthDate.Year;
                if (birthDate > today.AddYears(-calculatedAge)) calculatedAge--;
                return calculatedAge > age;
            })
            .Select(u => new
            {
                u.Login,
                u.Name,
                u.Birthday
            })
            .ToList();

        if (users.Count == 0)
            return Ok(new { message = $"Нет пользователей старше {age} лет." });

        return Ok(new
        {
            message = $"Найдено пользователей старше {age} лет: {users.Count}",
            users
        });
    }

    [HttpDelete("delete/{login}")]
    [Authorize(Roles = "Admin")]
    public IActionResult DeleteUser(string login, [FromBody] UserDeleteRequest request)
    {
        var sender = GetCurrentUser();
        if (sender == null)
            return Unauthorized("Вы не авторизованы.");

        if (string.IsNullOrWhiteSpace(login))
            return BadRequest("Логин пользователя не указан.");

        var target = _userService.GetByLogin(login);
        if (target == null)
            return NotFound($"Пользователь с логином '{login}' не найден.");

        if (target.Login == sender.Login)
            return Forbid("Вы не можете удалить самого себя.");

        if (request.SoftDelete)
        {
            if (target.RevokedOn != null)
                return Conflict($"Пользователь '{login}' уже был удалён ранее.");

            target.RevokedOn = DateTime.UtcNow;
            target.RevokedBy = sender.Login;
            target.ModifiedOn = DateTime.UtcNow;
            target.ModifiedBy = sender.Login;

            return Ok(new { message = $"Пользователь '{login}' мягко удалён." });
        }
        else
        {
            _userService.Remove(target);
            return Ok(new { message = $"Пользователь '{login}' полностью удалён." });
        }
    }

    [HttpPut("restore/{login}")]
    [Authorize(Roles = "Admin")]
    public IActionResult RestoreUser(string login)
    {
        var admin = GetCurrentUser();
        if (admin == null)
            return Unauthorized("Вы не авторизованы.");

        if (string.IsNullOrWhiteSpace(login))
            return BadRequest("Логин пользователя не указан.");

        var user = _userService.GetByLogin(login);
        if (user == null)
            return NotFound($"Пользователь с логином '{login}' не найден.");

        if (user.RevokedOn == null)
            return Conflict($"Пользователь '{login}' уже активен.");

        user.RevokedOn = null;
        user.RevokedBy = null;
        user.ModifiedOn = DateTime.UtcNow;
        user.ModifiedBy = admin.Login;

        return Ok(new { message = $"Пользователь '{login}' успешно восстановлен." });
    }
}